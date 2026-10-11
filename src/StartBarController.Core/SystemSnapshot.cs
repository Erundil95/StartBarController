namespace StartBarController.Core;

/// <summary>What the app knows about the machine's taskbar setup at a point in time.</summary>
public sealed record SystemSnapshot
{
    public required int WindowsBuild { get; init; }

    public required bool ExplorerPatcherInstalled { get; init; }

    /// <summary>ExplorerPatcher's OldTaskbar value, or null if it has never been written.</summary>
    public int? ExplorerPatcherTaskbarStyle { get; init; }

    /// <summary>Start_ShowClassicMode, or null if absent.</summary>
    public int? ClassicStartMenu { get; init; }

    /// <summary>Edge stored in StuckRects3, or null if the value is missing or unreadable.</summary>
    public TaskbarEdge? CurrentEdge { get; init; }

    public TaskbarAlignment? CurrentAlignment { get; init; }

    /// <summary>True when ExplorerPatcher is installed and set to one of its Windows 10 taskbar styles.</summary>
    public bool ClassicTaskbarActive =>
        ExplorerPatcherInstalled && ExplorerPatcherTaskbarStyle is 1 or 2;

    public string Describe()
    {
        string ep = !ExplorerPatcherInstalled
            ? "ExplorerPatcher: not installed"
            : ExplorerPatcherTaskbarStyle switch
            {
                1 => "ExplorerPatcher: installed, taskbar style Windows 10",
                2 => "ExplorerPatcher: installed, taskbar style Windows 10 (ExplorerPatcher)",
                _ => "ExplorerPatcher: installed, taskbar style Windows 11",
            };
        string edge = CurrentEdge?.ToString() ?? "unknown";
        return $"{WindowsRelease.Describe(WindowsBuild)} · {ep} · taskbar edge: {edge}";
    }
}

/// <summary>Builds a <see cref="SystemSnapshot"/> from the registry.</summary>
public sealed class SystemInspector
{
    private readonly IRegistry _registry;
    private readonly Func<string, bool> _directoryExists;
    private readonly IReadOnlyList<string> _explorerPatcherFolders;

    /// <param name="explorerPatcherFolders">Install folders to probe, e.g. "%ProgramFiles%\ExplorerPatcher".</param>
    public SystemInspector(IRegistry registry, Func<string, bool> directoryExists, IReadOnlyList<string> explorerPatcherFolders)
    {
        _registry = registry;
        _directoryExists = directoryExists;
        _explorerPatcherFolders = explorerPatcherFolders;
    }

    public SystemSnapshot Inspect(int windowsBuild)
    {
        byte[]? stuckRects = _registry.GetBinary(Hive.CurrentUser, RegistryPaths.StuckRects3, RegistryPaths.StuckRects3Value);
        int? alignment = _registry.GetDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.TaskbarAl);

        return new SystemSnapshot
        {
            WindowsBuild = windowsBuild,
            ExplorerPatcherInstalled = IsExplorerPatcherInstalled(),
            ExplorerPatcherTaskbarStyle = _registry.GetDword(Hive.CurrentUser, RegistryPaths.ExplorerPatcher, RegistryPaths.OldTaskbar),
            ClassicStartMenu = _registry.GetDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.StartShowClassicMode),
            CurrentEdge = StuckRects.ReadEdge(stuckRects),
            // Windows 11 defaults to centered when TaskbarAl is absent.
            CurrentAlignment = alignment == 0 ? TaskbarAlignment.Left
                : alignment is null && !WindowsRelease.IsWindows11(windowsBuild) ? TaskbarAlignment.Left
                : TaskbarAlignment.Center,
        };
    }

    private bool IsExplorerPatcherInstalled()
    {
        if (_explorerPatcherFolders.Any(_directoryExists))
        {
            return true;
        }

        foreach (Hive hive in new[] { Hive.LocalMachine, Hive.CurrentUser })
        {
            foreach (string sub in _registry.GetSubKeyNames(hive, RegistryPaths.Uninstall))
            {
                if (sub.Contains("ExplorerPatcher", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string? name = _registry.GetString(hive, $@"{RegistryPaths.Uninstall}\{sub}", "DisplayName");
                if (name?.Contains("ExplorerPatcher", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
