using System.Xml.Linq;
using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Views.Panels;

namespace XbPreview.Managed.Tests;

internal static class TrayInFrameUiContractTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Run()
    {
        PresentationAllowsStableRuntimeStatesOnly();
        ProductHasOneToggleAndFrozenCommandRow();
    }

    private static void PresentationAllowsStableRuntimeStatesOnly()
    {
        Require(Create(RecordingReviewState.Idle).CanToggleTrayInFrame,
            "Idle allows TrayInFrame changes");
        Require(Create(RecordingReviewState.Recording).CanToggleTrayInFrame,
            "Recording allows TrayInFrame changes");
        Require(Create(RecordingReviewState.Paused).CanToggleTrayInFrame,
            "Paused allows TrayInFrame changes");
        Require(!Create(
                RecordingReviewState.Starting,
                commandPending: true).CanToggleTrayInFrame &&
            !Create(
                RecordingReviewState.Stopping,
                commandPending: true).CanToggleTrayInFrame &&
            !Create(
                RecordingReviewState.Completed,
                completionSummaryVisible: true).CanToggleTrayInFrame,
            "Starting, Stopping, and Completed summary keep the toggle locked");
    }

    private static void ProductHasOneToggleAndFrozenCommandRow()
    {
        string root = Environment.CurrentDirectory;
        string xamlPath = Path.Combine(
            root,
            "XbPreview.Avalonia",
            "Views",
            "Panels",
            "RecordingPanelView.axaml");
        XDocument document = XDocument.Load(xamlPath);
        XElement toggle = FindNamedElement(document, "TrayInFrameToggle");
        XElement activeCommands = FindNamedElement(
            document,
            "RecordingActiveCommands");
        XElement stop = FindNamedElement(document, "StopRecordingButton");

        Require(document.Descendants().Count(element =>
                element.Name.LocalName == "ToggleButton" &&
                Attribute(element, "AutomationProperties.Name") ==
                    "托盘入镜") == 1 &&
            Attribute(toggle, "AutomationProperties.Name") == "托盘入镜",
            "Panel 4 exposes exactly one TrayInFrame toggle");
        Require(document.Descendants().Count(element =>
                Attribute(element, "Text") == "托盘入镜") == 1,
            "Panel 4 exposes exactly one formal TrayInFrame label");
        Require(Attribute(activeCommands, "Grid.Row") == "4" &&
            Attribute(activeCommands, "ColumnDefinitions") == "*,4,*,4,*" &&
            Attribute(stop, "Padding") == "13,8",
            "TrayInFrame preserves the command row and Stop padding");

        string[] productFiles =
        [
            xamlPath,
            Path.Combine(
                root,
                "XbPreview.Avalonia",
                "Views",
                "Panels",
                "RecordingPanelView.axaml.cs"),
            Path.Combine(
                root,
                "XbPreview.Host",
                "RecordingFixedHomeAdapter.cs"),
        ];
        Require(productFiles.All(path =>
                !File.ReadAllText(path).Contains(
                    "允许截图",
                    StringComparison.Ordinal)),
            "User-visible product path no longer contains the old label");
    }

    private static RecordingPanelPresentationState Create(
        RecordingReviewState phase,
        bool commandPending = false,
        bool completionSummaryVisible = false) =>
        RecordingPanelPresentationState.Create(
            RecordingReviewSnapshot.Idle with { State = phase },
            commandPending,
            @"C:\recordings",
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            string.Empty,
            completionSummaryVisible,
            false,
            false);

    private static XElement FindNamedElement(
        XDocument document,
        string name) => document.Descendants().Single(element =>
            (string?)element.Attribute(XamlNamespace + "Name") == name);

    private static string Attribute(XElement element, string name) =>
        (string?)element.Attribute(name) ?? string.Empty;

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
