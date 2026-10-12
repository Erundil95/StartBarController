namespace StartBarController.Core;

/// <summary>Applies change sets, keeping a backup of every original value it overwrites.</summary>
public sealed class TaskbarController
{
    private readonly SystemInspector _inspector;
    private readonly IRegistry _registry;
    private readonly IExplorerShell _shell;
    private readonly JsonFile<TaskbarBackup> _backups;
    private readonly IWindhawkExecutor _windhawk;

    public TaskbarController(
        SystemInspector inspector, IRegistry registry, IExplorerShell shell, JsonFile<TaskbarBackup> backups, IWindhawkExecutor windhawk)
    {
        _inspector = inspector;
        _registry = registry;
        _shell = shell;
        _backups = backups;
        _windhawk = windhawk;
    }

    public IExplorerShell Shell => _shell;

    public int WindowsBuild => _inspector.WindowsBuild;

    public bool HasBackup => _backups.Load() is { IsEmpty: false };

    public SystemState Inspect() => _inspector.Inspect();

    public PositionPlan Plan(PositionRequest request, SystemState? state = null) =>
        PositionPlanner.Plan(state ?? Inspect(), request);

    /// <summary>Applies <paramref name="changes"/>. Windhawk work runs first, so a cancelled elevation prompt changes nothing else.</summary>
    public void Apply(ChangeSet changes, SystemState? state = null, bool restartExplorer = true)
    {
        if (changes.IsEmpty)
        {
            return;
        }

        state ??= Inspect();
        if ((changes.Windhawk.Count > 0 || changes.InstallMods.Count > 0) && state.Windhawk is null)
        {
            throw new InvalidOperationException("Windhawk isn't installed.");
        }

        TaskbarBackup backup = _backups.Load() ?? new TaskbarBackup { CreatedUtc = DateTime.UtcNow, WindowsBuild = state.WindowsBuild };
        backup.Record(changes, state, _registry);
        _backups.Save(backup);

        Execute(changes, state, restartExplorer);
    }

    /// <summary>Writes back every original value. Returns false if nothing was backed up.</summary>
    public bool Restore(bool restartExplorer = true)
    {
        TaskbarBackup? backup = _backups.Load();
        if (backup is null || backup.IsEmpty)
        {
            return false;
        }

        SystemState state = Inspect();
        ChangeSet changes = backup.RestoreChanges();
        if (state.Windhawk is null)
        {
            changes = changes with { Windhawk = Array.Empty<Windhawk.WindhawkModEdit>() };
        }

        Execute(changes, state, restartExplorer);
        _backups.Delete();
        return true;
    }

    private void Execute(ChangeSet changes, SystemState state, bool restartExplorer)
    {
        if (changes.Windhawk.Count > 0 || changes.InstallMods.Count > 0)
        {
            _windhawk.Execute(state.Windhawk!, changes.InstallMods, changes.Windhawk);
        }

        foreach (RegistryEdit edit in changes.Registry)
        {
            _registry.Apply(edit);
        }

        if (changes.AutoHide is bool autoHide)
        {
            _shell.SetAutoHide(autoHide);
        }

        if (changes.Registry.Count > 0)
        {
            _shell.NotifySettingsChanged();
        }

        if (restartExplorer && changes.RestartExplorer)
        {
            _shell.RestartExplorer();
        }
    }
}
