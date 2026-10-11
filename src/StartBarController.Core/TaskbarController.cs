namespace StartBarController.Core;

/// <summary>Applies taskbar changes, backing up the original values the first time.</summary>
public sealed class TaskbarController
{
    private readonly IRegistry _registry;
    private readonly IExplorerShell _shell;
    private readonly IBackupStore _backups;
    private readonly SystemInspector _inspector;
    private readonly int _windowsBuild;

    public TaskbarController(IRegistry registry, IExplorerShell shell, IBackupStore backups, SystemInspector inspector, int windowsBuild)
    {
        _registry = registry;
        _shell = shell;
        _backups = backups;
        _inspector = inspector;
        _windowsBuild = windowsBuild;
    }

    public IExplorerShell Shell => _shell;

    public bool HasBackup => _backups.Load() is not null;

    public SystemSnapshot Inspect() => _inspector.Inspect(_windowsBuild);

    public PositionPlan Plan(PositionRequest request) => PositionPlanner.Plan(Inspect(), request);

    public void Apply(PositionPlan plan, bool restartExplorer = true)
    {
        if (!plan.CanApply)
        {
            throw new InvalidOperationException(plan.Summary);
        }

        byte[] stuckRects = _registry.GetBinary(Hive.CurrentUser, RegistryPaths.StuckRects3, RegistryPaths.StuckRects3Value)
            ?? throw new InvalidOperationException(
                "The StuckRects3 taskbar settings were not found in the registry. Sign out and back in, then try again.");
        if (stuckRects.Length < StuckRects.MinimumLength)
        {
            throw new InvalidOperationException($"Unexpected StuckRects3 format ({stuckRects.Length} bytes).");
        }

        EnsureBackup();

        _registry.SetBinary(Hive.CurrentUser, RegistryPaths.StuckRects3, RegistryPaths.StuckRects3Value,
            StuckRects.WithEdge(stuckRects, plan.Edge));

        // Secondary monitors keep their own blob; move them along with the primary taskbar.
        foreach (string name in _registry.GetValueNames(Hive.CurrentUser, RegistryPaths.MMStuckRects3))
        {
            byte[]? blob = _registry.GetBinary(Hive.CurrentUser, RegistryPaths.MMStuckRects3, name);
            if (blob is { Length: >= StuckRects.MinimumLength })
            {
                _registry.SetBinary(Hive.CurrentUser, RegistryPaths.MMStuckRects3, name, StuckRects.WithEdge(blob, plan.Edge));
            }
        }

        if (plan.SetExplorerPatcherTaskbarStyle is int style)
        {
            _registry.SetDword(Hive.CurrentUser, RegistryPaths.ExplorerPatcher, RegistryPaths.OldTaskbar, style);
        }

        if (plan.SetClassicStartMenu is int classic)
        {
            _registry.SetDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.StartShowClassicMode, classic);
        }

        if (restartExplorer)
        {
            _shell.RestartExplorer();
        }
    }

    /// <summary>
    /// Sets taskbar icon alignment. The Windows 11 taskbar picks the change up live;
    /// ExplorerPatcher's classic taskbar is updated too when it is installed.
    /// </summary>
    public void SetAlignment(TaskbarAlignment alignment)
    {
        EnsureBackup();
        int value = alignment == TaskbarAlignment.Left ? 0 : 1;
        _registry.SetDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.TaskbarAl, value);
        if (Inspect().ExplorerPatcherInstalled)
        {
            _registry.SetDword(Hive.CurrentUser, RegistryPaths.ExplorerPatcher, RegistryPaths.OldTaskbarAl, value);
        }
    }

    /// <summary>Writes back the values saved before the first change. Returns false if there is no backup.</summary>
    public bool Restore(bool restartExplorer = true)
    {
        TaskbarBackup? backup = _backups.Load();
        if (backup is null)
        {
            return false;
        }

        if (backup.StuckRects3 is not null)
        {
            _registry.SetBinary(Hive.CurrentUser, RegistryPaths.StuckRects3, RegistryPaths.StuckRects3Value, backup.StuckRects3);
        }

        foreach ((string name, byte[] blob) in backup.MMStuckRects3)
        {
            _registry.SetBinary(Hive.CurrentUser, RegistryPaths.MMStuckRects3, name, blob);
        }

        _registry.SetOrDeleteDword(Hive.CurrentUser, RegistryPaths.ExplorerPatcher, RegistryPaths.OldTaskbar, backup.OldTaskbar);
        _registry.SetOrDeleteDword(Hive.CurrentUser, RegistryPaths.ExplorerPatcher, RegistryPaths.OldTaskbarAl, backup.OldTaskbarAl);
        _registry.SetOrDeleteDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.StartShowClassicMode, backup.StartShowClassicMode);
        _registry.SetOrDeleteDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.TaskbarAl, backup.TaskbarAl);

        _backups.Delete();

        if (restartExplorer)
        {
            _shell.RestartExplorer();
        }

        return true;
    }

    private void EnsureBackup()
    {
        if (_backups.Load() is not null)
        {
            return;
        }

        var backup = new TaskbarBackup
        {
            CreatedUtc = DateTime.UtcNow,
            WindowsBuild = _windowsBuild,
            StuckRects3 = _registry.GetBinary(Hive.CurrentUser, RegistryPaths.StuckRects3, RegistryPaths.StuckRects3Value),
            OldTaskbar = _registry.GetDword(Hive.CurrentUser, RegistryPaths.ExplorerPatcher, RegistryPaths.OldTaskbar),
            OldTaskbarAl = _registry.GetDword(Hive.CurrentUser, RegistryPaths.ExplorerPatcher, RegistryPaths.OldTaskbarAl),
            StartShowClassicMode = _registry.GetDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.StartShowClassicMode),
            TaskbarAl = _registry.GetDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.TaskbarAl),
        };

        foreach (string name in _registry.GetValueNames(Hive.CurrentUser, RegistryPaths.MMStuckRects3))
        {
            if (_registry.GetBinary(Hive.CurrentUser, RegistryPaths.MMStuckRects3, name) is byte[] blob)
            {
                backup.MMStuckRects3[name] = blob;
            }
        }

        _backups.Save(backup);
    }
}
