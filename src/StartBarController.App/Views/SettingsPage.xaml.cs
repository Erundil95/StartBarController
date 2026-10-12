using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace StartBarController.App.Views;

public partial class SettingsPage : UserControl
{
    private readonly MainWindow _window;
    private readonly AppHost _host;

    public SettingsPage(MainWindow window)
    {
        _window = window;
        _host = window.Host;
        InitializeComponent();
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        VersionText.Text = $"Start Bar Controller {version}";
        _host.Changed += (_, _) => Update();
        Update();
    }

    private void Update()
    {
        AutostartToggle.IsChecked = Autostart.IsEnabled;
        TrayToggle.IsChecked = _host.Settings.ShowTrayIcon;
        CloseToTrayToggle.IsChecked = _host.Settings.CloseToTray;
        CloseToTrayToggle.IsEnabled = _host.Settings.ShowTrayIcon;
        bool backup = _host.Controller.HasBackup;
        RestoreButton.IsEnabled = backup;
        RestoreCaption.Text = backup
            ? "Put back every taskbar setting this app changed, including turning off the Windhawk mods it enabled."
            : "Nothing to restore: this app hasn't changed anything yet.";
    }

    private void OnAutostartClick(object sender, RoutedEventArgs e)
    {
        Autostart.Set(AutostartToggle.IsChecked == true);
        Update();
    }

    private void OnTrayClick(object sender, RoutedEventArgs e)
    {
        _host.UpdateSettings(s => s with { ShowTrayIcon = TrayToggle.IsChecked == true });
        _window.AppInstance.UpdateTrayIcon();
    }

    private void OnCloseToTrayClick(object sender, RoutedEventArgs e) =>
        _host.UpdateSettings(s => s with { CloseToTray = CloseToTrayToggle.IsChecked == true });

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (await _window.ConfirmAsync("Restore the original taskbar?",
                "Every taskbar setting this app changed goes back to how it was. Explorer may restart.", "Restore"))
        {
            await _window.RunAsync("Restoring the original taskbar…", () => _host.RestoreAsync(), "The original taskbar settings are back.");
        }
    }

    private void OnProjectClick(object sender, RoutedEventArgs e) => AppServices.OpenUrl(AppServices.RepositoryUrl);

    private void OnWindhawkClick(object sender, RoutedEventArgs e) => AppServices.OpenUrl(AppServices.WindhawkUrl);

    private void OnExplorerPatcherClick(object sender, RoutedEventArgs e) => AppServices.OpenUrl(AppServices.ExplorerPatcherUrl);

    private void OnDataFolderClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.DataFolder);
        Process.Start(new ProcessStartInfo(AppPaths.DataFolder) { UseShellExecute = true })?.Dispose();
    }
}
