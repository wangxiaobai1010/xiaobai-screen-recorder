using System.Reflection;
using System.Runtime.CompilerServices;
using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Views.Panels;
using XbPreview.Host;

namespace XbPreview.Managed.Tests;

internal static class Stage3DPanelInteractionTests
{
    internal static void Run()
    {
        RecordingAvailabilityIsPoseSpecific();
        RecordingTransitionPresentationStaysStable();
        AdapterExecutionGateRejectsPreservedPresentationClicks();
        FlatClickLeftEntersLeft();
        ActiveLeftSecondClickReturnsOnce();
        LeftLevel3ReturnsWithLatentSelection();
        FrontLevel1IsPoseAndReturnsToFlat();
        RightLevel2ReturnsToFlat();
        DifferentDirectionRetargetsWithoutReturn();
        EnterCanBeInterruptedImmediately();
        FlatReentryUsesLatentLevel();
        ReturnDoesNotOwnPanel2Zoom();
        LevelClickWhileFlatOnlyChangesLatentLevel();
        Stage3DPanelBackgroundTests.Run();
    }

    private static void RecordingAvailabilityIsPoseSpecific()
    {
        foreach (RecordingReviewState state in new[]
        {
            RecordingReviewState.Idle,
            RecordingReviewState.Recording,
            RecordingReviewState.Paused,
            RecordingReviewState.Completed,
            RecordingReviewState.Failed,
        })
        {
            Require(
                Stage3DPanelActionController.RecordingAllowsPoseActions(
                    state,
                    commandPending: false),
                $"{state} preserves all existing Pose prerequisites");
        }

        foreach (RecordingReviewState state in new[]
        {
            RecordingReviewState.Starting,
            RecordingReviewState.Stopping,
        })
        {
            Require(
                !Stage3DPanelActionController.RecordingAllowsPoseActions(
                    state,
                    commandPending: false),
                $"{state} preserves the formal transitional Pose lock");
        }

        foreach (RecordingReviewState state in
            Enum.GetValues<RecordingReviewState>())
        {
            Require(
                !Stage3DPanelActionController.RecordingAllowsPoseActions(
                    state,
                    commandPending: true),
                $"command-pending preserves the formal Pose lock in {state}");
        }
    }

    private static void RecordingTransitionPresentationStaysStable()
    {
        (string Name, bool PreviewAvailable, RecordingReviewState State,
            bool CommandPending, bool ExpectedExecution,
            bool ExpectedVisual)[] cases =
        [
            ("Idle", true, RecordingReviewState.Idle, false, true, true),
            ("Starting", true, RecordingReviewState.Starting, true, false, true),
            ("Recording", true, RecordingReviewState.Recording, false, true, true),
            ("Pause pending", true, RecordingReviewState.Recording, true, false, true),
            ("Paused", true, RecordingReviewState.Paused, false, true, true),
            ("Resume pending", true, RecordingReviewState.Paused, true, false, true),
            ("Stopping", true, RecordingReviewState.Stopping, true, false, true),
            ("Runtime unavailable", false, RecordingReviewState.Idle, false, false, false),
        ];

        foreach (var value in cases)
        {
            bool recordingAllowsPoseActions =
                Stage3DPanelActionController.RecordingAllowsPoseActions(
                    value.State,
                    value.CommandPending);
            (bool executionAllowed, bool changesPresentation) =
                ResolveProductionAvailability(
                    value.PreviewAvailable,
                    recordingAllowsPoseActions);
            bool visualEnabled = true;
            if (changesPresentation)
            {
                visualEnabled = executionAllowed;
            }

            Require(executionAllowed == value.ExpectedExecution,
                $"{value.Name} publishes the expected execution gate");
            Require(visualEnabled == value.ExpectedVisual,
                $"{value.Name} publishes the expected stable Pose visual");
        }
    }

