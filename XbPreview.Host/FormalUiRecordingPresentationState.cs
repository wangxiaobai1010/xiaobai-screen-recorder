namespace XbPreview.Host;

// HUMAN REVIEW / PRESENTATION STATE ONLY.
// Production RecordingController / Timeline must replace this as the real state source.
internal enum FormalUiRecordingPresentationState
{
    Idle,
    Recording,
    Paused,
    Completed,
}

// HUMAN REVIEW / PRESENTATION ONLY.
// This simulates a successful saved result for visual review. It does not mean
// that Safe Publish or any production recording output is connected here.
internal static class FormalUiRecordingCompletedPresentation
{
    internal const string CompletedDirectory = @"D:\小白录屏\录制文件\";
    internal const string CompletedFileName = "LegacyReview_2026-08-17_140143.mp4";
}
