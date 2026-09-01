using System.Globalization;
using System.Resources;

namespace XbPreview.Avalonia.Localization;

public static class UiLanguage
{
    public const string SimplifiedChinese = "zh-CN";
    public const string English = "en";

    public static string? NormalizePersisted(string? language)
    {
        if (string.Equals(language, SimplifiedChinese,
                StringComparison.OrdinalIgnoreCase))
        {
            return SimplifiedChinese;
        }
        return string.Equals(language, English, StringComparison.OrdinalIgnoreCase)
            ? English
            : null;
    }

    public static string Resolve(string? persistedLanguage, CultureInfo systemUiCulture)
    {
        ArgumentNullException.ThrowIfNull(systemUiCulture);
        if (NormalizePersisted(persistedLanguage) is { } normalized)
        {
            return normalized;
        }
        return systemUiCulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? SimplifiedChinese
            : English;
    }

    public static CultureInfo Apply(string language)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(Resolve(
            language,
            CultureInfo.InstalledUICulture));
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        return culture;
    }
}

public static class Strings
{
    private static readonly ResourceManager ResourceManager = new(
        "XbPreview.Avalonia.Resources.Strings",
        typeof(Strings).Assembly);

    public static string Get(string key) =>
        ResourceManager.GetString(key, CultureInfo.CurrentUICulture) is
            { Length: > 0 } value
                ? value
                : throw new MissingManifestResourceException(
                    $"Missing or empty UI resource '{key}' for " +
                    $"'{CultureInfo.CurrentUICulture.Name}'.");

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    public static IReadOnlySet<string> ResourceKeys(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ResourceSet resources = ResourceManager.GetResourceSet(
            culture,
            createIfNotExists: true,
            tryParents: false) ?? throw new MissingManifestResourceException(
                $"UI resource set '{culture.Name}' was not found.");
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in resources)
        {
            if (entry.Key is string key && entry.Value is string { Length: > 0 })
            {
                keys.Add(key);
            }
        }
        return keys;
    }

    public static string CaptureTitle => Get(nameof(CaptureTitle));
    public static string CaptureTarget => Get(nameof(CaptureTarget));
    public static string FullScreen => Get(nameof(FullScreen));
    public static string Window => Get(nameof(Window));
    public static string CaptureConnecting => Get(nameof(CaptureConnecting));
    public static string HideCursor => Get(nameof(HideCursor));
    public static string SystemAudio => Get(nameof(SystemAudio));
    public static string Microphone => Get(nameof(Microphone));
    public static string RefreshSystemAudio => Get(nameof(RefreshSystemAudio));
    public static string RefreshSystemAudioHelp => Get(nameof(RefreshSystemAudioHelp));
    public static string RefreshMicrophone => Get(nameof(RefreshMicrophone));
    public static string RefreshMicrophoneHelp => Get(nameof(RefreshMicrophoneHelp));
    public static string DirectorTitle => Get(nameof(DirectorTitle));
    public static string ManualZoom => Get(nameof(ManualZoom));
    public static string ZoomShortcuts => Get(nameof(ZoomShortcuts));
    public static string AutoZoom => Get(nameof(AutoZoom));
    public static string StageTitle => Get(nameof(StageTitle));
    public static string ViewAngle => Get(nameof(ViewAngle));
    public static string Left => Get(nameof(Left));
    public static string Front => Get(nameof(Front));
    public static string Right => Get(nameof(Right));
    public static string Tilt => Get(nameof(Tilt));
    public static string Light => Get(nameof(Light));
    public static string Medium => Get(nameof(Medium));
    public static string Strong => Get(nameof(Strong));
    public static string Background => Get(nameof(Background));
    public static string RecordingTitle => Get(nameof(RecordingTitle));
    public static string IncludeFloatingPanels => Get(nameof(IncludeFloatingPanels));
    public static string SaveLocation => Get(nameof(SaveLocation));
    public static string Resolution => Get(nameof(Resolution));
    public static string ResolutionOriginal => Get(nameof(ResolutionOriginal));
    public static string Resolution1080 => Get(nameof(Resolution1080));
    public static string Resolution1440 => Get(nameof(Resolution1440));
    public static string Resolution4K => Get(nameof(Resolution4K));
    public static string FrameRate => Get(nameof(FrameRate));
    public static string StartRecording => Get(nameof(StartRecording));
    public static string Recording => Get(nameof(Recording));
    public static string Starting => Get(nameof(Starting));
    public static string ElapsedTime => Get(nameof(ElapsedTime));
    public static string Pause => Get(nameof(Pause));
    public static string RestartRecording => Get(nameof(RestartRecording));
    public static string Stop => Get(nameof(Stop));
    public static string Status => Get(nameof(Status));
    public static string Saved => Get(nameof(Saved));
    public static string BackToReady => Get(nameof(BackToReady));
    public static string RecordingDuration => Get(nameof(RecordingDuration));
    public static string OpenFolder => Get(nameof(OpenFolder));
    public static string OpenVideo => Get(nameof(OpenVideo));
    public static string RestartConfirmTitle => Get(nameof(RestartConfirmTitle));
    public static string RestartConfirmBody => Get(nameof(RestartConfirmBody));
    public static string ContinueRecording => Get(nameof(ContinueRecording));
    public static string DiscardRecording => Get(nameof(DiscardRecording));
    public static string ReturnHome => Get(nameof(ReturnHome));
    public static string Later => Get(nameof(Later));
    public static string Back => Get(nameof(Back));
    public static string Settings => Get(nameof(Settings));
    public static string SettingsSubtitle => Get(nameof(SettingsSubtitle));
    public static string Language => Get(nameof(Language));
    public static string SimplifiedChineseSelf => Get(nameof(SimplifiedChineseSelf));
    public static string EnglishSelf => Get(nameof(EnglishSelf));
    public static string RestartToApply => Get(nameof(RestartToApply));
    public static string RestartNow => Get(nameof(RestartNow));
    public static string RestartLater => Get(nameof(RestartLater));
    public static string LanguageEntry => Get(nameof(LanguageEntry));
    public static string BrandName => Get(nameof(BrandName));
    public static string RecoveryBodyPreserved => Get(nameof(RecoveryBodyPreserved));
    public static string Frame60Help => Get(nameof(Frame60Help));
}
