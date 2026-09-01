using System.Diagnostics;

namespace XbPreview.Host;

internal sealed class FixedTargetCameraController
{
    private readonly object _gate = new();
    private readonly long _frequency;
    private bool _enabled = true;
    private bool _previewRunning;
    private ulong _sequence;
    private CameraMode _mode = CameraMode.Wide;
    private double _zoom = CameraSettings.WideZoom;
    private double _centerX = 0.5;
    private double _centerY = 0.5;
    private double _zoomVelocity;
    private double _centerVelocityX;
    private double _centerVelocityY;
    private double _targetX = 0.5;
    private double _targetY = 0.5;
    private double _startZoom = CameraSettings.WideZoom;
    private double _startCenterX = 0.5;
    private double _startCenterY = 0.5;
    private double _transitionEndZoom = CameraSettings.WideZoom;
    private double _transitionEndCenterX = 0.5;
    private double _transitionEndCenterY = 0.5;
    private CameraPreset _targetPreset = CameraPreset.Wide;
    private long _transitionStartQpc;
    private long _lastAdvanceQpc;
    private string _nextEvent = "created";
    private CameraOwner _owner = CameraOwner.Manual;
    private DirectorLiteState _directorState = DirectorLiteState.Wide;
    private DirectorFocusStrength _directorFocusStrength =
        DirectorFocusStrength.Soft;
    private long _lastDirectorActivityQpc;
    private bool _hasDirectorFocusTarget;

    internal FixedTargetCameraController(long frequency = 0)
    {
        _frequency = frequency > 0 ? frequency : Stopwatch.Frequency;
    }

    internal double ZoomVelocity
    {
        get { lock (_gate) { return _zoomVelocity; } }
    }

    internal double CenterVelocityX
    {
        get { lock (_gate) { return _centerVelocityX; } }
    }

    internal double CenterVelocityY
    {
        get { lock (_gate) { return _centerVelocityY; } }
    }

    internal CameraOwner Owner
    {
        get { lock (_gate) { return _owner; } }
    }

    internal DirectorLiteState DirectorState
    {
        get { lock (_gate) { return _directorState; } }
    }

    internal DirectorFocusStrength DirectorFocusStrength
    {
        get { lock (_gate) { return _directorFocusStrength; } }
    }

    internal bool HasDirectorFocusTarget
    {
        get { lock (_gate) { return _hasDirectorFocusTarget; } }
    }

    internal double TargetZoom
    {
        get
        {
            lock (_gate)
            {
                return CameraCommandDefinition.TargetZoom(_targetPreset);
            }
        }
    }

    internal long LastDirectorActivityQpc
    {
        get { lock (_gate) { return _lastDirectorActivityQpc; } }
    }

    internal void SetPreviewRunning(bool running, long nowQpc)
    {
        lock (_gate)
        {
            _previewRunning = running;
            if (!running)
            {
                _owner = CameraOwner.Manual;
                ClearDirectorUnsafe();
                SetWideUnsafe("preview-stopped");
            }
            else
            {
                _transitionStartQpc = nowQpc;
                _lastAdvanceQpc = nowQpc;
                _nextEvent = "preview-started";
            }
        }
    }

    internal void SetEnabled(bool enabled, long nowQpc)
    {
        lock (_gate)
        {
            _enabled = enabled;
            if (!enabled)
            {
                _owner = CameraOwner.Manual;
                ClearDirectorUnsafe();
                SetWideUnsafe("camera-disabled");
            }
            else
            {
                _transitionStartQpc = nowQpc;
                _lastAdvanceQpc = nowQpc;
                _nextEvent = "camera-enabled";
            }
        }
    }

    internal bool Execute(
        CameraCommand command,
        CameraPoint cursorTarget,
        long nowQpc,
        out string status) =>
        Execute(command, () => cursorTarget, nowQpc, out status);

