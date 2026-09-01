using System.Reflection;
using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Views;
using XbPreview.Avalonia.Views.Panels;

namespace XbPreview.Managed.Tests;

internal static class Panel1PreparationPolicyTests
{
    internal static void Run()
    {
        NoOpAndGroupedApplyContracts();
        RequireMode(false, false, Panel1AudioProgramMode.None);
        RequireMode(false, true, Panel1AudioProgramMode.SystemOnly);
        RequireMode(true, false, Panel1AudioProgramMode.MicrophoneOnly);
        RequireMode(true, true, Panel1AudioProgramMode.Dual);
        RequireIndicator(
            available: false,
            enabled: false,
            Panel1AudioSourceIndicator.Unavailable);
        RequireIndicator(
            available: false,
            enabled: true,
            Panel1AudioSourceIndicator.Unavailable);
        RequireIndicator(
            available: true,
            enabled: false,
            Panel1AudioSourceIndicator.Available);
        RequireIndicator(
            available: true,
            enabled: true,
            Panel1AudioSourceIndicator.Ready);

        Panel1ControlAvailability idle = Resolve(
            RecordingReviewState.Idle);
        Require(
            idle.CaptureEnabled && idle.AudioEnabled && idle.CursorEnabled,
            "Idle keeps Capture, Audio, and Cursor editable");

        Panel1ControlAvailability recording = Resolve(
            RecordingReviewState.Recording);
        Require(
            !recording.CaptureEnabled &&
            !recording.AudioEnabled &&
            recording.CursorEnabled,
            "Recording locks Capture/Audio and keeps Cursor editable");

        Panel1ControlAvailability paused = Resolve(
            RecordingReviewState.Paused);
        Require(
            !paused.CaptureEnabled &&
            !paused.AudioEnabled &&
            paused.CursorEnabled,
            "Paused locks Capture/Audio and keeps Cursor editable");

        Panel1ControlAvailability commandPending =
            Panel1PreparationPolicy.ResolveControlAvailability(
                RecordingReviewState.Idle,
                recordingCommandPending: true,
                captureCommandPending: false,
                audioCommandPending: false,
                cursorCommandPending: false);
        Require(
            !commandPending.CaptureEnabled &&
            !commandPending.AudioEnabled &&
            commandPending.CursorEnabled,
            "pending recording start locks Capture/Audio before Start");
    }

    private static void NoOpAndGroupedApplyContracts()
    {
        Panel1PreparationSnapshot baseline =
            Panel1PreparationSnapshot.Initial with
            {
                PresentationRevision = 7,
                WindowChoices =
                [
                    new StructuralCaptureWindowChoice((nint)123, "Window A"),
                ],
                MicrophoneDevices =
                [
                    new Panel1MicrophoneChoice(
                        "mic-a",
                        "Microphone A",
                        IsWindowsDefault: false,
                        Available: true),
                ],
                CaptureControlsEnabled = true,
                AudioControlsEnabled = true,
                CursorControlEnabled = true,
            };
        Panel1PreparationSnapshot equivalent = baseline with
        {
            PresentationRevision = 99,
            WindowChoices = baseline.WindowChoices.ToArray(),
            MicrophoneDevices = baseline.MicrophoneDevices.ToArray(),
        };
        Require(
            InvokeSemanticEquality(baseline, equivalent),
            "Panel 1 semantic equality ignores Revision and list identity");
        Require(
            !InvokeSemanticEquality(
                baseline,
                equivalent with { CaptureControlsEnabled = false }),
            "Panel 1 semantic equality preserves real availability changes");

        Panel1PreparationSnapshot meterOnly = baseline with
        {
            PresentationRevision = 8,
            SystemMeterActiveSegments = 4,
            SystemMeterAvailable = true,
        };
        Require(
            !InvokeGroupChange("Capture", baseline, meterOnly) &&
            !InvokeGroupChange("Cursor", baseline, meterOnly) &&
            !InvokeGroupChange("Microphone", baseline, meterOnly) &&
            !InvokeGroupChange("SystemAudio", baseline, meterOnly) &&
            InvokeGroupChange("Meter", baseline, meterOnly),
            "meter-only state changes only the meter presentation group");
        Panel1PreparationSnapshot capturePending = baseline with
        {
            PresentationRevision = 8,
            CaptureControlsEnabled = false,
        };
        Require(
            InvokeGroupChange("Capture", baseline, capturePending) &&
            !InvokeGroupChange("Cursor", baseline, capturePending) &&
            !InvokeGroupChange("Microphone", baseline, capturePending) &&
            !InvokeGroupChange("SystemAudio", baseline, capturePending) &&
            !InvokeGroupChange("Meter", baseline, capturePending),
            "real capture pending changes only the capture presentation group");

        string root = Environment.CurrentDirectory;
        string adapter = File.ReadAllText(Path.Combine(
            root,
            "XbPreview.Host",
            "Panel1PreparationAdapter.cs"));
        int semanticGuard = adapter.IndexOf(
            "if (HasSameSemanticState(current, candidate))",
            StringComparison.Ordinal);
        int revisionIncrement = adapter.IndexOf(
            "PresentationRevision = ++_snapshotRevision",
            StringComparison.Ordinal);
        int publication = adapter.IndexOf(
            "SnapshotChanged?.Invoke(next)",
            StringComparison.Ordinal);
        Require(
            semanticGuard >= 0 &&
            revisionIncrement > semanticGuard &&
            publication > revisionIncrement &&
            adapter.Contains(
                "current.WindowChoices.SequenceEqual(candidate.WindowChoices)",
                StringComparison.Ordinal) &&
            adapter.Contains(
                "current.MicrophoneDevices.SequenceEqual(",
                StringComparison.Ordinal) &&
            adapter.Contains(
                "new(TimeSpan.FromMilliseconds(80))",
                StringComparison.Ordinal),
            "Panel 1 suppresses semantic no-ops before Revision/publication");

        string view = File.ReadAllText(Path.Combine(
            root,
            "XbPreview.Avalonia",
            "Views",
            "Panels",
            "CapturePanelView.axaml.cs"));
        foreach (string group in new[]
        {
            "Capture",
            "Cursor",
            "Microphone",
            "SystemAudio",
            "Meter",
        })
        {
            Require(
                view.Contains(
                    $"if ({group}PresentationChanged(previous, snapshot))",
                    StringComparison.Ordinal) &&
                view.Contains(
                    $"Apply{group}Presentation(snapshot);",
                    StringComparison.Ordinal),
                $"Panel 1 {group} presentation applies only on semantic change");
        }
        Require(
            view.Contains(
                "new(TimeSpan.FromMilliseconds(80))",
                StringComparison.Ordinal) == false,
            "Capture view does not own or alter the frozen 80 ms meter timer");
    }

