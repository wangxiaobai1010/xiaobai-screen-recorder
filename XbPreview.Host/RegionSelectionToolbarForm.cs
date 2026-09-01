namespace XbPreview.Host;

internal sealed class RegionSelectionToolbarForm : Form
{
    private const int WsExToolWindow = 0x00000080;
    private readonly CaptureDisplaySnapshot _display;
    private readonly TableLayoutPanel _toolbarHost;
    private readonly FlowLayoutPanel _exactSizePanel;
    private readonly Label _sizeLabel;
    private readonly Label _ratioLabel;
    private readonly Label _coordinateLabel;
    private readonly Label _exactErrorLabel;
    private readonly Label _ratioLockLabel;
    private readonly Button _freeButton;
    private readonly Button _ratioButton;
    private readonly Button _newSelectionButton;
    private readonly Button _exactSizeButton;
    private readonly Button _confirmButton;
    private readonly TextBox _widthTextBox;
    private readonly TextBox _heightTextBox;
    private CaptureRegion? _selectedRegion;
    private RegionSelectionState _selectionState =
        RegionSelectionState.NoSelection;
    private RegionAspectMode _aspectMode = RegionAspectMode.Free;
    private ExactSizeEditedDimension _lastEditedDimension =
        ExactSizeEditedDimension.Width;
    private bool _suppressExactTextUpdate;
    private bool _closingByTransaction;

    internal RegionSelectionToolbarForm(CaptureDisplaySnapshot display)
    {
        _display = display;
        int contentMaximumWidth = Math.Max(1, display.Width - 32);

        Text = "选择录制区域";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = ToolbarBackground;
        Padding = Padding.Empty;

        _freeButton = CreateToolButton("自由比例");
        _ratioButton = CreateToolButton("16:9");
        _exactSizeButton = CreateToolButton("精确尺寸…");
        _newSelectionButton = CreateToolButton("重新选择");
        _confirmButton = CreateToolButton("确定 Enter");
        Button cancelButton = CreateToolButton("退出 Esc");

        FlowLayoutPanel actions = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = ToolbarBackground,
            Padding = new Padding(2),
            WrapContents = true,
            MaximumSize = new Size(contentMaximumWidth, 0),
        };
        actions.Controls.AddRange(
        [
            _freeButton,
            _ratioButton,
            _exactSizeButton,
            _newSelectionButton,
            _confirmButton,
            cancelButton,
        ]);

