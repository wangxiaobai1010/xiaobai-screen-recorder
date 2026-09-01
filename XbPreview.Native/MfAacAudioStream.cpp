#include "MfAacAudioStream.h"

#include <mfapi.h>
#include <mferror.h>
#include <winrt/base.h>

#include <cstring>
#include <limits>
#include <new>
#include <vector>

namespace xbpreview
{
    HRESULT MfAacAudioStream::Configure(
        IMFSinkWriter* const writer,
        DWORD& streamIndex) noexcept
    {
        streamIndex = 0;
        if (writer == nullptr)
        {
            return E_POINTER;
        }

        try
        {
            // ScreenRecorderLib v6.6.0 OutputManager::ConfigureOutputMediaTypes
            // configures this exact AAC output shape.
            winrt::com_ptr<IMFMediaType> outputType;
            winrt::check_hresult(MFCreateMediaType(outputType.put()));
            winrt::check_hresult(outputType->SetGUID(
                MF_MT_MAJOR_TYPE, MFMediaType_Audio));
            winrt::check_hresult(outputType->SetGUID(
                MF_MT_SUBTYPE, MFAudioFormat_AAC));
            winrt::check_hresult(outputType->SetUINT32(
                MF_MT_AUDIO_NUM_CHANNELS, MfAacAudioChannelCount));
            winrt::check_hresult(outputType->SetUINT32(
                MF_MT_AUDIO_BITS_PER_SAMPLE, MfAacAudioBitsPerSample));
            winrt::check_hresult(outputType->SetUINT32(
                MF_MT_AUDIO_SAMPLES_PER_SECOND,
                MfAacAudioSamplesPerSecond));
            winrt::check_hresult(outputType->SetUINT32(
                MF_MT_AUDIO_AVG_BYTES_PER_SECOND,
                MfAacAudioAverageBytesPerSecond));
            winrt::check_hresult(writer->AddStream(
                outputType.get(), &streamIndex));

            // ScreenRecorderLib v6.6.0 OutputManager::ConfigureInputMediaTypes
            // supplies PCM16/48 kHz/stereo to the Sink Writer.
            winrt::com_ptr<IMFMediaType> inputType;
            winrt::check_hresult(MFCreateMediaType(inputType.put()));
            winrt::check_hresult(inputType->SetGUID(
                MF_MT_MAJOR_TYPE, MFMediaType_Audio));
            winrt::check_hresult(inputType->SetGUID(
                MF_MT_SUBTYPE, MFAudioFormat_PCM));
            winrt::check_hresult(inputType->SetUINT32(
                MF_MT_AUDIO_BITS_PER_SAMPLE, MfAacAudioBitsPerSample));
            winrt::check_hresult(inputType->SetUINT32(
                MF_MT_AUDIO_SAMPLES_PER_SECOND,
                MfAacAudioSamplesPerSecond));
            winrt::check_hresult(inputType->SetUINT32(
                MF_MT_AUDIO_NUM_CHANNELS, MfAacAudioChannelCount));
            winrt::check_hresult(writer->SetInputMediaType(
                streamIndex, inputType.get(), nullptr));
            return S_OK;
        }
        catch (const winrt::hresult_error& error)
        {
            return error.code();
        }
        catch (...)
        {
            return E_FAIL;
        }
    }

