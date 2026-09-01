#include "SessionManifest.h"

#include "NarrowReconciler.h"

#include "PersistentFileIdentity.h"
#include "RecordingOutputRoot.h"
#include "RecordingSessionIdentity.h"

#include <windows.h>
#include <objbase.h>

#include <algorithm>
#include <atomic>
#include <cerrno>
#include <cmath>
#include <cwchar>
#include <filesystem>
#include <limits>
#include <map>
#include <new>
#include <stdexcept>
#include <string_view>
#include <system_error>
#include <utility>
#include <vector>

namespace xbpreview
{
    SessionManifestParseStatus ClassifySessionManifestReadFailure(
        const HRESULT result) noexcept
    {
        if (result == E_OUTOFMEMORY ||
            HRESULT_FACILITY(result) != FACILITY_WIN32)
        {
            return SessionManifestParseStatus::IoFailure;
        }
        switch (HRESULT_CODE(result))
        {
        case ERROR_FILE_NOT_FOUND:
        case ERROR_PATH_NOT_FOUND:
            return SessionManifestParseStatus::NotFound;
        case ERROR_ACCESS_DENIED:
        case ERROR_SHARING_VIOLATION:
        case ERROR_LOCK_VIOLATION:
        case ERROR_PRIVILEGE_NOT_HELD:
        case ERROR_NETWORK_ACCESS_DENIED:
            return SessionManifestParseStatus::Inaccessible;
        default:
            return SessionManifestParseStatus::IoFailure;
        }
    }

    namespace
    {
        constexpr std::uint64_t MaximumExactJsonInteger =
            (std::uint64_t{ 1 } << 53) - 1;
        constexpr std::uint64_t MaximumManifestBytes = 1024 * 1024;

        class UniqueHandle final
        {
        public:
            UniqueHandle() noexcept = default;
            explicit UniqueHandle(const HANDLE value) noexcept : value_(value) {}
            ~UniqueHandle()
            {
                Reset();
            }

            UniqueHandle(const UniqueHandle&) = delete;
            UniqueHandle& operator=(const UniqueHandle&) = delete;

            [[nodiscard]] HANDLE Get() const noexcept { return value_; }
            [[nodiscard]] bool Valid() const noexcept
            {
                return value_ != nullptr && value_ != INVALID_HANDLE_VALUE;
            }

            void Reset() noexcept
            {
                if (Valid())
                {
                    (void)CloseHandle(value_);
                }
                value_ = INVALID_HANDLE_VALUE;
            }

        private:
            HANDLE value_{ INVALID_HANDLE_VALUE };
        };

        class ManifestWriteLock final
        {
        public:
            explicit ManifestWriteLock(const std::filesystem::path& path)
                : handle_(CreateFileW(
                      path.c_str(),
                      GENERIC_READ | GENERIC_WRITE,
                      0,
                      nullptr,
                      OPEN_ALWAYS,
                      FILE_ATTRIBUTE_HIDDEN | FILE_FLAG_OPEN_REPARSE_POINT,
                      nullptr))
            {
                if (!handle_.Valid())
                {
                    const auto error = GetLastError();
                    hresult_ = HRESULT_FROM_WIN32(error == ERROR_SUCCESS
                        ? ERROR_GEN_FAILURE
                        : error);
                    return;
                }
                FILE_ATTRIBUTE_TAG_INFO attributes{};
                if (!GetFileInformationByHandleEx(
                        handle_.Get(),
                        FileAttributeTagInfo,
                        &attributes,
                        sizeof(attributes)))
                {
                    const auto error = GetLastError();
                    hresult_ = HRESULT_FROM_WIN32(error == ERROR_SUCCESS
                        ? ERROR_GEN_FAILURE
                        : error);
                    handle_.Reset();
                    return;
                }
                if ((attributes.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0 ||
                    (attributes.FileAttributes &
                        FILE_ATTRIBUTE_REPARSE_POINT) != 0)
                {
                    hresult_ = HRESULT_FROM_WIN32(ERROR_REPARSE_TAG_INVALID);
                    handle_.Reset();
                    return;
                }
                hresult_ = S_OK;
            }

            [[nodiscard]] bool Acquired() const noexcept
            {
                return handle_.Valid();
            }

            [[nodiscard]] HRESULT HResult() const noexcept
            {
                return hresult_;
            }

        private:
            UniqueHandle handle_;
            HRESULT hresult_{ E_UNEXPECTED };
        };

        class TemporaryFileCleanup final
        {
        public:
            explicit TemporaryFileCleanup(std::filesystem::path path)
                : path_(std::move(path))
            {
            }

            ~TemporaryFileCleanup()
            {
                (void)DeleteFileW(path_.c_str());
            }

        private:
            std::filesystem::path path_;
        };

        enum class JsonKind
        {
            Null,
            Boolean,
            Integer,
            Number,
            String,
            Object,
            Array
        };

        struct JsonValue final
        {
            JsonKind kind{ JsonKind::Null };
            bool boolean{};
            std::int64_t integer{};
            std::wstring string;
            std::map<std::wstring, JsonValue> object;
            std::vector<JsonValue> array;
        };

        constexpr std::size_t MaximumJsonNestingDepth = 128;

        class JsonNestingLimitError final
        {
        };

        class JsonParser final
        {
        public:
            explicit JsonParser(std::wstring_view text) : text_(text) {}

            JsonValue Parse()
            {
                SkipWhitespace();
                auto value = ParseValue(0);
                SkipWhitespace();
                if (position_ != text_.size())
                {
                    throw std::runtime_error("Trailing JSON data.");
                }
                return value;
            }

        private:
            void SkipWhitespace() noexcept
            {
                while (position_ < text_.size())
                {
                    const auto value = text_[position_];
                    if (value != L' ' && value != L'\t' &&
                        value != L'\r' && value != L'\n')
                    {
                        break;
                    }
                    ++position_;
                }
            }

            bool Consume(const wchar_t value) noexcept
            {
                if (position_ >= text_.size() || text_[position_] != value)
                {
                    return false;
                }
                ++position_;
                return true;
            }

            bool ConsumeLiteral(const std::wstring_view value) noexcept
            {
                if (text_.substr(position_, value.size()) != value)
                {
                    return false;
                }
                position_ += value.size();
                return true;
            }

            JsonValue ParseValue(const std::size_t depth)
            {
                SkipWhitespace();
                if (position_ >= text_.size())
                {
                    throw std::runtime_error("Missing JSON value.");
                }
                if (text_[position_] == L'{')
                {
                    if (depth >= MaximumJsonNestingDepth)
                    {
                        throw JsonNestingLimitError();
                    }
                    return ParseObject(depth + 1);
                }
                if (text_[position_] == L'[')
                {
                    if (depth >= MaximumJsonNestingDepth)
                    {
                        throw JsonNestingLimitError();
                    }
                    return ParseArray(depth + 1);
                }
                if (text_[position_] == L'"')
                {
                    JsonValue result{};
                    result.kind = JsonKind::String;
                    result.string = ParseString();
                    return result;
                }
                if (text_[position_] == L'-' ||
                    (text_[position_] >= L'0' && text_[position_] <= L'9'))
                {
                    return ParseNumber();
                }
                if (ConsumeLiteral(L"true"))
                {
                    JsonValue result{};
                    result.kind = JsonKind::Boolean;
                    result.boolean = true;
                    return result;
                }
                if (ConsumeLiteral(L"false"))
                {
                    JsonValue result{};
                    result.kind = JsonKind::Boolean;
                    return result;
                }
                if (ConsumeLiteral(L"null"))
                {
                    return {};
                }
                throw std::runtime_error("Unsupported JSON value.");
            }

            JsonValue ParseObject(const std::size_t depth)
            {
                if (!Consume(L'{'))
                {
                    throw std::runtime_error("Missing object start.");
                }
                JsonValue result{};
                result.kind = JsonKind::Object;
                SkipWhitespace();
                if (Consume(L'}'))
                {
                    return result;
                }
                for (;;)
                {
                    SkipWhitespace();
                    if (position_ >= text_.size() ||
                        text_[position_] != L'"')
                    {
                        throw std::runtime_error("Missing object key.");
                    }
                    auto key = ParseString();
                    SkipWhitespace();
                    if (!Consume(L':'))
                    {
                        throw std::runtime_error("Missing object separator.");
                    }
                    auto value = ParseValue(depth);
                    if (!result.object.emplace(
                            std::move(key), std::move(value)).second)
                    {
                        throw std::runtime_error("Duplicate object key.");
                    }
                    SkipWhitespace();
                    if (Consume(L'}'))
                    {
                        break;
                    }
                    if (!Consume(L','))
                    {
                        throw std::runtime_error("Missing object comma.");
                    }
                }
                return result;
            }

            JsonValue ParseArray(const std::size_t depth)
            {
                if (!Consume(L'['))
                {
                    throw std::runtime_error("Missing array start.");
                }
                JsonValue result{};
                result.kind = JsonKind::Array;
                SkipWhitespace();
                if (Consume(L']'))
                {
                    return result;
                }
                for (;;)
                {
                    result.array.push_back(ParseValue(depth));
                    SkipWhitespace();
                    if (Consume(L']'))
                    {
                        break;
                    }
                    if (!Consume(L','))
                    {
                        throw std::runtime_error("Missing array comma.");
                    }
                }
                return result;
            }

            static std::uint32_t HexValue(const wchar_t value)
            {
                if (value >= L'0' && value <= L'9') return value - L'0';
                if (value >= L'a' && value <= L'f') return value - L'a' + 10;
                if (value >= L'A' && value <= L'F') return value - L'A' + 10;
                throw std::runtime_error("Invalid JSON Unicode escape.");
            }

            std::uint32_t ParseHex4()
            {
                if (position_ + 4 > text_.size())
                {
                    throw std::runtime_error("Short JSON Unicode escape.");
                }
                std::uint32_t result{};
                for (int index = 0; index < 4; ++index)
                {
                    result = (result << 4) | HexValue(text_[position_++]);
                }
                return result;
            }

            std::wstring ParseString()
            {
                if (!Consume(L'"'))
                {
                    throw std::runtime_error("Missing string start.");
                }
                std::wstring result;
                while (position_ < text_.size())
                {
                    const auto value = text_[position_++];
                    if (value == L'"')
                    {
                        return result;
                    }
                    if (value < 0x20)
                    {
                        throw std::runtime_error("JSON string control character.");
                    }
                    if (value != L'\\')
                    {
                        result.push_back(value);
                        continue;
                    }
                    if (position_ >= text_.size())
                    {
                        throw std::runtime_error("Short JSON escape.");
                    }
                    const auto escaped = text_[position_++];
                    switch (escaped)
                    {
                    case L'"': result.push_back(L'"'); break;
                    case L'\\': result.push_back(L'\\'); break;
                    case L'/': result.push_back(L'/'); break;
                    case L'b': result.push_back(L'\b'); break;
                    case L'f': result.push_back(L'\f'); break;
                    case L'n': result.push_back(L'\n'); break;
                    case L'r': result.push_back(L'\r'); break;
                    case L't': result.push_back(L'\t'); break;
                    case L'u':
                    {
                        const auto first = ParseHex4();
                        if (first >= 0xD800 && first <= 0xDBFF)
                        {
                            if (position_ + 2 > text_.size() ||
                                text_[position_] != L'\\' ||
                                text_[position_ + 1] != L'u')
                            {
                                throw std::runtime_error("Missing low surrogate.");
                            }
                            position_ += 2;
                            const auto second = ParseHex4();
                            if (second < 0xDC00 || second > 0xDFFF)
                            {
                                throw std::runtime_error("Invalid low surrogate.");
                            }
                            result.push_back(static_cast<wchar_t>(first));
                            result.push_back(static_cast<wchar_t>(second));
                        }
                        else if (first >= 0xDC00 && first <= 0xDFFF)
                        {
                            throw std::runtime_error("Unexpected low surrogate.");
                        }
                        else
                        {
                            result.push_back(static_cast<wchar_t>(first));
                        }
                        break;
                    }
                    default:
                        throw std::runtime_error("Invalid JSON escape.");
                    }
                }
                throw std::runtime_error("Unterminated JSON string.");
            }

