using XbPreview.Avalonia.Views.Panels;
using XbPreview.Avalonia.Localization;

namespace XbPreview.Host;

/// <summary>
/// Connects the Panel 3 ComboBox and Windows picker to the background-only
/// controller. The view retains no background source identity or runtime state.
/// </summary>
internal sealed class Stage3DPanelBackgroundAdapter : IDisposable
{
    private readonly object _gate = new();
    private readonly Stage3DPanelBackgroundState _presentationState;
    private readonly Stage3DPanelBackgroundController _controller;
    private readonly Func<string?> _pickCustomBackground;
    private readonly HashSet<Stage3DPanelView> _views = [];
    private bool _disposed;

    internal Stage3DPanelBackgroundAdapter(
        Stage3DPanelBackgroundState presentationState,
        Stage3DPanelView view,
        ProductState productState,
        Func<IWindowShowcaseBackgroundCommands?> sessionProvider,
        Func<string?> pickCustomBackground)
    {
        _presentationState = presentationState ??
            throw new ArgumentNullException(nameof(presentationState));
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(productState);
        ArgumentNullException.ThrowIfNull(sessionProvider);
        _pickCustomBackground = pickCustomBackground ??
            throw new ArgumentNullException(nameof(pickCustomBackground));
        _controller = new Stage3DPanelBackgroundController(
            presentationState,
            productState,
            sessionProvider);
        AttachView(view);
    }

    internal void AttachView(Stage3DPanelView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!ReferenceEquals(view.BackgroundState, _presentationState))
            {
                throw new InvalidOperationException(
                    "Every Panel 3 view must use the authoritative shared background state.");
            }
            if (!_views.Add(view))
            {
                return;
            }

            view.BackgroundPresetRequested += OnBackgroundPresetRequested;
            view.CustomBackgroundPickerRequested +=
                OnCustomBackgroundPickerRequested;
        }
    }

    internal void DetachView(Stage3DPanelView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        lock (_gate)
        {
            if (!_views.Remove(view))
            {
                return;
            }

            view.BackgroundPresetRequested -= OnBackgroundPresetRequested;
            view.CustomBackgroundPickerRequested -=
                OnCustomBackgroundPickerRequested;
        }
    }

    internal NativeMethods.Result Initialize(bool actionsEnabled) =>
        _controller.Initialize(actionsEnabled);

    internal void SetActionsEnabled(
        bool enabled,
        bool changesPresentation = true)
    {
        if (!_disposed)
        {
            _controller.SetActionsEnabled(enabled, changesPresentation);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _controller.SetActionsEnabled(false);
            foreach (Stage3DPanelView view in _views.ToArray())
            {
                view.BackgroundPresetRequested -=
                    OnBackgroundPresetRequested;
                view.CustomBackgroundPickerRequested -=
                    OnCustomBackgroundPickerRequested;
            }
            _views.Clear();
        }
    }

    private void OnBackgroundPresetRequested(
        object? sender,
        Stage3DBackgroundPresetRequestedEventArgs e)
    {
        if (!_disposed)
        {
            _ = _controller.SelectPreset(e.Preset);
        }
    }

    private void OnCustomBackgroundPickerRequested(
        object? sender,
        EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _ = _controller.SelectCustom(_pickCustomBackground());
        }
        catch (Exception error) when (
            error is InvalidOperationException or IOException or
                UnauthorizedAccessException or NotSupportedException)
        {
            _controller.ReportPickerFailure(
                Strings.Get("BackgroundPickerFailed") + $": {error.Message}");
        }
    }
}