    private static bool InvokeSemanticEquality(
        Panel1PreparationSnapshot current,
        Panel1PreparationSnapshot candidate)
    {
        Assembly host = Assembly.LoadFrom(Path.Combine(
            AppContext.BaseDirectory,
            "XiaobaiRecorder.dll"));
        Type adapter = host.GetType(
            "XbPreview.Host.Panel1PreparationAdapter",
            throwOnError: true)!;
        MethodInfo method = adapter.GetMethod(
            "HasSameSemanticState",
            BindingFlags.NonPublic | BindingFlags.Static) ??
            throw new InvalidOperationException(
                "Panel 1 semantic equality method is unavailable.");
        return (bool)method.Invoke(null, [current, candidate])!;
    }

    private static bool InvokeGroupChange(
        string group,
        Panel1PreparationSnapshot previous,
        Panel1PreparationSnapshot current)
    {
        MethodInfo method = typeof(CapturePanelView).GetMethod(
            $"{group}PresentationChanged",
            BindingFlags.NonPublic | BindingFlags.Static) ??
            throw new InvalidOperationException(
                $"Panel 1 {group} presentation comparer is unavailable.");
        return (bool)method.Invoke(null, [previous, current])!;
    }

    private static Panel1ControlAvailability Resolve(
        RecordingReviewState state) =>
        Panel1PreparationPolicy.ResolveControlAvailability(
            state,
            recordingCommandPending: false,
            captureCommandPending: false,
            audioCommandPending: false,
            cursorCommandPending: false);

    private static void RequireMode(
        bool microphoneEnabled,
        bool systemAudioEnabled,
        Panel1AudioProgramMode expected)
    {
        Panel1AudioProgramMode actual =
            Panel1PreparationPolicy.ResolveAudioProgramMode(
                microphoneEnabled,
                systemAudioEnabled);
        Require(
            actual == expected,
            $"audio mode {microphoneEnabled}/{systemAudioEnabled}: " +
            $"{actual} != {expected}");
    }

    private static void RequireIndicator(
        bool available,
        bool enabled,
        Panel1AudioSourceIndicator expected)
    {
        Panel1AudioSourceIndicator actual =
            Panel1PreparationPolicy.ResolveAudioSourceIndicator(
                available,
                enabled);
        Require(
            actual == expected,
            $"audio source indicator {available}/{enabled}: " +
            $"{actual} != {expected}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
