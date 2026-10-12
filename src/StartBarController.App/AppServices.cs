using System.Diagnostics;
using StartBarController.App.Platform;
using StartBarController.Core;
using StartBarController.Core.Windhawk;

namespace StartBarController.App;

internal static class AppPaths
{
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StartBarController");

    public static string BackupFile => Path.Combine(DataFolder, "backup.json");

    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");
}

/// <summary>Wires the core logic to the real Windows implementations.</summary>
internal static class AppServices
{
    public const string RepositoryUrl = "https://github.com/Erundil95/StartBarController";
    public const string WindhawkUrl = "https://windhawk.net";
    public const string ExplorerPatcherUrl = "https://github.com/valinet/ExplorerPatcher/releases/latest";

    public static WindowsRegistry Registry { get; } = new();

    public static WindowsIniFiles Ini { get; } = new();

    public static WindhawkModStore ModStore(WindhawkStorage storage) => new(storage, Registry, Ini);

    public static WindhawkInstallation? LocateWindhawk()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return new WindhawkLocator(Registry, new WindowsFileSystem(), Ini, new[] { Path.Combine(programFiles, "Windhawk") }).Locate();
    }

    public static DirectWindhawkExecutor DirectExecutor() => new(ModStore, new ProcessRunner());

    public static TaskbarController CreateController()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var inspector = new SystemInspector(
            Registry,
            Directory.Exists,
            new[] { Path.Combine(programFiles, "ExplorerPatcher") },
            LocateWindhawk,
            ModStore,
            Environment.OSVersion.Version.Build);

        return new TaskbarController(inspector, Registry, new ExplorerShell(), new JsonFile<TaskbarBackup>(AppPaths.BackupFile),
            new ElevatingWindhawkExecutor(DirectExecutor()));
    }

    public static JsonFile<AppSettings> SettingsFile { get; } = new(AppPaths.SettingsFile);

    public static AppSettings LoadSettings() => SettingsFile.Load() ?? new AppSettings();

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    /// <summary>Opens a console window running winget, so the user sees progress and any prompts.</summary>
    public static void WingetInstall(string packageId) =>
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c winget install -e --id {packageId} --accept-source-agreements --accept-package-agreements & pause")
        {
            UseShellExecute = true,
        })?.Dispose();
}

/// <summary>The "start with Windows" entry under HKCU\...\Run.</summary>
internal static class Autostart
{
    private const string ValueName = "StartBarController";

    public static bool IsEnabled => AppServices.Registry.GetString(Hive.CurrentUser, RegistryPaths.Run, ValueName) is { Length: > 0 };

    public static void Set(bool enabled)
    {
        if (enabled)
        {
            AppServices.Registry.SetString(Hive.CurrentUser, RegistryPaths.Run, ValueName, $"\"{Environment.ProcessPath}\" --tray");
        }
        else
        {
            AppServices.Registry.DeleteValue(Hive.CurrentUser, RegistryPaths.Run, ValueName);
        }
    }
}
