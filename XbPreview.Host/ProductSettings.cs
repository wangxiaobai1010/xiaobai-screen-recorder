using System.Text.Json;
using System.Text.Json.Serialization;
using XbPreview.Avalonia.Localization;

namespace XbPreview.Host;

internal enum ProductCaptureTargetMode
{
    FullScreen = 0,
    Window = 1,
}

internal enum ProductStageOrientation
{
    Left = 0,
    Front = 1,
    Right = 2,
}

internal enum ProductStageLevel
{
    Level1 = 0,
    Level2 = 1,
    Level3 = 2,
}

internal enum ProductBackgroundPreset
{
    Warm = 0,
    Fantasy01 = 1,
    Fantasy001 = 2,
}

internal enum ProductBackgroundSource
{
    Preset = 0,
    CustomImage = 1,
}

[JsonConverter(typeof(FrameRateModeJsonConverter))]
internal enum FrameRateMode
{
    Fps30 = 30,
    Fps60 = 60,
}

[JsonConverter(typeof(RecordingResolutionModeJsonConverter))]
internal enum RecordingResolutionMode
{
    Original = 0,
    Fhd1080 = 1,
    Qhd1440 = 2,
    Uhd2160 = 3,
}

internal sealed class RecordingResolutionModeJsonConverter :
    JsonConverter<RecordingResolutionMode>
{
    public override RecordingResolutionMode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number &&
            reader.TryGetInt32(out int numeric) &&
            Enum.IsDefined((RecordingResolutionMode)numeric))
        {
            return (RecordingResolutionMode)numeric;
        }
        if (reader.TokenType == JsonTokenType.String)
        {
            string? text = reader.GetString();
            if (Enum.TryParse(
                    text,
                    ignoreCase: true,
                    out RecordingResolutionMode parsed) &&
                Enum.IsDefined(parsed))
            {
                return parsed;
            }
            return RecordingResolutionMode.Original;
        }
        reader.Skip();
        return RecordingResolutionMode.Original;
    }

    public override void Write(
        Utf8JsonWriter writer,
        RecordingResolutionMode value,
        JsonSerializerOptions options) => writer.WriteStringValue(
            Enum.IsDefined(value)
                ? value.ToString()
                : nameof(RecordingResolutionMode.Original));
}

internal sealed class FrameRateModeJsonConverter : JsonConverter<FrameRateMode>
{
    public override FrameRateMode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number &&
            reader.TryGetInt32(out int numeric))
        {
            return numeric == (int)FrameRateMode.Fps60
                ? FrameRateMode.Fps60
                : FrameRateMode.Fps30;
        }
        if (reader.TokenType == JsonTokenType.String)
        {
            string? text = reader.GetString();
            return string.Equals(text, nameof(FrameRateMode.Fps60),
                    StringComparison.OrdinalIgnoreCase) || text == "60"
                ? FrameRateMode.Fps60
                : FrameRateMode.Fps30;
        }
        reader.Skip();
        return FrameRateMode.Fps30;
    }

    public override void Write(
        Utf8JsonWriter writer,
        FrameRateMode value,
        JsonSerializerOptions options) => writer.WriteStringValue(
            value == FrameRateMode.Fps60
                ? nameof(FrameRateMode.Fps60)
                : nameof(FrameRateMode.Fps30));
}

// A persisted window identity deliberately excludes HWND and other transient
// machine pointers. A future selector resolves these hints against the current
// safe enumeration before capture starts.
internal sealed record ProductWindowIdentity(
    string ProcessName,
    string WindowTitle);

