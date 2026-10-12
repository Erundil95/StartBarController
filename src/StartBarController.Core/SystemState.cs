using StartBarController.Core.Windhawk;
using Mods = StartBarController.Core.Windhawk.WindhawkMods;

namespace StartBarController.Core;

/// <summary>What the app knows about the machine's taskbar setup at a point in time.</summary>
public sealed record SystemState
{
    public required int WindowsBuild { get; init; }

    public bool ExplorerPatcherInstalled { get; init; }

    /// <summary>ExplorerPatcher's OldTaskbar value, or null if it has never been written.</summary>
    public int? ExplorerPatcherTaskbarStyle { get; init; }

    public int? ClassicStartMenu { get; init; }

    public byte[]? StuckRects3 { get; init; }

    public IReadOnlyDictionary<string, byte[]> MMStuckRects3 { get; init; } = new Dictionary<string, byte[]>();

    /// <summary>Advanced\TaskbarLocation, read by builds with the native taskbar position feature.</summary>
    public int? NativeTaskbarLocation { get; init; }

    public WindhawkInstallation? Windhawk { get; init; }

    public IReadOnlyDictionary<string, WindhawkModState> WindhawkMods { get; init; } =
        new Dictionary<string, WindhawkModState>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Set when Windhawk was found but its configuration could not be read.</summary>
    public string? WindhawkError { get; init; }

    /// <summary>Current values of the <see cref="TaskbarSettings"/>, by setting id (null when unset).</summary>
    public IReadOnlyDictionary<string, int?> Settings { get; init; } = new Dictionary<string, int?>();

    public bool IsWindows11 => WindowsRelease.IsWindows11(WindowsBuild);

    public bool ClassicTaskbarActive => ExplorerPatcherInstalled && ExplorerPatcherTaskbarStyle is 1 or 2;

    public bool WindhawkInstalled => Windhawk is not null;

    public WindhawkModState Mod(string modId) =>
        WindhawkMods.TryGetValue(modId, out var state) ? state : new WindhawkModState { ModId = modId };

    public bool IsModActive(string modId) => Mod(modId) is { Installed: true, Enabled: true };

    public TaskbarEdge? StuckRectsEdge => StuckRects.ReadEdge(StuckRects3);

    /// <summary>The engine currently placing the taskbar.</summary>
    public PositionEngine ActiveEngine =>
        WindhawkMods.Values.Any(m => m is { Installed: true, Enabled: true }) ? PositionEngine.Windhawk
        : ClassicTaskbarActive ? PositionEngine.ExplorerPatcher
        : PositionEngine.Native;

    /// <summary>Best guess of where the taskbar is right now.</summary>
    public TaskbarEdge CurrentEdge
    {
        get
        {
            if (IsModActive(Mods.TaskbarVertical))
            {
                return Mod(Mods.TaskbarVertical).GetText("taskbarLocation") == "right" ? TaskbarEdge.Right : TaskbarEdge.Left;
            }

            if (IsModActive(Mods.TaskbarOnTop))
            {
                return Mod(Mods.TaskbarOnTop).GetText("taskbarLocation") == "bottom" ? TaskbarEdge.Bottom : TaskbarEdge.Top;
            }

            if (!IsWindows11 || ClassicTaskbarActive)
            {
                return StuckRectsEdge ?? TaskbarEdge.Bottom;
            }

            if (NativeTaskbarLocation is >= 0 and <= 3)
            {
                return (TaskbarEdge)NativeTaskbarLocation.Value;
            }

            // Windows 11 21H2 honours "top" in StuckRects3; later builds ignore it.
            return WindowsBuild < WindowsRelease.Windows11_22H2 && StuckRectsEdge == TaskbarEdge.Top
                ? TaskbarEdge.Top
                : TaskbarEdge.Bottom;
        }
    }

