using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using StartBarController.App.Views;
using StartBarController.Core;

namespace StartBarController.App;

internal enum Severity
{
    Info,
    Success,
    Warning,
    Error,
}

public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly App _app;
    private readonly Dictionary<string, FrameworkElement> _pages = new();
    private TaskCompletionSource<bool>? _dialog;
    private bool _busy;

    internal MainWindow(AppHost host, App app)
    {
        _host = host;
        _app = app;
        InitializeComponent();
        NavPosition.IsChecked = true;
        Activated += (_, _) =>
        {
            if (!_busy && _dialog is null)
            {
                _host.Refresh();
            }
        };
    }

    internal AppHost Host => _host;

    internal App AppInstance => _app;

    internal IReadOnlyList<string> PageNames { get; } = new[] { "position", "taskbar", "settings" };

    internal void Navigate(string page)
    {
        RadioButton nav = page switch
        {
            "taskbar" => NavTaskbar,
            "settings" => NavSettings,
            _ => NavPosition,
        };
        nav.IsChecked = true;
    }

    internal void ShowMessage(string text, Severity severity)
    {
        (string glyph, string icon, string background) = severity switch
        {
            Severity.Success => ("", "SystemFillColorSuccessBrush", "SystemFillColorSuccessBackgroundBrush"),
            Severity.Warning => ("", "SystemFillColorCautionBrush", "SystemFillColorCautionBackgroundBrush"),
            Severity.Error => ("", "SystemFillColorCriticalBrush", "SystemFillColorCriticalBackgroundBrush"),
            _ => ("", "SystemFillColorAttentionBrush", "SystemFillColorAttentionBackgroundBrush"),
        };
        InfoIcon.Text = glyph;
        InfoIcon.SetResourceReference(TextBlock.ForegroundProperty, icon);
        InfoBar.SetResourceReference(Border.BackgroundProperty, background);
        InfoText.Text = text;
        InfoBar.Visibility = Visibility.Visible;
    }

    internal Task<bool> ConfirmAsync(string title, string text, string primary)
    {
        _dialog?.TrySetResult(false);
        _dialog = new TaskCompletionSource<bool>();
        DialogTitle.Text = title;
        DialogText.Text = text;
        DialogPrimary.Content = primary;
        DialogOverlay.Visibility = Visibility.Visible;
        DialogPrimary.Focus();
        return _dialog.Task;
    }

    /// <summary>Runs an action with a busy overlay, turning failures into a message instead of a crash.</summary>
    internal async Task<bool> RunAsync(string? busyText, Func<Task> work, string? success = null, Severity successSeverity = Severity.Success)
    {
        if (_busy)
        {
            return false;
        }

        _busy = true;
        if (busyText is not null)
        {
            BusyText.Text = busyText;
            BusyOverlay.Visibility = Visibility.Visible;
        }

        try
        {
            await work();
            if (success is not null)
            {
                ShowMessage(success, successSeverity);
            }

            return true;
        }
        catch (OperationCanceledException e)
        {
            ShowMessage(e.Message, Severity.Warning);
        }
        catch (Exception e)
        {
            ShowMessage(e.Message, Severity.Error);
        }
        finally
        {
            BusyOverlay.Visibility = Visibility.Collapsed;
            _busy = false;
        }

        return false;
    }

    /// <summary>Moves the taskbar with the configured engine, asking first if Explorer must restart.</summary>
    internal async Task MoveTaskbarAsync(TaskbarEdge edge, bool fromTray = false)
    {
        PositionPlan plan = _host.PlanPosition(edge);
        if (!plan.CanApply || (fromTray && plan.Changes.RestartExplorer))
        {
            // Setup or confirmation is needed; do it in the window.
            _app.ShowMainWindow();
            Navigate("position");
        }

        if (!plan.CanApply)
        {
            ShowMessage(plan.Summary, Severity.Warning);
            return;
        }

        if (plan.Changes.IsEmpty)
        {
            ShowMessage($"The taskbar is already on the {edge.Lower()}.", Severity.Info);
            return;
        }

        if (plan.Changes.RestartExplorer &&
            !await ConfirmAsync("Restart Explorer?",
                plan.Summary + "\n\nThe taskbar disappears for a moment and open File Explorer windows close.", "Move taskbar"))
        {
            return;
        }

        string message = string.Join(" ", new[] { $"Taskbar moved to the {edge.Lower()}." }.Concat(plan.Warnings));
        await RunAsync($"Moving the taskbar to the {edge.Lower()}…", () => _host.ApplyAsync(plan.Changes), message,
            plan.Warnings.Count > 0 ? Severity.Warning : Severity.Success);
    }

    internal FrameworkElement Page(string name)
    {
        if (!_pages.TryGetValue(name, out FrameworkElement? page))
        {
            page = name switch
            {
                "taskbar" => new TaskbarPage(this),
                "settings" => new SettingsPage(this),
                _ => new PositionPage(this),
            };
            _pages[name] = page;
        }

        return page;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_app.KeepRunningInTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
        if (!_app.ExitRequested)
        {
            // No tray icon to live in: closing the window ends the app.
            Dispatcher.BeginInvoke(_app.ExitApp);
        }
    }

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string name } && PageHost is not null)
        {
            PageHost.Content = Page(name);
        }
    }

    private void OnCloseInfoBar(object sender, RoutedEventArgs e) => InfoBar.Visibility = Visibility.Collapsed;

    private void OnDialogPrimary(object sender, RoutedEventArgs e) => CloseDialog(true);

    private void OnDialogSecondary(object sender, RoutedEventArgs e) => CloseDialog(false);

    private void CloseDialog(bool result)
    {
        DialogOverlay.Visibility = Visibility.Collapsed;
        TaskCompletionSource<bool>? dialog = _dialog;
        _dialog = null;
        dialog?.TrySetResult(result);
    }
}