    HRESULT MfAacAudioStream::WritePcm(
        IMFSinkWriter* const writer,
        const DWORD streamIndex,
        const std::vector<BYTE>& bytes,
        const std::int64_t sampleTime100ns,
        const std::int64_t sampleDuration100ns) noexcept
    {
        if (writer == nullptr || sampleTime100ns < 0 ||
            sampleDuration100ns <= 0)
        {
            return E_INVALIDARG;
        }

        // Preserve ScreenRecorderLib v6.6.0 OutputManager::RenderFrame's
        // empty-frame policy: a first/continued empty frame is padded, while
        // the single empty frame immediately following real audio is skipped
        // to avoid inserting a glitch between short donor frames.
        const std::vector<BYTE>* sampleBytes = &bytes;
        std::vector<BYTE> padding;
        if (bytes.empty())
        {
            if (lastFrameHadAudio_)
            {
                lastFrameHadAudio_ = false;
                return S_FALSE;
            }

            constexpr std::uint64_t HundredNanosecondsPerSecond = 10'000'000;
            const auto requested = static_cast<std::uint64_t>(
                sampleDuration100ns);
            if (requested >
                ((std::numeric_limits<std::uint64_t>::max)() -
                    (HundredNanosecondsPerSecond - 1)) /
                    MfAacAudioSamplesPerSecond)
            {
                return E_INVALIDARG;
            }
            const auto frameCount =
                (requested * MfAacAudioSamplesPerSecond +
                    HundredNanosecondsPerSecond - 1) /
                HundredNanosecondsPerSecond;
            constexpr std::uint64_t BytesPerFrame =
                MfAacAudioChannelCount * (MfAacAudioBitsPerSample / 8);
            if (frameCount == 0 ||
                frameCount >
                    (std::numeric_limits<DWORD>::max)() / BytesPerFrame)
            {
                return E_INVALIDARG;
            }
            try
            {
                padding.resize(static_cast<std::size_t>(
                    frameCount * BytesPerFrame));
            }
            catch (const std::bad_alloc&)
            {
                return E_OUTOFMEMORY;
            }
            catch (...)
            {
                return E_FAIL;
            }
            sampleBytes = &padding;
            lastFrameHadAudio_ = false;
        }
        else
        {
            lastFrameHadAudio_ = true;
        }

        constexpr std::uint64_t BytesPerFrame =
            MfAacAudioChannelCount * (MfAacAudioBitsPerSample / 8);
        if (sampleBytes->size() >
                static_cast<std::size_t>((std::numeric_limits<DWORD>::max)()) ||
            sampleBytes->size() % BytesPerFrame != 0)
        {
            return E_INVALIDARG;
        }
        const auto frameCount = sampleBytes->size() / BytesPerFrame;
        constexpr std::uint64_t HundredNanosecondsPerSecond = 10'000'000;
        if (frameCount == 0 ||
            frameCount >
                (std::numeric_limits<std::uint64_t>::max)() /
                    HundredNanosecondsPerSecond)
        {
            return E_INVALIDARG;
        }
        const auto actualDuration100ns = static_cast<std::int64_t>(
            (frameCount * HundredNanosecondsPerSecond) /
                MfAacAudioSamplesPerSecond);
        if (actualDuration100ns <= 0)
        {
            return E_INVALIDARG;
        }
        return WriteSample(
            writer,
            streamIndex,
            sampleBytes->data(),
            static_cast<DWORD>(sampleBytes->size()),
            sampleTime100ns,
            actualDuration100ns);
    }

    HRESULT MfAacAudioStream::WriteSample(
        IMFSinkWriter* const writer,
        const DWORD streamIndex,
        const BYTE* const bytes,
        const DWORD byteCount,
        const std::int64_t sampleTime100ns,
        const std::int64_t sampleDuration100ns) noexcept
    {
        if (writer == nullptr || bytes == nullptr || byteCount == 0 ||
            sampleTime100ns < 0 || sampleDuration100ns <= 0)
        {
            return E_INVALIDARG;
        }

        try
        {
            // This is the ScreenRecorderLib v6.6.0
            // OutputManager::WriteAudioSamplesToVideo sample handoff, using
            // COM smart pointers for the same MF buffer/sample lifetime.
            winrt::com_ptr<IMFMediaBuffer> buffer;
            auto createBufferResult = MFCreateMemoryBuffer(
                byteCount, buffer.put());
            for (int retry = 0;
                FAILED(createBufferResult) && retry < 100;
                ++retry)
            {
                Sleep(10);
                buffer = nullptr;
                createBufferResult = MFCreateMemoryBuffer(
                    byteCount, buffer.put());
            }
            winrt::check_hresult(createBufferResult);

            BYTE* destination{};
            DWORD maximumLength{};
            winrt::check_hresult(buffer->Lock(
                &destination, &maximumLength, nullptr));
            if (destination == nullptr || maximumLength < byteCount)
            {
                (void)buffer->Unlock();
                return E_UNEXPECTED;
            }
            const auto copyResult = memcpy_s(
                destination, maximumLength, bytes, byteCount);
            const auto setLengthResult = copyResult == 0
                ? buffer->SetCurrentLength(byteCount)
                : E_INVALIDARG;
            const auto unlockResult = buffer->Unlock();
            if (copyResult != 0)
            {
                return E_INVALIDARG;
            }
            winrt::check_hresult(setLengthResult);
            winrt::check_hresult(unlockResult);

            winrt::com_ptr<IMFSample> sample;
            winrt::check_hresult(MFCreateSample(sample.put()));
            winrt::check_hresult(sample->AddBuffer(buffer.get()));
            winrt::check_hresult(sample->SetSampleTime(sampleTime100ns));
            winrt::check_hresult(sample->SetSampleDuration(
                sampleDuration100ns));
            return writer->WriteSample(streamIndex, sample.get());
        }
        catch (const winrt::hresult_error& error)
        {
            return error.code();
        }
        catch (...)
        {
            return E_FAIL;
        }
    }

