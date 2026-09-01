using System.Diagnostics;

namespace XbPreview.LongRun;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string repositoryRoot;
        try
        {
            repositoryRoot = RepositoryFacts.FindRepositoryRoot();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return (int)LongRunExitCode.UnhandledException;
        }

        if (args.Length == 1 && args[0] == "--self-test-arguments")
        {
            try
            {
                return LongRunOptionsTests.Run(repositoryRoot);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"LONG-RUN-ARGUMENT-TESTS: FAIL: {error.Message}");
                return (int)LongRunExitCode.UnhandledException;
            }
        }

        if (args.Length == 1 && args[0] == "--self-test-evidence-gates")
        {
            try
            {
                return LongRunEvidenceGateTests.Run(repositoryRoot);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(
                    $"LONG-RUN-EVIDENCE-GATE-TESTS: FAIL: {error}");
                return (int)LongRunExitCode.UnhandledException;
            }
        }

        if (args.Length == 1 && args[0] == "--self-test-final-hardening")
        {
            try
            {
                return LongRunFinalHardeningTests.RunAsync(repositoryRoot)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(
                    $"LONG-RUN-FINAL-HARDENING-TESTS: FAIL: {error}");
                return (int)LongRunExitCode.UnhandledException;
            }
        }

        if (!LongRunOptions.TryParse(args, repositoryRoot, out LongRunOptions? options, out string parseError))
        {
            Console.Error.WriteLine(parseError);
            PrintUsage();
            return (int)LongRunExitCode.InvalidArguments;
        }
        LongRunOptions parsedOptions = options!;

        string[] conflicts = FindConflictingProcesses();
        if (conflicts.Length != 0)
        {
            Console.Error.WriteLine("Related processes must be zero before start: " + string.Join(", ", conflicts));
            return (int)LongRunExitCode.PreviewStartFailed;
        }

        ApplicationConfiguration.Initialize();
        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
            Console.WriteLine("Cancellation requested; waiting for formal Stop/Finalize and Preview Close.");
        };
        Console.CancelKeyPress += cancelHandler;

        int exitCode = (int)LongRunExitCode.UnhandledException;
        bool allowClose = false;
        using Form window = new()
        {
            Text = $"XbPreview Long Run - {parsedOptions.RunId}",
            Width = 960,
            Height = 600,
            StartPosition = FormStartPosition.CenterScreen,
        };
        using Panel surface = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
        };
        window.Controls.Add(surface);
        window.FormClosing += (_, eventArgs) =>
        {
            if (!allowClose)
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            }
        };
        window.Shown += (_, _) => window.BeginInvoke(async () =>
        {
            try
            {
                surface.CreateControl();
                LongRunRunner runner = new(
                    parsedOptions,
                    window,
                    surface,
                    cancellation);
                exitCode = (int)await runner.RunAsync();
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Harness UI boundary failed: {error}");
                exitCode = (int)LongRunExitCode.UnhandledException;
            }
            finally
            {
                allowClose = true;
                window.Close();
            }
        });

        try
        {
            Application.Run(window);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
        return exitCode;
    }

    private static string[] FindConflictingProcesses()
    {
        int currentPid = Environment.ProcessId;
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase)
        {
            "XbPreview.Host",
            "XbPreview.Managed.Tests",
            "XbPreview.Native.Tests",
            "XbPreview.LongRun",
        };
        List<string> conflicts = [];
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id != currentPid &&
                    names.Contains(process.ProcessName))
                {
                    conflicts.Add(
                        $"{process.ProcessName}({process.Id})");
                }
            }
        }
        conflicts.Sort(StringComparer.Ordinal);
        return conflicts.ToArray();
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "Usage: XbPreview.LongRun --duration-seconds <positive> " +
            "[--sample-interval-ms <positive>] [--output-directory <path>] " +
            "[--run-id <id>] [--summary-json <path>] [--snapshots-jsonl <path>] " +
            "[--cancel-after-seconds <positive-less-than-duration>]");
    }
}