    /// <summary>Turns the "Auto" preference into a concrete engine for this system.</summary>
    public PositionEngine ResolveEngine(PositionEngine preference)
    {
        if (preference != PositionEngine.Auto)
        {
            return preference;
        }

        if (!IsWindows11)
        {
            return PositionEngine.Native;
        }

        return !WindhawkInstalled && ClassicTaskbarActive ? PositionEngine.ExplorerPatcher : PositionEngine.Windhawk;
    }

    public int GetSetting(TaskbarSetting setting) =>
        Settings.TryGetValue(setting.Id, out int? value) && value is int v ? v : setting.DefaultValue;

    public string Describe()
    {
        string engine = ActiveEngine switch
        {
            PositionEngine.Windhawk => "Windhawk",
            PositionEngine.ExplorerPatcher => "ExplorerPatcher (Windows 10 taskbar)",
            _ => "Windows",
        };
        return $"{WindowsRelease.Describe(WindowsBuild)} · taskbar on the {CurrentEdge.Lower()} · placed by {engine}";
    }
}

/// <summary>Builds a <see cref="SystemState"/> from the registry and file system.</summary>
public sealed class SystemInspector
{
    private readonly IRegistry _registry;
    private readonly Func<string, bool> _directoryExists;
    private readonly IReadOnlyList<string> _explorerPatcherFolders;
    private readonly Func<WindhawkInstallation?> _locateWindhawk;
    private readonly Func<WindhawkStorage, WindhawkModStore> _modStore;
    private readonly int _windowsBuild;

    public SystemInspector(
        IRegistry registry,
        Func<string, bool> directoryExists,
        IReadOnlyList<string> explorerPatcherFolders,
        Func<WindhawkInstallation?> locateWindhawk,
        Func<WindhawkStorage, WindhawkModStore> modStore,
        int windowsBuild)
    {
        _registry = registry;
        _directoryExists = directoryExists;
        _explorerPatcherFolders = explorerPatcherFolders;
        _locateWindhawk = locateWindhawk;
        _modStore = modStore;
        _windowsBuild = windowsBuild;
    }

    public int WindowsBuild => _windowsBuild;

    public SystemState Inspect()
    {
        var mm = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in _registry.GetValueNames(Hive.CurrentUser, RegistryPaths.MMStuckRects3))
        {
            if (_registry.GetBinary(Hive.CurrentUser, RegistryPaths.MMStuckRects3, name) is byte[] blob)
            {
                mm[name] = blob;
            }
        }

        var settings = TaskbarSettings.All.ToDictionary(
            s => s.Id, s => _registry.GetDword(Hive.CurrentUser, s.Key, s.Name));

        WindhawkInstallation? windhawk = null;
        var mods = new Dictionary<string, WindhawkModState>(StringComparer.OrdinalIgnoreCase);
        string? windhawkError = null;
        try
        {
            windhawk = _locateWindhawk();
            if (windhawk is not null)
            {
                WindhawkModStore store = _modStore(windhawk.Storage);
                foreach (string id in Mods.All)
                {
                    mods[id] = store.Read(id);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            windhawkError = e.Message;
        }

        return new SystemState
        {
            WindowsBuild = _windowsBuild,
            ExplorerPatcherInstalled = IsExplorerPatcherInstalled(),
            ExplorerPatcherTaskbarStyle = _registry.GetDword(Hive.CurrentUser, RegistryPaths.ExplorerPatcher, RegistryPaths.OldTaskbar),
            ClassicStartMenu = _registry.GetDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.StartShowClassicMode),
            StuckRects3 = _registry.GetBinary(Hive.CurrentUser, RegistryPaths.StuckRects3, RegistryPaths.StuckRects3Value),
            MMStuckRects3 = mm,
            NativeTaskbarLocation = _registry.GetDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.TaskbarLocation),
            Windhawk = windhawk,
            WindhawkMods = mods,
            WindhawkError = windhawkError,
            Settings = settings,
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
                if (sub.Contains("ExplorerPatcher", StringComparison.OrdinalIgnoreCase) ||
                    _registry.GetString(hive, $@"{RegistryPaths.Uninstall}\{sub}", "DisplayName")?
                        .Contains("ExplorerPatcher", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
