using XbPreview.Avalonia.Contracts;

namespace XbPreview.Host;

internal enum Panel1MicrophoneMeterSource
{
    PreflightRms,
    RecordingEndpointPeak,
}

internal static class Panel1MicrophoneMeterSourcePolicy
{
    internal static Panel1MicrophoneMeterSource Resolve(
        RecordingReviewState recordingPhase) => recordingPhase switch
        {
            RecordingReviewState.Recording or
            RecordingReviewState.Paused or
            RecordingReviewState.Stopping =>
                Panel1MicrophoneMeterSource.RecordingEndpointPeak,
            _ => Panel1MicrophoneMeterSource.PreflightRms,
        };
}

internal readonly record struct Panel1AudioMeterSourceSample(
    bool SystemSourceEnabled,
    bool SystemMeterAvailable,
    uint SystemPeakAbsolutePcm16,
    bool MicrophoneSourceEnabled,
    bool MicrophoneMeterAvailable,
    Panel1MicrophoneMeterSource MicrophoneSource,
    double MicrophoneLevelPcm16);

internal readonly record struct Panel1AudioMeterPresentationSample(
    int SystemActiveSegments,
    int MicrophoneActiveSegments,
    bool SystemMeterAvailable,
    bool MicrophoneMeterAvailable);

/// <summary>
/// Presentation-only level shaping. It never changes capture, recording, PCM,
/// or device eligibility; it only turns cached source levels into UI segments.
/// </summary>
internal sealed class Panel1AudioMeterPresentation
{
    internal const int SegmentCount = 12;
    internal const double ActivityDeltaDb = 6.0;
    internal static readonly TimeSpan BaselineWarmup =
        TimeSpan.FromMilliseconds(650);

    private readonly object _gate = new();
    private readonly MicrophoneMeaningfulActivityPresentation _microphone =
        new(BaselineWarmup, ActivityDeltaDb);
    private Panel1MicrophoneMeterSource? _microphoneSource;
    private double _systemLevel;
    private TimeSpan? _lastSystemUpdate;

    internal Panel1AudioMeterPresentationSample Update(
        Panel1AudioMeterSourceSample source,
        TimeSpan timestamp)
    {
        lock (_gate)
        {
            bool systemAvailable = source.SystemSourceEnabled &&
                source.SystemMeterAvailable;
            bool microphoneAvailable = source.MicrophoneSourceEnabled &&
                source.MicrophoneMeterAvailable;

            if (systemAvailable)
            {
                double target = ToSystemPresentationLevel(
                    source.SystemPeakAbsolutePcm16);
                double elapsedSeconds = ElapsedSeconds(
                    _lastSystemUpdate,
                    timestamp);
                _systemLevel = Smooth(
                    _systemLevel,
                    target,
                    elapsedSeconds,
                    target > _systemLevel ? 0.06 : 0.22);
                _lastSystemUpdate = timestamp;
            }
            else
            {
                _systemLevel = 0.0;
                _lastSystemUpdate = null;
            }

            if (_microphoneSource != source.MicrophoneSource)
            {
                _microphone.Reset();
                _microphoneSource = source.MicrophoneSource;
            }
            double microphoneLevel = microphoneAvailable
                ? _microphone.Update(source.MicrophoneLevelPcm16, timestamp)
                : ResetMicrophoneCore();
            return new Panel1AudioMeterPresentationSample(
                ToActiveSegments(_systemLevel),
                ToActiveSegments(microphoneLevel),
                systemAvailable,
                microphoneAvailable);
        }
    }

    internal void ResetMicrophone()
    {
        lock (_gate)
        {
            _microphone.Reset();
            _microphoneSource = null;
        }
    }

    internal void ResetSystem()
    {
        lock (_gate)
        {
            _systemLevel = 0.0;
            _lastSystemUpdate = null;
        }
    }

    private double ResetMicrophoneCore()
    {
        _microphone.Reset();
        _microphoneSource = null;
        return 0.0;
    }

    private static double ToSystemPresentationLevel(uint peakAbsolutePcm16)
    {
        double linear = Math.Clamp(peakAbsolutePcm16 / 32768.0, 0.0, 1.0);
        return linear <= 0.0 ? 0.0 : Math.Pow(linear, 0.35);
    }