            JsonValue ParseNumber()
            {
                const auto start = position_;
                if (Consume(L'-') && position_ >= text_.size())
                {
                    throw std::runtime_error("Incomplete JSON number.");
                }
                if (position_ < text_.size() && text_[position_] == L'0')
                {
                    ++position_;
                    if (position_ < text_.size() &&
                        text_[position_] >= L'0' && text_[position_] <= L'9')
                    {
                        throw std::runtime_error("JSON number leading zero.");
                    }
                }
                else
                {
                    const auto digits = position_;
                    while (position_ < text_.size() &&
                        text_[position_] >= L'0' && text_[position_] <= L'9')
                    {
                        ++position_;
                    }
                    if (digits == position_)
                    {
                        throw std::runtime_error("Missing JSON number digits.");
                    }
                }
                bool isInteger = true;
                if (position_ < text_.size() && text_[position_] == L'.')
                {
                    isInteger = false;
                    ++position_;
                    const auto fractionDigits = position_;
                    while (position_ < text_.size() &&
                        text_[position_] >= L'0' && text_[position_] <= L'9')
                    {
                        ++position_;
                    }
                    if (fractionDigits == position_)
                    {
                        throw std::runtime_error("Missing JSON fraction digits.");
                    }
                }
                if (position_ < text_.size() &&
                    (text_[position_] == L'e' || text_[position_] == L'E'))
                {
                    isInteger = false;
                    ++position_;
                    if (position_ < text_.size() &&
                        (text_[position_] == L'+' || text_[position_] == L'-'))
                    {
                        ++position_;
                    }
                    const auto exponentDigits = position_;
                    while (position_ < text_.size() &&
                        text_[position_] >= L'0' && text_[position_] <= L'9')
                    {
                        ++position_;
                    }
                    if (exponentDigits == position_)
                    {
                        throw std::runtime_error("Missing JSON exponent digits.");
                    }
                }
                JsonValue result{};
                result.kind = JsonKind::Number;
                if (!isInteger)
                {
                    return result;
                }
                const auto token = std::wstring(text_.substr(
                    start, position_ - start));
                wchar_t* end{};
                errno = 0;
                const auto number = _wcstoi64(token.c_str(), &end, 10);
                if (errno == ERANGE)
                {
                    return result;
                }
                if (end == token.c_str() || *end != L'\0')
                {
                    throw std::runtime_error("Invalid JSON number.");
                }
                result.kind = JsonKind::Integer;
                result.integer = number;
                return result;
            }

            std::wstring_view text_;
            std::size_t position_{};
        };

        const JsonValue& Required(
            const JsonValue& object,
            const wchar_t* const name,
            const JsonKind kind)
        {
            if (object.kind != JsonKind::Object)
            {
                throw std::runtime_error("JSON value is not an object.");
            }
            const auto found = object.object.find(name);
            if (found == object.object.end() || found->second.kind != kind)
            {
                throw std::runtime_error("Missing or invalid manifest field.");
            }
            return found->second;
        }

        std::optional<HRESULT> OptionalHResult(
            const JsonValue& object,
            const wchar_t* const name)
        {
            const auto found = object.object.find(name);
            if (found == object.object.end())
            {
                throw std::runtime_error("Missing HRESULT field.");
            }
            if (found->second.kind == JsonKind::Null)
            {
                return std::nullopt;
            }
            if (found->second.kind != JsonKind::Integer ||
                found->second.integer < (std::numeric_limits<std::int32_t>::min)() ||
                found->second.integer > (std::numeric_limits<std::int32_t>::max)())
            {
                throw std::runtime_error("HRESULT field out of range.");
            }
            return static_cast<HRESULT>(found->second.integer);
        }

        const wchar_t* StateText(const SessionManifestState state) noexcept
        {
            switch (state)
            {
            case SessionManifestState::Created: return L"Created";
            case SessionManifestState::Starting: return L"Starting";
            case SessionManifestState::Recording: return L"Recording";
            case SessionManifestState::Stopping: return L"Stopping";
            case SessionManifestState::ReadyToPublish: return L"ReadyToPublish";
            case SessionManifestState::Published: return L"Published";
            case SessionManifestState::Completed: return L"Completed";
            case SessionManifestState::Failed: return L"Failed";
            case SessionManifestState::Unknown: return L"Unknown";
            case SessionManifestState::ReconciledCompleted:
                return L"ReconciledCompleted";
            case SessionManifestState::UserCancelled:
                return L"UserCancelled";
            default: return nullptr;
            }
        }

        class UnknownManifestStateError final : public std::runtime_error
        {
        public:
            UnknownManifestStateError()
                : std::runtime_error("Unknown manifest state.")
            {
            }
        };

        SessionManifestState ParseState(const std::wstring& value)
        {
            for (const auto state : {
                SessionManifestState::Created,
                SessionManifestState::Starting,
                SessionManifestState::Recording,
                SessionManifestState::Stopping,
                SessionManifestState::ReadyToPublish,
                SessionManifestState::Published,
                SessionManifestState::Completed,
                SessionManifestState::Failed,
                SessionManifestState::Unknown,
                SessionManifestState::ReconciledCompleted,
                SessionManifestState::UserCancelled })
            {
                if (value == StateText(state)) return state;
            }
            throw UnknownManifestStateError();
        }

        const wchar_t* ReconciliationKindText(
            const SessionManifestReconciliationKind kind) noexcept
        {
            switch (kind)
            {
            case SessionManifestReconciliationKind::
                    FinalAtPlannedPathSamePersistentFileV1:
                return L"FinalAtPlannedPathSamePersistentFileV1";
            default:
                return nullptr;
            }
        }

        SessionManifestReconciliationKind ParseReconciliationKind(
            const std::wstring& value)
        {
            if (value == L"FinalAtPlannedPathSamePersistentFileV1")
            {
                return SessionManifestReconciliationKind::
                    FinalAtPlannedPathSamePersistentFileV1;
            }
            throw std::runtime_error("Unknown reconciliation kind.");
        }

        const wchar_t* ReconciliationEvidenceKindText(
            const SessionManifestReconciliationEvidenceKind kind) noexcept
        {
            switch (kind)
            {
            case SessionManifestReconciliationEvidenceKind::
                    MaintenanceLeaseCasHeldFinalIdentityV1:
                return L"MaintenanceLeaseCasHeldFinalIdentityV1";
            default:
                return nullptr;
            }
        }

        SessionManifestReconciliationEvidenceKind
            ParseReconciliationEvidenceKind(const std::wstring& value)
        {
            if (value == L"MaintenanceLeaseCasHeldFinalIdentityV1")
            {
                return SessionManifestReconciliationEvidenceKind::
                    MaintenanceLeaseCasHeldFinalIdentityV1;
            }
            throw std::runtime_error("Unknown reconciliation evidence kind.");
        }

        const wchar_t* ErrorCategoryText(
            const SessionManifestErrorCategory category) noexcept
        {
            switch (category)
            {
            case SessionManifestErrorCategory::None: return L"None";
            case SessionManifestErrorCategory::Recording: return L"Recording";
            case SessionManifestErrorCategory::Finalize: return L"Finalize";
            case SessionManifestErrorCategory::Validation: return L"Validation";
            case SessionManifestErrorCategory::Publish: return L"Publish";
            case SessionManifestErrorCategory::ManifestPersistence:
                return L"ManifestPersistence";
            case SessionManifestErrorCategory::UnknownCrash: return L"UnknownCrash";
            default: return nullptr;
            }
        }

        SessionManifestErrorCategory ParseErrorCategory(
            const std::wstring& value)
        {
            for (const auto category : {
                SessionManifestErrorCategory::None,
                SessionManifestErrorCategory::Recording,
                SessionManifestErrorCategory::Finalize,
                SessionManifestErrorCategory::Validation,
                SessionManifestErrorCategory::Publish,
                SessionManifestErrorCategory::ManifestPersistence,
                SessionManifestErrorCategory::UnknownCrash })
            {
                if (value == ErrorCategoryText(category)) return category;
            }
            throw std::runtime_error("Unknown manifest error category.");
        }

        void AppendJsonString(
            std::wstring& output,
            const std::wstring_view value)
        {
            constexpr wchar_t hex[] = L"0123456789ABCDEF";
            output.push_back(L'"');
            for (const auto character : value)
            {
                switch (character)
                {
                case L'"': output += L"\\\""; break;
                case L'\\': output += L"\\\\"; break;
                case L'\b': output += L"\\b"; break;
                case L'\f': output += L"\\f"; break;
                case L'\n': output += L"\\n"; break;
                case L'\r': output += L"\\r"; break;
                case L'\t': output += L"\\t"; break;
                default:
                    if (character < 0x20)
                    {
                        output += L"\\u";
                        output.push_back(hex[(character >> 12) & 0xF]);
                        output.push_back(hex[(character >> 8) & 0xF]);
                        output.push_back(hex[(character >> 4) & 0xF]);
                        output.push_back(hex[character & 0xF]);
                    }
                    else
                    {
                        output.push_back(character);
                    }
                    break;
                }
            }
            output.push_back(L'"');
        }

        void AppendFieldName(std::wstring& output, const wchar_t* const name)
        {
            AppendJsonString(output, name);
            output.push_back(L':');
        }

        void AppendOptionalHResult(
            std::wstring& output,
            const std::optional<HRESULT> value)
        {
            output += value.has_value()
                ? std::to_wstring(static_cast<std::int32_t>(*value))
                : L"null";
        }