    private static void AdapterExecutionGateRejectsPreservedPresentationClicks()
    {
        Assembly host = LoadProductionHostAssembly();
        Type adapterType = host.GetType(
            "XbPreview.Host.Stage3DPanelActionAdapter",
            throwOnError: true)!;
        object adapter = RuntimeHelpers.GetUninitializedObject(adapterType);
        Stage3DPanelPresentationState presentation = new();
        presentation.Apply(new(
            Stage3DPanelOrientation.Right,
            Stage3DPanelLevel.Level2,
            IsActive: true,
            ActionsEnabled: true));
        Stage3DPanelPresentationSnapshot before = presentation.Snapshot;

        SetPrivateField(adapterType, adapter, "_gate", new object());
        SetPrivateField(adapterType, adapter, "_presentationState", presentation);
        SetPrivateField(adapterType, adapter, "_actionsEnabled", false);
        SetPrivateField(adapterType, adapter, "_disposed", false);
        MethodInfo request = adapterType.GetMethod(
            "OnPoseRequested",
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException(
                "Panel 3 Adapter request guard was not found.");

        InvokeRejectedPose(request, adapter, new(
            Stage3DPanelOrientation.Left,
            Stage3DPanelLevel.Level1,
            IsActive: true));
        InvokeRejectedPose(request, adapter, new(
            Stage3DPanelOrientation.Right,
            Stage3DPanelLevel.Level3,
            IsActive: true));

        Require(presentation.Snapshot == before,
            "execution-disabled clicks preserve Pose and latent Level");
    }

    private static (bool ExecutionAllowed, bool ChangesPresentation)
        ResolveProductionAvailability(
            bool previewAvailable,
            bool recordingAllowsPoseActions)
    {
        Type hostType = LoadProductionHostAssembly().GetType(
            "XbPreview.Host.StructuralAvaloniaShellHost",
            throwOnError: true)!;
        MethodInfo resolver = hostType.GetMethod(
            "ResolveStage3DPoseAvailability",
            BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new InvalidOperationException(
                "Panel 3 Host availability policy was not found.");
        object result = resolver.Invoke(
            null,
            [previewAvailable, recordingAllowsPoseActions, true]) ??
            throw new InvalidOperationException(
                "Panel 3 Host availability policy returned no result.");
        return ((bool ExecutionAllowed, bool ChangesPresentation))result;
    }

    private static Assembly LoadProductionHostAssembly() =>
        Assembly.LoadFrom(Path.Combine(
            AppContext.BaseDirectory,
            "XiaobaiRecorder.dll"));

    private static void SetPrivateField(
        Type type,
        object instance,
        string name,
        object value)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException(
                $"Panel 3 Adapter field {name} was not found.");
        field.SetValue(instance, value);
    }

    private static void InvokeRejectedPose(
        MethodInfo request,
        object adapter,
        Stage3DPanelInteractionCommand command)
    {
        try
        {
            _ = request.Invoke(
                adapter,
                [null, new Stage3DPoseRequestedEventArgs(command)]);
        }
        catch (TargetInvocationException error)
        {
            throw new InvalidOperationException(
                "Execution-disabled Pose reached the Controller.",
                error.InnerException ?? error);
        }
    }

    private static void FlatClickLeftEntersLeft()
    {
        Harness value = NewHarness(
            Stage3DPanelOrientation.Right,
            Stage3DPanelLevel.Level2,
            isActive: false);

        ExecuteDirection(value, Stage3DPanelOrientation.Left);

        Require(value.State.Snapshot is
        {
            Orientation: Stage3DPanelOrientation.Left,
            Level: Stage3DPanelLevel.Level2,
            IsActive: true,
        }, "Flat -> LEFT enters the frozen LEFT pose");
        Require(value.Native.ShowcasePoseRequests.Single().Active,
            "Flat -> LEFT issues one active pose request");
    }

    private static void ActiveLeftSecondClickReturnsOnce()
    {
        Harness value = NewHarness(
            Stage3DPanelOrientation.Left,
            Stage3DPanelLevel.Level2,
            isActive: true);

        ExecuteDirection(value, Stage3DPanelOrientation.Left);

        Require(!value.State.Snapshot.IsActive,
            "active LEFT -> LEFT reaches Flat state immediately");
        Require(value.Native.ShowcasePoseRequests is
            [{ Orientation: NativeMethods.WindowStageOrientation.Left,
                Level: NativeMethods.WindowStageLevel.Level2,
                Active: false }],
            "active LEFT -> LEFT issues exactly one Return request");
    }

    private static void LeftLevel3ReturnsWithLatentSelection()
    {
        Harness value = NewHarness(
            Stage3DPanelOrientation.Left,
            Stage3DPanelLevel.Level3,
            isActive: true);

        ExecuteDirection(value, Stage3DPanelOrientation.Left);

        Require(value.State.Snapshot is
        {
            Orientation: Stage3DPanelOrientation.Left,
            Level: Stage3DPanelLevel.Level3,
            IsActive: false,
        }, "LEFT/L3 Return retains LEFT/L3 as latent selection");
    }

    private static void FrontLevel1IsPoseAndReturnsToFlat()
    {
        Harness value = NewHarness(
            Stage3DPanelOrientation.Front,
            Stage3DPanelLevel.Level1,
            isActive: true);

        ExecuteDirection(value, Stage3DPanelOrientation.Front);

        Require(value.State.Snapshot is
        {
            Orientation: Stage3DPanelOrientation.Front,
            Level: Stage3DPanelLevel.Level1,
            IsActive: false,
        }, "FRONT/L1 is a 2.5D pose whose second click reaches Flat");
        Require(!value.Native.ShowcasePoseRequests.Single().Active,
            "FRONT is not encoded as Flat; its second click requests Return");
    }

    private static void RightLevel2ReturnsToFlat()
    {
        Harness value = NewHarness(
            Stage3DPanelOrientation.Right,
            Stage3DPanelLevel.Level2,
            isActive: true);

        ExecuteDirection(value, Stage3DPanelOrientation.Right);

        Require(!value.State.Snapshot.IsActive &&
            value.State.Snapshot.Orientation == Stage3DPanelOrientation.Right,
            "RIGHT/L2 second click returns and retains latent RIGHT");
    }

    private static void DifferentDirectionRetargetsWithoutReturn()
    {
        Harness value = NewHarness(
            Stage3DPanelOrientation.Left,
            Stage3DPanelLevel.Level3,
            isActive: true);

        ExecuteDirection(value, Stage3DPanelOrientation.Right);

        Require(value.State.Snapshot is
        {
            Orientation: Stage3DPanelOrientation.Right,
            Level: Stage3DPanelLevel.Level3,
            IsActive: true,
        }, "LEFT/L3 -> RIGHT retargets directly at L3");
        Require(value.Native.ShowcasePoseRequests.Single().Active,
            "different direction does not issue Return");
    }

    private static void EnterCanBeInterruptedImmediately()
    {
        Harness value = NewHarness(
            Stage3DPanelOrientation.Left,
            Stage3DPanelLevel.Level2,
            isActive: false);

        ExecuteDirection(value, Stage3DPanelOrientation.Left);
        ExecuteDirection(value, Stage3DPanelOrientation.Left);

        Require(value.Native.ShowcasePoseRequests.Count == 2 &&
            value.Native.ShowcasePoseRequests[0].Active &&
            !value.Native.ShowcasePoseRequests[1].Active &&
            !value.State.Snapshot.IsActive,
            "second LEFT during entrance immediately invokes Return");
    }

    private static void FlatReentryUsesLatentLevel()
    {
        Harness value = NewHarness(
            Stage3DPanelOrientation.Left,
            Stage3DPanelLevel.Level3,
            isActive: true);

        ExecuteDirection(value, Stage3DPanelOrientation.Left);
        ExecuteDirection(value, Stage3DPanelOrientation.Left);

        Require(value.State.Snapshot is
        {
            Orientation: Stage3DPanelOrientation.Left,
            Level: Stage3DPanelLevel.Level3,
            IsActive: true,
        }, "Flat re-entry restores latent LEFT/L3");
        Require(value.Native.ShowcasePoseRequests[1] is
            { Level: NativeMethods.WindowStageLevel.Level3, Active: true },
            "re-entry sends the retained L3 level");
    }

    private static void ReturnDoesNotOwnPanel2Zoom()
    {
        foreach (double zoom in new[] { 1.6, 2.0 })
        {
            Harness value = NewHarness(
                Stage3DPanelOrientation.Right,
                Stage3DPanelLevel.Level2,
                isActive: true);
            value.Native.AppliedZoom = zoom;

            ExecuteDirection(value, Stage3DPanelOrientation.Right);

            Require(value.Native.AppliedZoom == zoom &&
                value.Native.CameraStateSetCount == 0,
                $"Return preserves Panel 2 zoom {zoom:0.0} with no camera write");
        }
    }

    private static void LevelClickWhileFlatOnlyChangesLatentLevel()
    {
        Harness value = NewHarness(
            Stage3DPanelOrientation.Right,
            Stage3DPanelLevel.Level2,
            isActive: false);
        Stage3DPanelInteractionCommand command =
            Stage3DPanelInteraction.LevelClick(
                value.State.Snapshot,
                Stage3DPanelLevel.Level1);

        NativeMethods.Result result = value.Controller.Execute(
            command,
            actionsEnabled: true);

        Require(result == NativeMethods.Result.Ok &&
            value.State.Snapshot.Level == Stage3DPanelLevel.Level1 &&
            !value.State.Snapshot.IsActive &&
            value.Native.ShowcasePoseRequests.Count == 0,
            "Level while Flat changes only latent Level and never toggles on");
    }

    private static Harness NewHarness(
        Stage3DPanelOrientation orientation,
        Stage3DPanelLevel level,
        bool isActive)
    {
        Stage3DPanelPresentationState state = new();
        state.Apply(new(orientation, level, isActive, ActionsEnabled: true));
        PreviewLifecycleTests.FakeNativeSession native = new(
            [],
            blockStart: false,
            blockStop: false,
            blockRecordingStop: false);
        Stage3DPanelActionController controller = new(state, () => native);
        return new(state, native, controller);
    }

    private static void ExecuteDirection(
        Harness value,
        Stage3DPanelOrientation orientation)
    {
        Stage3DPanelInteractionCommand command =
            Stage3DPanelInteraction.DirectionClick(
                value.State.Snapshot,
                orientation);
        Require(value.Controller.Execute(command, actionsEnabled: true) ==
            NativeMethods.Result.Ok,
            $"direction {orientation} request succeeds");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record Harness(
        Stage3DPanelPresentationState State,
        PreviewLifecycleTests.FakeNativeSession Native,
        Stage3DPanelActionController Controller);
}
