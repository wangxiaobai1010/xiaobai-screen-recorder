using XbPreview.Host;
using System.Reflection;
using System.Windows.Forms;

namespace XbPreview.Managed.Tests;

internal static class MinimalRecordingShellTests
{
    internal static void Run()
    {
        ActionGateIsSingleFlight();
        SnapshotFactsDriveProductStates();
        AudioTogglesSelectFrozenProgramModes();
        CameraControlsRespectDirectorOwnership();
        MainFormStartsAsAProductShell();
    }

    private static void ActionGateIsSingleFlight()
    {
        MinimalRecordingShellActionGate gate = new();
        Require(gate.TryBeginCountdown(), "first Start begins countdown");
        Require(!gate.TryBeginCountdown(), "countdown rejects duplicate Start");
        gate.EndCountdown();
        Require(gate.TryBeginCountdown(), "next idle session can count down");
        gate.EndCountdown();
        Require(gate.TryRequestStop(), "first Stop is accepted");
        Require(!gate.TryRequestStop(), "duplicate Stop is rejected");
        gate.Observe(ManagedRecordingSnapshot.Idle);
        Require(gate.TryRequestStop(), "terminal snapshot resets Stop gate");
    }

    private static void SnapshotFactsDriveProductStates()
    {
        MinimalShellControlState countdown = MinimalRecordingShellPolicy.Resolve(
            ManagedRecordingSnapshot.Idle,
            countdownActive: true,
            previewing: true,
            pendingOperation: false,
            directorEnabled: false);
        Require(
            countdown.Phase == MinimalShellPhase.Countdown &&
            !countdown.CanStart && !countdown.CanStop,
            "Countdown exposes no duplicate Start or premature Stop");

        TimeSpan realElapsed = TimeSpan.FromSeconds(19);
        ManagedRecordingSnapshot recording = ManagedRecordingSnapshot.Idle with
        {
            State = ManagedRecordingState.Recording,
            Elapsed = realElapsed,
        };
        MinimalShellControlState active = MinimalRecordingShellPolicy.Resolve(
            recording,
            countdownActive: false,
            previewing: true,
            pendingOperation: false,
            directorEnabled: false);
        Require(
            active.Phase == MinimalShellPhase.Recording &&
            active.Elapsed == realElapsed && active.CanStop &&
            !active.CanChangeAudio,
            "Recording UI uses real Snapshot duration and locks audio mode");

        ManagedRecordingSnapshot falseCompleted = ManagedRecordingSnapshot.Idle with
        {
            State = ManagedRecordingState.Completed,
            OutputSuccess = true,
            ReadyToPublish = false,
            Published = false,
        };
        Require(
            MinimalRecordingShellPolicy.Resolve(
                falseCompleted, false, true, false, false) is
                {
                    Phase: MinimalShellPhase.Failed,
                    ShowCompletion: false,
                    ShowFailure: true,
                },
            "Completed presentation requires real Publish success and otherwise fails visibly");
        ManagedRecordingSnapshot completed = falseCompleted with
        {
            ReadyToPublish = true,
            Published = true,
        };
        Require(
            MinimalRecordingShellPolicy.Resolve(
                completed, false, true, false, false).ShowCompletion,
            "published successful Snapshot exposes Completed");
        ManagedRecordingSnapshot failed = ManagedRecordingSnapshot.Idle with
        {
            State = ManagedRecordingState.Failed,
        };
        MinimalShellControlState failure = MinimalRecordingShellPolicy.Resolve(
            failed, false, true, false, false);
        Require(
            failure.ShowFailure && !failure.ShowCompletion,
            "Failed is never displayed as Completed");

        MinimalShellControlState afterStop =
            MinimalRecordingShellPolicy.Resolve(
                completed, false, true, false, false);
        Require(
            afterStop.CanStart && afterStop.CanUseManualCamera &&
            afterStop.ShowCompletion,
            "terminal Stop restores normal controls without hiding completion");
    }

