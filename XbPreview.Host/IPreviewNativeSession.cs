namespace XbPreview.Host;

internal interface IRecordingNativeSession
{
    NativeMethods.Result StartRecording();

    NativeMethods.Result PauseRecording() =>
        NativeMethods.Result.InvalidState;

    NativeMethods.Result ResumeRecording() =>
        NativeMethods.Result.InvalidState;

    NativeMethods.Result SetAudioProgramMode(
        NativeMethods.AudioProgramMode mode);

    MicrophoneDeviceCatalog GetMicrophoneDevices() =>
        MicrophoneDeviceCatalog.Empty;

    NativeMethods.Result SetMicrophoneSelection(
        MicrophoneSelection selection) => NativeMethods.Result.Ok;

    MicrophoneSelectionStatus GetMicrophoneSelection() =>
        MicrophoneSelectionStatus.UnavailableDefault;

    NativeMethods.Result StopRecording();

    NativeMethods.Result CancelRecording() =>
        NativeMethods.Result.InvalidState;

    NativeMethods.RecordingSnapshot GetRecordingSnapshot();

    NativeMethods.Result SetAudioControls(
        bool systemMuted,
        bool microphoneMuted,
        double microphoneGainDb);

    NativeMethods.AudioControlSnapshotV1 GetAudioControlSnapshot();

    string GetLastError();
}

internal interface IWindowShowcaseStageCommands
{
    NativeMethods.Result SetWindowShowcasePose(
        NativeMethods.WindowStageOrientation orientation,
        NativeMethods.WindowStageLevel level,
        bool active) => NativeMethods.Result.Ok;
}

internal interface IWindowShowcaseBackgroundCommands
{
    NativeMethods.Result SetWindowShowcaseBackgroundPreset(
        NativeMethods.WindowShowcaseBackgroundPreset preset) =>
        NativeMethods.Result.Ok;

    NativeMethods.Result SetWindowShowcaseCustomBackground(
        string validatedLocalPath) => NativeMethods.Result.Ok;
}

internal interface IPreviewNativeSession :
    IRecordingNativeSession,
    IWindowShowcaseStageCommands,
    IWindowShowcaseBackgroundCommands,
    IDisposable
{
    NativeMethods.Result Start();

    NativeMethods.Result Stop();

    NativeMethods.Result Resize(int width, int height);

    NativeMethods.Result SetSessionGeometry(
        in SessionGeometryNativeV1 geometry);

    NativeMethods.Result SetCameraState(CameraState state);

    NativeMethods.Result SetCursorMode(NativeMethods.CursorMode cursorMode);

    NativeMethods.Result SetRecordCursorVisible(bool visible) =>
        NativeMethods.Result.InvalidState;

    RecordCursorVisibilitySnapshot GetRecordCursorVisible() =>
        new(true, true, 0);

    NativeMethods.Result SetCaptureTarget(CaptureTarget target) =>
        NativeMethods.Result.Ok;

    NativeMethods.Result SetWindowStagePose(
        NativeMethods.WindowStageOrientation orientation,
        NativeMethods.WindowStageLevel level) => NativeMethods.Result.Ok;

    NativeMethods.Result SetRecordingOutputRoot(string? validatedLocalPath) =>
        NativeMethods.Result.Ok;

    NativeMethods.Result SetRecordingFrameRate(uint framesPerSecond) =>
        NativeMethods.Result.Ok;

    NativeMethods.CursorStats GetCursorStats();

    NativeMethods.PreviewStats GetStats();

    bool TryGetGpuExportFrame(out NativeMethods.GpuExportFrameV1 frame)
    {
        frame = default;
        return false;
    }

}

internal interface IPreviewCameraUpdateService : IAsyncDisposable
{
    event Action<CameraState, NativeMethods.Result>? StatePublished;

    event Action<ComfortZoneFollowStep>? FollowStatePublished;

    void SetFollowEnabled(bool enabled);

    void Start();

    ValueTask StopAsync();
}
