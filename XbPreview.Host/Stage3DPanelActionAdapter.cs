using XbPreview.Avalonia.Views.Panels;

namespace XbPreview.Host;

/// <summary>
/// Maps the existing Panel 3 controls to the final frozen Showcase seam. The
/// view owns no Stage, Motion, Punch, Camera, or persistence state.
/// </summary>
internal sealed class Stage3DPanelActionAdapter : IDisposable
{
    private readonly object _gate = new();
    private readonly Stage3DPanelPresentationState _presentationState;
    private readonly Stage3DPanelActionController _controller;
    private readonly HashSet<Stage3DPanelView> _views = [];
    private bool _actionsEnabled;
    private bool _disposed;

    internal Stage3DPanelActionAdapter(
        Stage3DPanelPresentationState presentationState,
        Stage3DPanelView view,
        Func<IPreviewNativeSession?> sessionProvider)
    {
        _presentationState = presentationState ??
            throw new ArgumentNullException(nameof(presentationState));
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(sessionProvider);

        _controller = new Stage3DPanelActionController(
            presentationState,
            sessionProvider);

        AttachView(view);
        PublishUnsafe(
            Stage3DPanelOrientation.Right,
            Stage3DPanelLevel.Level2,
            isActive: true);
    }

    internal void AttachView(Stage3DPanelView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!ReferenceEquals(view.PresentationState, _presentationState))
            {
                throw new InvalidOperationException(
                    "Every Panel 3 view must use the authoritative shared state.");
            }
            if (_views.Add(view))
            {
                view.PoseRequested += OnPoseRequested;
            }
        }
    }

    internal void DetachView(Stage3DPanelView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        lock (_gate)
        {
            if (_views.Remove(view))
            {
                view.PoseRequested -= OnPoseRequested;
            }
        }
    }

    internal void SetActionsEnabled(
        bool enabled,
        bool changesPresentation = true)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _actionsEnabled = enabled;
            if (!changesPresentation)
            {
                return;
            }
            Stage3DPanelPresentationSnapshot current =
                _presentationState.Snapshot;
            PublishUnsafe(
                current.Orientation,
                current.Level,
                current.IsActive);
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
            _actionsEnabled = false;
            foreach (Stage3DPanelView view in _views.ToArray())
            {
                view.PoseRequested -= OnPoseRequested;
            }
            _views.Clear();
        }
    }

    private void OnPoseRequested(
        object? sender,
        Stage3DPoseRequestedEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed || !_actionsEnabled)
            {
                return;
            }

            NativeMethods.Result result = _controller.Execute(
                e.Command,
                _actionsEnabled);
            if (result != NativeMethods.Result.Ok)
            {
                _actionsEnabled = false;
                Stage3DPanelPresentationSnapshot current =
                    _presentationState.Snapshot;
                PublishUnsafe(
                    current.Orientation,
                    current.Level,
                    current.IsActive);
            }
        }
    }

    private void PublishUnsafe(
        Stage3DPanelOrientation orientation,
        Stage3DPanelLevel level,
        bool isActive) => _presentationState.Apply(new(
            orientation,
            level,
            isActive,
            _actionsEnabled));
}
