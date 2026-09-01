using System.Xml.Linq;

namespace XbPreview.Managed.Tests;

internal static class Panel4CancelUiContractTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Run()
    {
        string sourcePath = Path.Combine(
            Environment.CurrentDirectory,
            "XbPreview.Avalonia",
            "Views",
            "Panels",
            "RecordingPanelView.axaml");
        Require(
            File.Exists(sourcePath),
            $"Panel 4 source XAML was not found in the current workspace: " +
                sourcePath);

        XDocument document = XDocument.Load(
            sourcePath,
            LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        XElement root = document.Root ?? throw new InvalidOperationException(
            "Panel 4 source XAML has no root element.");

        XElement commands = FindNamedElement(
            document,
            "RecordingActiveCommands");
        XElement pause = FindNamedElement(
            document,
            "PauseResumeRecordingButton");
        XElement restart = FindNamedElement(
            document,
            "RestartRecordingButton");
        XElement stop = FindNamedElement(
            document,
            "StopRecordingButton");

        Require(
            Attribute(commands, "ColumnDefinitions") == "*,4,*,4,*",
            "Panel 4 active command columns must remain exactly " +
                "'*,4,*,4,*'.");
        Require(
            ReferenceEquals(pause.Parent, commands) &&
            ReferenceEquals(restart.Parent, commands) &&
            ReferenceEquals(stop.Parent, commands),
            "Pause/Restart/Stop must be direct children of one command row.");

        XElement[] commandButtons = commands.Elements()
            .Where(element => element.Name.LocalName == "Button")
            .ToArray();
        Require(
            commandButtons.Length == 3 &&
            commands.Elements().Count() == 3 &&
            ReferenceEquals(commandButtons[0], pause) &&
            ReferenceEquals(commandButtons[1], restart) &&
            ReferenceEquals(commandButtons[2], stop),
            "The single active command row must be ordered Pause, Restart, Stop.");
        Require(
            commands.Attribute("RowDefinitions") is null &&
            pause.Attribute("Grid.Row") is null &&
            restart.Attribute("Grid.Row") is null &&
            stop.Attribute("Grid.Row") is null &&
            GridColumn(pause) == 0 &&
            GridColumn(restart) == 2 &&
            GridColumn(stop) == 4,
            "Pause/Restart/Stop must share one implicit row and occupy columns " +
                "0/2/4 respectively.");
        Require(
            Attribute(commands, "Grid.Row") == "4",
            "The active commands must remain in the existing active row 4.");

        Require(
            HasClass(restart, "skill-secondary") &&
            !HasClass(restart, "skill-primary") &&
            Attribute(restart, "Height") == string.Empty &&
            Attribute(restart, "MinHeight") == "38" &&
            Attribute(restart, "Padding") == string.Empty &&
            Attribute(restart, "FontSize") == string.Empty &&
            Attribute(restart, "HorizontalAlignment") == "Stretch",
            "Restart must match the neighboring button dimensions while " +
                "remaining a secondary action.");
        Require(
            Attribute(pause, "MinHeight") == "38" &&
            Attribute(restart, "MinHeight") == "38" &&
            Attribute(stop, "MinHeight") == "38" &&
            Attribute(pause, "HorizontalAlignment") == "Stretch" &&
            Attribute(restart, "HorizontalAlignment") == "Stretch" &&
            Attribute(stop, "HorizontalAlignment") == "Stretch",
            "Pause/Restart/Stop must use the same height and fill equal-width " +
                "columns.");
        Require(
            HasClass(stop, "skill-primary") &&
            GridColumn(stop) == 4 &&
            Attribute(stop, "HorizontalAlignment") == "Stretch",
            "Stop must remain the original right-side primary action.");

        XElement confirmation = FindNamedElement(
            document,
            "RestartRecordingConfirmation");
        XElement activePresentation = FindNamedElement(
            document,
            "RecordingActivePresentation");
        XElement? confirmationParent = confirmation.Parent;
        Require(
            confirmationParent is not null &&
            ReferenceEquals(confirmationParent, activePresentation.Parent) &&
            confirmationParent.Name.LocalName == "Grid" &&
            confirmationParent.Attribute("RowDefinitions") is null &&
            confirmation.Attribute("Grid.Row") is null &&
            confirmation.Attribute("Grid.RowSpan") is null,
            "The confirmation must overlay the existing Panel 4 content cell, " +
                "not create another row.");
        Require(
            Attribute(confirmation, "IsVisible") == "False" &&
            Attribute(confirmation, "ZIndex") == "10" &&
            Attribute(confirmation, "Background") ==
                "{DynamicResource SkillRecorder.Brush.PanelSurface}",
            "The confirmation must remain an initially hidden opaque overlay.");

        // Recording and Paused use this same state-independent Restart button,
        // so both formal labels are protected by the same exact Content value.
        Require(
            Attribute(restart, "Content") == "重录",
            "Recording Restart label must be exactly '重录'.");
        Require(
            Attribute(restart, "Content") == "重录" &&
            Attribute(restart, "AutomationProperties.Name") == "重录",
            "Paused Restart label and accessible name must be exactly '重录'.");
        Require(
            ContainsExactAttributeValue(confirmation, "Text", "重新录制？"),
            "Confirmation title must be exactly '重新录制？'.");
        Require(
            ContainsExactAttributeValue(
                confirmation,
                "Text",
                "当前这段录制将不会保存，并返回录制准备。"),
            "Confirmation body copy changed.");

        XElement keep = FindNamedElement(
            document,
            "ContinueCurrentRecordingButton");
        XElement discard = FindNamedElement(
            document,
            "DiscardCurrentRecordingButton");
        Require(
            Attribute(keep, "Content") == "继续录制" &&
            Attribute(keep, "AutomationProperties.Name") == "继续录制",
            "Keep button copy must be exactly '继续录制'.");
        Require(
            Attribute(discard, "Content") == "放弃这段录制" &&
            Attribute(discard, "AutomationProperties.Name") ==
                "放弃这段录制",
            "Discard button copy must be exactly '放弃这段录制'.");

        XElement layout = root.Elements().Single(element =>
            element.Name.LocalName == "Grid");
        XElement panelLayout = layout.Elements().Single(element =>
            element.Name.LocalName == "Grid" &&
            Attribute(element, "RowDefinitions") == "Auto,*,Auto");
        Require(
            root.Attribute("Height") is null &&
            root.Attribute("MinHeight") is null &&
            root.Attribute("MaxHeight") is null &&
            layout.Attribute("Height") is null &&
            layout.Attribute("MinHeight") is null &&
            layout.Attribute("MaxHeight") is null &&
            panelLayout.Attribute("Height") is null &&
            panelLayout.Attribute("MinHeight") is null &&
            panelLayout.Attribute("MaxHeight") is null &&
            Attribute(panelLayout, "RowDefinitions") == "Auto,*,Auto" &&
            Attribute(activePresentation, "RowDefinitions") ==
                "Auto,Auto,Auto,Auto,*",
            "The confirmation must not add a top-level row or impose a new " +
                "Panel 4 height.");

        Console.WriteLine("PANEL4-CANCEL-UI-CONTRACT = PASS");
    }

    private static XElement FindNamedElement(
        XDocument document,
        string name)
    {
        XElement[] matches = document.Descendants()
            .Where(element =>
                (string?)element.Attribute(XamlNamespace + "Name") == name)
            .ToArray();
        Require(
            matches.Length == 1,
            $"Expected exactly one x:Name='{name}', found {matches.Length}.");
        return matches[0];
    }

    private static bool ContainsExactAttributeValue(
        XElement root,
        string attributeName,
        string expected) => root.DescendantsAndSelf().Any(element =>
            Attribute(element, attributeName) == expected);

    private static int GridColumn(XElement element)
    {
        string value = Attribute(element, "Grid.Column");
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }
        Require(
            int.TryParse(value, out int column),
            $"Grid.Column '{value}' is not an integer.");
        return column;
    }

    private static bool HasClass(XElement element, string expected) =>
        Attribute(element, "Classes")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(expected, StringComparer.Ordinal);

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
