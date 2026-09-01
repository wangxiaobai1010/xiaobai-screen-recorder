using Avalonia;
using Avalonia.Controls;
using XbPreview.Avalonia.Contracts;
using XbPreview.Avalonia.Controls;
using XbPreview.Avalonia.Localization;
using XbPreview.Avalonia.Views.Panels;

namespace XbPreview.Avalonia.Views;

public sealed record StructuralCaptureWindowChoice(
    nint Handle,
    string Title)
{
    public override string ToString() => Title;
}

public readonly record struct StructuralCaptureTargetPresentation(
    bool IsWindow,
    nint WindowHandle,
    string Title);

public readonly record struct StructuralCaptureCommandResult(
    bool Succeeded,
    string Detail)
{
    public static StructuralCaptureCommandResult Success(string detail) =>
        new(true, detail);

    public static StructuralCaptureCommandResult Rejected(string detail) =>
        new(false, detail);
}

public interface IStructuralCaptureCommands
{
    StructuralCaptureTargetPresentation CurrentTarget { get; }

    Task<IReadOnlyList<StructuralCaptureWindowChoice>> EnumerateWindowsAsync();

    Task<StructuralCaptureCommandResult> SetFullScreenAsync();

    Task<StructuralCaptureCommandResult> SetWindowAsync(
        StructuralCaptureWindowChoice choice);
}

public sealed class StructuralTrayInFrameRequestedEventArgs(
    bool trayInFrame) : EventArgs
{
    public bool TrayInFrame { get; } = trayInFrame;
}

public sealed class StructuralUiLanguageRequestedEventArgs(
    string language) : EventArgs
{
    public string Language { get; } = language;

    public bool Persisted { get; set; }
}

public readonly record struct StructuralShellLayoutSnapshot(
    Rect Root,
    Rect Home,
    Rect Preview,
    Rect Deck);

public sealed record StructuralRecoveryCandidatePresentation(
    string SessionId,
    string Title,
    string StatusText,
    string DisplaySafePath,
    bool ShowTryRecovery,
    bool RecoveryRunning,
    bool CanOpenFolder);

public sealed record StructuralRecoveryBannerPresentation(
    string NoticeText,
    IReadOnlyList<StructuralRecoveryCandidatePresentation> Candidates)
{
    public bool IsCompactSingle => Candidates.Count == 1;

    public string BodyText
    {
        get
        {
            if (!IsCompactSingle)
            {
                return Strings.Get("RecoveryBodyPreserved");
            }

            StructuralRecoveryCandidatePresentation candidate = Candidates[0];
            return candidate.ShowTryRecovery || candidate.RecoveryRunning
                ? Strings.Get("RecoveryBodyCanTry")
                : candidate.StatusText;
        }
    }
}

public sealed class StructuralRecoveryCandidateEventArgs(
    string sessionId) : EventArgs
{
    public string SessionId { get; } = sessionId;
}

public sealed partial class StructuralShellView : UserControl
{
    private string _activeUiLanguage = UiLanguage.English;
    private string _persistedUiLanguage = UiLanguage.English;
    private RecordingReviewState _recordingState = RecordingReviewState.Idle;
    private bool _recordingCommandPending;
    private bool _languageMenuOpen;
    private bool _restartPromptDeferred;
    private bool _languageControlsAllowed = true;

