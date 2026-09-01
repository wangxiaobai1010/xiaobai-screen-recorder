namespace XbPreview.Host;

// HUMAN REVIEW / PRESENTATION STATE ONLY.
// A future integration can replace the Items source without coupling this UI
// to production window enumeration or capture target binding.
internal enum FormalUiCaptureMode
{
    FullScreen,
    Window,
}

internal sealed record FormalUiWindowPresentationItem(
    string Id,
    string Title,
    string IconGlyph);

internal sealed class FormalUiWindowTargetPresentationState
{
    private static readonly IReadOnlyList<FormalUiWindowPresentationItem> DemoItems =
    [
        new("chrome-chatgpt", "Google Chrome - ChatGPT", "\uE774"),
        new("visual-studio-code", "Visual Studio Code", "\uE943"),
        new("file-explorer", "文件资源管理器", "\uE8B7"),
        new("wechat", "微信", "\uE8BD"),
    ];

    internal FormalUiCaptureMode CaptureMode { get; private set; } =
        FormalUiCaptureMode.FullScreen;

    internal IReadOnlyList<FormalUiWindowPresentationItem> Items => DemoItems;

    internal FormalUiWindowPresentationItem SelectedWindow { get; private set; } =
        DemoItems[1];

    internal void SelectFullScreen() => CaptureMode = FormalUiCaptureMode.FullScreen;

    internal void SelectWindowMode() => CaptureMode = FormalUiCaptureMode.Window;

    internal void SelectWindow(FormalUiWindowPresentationItem item)
    {
        SelectedWindow = item;
        CaptureMode = FormalUiCaptureMode.Window;
    }
}
