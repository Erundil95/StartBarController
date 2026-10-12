using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using StartBarController.Core;
using StartBarController.Core.Windhawk;

namespace StartBarController.App.Views;

public partial class PositionPage : UserControl
{
    private readonly MainWindow _window;
    private readonly AppHost _host;
    // True until the first Update(): controls raise change events while InitializeComponent builds them.
    private bool _loading = true;
    private bool _layoutDirty;

    public PositionPage(MainWindow window)
    {
        _window = window;
        _host = window.Host;
        InitializeComponent();
        Preview.EdgeClicked += (_, edge) => _ = _window.MoveTaskbarAsync(edge);
        _host.Changed += (_, _) => Update();
        Update();
    }

    private void Update()
    {
        _loading = true;
        try
        {
            SystemState state = _host.State;
            AppSettings settings = _host.Settings;
            PositionEngine engine = state.ResolveEngine(settings.Engine);

            StatusText.Text = state.Describe();
            Preview.Edge = state.CurrentEdge;
            Preview.Centered = state.GetSetting(TaskbarSettings.Alignment) == 1;
            foreach ((Button button, TaskbarEdge edge) in EdgeButtons())
            {
                button.SetResourceReference(StyleProperty, edge == state.CurrentEdge ? "AccentButtonStyle" : "DefaultButtonStyle");
            }

            Select(EngineBox, settings.Engine.ToString());
            EngineDescription.Text = DescribeEngine(settings.Engine, engine, state);

            UpdateWindhawkCard(state, engine);
            UpdateExplorerPatcherCard(state, settings, engine);

            bool windhawkUsed = engine == PositionEngine.Windhawk;
            LayoutSection.Visibility = windhawkUsed ? Visibility.Visible : Visibility.Collapsed;
            WindhawkOptions options = settings.Windhawk;
            WidthSlider.Value = options.VerticalWidth;
            WidthText.Text = $"{options.VerticalWidth} px";
            Select(StartMenuAlignmentBox, options.StartMenuAlignment);
            Select(JumpListAlignmentBox, options.JumpListAlignment);
            IndicatorsToggle.IsChecked = options.RunningIndicatorsOnTop;
            AnimationToggle.IsChecked = options.AdjustStartMenuAnimation;
            NativeToggle.IsChecked = options.UseNativeTaskbar;

            bool modActive = state.ActiveEngine == PositionEngine.Windhawk && state.CurrentEdge != TaskbarEdge.Bottom;
            ApplyLayout.IsEnabled = modActive && _layoutDirty;
            LayoutHint.Text = !modActive
                ? "Saved. Used the next time the taskbar moves to another edge."
                : _layoutDirty ? "Apply to update the taskbar now." : "The taskbar uses these settings.";
        }
        finally
        {
            _loading = false;
        }
    }

    private void UpdateWindhawkCard(SystemState state, PositionEngine engine)
    {
        bool relevant = state.IsWindows11 && (engine == PositionEngine.Windhawk || state.WindhawkInstalled);
        WindhawkCard.Visibility = relevant ? Visibility.Visible : Visibility.Collapsed;
        InstallWindhawk.Visibility = state.WindhawkInstalled ? Visibility.Collapsed : Visibility.Visible;
        OpenWindhawk.Visibility = state.WindhawkInstalled ? Visibility.Visible : Visibility.Collapsed;

        WindhawkStatus.Text = state.WindhawkError is not null ? $"Installed, but its settings could not be read: {state.WindhawkError}"
            : !state.WindhawkInstalled ? "Not installed. Windhawk is a free, open-source customization platform; its mods move the real Windows 11 taskbar and start menu."
            : state.Windhawk!.CliPath is not null ? "Installed. Missing mods are installed automatically when needed."
            : "Installed. If a mod below is missing, install it from Windhawk's Explore page.";

        ModRows.Children.Clear();
        if (!state.WindhawkInstalled)
        {
            return;
        }

        foreach (string modId in WindhawkMods.All)
        {
            WindhawkModState mod = state.Mod(modId);
            string use = modId == WindhawkMods.TaskbarOnTop ? "used for Top" : "used for Left and Right";
            string status = !mod.Installed ? "not installed" : mod.Enabled ? "on" : "off";
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var glyph = new TextBlock
            {
                Text = mod.Installed ? "" : "",
                Style = (Style)FindResource("Icon"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 10, 0),
            };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, mod.Installed ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush");
            row.Children.Add(glyph);
            var text = new TextBlock { Text = $"{WindhawkMods.DisplayName(modId)} ({use}): {status}", TextWrapping = TextWrapping.Wrap };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            row.Children.Add(text);
            ModRows.Children.Add(row);
        }
    }

    private void UpdateExplorerPatcherCard(SystemState state, AppSettings settings, PositionEngine engine)
    {
        ExplorerPatcherCard.Visibility = engine == PositionEngine.ExplorerPatcher ? Visibility.Visible : Visibility.Collapsed;
        InstallExplorerPatcher.Visibility = state.ExplorerPatcherInstalled ? Visibility.Collapsed : Visibility.Visible;
        ExplorerPatcherStatus.Text = !state.ExplorerPatcherInstalled
            ? "Not installed. ExplorerPatcher brings back the Windows 10 taskbar, which supports every edge."
            : state.ClassicTaskbarActive ? "Installed; its Windows 10-style taskbar is active."
            : "Installed; its Windows 10-style taskbar turns on when you pick an edge.";
        Select(ClassicStartMenuBox, settings.ClassicStartMenu.ToString());
    }