    private StructuralShellView()
    {
        InitializeComponent();
        LanguageEntryButton.Click += (_, _) => ToggleLanguageMenu();
        SimplifiedChineseLanguageButton.Click += (_, _) =>
            RequestUiLanguageSelection(UiLanguage.SimplifiedChinese);
        EnglishLanguageButton.Click += (_, _) =>
            RequestUiLanguageSelection(UiLanguage.English);
        RestartLaterButton.Click += (_, _) => DeferRestartPrompt();
        RestartNowButton.Click += (_, _) => RequestRestartNow();

        CapturePanel.RecorderOwnedPopupOpened += (_, _) =>
            RecorderOwnedPopupOpened?.Invoke(this, EventArgs.Empty);

        CaptureReturnHomeButton.Click += (_, _) =>
            CaptureReturnHomeRequested?.Invoke(this, EventArgs.Empty);
        DirectorReturnHomeButton.Click += (_, _) =>
            DirectorReturnHomeRequested?.Invoke(this, EventArgs.Empty);
        Stage3DReturnHomeButton.Click += (_, _) =>
            Stage3DReturnHomeRequested?.Invoke(this, EventArgs.Empty);
        RecordingReturnHomeButton.Click += (_, _) =>
            RecordingReturnHomeRequested?.Invoke(this, EventArgs.Empty);
        RecoveryDismissButton.Click += (_, _) =>
            RecoveryDismissRequested?.Invoke(this, EventArgs.Empty);
        RecoverySingleDismissButton.Click += (_, _) =>
            RecoveryDismissRequested?.Invoke(this, EventArgs.Empty);
        UpdateLanguagePresentation();
    }

    public StructuralShellView(IGpuPreviewFrameSource frameSource)
        : this()
    {
        ArgumentNullException.ThrowIfNull(frameSource);
        GpuPreview.FrameSource = frameSource;
    }

    public event EventHandler? RecorderOwnedPopupOpened;

    public event EventHandler? CaptureReturnHomeRequested;

    public event EventHandler? DirectorReturnHomeRequested;

    public event EventHandler? Stage3DReturnHomeRequested;

    public event EventHandler? RecordingReturnHomeRequested;

    public event EventHandler<StructuralRecoveryCandidateEventArgs>?
        RecoveryTryRequested;

    public event EventHandler<StructuralRecoveryCandidateEventArgs>?
        RecoveryOpenFolderRequested;

    public event EventHandler<StructuralRecoveryCandidateEventArgs>?
        RecoveryDismissReminderRequested;

    public event EventHandler? RecoveryDismissRequested;

    public event EventHandler<StructuralUiLanguageRequestedEventArgs>?
        UiLanguageRequested;

    public event EventHandler? RestartNowRequested;

    public GpuPreviewControl PreviewControl => GpuPreview;

    public CapturePanelView DockedCaptureView => CapturePanel;

    public DirectorPanelView DockedDirectorView => DirectorPanel;

    public DirectorPanelPresentationState DirectorPresentationState =>
        DirectorPanel.PresentationState;

    public Stage3DPanelView Stage3DView => Stage3DPanel;

    public Stage3DPanelView DockedStage3DView => Stage3DPanel;

    public Stage3DPanelPresentationState Stage3DPresentationState =>
        Stage3DPanel.PresentationState;

    public Stage3DPanelBackgroundState Stage3DBackgroundState =>
        Stage3DPanel.BackgroundState;

    public RecordingPanelView DockedRecordingView => RecordingPanel;

    public bool SettingsVisible => false;

    public string ActiveUiLanguage => _activeUiLanguage;

    public string PersistedUiLanguage => _persistedUiLanguage;

    public bool HasPendingUiLanguage => !string.Equals(
        _activeUiLanguage,
        _persistedUiLanguage,
        StringComparison.Ordinal);

    public bool LanguageEntryVisible => LanguageOverlay.IsVisible;

    public double LanguageEntryButtonWidth => LanguageEntryButton.Width;

    public bool LanguageMenuVisible => LanguageMenu.IsVisible;

    public bool RestartPromptVisible => LanguageRestartPrompt.IsVisible;

    public bool LanguageEntryUsesHomeOverlay =>
        ReferenceEquals(LanguageOverlay.Parent, HomePreviewRegion);

    public bool RecoveryBannerVisible => RecoveryBanner.IsVisible;

