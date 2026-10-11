using System.Diagnostics;
using StartBarController.Core;

namespace StartBarController.App;

internal sealed class MainForm : Form
{
    private const string ExplorerPatcherPage = "https://github.com/valinet/ExplorerPatcher/releases/latest";
    private const string WindhawkTaskbarMods = "https://ramensoftware.com/windhawk-mods-for-the-windows-11-taskbar";
    private const int ContentWidth = 460;

    private readonly TaskbarController _controller;
    private readonly Dictionary<TaskbarEdge, RadioButton> _edgeButtons = new();
    private readonly TaskbarPreview _preview = new();
    private readonly Label _systemLabel = NewWrappingLabel();
    private readonly Label _planLabel = NewWrappingLabel();
    private readonly ComboBox _startMenu = NewDropDown("Keep current", "Windows 10 (follows the taskbar)", "Windows 11");
    private readonly CheckBox _forceNative = new()
    {
        Text = "Experimental: force the native registry method (may break the taskbar)",
        AutoSize = true,
    };
    private readonly ComboBox _alignment = NewDropDown("Left", "Center");
    private readonly CheckBox _autoHide = new() { Text = "Automatically hide the taskbar", AutoSize = true };
    private readonly Button _apply = new() { Text = "Apply position", AutoSize = true };
    private readonly Button _restore = new() { Text = "Restore original", AutoSize = true };
    private readonly Button _getExplorerPatcher = new() { Text = "Get ExplorerPatcher…", AutoSize = true };
    private readonly Button _refresh = new() { Text = "Refresh", AutoSize = true };
    private readonly ToolTip _toolTip = new();
    private Control _alignmentRow = null!;
    private Control _windhawkLink = null!;

    private SystemSnapshot? _snapshot;
    private PositionPlan? _plan;
    private bool _loading;

    public MainForm(TaskbarController controller)
    {
        _controller = controller;

        Text = "Start Bar Controller";
        Font = SystemFonts.MessageBoxFont ?? Font;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        root.Controls.Add(_systemLabel);
        root.Controls.Add(BuildPositionGroup());
        root.Controls.Add(_planLabel);
        root.Controls.Add(BuildButtonRow());
        root.Controls.Add(BuildMoreGroup());
        root.Controls.Add(BuildFooter());
        Controls.Add(root);

        _toolTip.SetToolTip(_restore, "Put back the taskbar settings saved before this app first changed anything.");
        _toolTip.SetToolTip(_getExplorerPatcher,
            "ExplorerPatcher restores the Windows 10 taskbar, which can sit on any screen edge.");

        _startMenu.SelectedIndexChanged += (_, _) => UpdatePlan();
        _forceNative.CheckedChanged += (_, _) => UpdatePlan();
        _apply.Click += (_, _) => ApplyPosition();
        _restore.Click += (_, _) => RestoreOriginal();
        _getExplorerPatcher.Click += (_, _) => GetExplorerPatcher();
        _refresh.Click += (_, _) => RefreshState();
        _alignment.SelectedIndexChanged += (_, _) => ChangeAlignment();
        _autoHide.CheckedChanged += (_, _) => ChangeAutoHide();

        RefreshState();
    }