    private static void AudioTogglesSelectFrozenProgramModes()
    {
        Require(
            MinimalRecordingShellPolicy.AudioMode(true, false) ==
                RecordingShellAudioMode.SystemOnly &&
            MinimalRecordingShellPolicy.NativeAudioMode(true, false) ==
                NativeMethods.AudioProgramMode.SystemOnly,
            "System ON and Mic OFF selects the frozen SystemOnly program");
        Require(
            MinimalRecordingShellPolicy.AudioMode(false, true) ==
                RecordingShellAudioMode.MicrophoneOnly &&
            MinimalRecordingShellPolicy.NativeAudioMode(false, true) ==
                NativeMethods.AudioProgramMode.MicrophoneOnly,
            "System OFF and Mic ON selects the frozen MicrophoneOnly program");
        Require(
            MinimalRecordingShellPolicy.AudioMode(true, true) ==
                RecordingShellAudioMode.Dual &&
            MinimalRecordingShellPolicy.NativeAudioMode(true, true) ==
                NativeMethods.AudioProgramMode.Dual,
            "both enabled selects the frozen Dual program");
        Require(
            MinimalRecordingShellPolicy.AudioMode(false, false) ==
                RecordingShellAudioMode.None &&
            MinimalRecordingShellPolicy.NativeAudioMode(false, false) ==
                NativeMethods.AudioProgramMode.None,
            "both disabled selects None instead of muted Dual");

        MinimalShellControlState countdown =
            MinimalRecordingShellPolicy.Resolve(
                ManagedRecordingSnapshot.Idle,
                countdownActive: true,
                previewing: true,
                pendingOperation: false,
                directorEnabled: false);
        MinimalShellControlState stopped =
            MinimalRecordingShellPolicy.Resolve(
                ManagedRecordingSnapshot.Idle,
                countdownActive: false,
                previewing: true,
                pendingOperation: false,
                directorEnabled: false);
        Require(
            !countdown.CanChangeAudio && stopped.CanChangeAudio,
            "audio toggles lock before native Start and unlock after Stop");
    }

    private static void CameraControlsRespectDirectorOwnership()
    {
        MinimalShellControlState manual = MinimalRecordingShellPolicy.Resolve(
            ManagedRecordingSnapshot.Idle, false, true, false, false);
        Require(
            manual.CanUseManualCamera && !manual.CanChangeStrength,
            "Director OFF enables manual and hides strength editing");
        MinimalShellControlState director = MinimalRecordingShellPolicy.Resolve(
            ManagedRecordingSnapshot.Idle, false, true, false, true);
        Require(
            !director.CanUseManualCamera && director.CanChangeStrength,
            "Director ON disables manual and enables pre-record strength");
        MinimalShellControlState directorRecording =
            MinimalRecordingShellPolicy.Resolve(
                ManagedRecordingSnapshot.Idle with
                {
                    State = ManagedRecordingState.Recording,
                },
                false,
                true,
                false,
                true);
        Require(
            !directorRecording.CanChangeDirector &&
            !directorRecording.CanChangeStrength &&
            !directorRecording.CanUseManualCamera,
            "Director recording preserves frozen ownership/strength locking");

        Require(
            DirectorFocusStrengthDefinition.TargetPreset(
                DirectorFocusStrength.Soft) == CameraPreset.Standard &&
            DirectorFocusStrengthDefinition.TargetPreset(
                DirectorFocusStrength.Strong) == CameraPreset.Strong,
            "Soft and Strong retain finalized camera preset mapping");
    }

    private static void MainFormStartsAsAProductShell()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                Assembly host = Assembly.LoadFrom(Path.Combine(
                    AppContext.BaseDirectory,
                    "XbPreview.Host.dll"));
                Type formType = host.GetType(
                    "XbPreview.Host.MainForm",
                    throwOnError: true)!;
                using Form form = (Form)(Activator.CreateInstance(
                    formType,
                    nonPublic: true) ?? throw new InvalidOperationException(
                        "MainForm construction failed"));
                CheckBox system = GetField<CheckBox>(
                    formType, form, "_systemAudioEnabled");
                CheckBox microphone = GetField<CheckBox>(
                    formType, form, "_microphoneEnabled");
                CheckBox director = GetField<CheckBox>(
                    formType, form, "_directorEnabled");
                RadioButton soft = GetField<RadioButton>(
                    formType, form, "_softStrengthRadio");
                RadioButton strong = GetField<RadioButton>(
                    formType, form, "_strongStrengthRadio");
                Button start = GetField<Button>(
                    formType, form, "_startRecordingButton");
                TextBox diagnostics = GetField<TextBox>(
                    formType, form, "_statusBox");
                Button region = GetField<Button>(
                    formType, form, "_selectRegionButton");
                Button hotkeyToggle = GetField<Button>(
                    formType, form, "_hotkeyToggleButton");
                Label hotkeyStatus = GetField<Label>(
                    formType, form, "_hotkeyStatusLabel");
                Label hotkeyHelp = GetField<Label>(
                    formType, form, "_hotkeyHelpLabel");
                Label cameraStatus = GetField<Label>(
                    formType, form, "_recordingModeLabel");
                Panel preview = GetField<Panel>(
                    formType, form, "_previewSurface");
                Control previewCard = GetField<Control>(
                    formType, form, "_shellPreviewCard");