    private static string DescribeEngine(PositionEngine preference, PositionEngine resolved, SystemState state)
    {
        string text = resolved switch
        {
            PositionEngine.Windhawk => "Keeps the real Windows 11 taskbar and start menu, and moves them with two Windhawk mods. Changes apply in seconds.",
            PositionEngine.ExplorerPatcher => "Replaces the taskbar with ExplorerPatcher's Windows 10-style taskbar, which supports every edge. Explorer restarts on each change.",
            _ when !state.IsWindows11 => "Uses Windows 10's own setting, which supports every edge.",
            _ => "Uses Windows' own setting. Windows 11 only honours top, left and right on preview builds with the new taskbar position feature.",
        };
        return preference == PositionEngine.Auto ? $"Automatic picks {EngineName(resolved)} here. {text}" : text;
    }

    private static string EngineName(PositionEngine engine) => engine switch
    {
        PositionEngine.Windhawk => "Windhawk",
        PositionEngine.ExplorerPatcher => "ExplorerPatcher",
        _ => "Windows' built-in setting",
    };

    private IEnumerable<(Button, TaskbarEdge)> EdgeButtons()
    {
        yield return (EdgeTop, TaskbarEdge.Top);
        yield return (EdgeLeft, TaskbarEdge.Left);
        yield return (EdgeRight, TaskbarEdge.Right);
        yield return (EdgeBottom, TaskbarEdge.Bottom);
    }

    private static void Select(Selector box, string tag)
    {
        foreach (object item in box.Items)
        {
            if (item is FrameworkElement { Tag: string t } && t.Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }

        box.SelectedIndex = 0;
    }

    private static string SelectedTag(Selector box) => (box.SelectedItem as FrameworkElement)?.Tag as string ?? string.Empty;

    private void OnEdgeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse(tag, out TaskbarEdge edge))
        {
            _ = _window.MoveTaskbarAsync(edge);
        }
    }

    private void OnEngineChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !Enum.TryParse(SelectedTag(EngineBox), out PositionEngine engine))
        {
            return;
        }

        _host.UpdateSettings(s => s with { Engine = engine });
        if (_host.State.ResolveEngine(engine) != _host.State.ActiveEngine && _host.State.CurrentEdge != TaskbarEdge.Bottom)
        {
            _window.ShowMessage("Saved. Pick an edge to move the taskbar with the new engine.", Severity.Info);
        }
    }

    private void OnClassicStartMenuChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && Enum.TryParse(SelectedTag(ClassicStartMenuBox), out StartMenuStyle style))
        {
            _host.UpdateSettings(s => s with { ClassicStartMenu = style });
        }
    }

    private void OnWidthChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (WidthText is not null)
        {
            WidthText.Text = $"{(int)e.NewValue} px";
        }

        OnLayoutChanged(sender, e);
    }

    private void OnLayoutChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _layoutDirty = true;
        _host.UpdateSettings(s => s with
        {
            Windhawk = s.Windhawk with
            {
                VerticalWidth = (int)WidthSlider.Value,
                StartMenuAlignment = SelectedTag(StartMenuAlignmentBox),
                JumpListAlignment = SelectedTag(JumpListAlignmentBox),
                RunningIndicatorsOnTop = IndicatorsToggle.IsChecked == true,
                AdjustStartMenuAnimation = AnimationToggle.IsChecked == true,
                UseNativeTaskbar = NativeToggle.IsChecked == true,
            },
        });
    }

    private async void OnApplyLayout(object sender, RoutedEventArgs e)
    {
        PositionPlan plan = _host.PlanPosition(_host.State.CurrentEdge);
        if (!plan.CanApply)
        {
            _window.ShowMessage(plan.Summary, Severity.Warning);
            return;
        }

        if (await _window.RunAsync("Updating the taskbar layout…", () => _host.ApplyAsync(plan.Changes), "Layout updated."))
        {
            _layoutDirty = false;
            Update();
        }
    }

    private void OnInstallWindhawk(object sender, RoutedEventArgs e)
    {
        AppServices.WingetInstall("RamenSoftware.Windhawk");
        _window.ShowMessage("Windhawk's installer is running in a separate window. When it finishes, come back here.", Severity.Info);
    }

    private void OnOpenWindhawk(object sender, RoutedEventArgs e)
    {
        if (_host.State.Windhawk is { } windhawk)
        {
            Process.Start(new ProcessStartInfo(windhawk.AppPath) { UseShellExecute = true })?.Dispose();
        }
    }

    private void OnWindhawkLearnMore(object sender, RoutedEventArgs e) => AppServices.OpenUrl(AppServices.WindhawkUrl);

    private void OnInstallExplorerPatcher(object sender, RoutedEventArgs e)
    {
        AppServices.WingetInstall("valinet.ExplorerPatcher");
        _window.ShowMessage("ExplorerPatcher's installer is running in a separate window. Some antivirus products flag it by mistake.", Severity.Info);
    }

    private void OnExplorerPatcherLearnMore(object sender, RoutedEventArgs e) => AppServices.OpenUrl(AppServices.ExplorerPatcherUrl);
}