internal sealed record ProductSettings(
    ProductCaptureTargetMode CaptureTargetMode,
    ProductWindowIdentity? SelectedWindowIdentity,
    MicrophoneSelection MicrophoneSelection,
    bool MicrophoneEnabled,
    bool SystemAudioEnabled,
    bool MouseVisible,
    bool ManualHotkeysEnabled,
    bool AutoDirectorEnabled,
    bool DirectorFinalVideoEnabled,
    ProductStageOrientation StageOrientation,
    ProductStageLevel StageLevel,
    ProductBackgroundSource BackgroundSource,
    ProductBackgroundPreset BackgroundPreset,
    string? CustomBackgroundPath,
    string? OutputRoot,
    FrameRateMode FrameRateMode,
    RecordingResolutionMode RecordingResolutionMode,
    string? UiLanguage,
    string[] RecoveryDismissedSessionIds)
{
    internal static ProductSettings Defaults { get; } = new(
        ProductCaptureTargetMode.FullScreen,
        SelectedWindowIdentity: null,
        MicrophoneSelection.WindowsDefault,
        MicrophoneEnabled: false,
        SystemAudioEnabled: true,
        MouseVisible: true,
        ManualHotkeysEnabled: true,
        AutoDirectorEnabled: false,
        DirectorFinalVideoEnabled: true,
        ProductStageOrientation.Right,
        ProductStageLevel.Level2,
        ProductBackgroundSource.Preset,
        ProductBackgroundPreset.Warm,
        CustomBackgroundPath: null,
        OutputRoot: null,
        FrameRateMode.Fps30,
        RecordingResolutionMode.Original,
        UiLanguage: null,
        RecoveryDismissedSessionIds: []);
}

internal static class ProductPathContract
{
    private static readonly HashSet<string> StaticImageExtensions = new(
        [".png", ".jpg", ".jpeg", ".bmp"],
        StringComparer.OrdinalIgnoreCase);

    internal static bool TryValidateCustomBackground(
        string? path,
        out string validatedPath)
    {
        validatedPath = string.Empty;
        if (!TryNormalizeLocalPath(path, out string candidate) ||
            !File.Exists(candidate) ||
            !StaticImageExtensions.Contains(Path.GetExtension(candidate)))
        {
            return false;
        }
        validatedPath = candidate;
        return true;
    }

    internal static bool TryValidateOutputRoot(
        string? path,
        out string validatedPath)
    {
        validatedPath = string.Empty;
        if (!TryNormalizeLocalPath(path, out string candidate) ||
            !Directory.Exists(candidate))
        {
            return false;
        }
        validatedPath = candidate.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return true;
    }

    private static bool TryNormalizeLocalPath(
        string? path,
        out string validatedPath)
    {
        validatedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path) ||
            path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return false;
        }
        try
        {
            string fullPath = Path.GetFullPath(path);
            string? root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root) ||
                new DriveInfo(root).DriveType == DriveType.Network)
            {
                return false;
            }
            validatedPath = fullPath;
            return true;
        }
        catch (Exception error) when (
            error is ArgumentException or IOException or
                UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}

