using System.Diagnostics;

namespace XbPreview.Host;

internal sealed class RecoveryFolderNavigator
{
    private readonly Action<ProcessStartInfo> _start;

    internal RecoveryFolderNavigator()
        : this(static startInfo =>
        {
            _ = Process.Start(startInfo) ?? throw new InvalidOperationException(
                "Windows Explorer did not start.");
        })
    {
    }

    internal RecoveryFolderNavigator(Action<ProcessStartInfo> start)
    {
        _start = start ?? throw new ArgumentNullException(nameof(start));
    }

    internal void OpenContainingFolder(string candidatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        if (!Path.IsPathFullyQualified(candidatePath) ||
            candidatePath.IndexOf('\0') >= 0)
        {
            throw new ArgumentException(
                "Recovery navigation requires an absolute candidate path.",
                nameof(candidatePath));
        }

        ProcessStartInfo startInfo = new("explorer.exe")
        {
            Arguments = $"/select,\"{candidatePath}\"",
            UseShellExecute = true,
        };
        _start(startInfo);
    }
}