    private Control BuildPositionGroup()
    {
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, RowCount = 3, Anchor = AnchorStyles.None };
        for (int i = 0; i < 3; i++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        grid.Controls.Add(EdgeButton(TaskbarEdge.Top), 1, 0);
        grid.Controls.Add(EdgeButton(TaskbarEdge.Left), 0, 1);
        grid.Controls.Add(_preview, 1, 1);
        grid.Controls.Add(EdgeButton(TaskbarEdge.Right), 2, 1);
        grid.Controls.Add(EdgeButton(TaskbarEdge.Bottom), 1, 2);

        var startMenuRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        startMenuRow.Controls.Add(new Label { Text = "Start menu style:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) });
        startMenuRow.Controls.Add(_startMenu);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(grid);
        layout.Controls.Add(startMenuRow);
        layout.Controls.Add(_forceNative);

        return Group("Taskbar position", layout);
    }

    private RadioButton EdgeButton(TaskbarEdge edge)
    {
        var button = new RadioButton
        {
            Text = edge.ToString(),
            Appearance = Appearance.Button,
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(96, 32),
            Anchor = AnchorStyles.None,
        };
        button.CheckedChanged += (_, _) =>
        {
            if (button.Checked)
            {
                _preview.Edge = edge;
                UpdatePlan();
            }
        };
        _edgeButtons[edge] = button;
        return button;
    }

    private Control BuildButtonRow()
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 6, 0, 6) };
        row.Controls.AddRange(new Control[] { _apply, _restore, _getExplorerPatcher, _refresh });
        return row;
    }

    private Control BuildMoreGroup()
    {
        var alignRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        alignRow.Controls.Add(new Label { Text = "Icon alignment:", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        alignRow.Controls.Add(_alignment);
        _alignmentRow = alignRow;

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(alignRow);
        layout.Controls.Add(_autoHide);
        layout.Controls.Add(new Label
        {
            Text = "These apply immediately.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
        });
        return Group("More options", layout);
    }

    private Control BuildFooter()
    {
        var link = new LinkLabel
        {
            Text = "Want to keep the Windows 11 look instead? See the Windhawk taskbar mods.",
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(3, 8, 3, 0),
        };
        link.LinkClicked += (_, _) => OpenUrl(WindhawkTaskbarMods);
        _windhawkLink = link;
        return link;
    }

    private void RefreshState()
    {
        _loading = true;
        try
        {
            _snapshot = _controller.Inspect();
            _systemLabel.Text = _snapshot.Describe();
            // Windows 10 moves its taskbar natively and has no centered icons; hide the Windows 11-only parts.
            bool windows11 = WindowsRelease.IsWindows11(_snapshot.WindowsBuild);
            _getExplorerPatcher.Visible = windows11 && !_snapshot.ExplorerPatcherInstalled;
            _windhawkLink.Visible = windows11;
            _alignmentRow.Visible = windows11;
            _restore.Enabled = _controller.HasBackup;

            TaskbarEdge current = _snapshot.CurrentEdge ?? TaskbarEdge.Bottom;
            _edgeButtons[current].Checked = true;
            _preview.Edge = current;

            _startMenu.SelectedIndex = 0;
            _alignment.SelectedIndex = _snapshot.CurrentAlignment == TaskbarAlignment.Left ? 0 : 1;
            _autoHide.Checked = _controller.Shell.GetAutoHide();
        }
        finally
        {
            _loading = false;
        }

        UpdatePlan();
    }

    private PositionRequest CurrentRequest()
    {
        TaskbarEdge edge = _edgeButtons.First(pair => pair.Value.Checked).Key;
        var style = (StartMenuStyle)_startMenu.SelectedIndex; // combo order matches the enum
        return new PositionRequest(edge, style, _forceNative.Checked);
    }

    private void UpdatePlan()
    {
        if (_loading || _snapshot is null)
        {
            return;
        }

        _plan = PositionPlanner.Plan(_snapshot, CurrentRequest());
        _planLabel.Text = string.Join(Environment.NewLine + Environment.NewLine,
            new[] { _plan.Summary }.Concat(_plan.Warnings));
        _planLabel.ForeColor = _plan.CanApply ? SystemColors.ControlText : Color.Firebrick;
        _apply.Enabled = _plan.CanApply;
        _startMenu.Enabled = _plan.Method == PositionMethod.ExplorerPatcher;
        _forceNative.Visible = WindowsRelease.IsWindows11(_snapshot.WindowsBuild) && !_snapshot.ClassicTaskbarActive;
    }

    private void ApplyPosition()
    {
        if (_plan is not { CanApply: true } plan)
        {
            return;
        }

        if (!Confirm("Explorer will restart to apply the new position. Open File Explorer windows will close.\n\nContinue?"))
        {
            return;
        }

        Run(() => _controller.Apply(plan));
    }

    private void RestoreOriginal()
    {
        if (!Confirm("Restore the taskbar settings saved before your first change? Explorer will restart."))
        {
            return;
        }

        Run(() => _controller.Restore());
    }

    private void GetExplorerPatcher()
    {
        DialogResult answer = MessageBox.Show(this,
            "ExplorerPatcher is a free, open-source tool by a third party (github.com/valinet/ExplorerPatcher). " +
            "It restores the Windows 10 taskbar, which can be docked to any edge, with a start menu that follows it.\n\n" +
            "Some antivirus products flag it as a false positive. Its installer asks for administrator rights and restarts Explorer.\n\n" +
            "Yes: install it now with winget.\nNo: open the download page.",
            "Get ExplorerPatcher", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);

        if (answer == DialogResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/k winget install -e --id valinet.ExplorerPatcher")
                {
                    UseShellExecute = true,
                })?.Dispose();
                MessageBox.Show(this, "When the installer has finished, click Refresh.", Text);
            }
            catch (Exception e)
            {
                ShowError($"Could not start winget ({e.Message}). Opening the download page instead.");
                OpenUrl(ExplorerPatcherPage);
            }
        }
        else if (answer == DialogResult.No)
        {
            OpenUrl(ExplorerPatcherPage);
        }
    }

    private void ChangeAlignment()
    {
        if (_loading)
        {
            return;
        }

        var alignment = _alignment.SelectedIndex == 0 ? TaskbarAlignment.Left : TaskbarAlignment.Center;
        Run(() => _controller.SetAlignment(alignment));
    }

    private void ChangeAutoHide()
    {
        if (_loading)
        {
            return;
        }

        bool enabled = _autoHide.Checked;
        Run(() => _controller.Shell.SetAutoHide(enabled));
    }

    private void Run(Action action)
    {
        UseWaitCursor = true;
        try
        {
            action();
        }
        catch (Exception e)
        {
            ShowError(e.Message);
        }
        finally
        {
            UseWaitCursor = false;
            RefreshState();
        }
    }

    private bool Confirm(string message) =>
        MessageBox.Show(this, message, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;

    private void ShowError(string message) =>
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e)
        {
            ShowError($"Could not open {url}: {e.Message}");
        }
    }

    private static GroupBox Group(string title, Control content)
    {
        var group = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10),
            MinimumSize = new Size(ContentWidth, 0),
            Dock = DockStyle.Fill,
        };
        group.Controls.Add(content);
        return group;
    }

    private static Label NewWrappingLabel() => new()
    {
        AutoSize = true,
        MaximumSize = new Size(ContentWidth, 0),
        Margin = new Padding(3, 6, 3, 6),
    };

    private static ComboBox NewDropDown(params string[] items)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        combo.Items.AddRange(items);
        return combo;
    }
}