internal sealed class ProductSettingsStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new FrameRateModeJsonConverter(),
            new RecordingResolutionModeJsonConverter(),
            new JsonStringEnumConverter(),
        },
    };

    private sealed record Document(
        int SchemaVersion,
        ProductSettings Settings);

    private readonly string _path;
    private readonly string? _legacyMicrophonePath;

    internal ProductSettingsStore(
        string? path = null,
        string? legacyMicrophonePath = null)
    {
        _path = path ?? DefaultPath;
        _legacyMicrophonePath = legacyMicrophonePath ??
            MicrophoneSelectionSettings.SettingsPath;
    }

    internal static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XbPreview",
        "settings",
        "product-settings.json");

    internal string Path => _path;

    internal ProductSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return DefaultsWithLegacyMicrophone();
            }
            Document? document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(_path), JsonOptions);
            return document is not null &&
                document.SchemaVersion == CurrentSchemaVersion
                ? Normalize(document.Settings)
                : DefaultsWithLegacyMicrophone();
        }
        catch (Exception error) when (
            error is JsonException or IOException or
                UnauthorizedAccessException or NotSupportedException)
        {
            return DefaultsWithLegacyMicrophone();
        }
    }

    internal void Save(ProductSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? directory = System.IO.Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException(
                "Product settings path has no parent directory.",
                nameof(_path));
        }
        Directory.CreateDirectory(directory);
        string temporary = _path + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(
                new Document(CurrentSchemaVersion, Normalize(settings)),
                JsonOptions));
        File.Move(temporary, _path, overwrite: true);
    }

    private ProductSettings DefaultsWithLegacyMicrophone()
    {
        MicrophoneSelection microphone =
            string.IsNullOrWhiteSpace(_legacyMicrophonePath)
                ? MicrophoneSelection.WindowsDefault
                : MicrophoneSelectionSettings.Load(_legacyMicrophonePath);
        return ProductSettings.Defaults with
        {
            MicrophoneSelection = microphone,
        };
    }

    private static ProductSettings Normalize(ProductSettings? value)
    {
        if (value is null ||
            !Enum.IsDefined(value.CaptureTargetMode) ||
            !Enum.IsDefined(value.StageOrientation) ||
            !Enum.IsDefined(value.StageLevel) ||
            !Enum.IsDefined(value.BackgroundSource) ||
            !Enum.IsDefined(value.BackgroundPreset))
        {
            return ProductSettings.Defaults;
        }

        ProductWindowIdentity? identity = value.SelectedWindowIdentity;
        if (identity is not null &&
            (string.IsNullOrWhiteSpace(identity.ProcessName) ||
                string.IsNullOrWhiteSpace(identity.WindowTitle) ||
                identity.ProcessName.Length > 260 ||
                identity.WindowTitle.Length > 1024))
        {
            identity = null;
        }
        MicrophoneSelection microphone = value.MicrophoneSelection is
            { Kind: MicrophoneSelectionKind.ConcreteEndpoint } selected &&
            !string.IsNullOrWhiteSpace(selected.EndpointId)
                ? selected
                : MicrophoneSelection.WindowsDefault;
        string? custom = ProductPathContract.TryValidateCustomBackground(
            value.CustomBackgroundPath, out string validatedCustom)
                ? validatedCustom
                : null;
        string? output = ProductPathContract.TryValidateOutputRoot(
            value.OutputRoot, out string validatedOutput)
                ? validatedOutput
                : null;
        bool missingCustom = value.BackgroundSource ==
            ProductBackgroundSource.CustomImage && custom is null;
        ProductBackgroundSource source =
            value.BackgroundSource == ProductBackgroundSource.CustomImage &&
            custom is not null
                ? ProductBackgroundSource.CustomImage
                : ProductBackgroundSource.Preset;
        return value with
        {
            SelectedWindowIdentity = identity,
            MicrophoneSelection = microphone,
            BackgroundSource = source,
            BackgroundPreset = missingCustom
                ? ProductBackgroundPreset.Warm
                : value.BackgroundPreset,
            CustomBackgroundPath = custom,
            OutputRoot = output,
            FrameRateMode = Enum.IsDefined(value.FrameRateMode)
                ? value.FrameRateMode
                : FrameRateMode.Fps30,
            RecordingResolutionMode =
                Enum.IsDefined(value.RecordingResolutionMode)
                    ? value.RecordingResolutionMode
                    : RecordingResolutionMode.Original,
            UiLanguage = XbPreview.Avalonia.Localization.UiLanguage
                .NormalizePersisted(value.UiLanguage),
            RecoveryDismissedSessionIds =
                NormalizeRecoveryDismissedSessionIds(
                    value.RecoveryDismissedSessionIds),
        };
    }

    private static string[] NormalizeRecoveryDismissedSessionIds(
        IEnumerable<string>? values)
    {
        if (values is null)
        {
            return [];
        }

        HashSet<string> unique = new(StringComparer.Ordinal);
        List<string> normalized = [];
        foreach (string? value in values)
        {
            string candidate = value?.Trim() ?? string.Empty;
            if (candidate.Length != 0 && unique.Add(candidate))
            {
                normalized.Add(candidate);
            }
        }
        return normalized.ToArray();
    }
}

internal sealed class ProductState
{
    private readonly object _gate = new();
    private readonly ProductSettingsStore _store;
    private ProductSettings _current;

    internal ProductState(ProductSettingsStore? store = null)
    {
        _store = store ?? new ProductSettingsStore();
        _current = _store.Load();
    }

