using StartBarController.Core;

namespace StartBarController.App;

/// <summary>Shared state of the running app: the controller, settings and last inspected system state.</summary>
internal sealed class AppHost
{
    public AppHost(TaskbarController controller)
    {
        Controller = controller;
        Settings = AppServices.LoadSettings();
        State = controller.Inspect();
        AutoHide = ReadAutoHide();
    }

    /// <summary>Raised on the UI thread after the state or settings change.</summary>
    public event EventHandler? Changed;

    public TaskbarController Controller { get; }

    public AppSettings Settings { get; private set; }

    public SystemState State { get; private set; }

    public bool AutoHide { get; private set; }

    public void Refresh()
    {
        State = Controller.Inspect();
        AutoHide = ReadAutoHide();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateSettings(Func<AppSettings, AppSettings> update)
    {
        Settings = update(Settings);
        AppServices.SettingsFile.Save(Settings);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public PositionPlan PlanPosition(TaskbarEdge edge) =>
        PositionPlanner.Plan(State, new PositionRequest(edge, Settings.Engine, Settings.ClassicStartMenu, Settings.Windhawk));

    /// <summary>Applies changes on a worker thread (it may wait for UAC, winget or Explorer), then refreshes.</summary>
    public async Task ApplyAsync(ChangeSet changes)
    {
        SystemState state = State;
        try
        {
            await Task.Run(() => Controller.Apply(changes, state));
        }
        finally
        {
            Refresh();
        }
    }

    public async Task<bool> RestoreAsync()
    {
        try
        {
            return await Task.Run(() => Controller.Restore());
        }
        finally
        {
            Refresh();
        }
    }

    private bool ReadAutoHide()
    {
        try
        {
            return Controller.Shell.GetAutoHide();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
