using Microsoft.Win32;

namespace XbPreview.Host;

internal sealed class RegionSelectionController
{
    private readonly DisplayGeometryProvider _displayProvider;
    private bool _active;

    internal RegionSelectionController(DisplayGeometryProvider displayProvider)
    {
        _displayProvider = displayProvider;
    }

    internal bool IsActive => _active;
    internal WindowDisplayAffinityResult LastWdaResult { get; private set; }

    internal RegionSelectionResult SelectRegion(
        Form mainForm,
        CaptureRegion? existingRegion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mainForm);
        if (_active)
        {
            return RegionSelectionResult.Cancel(
                RegionSelectionCancelReason.Error,
                "A region-selection transaction is already active.");
        }

        _active = true;
        RegionSelectionOverlayForm? overlay = null;
        RegionSelectionToolbarForm? toolbar = null;
        bool displayChanged = false;
        EventHandler displayChangedHandler = (_, _) =>
        {
            displayChanged = true;
            overlay?.CancelForDisplayChange();
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            CaptureDisplaySnapshot openedDisplay =
                _displayProvider.ReadPrimaryDisplay();
            CaptureRegion? safeExisting =
                existingRegion is CaptureRegion candidate &&
                candidate.IsWithin(openedDisplay.Width, openedDisplay.Height)
                    ? candidate
                    : null;
            overlay = new RegionSelectionOverlayForm(
                openedDisplay,
                _displayProvider,
                safeExisting);
            toolbar = new RegionSelectionToolbarForm(openedDisplay);
            using CancellationTokenRegistration cancellation =
                cancellationToken.Register(
                    () =>
                    {
                        RegionSelectionOverlayForm? active = overlay;
                        if (active is null ||
                            active.IsDisposed ||
                            !active.IsHandleCreated)
                        {
                            return;
                        }
                        try
                        {
                            active.BeginInvoke(active.CancelSelection);
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    });

            void SynchronizeToolbar()
            {
                if (toolbar.IsDisposed)
                {
                    return;
                }
                toolbar.Synchronize(
                    overlay.SelectedRegion,
                    overlay.SelectionState,
                    overlay.AspectMode);
            }

            overlay.VisualStateChanged += (_, _) => SynchronizeToolbar();
            overlay.InteractionStarted += (_, _) =>
                toolbar.CancelExactSizeEdit();
            overlay.Shown += (_, _) =>
            {
                SynchronizeToolbar();
                if (!toolbar.Visible)
                {
                    toolbar.Show(overlay);
                }
                SynchronizeToolbar();
            };
            overlay.FormClosing += (_, _) => toolbar.CloseForTransaction();

            toolbar.AspectModeRequested += overlay.SetAspectMode;
            toolbar.NewSelectionRequested += () =>
            {
                toolbar.CancelExactSizeEdit();
                overlay.BeginNewSelection();
            };
            toolbar.ConfirmRequested += overlay.ConfirmSelection;
            toolbar.CancelRequested += overlay.CancelSelection;
            toolbar.ExactSizeApplyRequested += (
                widthText,
                heightText,
                lastEditedDimension) =>
            {
                if (overlay.TryApplyExactSize(
                    widthText,
                    heightText,
                    lastEditedDimension,
                    out string? error))
                {
                    toolbar.CompleteExactSizeApply();
                }
                else
                {
                    toolbar.ShowExactSizeError(error);
                }
            };
            SystemEvents.DisplaySettingsChanged += displayChangedHandler;

            mainForm.Hide();
            DialogResult dialogResult = overlay.ShowDialog();
            LastWdaResult = overlay.WdaResult;
            if (displayChanged || overlay.DisplayChanged)
            {
                return RegionSelectionResult.Cancel(
                    RegionSelectionCancelReason.DisplayChanged,
                    "Display configuration changed; select the region again.");
            }
            if (dialogResult != DialogResult.OK ||
                overlay.SelectedRegion is not CaptureRegion selected)
            {
                return RegionSelectionResult.Cancel();
            }

            CaptureDisplaySnapshot confirmedDisplay =
                _displayProvider.ReadPrimaryDisplay();
            if (!openedDisplay.Matches(confirmedDisplay))
            {
                return RegionSelectionResult.Cancel(
                    RegionSelectionCancelReason.DisplayChanged,
                    "Primary display identity, bounds, dimensions, or DPI changed.");
            }
            return RegionSelectionResult.Confirm(
                confirmedDisplay,
                selected);
        }
        catch (Exception error)
        {
            return RegionSelectionResult.Cancel(
                RegionSelectionCancelReason.Error,
                error.Message);
        }
        finally
        {
            SystemEvents.DisplaySettingsChanged -= displayChangedHandler;
            toolbar?.CloseForTransaction();
            toolbar?.Dispose();
            overlay?.Dispose();
            _active = false;
            if (!mainForm.IsDisposed && !mainForm.Disposing)
            {
                mainForm.Show();
                mainForm.Activate();
                mainForm.BringToFront();
            }
        }
    }
}
