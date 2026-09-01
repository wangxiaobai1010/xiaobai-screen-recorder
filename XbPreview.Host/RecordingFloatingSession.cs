using System.Diagnostics;
using XbPreview.Avalonia.Views.Panels;

namespace XbPreview.Host;

/// <summary>
/// Owns one Panel 4 detach session. Floating geometry deliberately dies with
/// the session so every later detach measures the current Slot 4 Home again.
/// </summary>
internal sealed class RecordingFloatingSession : IDisposable
{
    private IDisposable? _captureVisibilityRegistration;
    private bool _disposed;

    internal RecordingFloatingSession(
        RecordingFloatingForm form,
        RecordingPanelView view,
        IDisposable captureVisibilityRegistration)
    {
        Form = form ?? throw new ArgumentNullException(nameof(form));
        View = view ?? throw new ArgumentNullException(nameof(view));
        _captureVisibilityRegistration = captureVisibilityRegistration ??
            throw new ArgumentNullException(
                nameof(captureVisibilityRegistration));
    }

    internal RecordingFloatingForm Form { get; }

    internal RecordingPanelView View { get; }

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
        TryTeardown(View.Dispose, "detach floating Panel 4 view");
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
                $"Panel 4 floating teardown failed to {operation}: {error}");
        }
    }
}