    internal bool Execute(
        CameraCommand command,
        Func<CameraPoint> readCursorTarget,
        long nowQpc,
        out string status)
    {
        lock (_gate)
        {
            if (_owner != CameraOwner.Manual)
            {
                status = "Director Lite owns the camera; manual command ignored.";
                return false;
            }
            if (!_enabled)
            {
                status = "相机已禁用；镜头命令未改变预览。";
                return false;
            }
            if (!_previewRunning)
            {
                status = "预览尚未运行；镜头命令未改变预览。";
                return false;
            }

            AdvanceUnsafe(nowQpc);
            CameraView currentView = CameraMath.ClampView(_zoom, _centerX, _centerY);
            _zoom = currentView.Zoom;
            _centerX = currentView.CenterX;
            _centerY = currentView.CenterY;
            CameraPreset requestedPreset =
                CameraCommandDefinition.TargetPreset(command);
            CameraPreset previousTargetPreset = _targetPreset;
            CameraPreset destinationPreset =
                previousTargetPreset == requestedPreset
                    ? CameraPreset.Wide
                    : requestedPreset;

            _startZoom = _zoom;
            _startCenterX = _centerX;
            _startCenterY = _centerY;
            _transitionStartQpc = nowQpc;
            _lastAdvanceQpc = nowQpc;
            _targetPreset = destinationPreset;

            if (destinationPreset == CameraPreset.Wide)
            {
                _targetX = 0.5;
                _targetY = 0.5;
                _nextEvent = requestedPreset == CameraPreset.Standard
                    ? "f9-standard-exit"
                    : "f10-strong-exit";
                status = requestedPreset == CameraPreset.Standard
                    ? "F9：从当前镜头连续缩回 1.0x 全景。"
                    : "F10：从当前镜头连续缩回 1.0x 全景。";
            }
            else
            {
                CameraPoint cursorTarget = readCursorTarget();
                _targetX = CameraMath.Clamp(cursorTarget.X, 0.0, 1.0);
                _targetY = CameraMath.Clamp(cursorTarget.Y, 0.0, 1.0);
                if (destinationPreset == CameraPreset.Standard)
                {
                    _nextEvent = previousTargetPreset == CameraPreset.Strong
                        ? "strong-to-standard"
                        : "f9-standard-enter";
                    status = previousTargetPreset == CameraPreset.Strong
                        ? "F9：已重新锁定光标，从当前镜头连续切换到 1.6x 标准特写。"
                        : "F9：已锁定按键时光标，进入 1.6x 标准特写。";
                }
                else
                {
                    _nextEvent = previousTargetPreset == CameraPreset.Standard
                        ? "standard-to-strong"
                        : "f10-strong-enter";
                    status = previousTargetPreset == CameraPreset.Standard
                        ? "F10：已重新锁定光标，从当前镜头连续切换到 2.0x 强特写。"
                        : "F10：已锁定按键时光标，进入 2.0x 强特写。";
                }
            }

            _transitionEndZoom =
                CameraCommandDefinition.TargetZoom(destinationPreset);
            CameraView destinationView = CameraMath.ClampView(
                _transitionEndZoom,
                _targetX,
                _targetY);
            _transitionEndCenterX = destinationView.CenterX;
            _transitionEndCenterY = destinationView.CenterY;
            _mode = _transitionEndZoom >= _startZoom
                ? CameraMode.ZoomingIn
                : CameraMode.ZoomingOut;

            if (IsAtRestUnsafe())
            {
                CompleteTransitionUnsafe();
            }
            return true;
        }
    }

    internal CameraState Snapshot(long nowQpc)
    {
        lock (_gate)
        {
            try
            {
                AdvanceUnsafe(nowQpc);
                ApplyDirectorInactivityUnsafe(nowQpc);
                CameraView view = CameraMath.ClampView(_zoom, _centerX, _centerY);
                _zoom = view.Zoom;
                _centerX = view.CenterX;
                _centerY = view.CenterY;
                double elapsed = IsTransition(_mode)
                    ? Math.Max(0.0, (nowQpc - _transitionStartQpc) / (double)_frequency)
                    : 0.0;
                double progress = IsTransition(_mode)
                    ? SpatialProgressUnsafe()
                    : 1.0;
                string eventName = _nextEvent;
                _nextEvent = "tick";
                CameraState state = new(
                    ++_sequence,
                    nowQpc,
                    _enabled && _previewRunning && _zoom > 1.0,
                    _mode,
                    _zoom,
                    _centerX,
                    _centerY,
                    progress,
                    _targetX,
                    _targetY,
                    view.ClampX,
                    view.ClampY,
                    elapsed,
                    _startZoom,
                    _startCenterX,
                    _startCenterY,
                    CameraSettings.NominalSettleSeconds,
                    progress,
                    eventName);
                if (!CameraMath.IsValidState(state))
                {
                    throw new InvalidOperationException("Camera state is non-finite or out of range.");
                }
                return state;
            }
            catch
            {
                SetWideUnsafe("managed-error-fallback");
                return CameraState.Wide(
                    ++_sequence,
                    nowQpc,
                    CameraMode.ErrorFallback,
                    "managed-error-fallback");
            }
        }
    }

