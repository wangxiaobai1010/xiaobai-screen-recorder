using System.Diagnostics;
using XbPreview.Avalonia.Views.Panels;

namespace XbPreview.Host;

/// <summary>
/// Owns only one detach session. No bounds survive disposal, so the next
/// detach must measure the current Home again.
/// </summary>
internal sealed class CaptureFloatingSession : IDisposable
{
    private IDisposable? _captureVisibilityRegistration;
    private bool _disposed;

    internal CaptureFloatingSession(
        CaptureFloatingForm form,
        CapturePanelView view,
        IDisposable captureVisibilityRegistration)
    {
        Form = form ?? throw new ArgumentNullException(nameof(form));
        View = view ?? throw new ArgumentNullException(nameof(view));
        _captureVisibilityRegistration = captureVisibilityRegistration ??
            throw new ArgumentNullException(
                nameof(captureVisibilityRegistration));
    }

    internal CaptureFloatingForm Form { get; }

    internal CapturePanelView View { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        TryTeardown(Form.CloseForReturnHome, "close floating form");
        TryTeardown(
            () => _captureVisibilityRegistration?.Dispose(),
            "unregister floating capture visibility");
        _captureVisibilityRegistration = null;
        TryTeardown(View.Dispose, "detach floating Capture view");
        TryTeardown(Form.Dispose, "dispose floating form");
    }

    private static void TryTeardown(Action action, string operation)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            Debug.WriteLine(
                $"Capture floating teardown failed to {operation}: {error}");
        }
    }
}
