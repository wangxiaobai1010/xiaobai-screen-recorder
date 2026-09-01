using System.Diagnostics;
using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Views.Panels;
using XbPreview.Avalonia.Localization;

namespace XbPreview.Host;

/// <summary>
/// Owns Panel 4's shared presentation and command policy while delegating all
/// recording runtime work to the frozen production controller chain.
/// </summary>
internal sealed class RecordingFixedHomeAdapter :
    IRecordingPanelController,
    IDisposable
{
    private readonly object _gate = new();
    private readonly Form _owner;
    private readonly ProductionRecordingAdapter _recordingCommands;
    private readonly RecordingController _recordingTruth;
    private readonly IPreviewNativeSession _native;
    private readonly ProductState _productState;
    private readonly IRecordingResolutionCommands _resolutionCommands;
    private readonly RecorderCaptureVisibilityController _captureVisibility;
    private RecordingReviewSnapshot _recording;
    private RecordingReviewState _lastRecordingState;
    private RecordingPanelPresentationState _current =
        RecordingPanelPresentationState.Initial;
    private RecordingReviewState? _pendingPhase;
    private string _canonicalOutputRoot = string.Empty;
    private string _actionError = string.Empty;
    private bool _fixedCommandPending;
    private bool _restartConfirmationVisible;
    private bool _cancelCommandPending;
    private bool _completionSummaryVisible;
    private bool _disposed;

    internal RecordingFixedHomeAdapter(
        Form owner,
        ProductionRecordingAdapter recordingCommands,
        RecordingController recordingTruth,
        IPreviewNativeSession native,
        ProductState productState,
        IRecordingResolutionCommands resolutionCommands,
        RecorderCaptureVisibilityController captureVisibility,
        string safeDefaultOutputRoot)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _recordingCommands = recordingCommands ?? throw new
            ArgumentNullException(nameof(recordingCommands));
        _recordingTruth = recordingTruth ?? throw new
            ArgumentNullException(nameof(recordingTruth));
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _productState = productState ?? throw new
            ArgumentNullException(nameof(productState));
        _resolutionCommands = resolutionCommands ?? throw new
            ArgumentNullException(nameof(resolutionCommands));
        _captureVisibility = captureVisibility ?? throw new
            ArgumentNullException(nameof(captureVisibility));

        _recording = _recordingCommands.CurrentSnapshot;
        _lastRecordingState = _recording.State;
        _completionSummaryVisible =
            _recording.State == RecordingReviewState.Completed;
        InitializeOutputRoot(safeDefaultOutputRoot);
        InitializeFrameRate();
        _current = BuildState();
        _recordingCommands.SnapshotChanged += OnRecordingSnapshotChanged;
        _captureVisibility.StateChanged += OnCaptureVisibilityStateChanged;
    }

    public event Action<RecordingPanelPresentationState>? StateChanged;

    public RecordingPanelPresentationState CurrentState
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    internal string CanonicalOutputRoot
    {
        get
        {
            lock (_gate)
            {
                return _canonicalOutputRoot;
            }
        }
    }

    public Task StartAsync() => ExecuteAsync(
        static state => state.CanStart,
        RecordingReviewState.Starting,
        _recordingCommands.StartAsync,
        Strings.Get("CannotStartRecording"));

    public Task StopAsync() => ExecuteAsync(
        static state => state.CanStop,
        RecordingReviewState.Stopping,
        _recordingCommands.StopAsync,
        Strings.Get("CannotStopRecording"));

    public void ShowRestartConfirmation()
    {
        bool changed = false;
        lock (_gate)
        {
            if (!_disposed && !_fixedCommandPending &&
                !_restartConfirmationVisible && _current.CanRestart)
            {
                _restartConfirmationVisible = true;
                changed = true;
            }
        }
        if (changed)
        {
            Publish();
        }
    }

    public void DismissRestartConfirmation()
    {
        bool changed = false;
        lock (_gate)
        {
            if (!_disposed && !_fixedCommandPending &&
                _restartConfirmationVisible)
            {
                _restartConfirmationVisible = false;
                changed = true;
            }
        }
        if (changed)
        {
            Publish();
        }
    }

    public Task DiscardCurrentRecordingAsync() => ExecuteAsync(
        static state => state.CanDiscardCurrentRecording,
        RecordingReviewState.Stopping,
        _recordingCommands.CancelAsync,
        Strings.Get("CannotDiscardRecording"),
        cancellationPending: true);

    public Task PauseOrResumeAsync()
    {
        RecordingPanelPresentationState state = CurrentState;
        return state.RecordingState == RecordingReviewState.Paused
            ? ExecuteAsync(
                static value => value.CanResume,
                pendingPhase: null,
                _recordingCommands.ResumeAsync,
                Strings.Get("CannotResumeRecording"))
            : ExecuteAsync(
                static value => value.CanPause,
                pendingPhase: null,
                _recordingCommands.PauseAsync,
                Strings.Get("CannotPauseRecording"));
    }

    public void SetTrayInFrame(bool trayInFrame)
    {
        if (!CurrentState.CanToggleTrayInFrame)
        {
            PublishActionError(Strings.Get("CannotChangeTray"));
            return;
        }

        RecorderCaptureVisibilityResult result =
            _captureVisibility.TrySetTrayInFrame(trayInFrame);
        lock (_gate)
        {
            _actionError = result.Succeeded
                ? string.Empty
                : Strings.Format("TrayChangeFailed", result.Failure) + "; " +
                    $"Win32={result.WindowsErrorCode}。";
        }
        Publish();
    }

    public void SetFrameRate(RecordingFrameRateMode frameRateMode)
    {
        if (!CurrentState.CanChangeFrameRate)
        {
            PublishActionError(Strings.Get("CannotChangeFrameRate"));
            return;
        }
        FrameRateMode selected = frameRateMode == RecordingFrameRateMode.Fps60
            ? FrameRateMode.Fps60
            : FrameRateMode.Fps30;
        ProductSettings previous = _productState.Current;
        NativeMethods.Result nativeResult =
            _native.SetRecordingFrameRate((uint)selected);
        if (nativeResult != NativeMethods.Result.Ok)
        {
            PublishActionError(Strings.Format("FrameRateChangeFailed",
                _native.GetLastError()));
            return;
        }
        try
        {
            _productState.Set(previous with { FrameRateMode = selected });
            _productState.Persist();
        }
        catch (Exception error)
        {
            _productState.Set(previous);
            NativeMethods.Result rollback =
                _native.SetRecordingFrameRate((uint)previous.FrameRateMode);
            string rollbackDetail = rollback == NativeMethods.Result.Ok
                ? string.Empty
                : Strings.Format("FrameRateRollbackFailed", _native.GetLastError());
            PublishActionError(
                Strings.Format("FrameRateSaveFailed", error.Message, rollbackDetail));
            return;
        }
        ClearActionError();
    }

    public async Task SetResolutionAsync(
        RecordingResolutionChoice resolutionChoice)
    {
        bool rejected;
        lock (_gate)
        {
            rejected = _disposed || _fixedCommandPending ||
                !_current.CanChangeResolution;
            if (rejected)
            {
                _actionError = Strings.Get("CannotChangeResolution");
            }
            else
            {
                _fixedCommandPending = true;
                _actionError = string.Empty;
            }
        }
        Publish();
        if (rejected)
        {
            return;
        }

        try
        {
            RecordingResolutionChangeResult result =
                await _resolutionCommands.SetResolutionAsync(
                    MapResolution(resolutionChoice)).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                lock (_gate)
                {
                    _actionError = result.Error;
                }
            }
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                _actionError = Strings.Format("ResolutionChangeFailed", error.Message);
            }
        }
        finally
        {
            lock (_gate)
            {
                _fixedCommandPending = false;
            }
            Publish();
        }
    }

    internal bool IsPassiveIdleConfigurationPending
    {
        get
        {
            lock (_gate)
            {
                return IsPassiveIdleConfigurationPendingUnsafe();
            }
        }
    }

    public void ChooseOutputRoot()
    {
        if (!CurrentState.CanChangePath)
        {
            PublishActionError(Strings.Get("CannotChangeSaveLocation"));
            return;
        }

        using FolderBrowserDialog dialog = new()
        {
            Description = Strings.Get("ChooseRecordingFolder"),
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(CanonicalOutputRoot)
                ? CanonicalOutputRoot
                : string.Empty,
        };
        if (dialog.ShowDialog(_owner) != DialogResult.OK)
        {
            return;
        }

        TryApplyOutputRoot(dialog.SelectedPath);
    }

    public void OpenRecording()
    {
        if (!CurrentState.CanOpenVideo ||
            !RecordingOutputActions.CanOpenVideo(
                _recordingTruth.CurrentSnapshot))
        {
            PublishActionError(Strings.Get("FinalVideoNotReady"));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(
                _recordingTruth.CurrentSnapshot.PublishedPath)
            {
                UseShellExecute = true,
            });
            ClearActionError();
        }
        catch (Exception error)
        {
            PublishActionError(Strings.Format("OpenVideoFailed", error.Message));
        }
    }

    public void OpenRecordingFolder()
    {
        if (!CurrentState.CanOpenFolder ||
            !RecordingOutputActions.CanOpenFolder(
                _recordingTruth.CurrentSnapshot))
        {
            PublishActionError(Strings.Get("FinalFolderNotReady"));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe")
            {
                Arguments =
                    $"/select,\"{_recordingTruth.CurrentSnapshot.PublishedPath}\"",
                UseShellExecute = true,
            });
            ClearActionError();
        }
        catch (Exception error)
        {
            PublishActionError(Strings.Format("OpenFolderFailed", error.Message));
        }
    }

    public void ReturnToRecordingReady()
    {
        lock (_gate)
        {
            if (!_current.CanDismissCompletion)
            {
                return;
            }
            _completionSummaryVisible = false;
            _actionError = string.Empty;
        }
        Publish();
    }

    internal void ReportActionError(string message) =>
        PublishActionError(message);

    internal bool TryApplyOutputRoot(string? selectedPath)
    {
        if (!CurrentState.CanChangePath)
        {
            PublishActionError(Strings.Get("CannotChangeSaveLocation"));
            return false;
        }
        if (!ProductPathContract.TryValidateOutputRoot(
                selectedPath, out string validated))
        {
            PublishActionError(Strings.Get("SaveLocationUnavailable"));
            return false;
        }

        ProductSettings previous = _productState.Current;
        string previousRoot = CanonicalOutputRoot;
        NativeMethods.Result nativeResult =
            _native.SetRecordingOutputRoot(validated);
        if (nativeResult != NativeMethods.Result.Ok)
        {
            PublishActionError(
                Strings.Format("SaveLocationRuntimeFailed", _native.GetLastError()));
            return false;
        }

        try
        {
            _productState.Set(previous with { OutputRoot = validated });
            _productState.Persist();
        }
        catch (Exception error)
        {
            _productState.Set(previous);
            NativeMethods.Result rollback =
                _native.SetRecordingOutputRoot(previousRoot);
            string rollbackDetail = rollback == NativeMethods.Result.Ok
                ? string.Empty
                : Strings.Format("SaveLocationRollbackFailed", _native.GetLastError());
            PublishActionError(
                Strings.Format("SaveLocationSaveFailed", error.Message, rollbackDetail));
            return false;
        }

        lock (_gate)
        {
            _canonicalOutputRoot = validated;
            _actionError = string.Empty;
        }
        Publish();
        return true;
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
        }
        _recordingCommands.SnapshotChanged -= OnRecordingSnapshotChanged;
        _captureVisibility.StateChanged -= OnCaptureVisibilityStateChanged;
    }

    private async Task ExecuteAsync(
        Func<RecordingPanelPresentationState, bool> allowed,
        RecordingReviewState? pendingPhase,
        Func<Task> command,
        string rejection,
        bool cancellationPending = false)
    {
        bool rejected;
        lock (_gate)
        {
            rejected =
                _disposed || _fixedCommandPending || !allowed(_current);
            if (rejected)
            {
                _actionError = rejection;
            }
            else
            {
                _fixedCommandPending = true;
                _pendingPhase = pendingPhase;
                _restartConfirmationVisible = false;
                _cancelCommandPending = cancellationPending;
                _actionError = string.Empty;
            }
        }
        Publish();
        if (rejected)
        {
            return;
        }

        try
        {
            await command().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                _actionError = error.Message;
            }
        }
        finally
        {
            lock (_gate)
            {
                _recording = _recordingCommands.CurrentSnapshot;
                ObserveCompletionLocked(_recording.State);
                _fixedCommandPending = false;
                _pendingPhase = null;
                _cancelCommandPending = false;
            }
            Publish();
        }
    }

    private void InitializeOutputRoot(string safeDefaultOutputRoot)
    {
        string fallback = Path.GetFullPath(safeDefaultOutputRoot);
        Directory.CreateDirectory(fallback);
        string candidate = _productState.Current.OutputRoot ?? fallback;
        if (!ProductPathContract.TryValidateOutputRoot(
                candidate, out string validated))
        {
            validated = fallback;
        }

        NativeMethods.Result result = _native.SetRecordingOutputRoot(validated);
        if (result != NativeMethods.Result.Ok &&
            !string.Equals(validated, fallback, StringComparison.OrdinalIgnoreCase))
        {
            validated = fallback;
            result = _native.SetRecordingOutputRoot(validated);
        }
        if (result != NativeMethods.Result.Ok)
        {
            throw new InvalidOperationException(
                Strings.Format("ApplySaveLocationFailed", _native.GetLastError()));
        }
        _canonicalOutputRoot = validated;
    }

    private void InitializeFrameRate()
    {
        NativeMethods.Result result = _native.SetRecordingFrameRate(
            (uint)_productState.Current.FrameRateMode);
        if (result != NativeMethods.Result.Ok)
        {
            throw new InvalidOperationException(
                Strings.Format("ApplyFrameRateFailed", _native.GetLastError()));
        }
    }

    private void OnRecordingSnapshotChanged(RecordingReviewSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _recording = snapshot;
            ObserveCompletionLocked(snapshot.State);
        }
        Publish();
    }

    private void ObserveCompletionLocked(RecordingReviewState state)
    {
        if (state is not (
            RecordingReviewState.Recording or RecordingReviewState.Paused))
        {
            _restartConfirmationVisible = false;
        }
        if (state == RecordingReviewState.Completed &&
            _lastRecordingState != RecordingReviewState.Completed)
        {
            _completionSummaryVisible = true;
        }
        else if (state != RecordingReviewState.Completed)
        {
            _completionSummaryVisible = false;
        }
        _lastRecordingState = state;
    }

    private void OnCaptureVisibilityStateChanged(object? sender, EventArgs e) =>
        Publish();

    private void ClearActionError()
    {
        lock (_gate)
        {
            _actionError = string.Empty;
        }
        Publish();
    }

    private void PublishActionError(string message)
    {
        lock (_gate)
        {
            _actionError = message ?? string.Empty;
        }
        Publish();
    }

    private void Publish()
    {
        RecordingPanelPresentationState next;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            next = BuildState();
            if (_current == next)
            {
                return;
            }
            _current = next;
        }
        StateChanged?.Invoke(next);
    }

    private RecordingPanelPresentationState BuildState()
    {
        RecordingReviewSnapshot recording = _recording;
        if (_fixedCommandPending && _pendingPhase is { } pendingPhase)
        {
            recording = recording with { State = pendingPhase };
        }
        ManagedRecordingSnapshot truth = _recordingTruth.CurrentSnapshot;
        RecorderCaptureVisibilityResult capture =
            _captureVisibility.LastResult;
        string captureDetail = capture.Succeeded
            ? _captureVisibility.TrayInFrame
                ? _captureVisibility.Phase == RecorderCapturePhase.Idle
                    ? Strings.Get("TrayReadyAllIncluded")
                    : Strings.Get("TrayMainExcluded")
                : Strings.Get("TrayAllExcluded")
            : Strings.Format("TrayPolicyFailed", capture.Failure,
                $"Win32={capture.WindowsErrorCode}");
        string publishedPath = truth.PublishedPath ?? string.Empty;
        bool changesPresentation =
            !IsPassiveIdleConfigurationPendingUnsafe();
        return RecordingPanelPresentationState.Create(
            recording,
            (changesPresentation && _fixedCommandPending) ||
                recording.CommandPending,
            _canonicalOutputRoot,
            truth.WorkingPath ?? string.Empty,
            truth.PlannedFinalPath ?? string.Empty,
            publishedPath,
            _captureVisibility.TrayInFrame,
            captureDetail,
            _completionSummaryVisible,
            FileExists(publishedPath),
            ParentDirectoryExists(publishedPath),
            actionError: _actionError,
            restartConfirmationVisible: _restartConfirmationVisible,
            cancellationPending: _cancelCommandPending,
            frameRateMode: _productState.Current.FrameRateMode ==
                    FrameRateMode.Fps60
                ? RecordingFrameRateMode.Fps60
                : RecordingFrameRateMode.Fps30,
            resolutionChoice: MapResolution(_resolutionCommands.CurrentMode),
            resolutionUpscalesSource:
                _resolutionCommands.CurrentSelectionUpscales);
    }

    private bool IsPassiveIdleConfigurationPendingUnsafe() =>
        _fixedCommandPending &&
        _pendingPhase is null &&
        _current.IdlePresentationVisible;

    private static RecordingResolutionMode MapResolution(
        RecordingResolutionChoice choice) => choice switch
        {
            RecordingResolutionChoice.Fhd1080 =>
                RecordingResolutionMode.Fhd1080,
            RecordingResolutionChoice.Qhd1440 =>
                RecordingResolutionMode.Qhd1440,
            RecordingResolutionChoice.Uhd2160 =>
                RecordingResolutionMode.Uhd2160,
            _ => RecordingResolutionMode.Original,
        };

    private static RecordingResolutionChoice MapResolution(
        RecordingResolutionMode mode) => mode switch
        {
            RecordingResolutionMode.Fhd1080 =>
                RecordingResolutionChoice.Fhd1080,
            RecordingResolutionMode.Qhd1440 =>
                RecordingResolutionChoice.Qhd1440,
            RecordingResolutionMode.Uhd2160 =>
                RecordingResolutionChoice.Uhd2160,
            _ => RecordingResolutionChoice.Original,
        };

    private static bool FileExists(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static bool ParentDirectoryExists(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            return !string.IsNullOrWhiteSpace(directory) &&
                Directory.Exists(directory);
        }
        catch
        {
            return false;
        }
    }
}