    public void ApplyRecoveryPresentation(
        StructuralRecoveryBannerPresentation? presentation)
    {
        RecoveryCandidateRows.Children.Clear();
        RecoverySingleCandidateActions.Children.Clear();
        if (presentation is null || presentation.Candidates.Count == 0)
        {
            RecoveryNoticeText.Text = string.Empty;
            RecoveryBodyText.Text = string.Empty;
            RecoverySingleNoticeText.Text = string.Empty;
            RecoverySingleBodyText.Text = string.Empty;
            RecoverySingleLayout.IsVisible = false;
            RecoveryMultipleLayout.IsVisible = false;
            RecoveryBanner.IsVisible = false;
            return;
        }

        if (presentation.IsCompactSingle)
        {
            RecoverySingleNoticeText.Text = presentation.NoticeText;
            RecoverySingleBodyText.Text = presentation.BodyText;
            RecoverySingleCandidateActions.Children.Add(
                CreateRecoveryCandidateRow(
                    presentation.Candidates[0],
                    includeCopy: false));
        }
        else
        {
            RecoveryNoticeText.Text = presentation.NoticeText;
            RecoveryBodyText.Text = presentation.BodyText;
            foreach (StructuralRecoveryCandidatePresentation candidate in
                presentation.Candidates)
            {
                RecoveryCandidateRows.Children.Add(
                    CreateRecoveryCandidateRow(
                        candidate,
                        includeCopy: true));
            }
        }
        RecoverySingleLayout.IsVisible = presentation.IsCompactSingle;
        RecoveryMultipleLayout.IsVisible = !presentation.IsCompactSingle;
        RecoveryBanner.IsVisible = true;
    }

    public void AttachPanel1PreparationController(
        IPanel1PreparationController controller) =>
        CapturePanel.AttachPreparationController(controller);

    public RecordingPanelPresentationState RecordingPresentationState =>
        RecordingPanel.CurrentPresentation;

    public void AttachRecordingController(
        IRecordingPanelController recordingController) =>
        RecordingPanel.AttachController(recordingController);

    public void DetachRecordingController() =>
        RecordingPanel.DetachController();

    public void ApplyMouseHiddenPresentation(
        bool mouseHidden,
        bool enabled,
        string? detail = null) =>
        CapturePanel.ApplyMouseHiddenPresentation(mouseHidden, enabled, detail);

    public bool TryMapPreviewPointToScreen(
        double normalizedX,
        double normalizedY,
        out PixelPoint screenPoint)
    {
        screenPoint = default;
        bool canMap = double.IsFinite(normalizedX) &&
            double.IsFinite(normalizedY) &&
            normalizedX >= 0.0 && normalizedX <= 1.0 &&
            normalizedY >= 0.0 && normalizedY <= 1.0 &&
            PreviewPresentationLayer.Bounds.Width > 0.0 &&
            PreviewPresentationLayer.Bounds.Height > 0.0;
        if (!canMap)
        {
            return false;
        }

        screenPoint = PreviewPresentationLayer.PointToScreen(new Point(
            normalizedX * PreviewPresentationLayer.Bounds.Width,
            normalizedY * PreviewPresentationLayer.Bounds.Height));
        return true;
    }

    // Compatibility shims for the retired structural performance gate. The
    // formal product has no independent Settings surface.
    public void ShowSettings()
    {
    }

    public void ShowHome()
    {
    }

    public void ConfigureUiLanguage(
        string activeLanguage,
        string persistedLanguage)
    {
        _activeUiLanguage = UiLanguage.NormalizePersisted(activeLanguage) ??
            UiLanguage.Resolve(
                null,
                global::System.Globalization.CultureInfo.CurrentUICulture);
        _persistedUiLanguage = UiLanguage.NormalizePersisted(
            persistedLanguage) ?? _activeUiLanguage;
        _languageMenuOpen = false;
        _restartPromptDeferred = false;
        UpdateLanguagePresentation();
    }

