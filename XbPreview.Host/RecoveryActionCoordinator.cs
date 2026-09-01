using XbPreview.Avalonia.Localization;

namespace XbPreview.Host;

internal sealed class RecoveryActionCoordinator : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IUserRecoveryService _recoveryService;
    private readonly IStartupSessionInspector _rescanInspector;
    private CancellationTokenSource? _cancellation;
    private Task<RecoveryAttemptSnapshot>? _activeTask;
    private RecoveryAttemptSnapshot _current =
        RecoveryAttemptSnapshot.NotStarted;
    private long _generation;
    private bool _disposed;

    internal RecoveryActionCoordinator(
        IUserRecoveryService recoveryService,
        IStartupSessionInspector rescanInspector)
    {
        _recoveryService = recoveryService ??
            throw new ArgumentNullException(nameof(recoveryService));
        _rescanInspector = rescanInspector ??
            throw new ArgumentNullException(nameof(rescanInspector));
    }

    internal event Action<RecoveryActionCoordinator, RecoveryAttemptSnapshot>?
        SnapshotChanged;

    internal RecoveryAttemptSnapshot CurrentSnapshot
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    internal Task<RecoveryAttemptSnapshot> StartAsync(
        UserRecoveryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.CanTryRecovery || !candidate.ObservedRevision.HasValue)
        {
            throw new InvalidOperationException(
                "Only an explicitly presented recovery candidate can run.");
        }

        RecoveryAttemptSnapshot running;
        Task<RecoveryAttemptSnapshot> task;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_activeTask is { IsCompleted: false })
            {
                return _activeTask;
            }

            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            long generation = ++_generation;
            running = new RecoveryAttemptSnapshot(
                generation,
                RecoveryAttemptState.Running,
                candidate.SessionId,
                null,
                null,
                false,
                Strings.Get("RecoveryChecking"),
                null);
            _current = running;
            CancellationToken token = _cancellation.Token;
            task = Task.Run(() => Run(candidate, generation, token));
            _activeTask = task;
        }
        Publish(running);
        return task;
    }

    internal void RequestCancellation()
    {
        lock (_gate)
        {
            _cancellation?.Cancel();
        }
    }

    internal async Task<RecoveryAttemptSnapshot> CancelAndWaitAsync()
    {
        Task<RecoveryAttemptSnapshot>? task;
        lock (_gate)
        {
            _cancellation?.Cancel();
            task = _activeTask;
        }
        return task is null
            ? CurrentSnapshot
            : await task.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Task<RecoveryAttemptSnapshot>? task;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _cancellation?.Cancel();
            task = _activeTask;
        }
        if (task is not null)
        {
            _ = await task.ConfigureAwait(false);
        }
        lock (_gate)
        {
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private RecoveryAttemptSnapshot Run(
        UserRecoveryCandidate candidate,
        long generation,
        CancellationToken cancellationToken)
    {
        RecoveryAttemptSnapshot terminal;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            NarrowRecoveryResult native = _recoveryService.Recover(
                candidate.SessionId,
                candidate.ObservedRevision!.Value,
                cancellationToken);
            StartupInspectionResult? rescan = null;
            bool confirmed = false;
            if (native.RequiresConfirmationRescan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                rescan = _rescanInspector.Inspect(cancellationToken);
                confirmed = rescan.Sessions.Any(session =>
                    string.Equals(
                        session.SessionId,
                        candidate.SessionId,
                        StringComparison.Ordinal) &&
                    session.Classification ==
                        HistoricalSessionClassification.
                            ReconciledCompletedConsistent);
            }
            terminal = new RecoveryAttemptSnapshot(
                generation,
                RecoveryAttemptState.Completed,
                candidate.SessionId,
                native,
                rescan,
                confirmed,
                Describe(native.Status, confirmed),
                null);
        }
        catch (OperationCanceledException)
        {
            terminal = new RecoveryAttemptSnapshot(
                generation,
                RecoveryAttemptState.Canceled,
                candidate.SessionId,
                null,
                null,
                false,
                Strings.Get("RecoveryCanceledStatus"),
                null);
        }
        catch (Exception error)
        {
            terminal = new RecoveryAttemptSnapshot(
                generation,
                RecoveryAttemptState.Failed,
                candidate.SessionId,
                null,
                null,
                false,
                Strings.Get("RecoveryReadFailureStatus"),
                $"{error.GetType().Name}: {error.Message}");
        }

        bool publish;
        lock (_gate)
        {
            publish = generation == _generation && !_disposed;
            if (publish)
            {
                _current = terminal;
            }
        }
        if (publish)
        {
            Publish(terminal);
        }
        return terminal;
    }

    private void Publish(RecoveryAttemptSnapshot snapshot)
    {
        Delegate[] handlers = SnapshotChanged?.GetInvocationList() ?? [];
        foreach (Delegate handler in handlers)
        {
            try
            {
                ((Action<RecoveryActionCoordinator, RecoveryAttemptSnapshot>)
                    handler)(this, snapshot);
            }
            catch
            {
                // Observers cannot change the native result or leave the
                // coordinator task unobserved.
            }
        }
    }

    private static string Describe(
        NarrowRecoveryStatus status,
        bool confirmedRecovered)
    {
        if (confirmedRecovered)
        {
            return Strings.Get("RecoveryRecoveredNotice");
        }
        return status switch
        {
            NarrowRecoveryStatus.Reconciled or
            NarrowRecoveryStatus.AlreadyReconciled or
            NarrowRecoveryStatus.GuardRejected or
            NarrowRecoveryStatus.RevisionChanged or
            NarrowRecoveryStatus.ConcurrentChange or
            NarrowRecoveryStatus.EvidenceInsufficient =>
                Strings.Get("RecoveryChangedStatus"),
            NarrowRecoveryStatus.IoFailure or
            NarrowRecoveryStatus.Unknown =>
                Strings.Get("RecoveryReadFailureStatus"),
            _ => Strings.Get("RecoveryUnavailableStatus"),
        };
    }
}