        std::wstring SerializeWide(const SessionManifest& value)
        {
            std::wstring output;
            output.reserve(2048);
            output.push_back(L'{');
            AppendFieldName(output, L"schemaVersion");
            output += std::to_wstring(value.schemaVersion);
            output += L",";
            AppendFieldName(output, L"revision");
            output += std::to_wstring(value.revision);
            output += L",";
            AppendFieldName(output, L"writerStrategy");
            AppendJsonString(output, value.writerStrategy);
            output += L",";
            AppendFieldName(output, L"sessionId");
            AppendJsonString(output, value.sessionId);
            output += L",";
            AppendFieldName(output, L"createdAtUtc");
            AppendJsonString(output, value.createdAtUtc);
            output += L",";
            AppendFieldName(output, L"updatedAtUtc");
            AppendJsonString(output, value.updatedAtUtc);
            output += L",";
            AppendFieldName(output, L"workingPath");
            AppendJsonString(output, value.workingPath);
            output += L",";
            AppendFieldName(output, L"plannedFinalPath");
            AppendJsonString(output, value.plannedFinalPath);
            output += L",";
            AppendFieldName(output, L"publishedPath");
            AppendJsonString(output, value.publishedPath);
            output += L",";
            AppendFieldName(output, L"state");
            AppendJsonString(output, StateText(value.state));
            output += L",";
            AppendFieldName(output, L"workingFileOwnedBySession");
            output += value.workingFileOwnedBySession ? L"true" : L"false";
            output += L",";
            AppendFieldName(output, L"writeSampleAttempted");
            output += value.writeSampleAttempted ? L"true" : L"false";
            output += L",";
            AppendFieldName(output, L"frameSubmitted");
            output += value.frameSubmitted ? L"true" : L"false";
            output += L",";
            AppendFieldName(output, L"workerExited");
            output += value.workerExited ? L"true" : L"false";
            output += L",";
            AppendFieldName(output, L"recordingResourcesReleased");
            output += value.recordingResourcesReleased ? L"true" : L"false";
            output += L",";
            AppendFieldName(output, L"residualOutstanding");
            output += std::to_wstring(value.residualOutstanding);
            output += L",";
            AppendFieldName(output, L"finalize");
            output.push_back(L'{');
            AppendFieldName(output, L"attempted");
            output += value.finalize.attempted ? L"true" : L"false";
            output += L",";
            AppendFieldName(output, L"count");
            output += std::to_wstring(value.finalize.count);
            output += L",";
            AppendFieldName(output, L"hresult");
            AppendOptionalHResult(output, value.finalize.hresult);
            output += L"},";
            AppendFieldName(output, L"validation");
            output.push_back(L'{');
            AppendFieldName(output, L"attempted");
            output += value.validation.attempted ? L"true" : L"false";
            output += L",";
            AppendFieldName(output, L"passed");
            output += value.validation.passed ? L"true" : L"false";
            output += L",";
            AppendFieldName(output, L"hresult");
            AppendOptionalHResult(output, value.validation.hresult);
            output += L"},";
            AppendFieldName(output, L"publish");
            output.push_back(L'{');
            AppendFieldName(output, L"attempted");
            output += value.publish.attempted ? L"true" : L"false";
            output += L",";
            AppendFieldName(output, L"published");
            output += value.publish.published ? L"true" : L"false";
            output += L",";
            AppendFieldName(output, L"hresult");
            AppendOptionalHResult(output, value.publish.hresult);
            output += L"},";
            if (value.schemaVersion == SessionManifestSchemaVersion ||
                value.schemaVersion == SessionManifestReconciledSchemaVersion)
            {
                AppendFieldName(output, L"workingFileIdentity");
                output.push_back(L'{');
                AppendFieldName(output, L"attempted");
                output += value.workingFileIdentity.attempted
                    ? L"true" : L"false";
                output += L",";
                AppendFieldName(output, L"captured");
                output += value.workingFileIdentity.captured
                    ? L"true" : L"false";
                output += L",";
                AppendFieldName(output, L"volumeIdentity");
                AppendJsonString(
                    output, value.workingFileIdentity.volumeIdentity);
                output += L",";
                AppendFieldName(output, L"fileId");
                AppendJsonString(output, value.workingFileIdentity.fileId);
                output += L",";
                AppendFieldName(output, L"hresult");
                AppendOptionalHResult(
                    output, value.workingFileIdentity.hresult);
                output += L"},";
                AppendFieldName(
                    output, L"postPublishIdentityVerification");
                output.push_back(L'{');
                AppendFieldName(output, L"attempted");
                output += value.postPublishIdentityVerification.attempted
                    ? L"true" : L"false";
                output += L",";
                AppendFieldName(output, L"matched");
                output += value.postPublishIdentityVerification.matched
                    ? L"true" : L"false";
                output += L",";
                AppendFieldName(output, L"hresult");
                AppendOptionalHResult(
                    output, value.postPublishIdentityVerification.hresult);
                output += L"},";
            }
            if (value.schemaVersion == SessionManifestReconciledSchemaVersion)
            {
                AppendFieldName(output, L"reconciliation");
                output.push_back(L'{');
                AppendFieldName(output, L"reconciled");
                output += value.reconciliation.reconciled
                    ? L"true" : L"false";
                output += L",";
                AppendFieldName(output, L"kind");
                AppendJsonString(
                    output,
                    ReconciliationKindText(value.reconciliation.kind));
                output += L",";
                AppendFieldName(output, L"sourceRevision");
                output += std::to_wstring(
                    value.reconciliation.sourceRevision);
                output += L",";
                AppendFieldName(output, L"reconciledAtUtc");
                AppendJsonString(
                    output, value.reconciliation.reconciledAtUtc);
                output += L",";
                AppendFieldName(output, L"evidenceKind");
                AppendJsonString(
                    output,
                    ReconciliationEvidenceKindText(
                        value.reconciliation.evidenceKind));
                output += L",";
                AppendFieldName(output, L"originalPublishResultKnown");
                output += value.reconciliation.originalPublishResultKnown
                    ? L"true" : L"false";
                output += L",";
                AppendFieldName(output, L"confirmedFinalPath");
                AppendJsonString(
                    output, value.reconciliation.confirmedFinalPath);
                output += L"},";
            }
            AppendFieldName(output, L"errorCategory");
            AppendJsonString(output, ErrorCategoryText(value.errorCategory));
            output += L",";
            AppendFieldName(output, L"errorCode");
            AppendOptionalHResult(output, value.errorCode);
            output += L",";
            AppendFieldName(output, L"errorMessage");
            AppendJsonString(output, value.errorMessage);
            output.push_back(L'}');
            return output;
        }

        bool WideToUtf8(
            const std::wstring_view value,
            std::string& output) noexcept
        {
            if (value.empty())
            {
                output.clear();
                return true;
            }
            const auto length = WideCharToMultiByte(
                CP_UTF8,
                WC_ERR_INVALID_CHARS,
                value.data(),
                static_cast<int>(value.size()),
                nullptr,
                0,
                nullptr,
                nullptr);
            if (length <= 0)
            {
                return false;
            }
            try
            {
                output.resize(static_cast<std::size_t>(length));
            }
            catch (...)
            {
                return false;
            }
            return WideCharToMultiByte(
                CP_UTF8,
                WC_ERR_INVALID_CHARS,
                value.data(),
                static_cast<int>(value.size()),
                output.data(),
                length,
                nullptr,
                nullptr) == length;
        }

        HRESULT Utf8ToWide(
            const std::string_view value,
            std::wstring& output) noexcept
        {
            if (value.empty())
            {
                output.clear();
                return S_OK;
            }
            const auto length = MultiByteToWideChar(
                CP_UTF8,
                MB_ERR_INVALID_CHARS,
                value.data(),
                static_cast<int>(value.size()),
                nullptr,
                0);
            if (length <= 0)
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
            try
            {
                output.resize(static_cast<std::size_t>(length));
            }
            catch (...)
            {
                return E_OUTOFMEMORY;
            }
            if (MultiByteToWideChar(
                CP_UTF8,
                MB_ERR_INVALID_CHARS,
                value.data(),
                static_cast<int>(value.size()),
                output.data(),
                length) != length)
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
            return S_OK;
        }

        SessionManifest DeserializeManifest(const JsonValue& root)
        {
            if (root.kind != JsonKind::Object)
            {
                throw std::runtime_error("Unexpected manifest root schema.");
            }
            SessionManifest result{};
            const auto schema = Required(
                root, L"schemaVersion", JsonKind::Integer).integer;
            const auto revision = Required(
                root, L"revision", JsonKind::Integer).integer;
            if (schema < 0 || schema > (std::numeric_limits<std::uint32_t>::max)() ||
                revision < 0)
            {
                throw std::runtime_error("Manifest numeric field out of range.");
            }
            result.schemaVersion = static_cast<std::uint32_t>(schema);
            const auto expectedFields = result.schemaVersion ==
                    SessionManifestLegacySchemaVersion
                ? 22u
                : result.schemaVersion == SessionManifestSchemaVersion
                    ? 24u
                    : result.schemaVersion ==
                            SessionManifestReconciledSchemaVersion
                        ? 25u
                        : 0u;
            if (expectedFields == 0 || root.object.size() != expectedFields)
            {
                throw std::runtime_error("Unexpected manifest root schema.");
            }
            result.revision = static_cast<std::uint64_t>(revision);
            result.writerStrategy = Required(
                root, L"writerStrategy", JsonKind::String).string;
            result.sessionId = Required(
                root, L"sessionId", JsonKind::String).string;
            result.createdAtUtc = Required(
                root, L"createdAtUtc", JsonKind::String).string;
            result.updatedAtUtc = Required(
                root, L"updatedAtUtc", JsonKind::String).string;
            result.workingPath = Required(
                root, L"workingPath", JsonKind::String).string;
            result.plannedFinalPath = Required(
                root, L"plannedFinalPath", JsonKind::String).string;
            result.publishedPath = Required(
                root, L"publishedPath", JsonKind::String).string;
            result.state = ParseState(Required(
                root, L"state", JsonKind::String).string);
            result.workingFileOwnedBySession = Required(
                root, L"workingFileOwnedBySession", JsonKind::Boolean).boolean;
            result.writeSampleAttempted = Required(
                root, L"writeSampleAttempted", JsonKind::Boolean).boolean;
            result.frameSubmitted = Required(
                root, L"frameSubmitted", JsonKind::Boolean).boolean;
            result.workerExited = Required(
                root, L"workerExited", JsonKind::Boolean).boolean;
            result.recordingResourcesReleased = Required(
                root, L"recordingResourcesReleased", JsonKind::Boolean).boolean;
            const auto residualOutstanding = Required(
                root, L"residualOutstanding", JsonKind::Integer).integer;
            if (residualOutstanding < 0 ||
                residualOutstanding >
                    (std::numeric_limits<std::uint32_t>::max)())
            {
                throw std::runtime_error(
                    "Residual outstanding is out of range.");
            }
            result.residualOutstanding =
                static_cast<std::uint32_t>(residualOutstanding);

            const auto& finalize = Required(
                root, L"finalize", JsonKind::Object);
            if (finalize.object.size() != 3)
            {
                throw std::runtime_error("Unexpected finalize schema.");
            }
            result.finalize.attempted = Required(
                finalize, L"attempted", JsonKind::Boolean).boolean;
            const auto finalizeCount = Required(
                finalize, L"count", JsonKind::Integer).integer;
            if (finalizeCount < 0 ||
                finalizeCount > (std::numeric_limits<std::uint32_t>::max)())
            {
                throw std::runtime_error("Finalize count out of range.");
            }
            result.finalize.count = static_cast<std::uint32_t>(finalizeCount);
            result.finalize.hresult = OptionalHResult(finalize, L"hresult");

            const auto& validation = Required(
                root, L"validation", JsonKind::Object);
            if (validation.object.size() != 3)
            {
                throw std::runtime_error("Unexpected validation schema.");
            }
            result.validation.attempted = Required(
                validation, L"attempted", JsonKind::Boolean).boolean;
            result.validation.passed = Required(
                validation, L"passed", JsonKind::Boolean).boolean;
            result.validation.hresult = OptionalHResult(validation, L"hresult");

            const auto& publish = Required(root, L"publish", JsonKind::Object);
            if (publish.object.size() != 3)
            {
                throw std::runtime_error("Unexpected publish schema.");
            }
            result.publish.attempted = Required(
                publish, L"attempted", JsonKind::Boolean).boolean;
            result.publish.published = Required(
                publish, L"published", JsonKind::Boolean).boolean;
            result.publish.hresult = OptionalHResult(publish, L"hresult");

            if (result.schemaVersion == SessionManifestSchemaVersion ||
                result.schemaVersion ==
                    SessionManifestReconciledSchemaVersion)
            {
                const auto& identity = Required(
                    root, L"workingFileIdentity", JsonKind::Object);
                if (identity.object.size() != 5)
                {
                    throw std::runtime_error(
                        "Unexpected working identity schema.");
                }
                result.workingFileIdentity.attempted = Required(
                    identity, L"attempted", JsonKind::Boolean).boolean;
                result.workingFileIdentity.captured = Required(
                    identity, L"captured", JsonKind::Boolean).boolean;
                result.workingFileIdentity.volumeIdentity = Required(
                    identity, L"volumeIdentity", JsonKind::String).string;
                result.workingFileIdentity.fileId = Required(
                    identity, L"fileId", JsonKind::String).string;
                result.workingFileIdentity.hresult = OptionalHResult(
                    identity, L"hresult");

                const auto& verification = Required(
                    root,
                    L"postPublishIdentityVerification",
                    JsonKind::Object);
                if (verification.object.size() != 3)
                {
                    throw std::runtime_error(
                        "Unexpected post-publish identity schema.");
                }
                result.postPublishIdentityVerification.attempted = Required(
                    verification, L"attempted", JsonKind::Boolean).boolean;
                result.postPublishIdentityVerification.matched = Required(
                    verification, L"matched", JsonKind::Boolean).boolean;
                result.postPublishIdentityVerification.hresult =
                    OptionalHResult(verification, L"hresult");
            }

            if (result.schemaVersion ==
                SessionManifestReconciledSchemaVersion)
            {
                const auto& reconciliation = Required(
                    root, L"reconciliation", JsonKind::Object);
                if (reconciliation.object.size() != 7)
                {
                    throw std::runtime_error(
                        "Unexpected reconciliation schema.");
                }
                result.reconciliation.reconciled = Required(
                    reconciliation,
                    L"reconciled",
                    JsonKind::Boolean).boolean;
                result.reconciliation.kind = ParseReconciliationKind(
                    Required(
                        reconciliation,
                        L"kind",
                        JsonKind::String).string);
                const auto sourceRevision = Required(
                    reconciliation,
                    L"sourceRevision",
                    JsonKind::Integer).integer;
                if (sourceRevision < 0)
                {
                    throw std::runtime_error(
                        "Reconciliation source revision is out of range.");
                }
                result.reconciliation.sourceRevision =
                    static_cast<std::uint64_t>(sourceRevision);
                result.reconciliation.reconciledAtUtc = Required(
                    reconciliation,
                    L"reconciledAtUtc",
                    JsonKind::String).string;
                result.reconciliation.evidenceKind =
                    ParseReconciliationEvidenceKind(Required(
                        reconciliation,
                        L"evidenceKind",
                        JsonKind::String).string);
                result.reconciliation.originalPublishResultKnown = Required(
                    reconciliation,
                    L"originalPublishResultKnown",
                    JsonKind::Boolean).boolean;
                result.reconciliation.confirmedFinalPath = Required(
                    reconciliation,
                    L"confirmedFinalPath",
                    JsonKind::String).string;
            }

            result.errorCategory = ParseErrorCategory(Required(
                root, L"errorCategory", JsonKind::String).string);
            result.errorCode = OptionalHResult(root, L"errorCode");
            result.errorMessage = Required(
                root, L"errorMessage", JsonKind::String).string;
            return result;
        }