/// <summary>A small monitor drawing that shows where the taskbar will go.</summary>
internal sealed class TaskbarPreview : Control
{
    private TaskbarEdge _edge = TaskbarEdge.Bottom;

    public TaskbarPreview()
    {
        Size = new Size(160, 100);
        Margin = new Padding(8);
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public TaskbarEdge Edge
    {
        get => _edge;
        set
        {
            _edge = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);

        var screen = new Rectangle(2, 2, Width - 5, Height - 5);
        using (var desktop = new SolidBrush(Color.FromArgb(70, 110, 170)))
        {
            g.FillRectangle(desktop, screen);
        }

        const int thickness = 12;
        Rectangle bar = _edge switch
        {
            TaskbarEdge.Top => new Rectangle(screen.Left, screen.Top, screen.Width, thickness),
            TaskbarEdge.Left => new Rectangle(screen.Left, screen.Top, thickness, screen.Height),
            TaskbarEdge.Right => new Rectangle(screen.Right - thickness, screen.Top, thickness, screen.Height),
            _ => new Rectangle(screen.Left, screen.Bottom - thickness, screen.Width, thickness),
        };
        using (var barBrush = new SolidBrush(Color.FromArgb(32, 32, 32)))
        {
            g.FillRectangle(barBrush, bar);
        }

        // Start button and the start menu opening from it.
        var start = new Rectangle(bar.Left + 3, bar.Top + 3, thickness - 6, thickness - 6);
        Rectangle menu = _edge switch
        {
            TaskbarEdge.Top => new Rectangle(bar.Left + 2, bar.Bottom + 2, 48, 40),
            TaskbarEdge.Left => new Rectangle(bar.Right + 2, bar.Top + 2, 48, 40),
            TaskbarEdge.Right => new Rectangle(bar.Left - 50, bar.Top + 2, 48, 40),
            _ => new Rectangle(bar.Left + 2, bar.Top - 42, 48, 40),
        };
        using (var accent = new SolidBrush(Color.FromArgb(0, 120, 215)))
        using (var menuBrush = new SolidBrush(Color.FromArgb(200, 45, 45, 45)))
        {
            g.FillRectangle(menuBrush, menu);
            g.FillRectangle(accent, start);
        }

        g.DrawRectangle(SystemPens.ControlDark, screen);
    }
}