    internal CameraState PrepareForExit(long nowQpc)
    {
        lock (_gate)
        {
            _previewRunning = false;
            _owner = CameraOwner.Manual;
            ClearDirectorUnsafe();
            SetWideUnsafe("exit-fallback");
            return CameraState.Wide(++_sequence, nowQpc, eventName: "exit-fallback");
        }
    }

    internal bool SetDirectorLiteEnabled(
        bool enabled,
        long nowQpc,
        out string status)
    {
        lock (_gate)
        {
            CameraOwner requested = enabled
                ? CameraOwner.DirectorLite
                : CameraOwner.Manual;
            if (_owner == requested)
            {
                status = enabled
                    ? "Director Lite is already enabled."
                    : "Manual camera is already enabled.";
                return true;
            }
            if (enabled && (!_enabled || !_previewRunning))
            {
                status = "Director Lite requires a running, enabled camera.";
                return false;
            }

            AdvanceUnsafe(nowQpc);
            ClearDirectorUnsafe();
            BeginTransitionUnsafe(
                CameraPreset.Wide,
                new CameraPoint(0.5, 0.5),
                nowQpc,
                enabled ? "director-enabled-wide" : "director-disabled-wide");
            _owner = requested;
            status = enabled
                ? "Director Lite enabled; camera returning smoothly to Wide."
                : "Director Lite disabled; manual camera restored after Wide target.";
            return true;
        }
    }

    internal bool SetDirectorFocusStrength(
        DirectorFocusStrength strength,
        out string status)
    {
        lock (_gate)
        {
            if (!Enum.IsDefined(strength))
            {
                status = "Director focus strength is invalid.";
                return false;
            }
            if (_owner != CameraOwner.Manual)
            {
                status =
                    "Director Lite is enabled; focus strength is locked for this session.";
                return false;
            }

            _directorFocusStrength = strength;
            double zoom = CameraCommandDefinition.TargetZoom(
                DirectorFocusStrengthDefinition.TargetPreset(strength));
            status = $"Director focus strength configured: {strength} {zoom:F1}x.";
            return true;
        }
    }

    internal bool HandleDirectorLeftClick(
        CameraPoint clickTarget,
        long nowQpc,
        out string status)
    {
        lock (_gate)
        {
            if (_owner != CameraOwner.DirectorLite ||
                !_enabled ||
                !_previewRunning)
            {
                status = "Director Lite click ignored because it does not own the camera.";
                return false;
            }

            AdvanceUnsafe(nowQpc);
            bool retarget = _directorState == DirectorLiteState.Focused;
            CameraPoint clamped = new(
                CameraMath.Clamp(clickTarget.X, 0.0, 1.0),
                CameraMath.Clamp(clickTarget.Y, 0.0, 1.0));
            CameraPreset focusPreset =
                DirectorFocusStrengthDefinition.TargetPreset(
                    _directorFocusStrength);
            double focusZoom =
                CameraCommandDefinition.TargetZoom(focusPreset);
            BeginTransitionUnsafe(
                focusPreset,
                clamped,
                nowQpc,
                retarget ? "director-focus-retarget" : "director-focus-enter");
            _directorState = DirectorLiteState.Focused;
            _hasDirectorFocusTarget = true;
            _lastDirectorActivityQpc = nowQpc;
            status = retarget
                ? $"Director Lite smoothly retargeted at {focusZoom:F1}x."
                : $"Director Lite focused the click at {focusZoom:F1}x.";
            return true;
        }
    }

    internal bool HandleDirectorPointerActivity(long nowQpc)
    {
        lock (_gate)
        {
            if (_owner != CameraOwner.DirectorLite ||
                _directorState != DirectorLiteState.Focused)
            {
                return false;
            }
            _lastDirectorActivityQpc = Math.Max(nowQpc, _lastDirectorActivityQpc);
            return true;
        }
    }

