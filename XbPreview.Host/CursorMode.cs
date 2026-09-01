namespace XbPreview.Host;

internal enum CursorSelectionMode
{
    SystemCursor = 0,
    CustomCursor = 1,
}

internal readonly record struct RecordCursorVisibilitySnapshot(
    bool RequestedVisible,
    bool AppliedVisible,
    ulong Revision);

internal static class CursorModeText
{
    internal static string Describe(
        NativeMethods.CursorMode requested,
        NativeMethods.CursorMode actual,
        NativeMethods.CursorFallbackReason fallback) =>
        fallback == NativeMethods.CursorFallbackReason.None
            ? $"requested={requested}; actual={actual}"
            : $"requested={requested}; actual={actual}; fallback={fallback}";
}
