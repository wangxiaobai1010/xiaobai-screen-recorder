using System.Text.RegularExpressions;

namespace XbPreview.Managed.Tests;

internal static class NormalExitCodeContractTests
{
    internal static void Run()
    {
        string host = File.ReadAllText(Path.Combine(
            Environment.CurrentDirectory,
            "XbPreview.Host",
            "StructuralAvaloniaShellHost.cs"));
        string startup = Slice(
            host,
            "private async void OnShown",
            "private async void OnFormClosing");
        string shutdown = Slice(
            host,
            "private async void OnFormClosing",
            "private async Task DisposeRecoveryAsync");

        Require(
            Regex.Matches(
                startup,
                @"if \(_closeCleanupStarted\)\s*\{\s*return;\s*\}").Count == 7,
            "every asynchronous production-start seam yields to normal close");
        Require(
            startup.Contains("Environment.ExitCode = 1;", StringComparison.Ordinal),
            "genuine startup failure remains non-zero");
        Require(
            shutdown.Contains("Environment.ExitCode = 1;", StringComparison.Ordinal),
            "genuine shutdown exception remains non-zero");
        Require(
            shutdown.Contains(
                "await _recordingController.StopForCloseAsync();",
                StringComparison.Ordinal) &&
            shutdown.Contains(
                "await _lifecycle.DisposeAsync();",
                StringComparison.Ordinal) &&
            shutdown.Contains("BeginInvoke(Close);", StringComparison.Ordinal),
            "recording stop, lifecycle disposal, and final close remain ordered");
        Require(
            !host.Contains("Environment.ExitCode = 0;", StringComparison.Ordinal),
            "normal success uses the mature default process exit code");
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start + startMarker.Length,
            StringComparison.Ordinal);
        Require(start >= 0 && end > start,
            $"source markers exist: {startMarker} -> {endMarker}");
        return source[start..end];
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