    internal bool TrySetZoomedCenter(
        CameraState expectedState,
        CameraPoint requestedCenter,
        out CameraState updatedState)
    {
        lock (_gate)
        {
            updatedState = expectedState;
            if (_mode != CameraMode.ZoomedFixed ||
                expectedState.Mode != CameraMode.ZoomedFixed ||
                expectedState.Sequence != _sequence)
            {
                return false;
            }

            CameraView view = CameraMath.ClampView(
                _zoom,
                requestedCenter.X,
                requestedCenter.Y);
            _centerX = view.CenterX;
            _centerY = view.CenterY;
            updatedState = expectedState with
            {
                CenterX = view.CenterX,
                CenterY = view.CenterY,
                ClampX = view.ClampX,
                ClampY = view.ClampY,
            };
            return updatedState.IsValid;
        }
    }

    private void AdvanceUnsafe(long nowQpc)
    {
        if (nowQpc <= _lastAdvanceQpc)
        {
            return;
        }

        double remaining =
            (nowQpc - _lastAdvanceQpc) / (double)_frequency;
        _lastAdvanceQpc = nowQpc;
        if (!IsTransition(_mode) ||
            !CameraMath.IsFinite(remaining) ||
            remaining <= 0.0)
        {
            return;
        }

        while (remaining > 0.0 && IsTransition(_mode))
        {
            double delta = Math.Min(
                remaining,
                CameraSettings.MaximumDeltaSeconds);
            remaining -= delta;

            bool zoomSettled = CameraMath.AdvanceCriticalDamped(
                ref _zoom,
                ref _zoomVelocity,
                _transitionEndZoom,
                CameraSettings.SpringAngularFrequency,
                delta,
                CameraSettings.MaximumDeltaSeconds,
                CameraSettings.ZoomStopPositionEpsilon,
                CameraSettings.ZoomStopVelocityEpsilon);
            bool centerXSettled = CameraMath.AdvanceCriticalDamped(
                ref _centerX,
                ref _centerVelocityX,
                _transitionEndCenterX,
                CameraSettings.SpringAngularFrequency,
                delta,
                CameraSettings.MaximumDeltaSeconds,
                CameraSettings.CenterStopPositionEpsilon,
                CameraSettings.CenterStopVelocityEpsilon);
            bool centerYSettled = CameraMath.AdvanceCriticalDamped(
                ref _centerY,
                ref _centerVelocityY,
                _transitionEndCenterY,
                CameraSettings.SpringAngularFrequency,
                delta,
                CameraSettings.MaximumDeltaSeconds,
                CameraSettings.CenterStopPositionEpsilon,
                CameraSettings.CenterStopVelocityEpsilon);

            CameraView clamped = CameraMath.ClampView(
                _zoom,
                _centerX,
                _centerY);
            if (Math.Abs(clamped.Zoom - _zoom) > 1e-12)
            {
                _zoomVelocity = 0.0;
            }
            if (Math.Abs(clamped.CenterX - _centerX) > 1e-12)
            {
                _centerVelocityX = 0.0;
            }
            if (Math.Abs(clamped.CenterY - _centerY) > 1e-12)
            {
                _centerVelocityY = 0.0;
            }
            _zoom = clamped.Zoom;
            _centerX = clamped.CenterX;
            _centerY = clamped.CenterY;

            if ((zoomSettled && centerXSettled && centerYSettled) ||
                IsAtRestUnsafe())
            {
                CompleteTransitionUnsafe();
            }
        }
    }

    private bool IsAtRestUnsafe() =>
        Math.Abs(_zoom - _transitionEndZoom) <=
            CameraSettings.ZoomStopPositionEpsilon &&
        Math.Abs(_zoomVelocity) <=
            CameraSettings.ZoomStopVelocityEpsilon &&
        Math.Abs(_centerX - _transitionEndCenterX) <=
            CameraSettings.CenterStopPositionEpsilon &&
        Math.Abs(_centerVelocityX) <=
            CameraSettings.CenterStopVelocityEpsilon &&
        Math.Abs(_centerY - _transitionEndCenterY) <=
            CameraSettings.CenterStopPositionEpsilon &&
        Math.Abs(_centerVelocityY) <=
            CameraSettings.CenterStopVelocityEpsilon;

