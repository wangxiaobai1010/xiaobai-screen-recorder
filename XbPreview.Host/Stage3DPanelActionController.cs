using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Views.Panels;

namespace XbPreview.Host;

/// <summary>
/// Owns the thin Panel 3 command boundary. It can issue only Stage commands,
/// so a Return cannot reset or otherwise mutate Panel 2 camera zoom.
/// </summary>
internal sealed class Stage3DPanelActionController
{
    private readonly Stage3DPanelPresentationState _presentationState;
    private readonly Func<IWindowShowcaseStageCommands?> _sessionProvider;

    internal Stage3DPanelActionController(
        Stage3DPanelPresentationState presentationState,
        Func<IWindowShowcaseStageCommands?> sessionProvider)
    {
        _presentationState = presentationState ??
            throw new ArgumentNullException(nameof(presentationState));
        _sessionProvider = sessionProvider ??
            throw new ArgumentNullException(nameof(sessionProvider));
    }

    internal static bool RecordingAllowsPoseActions(
        RecordingPanelPresentationState? recordingState) =>
        recordingState is null || RecordingAllowsPoseActions(
            recordingState.RecordingState,
            recordingState.CommandPending);

    internal static bool RecordingAllowsPoseActions(
        RecordingReviewState recordingState,
        bool commandPending) =>
        !commandPending && recordingState is not (
            RecordingReviewState.Starting or
            RecordingReviewState.Stopping);

    internal NativeMethods.Result Execute(
        Stage3DPanelInteractionCommand command,
        bool actionsEnabled)
    {
        ArgumentNullException.ThrowIfNull(command);
        Stage3DPanelPresentationSnapshot current =
            _presentationState.Snapshot;

        // While Flat, Level buttons update only latent Level. Direction is the
        // sole interaction that re-enters a 2.5D pose.
        if (!current.IsActive && !command.IsActive)
        {
            _presentationState.Apply(current with
            {
                Level = command.Level,
                ActionsEnabled = actionsEnabled,
            });
            return NativeMethods.Result.Ok;
        }

        IWindowShowcaseStageCommands? session = _sessionProvider();
        if (session is null)
        {
            return NativeMethods.Result.InvalidState;
        }

        NativeMethods.Result result = session.SetWindowShowcasePose(
            (NativeMethods.WindowStageOrientation)command.Orientation,
            (NativeMethods.WindowStageLevel)command.Level,
            command.IsActive);
        if (result == NativeMethods.Result.Ok)
        {
            _presentationState.Apply(new(
                command.Orientation,
                command.Level,
                command.IsActive,
                actionsEnabled));
        }
        return result;
    }
}