                Require(
                    form.Text == "小白录屏器" &&
                    start.Text == "开始录制",
                    "MainForm exposes the product identity and primary action");
                Require(
                    system.Checked && microphone.Checked &&
                    !director.Checked && soft.Checked && !strong.Checked,
                    "first launch keeps mature audio defaults, Manual owner, and Soft strength");
                Require(
                    !form.Contains(diagnostics) && !form.Contains(region),
                    "engineering diagnostics and custom region stay outside the user path");
                Require(
                    form.Contains(hotkeyToggle) &&
                    form.Contains(hotkeyStatus) &&
                    form.Contains(hotkeyHelp) &&
                    form.Contains(cameraStatus) &&
                    hotkeyHelp.Text.Contains("F9", StringComparison.Ordinal) &&
                    hotkeyHelp.Text.Contains("F10", StringComparison.Ordinal),
                    "manual camera strip exposes shortcut toggle, real mappings, and camera state");
                Require(
                    form.FormBorderStyle == FormBorderStyle.Sizable &&
                    form.MaximizeBox && form.MaximumSize.IsEmpty,
                    "director monitor uses standard resize, maximize, and restore behavior");
                Require(
                    form.MinimumSize.Width > 0 &&
                    form.MinimumSize.Height > 0 &&
                    preview.Dock == DockStyle.Fill &&
                    previewCard.Dock == DockStyle.Fill,
                    "director monitor has a usable minimum and a fill-docked preview viewport");

                form.CreateControl();
                form.ClientSize = new Size(900, 760);
                PerformLayoutTree(form);
                Size normalPreview = preview.ClientSize;
                Require(
                    normalPreview.Width >= form.ClientSize.Width - 60 &&
                    normalPreview.Height >= 300,
                    "Preview is the dominant wide surface before recording");
                form.ClientSize = new Size(1440, 900);
                PerformLayoutTree(form);
                Size largePreview = preview.ClientSize;
                Rectangle largePreviewBounds = preview.Bounds;
                Require(
                    largePreview.Width > normalPreview.Width &&
                    largePreview.Height > normalPreview.Height,
                    "preview viewport grows in both dimensions with the client area");