    public bool RequestUiLanguageSelection(string language)
    {
        if (!_languageControlsAllowed ||
            UiLanguage.NormalizePersisted(language) is not { } normalized)
        {
            return false;
        }

        _languageMenuOpen = false;
        if (string.Equals(
            normalized,
            _persistedUiLanguage,
            StringComparison.Ordinal))
        {
            // An explicit click on the already-selected pending language is
            // meaningful after Later: re-evaluate persisted vs active truth
            // and surface the restart choice again.
            _restartPromptDeferred = false;
            UpdateLanguagePresentation();
            return true;
        }

        StructuralUiLanguageRequestedEventArgs args = new(normalized);
        UiLanguageRequested?.Invoke(this, args);
        if (!args.Persisted)
        {
            UpdateLanguagePresentation();
            return false;
        }

        _persistedUiLanguage = normalized;
        _restartPromptDeferred = false;
        UpdateLanguagePresentation();
        return true;
    }

    public void ApplyLanguageRecordingState(
        RecordingReviewState recordingState,
        bool commandPending)
    {
        bool wasAllowed = _languageControlsAllowed;
        _recordingState = recordingState;
        _recordingCommandPending = commandPending;
        _languageControlsAllowed =
            UiLanguagePresentationPolicy.ControlsAllowed(
                recordingState,
                commandPending);
        if (!_languageControlsAllowed)
        {
            _languageMenuOpen = false;
        }
        else if (!wasAllowed && HasPendingUiLanguage)
        {
            // A pending choice survives recording and is surfaced again when
            // the formal recording presentation returns to a ready state.
            _restartPromptDeferred = false;
        }
        UpdateLanguagePresentation();
    }

    public bool DeferRestartPrompt()
    {
        if (!RestartPromptVisible || !_languageControlsAllowed)
        {
            return false;
        }

        _restartPromptDeferred = true;
        UpdateLanguagePresentation();
        return true;
    }