    HRESULT MfAacAudioStream::ValidateOutput(
        const std::wstring& outputPath,
        const std::uint32_t sampleLimit,
        const bool requireEndOfStream) noexcept
    {
        if (outputPath.empty())
        {
            return E_INVALIDARG;
        }

        try
        {
            winrt::com_ptr<IMFSourceReader> reader;
            winrt::check_hresult(MFCreateSourceReaderFromURL(
                outputPath.c_str(), nullptr, reader.put()));
            winrt::check_hresult(reader->SetStreamSelection(
                static_cast<DWORD>(MF_SOURCE_READER_ALL_STREAMS), FALSE));
            winrt::check_hresult(reader->SetStreamSelection(
                static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM),
                TRUE));

            winrt::com_ptr<IMFMediaType> decodedType;
            winrt::check_hresult(MFCreateMediaType(decodedType.put()));
            winrt::check_hresult(decodedType->SetGUID(
                MF_MT_MAJOR_TYPE, MFMediaType_Audio));
            winrt::check_hresult(decodedType->SetGUID(
                MF_MT_SUBTYPE, MFAudioFormat_PCM));
            winrt::check_hresult(reader->SetCurrentMediaType(
                static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM),
                nullptr,
                decodedType.get()));
            winrt::com_ptr<IMFMediaType> activeType;
            winrt::check_hresult(reader->GetCurrentMediaType(
                static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM),
                activeType.put()));
            GUID actualSubtype{};
            UINT32 actualBitsPerSample{};
            UINT32 actualSamplesPerSecond{};
            UINT32 actualChannels{};
            winrt::check_hresult(activeType->GetGUID(
                MF_MT_SUBTYPE, &actualSubtype));
            winrt::check_hresult(activeType->GetUINT32(
                MF_MT_AUDIO_BITS_PER_SAMPLE, &actualBitsPerSample));
            winrt::check_hresult(activeType->GetUINT32(
                MF_MT_AUDIO_SAMPLES_PER_SECOND, &actualSamplesPerSecond));
            winrt::check_hresult(activeType->GetUINT32(
                MF_MT_AUDIO_NUM_CHANNELS, &actualChannels));
            if (actualSubtype != MFAudioFormat_PCM ||
                actualBitsPerSample != MfAacAudioBitsPerSample ||
                actualSamplesPerSecond != MfAacAudioSamplesPerSecond ||
                actualChannels != MfAacAudioChannelCount)
            {
                return MF_E_INVALIDMEDIATYPE;
            }

            std::uint32_t samplesRead{};
            bool reachedEndOfStream{};
            bool haveTimestamp{};
            LONGLONG lastTimestamp{};
            for (;;)
            {
                DWORD flags{};
                LONGLONG timestamp{};
                winrt::com_ptr<IMFSample> sample;
                winrt::check_hresult(reader->ReadSample(
                    static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM),
                    0,
                    nullptr,
                    &flags,
                    &timestamp,
                    sample.put()));
                if ((flags & MF_SOURCE_READERF_ERROR) != 0)
                {
                    return E_FAIL;
                }
                if ((flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    reachedEndOfStream = true;
                    break;
                }
                if (!sample)
                {
                    continue;
                }
                if (haveTimestamp && timestamp < lastTimestamp)
                {
                    return MF_E_INVALID_TIMESTAMP;
                }

                winrt::com_ptr<IMFMediaBuffer> buffer;
                winrt::check_hresult(sample->ConvertToContiguousBuffer(
                    buffer.put()));
                DWORD currentLength{};
                winrt::check_hresult(buffer->GetCurrentLength(&currentLength));
                if (currentLength == 0)
                {
                    return HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
                }

                haveTimestamp = true;
                lastTimestamp = timestamp;
                ++samplesRead;
                if (sampleLimit != 0 && samplesRead >= sampleLimit)
                {
                    break;
                }
            }

            if (samplesRead == 0 ||
                (requireEndOfStream && !reachedEndOfStream))
            {
                return MF_E_INVALID_TIMESTAMP;
            }
            return S_OK;
        }
        catch (const winrt::hresult_error& error)
        {
            return error.code();
        }
        catch (...)
        {
            return E_FAIL;
        }
    }

    void MfAacAudioStream::Reset() noexcept
    {
        lastFrameHadAudio_ = false;
    }
}
