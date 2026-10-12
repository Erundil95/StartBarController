using System.Windows;
using System.Windows.Threading;

namespace StartBarController.App;

public partial class App : Application
{
    private readonly bool _startInTray;
    private readonly EventWaitHandle _showEvent;
    private readonly string? _smokeTestFolder;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private AppHost? _host;

    internal App(bool startInTray, EventWaitHandle showEvent, string? smokeTestFolder = null)
    {
        _startInTray = startInTray;
        _showEvent = showEvent;
        _smokeTestFolder = smokeTestFolder;
    }

    internal bool ExitRequested { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        _host = new AppHost(AppServices.CreateController());
        _window = new MainWindow(_host, this);
        UpdateTrayIcon();

        if (_smokeTestFolder is not null)
        {
            _window.Show();
            _ = SmokeTest.RunAsync(_window, _smokeTestFolder);
            return;
        }

        if (!_startInTray || _tray is null)
        {
            _window.Show();
        }

        // A second launch signals this event to bring the window up.
        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                Dispatcher.BeginInvoke(ShowMainWindow);
            }
        }) { IsBackground = true, Name = "Single instance listener" };
        listener.Start();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Signing out or shutting down: don't hide in the tray, just exit.
        ExitRequested = true;
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }

    internal void UpdateTrayIcon()
    {
        if (_host!.Settings.ShowTrayIcon && _tray is null)
        {
            _tray = new TrayIcon(_host, ShowMainWindow, edge => _ = MoveFromTrayAsync(edge), ExitApp);
        }
        else if (!_host.Settings.ShowTrayIcon && _tray is not null)
        {
            _tray.Dispose();
            _tray = null;
        }
    }

    internal bool KeepRunningInTray => _tray is not null && _host!.Settings.CloseToTray && !ExitRequested;

    internal void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        _host!.Refresh();
    }

    internal void ExitApp()
    {
        ExitRequested = true;
        Shutdown();
    }

    private async Task MoveFromTrayAsync(Core.TaskbarEdge edge)
    {
        // Moves that need confirmation or setup happen in the window.
        _host!.Refresh();
        await _window!.MoveTaskbarAsync(edge, fromTray: true);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_smokeTestFolder is not null)
        {
            File.WriteAllText(Path.Combine(_smokeTestFolder, "error.txt"), e.Exception.ToString());
            Shutdown(1);
            return;
        }

        e.Handled = true;
        _window?.ShowMessage("Something went wrong: " + e.Exception.Message, Severity.Error);
    }
}