    public bool RequestRestartNow()
    {
        if (!RestartPromptVisible || !_languageControlsAllowed)
        {
            return false;
        }

        RestartNowRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool TryGetDirectorHomeScreenBounds(out PixelRect screenBounds)
    {
        screenBounds = default;
        if (TopLevel.GetTopLevel(DirectorHomeSlot) is null ||
            DirectorHomeSlot.Bounds.Width <= 0.0 ||
            DirectorHomeSlot.Bounds.Height <= 0.0)
        {
            return false;
        }

        PixelPoint topLeft = DirectorHomeSlot.PointToScreen(default);
        PixelPoint bottomRight = DirectorHomeSlot.PointToScreen(new Point(
            DirectorHomeSlot.Bounds.Width,
            DirectorHomeSlot.Bounds.Height));
        int width = bottomRight.X - topLeft.X;
        int height = bottomRight.Y - topLeft.Y;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        screenBounds = new PixelRect(topLeft.X, topLeft.Y, width, height);
        return true;
    }

    public bool TryGetCaptureHomeScreenBounds(out PixelRect screenBounds)
    {
        screenBounds = default;
        if (TopLevel.GetTopLevel(CaptureHomeSlot) is null ||
            CaptureHomeSlot.Bounds.Width <= 0.0 ||
            CaptureHomeSlot.Bounds.Height <= 0.0)
        {
            return false;
        }

        PixelPoint topLeft = CaptureHomeSlot.PointToScreen(default);
        PixelPoint bottomRight = CaptureHomeSlot.PointToScreen(new Point(
            CaptureHomeSlot.Bounds.Width,
            CaptureHomeSlot.Bounds.Height));
        int width = bottomRight.X - topLeft.X;
        int height = bottomRight.Y - topLeft.Y;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        screenBounds = new PixelRect(topLeft.X, topLeft.Y, width, height);
        return true;
    }

    public bool TryGetStage3DHomeScreenBounds(out PixelRect screenBounds)
    {
        screenBounds = default;
        if (TopLevel.GetTopLevel(Stage3DHomeSlot) is null ||
            Stage3DHomeSlot.Bounds.Width <= 0.0 ||
            Stage3DHomeSlot.Bounds.Height <= 0.0)
        {
            return false;
        }

        PixelPoint topLeft = Stage3DHomeSlot.PointToScreen(default);
        PixelPoint bottomRight = Stage3DHomeSlot.PointToScreen(new Point(
            Stage3DHomeSlot.Bounds.Width,
            Stage3DHomeSlot.Bounds.Height));
        int width = bottomRight.X - topLeft.X;
        int height = bottomRight.Y - topLeft.Y;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        screenBounds = new PixelRect(topLeft.X, topLeft.Y, width, height);
        return true;
    }

    public bool TryGetRecordingHomeScreenBounds(out PixelRect screenBounds)
    {
        screenBounds = default;
        if (TopLevel.GetTopLevel(RecordingHomeSlot) is null ||
            RecordingHomeSlot.Bounds.Width <= 0.0 ||
            RecordingHomeSlot.Bounds.Height <= 0.0)
        {
            return false;
        }

        PixelPoint topLeft = RecordingHomeSlot.PointToScreen(default);
        PixelPoint bottomRight = RecordingHomeSlot.PointToScreen(new Point(
            RecordingHomeSlot.Bounds.Width,
            RecordingHomeSlot.Bounds.Height));
        int width = bottomRight.X - topLeft.X;
        int height = bottomRight.Y - topLeft.Y;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        screenBounds = new PixelRect(topLeft.X, topLeft.Y, width, height);
        return true;
    }

    public void SetCaptureFloatingPresentation(bool floating)
    {
        CapturePanel.IsVisible = !floating;
        CaptureHomePlaceholder.IsVisible = floating;
        if (!floating)
        {
            SetCaptureHomeHighlighted(false);
        }
    }

    public void SetCaptureHomeHighlighted(bool highlighted)
    {
        CaptureHomePlaceholder.Opacity = highlighted ? 0.96 : 0.72;
        CaptureHomeOutline.Opacity = highlighted ? 1.0 : 0.76;
    }

    public void SetDirectorFloatingPresentation(bool floating)
    {
        DirectorPanel.IsVisible = !floating;
        DirectorHomePlaceholder.IsVisible = floating;
        if (!floating)
        {
            SetDirectorHomeHighlighted(false);
        }
    }

    public void SetDirectorHomeHighlighted(bool highlighted)
    {
        DirectorHomePlaceholder.Opacity = highlighted ? 0.96 : 0.72;
        DirectorHomeOutline.Opacity = highlighted ? 1.0 : 0.76;
    }

    public void SetStage3DFloatingPresentation(bool floating)
    {
        Stage3DPanel.IsVisible = !floating;
        Stage3DHomePlaceholder.IsVisible = floating;
        if (!floating)
        {
            SetStage3DHomeHighlighted(false);
        }
    }

    public void SetStage3DHomeHighlighted(bool highlighted)
    {
        Stage3DHomePlaceholder.Opacity = highlighted ? 0.96 : 0.72;
        Stage3DHomeOutline.Opacity = highlighted ? 1.0 : 0.76;
    }

    public void SetRecordingFloatingPresentation(bool floating)
    {
        RecordingPanel.IsVisible = !floating;
        RecordingHomePlaceholder.IsVisible = floating;
        if (!floating)
        {
            SetRecordingHomeHighlighted(false);
        }
    }

    public void SetRecordingHomeHighlighted(bool highlighted)
    {
        RecordingHomePlaceholder.Opacity = highlighted ? 0.96 : 0.72;
        RecordingHomeOutline.Opacity = highlighted ? 1.0 : 0.76;
    }

    public StructuralShellLayoutSnapshot CaptureLayoutSnapshot() => new(
        new Rect(Bounds.Size),
        CaptureRectInRoot(HomeSurface),
        CaptureRectInRoot(PreviewControl),
        CaptureRectInRoot(DeckRegion));

    public async Task ShutdownPreviewAsync()
    {
        RecordingPanel.DetachController();
        await PreviewControl.ShutdownAsync();
    }

    private Control CreateRecoveryCandidateRow(
        StructuralRecoveryCandidatePresentation candidate,
        bool includeCopy)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions(
                includeCopy ? "*,Auto,Auto,Auto" : "Auto,Auto,Auto"),
            ColumnSpacing = 8,
        };
        if (includeCopy)
        {
            StackPanel copy = new()
            {
                Spacing = 1,
                VerticalAlignment =
                    global::Avalonia.Layout.VerticalAlignment.Center,
            };
            copy.Children.Add(new TextBlock
            {
                Text = candidate.Title,
                Classes = { "skill-label" },
            });
            copy.Children.Add(new TextBlock
            {
                Text = candidate.StatusText,
                Classes = { "skill-muted" },
                TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
            });
            row.Children.Add(copy);
        }

