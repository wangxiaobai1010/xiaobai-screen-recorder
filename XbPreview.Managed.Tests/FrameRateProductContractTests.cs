using System.Xml.Linq;
using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Views.Panels;

namespace XbPreview.Managed.Tests;

internal static class FrameRateProductContractTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Run()
    {
        string root = Environment.CurrentDirectory;
        string xamlPath = Path.Combine(
            root,
            "XbPreview.Avalonia",
            "Views",
            "Panels",
            "RecordingPanelView.axaml");
        XDocument document = XDocument.Load(xamlPath);
        XElement idle = FindNamed(document, "RecordingIdlePresentation");
        XElement path = FindNamed(document, "ChooseRecordingFolderButton");
        XElement resolution = FindNamed(document, "ResolutionControl");
        XElement frameRate = FindNamed(document, "FrameRateControl");
        XElement fps30 = FindNamed(document, "FrameRate30Button");
        XElement fps60 = FindNamed(document, "FrameRate60Button");
        XElement start = FindNamed(document, "StartRecordingButton");
        XElement commands = FindNamed(document, "RecordingActiveCommands");
        XElement stop = FindNamed(document, "StopRecordingButton");

        Require(
            Attribute(idle, "RowDefinitions") == "22,28,28,28,42" &&
            Attribute(idle, "RowSpacing") == "2" &&
            Attribute(path.Parent!, "Grid.Row") == "1" &&
            Attribute(resolution, "Grid.Row") == "2" &&
            Attribute(frameRate, "Grid.Row") == "3" &&
            Attribute(start, "Grid.Row") == "4",
            "Frame rate remains after resolution and before Start");
        Require(
            Attribute(fps30, "AutomationProperties.Name") == "30 FPS" &&
            Attribute(fps60, "AutomationProperties.Name") == "60 FPS" &&
            fps30.Name.LocalName == "Button" &&
            fps60.Name.LocalName == "Button" &&
            Classes(fps30).SequenceEqual(["director-small"]) &&
            Classes(fps60).SequenceEqual(["director-small"]),
            "Panel 4 uses recorder buttons for the 30/60 control");
        Require(
            !document.Descendants().Any(element =>
                element.Name.LocalName == "ToggleButton" &&
                Classes(element).Contains("frame-rate")) &&
            !document.Descendants().Any(element =>
                Attribute(element, "Selector").Contains(
                    "ToggleButton.frame-rate",
                    StringComparison.Ordinal)),
            "Frame-rate controls cannot inherit the Fluent checked accent");

        string viewCode = File.ReadAllText(Path.Combine(
            root,
            "XbPreview.Avalonia",
            "Views",
            "Panels",
            "RecordingPanelView.axaml.cs"));
        Require(
            viewCode.Contains(
                "FrameRate30Button.Classes.Set(",
                StringComparison.Ordinal) &&
            viewCode.Contains(
                "FrameRate60Button.Classes.Set(",
                StringComparison.Ordinal) &&
            viewCode.Contains("\"selected\"", StringComparison.Ordinal),
            "Frame-rate selection reuses director-small.selected");

        XDocument styles = XDocument.Load(Path.Combine(
            root,
            "XbPreview.Avalonia",
            "Styles",
            "SkillRecorderStyles.axaml"));
        XElement unselectedStyle = FindStyle(
            styles,
            "Button.director-small");
        XElement selectedStyle = FindStyle(
            styles,
            "Button.director-small.selected");
        XElement disabledStyle = FindStyle(
            styles,
            "Button.director-small:disabled");
        XElement focusStyle = FindStyle(
            styles,
            "Button.director-small:focus-visible");
        Require(
            SetterValue(unselectedStyle, "Background") ==
                "{StaticResource SkillRecorder.Brush.ControlSurface}" &&
            SetterValue(unselectedStyle, "Foreground") ==
                "{StaticResource SkillRecorder.Brush.TextPrimary}" &&
            SetterValue(unselectedStyle, "BorderBrush") ==
                "{StaticResource SkillRecorder.Brush.Line}" &&
            SetterValue(unselectedStyle, "Template").Length == 0 &&
            unselectedStyle.Descendants().Any(element =>
                element.Name.LocalName == "ControlTemplate"),
            "Unselected frame rate uses the recorder button template and tokens");
        Require(
            SetterValue(selectedStyle, "Background") ==
                "{StaticResource SkillRecorder.Brush.SignalDim}" &&
            SetterValue(selectedStyle, "Foreground") ==
                "{StaticResource SkillRecorder.Brush.BrandInk}" &&
            SetterValue(selectedStyle, "BorderBrush") ==
                "{StaticResource SkillRecorder.Brush.BrandInk}",
            "Selected frame rate uses the recorder selected tokens");
        Require(
            SetterValue(disabledStyle, "Background") ==
                "{StaticResource SkillRecorder.Brush.ControlSurface}" &&
            SetterValue(disabledStyle, "Foreground") ==
                "{StaticResource SkillRecorder.Brush.TextFaint}" &&
            SetterValue(disabledStyle, "BorderBrush") ==
                "{StaticResource SkillRecorder.Brush.Line}" &&
            SetterValue(focusStyle, "BorderBrush") ==
                "{StaticResource SkillRecorder.Brush.BrandInk}",
            "Disabled and keyboard-focused frame rates use recorder tokens");
        Require(
            Attribute(commands, "Grid.Row") == "4" &&
            Attribute(commands, "ColumnDefinitions") == "*,4,*,4,*" &&
            Attribute(stop, "Padding") == "13,8",
            "Frame rate preserves the active command row and Stop padding");
        Require(
            document.Root!.Attribute("Height") is null &&
            document.Root.Attribute("MinHeight") is null &&
            document.Root.Attribute("MaxHeight") is null,
            "Frame rate adds no Panel 4 height constraint");

        Require(Create(RecordingReviewState.Idle).CanChangeFrameRate,
            "Idle permits frame-rate selection");
        foreach (RecordingReviewState state in new[]
        {
            RecordingReviewState.Starting,
            RecordingReviewState.Recording,
            RecordingReviewState.Paused,
            RecordingReviewState.Stopping,
        })
        {
            Require(!Create(state).CanChangeFrameRate,
                $"{state} locks frame-rate selection");
        }

        string engine = File.ReadAllText(Path.Combine(
            root, "XbPreview.Native", "PreviewEngine.cpp"));
        Require(
            engine.Contains(
                "IsSupportedVideoEncoderFrameRate(framesPerSecond)",
                StringComparison.Ordinal) &&
            engine.Contains("XbRecordingState_Paused", StringComparison.Ordinal) &&
            engine.Contains("productRecordingFrameRate_ = framesPerSecond",
                StringComparison.Ordinal),
            "Native setter validates values and rejects active session phases");
    }

    private static RecordingPanelPresentationState Create(
        RecordingReviewState state) => RecordingPanelPresentationState.Create(
            RecordingReviewSnapshot.Idle with { State = state },
            false,
            @"C:\recordings",
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            string.Empty,
            false,
            false,
            false);

    private static XElement FindNamed(XDocument document, string name) =>
        document.Descendants().Single(element =>
            (string?)element.Attribute(XamlNamespace + "Name") == name);

    private static XElement FindStyle(
        XDocument document,
        string selector) => document.Descendants().Single(element =>
            element.Name.LocalName == "Style" &&
            Attribute(element, "Selector") == selector);

    private static string SetterValue(
        XElement style,
        string property) => style.Elements().Where(element =>
            element.Name.LocalName == "Setter" &&
            Attribute(element, "Property") == property)
        .Select(element => Attribute(element, "Value"))
        .SingleOrDefault() ?? string.Empty;

    private static string Attribute(XElement element, string name) =>
        (string?)element.Attribute(name) ?? string.Empty;

    private static string[] Classes(XElement element) =>
        Attribute(element, "Classes").Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"Frame-rate product contract failed: {message}");
        }
    }
}
