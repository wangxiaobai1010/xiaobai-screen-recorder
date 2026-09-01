using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static class AudioLevelMeterPresentationTests
{
    internal static void Run()
    {
        StationaryNoiseSettlesToNoMeaningfulActivity();
        SpeechAboveLearnedFloorBecomesVisible();
        BindingResetRequiresFreshWarmup();
        RecordingSourceHandoffUsesEndpointPeakAndFreshBaseline();
        SystemAndMicrophonePresentationRemainIndependent();
    }

    private static void StationaryNoiseSettlesToNoMeaningfulActivity()
    {
        Panel1AudioMeterPresentation presentation = new();
        int maximumAfterWarmup = 0;
        for (int index = 0; index < 50; index++)
        {
            double dbfs = -54.0 + ((index % 5) - 2) * 0.35;
            Panel1AudioMeterPresentationSample sample = presentation.Update(
                MicrophoneSample(dbfs),
                TimeSpan.FromMilliseconds(index * 80));
            if (index >= 10)
            {
                maximumAfterWarmup = Math.Max(
                    maximumAfterWarmup,
                    sample.MicrophoneActiveSegments);
            }
        }
        Require(
            maximumAfterWarmup == 0,
            "stationary RMS noise must settle to zero meaningful segments");
    }

    private static void SpeechAboveLearnedFloorBecomesVisible()
    {
        Panel1AudioMeterPresentation presentation = new();
        for (int index = 0; index < 25; index++)
        {
            _ = presentation.Update(
                MicrophoneSample(-54.0),
                TimeSpan.FromMilliseconds(index * 80));
        }

        int maximum = 0;
        for (int index = 25; index < 30; index++)
        {
            Panel1AudioMeterPresentationSample sample = presentation.Update(
                MicrophoneSample(-30.0),
                TimeSpan.FromMilliseconds(index * 80));
            maximum = Math.Max(maximum, sample.MicrophoneActiveSegments);
        }
        Require(
            maximum >= 5,
            "speech 24 dB above the learned floor must be clearly visible");
    }

    private static void BindingResetRequiresFreshWarmup()
    {
        Panel1AudioMeterPresentation presentation = new();
        for (int index = 0; index < 20; index++)
        {
            _ = presentation.Update(
                MicrophoneSample(-54.0),
                TimeSpan.FromMilliseconds(index * 80));
        }
        presentation.ResetMicrophone();
        Panel1AudioMeterPresentationSample afterReset = presentation.Update(
            MicrophoneSample(-30.0),
            TimeSpan.FromMilliseconds(1_680));
        Require(
            afterReset.MicrophoneActiveSegments == 0,
            "a new microphone binding must start with a fresh baseline warmup");
    }

    private static void SystemAndMicrophonePresentationRemainIndependent()
    {
        Panel1AudioMeterPresentation presentation = new();
        Panel1AudioMeterPresentationSample activeSystem = presentation.Update(
            new Panel1AudioMeterSourceSample(
                SystemSourceEnabled: true,
                SystemMeterAvailable: true,
                SystemPeakAbsolutePcm16: 8_192,
                MicrophoneSourceEnabled: false,
                MicrophoneMeterAvailable: false,
                MicrophoneSource:
                    Panel1MicrophoneMeterSource.PreflightRms,
                MicrophoneLevelPcm16: 0.0),
            TimeSpan.Zero);
        Require(
            activeSystem.SystemActiveSegments > 0 &&
            activeSystem.MicrophoneActiveSegments == 0,
            "system activity must not activate the microphone meter");

        Panel1AudioMeterPresentationSample disabledSystem =
            presentation.Update(
                new Panel1AudioMeterSourceSample(
                    SystemSourceEnabled: false,
                    SystemMeterAvailable: true,
                    SystemPeakAbsolutePcm16: 32_767,
                    MicrophoneSourceEnabled: false,
                    MicrophoneMeterAvailable: false,
                    MicrophoneSource:
                        Panel1MicrophoneMeterSource.PreflightRms,
                    MicrophoneLevelPcm16: 0.0),
                TimeSpan.FromMilliseconds(80));
        Require(
            disabledSystem.SystemActiveSegments == 0,
            "a disabled System source must immediately present zero segments");
    }

    private static void RecordingSourceHandoffUsesEndpointPeakAndFreshBaseline()
    {
        Require(
            Panel1MicrophoneMeterSourcePolicy.Resolve(
                XbPreview.Avalonia.Contracts.RecordingReviewState.Idle) ==
                Panel1MicrophoneMeterSource.PreflightRms,
            "Idle must use the frozen preflight RMS source");
        Require(
            Panel1MicrophoneMeterSourcePolicy.Resolve(
                XbPreview.Avalonia.Contracts.RecordingReviewState.Starting) ==
                Panel1MicrophoneMeterSource.PreflightRms,
            "Starting must not claim endpoint peak before Recording owns it");
        foreach (XbPreview.Avalonia.Contracts.RecordingReviewState phase in
            new[]
            {
                XbPreview.Avalonia.Contracts.RecordingReviewState.Recording,
                XbPreview.Avalonia.Contracts.RecordingReviewState.Paused,
                XbPreview.Avalonia.Contracts.RecordingReviewState.Stopping,
            })
        {
            Require(
                Panel1MicrophoneMeterSourcePolicy.Resolve(phase) ==
                    Panel1MicrophoneMeterSource.RecordingEndpointPeak,
                $"{phase} must use the locked recording endpoint peak");
        }
        Require(
            Panel1MicrophoneMeterSourcePolicy.Resolve(
                XbPreview.Avalonia.Contracts.RecordingReviewState.Completed) ==
                Panel1MicrophoneMeterSource.PreflightRms,
            "Stop completion must return to the frozen preflight source");

        Panel1AudioMeterPresentation presentation = new();
        for (int index = 0; index < 20; index++)
        {
            _ = presentation.Update(
                MicrophoneSample(-54.0),
                TimeSpan.FromMilliseconds(index * 80));
        }

        Panel1AudioMeterPresentationSample firstRecording =
            presentation.Update(
                MicrophoneSample(
                    -54.0,
                    Panel1MicrophoneMeterSource.RecordingEndpointPeak),
                TimeSpan.FromMilliseconds(1_600));
        Require(
            firstRecording.MicrophoneActiveSegments == 0,
            "Idle-to-Recording handoff must start a fresh baseline warmup");
        for (int index = 21; index < 30; index++)
        {
            _ = presentation.Update(
                MicrophoneSample(
                    -54.0,
                    Panel1MicrophoneMeterSource.RecordingEndpointPeak),
                TimeSpan.FromMilliseconds(index * 80));
        }

        int recordingMaximum = 0;
        for (int index = 30; index < 35; index++)
        {
            Panel1AudioMeterPresentationSample sample = presentation.Update(
                MicrophoneSample(
                    -30.0,
                    Panel1MicrophoneMeterSource.RecordingEndpointPeak),
                TimeSpan.FromMilliseconds(index * 80));
            recordingMaximum = Math.Max(
                recordingMaximum,
                sample.MicrophoneActiveSegments);
        }
        Require(
            recordingMaximum >= 5,
            "recording endpoint peak must drive meaningful Mic activity");

        Panel1AudioMeterPresentationSample returnedIdle =
            presentation.Update(
                MicrophoneSample(-30.0),
                TimeSpan.FromMilliseconds(2_800));
        Require(
            returnedIdle.MicrophoneActiveSegments == 0,
            "Recording-to-Idle handoff must reset stale endpoint activity");
    }

    private static Panel1AudioMeterSourceSample MicrophoneSample(
        double levelDbfs,
        Panel1MicrophoneMeterSource source =
            Panel1MicrophoneMeterSource.PreflightRms) => new(
            SystemSourceEnabled: false,
            SystemMeterAvailable: false,
            SystemPeakAbsolutePcm16: 0,
            MicrophoneSourceEnabled: true,
            MicrophoneMeterAvailable: true,
            MicrophoneSource: source,
            MicrophoneLevelPcm16:
                32768.0 * Math.Pow(10.0, levelDbfs / 20.0));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