        if (candidate.ShowTryRecovery || candidate.RecoveryRunning)
        {
            Button recover = new()
            {
                Content = candidate.RecoveryRunning
                    ? Strings.Get("RecoveryChecking")
                    : Strings.Get("RecoveryTry"),
                IsEnabled = !candidate.RecoveryRunning,
                Classes = { "skill-primary" },
                Tag = candidate.SessionId,
            };
            recover.Click += (_, _) => RecoveryTryRequested?.Invoke(
                this,
                new StructuralRecoveryCandidateEventArgs(candidate.SessionId));
            Grid.SetColumn(recover, includeCopy ? 1 : 0);
            row.Children.Add(recover);
        }

        if (candidate.CanOpenFolder)
        {
            Button openFolder = new()
            {
                Content = Strings.Get("OpenContainingFolder"),
                Classes = { "director-small" },
                Tag = candidate.SessionId,
            };
            ToolTip.SetTip(openFolder, candidate.DisplaySafePath);
            openFolder.Click += (_, _) => RecoveryOpenFolderRequested?.Invoke(
                this,
                new StructuralRecoveryCandidateEventArgs(candidate.SessionId));
            Grid.SetColumn(openFolder, includeCopy ? 2 : 1);
            row.Children.Add(openFolder);
        }

        Button dismissReminder = new()
        {
            Content = Strings.Get("DontRemindAgain"),
            Classes = { "director-small" },
            Tag = candidate.SessionId,
        };
        dismissReminder.Click += (_, _) =>
            RecoveryDismissReminderRequested?.Invoke(
                this,
                new StructuralRecoveryCandidateEventArgs(candidate.SessionId));
        Grid.SetColumn(dismissReminder, includeCopy ? 3 : 2);
        row.Children.Add(dismissReminder);
        return row;
    }

    private void ToggleLanguageMenu()
    {
        if (!_languageControlsAllowed)
        {
            return;
        }

        _languageMenuOpen = !_languageMenuOpen;
        UpdateLanguagePresentation();
    }

    private void UpdateLanguagePresentation()
    {
        UiLanguagePresentation presentation =
            UiLanguagePresentationPolicy.Resolve(
                _activeUiLanguage,
                _persistedUiLanguage,
                _recordingState,
                _recordingCommandPending,
                _restartPromptDeferred);
        LanguageOverlay.IsVisible = presentation.EntryVisible;
        LanguageMenu.IsVisible =
            presentation.EntryVisible && _languageMenuOpen;
        LanguageRestartPrompt.IsVisible = presentation.PromptVisible;
    }

    private Rect CaptureRectInRoot(Control control)
    {
        Matrix? transform = control.TransformToVisual(this);
        return transform is { } matrix
            ? new Rect(control.Bounds.Size).TransformToAABB(matrix)
            : default;
    }
}