        _sizeLabel = CreateInfoLabel("尺寸：尚未选择");
        _ratioLabel = CreateInfoLabel("比例：--");
        _coordinateLabel = CreateInfoLabel("物理坐标：--");
        FlowLayoutPanel information = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = ToolbarBackground,
            Padding = new Padding(2, 0, 2, 2),
            WrapContents = true,
            MaximumSize = new Size(contentMaximumWidth, 0),
        };
        information.Controls.AddRange(
        [
            _sizeLabel,
            _ratioLabel,
            _coordinateLabel,
        ]);

        _widthTextBox = CreateDimensionTextBox();
        _heightTextBox = CreateDimensionTextBox();
        Button applyExactButton = CreateToolButton("应用尺寸");
        Button cancelExactButton = CreateToolButton("取消编辑");
        _ratioLockLabel = CreateInfoLabel(string.Empty);
        _exactErrorLabel = CreateInfoLabel(string.Empty);
        _exactErrorLabel.ForeColor = Color.FromArgb(255, 236, 142, 142);
        _exactSizePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.FromArgb(255, 36, 36, 42),
            Padding = new Padding(4, 4, 4, 6),
            Visible = false,
            WrapContents = true,
            MaximumSize = new Size(contentMaximumWidth, 0),
        };
        _exactSizePanel.Controls.AddRange(
        [
            CreateInfoLabel("宽度"),
            _widthTextBox,
            CreateInfoLabel("px"),
            CreateInfoLabel("高度"),
            _heightTextBox,
            CreateInfoLabel("px"),
            _ratioLockLabel,
            applyExactButton,
            cancelExactButton,
            _exactErrorLabel,
        ]);

        _toolbarHost = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = ToolbarBackground,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10),
            Margin = Padding.Empty,
        };
        _toolbarHost.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _toolbarHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _toolbarHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _toolbarHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _toolbarHost.Controls.Add(actions, 0, 0);
        _toolbarHost.Controls.Add(information, 0, 1);
        _toolbarHost.Controls.Add(_exactSizePanel, 0, 2);
        Controls.Add(_toolbarHost);

        _freeButton.Click += (_, _) =>
            AspectModeRequested?.Invoke(RegionAspectMode.Free);
        _ratioButton.Click += (_, _) =>
            AspectModeRequested?.Invoke(RegionAspectMode.Ratio16By9);
        _exactSizeButton.Click += (_, _) => OpenExactSizeEditor();
        _newSelectionButton.Click += (_, _) => NewSelectionRequested?.Invoke();
        _confirmButton.Click += (_, _) => ConfirmRequested?.Invoke();
        cancelButton.Click += (_, _) => CancelRequested?.Invoke();
        applyExactButton.Click += (_, _) => RequestExactSizeApply();
        cancelExactButton.Click += (_, _) => CancelExactSizeEdit();
        _widthTextBox.TextChanged += (_, _) =>
            OnExactDimensionChanged(ExactSizeEditedDimension.Width);
        _heightTextBox.TextChanged += (_, _) =>
            OnExactDimensionChanged(ExactSizeEditedDimension.Height);
    }

    internal event Action<RegionAspectMode>? AspectModeRequested;
    internal event Action? NewSelectionRequested;
    internal event Action? ConfirmRequested;
    internal event Action? CancelRequested;
    internal event Action<
        string,
        string,
        ExactSizeEditedDimension>? ExactSizeApplyRequested;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow;
            return parameters;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Synchronize(
            _selectedRegion,
            _selectionState,
            _aspectMode);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys keyCode = keyData & Keys.KeyCode;
        if (_exactSizePanel.Visible && _exactSizePanel.ContainsFocus)
        {
            if (keyCode == Keys.Enter)
            {
                RequestExactSizeApply();
                return true;
            }
            if (keyCode == Keys.Escape)
            {
                CancelExactSizeEdit();
                return true;
            }
        }
        if (keyCode == Keys.Escape)
        {
            CancelRequested?.Invoke();
            return true;
        }
        if (keyCode == Keys.Enter)
        {
            if (_confirmButton.Enabled)
            {
                ConfirmRequested?.Invoke();
            }
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closingByTransaction)
        {
            e.Cancel = true;
            if (IsHandleCreated)
            {
                BeginInvoke(new Action(() => CancelRequested?.Invoke()));
            }
        }
        base.OnFormClosing(e);
    }

    internal void Synchronize(
        CaptureRegion? selectedRegion,
        RegionSelectionState selectionState,
        RegionAspectMode aspectMode)
    {
        _selectedRegion = selectedRegion;
        _selectionState = selectionState;
        _aspectMode = aspectMode;
        bool selected = RegionSelectionAvailability.HasSelection(
            selectedRegion,
            selectionState);

        ApplyAspectButtonStyle(
            _freeButton,
            aspectMode == RegionAspectMode.Free,
            "自由比例");
        ApplyAspectButtonStyle(
            _ratioButton,
            aspectMode == RegionAspectMode.Ratio16By9,
            "16:9");
        _confirmButton.Enabled = selected;
        _newSelectionButton.Enabled = selected;
        _exactSizeButton.Enabled = selected;

        if (selectedRegion is CaptureRegion region)
        {
            _sizeLabel.Text =
                $"尺寸：{region.Width} × {region.Height} 物理像素";
            _ratioLabel.Text =
                $"比例：{region.Width / (double)region.Height:F4}:1";
            _coordinateLabel.Text =
                $"物理坐标：[{region.Left},{region.Right}) × " +
                $"[{region.Top},{region.Bottom})";
        }
        else
        {
            _sizeLabel.Text =
                $"拖动至少 {RegionSelectionMath.DragActivationThresholdPixels} × " +
                $"{RegionSelectionMath.DragActivationThresholdPixels} 个物理像素";
            _ratioLabel.Text = "比例：--";
            _coordinateLabel.Text = "物理坐标：--";
        }
        if (!selected)
        {
            _sizeLabel.Text =
                $"请拖拽选择区域（至少 {RegionSelectionMath.DragActivationThresholdPixels} × " +
                $"{RegionSelectionMath.DragActivationThresholdPixels} 个物理像素）";
        }
        _ratioLockLabel.Text = aspectMode == RegionAspectMode.Ratio16By9
            ? "宽高已锁定为16:9"
            : "宽度和高度可独立修改";

        SyncExactEditorFromRegion();
        PositionForSelection(selectedRegion);
    }

    internal void CompleteExactSizeApply()
    {
        _exactErrorLabel.Text = string.Empty;
        _exactSizePanel.Visible = false;
        PositionForSelection(_selectedRegion);
    }

    internal void ShowExactSizeError(string? error)
    {
        _exactErrorLabel.Text = error ?? "无法应用当前尺寸。";
        PositionForSelection(_selectedRegion);
    }

    internal void CancelExactSizeEdit()
    {
        _exactErrorLabel.Text = string.Empty;
        _exactSizePanel.Visible = false;
        SyncExactEditorFromRegion();
        PositionForSelection(_selectedRegion);
    }

    internal void CloseForTransaction()
    {
        if (IsDisposed)
        {
            return;
        }
        _closingByTransaction = true;
        Close();
    }

    private void OpenExactSizeEditor()
    {
        if (_selectedRegion is null)
        {
            return;
        }
        _exactErrorLabel.Text = string.Empty;
        _exactSizePanel.Visible = true;
        SyncExactEditorFromRegion();
        PositionForSelection(_selectedRegion);
        _widthTextBox.SelectAll();
        _widthTextBox.Focus();
    }

    private void RequestExactSizeApply()
    {
        ExactSizeApplyRequested?.Invoke(
            _widthTextBox.Text,
            _heightTextBox.Text,
            _lastEditedDimension);
    }

    private void OnExactDimensionChanged(
        ExactSizeEditedDimension editedDimension)
    {
        if (_suppressExactTextUpdate)
        {
            return;
        }
        _lastEditedDimension = editedDimension;
        _exactErrorLabel.Text = string.Empty;
        if (_aspectMode != RegionAspectMode.Ratio16By9)
        {
            return;
        }

        TextBox edited = editedDimension == ExactSizeEditedDimension.Width
            ? _widthTextBox
            : _heightTextBox;
        TextBox linked = editedDimension == ExactSizeEditedDimension.Width
            ? _heightTextBox
            : _widthTextBox;
        if (RegionSelectionMath.TryCalculateLinkedDimension(
            edited.Text,
            editedDimension,
            out int value))
        {
            _suppressExactTextUpdate = true;
            linked.Text = value.ToString();
            _suppressExactTextUpdate = false;
        }
    }

    private void SyncExactEditorFromRegion()
    {
        if (!_exactSizePanel.Visible ||
            _selectedRegion is not CaptureRegion region)
        {
            return;
        }
        _suppressExactTextUpdate = true;
        _widthTextBox.Text = region.Width.ToString();
        _heightTextBox.Text = region.Height.ToString();
        _suppressExactTextUpdate = false;
    }

    private void PositionForSelection(CaptureRegion? selectedRegion)
    {
        _toolbarHost.PerformLayout();
        Size preferred = _toolbarHost.GetPreferredSize(
            new Size(Math.Max(1, _display.Width - 12), _display.Height));
        ClientSize = preferred;

        int localX = selectedRegion is CaptureRegion region
            ? Math.Clamp(
                region.Left,
                16,
                Math.Max(16, _display.Width - preferred.Width - 16))
            : 16;
        int localY = 16;
        if (selectedRegion is CaptureRegion positioned)
        {
            int above = positioned.Top - preferred.Height - 12;
            int below = positioned.Bottom + 12;
            if (above >= 16)
            {
                localY = above;
            }
            else if (below + preferred.Height <= _display.Height - 16)
            {
                localY = below;
            }
        }
        localX = Math.Clamp(
            localX,
            0,
            Math.Max(0, _display.Width - preferred.Width));
        localY = Math.Clamp(
            localY,
            0,
            Math.Max(0, _display.Height - preferred.Height));
        Location = new Point(
            checked(_display.DesktopLeft + localX),
            checked(_display.DesktopTop + localY));
    }

    private static Color ToolbarBackground =>
        Color.FromArgb(255, 27, 27, 32);

    private static Button CreateToolButton(string text)
    {
        Button button = new()
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(255, 59, 59, 68),
            ForeColor = Color.White,
            UseVisualStyleBackColor = false,
            Margin = new Padding(4),
            Padding = new Padding(8, 4, 8, 4),
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(255, 145, 145, 160);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor =
            Color.FromArgb(255, 82, 82, 96);
        button.FlatAppearance.MouseDownBackColor =
            Color.FromArgb(255, 36, 36, 44);
        return button;
    }

    private static Label CreateInfoLabel(string text) =>
        new()
        {
            AutoSize = true,
            BackColor = ToolbarBackground,
            ForeColor = Color.White,
            Padding = new Padding(8, 7, 8, 7),
            Text = text,
        };

    private static TextBox CreateDimensionTextBox() =>
        new()
        {
            Width = 84,
            BackColor = Color.White,
            ForeColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(4, 7, 4, 4),
            TextAlign = HorizontalAlignment.Right,
        };

    private static void ApplyAspectButtonStyle(
        Button button,
        bool selected,
        string text)
    {
        button.Text = selected ? $"● {text}" : text;
        button.BackColor = selected
            ? Color.FromArgb(255, 25, 104, 190)
            : Color.FromArgb(255, 59, 59, 68);
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = selected
            ? Color.FromArgb(255, 255, 214, 48)
            : Color.FromArgb(255, 145, 145, 160);
        button.FlatAppearance.BorderSize = selected ? 2 : 1;
    }
}
