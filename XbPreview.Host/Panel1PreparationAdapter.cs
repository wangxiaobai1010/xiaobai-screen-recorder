using System.Diagnostics;
using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Views;
using XbPreview.Avalonia.Views.Panels;
using XbPreview.Avalonia.Localization;

namespace XbPreview.Host;

internal readonly record struct Panel1MouseHiddenRuntimeResult(
    bool Succeeded,
    bool MouseHidden,
    string Detail);

internal enum MicrophoneRefreshReason
{
    UserInitiated,
    PassiveLifecycle,
}

internal sealed class Panel1PreparationAdapter :
    IPanel1PreparationController,
    IDisposable
{
    private const string WindowsDefaultMicrophoneKey =
        "__windows_default_microphone__";

    private readonly object _snapshotGate = new();
    private readonly IStructuralCaptureCommands _capture;
    private readonly RecordingController _recording;
    private readonly Func<bool, bool, Task<Panel1MouseHiddenRuntimeResult>>
        _setMouseHidden;
    private readonly SemaphoreSlim _audioCommandGate = new(1, 1);
    private readonly CancellationTokenSource _meterCancellation = new();
    private readonly Panel1AudioMeterPresentation _meterPresentation = new();
    private readonly Stopwatch _meterClock = Stopwatch.StartNew();
    private Panel1PreparationSnapshot _snapshot;
    private Task? _meterTask;
    private long _snapshotRevision;
    private int _captureCommandPending;
    private int _audioCommandPending;
    private int _cursorCommandPending;
    private int _recordingCommandPending;
    private int _disposed;

    internal Panel1PreparationAdapter(
        IStructuralCaptureCommands capture,
        RecordingController recording,
        Func<bool, bool, Task<Panel1MouseHiddenRuntimeResult>> setMouseHidden)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _recording = recording ??
            throw new ArgumentNullException(nameof(recording));
        _setMouseHidden = setMouseHidden ??
            throw new ArgumentNullException(nameof(setMouseHidden));
        _snapshot = ApplyControlPolicy(
            Panel1PreparationSnapshot.Initial with
            {
                CaptureTarget = capture.CurrentTarget,
                CaptureDetail = Describe(capture.CurrentTarget),
            });
    }

    public event Action<Panel1PreparationSnapshot>? SnapshotChanged;

    public Panel1PreparationSnapshot CurrentSnapshot
    {
        get
        {
            lock (_snapshotGate)
            {
                return _snapshot;
            }
        }
    }

    public StructuralCaptureTargetPresentation CurrentTarget =>
        _capture.CurrentTarget;

    internal int AvailableMicrophoneDeviceCount => CurrentSnapshot
        .MicrophoneDevices
        .Count(choice => choice.Available && !choice.IsWindowsDefault);

    internal async Task InitializeAsync()
    {
        NativeMethods.Result audioModeResult = _recording.SetAudioProgramMode(
            NativeMethods.AudioProgramMode.None);
        if (audioModeResult != NativeMethods.Result.Ok)
        {
            throw new InvalidOperationException(
                Strings.Format("AudioInitFailed", audioModeResult));
        }

        Update(snapshot => snapshot with
        {
            AudioProgramMode = Panel1AudioProgramMode.None,
            MicrophoneEnabled = false,
            SystemAudioEnabled = false,
            Detail = Strings.Format("AudioModeConfirmed", "None"),
        });

        MicrophoneSelection persisted = MicrophoneSelectionSettings.Load(
            MicrophoneSelectionSettings.SettingsPath);
        NativeMethods.Result selectionResult =
            _recording.SetMicrophoneSelection(persisted);
        string selectionDetail = string.Empty;
        if (selectionResult != NativeMethods.Result.Ok)
        {
            NativeMethods.Result fallbackResult =
                _recording.SetMicrophoneSelection(
                    MicrophoneSelection.WindowsDefault);
            if (fallbackResult != NativeMethods.Result.Ok)
            {
                throw new InvalidOperationException(
                    Strings.Format("RestoreMicrophoneFailed",
                        selectionResult, fallbackResult));
            }
            selectionDetail =
                Strings.Format("SavedMicrophoneFallback", selectionResult);
        }

        await RefreshMicrophonesCoreAsync(selectionDetail)
            .ConfigureAwait(false);
        _ = RefreshSystemAudioAvailabilityCore();
        StartMeterPresentationLoop();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _meterCancellation.Cancel();
        SnapshotChanged = null;
        try
        {
            _meterTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        _meterCancellation.Dispose();
    }

    public async Task<IReadOnlyList<StructuralCaptureWindowChoice>>
        EnumerateWindowsAsync()
    {
        if (!CurrentSnapshot.CaptureControlsEnabled ||
            Interlocked.Exchange(ref _captureCommandPending, 1) != 0)
        {
            return CurrentSnapshot.WindowChoices;
        }

        Update(snapshot => snapshot with
        {
            CaptureDetail = Strings.Get("EnumeratingWindows"),
        });
        try
        {
            IReadOnlyList<StructuralCaptureWindowChoice> choices =
                (await _capture.EnumerateWindowsAsync()
                    .ConfigureAwait(false)).ToArray();
            Update(snapshot => snapshot with
            {
                CaptureTarget = _capture.CurrentTarget,
                WindowChoices = choices,
                CaptureDetail =
                    Strings.Format("WindowCount", choices.Count,
                        Describe(_capture.CurrentTarget)),
            });
            return choices;
        }
        catch (Exception error)
        {
            Update(snapshot => snapshot with
            {
                CaptureTarget = _capture.CurrentTarget,
                CaptureDetail = Strings.Format("WindowEnumerationFailed", error.Message),
            });
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _captureCommandPending, 0);
            Update(static snapshot => snapshot);
        }
    }

    public Task<StructuralCaptureCommandResult> SetFullScreenAsync() =>
        RunCaptureCommandAsync(static capture => capture.SetFullScreenAsync());

    public Task<StructuralCaptureCommandResult> SetWindowAsync(
        StructuralCaptureWindowChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return RunCaptureCommandAsync(capture => capture.SetWindowAsync(choice));
    }

    public Task<Panel1PreparationCommandResult> RefreshMicrophonesAsync() =>
        RefreshMicrophonesAsync(MicrophoneRefreshReason.UserInitiated);

    internal async Task<Panel1PreparationCommandResult> RefreshMicrophonesAsync(
        MicrophoneRefreshReason reason)
    {
        if (!CurrentSnapshot.AudioControlsEnabled)
        {
            return Rejected(Strings.Get("RecordingActiveCannotRefreshMicrophones"));
        }
        if (!await _audioCommandGate.WaitAsync(0).ConfigureAwait(false))
        {
            return Rejected(Strings.Get("AnotherAudioCommand"));
        }

        bool changesPresentation = reason == MicrophoneRefreshReason.UserInitiated;
        if (changesPresentation)
        {
            Interlocked.Exchange(ref _audioCommandPending, 1);
            Update(snapshot => snapshot with
            {
                MicrophoneDetail = Strings.Get("RefreshingMicrophones"),
                Detail = Strings.Get("RefreshingMicrophones"),
            });
        }
        try
        {
            return await RefreshMicrophonesCoreAsync(string.Empty)
                .ConfigureAwait(false);
        }
        catch (Exception error)
        {
            string detail = Strings.Format("MicrophoneRefreshFailed", error.Message);
            Update(snapshot => snapshot with
            {
                MicrophoneDetail = detail,
                Detail = detail,
            });
            return Rejected(detail);
        }
        finally
        {
            if (changesPresentation)
            {
                Interlocked.Exchange(ref _audioCommandPending, 0);
            }
            _audioCommandGate.Release();
            if (changesPresentation)
            {
                Update(static snapshot => snapshot);
            }
        }
    }

    public async Task<Panel1PreparationCommandResult>
        RefreshSystemAudioAvailabilityAsync()
    {
        if (!CurrentSnapshot.AudioControlsEnabled)
        {
            return Rejected(Strings.Get("RecordingActiveCannotRefreshSystemAudio"));
        }
        if (!await _audioCommandGate.WaitAsync(0).ConfigureAwait(false))
        {
            return Rejected(Strings.Get("AnotherAudioCommand"));
        }

        Interlocked.Exchange(ref _audioCommandPending, 1);
        Update(snapshot => snapshot with
        {
            SystemAudioDetail = Strings.Get("RefreshingSystemAudio"),
            Detail = Strings.Get("RefreshingSystemAudio"),
        });
        try
        {
            return RefreshSystemAudioAvailabilityCore();
        }
        catch (Exception error)
        {
            string detail = Strings.Format("SystemAudioRefreshFailed", error.Message);
            Update(snapshot => snapshot with
            {
                SystemAudioDefaultRenderPresent = false,
                SystemAudioAvailable = false,
                SystemAudioDetail = detail,
                Detail = detail,
            });
            return Rejected(detail);
        }
        finally
        {
            Interlocked.Exchange(ref _audioCommandPending, 0);
            _audioCommandGate.Release();
            Update(static snapshot => snapshot);
        }
    }

    public Task<Panel1PreparationCommandResult> SetMicrophoneEnabledAsync(
        bool enabled) => RunAudioCommandAsync(snapshot =>
    {
        if (enabled &&
            (!snapshot.MicrophoneAvailable ||
             !snapshot.SelectedMicrophoneAvailable))
        {
            return Rejected(Strings.Get("SelectedMicrophoneUnavailable"));
        }

        return ApplyAudioMode(
            enabled,
            snapshot.SystemAudioEnabled,
            Strings.Get("Microphone"));
    });

    public Task<Panel1PreparationCommandResult> SetSystemAudioEnabledAsync(
        bool enabled) => RunAudioCommandAsync(snapshot =>
    {
        if (enabled && !snapshot.SystemAudioAvailable)
        {
            return Rejected(Strings.Get("NoSystemAudio"));
        }

        return ApplyAudioMode(
            snapshot.MicrophoneEnabled,
            enabled,
            Strings.Get("SystemAudio"));
    });

    public Task<Panel1PreparationCommandResult> SelectMicrophoneAsync(
        Panel1MicrophoneChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return RunAudioCommandAsync(_ => SelectMicrophoneCore(choice));
    }

    public async Task<Panel1PreparationCommandResult> SetMouseHiddenAsync(
        bool hidden)
    {
        Panel1PreparationSnapshot before = CurrentSnapshot;
        if (!before.CursorControlEnabled ||
            Interlocked.Exchange(ref _cursorCommandPending, 1) != 0)
        {
            return Rejected(Strings.Get("CursorCommandPending"));
        }

        Update(snapshot => snapshot with
        {
            MouseHiddenDetail = Strings.Get("ApplyingCursor"),
            Detail = Strings.Get("ApplyingCursor"),
        });
        try
        {
            Panel1MouseHiddenRuntimeResult result = await _setMouseHidden(
                hidden,
                before.MouseHidden).ConfigureAwait(false);
            Update(snapshot => snapshot with
            {
                MouseHidden = result.MouseHidden,
                MouseHiddenDetail = result.Detail,
                Detail = string.IsNullOrWhiteSpace(result.Detail)
                    ? Strings.Get(result.MouseHidden
                        ? "CursorHiddenOn" : "CursorHiddenOff")
                    : result.Detail,
            });
            return new Panel1PreparationCommandResult(
                result.Succeeded,
                result.Detail,
                CurrentSnapshot);
        }
        catch (Exception error)
        {
            string detail = Strings.Format("CursorCommandFailed", error.Message);
            Update(snapshot => snapshot with
            {
                MouseHidden = before.MouseHidden,
                MouseHiddenDetail = detail,
                Detail = detail,
            });
            return Rejected(detail);
        }
        finally
        {
            Interlocked.Exchange(ref _cursorCommandPending, 0);
            Update(static snapshot => snapshot);
        }
    }

    internal void ApplyRecordingSnapshot(RecordingReviewSnapshot recording)
    {
        Interlocked.Exchange(
            ref _recordingCommandPending,
            recording.CommandPending ? 1 : 0);
        Update(snapshot => snapshot with
        {
            RecordingPhase = recording.State,
        });
    }

    internal async Task PrepareRecordingStartAsync()
    {
        Interlocked.Exchange(ref _recordingCommandPending, 1);
        Update(snapshot => snapshot with
        {
            RecordingPhase = RecordingReviewState.Starting,
            Detail = Strings.Get("ConfirmingAudioMode"),
        });

        await _audioCommandGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Panel1PreparationSnapshot snapshot = CurrentSnapshot;
            if (snapshot.MicrophoneEnabled)
            {
                MicrophoneSelectionStatus selection =
                    _recording.GetMicrophoneSelection();
                if (!selection.Available)
                {
                    throw new InvalidOperationException(
                        MicrophoneAvailabilityContract.UserMessage);
                }
            }

            NativeMethods.Result result = _recording.SetAudioProgramMode(
                ToNative(snapshot.AudioProgramMode));
            if (result != NativeMethods.Result.Ok)
            {
                throw new InvalidOperationException(
                    Strings.Format("AudioConfirmationFailed", result));
            }

            Update(current => current with
            {
                Detail =
                    Strings.Format("AudioConfirmationSucceeded",
                        snapshot.AudioProgramMode),
            });
        }
        finally
        {
            _audioCommandGate.Release();
        }
    }

    internal void CancelRecordingStart(RecordingReviewSnapshot recording)
    {
        Interlocked.Exchange(
            ref _recordingCommandPending,
            recording.CommandPending ? 1 : 0);
        Update(snapshot => snapshot with
        {
            RecordingPhase = recording.State,
        });
    }

    private async Task<StructuralCaptureCommandResult> RunCaptureCommandAsync(
        Func<IStructuralCaptureCommands,
            Task<StructuralCaptureCommandResult>> command)
    {
        if (!CurrentSnapshot.CaptureControlsEnabled ||
            Interlocked.Exchange(ref _captureCommandPending, 1) != 0)
        {
            return StructuralCaptureCommandResult.Rejected(
                Strings.Get("RecordingOrCapturePending"));
        }

        Update(snapshot => snapshot with
        {
            CaptureDetail = Strings.Get("SwitchingCapture"),
        });
        try
        {
            StructuralCaptureCommandResult result = await command(_capture)
                .ConfigureAwait(false);
            Update(snapshot => snapshot with
            {
                CaptureTarget = _capture.CurrentTarget,
                CaptureDetail = result.Detail,
            });
            return result;
        }
        catch (Exception error)
        {
            string detail = Strings.Format("CaptureSwitchFailed", error.Message);
            Update(snapshot => snapshot with
            {
                CaptureTarget = _capture.CurrentTarget,
                CaptureDetail = detail,
            });
            return StructuralCaptureCommandResult.Rejected(detail);
        }
        finally
        {
            Interlocked.Exchange(ref _captureCommandPending, 0);
            Update(static snapshot => snapshot);
        }
    }

    private async Task<Panel1PreparationCommandResult> RunAudioCommandAsync(
        Func<Panel1PreparationSnapshot, Panel1PreparationCommandResult> command)
    {
        if (!CurrentSnapshot.AudioControlsEnabled)
        {
            return Rejected(Strings.Get("AudioLockedDuringRecording"));
        }
        if (!await _audioCommandGate.WaitAsync(0).ConfigureAwait(false))
        {
            return Rejected(Strings.Get("AnotherAudioCommand"));
        }

        Interlocked.Exchange(ref _audioCommandPending, 1);
        Update(snapshot => snapshot with
        {
            Detail = Strings.Get("SubmittingAudio"),
        });
        try
        {
            return command(CurrentSnapshot);
        }
        catch (Exception error)
        {
            string detail = Strings.Format("AudioCommandFailed", error.Message);
            Update(snapshot => snapshot with { Detail = detail });
            return Rejected(detail);
        }
        finally
        {
            Interlocked.Exchange(ref _audioCommandPending, 0);
            _audioCommandGate.Release();
            Update(static snapshot => snapshot);
        }
    }

    private Panel1PreparationCommandResult ApplyAudioMode(
        bool microphoneEnabled,
        bool systemAudioEnabled,
        string source)
    {
        Panel1AudioProgramMode desired =
            Panel1PreparationPolicy.ResolveAudioProgramMode(
                microphoneEnabled,
                systemAudioEnabled);
        NativeMethods.Result result = _recording.SetAudioProgramMode(
            ToNative(desired));
        if (result != NativeMethods.Result.Ok)
        {
            string failure =
                Strings.Format("AudioSourceRejected", source, result);
            Update(snapshot => snapshot with { Detail = failure });
            return Rejected(failure);
        }

        Panel1PreparationSnapshot before = CurrentSnapshot;
        bool microphoneChanged =
            before.MicrophoneEnabled != microphoneEnabled;
        bool systemChanged = before.SystemAudioEnabled != systemAudioEnabled;
        if (microphoneChanged)
        {
            _meterPresentation.ResetMicrophone();
        }
        if (systemChanged)
        {
            _meterPresentation.ResetSystem();
        }

        string detail = Strings.Format("AudioModeConfirmed", desired);
        Update(snapshot => snapshot with
        {
            MicrophoneEnabled = microphoneEnabled,
            SystemAudioEnabled = systemAudioEnabled,
            MicrophoneMeterActiveSegments = microphoneChanged
                ? 0
                : snapshot.MicrophoneMeterActiveSegments,
            MicrophoneMeterAvailable = microphoneChanged
                ? false
                : snapshot.MicrophoneMeterAvailable,
            SystemMeterActiveSegments = systemChanged
                ? 0
                : snapshot.SystemMeterActiveSegments,
            SystemMeterAvailable = systemChanged
                ? false
                : snapshot.SystemMeterAvailable,
            AudioProgramMode = desired,
            Detail = detail,
        });
        return Accepted(detail);
    }

    private Panel1PreparationCommandResult SelectMicrophoneCore(
        Panel1MicrophoneChoice choice)
    {
        if (!choice.Available)
        {
            return Rejected(Strings.Get("CannotSelectUnavailableMicrophone"));
        }

        MicrophoneSelection requested = choice.IsWindowsDefault
            ? MicrophoneSelection.WindowsDefault
            : new MicrophoneSelection(
                MicrophoneSelectionKind.ConcreteEndpoint,
                choice.Key,
                choice.DisplayName);
        NativeMethods.Result result =
            _recording.SetMicrophoneSelection(requested);
        if (result != NativeMethods.Result.Ok)
        {
            string failure = Strings.Format("MicrophoneSelectionRejected", result);
            Update(snapshot => snapshot with { Detail = failure });
            return Rejected(failure);
        }

        MicrophoneSelectionStatus readback =
            _recording.GetMicrophoneSelection();
        bool matches = readback.Kind == requested.Kind &&
            (requested.Kind == MicrophoneSelectionKind.WindowsDefault ||
             string.Equals(
                 readback.EndpointId,
                 requested.EndpointId,
                 StringComparison.Ordinal));
        _meterPresentation.ResetMicrophone();
        Update(snapshot => snapshot with
        {
            MicrophoneMeterActiveSegments = 0,
            MicrophoneMeterAvailable = false,
        });
        _ = RefreshMicrophonesCoreAsync(string.Empty)
            .GetAwaiter()
            .GetResult();
        if (!matches)
        {
            string failure =
                Strings.Get("MicrophoneReadbackMismatch");
            Update(snapshot => snapshot with { Detail = failure });
            return Rejected(failure);
        }

        string persistenceDetail = string.Empty;
        try
        {
            MicrophoneSelectionSettings.Save(
                MicrophoneSelectionSettings.SettingsPath,
                SelectionFromStatus(readback));
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException)
        {
            persistenceDetail =
                Strings.Format("RuntimeSelectedSaveFailed", error.Message);
        }

        string detail =
            Strings.Format("MicrophoneSelected", SelectedDisplayName(readback)) +
            persistenceDetail;
        Update(snapshot => snapshot with
        {
            MicrophoneDetail = detail,
            Detail = detail,
        });
        return Accepted(detail);
    }

    private Task<Panel1PreparationCommandResult> RefreshMicrophonesCoreAsync(
        string prefix)
    {
        MicrophoneDeviceCatalog catalog = _recording.GetMicrophoneDevices();
        MicrophoneSelectionStatus selected =
            _recording.GetMicrophoneSelection();
        IReadOnlyList<Panel1MicrophoneChoice> choices =
            CreateMicrophoneChoices(catalog, selected);
        bool microphoneAvailable = choices.Any(choice => choice.Available);

        Panel1PreparationSnapshot before = CurrentSnapshot;
        if (before.MicrophoneEnabled && !selected.Available)
        {
            Panel1PreparationCommandResult disabled = ApplyAudioMode(
                microphoneEnabled: false,
                before.SystemAudioEnabled,
                Strings.Get("Microphone"));
            if (!disabled.Succeeded)
            {
                throw new InvalidOperationException(disabled.Detail);
            }
        }

        string microphoneDetail = !microphoneAvailable
            ? Strings.Get("NoMicrophones")
            : !selected.Available
                ? Strings.Get("CurrentMicrophoneUnavailable")
                : Strings.Format("MicrophoneCount", catalog.Devices.Count);
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            microphoneDetail = prefix + " " + microphoneDetail;
        }

        string selectedKey = SelectionKey(selected);
        if (!string.Equals(
                before.SelectedMicrophoneKey,
                selectedKey,
                StringComparison.Ordinal))
        {
            _meterPresentation.ResetMicrophone();
        }
        Update(snapshot => snapshot with
        {
            MicrophoneAvailable = microphoneAvailable,
            SelectedMicrophoneAvailable = selected.Available,
            MicrophoneDevices = choices,
            SelectedMicrophoneKey = selectedKey,
            MicrophoneMeterActiveSegments =
                !selected.Available || !string.Equals(
                    before.SelectedMicrophoneKey,
                    selectedKey,
                    StringComparison.Ordinal)
                    ? 0
                    : snapshot.MicrophoneMeterActiveSegments,
            MicrophoneMeterAvailable =
                selected.Available && string.Equals(
                    before.SelectedMicrophoneKey,
                    selectedKey,
                    StringComparison.Ordinal) &&
                snapshot.MicrophoneMeterAvailable,
            MicrophoneDetail = microphoneDetail,
            Detail =
                Strings.Format("AudioModeConfirmed", snapshot.AudioProgramMode) +
                    $" · {microphoneDetail}",
        });
        return Task.FromResult(Accepted(microphoneDetail));
    }

    private Panel1PreparationCommandResult
        RefreshSystemAudioAvailabilityCore()
    {
        SystemAudioDefaultRenderAvailability availability =
            WindowsCoreAudioDefaultRenderProbe.Query();
        string detail = !availability.DefaultRenderPresent
            ? Strings.Get("NoDefaultSystemAudio")
            : !availability.Active
                ? Strings.Get("DefaultSystemAudioUnavailable")
                : Strings.Get("DefaultSystemAudioAvailable");
        Update(snapshot => snapshot with
        {
            SystemAudioDefaultRenderPresent =
                availability.DefaultRenderPresent,
            SystemAudioAvailable = availability.Active,
            SystemAudioDetail = detail,
            Detail = detail,
        });
        return Accepted(detail);
    }

    private static IReadOnlyList<Panel1MicrophoneChoice>
        CreateMicrophoneChoices(
            MicrophoneDeviceCatalog catalog,
            MicrophoneSelectionStatus selected)
    {
        List<Panel1MicrophoneChoice> choices =
        [
            new(
                WindowsDefaultMicrophoneKey,
                catalog.DefaultAvailable &&
                    !string.IsNullOrWhiteSpace(catalog.DefaultDisplayName)
                    ? Strings.Format("WindowsDefaultWithName", catalog.DefaultDisplayName)
                    : Strings.Get("WindowsDefaultUnavailable"),
                IsWindowsDefault: true,
                Available: catalog.DefaultAvailable),
        ];
        choices.AddRange(catalog.Devices.Select(device =>
            new Panel1MicrophoneChoice(
                device.EndpointId,
                device.DisplayName,
                IsWindowsDefault: false,
                Available: true)));

        if (selected.Kind == MicrophoneSelectionKind.ConcreteEndpoint &&
            !choices.Any(choice => string.Equals(
                choice.Key,
                selected.EndpointId,
                StringComparison.Ordinal)))
        {
            choices.Add(new Panel1MicrophoneChoice(
                selected.EndpointId,
                string.IsNullOrWhiteSpace(selected.DisplayName)
                    ? Strings.Format("DeviceUnavailable", selected.EndpointId)
                    : Strings.Format("DeviceUnavailable", selected.DisplayName),
                IsWindowsDefault: false,
                Available: false));
        }
        return choices.ToArray();
    }

    private Panel1PreparationSnapshot Update(
        Func<Panel1PreparationSnapshot, Panel1PreparationSnapshot> update)
    {
        Panel1PreparationSnapshot next;
        lock (_snapshotGate)
        {
            Panel1PreparationSnapshot current = _snapshot;
            Panel1PreparationSnapshot candidate =
                ApplyControlPolicy(update(current));
            if (HasSameSemanticState(current, candidate))
            {
                return current;
            }
            next = candidate with
            {
                PresentationRevision = ++_snapshotRevision,
            };
            _snapshot = next;
        }
        SnapshotChanged?.Invoke(next);
        return next;
    }

    private static bool HasSameSemanticState(
        Panel1PreparationSnapshot current,
        Panel1PreparationSnapshot candidate)
    {
        if (!current.WindowChoices.SequenceEqual(candidate.WindowChoices) ||
            !current.MicrophoneDevices.SequenceEqual(
                candidate.MicrophoneDevices))
        {
            return false;
        }

        return current with
        {
            PresentationRevision = candidate.PresentationRevision,
            WindowChoices = candidate.WindowChoices,
            MicrophoneDevices = candidate.MicrophoneDevices,
        } == candidate;
    }

    private void StartMeterPresentationLoop()
    {
        if (_meterTask is not null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }
        _meterTask = Task.Run(() => RunMeterPresentationLoopAsync(
            _meterCancellation.Token));
    }

    private async Task RunMeterPresentationLoopAsync(
        CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(80));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                Panel1PreparationSnapshot state = CurrentSnapshot;
                NativeMethods.AudioControlSnapshotV1 source;
                try
                {
                    source = _recording.GetAudioControlSnapshot();
                }
                catch (Exception) when (
                    cancellationToken.IsCancellationRequested ||
                    Volatile.Read(ref _disposed) != 0)
                {
                    break;
                }
                catch (Exception)
                {
                    continue;
                }

                NativeMethods.AudioEndpointLevelFlagsV1 flags =
                    source.EndpointLevelFlags;
                Panel1MicrophoneMeterSource microphoneSource =
                    Panel1MicrophoneMeterSourcePolicy.Resolve(
                        state.RecordingPhase);
                double microphoneLevelPcm16 = microphoneSource ==
                        Panel1MicrophoneMeterSource.RecordingEndpointPeak
                    ? source.MicrophonePeakAbsolutePcm16
                    : source.MicrophoneRmsPcm16;
                Panel1AudioMeterPresentationSample presentation =
                    _meterPresentation.Update(
                        new Panel1AudioMeterSourceSample(
                            state.SystemAudioEnabled && flags.HasFlag(
                                NativeMethods.AudioEndpointLevelFlagsV1
                                    .SystemSourceEnabled),
                            flags.HasFlag(
                                NativeMethods.AudioEndpointLevelFlagsV1
                                    .SystemMeterAvailable),
                            source.SystemPeakAbsolutePcm16,
                            state.MicrophoneEnabled && flags.HasFlag(
                                NativeMethods.AudioEndpointLevelFlagsV1
                                    .MicrophoneSourceEnabled),
                            flags.HasFlag(
                                NativeMethods.AudioEndpointLevelFlagsV1
                                    .MicrophoneMeterAvailable),
                            microphoneSource,
                            microphoneLevelPcm16),
                        _meterClock.Elapsed);
                Panel1PreparationSnapshot current = CurrentSnapshot;
                int systemSegments = current.SystemAudioEnabled
                    ? presentation.SystemActiveSegments
                    : 0;
                bool systemAvailable = current.SystemAudioEnabled &&
                    presentation.SystemMeterAvailable;
                int microphoneSegments = current.MicrophoneEnabled
                    ? presentation.MicrophoneActiveSegments
                    : 0;
                bool microphoneAvailable = current.MicrophoneEnabled &&
                    presentation.MicrophoneMeterAvailable;
                if (current.SystemMeterActiveSegments == systemSegments &&
                    current.SystemMeterAvailable == systemAvailable &&
                    current.MicrophoneMeterActiveSegments ==
                        microphoneSegments &&
                    current.MicrophoneMeterAvailable == microphoneAvailable)
                {
                    continue;
                }
                Update(snapshot => snapshot with
                {
                    SystemMeterActiveSegments =
                        snapshot.SystemAudioEnabled
                            ? systemSegments
                            : 0,
                    SystemMeterAvailable =
                        snapshot.SystemAudioEnabled &&
                        systemAvailable,
                    MicrophoneMeterActiveSegments =
                        snapshot.MicrophoneEnabled
                            ? microphoneSegments
                            : 0,
                    MicrophoneMeterAvailable =
                        snapshot.MicrophoneEnabled &&
                        microphoneAvailable,
                });
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
        }
    }

    private Panel1PreparationSnapshot ApplyControlPolicy(
        Panel1PreparationSnapshot snapshot)
    {
        Panel1ControlAvailability availability =
            Panel1PreparationPolicy.ResolveControlAvailability(
                snapshot.RecordingPhase,
                Volatile.Read(ref _recordingCommandPending) != 0,
                Volatile.Read(ref _captureCommandPending) != 0,
                Volatile.Read(ref _audioCommandPending) != 0,
                Volatile.Read(ref _cursorCommandPending) != 0);
        return snapshot with
        {
            CaptureControlsEnabled = availability.CaptureEnabled,
            AudioControlsEnabled = availability.AudioEnabled,
            CursorControlEnabled = availability.CursorEnabled,
            Pending =
                Volatile.Read(ref _recordingCommandPending) != 0 ||
                Volatile.Read(ref _captureCommandPending) != 0 ||
                Volatile.Read(ref _audioCommandPending) != 0 ||
                Volatile.Read(ref _cursorCommandPending) != 0,
        };
    }

    private Panel1PreparationCommandResult Accepted(string detail) =>
        new(true, detail, CurrentSnapshot);

    private Panel1PreparationCommandResult Rejected(string detail) =>
        new(false, detail, CurrentSnapshot);

    private static string SelectionKey(
        MicrophoneSelectionStatus selection) =>
        selection.Kind == MicrophoneSelectionKind.WindowsDefault
            ? WindowsDefaultMicrophoneKey
            : selection.EndpointId;

    private static MicrophoneSelection SelectionFromStatus(
        MicrophoneSelectionStatus selection) =>
        selection.Kind == MicrophoneSelectionKind.WindowsDefault
            ? MicrophoneSelection.WindowsDefault
            : new MicrophoneSelection(
                MicrophoneSelectionKind.ConcreteEndpoint,
                selection.EndpointId,
                selection.DisplayName);

    private static string SelectedDisplayName(
        MicrophoneSelectionStatus selection) =>
        selection.Kind == MicrophoneSelectionKind.WindowsDefault
            ? string.IsNullOrWhiteSpace(selection.DisplayName)
                ? Strings.Get("WindowsDefault")
                : Strings.Format("WindowsDefaultWithName", selection.DisplayName)
            : selection.DisplayName;

    private static NativeMethods.AudioProgramMode ToNative(
        Panel1AudioProgramMode mode) => mode switch
        {
            Panel1AudioProgramMode.None =>
                NativeMethods.AudioProgramMode.None,
            Panel1AudioProgramMode.SystemOnly =>
                NativeMethods.AudioProgramMode.SystemOnly,
            Panel1AudioProgramMode.MicrophoneOnly =>
                NativeMethods.AudioProgramMode.MicrophoneOnly,
            Panel1AudioProgramMode.Dual =>
                NativeMethods.AudioProgramMode.Dual,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

    private static string Describe(
        StructuralCaptureTargetPresentation target) =>
        target.IsWindow
            ? Strings.Format("CurrentWindow", target.Title)
            : Strings.Get("CurrentFullScreen");
}
