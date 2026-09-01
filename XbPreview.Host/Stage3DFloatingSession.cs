using System.Diagnostics;
using XbPreview.Avalonia.Views.Panels;

namespace XbPreview.Host;

/// <summary>
/// Owns one Panel 3 detach session. Floating geometry deliberately dies with
/// the session so every later detach measures the current Slot 3 Home again.
/// </summary>
internal sealed class Stage3DFloatingSession : IDisposable
{
    private IDisposable? _captureVisibilityRegistration;
    private bool _disposed;

    internal Stage3DFloatingSession(
        Stage3DFloatingForm form,
        Stage3DPanelView view,
        IDisposable captureVisibilityRegistration)
    {
        Form = form ?? throw new ArgumentNullException(nameof(form));
        View = view ?? throw new ArgumentNullException(nameof(view));
        _captureVisibilityRegistration = captureVisibilityRegistration ??
            throw new ArgumentNullException(
                nameof(captureVisibilityRegistration));
    }

    internal Stage3DFloatingForm Form { get; }

    internal Stage3DPanelView View { get; }

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
        TryTeardown(View.Dispose, "detach floating Panel 3 view");
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
                $"Panel 3 floating teardown failed to {operation}: {error}");
        }
    }
}