        bool FileNameUsesCanonicalSessionIdentity(
            const std::filesystem::path& path,
            const std::wstring& expectedSessionId,
            const std::wstring_view suffix)
        {
            auto expectedFileName = expectedSessionId;
            expectedFileName.append(suffix.data(), suffix.size());
            return _wcsicmp(
                path.filename().c_str(), expectedFileName.c_str()) == 0;
        }

        bool ValidUtcTimestamp(const std::wstring& value) noexcept
        {
            if (value.size() != 28 ||
                value[4] != L'-' || value[7] != L'-' ||
                value[10] != L'T' || value[13] != L':' ||
                value[16] != L':' || value[19] != L'.' ||
                value[27] != L'Z')
            {
                return false;
            }
            for (std::size_t index = 0; index < value.size(); ++index)
            {
                if (index == 4 || index == 7 || index == 10 ||
                    index == 13 || index == 16 || index == 19 || index == 27)
                {
                    continue;
                }
                if (value[index] < L'0' || value[index] > L'9')
                {
                    return false;
                }
            }
            unsigned short year{}, month{}, day{}, hour{}, minute{}, second{};
            if (swscanf_s(
                    value.c_str(),
                    L"%4hu-%2hu-%2huT%2hu:%2hu:%2hu",
                    &year,
                    &month,
                    &day,
                    &hour,
                    &minute,
                    &second) != 6)
            {
                return false;
            }
            SYSTEMTIME systemTime{};
            systemTime.wYear = year;
            systemTime.wMonth = month;
            systemTime.wDay = day;
            systemTime.wHour = hour;
            systemTime.wMinute = minute;
            systemTime.wSecond = second;
            FILETIME fileTime{};
            return SystemTimeToFileTime(&systemTime, &fileTime) != FALSE;
        }

        std::wstring UtcNowText()
        {
            FILETIME fileTime{};
            GetSystemTimePreciseAsFileTime(&fileTime);
            SYSTEMTIME systemTime{};
            if (!FileTimeToSystemTime(&fileTime, &systemTime))
            {
                throw std::system_error(
                    static_cast<int>(GetLastError()), std::system_category());
            }
            ULARGE_INTEGER ticks{};
            ticks.LowPart = fileTime.dwLowDateTime;
            ticks.HighPart = fileTime.dwHighDateTime;
            const auto fraction = static_cast<unsigned long>(
                ticks.QuadPart % 10'000'000ull);
            wchar_t buffer[64]{};
            swprintf_s(
                buffer,
                L"%04hu-%02hu-%02huT%02hu:%02hu:%02hu.%07luZ",
                systemTime.wYear,
                systemTime.wMonth,
                systemTime.wDay,
                systemTime.wHour,
                systemTime.wMinute,
                systemTime.wSecond,
                fraction);
            return buffer;
        }

        bool EqualPath(
            const std::filesystem::path& left,
            const std::filesystem::path& right)
        {
            const auto normalizedLeft = std::filesystem::absolute(left).
                lexically_normal().wstring();
            const auto normalizedRight = std::filesystem::absolute(right).
                lexically_normal().wstring();
            return _wcsicmp(normalizedLeft.c_str(), normalizedRight.c_str()) == 0;
        }

