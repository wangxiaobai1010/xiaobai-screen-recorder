namespace XbPreview.Host;

internal sealed class StartupInspectionCoordinator : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IStartupSessionInspector _inspector;
    private CancellationTokenSource? _cancellation;
    private Task<StartupInspectionSnapshot>? _runTask;
    private StartupInspectionSnapshot _current =
        StartupInspectionSnapshot.NotStarted;
    private long _generation;
    private bool _disposed;

    internal StartupInspectionCoordinator(IStartupSessionInspector inspector)
    {
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    internal event Action<
        StartupInspectionCoordinator,
        StartupInspectionSnapshot>? SnapshotChanged;

    internal StartupInspectionSnapshot CurrentSnapshot
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    internal Task<StartupInspectionSnapshot> StartAsync()
    {
        StartupInspectionSnapshot running;
        Task<StartupInspectionSnapshot> task;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runTask is not null)
            {
                return _runTask;
            }

            long generation = ++_generation;
            _cancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = _cancellation.Token;
            running = new StartupInspectionSnapshot(
                generation,
                StartupInspectionState.Running,
                null,
                null);
            _current = running;
            task = Task.Run(
                () => Run(generation, cancellationToken),
                CancellationToken.None);
            _runTask = task;
        }

        Publish(running);
        return task;
    }

    internal void RequestCancellation()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            cancellation = _cancellation;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    internal async Task<StartupInspectionSnapshot> CancelAndWaitAsync()
    {
        Task<StartupInspectionSnapshot>? task;
        RequestCancellation();
        lock (_gate)
        {
            task = _runTask;
        }

        return task is null ? CurrentSnapshot : await task.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Task<StartupInspectionSnapshot>? task;
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            task = _runTask;
            cancellation = _cancellation;
        }

        RequestCancellation();
        if (task is not null)
        {
            await task.ConfigureAwait(false);
        }

        lock (_gate)
        {
            if (ReferenceEquals(_cancellation, cancellation))
            {
                _cancellation = null;
            }
        }
        cancellation?.Dispose();
    }

    private StartupInspectionSnapshot Run(
        long generation,
        CancellationToken cancellationToken)
    {
        StartupInspectionSnapshot terminal;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartupInspectionResult result = _inspector.Inspect(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            terminal = new StartupInspectionSnapshot(
                generation,
                StartupInspectionState.Completed,
                result,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            terminal = new StartupInspectionSnapshot(
                generation,
                StartupInspectionState.Canceled,
                null,
                null);
        }
        catch (Exception error)
        {
            terminal = new StartupInspectionSnapshot(
                generation,
                StartupInspectionState.Failed,
                null,
                $"{error.GetType().Name}: {error.Message}");
        }

        bool publish;
        lock (_gate)
        {
            publish = !_disposed && generation == _generation;
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

    private void Publish(StartupInspectionSnapshot snapshot)
    {
        Action<StartupInspectionCoordinator, StartupInspectionSnapshot>? handler;
        lock (_gate)
        {
            if (_disposed ||
                snapshot.Generation != _generation ||
                _current != snapshot)
            {
                return;
            }
            handler = SnapshotChanged;
        }

        if (handler is null)
        {
            return;
        }
        try
        {
            handler(this, snapshot);
        }
        catch
        {
            // Observers cannot own or change the immutable inspection result.
        }
    }
}