    private void CompleteTransitionUnsafe()
    {
        _zoom = _transitionEndZoom;
        _centerX = _transitionEndCenterX;
        _centerY = _transitionEndCenterY;
        _zoomVelocity = 0.0;
        _centerVelocityX = 0.0;
        _centerVelocityY = 0.0;
        _mode = _targetPreset == CameraPreset.Wide
            ? CameraMode.Wide
            : CameraMode.ZoomedFixed;
        _nextEvent = _targetPreset switch
        {
            CameraPreset.Standard => "standard-complete",
            CameraPreset.Strong => "strong-complete",
            _ => "wide-complete",
        };
    }

    private void ApplyDirectorInactivityUnsafe(long nowQpc)
    {
        if (_owner != CameraOwner.DirectorLite ||
            _directorState != DirectorLiteState.Focused ||
            _lastDirectorActivityQpc <= 0)
        {
            return;
        }
        double inactiveSeconds =
            (nowQpc - _lastDirectorActivityQpc) / (double)_frequency;
        if (inactiveSeconds < CameraSettings.DirectorLiteInactivitySeconds)
        {
            return;
        }

        BeginTransitionUnsafe(
            CameraPreset.Wide,
            new CameraPoint(0.5, 0.5),
            nowQpc,
            "director-inactivity-wide");
        ClearDirectorUnsafe();
    }

    private void BeginTransitionUnsafe(
        CameraPreset preset,
        CameraPoint target,
        long nowQpc,
        string eventName)
    {
        _startZoom = _zoom;
        _startCenterX = _centerX;
        _startCenterY = _centerY;
        _transitionStartQpc = nowQpc;
        _lastAdvanceQpc = nowQpc;
        _targetPreset = preset;
        _targetX = CameraMath.Clamp(target.X, 0.0, 1.0);
        _targetY = CameraMath.Clamp(target.Y, 0.0, 1.0);
        _transitionEndZoom = CameraCommandDefinition.TargetZoom(preset);
        CameraView destination = CameraMath.ClampView(
            _transitionEndZoom,
            _targetX,
            _targetY);
        _transitionEndCenterX = destination.CenterX;
        _transitionEndCenterY = destination.CenterY;
        _mode = _transitionEndZoom >= _startZoom
            ? CameraMode.ZoomingIn
            : CameraMode.ZoomingOut;
        _nextEvent = eventName;
        if (IsAtRestUnsafe())
        {
            CompleteTransitionUnsafe();
        }
    }

    private void ClearDirectorUnsafe()
    {
        _directorState = DirectorLiteState.Wide;
        _lastDirectorActivityQpc = 0;
        _hasDirectorFocusTarget = false;
    }

    private double SpatialProgressUnsafe()
    {
        double initialDistance = Math.Sqrt(
            Math.Pow(_transitionEndZoom - _startZoom, 2.0) +
            Math.Pow(_transitionEndCenterX - _startCenterX, 2.0) +
            Math.Pow(_transitionEndCenterY - _startCenterY, 2.0));
        if (initialDistance <= 1e-12)
        {
            return 1.0;
        }
        double remainingDistance = Math.Sqrt(
            Math.Pow(_transitionEndZoom - _zoom, 2.0) +
            Math.Pow(_transitionEndCenterX - _centerX, 2.0) +
            Math.Pow(_transitionEndCenterY - _centerY, 2.0));
        return CameraMath.Clamp(
            1.0 - (remainingDistance / initialDistance),
            0.0,
            1.0);
    }

    private void SetWideUnsafe(string eventName)
    {
        _mode = CameraMode.Wide;
        _zoom = CameraSettings.WideZoom;
        _centerX = 0.5;
        _centerY = 0.5;
        _zoomVelocity = 0.0;
        _centerVelocityX = 0.0;
        _centerVelocityY = 0.0;
        _targetX = 0.5;
        _targetY = 0.5;
        _startZoom = CameraSettings.WideZoom;
        _startCenterX = 0.5;
        _startCenterY = 0.5;
        _transitionEndZoom = CameraSettings.WideZoom;
        _transitionEndCenterX = 0.5;
        _transitionEndCenterY = 0.5;
        _targetPreset = CameraPreset.Wide;
        _lastAdvanceQpc = _transitionStartQpc;
        _nextEvent = eventName;
    }

    private static bool IsTransition(CameraMode mode) =>
        mode is CameraMode.ZoomingIn or CameraMode.ZoomingOut;
}