        HRESULT ValidateManifest(
            const SessionManifest& value,
            const std::filesystem::path& managedOutputRoot,
            const std::wstring& expectedSessionId,
            SessionManifestSemanticIssue* const semanticIssue = nullptr)
            noexcept
        {
            const auto setIssue = [semanticIssue](
                const SessionManifestSemanticIssue issue) noexcept
            {
                if (semanticIssue != nullptr)
                {
                    *semanticIssue = issue;
                }
            };
            setIssue(SessionManifestSemanticIssue::None);
            try
            {
                std::wstring canonicalExpectedSessionId;
                std::wstring canonicalManifestSessionId;
                const auto expectedIdentityResult = NormalizeRecordingSessionId(
                    expectedSessionId, canonicalExpectedSessionId);
                const auto manifestIdentityResult = NormalizeRecordingSessionId(
                    value.sessionId, canonicalManifestSessionId);
                if (expectedIdentityResult == E_OUTOFMEMORY ||
                    manifestIdentityResult == E_OUTOFMEMORY)
                {
                    return E_OUTOFMEMORY;
                }
                if (FAILED(expectedIdentityResult) ||
                    FAILED(manifestIdentityResult) ||
                    canonicalExpectedSessionId != canonicalManifestSessionId)
                {
                    setIssue(
                        SessionManifestSemanticIssue::
                            SessionIdentityMismatch);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                if (!managedOutputRoot.is_absolute() ||
                    (value.schemaVersion != SessionManifestSchemaVersion &&
                        value.schemaVersion !=
                            SessionManifestLegacySchemaVersion &&
                        value.schemaVersion !=
                            SessionManifestReconciledSchemaVersion) ||
                    value.revision == 0 ||
                    value.revision > MaximumExactJsonInteger ||
                    value.writerStrategy != L"mf-sinkwriter-standard-mp4-v1" ||
                    !ValidUtcTimestamp(value.createdAtUtc) ||
                    !ValidUtcTimestamp(value.updatedAtUtc) ||
                    value.updatedAtUtc < value.createdAtUtc ||
                    value.errorMessage.size() > 1024)
                {
                    setIssue(SessionManifestSemanticIssue::Other);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                const auto stateText = StateText(value.state);
                const auto categoryText = ErrorCategoryText(value.errorCategory);
                if (stateText == nullptr || categoryText == nullptr)
                {
                    setIssue(SessionManifestSemanticIssue::Other);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                const std::filesystem::path working(value.workingPath);
                const std::filesystem::path planned(value.plannedFinalPath);
                if (value.workingPath.empty() || value.plannedFinalPath.empty() ||
                    !working.is_absolute() || !planned.is_absolute() ||
                    !EqualPath(working.parent_path(), managedOutputRoot) ||
                    !EqualPath(planned.parent_path(), managedOutputRoot) ||
                    !FileNameUsesCanonicalSessionIdentity(
                        working,
                        canonicalExpectedSessionId,
                        L".partial.mp4") ||
                    !FileNameUsesCanonicalSessionIdentity(
                        planned,
                        canonicalExpectedSessionId,
                        L".mp4"))
                {
                    setIssue(
                        SessionManifestSemanticIssue::PathPolicyViolation);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                if (value.publish.published)
                {
                    if (value.publishedPath.empty() ||
                        !EqualPath(value.publishedPath, value.plannedFinalPath))
                    {
                        setIssue(
                            SessionManifestSemanticIssue::
                                PublishedPathMismatch);
                        return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                    }
                }
                else if (!value.publishedPath.empty())
                {
                    setIssue(
                        SessionManifestSemanticIssue::
                            PublishedPathMismatch);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                if (value.frameSubmitted && !value.writeSampleAttempted)
                {
                    setIssue(SessionManifestSemanticIssue::Other);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                if (value.recordingResourcesReleased &&
                    (!value.workerExited || value.residualOutstanding != 0))
                {
                    setIssue(SessionManifestSemanticIssue::Other);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                if (value.finalize.attempted != value.finalize.hresult.has_value() ||
                    (value.finalize.attempted && value.finalize.count == 0) ||
                    (!value.finalize.attempted && value.finalize.count != 0))
                {
                    setIssue(SessionManifestSemanticIssue::Other);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                if (value.validation.attempted !=
                        value.validation.hresult.has_value() ||
                    value.validation.passed !=
                        (value.validation.hresult.has_value() &&
                            SUCCEEDED(*value.validation.hresult)))
                {
                    setIssue(SessionManifestSemanticIssue::Other);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                if (value.publish.attempted != value.publish.hresult.has_value() ||
                    value.publish.published !=
                        (value.publish.hresult.has_value() &&
                            SUCCEEDED(*value.publish.hresult)))
                {
                    setIssue(SessionManifestSemanticIssue::Other);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                if (value.schemaVersion == SessionManifestLegacySchemaVersion)
                {
                    if (value.workingFileIdentity.attempted ||
                        value.workingFileIdentity.captured ||
                        !value.workingFileIdentity.volumeIdentity.empty() ||
                        !value.workingFileIdentity.fileId.empty() ||
                        value.workingFileIdentity.hresult.has_value() ||
                        value.postPublishIdentityVerification.attempted ||
                        value.postPublishIdentityVerification.matched ||
                        value.postPublishIdentityVerification.hresult.has_value())
                    {
                        setIssue(SessionManifestSemanticIssue::Other);
                        return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                    }
                }
                else
                {
                    const auto& identity = value.workingFileIdentity;
                    if (identity.attempted != identity.hresult.has_value() ||
                        identity.captured !=
                            (identity.hresult.has_value() &&
                                SUCCEEDED(*identity.hresult)))
                    {
                        setIssue(SessionManifestSemanticIssue::Other);
                        return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                    }
                    if (identity.captured)
                    {
                        std::uint64_t volume{};
                        std::array<std::uint8_t, 16> fileId{};
                        if (!ParseVolumeIdentityCanonical(
                                identity.volumeIdentity, volume) ||
                            !ParseFileIdCanonical(identity.fileId, fileId))
                        {
                            setIssue(SessionManifestSemanticIssue::Other);
                            return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                        }
                    }
                    else if (!identity.volumeIdentity.empty() ||
                        !identity.fileId.empty())
                    {
                        setIssue(SessionManifestSemanticIssue::Other);
                        return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                    }
                    const auto& verification =
                        value.postPublishIdentityVerification;
                    if (verification.attempted !=
                            verification.hresult.has_value() ||
                        (verification.matched &&
                            (!verification.attempted ||
                                FAILED(*verification.hresult) ||
                                !identity.captured)))
                    {
                        setIssue(SessionManifestSemanticIssue::Other);
                        return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                    }
                }
                if (value.schemaVersion !=
                        SessionManifestReconciledSchemaVersion &&
                    (value.reconciliation.reconciled ||
                        value.reconciliation.kind !=
                            SessionManifestReconciliationKind::None ||
                        value.reconciliation.sourceRevision != 0 ||
                        !value.reconciliation.reconciledAtUtc.empty() ||
                        value.reconciliation.evidenceKind !=
                            SessionManifestReconciliationEvidenceKind::None ||
                        value.reconciliation.originalPublishResultKnown ||
                        !value.reconciliation.confirmedFinalPath.empty()))
                {
                    setIssue(SessionManifestSemanticIssue::Other);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                if (value.state == SessionManifestState::UserCancelled &&
                    (value.schemaVersion != SessionManifestSchemaVersion ||
                        !value.workingFileOwnedBySession ||
                        !value.workerExited ||
                        !value.recordingResourcesReleased ||
                        value.residualOutstanding != 0 ||
                        !value.finalize.attempted ||
                        value.finalize.count != 1 ||
                        value.validation.attempted ||
                        value.validation.passed ||
                        value.validation.hresult.has_value() ||
                        value.publish.attempted ||
                        value.publish.published ||
                        value.publish.hresult.has_value() ||
                        !value.publishedPath.empty() ||
                        !value.workingFileIdentity.attempted ||
                        value.postPublishIdentityVerification.attempted ||
                        value.postPublishIdentityVerification.matched ||
                        value.postPublishIdentityVerification.hresult.has_value() ||
                        value.errorCategory !=
                            SessionManifestErrorCategory::None ||
                        value.errorCode.has_value() ||
                        !value.errorMessage.empty()))
                {
                    setIssue(SessionManifestSemanticIssue::Other);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                if ((value.state == SessionManifestState::ReadyToPublish ||
                        value.state == SessionManifestState::Published ||
                        value.state == SessionManifestState::Completed ||
                        value.state ==
                            SessionManifestState::ReconciledCompleted) &&
                    (!value.finalize.attempted || value.finalize.count != 1 ||
                        FAILED(*value.finalize.hresult) ||
                        !value.validation.attempted ||
                        !value.validation.passed ||
                        !value.workerExited ||
                        !value.recordingResourcesReleased ||
                        value.residualOutstanding != 0))
                {
                    setIssue(SessionManifestSemanticIssue::Other);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                if ((value.schemaVersion == SessionManifestSchemaVersion ||
                        value.schemaVersion ==
                            SessionManifestReconciledSchemaVersion) &&
                    (value.state == SessionManifestState::ReadyToPublish ||
                        value.state == SessionManifestState::Published ||
                        value.state == SessionManifestState::Completed ||
                        value.state ==
                            SessionManifestState::ReconciledCompleted) &&
                    !value.workingFileIdentity.attempted)
                {
                    setIssue(SessionManifestSemanticIssue::Other);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                if ((value.state == SessionManifestState::Published ||
                        value.state == SessionManifestState::Completed) !=
                    value.publish.published)
                {
                    setIssue(SessionManifestSemanticIssue::Other);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                if (value.schemaVersion == SessionManifestSchemaVersion &&
                    (value.state == SessionManifestState::Published ||
                        value.state == SessionManifestState::Completed) &&
                    !value.postPublishIdentityVerification.attempted)
                {
                    setIssue(SessionManifestSemanticIssue::Other);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                if (value.schemaVersion ==
                    SessionManifestReconciledSchemaVersion)
                {
                    const auto& reconciliation = value.reconciliation;
                    const auto& identity = value.workingFileIdentity;
                    const auto& verification =
                        value.postPublishIdentityVerification;
                    const std::filesystem::path confirmed(
                        reconciliation.confirmedFinalPath);
                    if (value.state !=
                            SessionManifestState::ReconciledCompleted ||
                        !reconciliation.reconciled ||
                        reconciliation.kind !=
                            SessionManifestReconciliationKind::
                                FinalAtPlannedPathSamePersistentFileV1 ||
                        reconciliation.sourceRevision == 0 ||
                        reconciliation.sourceRevision >=
                            MaximumExactJsonInteger ||
                        value.revision !=
                            reconciliation.sourceRevision + 1 ||
                        !ValidUtcTimestamp(
                            reconciliation.reconciledAtUtc) ||
                        value.updatedAtUtc !=
                            reconciliation.reconciledAtUtc ||
                        reconciliation.evidenceKind !=
                            SessionManifestReconciliationEvidenceKind::
                                MaintenanceLeaseCasHeldFinalIdentityV1 ||
                        reconciliation.originalPublishResultKnown ||
                        reconciliation.confirmedFinalPath.empty() ||
                        !confirmed.is_absolute() ||
                        !EqualPath(
                            reconciliation.confirmedFinalPath,
                            value.plannedFinalPath) ||
                        !value.workingFileOwnedBySession ||
                        !value.writeSampleAttempted ||
                        !value.frameSubmitted ||
                        !value.workerExited ||
                        !value.recordingResourcesReleased ||
                        value.residualOutstanding != 0 ||
                        !value.finalize.attempted ||
                        value.finalize.count != 1 ||
                        !value.finalize.hresult.has_value() ||
                        *value.finalize.hresult != S_OK ||
                        !value.validation.attempted ||
                        !value.validation.passed ||
                        !value.validation.hresult.has_value() ||
                        *value.validation.hresult != S_OK ||
                        value.publish.attempted ||
                        value.publish.published ||
                        value.publish.hresult.has_value() ||
                        !value.publishedPath.empty() ||
                        !identity.attempted || !identity.captured ||
                        !identity.hresult.has_value() ||
                        *identity.hresult != S_OK ||
                        verification.attempted || verification.matched ||
                        verification.hresult.has_value() ||
                        value.errorCategory !=
                            SessionManifestErrorCategory::None ||
                        value.errorCode.has_value() ||
                        !value.errorMessage.empty())
                    {
                        setIssue(SessionManifestSemanticIssue::Other);
                        return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                    }
                }
                else if (value.state ==
                    SessionManifestState::ReconciledCompleted)
                {
                    setIssue(SessionManifestSemanticIssue::Other);
                    return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
                return S_OK;
            }
            catch (const std::bad_alloc&)
            {
                return E_OUTOFMEMORY;
            }
            catch (...)
            {
                setIssue(SessionManifestSemanticIssue::Other);
                return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            }
        }

        bool StateTransitionAllowed(
            const SessionManifestState from,
            const SessionManifestState to) noexcept
        {
            if (from == to) return true;
            if (to == SessionManifestState::Failed &&
                from != SessionManifestState::Published &&
                from != SessionManifestState::Completed &&
                from != SessionManifestState::Unknown &&
                from != SessionManifestState::UserCancelled)
            {
                return true;
            }
            switch (from)
            {
            case SessionManifestState::Created:
                return to == SessionManifestState::Starting;
            case SessionManifestState::Starting:
                return to == SessionManifestState::Recording ||
                    to == SessionManifestState::Stopping;
            case SessionManifestState::Recording:
                return to == SessionManifestState::Stopping;
            case SessionManifestState::Stopping:
                return to == SessionManifestState::ReadyToPublish ||
                    to == SessionManifestState::UserCancelled;
            case SessionManifestState::ReadyToPublish:
                return to == SessionManifestState::Published;
            case SessionManifestState::Published:
                return to == SessionManifestState::Completed;
            case SessionManifestState::Unknown:
                return to == SessionManifestState::Unknown;
            default:
                return false;
            }
        }

        HRESULT ValidateUpdate(
            const SessionManifest& current,
            const SessionManifest& next) noexcept
        {
            if (current.schemaVersion != next.schemaVersion ||
                current.sessionId != next.sessionId ||
                current.writerStrategy != next.writerStrategy ||
                current.createdAtUtc != next.createdAtUtc ||
                current.workingPath != next.workingPath ||
                current.plannedFinalPath != next.plannedFinalPath ||
                !StateTransitionAllowed(current.state, next.state) ||
                (current.workingFileOwnedBySession &&
                    !next.workingFileOwnedBySession) ||
                (current.writeSampleAttempted && !next.writeSampleAttempted) ||
                (current.frameSubmitted && !next.frameSubmitted) ||
                (current.workerExited && !next.workerExited) ||
                (current.recordingResourcesReleased &&
                    !next.recordingResourcesReleased) ||
                (current.finalize.attempted && !next.finalize.attempted) ||
                next.finalize.count < current.finalize.count ||
                (current.validation.attempted && !next.validation.attempted) ||
                (current.validation.passed && !next.validation.passed) ||
                (current.publish.attempted && !next.publish.attempted) ||
                (current.publish.published && !next.publish.published) ||
                (current.publish.published &&
                    current.publishedPath != next.publishedPath) ||
                (current.workingFileIdentity.attempted &&
                    !next.workingFileIdentity.attempted) ||
                (current.workingFileIdentity.captured &&
                    (!next.workingFileIdentity.captured ||
                        current.workingFileIdentity.volumeIdentity !=
                            next.workingFileIdentity.volumeIdentity ||
                        current.workingFileIdentity.fileId !=
                            next.workingFileIdentity.fileId)) ||
                (current.postPublishIdentityVerification.attempted &&
                    !next.postPublishIdentityVerification.attempted) ||
                (current.postPublishIdentityVerification.matched &&
                    !next.postPublishIdentityVerification.matched))
            {
                return HRESULT_FROM_WIN32(ERROR_INVALID_STATE);
            }
            return S_OK;
        }

        enum class ManifestByteReadStatus
        {
            Success,
            Empty,
            Oversized,
            Failure
        };

        struct ManifestByteReadResult final
        {
            ManifestByteReadStatus status{ ManifestByteReadStatus::Failure };
            HRESULT hresult{ E_UNEXPECTED };
        };

        ManifestByteReadResult ReadAllBytes(
            const std::filesystem::path& path,
            std::string& bytes) noexcept
        {
            const UniqueHandle file(CreateFileW(
                path.c_str(),
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                nullptr,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                nullptr));
            if (!file.Valid())
            {
                return {
                    ManifestByteReadStatus::Failure,
                    HRESULT_FROM_WIN32(GetLastError()) };
            }
            LARGE_INTEGER size{};
            if (!GetFileSizeEx(file.Get(), &size))
            {
                return {
                    ManifestByteReadStatus::Failure,
                    HRESULT_FROM_WIN32(GetLastError()) };
            }
            if (size.QuadPart <= 0)
            {
                return {
                    ManifestByteReadStatus::Empty,
                    HRESULT_FROM_WIN32(ERROR_INVALID_DATA) };
            }
            if (static_cast<std::uint64_t>(size.QuadPart) >
                MaximumManifestBytes)
            {
                return {
                    ManifestByteReadStatus::Oversized,
                    HRESULT_FROM_WIN32(ERROR_INVALID_DATA) };
            }
            try
            {
                bytes.resize(static_cast<std::size_t>(size.QuadPart));
            }
            catch (...)
            {
                return { ManifestByteReadStatus::Failure, E_OUTOFMEMORY };
            }
            std::size_t offset{};
            while (offset < bytes.size())
            {
                DWORD read{};
                const auto request = static_cast<DWORD>((std::min)(
                    bytes.size() - offset,
                    static_cast<std::size_t>((std::numeric_limits<DWORD>::max)())));
                if (!ReadFile(
                        file.Get(), bytes.data() + offset, request, &read, nullptr))
                {
                    return {
                        ManifestByteReadStatus::Failure,
                        HRESULT_FROM_WIN32(GetLastError()) };
                }
                if (read == 0)
                {
                    return {
                        ManifestByteReadStatus::Failure,
                        HRESULT_FROM_WIN32(ERROR_HANDLE_EOF) };
                }
                offset += read;
            }
            return { ManifestByteReadStatus::Success, S_OK };
        }

        HRESULT WriteAllBytesAndFlush(
            const std::filesystem::path& path,
            const std::string& bytes) noexcept
        {
            const UniqueHandle file(CreateFileW(
                path.c_str(),
                GENERIC_WRITE,
                0,
                nullptr,
                CREATE_NEW,
                FILE_ATTRIBUTE_TEMPORARY,
                nullptr));
            if (!file.Valid())
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
            std::size_t offset{};
            while (offset < bytes.size())
            {
                DWORD written{};
                const auto request = static_cast<DWORD>((std::min)(
                    bytes.size() - offset,
                    static_cast<std::size_t>((std::numeric_limits<DWORD>::max)())));
                if (!WriteFile(
                        file.Get(), bytes.data() + offset, request, &written, nullptr))
                {
                    return HRESULT_FROM_WIN32(GetLastError());
                }
                if (written == 0)
                {
                    return HRESULT_FROM_WIN32(ERROR_WRITE_FAULT);
                }
                offset += written;
            }
            if (!FlushFileBuffers(file.Get()))
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
            return S_OK;
        }

        SessionManifestParseResult ParseResult(
            const SessionManifestParseStatus status,
            const HRESULT diagnosticHResult,
            const std::optional<std::uint32_t> observedSchemaVersion =
                std::nullopt,
            const SessionManifestSemanticIssue semanticIssue =
                SessionManifestSemanticIssue::None) noexcept
        {
            return {
                status,
                diagnosticHResult,
                observedSchemaVersion,
                semanticIssue };
        }

        HRESULT CompatibilityHResult(
            const SessionManifestParseResult& result) noexcept
        {
            if (result.status == SessionManifestParseStatus::Valid)
            {
                return S_OK;
            }
            if (result.status == SessionManifestParseStatus::UnsupportedSchema ||
                result.status ==
                    SessionManifestParseStatus::UnknownOrFutureState)
            {
                return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            }
            return result.diagnosticHResult;
        }

        SessionManifestParseResult ParseManifestFile(
            const std::filesystem::path& path,
            SessionManifest& manifest) noexcept
        {
            std::string bytes;
            const auto read = ReadAllBytes(path, bytes);
            if (read.status == ManifestByteReadStatus::Empty)
            {
                return ParseResult(
                    SessionManifestParseStatus::MalformedJson,
                    read.hresult);
            }
            if (read.status == ManifestByteReadStatus::Oversized)
            {
                return ParseResult(
                    SessionManifestParseStatus::SemanticInvalid,
                    read.hresult);
            }
            if (read.status == ManifestByteReadStatus::Failure)
            {
                return ParseResult(
                    ClassifySessionManifestReadFailure(read.hresult),
                    read.hresult);
            }

            std::wstring text;
            const auto conversion = Utf8ToWide(bytes, text);
            if (FAILED(conversion))
            {
                return ParseResult(
                    conversion == E_OUTOFMEMORY
                        ? SessionManifestParseStatus::IoFailure
                        : SessionManifestParseStatus::MalformedJson,
                    conversion);
            }

            JsonValue root{};
            try
            {
                root = JsonParser(text).Parse();
            }
            catch (const JsonNestingLimitError&)
            {
                return ParseResult(
                    SessionManifestParseStatus::SemanticInvalid,
                    HRESULT_FROM_WIN32(ERROR_INVALID_DATA));
            }
            catch (const std::bad_alloc&)
            {
                return ParseResult(
                    SessionManifestParseStatus::IoFailure, E_OUTOFMEMORY);
            }
            catch (...)
            {
                return ParseResult(
                    SessionManifestParseStatus::MalformedJson,
                    HRESULT_FROM_WIN32(ERROR_INVALID_DATA));
            }

            if (root.kind != JsonKind::Object)
            {
                return ParseResult(
                    SessionManifestParseStatus::SemanticInvalid,
                    HRESULT_FROM_WIN32(ERROR_INVALID_DATA));
            }
            const auto schemaField = root.object.find(L"schemaVersion");
            if (schemaField == root.object.end() ||
                schemaField->second.kind != JsonKind::Integer ||
                schemaField->second.integer < 0 ||
                schemaField->second.integer >
                    (std::numeric_limits<std::uint32_t>::max)())
            {
                return ParseResult(
                    SessionManifestParseStatus::SemanticInvalid,
                    HRESULT_FROM_WIN32(ERROR_INVALID_DATA));
            }
            const auto schemaVersion = static_cast<std::uint32_t>(
                schemaField->second.integer);
            if (schemaVersion != SessionManifestSchemaVersion &&
                schemaVersion != SessionManifestLegacySchemaVersion &&
                schemaVersion != SessionManifestReconciledSchemaVersion)
            {
                return ParseResult(
                    SessionManifestParseStatus::UnsupportedSchema,
                    HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED),
                    schemaVersion);
            }

            SessionManifest candidate{};
            try
            {
                candidate = DeserializeManifest(root);
            }
            catch (const UnknownManifestStateError&)
            {
                return ParseResult(
                    SessionManifestParseStatus::UnknownOrFutureState,
                    HRESULT_FROM_WIN32(ERROR_INVALID_STATE),
                    schemaVersion);
            }
            catch (const std::bad_alloc&)
            {
                return ParseResult(
                    SessionManifestParseStatus::IoFailure,
                    E_OUTOFMEMORY,
                    schemaVersion);
            }
            catch (...)
            {
                return ParseResult(
                    SessionManifestParseStatus::SemanticInvalid,
                    HRESULT_FROM_WIN32(ERROR_INVALID_DATA),
                    schemaVersion);
            }

            manifest = std::move(candidate);
            return ParseResult(
                SessionManifestParseStatus::Valid, S_OK, schemaVersion);
        }

        HRESULT ReadManifestFile(
            const std::filesystem::path& path,
            SessionManifest& manifest) noexcept
        {
            SessionManifest candidate{};
            const auto result = ParseManifestFile(path, candidate);
            if (result.status == SessionManifestParseStatus::Valid)
            {
                manifest = std::move(candidate);
            }
            return CompatibilityHResult(result);
        }

        std::filesystem::path UniqueTemporaryPath(
            const std::filesystem::path& directory)
        {
            static std::atomic<std::uint64_t> sequence{};
            return directory /
                (L"manifest." + std::to_wstring(GetCurrentProcessId()) + L"." +
                    std::to_wstring(GetTickCount64()) + L"." +
                    std::to_wstring(sequence.fetch_add(1)) + L".tmp");
        }

        SessionManifestCompareExchangeResult CompareExchangeResult(
            const SessionManifestCompareExchangeStatus status,
            const HRESULT hresult,
            const std::uint64_t expectedRevision,
            const std::optional<std::uint64_t> observedRevision =
                std::nullopt,
            const SessionManifestSemanticIssue semanticIssue =
                SessionManifestSemanticIssue::None) noexcept
        {
            SessionManifestCompareExchangeResult result{};
            result.status = status;
            result.diagnosticHResult = hresult;
            result.expectedRevision = expectedRevision;
            result.observedRevision = observedRevision;
            result.semanticIssue = semanticIssue;
            return result;
        }

        SessionManifestCompareExchangeStatus CompareExchangeStatusFromParse(
            const SessionManifestParseStatus status) noexcept
        {
            switch (status)
            {
            case SessionManifestParseStatus::NotFound:
                return SessionManifestCompareExchangeStatus::NotFound;
            case SessionManifestParseStatus::Inaccessible:
                return SessionManifestCompareExchangeStatus::Inaccessible;
            case SessionManifestParseStatus::UnsupportedSchema:
            case SessionManifestParseStatus::UnknownOrFutureState:
                return SessionManifestCompareExchangeStatus::UnsupportedSchema;
            case SessionManifestParseStatus::MalformedJson:
                return SessionManifestCompareExchangeStatus::MalformedManifest;
            case SessionManifestParseStatus::SemanticInvalid:
                return SessionManifestCompareExchangeStatus::SemanticInvalid;
            case SessionManifestParseStatus::IoFailure:
                return SessionManifestCompareExchangeStatus::IoFailure;
            default:
                return SessionManifestCompareExchangeStatus::IoFailure;
            }
        }

        HRESULT WriteManifestAtomicallyUnderHeldLock(
            const SessionManifest& manifest,
            const std::filesystem::path& managedOutputRoot,
            const std::wstring& sessionId,
            const std::filesystem::path& sessionDirectory,
            const std::filesystem::path& manifestPath,
            const bool createOnly)
        {
            std::string bytes;
            if (!WideToUtf8(SerializeWide(manifest), bytes))
            {
                return HRESULT_FROM_WIN32(ERROR_NO_UNICODE_TRANSLATION);
            }
            const auto temporaryPath = UniqueTemporaryPath(sessionDirectory);
            TemporaryFileCleanup temporaryCleanup(temporaryPath);
            auto result = WriteAllBytesAndFlush(temporaryPath, bytes);
            if (FAILED(result)) return result;

            SessionManifest verified{};
            result = ReadManifestFile(temporaryPath, verified);
            if (FAILED(result)) return result;
            result = ValidateManifest(
                verified, managedOutputRoot, sessionId);
            if (FAILED(result) ||
                SerializeWide(verified) != SerializeWide(manifest))
            {
                return FAILED(result)
                    ? result
                    : HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            }

            if (createOnly)
            {
                if (!MoveFileExW(
                        temporaryPath.c_str(),
                        manifestPath.c_str(),
                        MOVEFILE_WRITE_THROUGH))
                {
                    return HRESULT_FROM_WIN32(GetLastError());
                }
                return S_OK;
            }

            if (!ReplaceFileW(
                    manifestPath.c_str(),
                    temporaryPath.c_str(),
                    nullptr,
                    REPLACEFILE_IGNORE_MERGE_ERRORS,
                    nullptr,
                    nullptr))
            {
                return HRESULT_FROM_WIN32(GetLastError());
            }
            return S_OK;
        }
    }

    SessionManifestStore::SessionManifestStore(
        std::filesystem::path managedOutputRoot,
        std::wstring sessionId)
        : managedOutputRoot_(std::move(managedOutputRoot)),
          sessionId_(std::move(sessionId))
    {
        const auto roots = ResolveRecordingOutputRootsFromManagedRoot(
            managedOutputRoot_);
        if (!roots.Succeeded())
        {
            initializationHResult_ = roots.hresult;
            return;
        }

        std::wstring canonicalSessionId;
        const auto identityResult = NormalizeRecordingSessionId(
            sessionId_, canonicalSessionId);
        if (FAILED(identityResult))
        {
            initializationHResult_ = identityResult;
            return;
        }

        managedOutputRoot_ = roots.mediaOutputRoot;
        sessionsRoot_ = roots.sessionsRoot;
        sessionId_ = std::move(canonicalSessionId);
        sessionDirectory_ = sessionsRoot_ / sessionId_;
        manifestPath_ = sessionDirectory_ / L"manifest.json";
        initializationHResult_ = S_OK;
    }

    const std::filesystem::path& SessionManifestStore::ManagedOutputRoot()
        const noexcept
    {
        return managedOutputRoot_;
    }

    const std::filesystem::path& SessionManifestStore::SessionsRoot()
        const noexcept
    {
        return sessionsRoot_;
    }

    const std::filesystem::path& SessionManifestStore::SessionDirectory()
        const noexcept
    {
        return sessionDirectory_;
    }

    const std::filesystem::path& SessionManifestStore::ManifestPath()
        const noexcept
    {
        return manifestPath_;
    }

    HRESULT SessionManifestStore::CreateManifest(
        SessionManifest& manifest) noexcept
    {
        try
        {
            if (FAILED(initializationHResult_))
            {
                return initializationHResult_;
            }
            if (manifest.revision != 0 ||
                manifest.sessionId != sessionId_ ||
                !IsCanonicalRecordingSessionId(manifest.sessionId))
            {
                return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            }
            auto candidate = manifest;
            candidate.schemaVersion = SessionManifestSchemaVersion;
            candidate.revision = 1;
            const auto now = UtcNowText();
            candidate.createdAtUtc = now;
            candidate.updatedAtUtc = now;
            const auto validation = ValidateManifest(
                candidate, managedOutputRoot_, sessionId_);
            if (FAILED(validation)) return validation;
            const auto result = SaveAtomic(candidate, std::nullopt);
            if (SUCCEEDED(result)) manifest = std::move(candidate);
            return result;
        }
        catch (const std::bad_alloc&)
        {
            return E_OUTOFMEMORY;
        }
        catch (...)
        {
            return E_UNEXPECTED;
        }
    }

    HRESULT SessionManifestStore::UpdateManifest(
        SessionManifest& manifest) noexcept
    {
        try
        {
            if (FAILED(initializationHResult_))
            {
                return initializationHResult_;
            }
            if (!IsCanonicalRecordingSessionId(manifest.sessionId))
            {
                return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            }
            SessionManifest current{};
            auto result = LoadManifest(current);
            if (FAILED(result)) return result;
            if (manifest.revision != current.revision)
            {
                return HRESULT_FROM_WIN32(ERROR_REVISION_MISMATCH);
            }
            result = ValidateUpdate(current, manifest);
            if (FAILED(result)) return result;
            if (current.revision >= MaximumExactJsonInteger)
            {
                return HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
            }
            auto candidate = manifest;
            candidate.revision = current.revision + 1;
            candidate.createdAtUtc = current.createdAtUtc;
            candidate.updatedAtUtc = UtcNowText();
            result = ValidateManifest(candidate, managedOutputRoot_, sessionId_);
            if (FAILED(result)) return result;
            result = SaveAtomic(candidate, current.revision);
            if (SUCCEEDED(result)) manifest = std::move(candidate);
            return result;
        }
        catch (const std::bad_alloc&)
        {
            return E_OUTOFMEMORY;
        }
        catch (...)
        {
            return E_UNEXPECTED;
        }
    }

    SessionManifestParseResult SessionManifestStore::ParseManifest(
        SessionManifest& manifest) const noexcept
    {
        if (FAILED(initializationHResult_))
        {
            return ParseResult(
                initializationHResult_ == E_OUTOFMEMORY
                    ? SessionManifestParseStatus::IoFailure
                    : SessionManifestParseStatus::SemanticInvalid,
                initializationHResult_);
        }
        SessionManifest candidate{};
        auto result = ParseManifestFile(manifestPath_, candidate);
        if (result.status != SessionManifestParseStatus::Valid)
        {
            return result;
        }
        SessionManifestSemanticIssue semanticIssue{};
        const auto validation = ValidateManifest(
            candidate, managedOutputRoot_, sessionId_, &semanticIssue);
        if (FAILED(validation))
        {
            return ParseResult(
                validation == E_OUTOFMEMORY
                    ? SessionManifestParseStatus::IoFailure
                    : SessionManifestParseStatus::SemanticInvalid,
                validation,
                result.observedSchemaVersion,
                semanticIssue);
        }
        std::wstring canonicalSessionId;
        const auto identityResult = NormalizeRecordingSessionId(
            candidate.sessionId, canonicalSessionId);
        if (FAILED(identityResult))
        {
            return ParseResult(
                identityResult == E_OUTOFMEMORY
                    ? SessionManifestParseStatus::IoFailure
                    : SessionManifestParseStatus::SemanticInvalid,
                identityResult,
                result.observedSchemaVersion);
        }
        candidate.sessionId = std::move(canonicalSessionId);
        manifest = std::move(candidate);
        return result;
    }

    HRESULT SessionManifestStore::LoadManifest(
        SessionManifest& manifest) const noexcept
    {
        return CompatibilityHResult(ParseManifest(manifest));
    }

    SessionManifestWriteTransaction::~SessionManifestWriteTransaction()
    {
        Reset();
    }

    SessionManifestWriteTransaction::SessionManifestWriteTransaction(
        SessionManifestWriteTransaction&& other) noexcept
        : lockHandle_(std::exchange(
              other.lockHandle_, INVALID_HANDLE_VALUE)),
          lockPath_(std::move(other.lockPath_)),
          managedOutputRoot_(std::move(other.managedOutputRoot_)),
          sessionDirectory_(std::move(other.sessionDirectory_)),
          manifestPath_(std::move(other.manifestPath_)),
          sessionId_(std::move(other.sessionId_)),
          current_(std::move(other.current_)),
          expectedRevision_(other.expectedRevision_)
    {
        other.expectedRevision_ = 0;
    }

    SessionManifestWriteTransaction&
        SessionManifestWriteTransaction::operator=(
            SessionManifestWriteTransaction&& other) noexcept
    {
        if (this != &other)
        {
            Reset();
            lockHandle_ = std::exchange(
                other.lockHandle_, INVALID_HANDLE_VALUE);
            lockPath_ = std::move(other.lockPath_);
            managedOutputRoot_ = std::move(other.managedOutputRoot_);
            sessionDirectory_ = std::move(other.sessionDirectory_);
            manifestPath_ = std::move(other.manifestPath_);
            sessionId_ = std::move(other.sessionId_);
            current_ = std::move(other.current_);
            expectedRevision_ = other.expectedRevision_;
            other.expectedRevision_ = 0;
        }
        return *this;
    }

    bool SessionManifestWriteTransaction::Active() const noexcept
    {
        return lockHandle_ != nullptr && lockHandle_ != INVALID_HANDLE_VALUE;
    }

    std::uint64_t
        SessionManifestWriteTransaction::ExpectedRevision() const noexcept
    {
        return expectedRevision_;
    }

    const SessionManifest&
        SessionManifestWriteTransaction::CurrentManifest() const noexcept
    {
        return current_;
    }

    void SessionManifestWriteTransaction::Reset() noexcept
    {
        if (Active()) (void)CloseHandle(lockHandle_);
        lockHandle_ = INVALID_HANDLE_VALUE;
        // The rendezvous file persists. Deleting by name after closing would
        // create a close/delete replacement race against the next writer.
        lockPath_.clear();
        managedOutputRoot_.clear();
        sessionDirectory_.clear();
        manifestPath_.clear();
        sessionId_.clear();
        current_ = {};
        expectedRevision_ = 0;
    }

    SessionManifestCompareExchangeResult
        SessionManifestWriteTransaction::CompareExchange(
            SessionManifest& manifest) noexcept
    {
        return CompareExchangeImpl(manifest, false);
    }

    SessionManifestCompareExchangeResult
        SessionManifestWriteTransaction::
            CompareExchangeNarrowReconciliation(
                SessionManifest& manifest) noexcept
    {
        return CompareExchangeImpl(manifest, true);
    }

    SessionManifestCompareExchangeResult
        SessionManifestWriteTransaction::CompareExchangeImpl(
            SessionManifest& manifest,
            const bool narrowReconciliation) noexcept
    {
        const auto finish = [this](
            const SessionManifestCompareExchangeResult& result) noexcept
        {
            Reset();
            return result;
        };
        try
        {
            if (!Active())
            {
                return CompareExchangeResult(
                    SessionManifestCompareExchangeStatus::Inactive,
                    HRESULT_FROM_WIN32(ERROR_INVALID_STATE),
                    expectedRevision_);
            }
            const auto observed = current_.revision;
            if (observed != expectedRevision_ ||
                (!narrowReconciliation &&
                    manifest.revision != expectedRevision_) ||
                (narrowReconciliation &&
                    (expectedRevision_ >= MaximumExactJsonInteger ||
                        manifest.revision != expectedRevision_ + 1)))
            {
                return finish(CompareExchangeResult(
                    SessionManifestCompareExchangeStatus::RevisionMismatch,
                    HRESULT_FROM_WIN32(ERROR_REVISION_MISMATCH),
                    expectedRevision_, observed));
            }
            SessionManifest diskCurrent{};
            auto parsed = ParseManifestFile(manifestPath_, diskCurrent);
            if (parsed.status != SessionManifestParseStatus::Valid)
            {
                return finish(CompareExchangeResult(
                    CompareExchangeStatusFromParse(parsed.status),
                    parsed.diagnosticHResult,
                    expectedRevision_));
            }
            SessionManifestSemanticIssue semanticIssue{};
            auto result = ValidateManifest(
                diskCurrent,
                managedOutputRoot_,
                sessionId_,
                &semanticIssue);
            if (FAILED(result))
            {
                return finish(CompareExchangeResult(
                    result == E_OUTOFMEMORY
                        ? SessionManifestCompareExchangeStatus::IoFailure
                        : SessionManifestCompareExchangeStatus::SemanticInvalid,
                    result,
                    expectedRevision_,
                    diskCurrent.revision,
                    semanticIssue));
            }
            std::wstring canonicalDiskSessionId;
            result = NormalizeRecordingSessionId(
                diskCurrent.sessionId, canonicalDiskSessionId);
            if (FAILED(result))
            {
                return finish(CompareExchangeResult(
                    result == E_OUTOFMEMORY
                        ? SessionManifestCompareExchangeStatus::IoFailure
                        : SessionManifestCompareExchangeStatus::SemanticInvalid,
                    result,
                    expectedRevision_,
                    diskCurrent.revision));
            }
            diskCurrent.sessionId = std::move(canonicalDiskSessionId);
            if (diskCurrent.revision != expectedRevision_)
            {
                return finish(CompareExchangeResult(
                    SessionManifestCompareExchangeStatus::RevisionMismatch,
                    HRESULT_FROM_WIN32(ERROR_REVISION_MISMATCH),
                    expectedRevision_,
                    diskCurrent.revision));
            }
            if (SerializeWide(diskCurrent) != SerializeWide(current_))
            {
                return finish(CompareExchangeResult(
                    SessionManifestCompareExchangeStatus::ConcurrentChange,
                    HRESULT_FROM_WIN32(ERROR_RETRY),
                    expectedRevision_,
                    diskCurrent.revision));
            }
            if (!IsCanonicalRecordingSessionId(manifest.sessionId) ||
                manifest.sessionId != sessionId_ ||
                (!narrowReconciliation &&
                    manifest.schemaVersion != current_.schemaVersion) ||
                (narrowReconciliation &&
                    (current_.schemaVersion != SessionManifestSchemaVersion ||
                        manifest.schemaVersion !=
                            SessionManifestReconciledSchemaVersion)))
            {
                return finish(CompareExchangeResult(
                    SessionManifestCompareExchangeStatus::InvalidInput,
                    HRESULT_FROM_WIN32(ERROR_INVALID_DATA),
                    expectedRevision_, observed));
            }
            if (narrowReconciliation)
            {
                const auto whitelist =
                    ValidateNarrowReconciliationMutation(
                        diskCurrent, manifest);
                result = whitelist.diagnosticHResult;
                if (!whitelist.Valid())
                {
                    return finish(CompareExchangeResult(
                        SessionManifestCompareExchangeStatus::
                            SemanticInvalid,
                        result, expectedRevision_, observed));
                }
            }
            else
            {
                result = ValidateUpdate(diskCurrent, manifest);
                if (FAILED(result))
                {
                    return finish(CompareExchangeResult(
                        SessionManifestCompareExchangeStatus::
                            SemanticInvalid,
                        result, expectedRevision_, observed));
                }
            }
            if (expectedRevision_ >= MaximumExactJsonInteger)
            {
                return finish(CompareExchangeResult(
                    SessionManifestCompareExchangeStatus::InvalidInput,
                    HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW),
                    expectedRevision_, observed));
            }
            auto candidate = manifest;
            if (!narrowReconciliation)
            {
                candidate.revision = expectedRevision_ + 1;
                candidate.createdAtUtc = current_.createdAtUtc;
                candidate.updatedAtUtc = UtcNowText();
            }
            result = ValidateManifest(
                candidate, managedOutputRoot_, sessionId_);
            if (FAILED(result))
            {
                return finish(CompareExchangeResult(
                    SessionManifestCompareExchangeStatus::SemanticInvalid,
                    result, expectedRevision_, observed));
            }
            result = WriteManifestAtomicallyUnderHeldLock(
                candidate,
                managedOutputRoot_,
                sessionId_,
                sessionDirectory_,
                manifestPath_,
                false);
            if (FAILED(result))
            {
                return finish(CompareExchangeResult(
                    SessionManifestCompareExchangeStatus::AtomicWriteFailure,
                    result, expectedRevision_, observed));
            }
            manifest = std::move(candidate);
            return finish(CompareExchangeResult(
                SessionManifestCompareExchangeStatus::Succeeded,
                S_OK, expectedRevision_, manifest.revision));
        }
        catch (const std::bad_alloc&)
        {
            return finish(CompareExchangeResult(
                SessionManifestCompareExchangeStatus::IoFailure,
                E_OUTOFMEMORY, expectedRevision_));
        }
        catch (...)
        {
            return finish(CompareExchangeResult(
                SessionManifestCompareExchangeStatus::IoFailure,
                E_UNEXPECTED, expectedRevision_));
        }
    }

    SessionManifestCompareExchangeResult
        SessionManifestStore::BeginExpectedRevisionTransaction(
            const std::uint64_t expectedRevision,
            SessionManifestWriteTransaction& transaction) const noexcept
    {
        transaction.Reset();
        try
        {
            if (FAILED(initializationHResult_) || expectedRevision == 0 ||
                expectedRevision >= MaximumExactJsonInteger)
            {
                return CompareExchangeResult(
                    SessionManifestCompareExchangeStatus::InvalidInput,
                    FAILED(initializationHResult_)
                        ? initializationHResult_
                        : E_INVALIDARG,
                    expectedRevision);
            }

            SessionManifestWriteTransaction candidate{};
            candidate.lockPath_ =
                sessionDirectory_ / L"manifest.write.lock";
            candidate.managedOutputRoot_ = managedOutputRoot_;
            candidate.sessionDirectory_ = sessionDirectory_;
            candidate.manifestPath_ = manifestPath_;
            candidate.sessionId_ = sessionId_;
            candidate.expectedRevision_ = expectedRevision;

            SetLastError(ERROR_SUCCESS);
            candidate.lockHandle_ = CreateFileW(
                candidate.lockPath_.c_str(),
                GENERIC_READ | GENERIC_WRITE,
                0,
                nullptr,
                OPEN_ALWAYS,
                FILE_ATTRIBUTE_HIDDEN | FILE_FLAG_OPEN_REPARSE_POINT,
                nullptr);
            if (!candidate.Active())
            {
                const auto error = GetLastError();
                const auto status = error == ERROR_SHARING_VIOLATION ||
                        error == ERROR_LOCK_VIOLATION
                    ? SessionManifestCompareExchangeStatus::ConcurrentChange
                    : error == ERROR_FILE_NOT_FOUND ||
                            error == ERROR_PATH_NOT_FOUND
                        ? SessionManifestCompareExchangeStatus::NotFound
                        : error == ERROR_ACCESS_DENIED ||
                                error == ERROR_PRIVILEGE_NOT_HELD ||
                                error == ERROR_NETWORK_ACCESS_DENIED
                            ? SessionManifestCompareExchangeStatus::Inaccessible
                            : SessionManifestCompareExchangeStatus::IoFailure;
                return CompareExchangeResult(
                    status, HRESULT_FROM_WIN32(error), expectedRevision);
            }
            FILE_ATTRIBUTE_TAG_INFO lockAttributes{};
            if (!GetFileInformationByHandleEx(
                    candidate.lockHandle_,
                    FileAttributeTagInfo,
                    &lockAttributes,
                    sizeof(lockAttributes)))
            {
                const auto error = GetLastError();
                return CompareExchangeResult(
                    SessionManifestCompareExchangeStatus::Inaccessible,
                    HRESULT_FROM_WIN32(error == ERROR_SUCCESS
                        ? ERROR_GEN_FAILURE
                        : error),
                    expectedRevision);
            }
            if ((lockAttributes.FileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0 ||
                (lockAttributes.FileAttributes &
                    FILE_ATTRIBUTE_REPARSE_POINT) != 0)
            {
                return CompareExchangeResult(
                    SessionManifestCompareExchangeStatus::Inaccessible,
                    HRESULT_FROM_WIN32(ERROR_REPARSE_TAG_INVALID),
                    expectedRevision);
            }

            SessionManifest current{};
            const auto parsed = ParseManifestFile(manifestPath_, current);
            if (parsed.status != SessionManifestParseStatus::Valid)
            {
                return CompareExchangeResult(
                    CompareExchangeStatusFromParse(parsed.status),
                    parsed.diagnosticHResult,
                    expectedRevision);
            }
            SessionManifestSemanticIssue semanticIssue{};
            auto result = ValidateManifest(
                current, managedOutputRoot_, sessionId_, &semanticIssue);
            if (FAILED(result))
            {
                return CompareExchangeResult(
                    result == E_OUTOFMEMORY
                        ? SessionManifestCompareExchangeStatus::IoFailure
                        : SessionManifestCompareExchangeStatus::SemanticInvalid,
                    result,
                    expectedRevision,
                    current.revision,
                    semanticIssue);
            }
            std::wstring canonicalSessionId;
            result = NormalizeRecordingSessionId(
                current.sessionId, canonicalSessionId);
            if (FAILED(result) || canonicalSessionId != sessionId_)
            {
                return CompareExchangeResult(
                    result == E_OUTOFMEMORY
                        ? SessionManifestCompareExchangeStatus::IoFailure
                        : SessionManifestCompareExchangeStatus::SemanticInvalid,
                    FAILED(result)
                        ? result
                        : HRESULT_FROM_WIN32(ERROR_INVALID_DATA),
                    expectedRevision,
                    current.revision);
            }
            current.sessionId = std::move(canonicalSessionId);
            if (current.revision != expectedRevision)
            {
                return CompareExchangeResult(
                    SessionManifestCompareExchangeStatus::RevisionMismatch,
                    HRESULT_FROM_WIN32(ERROR_REVISION_MISMATCH),
                    expectedRevision,
                    current.revision);
            }
            candidate.current_ = std::move(current);
            transaction = std::move(candidate);
            return CompareExchangeResult(
                SessionManifestCompareExchangeStatus::Ready,
                S_OK,
                expectedRevision,
                transaction.CurrentManifest().revision);
        }
        catch (const std::bad_alloc&)
        {
            return CompareExchangeResult(
                SessionManifestCompareExchangeStatus::IoFailure,
                E_OUTOFMEMORY,
                expectedRevision);
        }
        catch (...)
        {
            return CompareExchangeResult(
                SessionManifestCompareExchangeStatus::IoFailure,
                E_UNEXPECTED,
                expectedRevision);
        }
    }

    HRESULT SessionManifestStore::SaveAtomic(
        const SessionManifest& manifest,
        const std::optional<std::uint64_t> expectedRevision) noexcept
    {
        try
        {
            if (FAILED(initializationHResult_))
            {
                return initializationHResult_;
            }
            if (!IsCanonicalRecordingSessionId(manifest.sessionId))
            {
                return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            }
            const auto validation = ValidateManifest(
                manifest, managedOutputRoot_, sessionId_);
            if (FAILED(validation)) return validation;
            if ((!expectedRevision.has_value() && manifest.revision != 1) ||
                (expectedRevision.has_value() &&
                    (*expectedRevision >= MaximumExactJsonInteger ||
                        manifest.revision != *expectedRevision + 1)))
            {
                return HRESULT_FROM_WIN32(ERROR_REVISION_MISMATCH);
            }

            std::error_code directoryError;
            std::filesystem::create_directories(
                sessionDirectory_, directoryError);
            if (directoryError)
            {
                return HRESULT_FROM_WIN32(
                    static_cast<DWORD>(directoryError.value()));
            }

            ManifestWriteLock writeLock(
                sessionDirectory_ / L"manifest.write.lock");
            if (!writeLock.Acquired())
            {
                return writeLock.HResult();
            }

            const auto attributes = GetFileAttributesW(manifestPath_.c_str());
            if (!expectedRevision.has_value())
            {
                if (attributes != INVALID_FILE_ATTRIBUTES)
                {
                    return HRESULT_FROM_WIN32(ERROR_FILE_EXISTS);
                }
                const auto missingError = GetLastError();
                if (missingError != ERROR_FILE_NOT_FOUND &&
                    missingError != ERROR_PATH_NOT_FOUND)
                {
                    return HRESULT_FROM_WIN32(missingError);
                }
            }
            else
            {
                if (attributes == INVALID_FILE_ATTRIBUTES)
                {
                    return HRESULT_FROM_WIN32(GetLastError());
                }
                SessionManifest current{};
                auto result = ReadManifestFile(manifestPath_, current);
                if (FAILED(result)) return result;
                result = ValidateManifest(
                    current, managedOutputRoot_, sessionId_);
                if (FAILED(result)) return result;
                if (current.revision != *expectedRevision)
                {
                    return HRESULT_FROM_WIN32(ERROR_REVISION_MISMATCH);
                }
                result = ValidateUpdate(current, manifest);
                if (FAILED(result)) return result;
            }

            return WriteManifestAtomicallyUnderHeldLock(
                manifest,
                managedOutputRoot_,
                sessionId_,
                sessionDirectory_,
                manifestPath_,
                !expectedRevision.has_value());
        }
        catch (const std::bad_alloc&)
        {
            return E_OUTOFMEMORY;
        }
        catch (...)
        {
            return E_UNEXPECTED;
        }
    }
}
