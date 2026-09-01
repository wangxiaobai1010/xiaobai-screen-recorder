using System.Text.Json;

namespace XbPreview.Host;

internal enum MicrophoneSelectionKind
{
    WindowsDefault = 0,
    ConcreteEndpoint = 1,
}

internal sealed record MicrophoneDevice(
    string EndpointId,
    string DisplayName);

internal sealed record MicrophoneDeviceCatalog(
    ulong Generation,
    bool MonitorActive,
    bool DefaultAvailable,
    string DefaultEndpointId,
    string DefaultDisplayName,
    uint DeviceAddedCount,
    uint DeviceRemovedCount,
    IReadOnlyList<MicrophoneDevice> Devices)
{
    internal static MicrophoneDeviceCatalog Empty { get; } = new(
        0,
        false,
        false,
        string.Empty,
        string.Empty,
        0,
        0,
        Array.Empty<MicrophoneDevice>());
}

internal sealed record MicrophoneSelection(
    MicrophoneSelectionKind Kind,
    string EndpointId,
    string DisplayName)
{
    internal static MicrophoneSelection WindowsDefault { get; } = new(
        MicrophoneSelectionKind.WindowsDefault,
        string.Empty,
        string.Empty);
}

internal sealed record MicrophoneSelectionStatus(
    MicrophoneSelectionKind Kind,
    bool Available,
    bool SessionLocked,
    string EndpointId,
    string DisplayName)
{
    internal static MicrophoneSelectionStatus UnavailableDefault { get; } =
        new(
            MicrophoneSelectionKind.WindowsDefault,
            false,
            false,
            string.Empty,
            string.Empty);
}

internal sealed record MicrophoneDeviceChoice(
    MicrophoneSelection Selection,
    bool Available)
{
    public override string ToString()
    {
        if (Selection.Kind == MicrophoneSelectionKind.WindowsDefault)
        {
            return Available && !string.IsNullOrWhiteSpace(Selection.DisplayName)
                ? $"Windows 默认麦克风 — {Selection.DisplayName}"
                : "Windows 默认麦克风（不可用）";
        }
        return Available
            ? Selection.DisplayName
            : $"{Selection.DisplayName}（不可用）";
    }
}

internal static class MicrophoneSelectionSettings
{
    private sealed record Document(
        string Selection,
        string? EndpointId,
        string? DisplayName);

    internal static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XbPreview",
        "settings",
        "microphone-selection.json");

    // ProductSettings is the single product source of truth. The path-based
    // overload below remains only as the legacy migration reader and for its
    // focused compatibility tests.
    internal static MicrophoneSelection Load() =>
        new ProductSettingsStore().Load().MicrophoneSelection;

    internal static MicrophoneSelection Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return MicrophoneSelection.WindowsDefault;
            }
            Document? value = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path));
            if (value is null ||
                !string.Equals(
                    value.Selection,
                    "concrete-endpoint",
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(value.EndpointId))
            {
                return MicrophoneSelection.WindowsDefault;
            }
            return new MicrophoneSelection(
                MicrophoneSelectionKind.ConcreteEndpoint,
                value.EndpointId,
                string.IsNullOrWhiteSpace(value.DisplayName)
                    ? value.EndpointId
                    : value.DisplayName);
        }
        catch (JsonException)
        {
            return MicrophoneSelection.WindowsDefault;
        }
        catch (IOException)
        {
            return MicrophoneSelection.WindowsDefault;
        }
        catch (UnauthorizedAccessException)
        {
            return MicrophoneSelection.WindowsDefault;
        }
    }

    internal static void Save(MicrophoneSelection selection)
    {
        ProductSettingsStore store = new();
        ProductSettings settings = store.Load() with
        {
            MicrophoneSelection = selection,
        };
        store.Save(settings);
    }

    internal static void Save(string path, MicrophoneSelection selection)
    {
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException(
                "Microphone settings path has no parent directory.",
                nameof(path));
        }
        Directory.CreateDirectory(directory);
        Document document = selection.Kind ==
                MicrophoneSelectionKind.WindowsDefault
            ? new Document("windows-default", null, null)
            : new Document(
                "concrete-endpoint",
                selection.EndpointId,
                selection.DisplayName);
        string temporary = path + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(document));
        File.Move(temporary, path, overwrite: true);
    }
}