    private static int ToActiveSegments(double level) =>
        level <= 0.01
            ? 0
            : Math.Clamp(
                (int)Math.Ceiling(level * SegmentCount),
                1,
                SegmentCount);

    private static double ElapsedSeconds(
        TimeSpan? previous,
        TimeSpan timestamp) => previous is not { } value
            ? 0.08
            : Math.Clamp((timestamp - value).TotalSeconds, 0.001, 0.5);

    private static double Smooth(
        double current,
        double target,
        double elapsedSeconds,
        double timeConstantSeconds)
    {
        double coefficient = 1.0 - Math.Exp(
            -elapsedSeconds / timeConstantSeconds);
        return current + ((target - current) * coefficient);
    }
}

/// <summary>
/// Learns a per-binding presentation noise floor and presents only activity
/// that rises materially above it. The input may be preflight RMS or recording
/// endpoint peak; all values are UI state and never feed recording.
/// </summary>
internal sealed class MicrophoneMeaningfulActivityPresentation(
    TimeSpan baselineWarmup,
    double activityDeltaDb)
{
    private const double MinimumDbfs = -96.0;
    private readonly TimeSpan _baselineWarmup = baselineWarmup;
    private readonly double _activityDeltaDb = activityDeltaDb;
    private bool _initialized;
    private TimeSpan _warmupStarted;
    private TimeSpan _lastUpdate;
    private double _noiseFloorDb;
    private double _level;

    internal double NoiseFloorDb => _noiseFloorDb;

    internal bool WarmupComplete { get; private set; }

    internal double Update(double levelPcm16, TimeSpan timestamp)
    {
        double levelDb = ToDbfs(levelPcm16);
        if (!_initialized || timestamp < _lastUpdate)
        {
            _initialized = true;
            _warmupStarted = timestamp;
            _lastUpdate = timestamp;
            _noiseFloorDb = levelDb;
            _level = 0.0;
            WarmupComplete = false;
            return 0.0;
        }

        double elapsedSeconds = Math.Clamp(
            (timestamp - _lastUpdate).TotalSeconds,
            0.001,
            0.5);
        _lastUpdate = timestamp;
        WarmupComplete = timestamp - _warmupStarted >= _baselineWarmup;
        if (!WarmupComplete)
        {
            _noiseFloorDb = Smooth(
                _noiseFloorDb,
                levelDb,
                elapsedSeconds,
                0.22);
            _level = 0.0;
            return 0.0;
        }

        double deltaDb = levelDb - _noiseFloorDb;
        double excessDb = deltaDb - _activityDeltaDb;
        double target = excessDb <= 0.0
            ? 0.0
            : Math.Clamp(0.18 + (excessDb / 24.0), 0.0, 1.0);

        // Follow a quieter room quickly. Rise slowly during stationary noise,
        // and extremely slowly while likely speech is present so speech does
        // not immediately become the new floor.
        double floorTimeConstant = levelDb < _noiseFloorDb
            ? 0.35
            : excessDb > 0.0
                ? 18.0
                : 6.0;
        _noiseFloorDb = Smooth(
            _noiseFloorDb,
            levelDb,
            elapsedSeconds,
            floorTimeConstant);
        _level = Smooth(
            _level,
            target,
            elapsedSeconds,
            target > _level ? 0.07 : 0.24);
        return _level;
    }

    internal void Reset()
    {
        _initialized = false;
        _noiseFloorDb = MinimumDbfs;
        _level = 0.0;
        WarmupComplete = false;
    }

    private static double ToDbfs(double levelPcm16)
    {
        if (!double.IsFinite(levelPcm16) || levelPcm16 <= 0.0)
        {
            return MinimumDbfs;
        }
        return Math.Clamp(
            20.0 * Math.Log10(levelPcm16 / 32768.0),
            MinimumDbfs,
            0.0);
    }

    private static double Smooth(
        double current,
        double target,
        double elapsedSeconds,
        double timeConstantSeconds)
    {
        double coefficient = 1.0 - Math.Exp(
            -elapsedSeconds / timeConstantSeconds);
        return current + ((target - current) * coefficient);
    }
}
