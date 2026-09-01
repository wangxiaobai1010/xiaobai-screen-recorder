using XbPreview.Avalonia.Contracts;

namespace XbPreview.Host;

internal sealed class ProductionRecordingAdapter : IRecordingReviewController
{
    private static readonly TimeSpan TransitionTimeout =
        TimeSpan.FromSeconds(30);
    private readonly RecordingController _recording;
    private readonly Func<Task>? _beforeStart;
    private int _commandPending;
    private int _resumeCommandCount;

    internal ProductionRecordingAdapter(
        RecordingController recording,
        Func<Task>? beforeStart = null)
    {
        _recording = recording ??
            throw new ArgumentNullException(nameof(recording));
        _beforeStart = beforeStart;
    }

    public event Action<RecordingReviewSnapshot>? SnapshotChanged;

    public RecordingReviewSnapshot CurrentSnapshot =>
        Map(
            _recording.CurrentSnapshot,
            commandPending: Volatile.Read(ref _commandPending) != 0);

    internal int ResumeCommandCount => Volatile.Read(ref _resumeCommandCount);

    public async Task StartAsync()
    {
        if (_beforeStart is not null)
        {
            await _beforeStart().ConfigureAwait(false);
        }
        await ExecuteAsync(
            RecordingReviewState.Starting,
            _recording.StartAsync,
            ManagedRecordingState.Recording).ConfigureAwait(false);
    }

    public Task PauseAsync() => ExecuteAsync(
        pendingState: null,
        () => _recording.PauseAsync());

    public Task ResumeAsync()
    {
        Interlocked.Increment(ref _resumeCommandCount);
        return ExecuteAsync(
            pendingState: null,
            () => _recording.ResumeAsync());
    }

    public Task StopAsync() => ExecuteAsync(
        RecordingReviewState.Stopping,
        _recording.StopAsync,
        ManagedRecordingState.Completed);

    public Task CancelAsync() => ExecuteAsync(
        RecordingReviewState.Stopping,
        _recording.CancelAsync,
        ManagedRecordingState.UserCancelled);

    internal RecordingReviewSnapshot RefreshSnapshot()
    {
        RecordingReviewSnapshot snapshot = Map(
            _recording.RefreshSnapshot(),
            commandPending: Volatile.Read(ref _commandPending) != 0);
        Publish(snapshot);
        return snapshot;
    }

    private async Task ExecuteAsync(
        RecordingReviewState? pendingState,
        Func<Task<ManagedRecordingSnapshot>> command,
        ManagedRecordingState? acknowledgedState = null)
    {
        Interlocked.Increment(ref _commandPending);
        RecordingReviewSnapshot pending = Map(
            _recording.CurrentSnapshot,
            commandPending: true);
        if (pendingState is not null)
        {
            pending = pending with { State = pendingState.Value };
        }
        Publish(pending);

        try
        {
            ManagedRecordingSnapshot completed =
                await command().ConfigureAwait(false);
            if (acknowledgedState is not null)
            {
                completed = await AwaitAcknowledgedStateAsync(
                    completed,
                    acknowledgedState.Value).ConfigureAwait(false);
            }
            Publish(Map(completed, commandPending: false));
        }
        catch (Exception error)
        {
            Publish(Map(
                _recording.CurrentSnapshot,
                commandPending: false) with
            {
                State = RecordingReviewState.Failed,
                ErrorMessage = error.Message,
            });
        }
        finally
        {
            Interlocked.Decrement(ref _commandPending);
        }
    }

    private async Task<ManagedRecordingSnapshot> AwaitAcknowledgedStateAsync(
        ManagedRecordingSnapshot snapshot,
        ManagedRecordingState acknowledgedState)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TransitionTimeout;
        TimeSpan delay = TimeSpan.FromMilliseconds(10);
        while (snapshot.State != acknowledgedState && !snapshot.IsTerminal)
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out waiting for Production recording state " +
                    $"{acknowledgedState}; current state is {snapshot.State}.");
            }
            await Task.Delay(delay).ConfigureAwait(false);
            snapshot = _recording.RefreshSnapshot();
            delay = TimeSpan.FromMilliseconds(Math.Min(
                delay.TotalMilliseconds * 2,
                250));
        }
        return snapshot;
    }

    private void Publish(RecordingReviewSnapshot snapshot) =>
        SnapshotChanged?.Invoke(snapshot);

    private static RecordingReviewSnapshot Map(
        ManagedRecordingSnapshot snapshot,
        bool commandPending)
    {
        string outputPath = !string.IsNullOrWhiteSpace(snapshot.PublishedPath)
            ? snapshot.PublishedPath
            : !string.IsNullOrWhiteSpace(snapshot.WorkingPath)
                ? snapshot.WorkingPath
                : snapshot.PlannedFinalPath;
        return new RecordingReviewSnapshot(
            MapState(snapshot.State),
            commandPending,
            snapshot.SessionId,
            outputPath ?? string.Empty,
            snapshot.ErrorMessage ?? string.Empty,
            snapshot.Elapsed,
            snapshot.FramesSubmitted,
            snapshot.PauseCount,
            snapshot.TotalPaused,
            snapshot.ActiveEncoder,
            snapshot.OutputSuccess,
            snapshot.FinalizeAttempted,
            snapshot.FinalizeHResult,
            snapshot.FinalizeCount,
            snapshot.ReadyToPublish,
            snapshot.Published,
            snapshot.PublishAttempted,
            snapshot.PublishHResult,
            snapshot.ValidationAttempted,
            snapshot.ValidationHResult);
    }

    private static RecordingReviewState MapState(
        ManagedRecordingState state) => state switch
        {
            ManagedRecordingState.Idle => RecordingReviewState.Idle,
            ManagedRecordingState.Starting => RecordingReviewState.Starting,
            ManagedRecordingState.Recording or
                ManagedRecordingState.Pausing =>
                RecordingReviewState.Recording,
            ManagedRecordingState.Paused or
                ManagedRecordingState.Resuming =>
                RecordingReviewState.Paused,
            ManagedRecordingState.Stopping => RecordingReviewState.Stopping,
            ManagedRecordingState.Completed => RecordingReviewState.Completed,
            ManagedRecordingState.Failed => RecordingReviewState.Failed,
            ManagedRecordingState.UserCancelled => RecordingReviewState.Idle,
            _ => RecordingReviewState.Failed,
        };
}