                Rectangle beforeCameraStateSwitch = preview.Bounds;
                object cameraController = GetField<object>(
                    formType, form, "_cameraController");
                Type cameraControllerType = cameraController.GetType();
                cameraControllerType.GetMethod(
                    "SetPreviewRunning",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(
                        cameraController, [true, 1L]);
                Type commandType = host.GetType(
                    "XbPreview.Host.CameraCommand",
                    throwOnError: true)!;
                Type pointType = host.GetType(
                    "XbPreview.Host.CameraPoint",
                    throwOnError: true)!;
                object standardCommand = Enum.Parse(
                    commandType, "ToggleStandardCloseUp");
                object point = Activator.CreateInstance(pointType, [0.5, 0.5])!;
                MethodInfo execute = cameraControllerType.GetMethods(
                    BindingFlags.Instance | BindingFlags.NonPublic).Single(
                        method => method.Name == "Execute" &&
                        method.GetParameters() is { Length: 4 } parameters &&
                        parameters[1].ParameterType == pointType);
                execute.Invoke(
                    cameraController,
                    [standardCommand, point, 2L, null]);
                MethodInfo updateCameraStatus = formType.GetMethod(
                    "UpdateCameraModeLabel",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                updateCameraStatus.Invoke(form, null);
                Require(
                    cameraStatus.Text == "当前镜头：1.6x",
                    "manual command publishes immediate current-camera text");
                MethodInfo setDirector = cameraControllerType.GetMethod(
                    "SetDirectorLiteEnabled",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                setDirector.Invoke(cameraController, [true, 3L, null]);
                updateCameraStatus.Invoke(form, null);
                Require(
                    cameraStatus.Text.Contains("自动跟随重点", StringComparison.Ordinal) &&
                    cameraStatus.Text.Contains("1.6x", StringComparison.Ordinal),
                    "Director publishes user-facing focus state without engineering terms");
                MethodInfo updateShell = formType.GetMethod(
                    "UpdateProductShellControls",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                object recordingSnapshot = GetFieldInfo(
                    formType, "_recordingUiSnapshot").GetValue(form)!;
                updateShell.Invoke(form, [recordingSnapshot]);
                PerformLayoutTree(form);
                Require(
                    preview.Bounds == beforeCameraStateSwitch,
                    "Manual to Director keeps Preview bounds stable");
                setDirector.Invoke(cameraController, [false, 4L, null]);
                updateShell.Invoke(form, [recordingSnapshot]);
                PerformLayoutTree(form);
                Require(
                    preview.Bounds == beforeCameraStateSwitch,
                    "Director to Manual keeps Preview bounds stable");

                soft.Enabled = false;
                strong.Enabled = true;
                hotkeyToggle.Enabled = false;
                hotkeyStatus.Text = "镜头快捷键：导演模式暂时停用";
                hotkeyHelp.Text = "F9/F10 已暂停；关闭导演模式后自动恢复";
                PerformLayoutTree(form);
                Require(
                    preview.Bounds == beforeCameraStateSwitch,
                    $"Manual/Director and shortcut state switches never reflow Preview bounds " +
                    $"(before={beforeCameraStateSwitch}, after={preview.Bounds})");
                soft.Checked = false;
                strong.Checked = true;
                PerformLayoutTree(form);
                Require(
                    preview.Bounds == beforeCameraStateSwitch,
                    "Soft/Strong state switching keeps Preview bounds stable");

                MethodInfo recordingPresentation = formType.GetMethod(
                    "ApplyCompactRecordingPresentation",
                    BindingFlags.Instance | BindingFlags.NonPublic) ??
                    throw new InvalidOperationException(
                        "recording presentation method was not found");
                Size beforeRecording = form.ClientSize;
                FormWindowState stateBeforeRecording = form.WindowState;
                recordingPresentation.Invoke(form, [true]);
                PerformLayoutTree(form);
                Require(
                    form.ClientSize == beforeRecording &&
                    form.WindowState == stateBeforeRecording &&
                    preview.Parent == previewCard &&
                    preview.Bounds == largePreviewBounds,
                    "Start recording presentation does not reflow or resize the Preview");
                recordingPresentation.Invoke(form, [false]);
                PerformLayoutTree(form);
                Require(
                    preview.Bounds == largePreviewBounds,
                    "Stop recording presentation preserves Preview bounds");

                MethodInfo prepareForRecording = formType.GetMethod(
                    "PrepareProductWindowForRecording",
                    BindingFlags.Instance | BindingFlags.NonPublic) ??
                    throw new InvalidOperationException(
                        "recording window safety method was not found");
                MethodInfo restoreAfterRecording = formType.GetMethod(
                    "RestoreProductWindowAfterRecording",
                    BindingFlags.Instance | BindingFlags.NonPublic) ??
                    throw new InvalidOperationException(
                        "recording window restore method was not found");
                FieldInfo exclusionSucceeded = GetFieldInfo(
                    formType, "_windowExclusionSucceeded");
                form.WindowState = FormWindowState.Normal;
                form.Bounds = new Rectangle(120, 90, 1110, 730);
                Rectangle userBounds = form.Bounds;
                exclusionSucceeded.SetValue(form, false);
                prepareForRecording.Invoke(form, null);
                Require(
                    form.WindowState == FormWindowState.Normal &&
                    form.Bounds == userBounds,
                    "normal resized window keeps user bounds on Start even without exclusion");
                restoreAfterRecording.Invoke(form, null);
                Require(
                    form.WindowState == FormWindowState.Normal &&
                    form.Bounds == userBounds,
                    "normal resized window keeps user bounds after Stop");
                form.WindowState = FormWindowState.Maximized;
                exclusionSucceeded.SetValue(form, true);
                prepareForRecording.Invoke(form, null);
                Require(
                    form.WindowState == FormWindowState.Maximized,
                    "capture exclusion success keeps the recording monitor visible");
                exclusionSucceeded.SetValue(form, false);
                prepareForRecording.Invoke(form, null);
                Require(
                    form.WindowState == FormWindowState.Maximized,
                    "maximized window stays maximized on Start without exclusion");
                restoreAfterRecording.Invoke(form, null);
                Require(
                    form.WindowState == FormWindowState.Maximized,
                    "maximized window stays maximized after Stop");
            }
            catch (Exception error)
            {
                failure = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Require(thread.Join(TimeSpan.FromSeconds(10)),
            "product shell construction completes on its STA thread");
        if (failure is not null)
        {
            throw new InvalidOperationException(
                "product shell construction failed",
                failure);
        }
    }

    private static T GetField<T>(Type owner, object instance, string name)
        where T : class =>
        (T)(GetFieldInfo(owner, name).GetValue(instance)
            ?? throw new InvalidOperationException(
                $"MainForm field not found: {name}"));

    private static FieldInfo GetFieldInfo(Type owner, string name) =>
        owner.GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new InvalidOperationException(
            $"MainForm field not found: {name}");

    private static void PerformLayoutTree(Control control)
    {
        control.PerformLayout();
        foreach (Control child in control.Controls)
        {
            PerformLayoutTree(child);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
