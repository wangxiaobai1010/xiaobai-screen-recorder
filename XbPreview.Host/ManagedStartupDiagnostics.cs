using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace XbPreview.Host;

internal sealed record ManagedStartupDiagnosticEvent
{
    public string Event { get; init; } = "managed-startup";
    public string ManagedStage { get; init; } = "unknown";
    public string? StartupAttemptId { get; init; }
    public int? StartAttemptNumber { get; init; }
    public string? SessionGuid { get; init; }
    public int ThreadId { get; init; } = Environment.CurrentManagedThreadId;
    public long Qpc { get; init; } = Stopwatch.GetTimestamp();
    public DateTimeOffset Utc { get; init; } = DateTimeOffset.UtcNow;
    public bool? MainFormIsHandleCreated { get; init; }
    public long? MainFormHandle { get; init; }
    public bool? PreviewSurfaceIsHandleCreated { get; init; }
    public long? PreviewSurfaceHandle { get; init; }
    public bool? Visible { get; init; }
    public string? WindowState { get; init; }
    public bool? IsDisposed { get; init; }
    public bool? Disposing { get; init; }
    public string? LifecycleState { get; init; }
    public string? NativeHResult { get; init; }
    public bool? RetryAvailable { get; init; }
    public string? Result { get; init; }
}

internal static class ManagedStartupDiagnostics
{
    private static readonly object Gate = new();
    private static readonly ConcurrentQueue<ManagedStartupDiagnosticEvent>
        Pending = new();
    private static StreamWriter? _writer;
    private static string? _filePath;

    internal static string? FilePath
    {
        get
        {
            lock (Gate)
            {
                return _filePath;
            }
        }
    }

    internal static void Configure(string diagnosticDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticDirectory);
        lock (Gate)
        {
            if (_writer is not null)
            {
                return;
            }

            Directory.CreateDirectory(diagnosticDirectory);
            string fileName =
                $"p2.4-startup-managed-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-" +
                $"{Environment.ProcessId}.jsonl";
            _filePath = Path.Combine(diagnosticDirectory, fileName);
            _writer = new StreamWriter(
                new FileStream(
                    _filePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };

            while (Pending.TryDequeue(out ManagedStartupDiagnosticEvent? item))
            {
                WriteUnlocked(item);
            }
        }
    }

    internal static void Write(ManagedStartupDiagnosticEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (Gate)
        {
            if (_writer is null)
            {
                Pending.Enqueue(item);
                return;
            }
            WriteUnlocked(item);
        }
    }

    internal static void Close()
    {
        lock (Gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _writer?.Dispose();
            _writer = null;
            _filePath = null;
            while (Pending.TryDequeue(out _))
            {
            }
        }
    }

    private static void WriteUnlocked(ManagedStartupDiagnosticEvent item)
    {
        _writer!.WriteLine(JsonSerializer.Serialize(item, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
        }));
    }
}
