namespace StartBarController.Core.Windhawk;

/// <summary>Where a Windhawk installation keeps its mod configuration.</summary>
public sealed record WindhawkStorage
{
    /// <summary>True for portable installs: one INI file per mod under <see cref="ModsIniFolder"/>.</summary>
    public bool Portable { get; init; }

    /// <summary>Registry root of the engine configuration, e.g. HKLM\SOFTWARE\Windhawk\Engine.</summary>
    public Hive RegistryHive { get; init; }

    public string RegistryKey { get; init; } = string.Empty;

    public string ModsIniFolder { get; init; } = string.Empty;

    public string ModRegistryKey(string modId) => $@"{RegistryKey}\Mods\{modId}";

    public string ModIniPath(string modId) => WinPath.Combine(ModsIniFolder, modId + ".ini");
}

public sealed record WindhawkInstallation
{
    public required string InstallFolder { get; init; }

    public required string AppPath { get; init; }

    /// <summary>windhawk-cli.exe (Windhawk 2.0 and later), which can install mods unattended.</summary>
    public string? CliPath { get; init; }

    public required WindhawkStorage Storage { get; init; }
}

/// <summary>
/// Finds Windhawk the way Windhawk itself does: windhawk.ini next to windhawk.exe names
/// the engine folder, and the engine's engine.ini names where mod configuration lives.
/// </summary>
public sealed class WindhawkLocator
{
    private readonly IRegistry _registry;
    private readonly IFileSystem _files;
    private readonly IIniFiles _ini;
    private readonly IReadOnlyList<string> _defaultFolders;

    public WindhawkLocator(IRegistry registry, IFileSystem files, IIniFiles ini, IReadOnlyList<string> defaultFolders)
    {
        _registry = registry;
        _files = files;
        _ini = ini;
        _defaultFolders = defaultFolders;
    }

    public WindhawkInstallation? Locate()
    {
        foreach (string folder in CandidateFolders())
        {
            if (TryLoad(folder) is { } installation)
            {
                return installation;
            }
        }

        return null;
    }

    private IEnumerable<string> CandidateFolders()
    {
        foreach (Hive hive in new[] { Hive.LocalMachine, Hive.CurrentUser })
        {
            foreach (string sub in _registry.GetSubKeyNames(hive, RegistryPaths.Uninstall))
            {
                string key = $@"{RegistryPaths.Uninstall}\{sub}";
                string? name = _registry.GetString(hive, key, "DisplayName");
                if (name is null || !name.StartsWith("Windhawk", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (_registry.GetString(hive, key, "InstallLocation") is { Length: > 0 } location)
                {
                    yield return location.Trim('"');
                }

                if (_registry.GetString(hive, key, "UninstallString") is { Length: > 0 } uninstall)
                {
                    string exe = uninstall.Trim().Trim('"');
                    int end = exe.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                    if (end > 0 && WinPath.GetDirectory(exe[..(end + 4)]) is { Length: > 0 } dir)
                    {
                        yield return dir;
                    }
                }
            }
        }

        foreach (string folder in _defaultFolders)
        {
            yield return folder;
        }
    }

    private WindhawkInstallation? TryLoad(string folder)
    {
        string appPath = WinPath.Combine(folder, "windhawk.exe");
        string appIni = WinPath.Combine(folder, "windhawk.ini");
        if (!_files.FileExists(appPath) || !_files.FileExists(appIni))
        {
            return null;
        }

        string? enginePath = ResolvePath(_ini.Read(appIni, "Storage", "EnginePath"), folder);
        if (enginePath is null)
        {
            return null;
        }

        string engineIni = WinPath.Combine(enginePath, "engine.ini");
        if (!_files.FileExists(engineIni))
        {
            return null;
        }

        WindhawkStorage? storage = ReadStorage(engineIni, enginePath);
        if (storage is null)
        {
            return null;
        }

        string cli = WinPath.Combine(folder, "windhawk-cli.exe");
        return new WindhawkInstallation
        {
            InstallFolder = folder,
            AppPath = appPath,
            CliPath = _files.FileExists(cli) ? cli : null,
            Storage = storage,
        };
    }

    private WindhawkStorage? ReadStorage(string engineIni, string engineFolder)
    {
        bool portable = int.TryParse(_ini.Read(engineIni, "Storage", "Portable"), out int p) && p != 0;
        if (portable)
        {
            string? appData = ResolvePath(_ini.Read(engineIni, "Storage", "AppDataPath"), engineFolder);
            return appData is null ? null : new WindhawkStorage { Portable = true, ModsIniFolder = WinPath.Combine(appData, "Mods") };
        }

        return RegistryExtensions.TryParseRoot(_ini.Read(engineIni, "Storage", "RegistryKey"), out Hive hive, out string key)
            ? new WindhawkStorage { RegistryHive = hive, RegistryKey = key }
            : null;
    }

    /// <summary>Expands environment variables and resolves a relative path against <paramref name="baseFolder"/>.</summary>
    private string? ResolvePath(string? value, string baseFolder)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string expanded = _files.ExpandEnvironmentVariables(value.Trim());
        return WinPath.IsAbsolute(expanded) ? expanded : WinPath.Combine(baseFolder, expanded);
    }
}
