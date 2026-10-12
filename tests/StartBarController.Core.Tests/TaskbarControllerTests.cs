using StartBarController.Core.Windhawk;

namespace StartBarController.Core.Tests;

public class TaskbarControllerTests : IDisposable
{
    private readonly FakeRegistry _registry = new();
    private readonly FakeIni _ini = new();
    private readonly FakeShell _shell = new();
    private readonly FakeProcessRunner _processes = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sbc-{Guid.NewGuid():N}");
    private readonly WindhawkInstallation _windhawk = Samples.Windhawk(cli: true);
    private bool _windhawkInstalled = true;
    private int _build = Samples.Win11_23H2;

    public TaskbarControllerTests()
    {
        _registry.SetBinary(Hive.CurrentUser, RegistryPaths.StuckRects3, RegistryPaths.StuckRects3Value, Samples.StuckRectsBottom());
        _registry.SetBinary(Hive.CurrentUser, RegistryPaths.MMStuckRects3, "MON2", Samples.StuckRectsBottom());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private WindhawkModStore Store(WindhawkStorage storage) => new(storage, _registry, _ini);

    private TaskbarController Controller() => new(
        new SystemInspector(_registry, _ => false, Array.Empty<string>(), () => _windhawkInstalled ? _windhawk : null, Store, _build),
        _registry,
        _shell,
        new JsonFile<TaskbarBackup>(Path.Combine(_dir, "backup.json")),
        new DirectWindhawkExecutor(Store, _processes));

    private string ModKey(string id) => _windhawk.Storage.ModRegistryKey(id);

    private void InstallMod(string id, bool enabled) =>
        _registry.SetDword(Hive.LocalMachine, ModKey(id), "Disabled", enabled ? 0 : 1);

    private void MarkInstalled(string id) => _registry.SetString(Hive.LocalMachine, ModKey(id), "LibraryFileName", id + ".dll");

    [Fact]
    public void WindhawkTopInstallsConfiguresAndEnablesWithoutRestart()
    {
        // The fake CLI "installs" the mod by writing its library name, like Windhawk would.
        _processes.Handler = (_, args) =>
        {
            MarkInstalled(args[2]);
            InstallMod(args[2], enabled: false);
            return new ProcessResult(0, "");
        };
        var controller = Controller();

        var plan = controller.Plan(new PositionRequest(TaskbarEdge.Top));
        controller.Apply(plan.Changes);

        var state = controller.Inspect();
        Assert.True(state.IsModActive(WindhawkMods.TaskbarOnTop));
        Assert.Equal(TaskbarEdge.Top, state.CurrentEdge);
        Assert.Equal(PositionEngine.Windhawk, state.ActiveEngine);
        Assert.Equal(0, _shell.Restarts);
        Assert.True(controller.HasBackup);
    }

    [Fact]
    public void RestoreRevertsWindhawkAndRegistryChanges()
    {
        MarkInstalled(WindhawkMods.TaskbarVertical);
        InstallMod(WindhawkMods.TaskbarVertical, enabled: false);
        _registry.SetString(Hive.LocalMachine, ModKey(WindhawkMods.TaskbarVertical) + @"\Settings", "taskbarLocation", "left");
        var controller = Controller();

        controller.Apply(controller.Plan(new PositionRequest(TaskbarEdge.Right)).Changes);
        controller.Apply(new ChangeSet { Registry = new[] { TaskbarSettings.Alignment.Edit(0) } });
        Assert.Equal(TaskbarEdge.Right, controller.Inspect().CurrentEdge);

        Assert.True(controller.Restore());

        var state = controller.Inspect();
        Assert.False(state.IsModActive(WindhawkMods.TaskbarVertical));
        Assert.Equal("left", state.Mod(WindhawkMods.TaskbarVertical).GetText("taskbarLocation"));
        Assert.Null(state.Mod(WindhawkMods.TaskbarVertical).GetNumber("TaskbarWidth"));
        Assert.Null(_registry.GetDword(Hive.CurrentUser, RegistryPaths.Advanced, "TaskbarAl"));
        Assert.False(controller.HasBackup);
        Assert.False(controller.Restore());
    }

    [Fact]
    public void RestoreTurnsOffModsThisAppInstalled()
    {
        _processes.Handler = (_, args) => { MarkInstalled(args[2]); return new ProcessResult(0, ""); };
        var controller = Controller();
        controller.Apply(controller.Plan(new PositionRequest(TaskbarEdge.Left)).Changes);

        controller.Restore();

        var mod = controller.Inspect().Mod(WindhawkMods.TaskbarVertical);
        Assert.True(mod.Installed);
        Assert.False(mod.Enabled);
        Assert.Equal("left", mod.GetText("taskbarLocation")); // settings kept for later use
    }

    [Fact]
    public void RegistryEnginesRestartExplorerAndRestoreBlobs()
    {
        _build = Samples.Win10;
        _windhawkInstalled = false;
        var controller = Controller();

        controller.Apply(controller.Plan(new PositionRequest(TaskbarEdge.Left)).Changes);

        Assert.Equal(1, _shell.Restarts);
        Assert.Equal(TaskbarEdge.Left, controller.Inspect().CurrentEdge);
        Assert.Equal(TaskbarEdge.Left, StuckRects.ReadEdge(_registry.GetBinary(Hive.CurrentUser, RegistryPaths.MMStuckRects3, "MON2")));

        controller.Restore(restartExplorer: false);

        Assert.Equal(TaskbarEdge.Bottom, controller.Inspect().CurrentEdge);
        Assert.Equal(1, _shell.Restarts);
    }

    [Fact]
    public void SettingChangesNotifyExplorerWithoutRestart()
    {
        var controller = Controller();
        controller.Apply(new ChangeSet { Registry = new[] { TaskbarSettings.Search.Edit(0) }, AutoHide = true });

        Assert.Equal(0, _registry.GetDword(Hive.CurrentUser, RegistryPaths.Search, "SearchboxTaskbarMode"));
        Assert.Equal(1, _shell.Notifications);
        Assert.Equal(0, _shell.Restarts);
        Assert.True(_shell.AutoHide);
    }

    [Fact]
    public void FailedWindhawkStepLeavesRegistryUntouched()
    {
        _processes.Handler = (_, _) => new ProcessResult(1, "cancelled");
        var controller = Controller();
        var changes = controller.Plan(new PositionRequest(TaskbarEdge.Top)).Changes
            .Merge(new ChangeSet { Registry = new[] { TaskbarSettings.TaskView.Edit(0) } });

        Assert.Throws<InvalidOperationException>(() => controller.Apply(changes));
        Assert.Null(_registry.GetDword(Hive.CurrentUser, RegistryPaths.Advanced, "ShowTaskViewButton"));
    }

    [Fact]
    public void InspectReportsSettings()
    {
        _registry.SetDword(Hive.CurrentUser, RegistryPaths.Advanced, "TaskbarAl", 0);
        var state = Controller().Inspect();
        Assert.Equal(0, state.GetSetting(TaskbarSettings.Alignment));
        Assert.Equal(TaskbarSettings.TaskView.DefaultValue, state.GetSetting(TaskbarSettings.TaskView));
    }

    [Fact]
    public void BackupSurvivesJsonRoundTrip()
    {
        var file = new JsonFile<TaskbarBackup>(Path.Combine(_dir, "b.json"));
        var backup = new TaskbarBackup { WindowsBuild = 1 };
        backup.Registry["x"] = new RegistryEdit(RegistryPaths.StuckRects3, "Settings", RegistryData.FromBinary(new byte[] { 1, 2 }));
        backup.Registry["y"] = new RegistryEdit(RegistryPaths.Advanced, "TaskbarAl", null);
        backup.Windhawk["taskbar-on-top"] = new WindhawkModBackup { Installed = true, Settings = { ["taskbarLocation"] = ModSettingValue.Of("top"), ["w"] = null } };

        file.Save(backup);
        var copy = file.Load()!;

        Assert.Equal(new byte[] { 1, 2 }, copy.Registry["x"].Value!.Binary);
        Assert.Null(copy.Registry["y"].Value);
        Assert.Equal("top", copy.Windhawk["taskbar-on-top"].Settings["taskbarLocation"]!.Text);
    }
}