    internal ProductSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    internal void Set(ProductSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            _current = settings;
        }
    }

    internal bool TrySetCustomBackground(string path)
    {
        if (!ProductPathContract.TryValidateCustomBackground(
                path, out string validated))
        {
            return false;
        }
        lock (_gate)
        {
            _current = _current with
            {
                BackgroundSource = ProductBackgroundSource.CustomImage,
                CustomBackgroundPath = validated,
            };
        }
        return true;
    }

    internal bool TrySetOutputRoot(string path)
    {
        if (!ProductPathContract.TryValidateOutputRoot(
                path, out string validated))
        {
            return false;
        }
        lock (_gate)
        {
            _current = _current with { OutputRoot = validated };
        }
        return true;
    }

    internal void Persist()
    {
        lock (_gate)
        {
            _store.Save(_current);
        }
    }

    internal bool TrySetUiLanguage(string language)
    {
        if (XbPreview.Avalonia.Localization.UiLanguage
            .NormalizePersisted(language) is not { } normalized)
        {
            return false;
        }

        lock (_gate)
        {
            ProductSettings updated = _current with { UiLanguage = normalized };
            try
            {
                _store.Save(updated);
            }
            catch (Exception error) when (
                error is ArgumentException or IOException or
                    UnauthorizedAccessException or NotSupportedException)
            {
                return false;
            }
            _current = updated;
            return true;
        }
    }

    internal bool TryDismissRecoveryReminder(string sessionId)
    {
        string normalized = sessionId?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            string[] currentIds = _current.RecoveryDismissedSessionIds ?? [];
            if (currentIds.Contains(normalized, StringComparer.Ordinal))
            {
                return true;
            }

            ProductSettings updated = _current with
            {
                RecoveryDismissedSessionIds =
                    [.. currentIds, normalized],
            };
            try
            {
                _store.Save(updated);
            }
            catch (Exception error) when (
                error is ArgumentException or IOException or
                    UnauthorizedAccessException or NotSupportedException)
            {
                return false;
            }
            _current = updated;
            return true;
        }
    }

    internal ProductSettings Reload()
    {
        lock (_gate)
        {
            _current = _store.Load();
            return _current;
        }
    }

    internal ProductSettings ResetToDefaults()
    {
        lock (_gate)
        {
            _current = ProductSettings.Defaults with
            {
                RecoveryDismissedSessionIds =
                    _current.RecoveryDismissedSessionIds ?? [],
            };
            return _current;
        }
    }
}

internal readonly record struct ProductSettingsApplyResult(
    NativeMethods.Result Result,
    string Operation)
{
    internal bool Succeeded => Result == NativeMethods.Result.Ok;
}

internal static class ProductSettingsRuntimeAdapter
{
    internal static ProductSettingsApplyResult Apply(
        IPreviewNativeSession session,
        ProductSettings settings)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);

        ProductSettingsApplyResult result = Invoke(
            "3D pose",
            session.SetWindowShowcasePose(
                (NativeMethods.WindowStageOrientation)settings.StageOrientation,
                (NativeMethods.WindowStageLevel)settings.StageLevel,
                active: true));
        if (!result.Succeeded)
        {
            return result;
        }

        if (settings.BackgroundSource == ProductBackgroundSource.CustomImage)
        {
            if (!ProductPathContract.TryValidateCustomBackground(
                    settings.CustomBackgroundPath, out string customPath))
            {
                return Invoke(
                    "custom background validation",
                    NativeMethods.Result.InvalidArgument);
            }
            result = Invoke(
                "custom background",
                session.SetWindowShowcaseCustomBackground(customPath));
        }
        else
        {
            result = Invoke(
                "background preset",
                session.SetWindowShowcaseBackgroundPreset(
                    (NativeMethods.WindowShowcaseBackgroundPreset)
                        settings.BackgroundPreset));
        }
        if (!result.Succeeded)
        {
            return result;
        }

        result = Invoke(
            "recording output root",
            session.SetRecordingOutputRoot(settings.OutputRoot));
        if (!result.Succeeded)
        {
            return result;
        }
        result = Invoke(
            "recording frame rate",
            session.SetRecordingFrameRate((uint)settings.FrameRateMode));
        if (!result.Succeeded)
        {
            return result;
        }
        result = Invoke(
            "record cursor visibility",
            session.SetRecordCursorVisible(settings.MouseVisible));
        if (!result.Succeeded)
        {
            return result;
        }
        result = Invoke(
            "audio program mode",
            session.SetAudioProgramMode(
                MinimalRecordingShellPolicy.NativeAudioMode(
                    settings.SystemAudioEnabled,
                    settings.MicrophoneEnabled)));
        if (!result.Succeeded)
        {
            return result;
        }
        return Invoke(
            "microphone selection",
            session.SetMicrophoneSelection(settings.MicrophoneSelection));
    }

    private static ProductSettingsApplyResult Invoke(
        string operation,
        NativeMethods.Result result) => new(result, operation);
}
