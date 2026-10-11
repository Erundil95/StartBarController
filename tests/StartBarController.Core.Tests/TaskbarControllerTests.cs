namespace StartBarController.Core.Tests;

public class TaskbarControllerTests
{
    private const int Win11_23H2 = 22631;

    private readonly FakeRegistry _registry = new();
    private readonly FakeShell _shell = new();
    private readonly MemoryBackupStore _backups = new();
    private bool _epFolder;

    public TaskbarControllerTests()
    {
        _registry.SetBinary(Hive.CurrentUser, RegistryPaths.StuckRects3, RegistryPaths.StuckRects3Value, Samples.StuckRectsBottom());
        _registry.SetBinary(Hive.CurrentUser, RegistryPaths.MMStuckRects3, "MON1", Samples.StuckRectsBottom());
    }

    private TaskbarController Controller(int build = Win11_23H2) => new(
        _registry, _shell, _backups,
        new SystemInspector(_registry, _ => _epFolder, new[] { @"C:\Program Files\ExplorerPatcher" }),
        build);

    private TaskbarEdge? Edge(string key, string name) => StuckRects.ReadEdge(_registry.GetBinary(Hive.CurrentUser, key, name));

    [Fact]
    public void InspectDetectsExplorerPatcherFromFolder()
    {
        Assert.False(Controller().Inspect().ExplorerPatcherInstalled);
        _epFolder = true;
        Assert.True(Controller().Inspect().ExplorerPatcherInstalled);
    }

    [Fact]
    public void InspectDetectsExplorerPatcherFromUninstallEntry()
    {
        _registry.SetString(Hive.LocalMachine, RegistryPaths.Uninstall + @"\{ABC}", "DisplayName", "ExplorerPatcher");
        Assert.True(Controller().Inspect().ExplorerPatcherInstalled);
    }

    [Fact]
    public void InspectReadsEdgeAndAlignment()
    {
        _registry.SetDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.TaskbarAl, 0);
        var snapshot = Controller().Inspect();
        Assert.Equal(TaskbarEdge.Bottom, snapshot.CurrentEdge);
        Assert.Equal(TaskbarAlignment.Left, snapshot.CurrentAlignment);
    }

    [Fact]
    public void ApplyWithExplorerPatcherWritesEdgesStyleAndRestarts()
    {
        _epFolder = true;
        var controller = Controller();
        var plan = controller.Plan(new PositionRequest(TaskbarEdge.Top, StartMenuStyle.Windows10));

        controller.Apply(plan);

        Assert.Equal(TaskbarEdge.Top, Edge(RegistryPaths.StuckRects3, RegistryPaths.StuckRects3Value));
        Assert.Equal(TaskbarEdge.Top, Edge(RegistryPaths.MMStuckRects3, "MON1"));
        Assert.Equal(1, _registry.GetDword(Hive.CurrentUser, RegistryPaths.ExplorerPatcher, RegistryPaths.OldTaskbar));
        Assert.Equal(1, _registry.GetDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.StartShowClassicMode));
        Assert.Equal(1, _shell.Restarts);
        Assert.NotNull(_backups.Backup);
    }

    [Fact]
    public void ApplyRefusesUnsupportedPlan()
    {
        var controller = Controller();
        var plan = controller.Plan(new PositionRequest(TaskbarEdge.Left));
        Assert.Throws<InvalidOperationException>(() => controller.Apply(plan));
        Assert.Equal(TaskbarEdge.Bottom, Edge(RegistryPaths.StuckRects3, RegistryPaths.StuckRects3Value));
        Assert.Equal(0, _shell.Restarts);
    }

    [Fact]
    public void ApplyFailsClearlyWhenStuckRectsIsMissing()
    {
        _registry.DeleteValue(Hive.CurrentUser, RegistryPaths.StuckRects3, RegistryPaths.StuckRects3Value);
        var controller = Controller(19045);
        var plan = controller.Plan(new PositionRequest(TaskbarEdge.Top));
        var ex = Assert.Throws<InvalidOperationException>(() => controller.Apply(plan));
        Assert.Contains("StuckRects3", ex.Message);
        Assert.Null(_backups.Backup);
    }

    [Fact]
    public void NoRestartSkipsExplorerRestart()
    {
        var controller = Controller(19045);
        controller.Apply(controller.Plan(new PositionRequest(TaskbarEdge.Left)), restartExplorer: false);
        Assert.Equal(TaskbarEdge.Left, Edge(RegistryPaths.StuckRects3, RegistryPaths.StuckRects3Value));
        Assert.Equal(0, _shell.Restarts);
    }

    [Fact]
    public void BackupKeepsTheOriginalAcrossSeveralChanges()
    {
        _epFolder = true;
        var controller = Controller();
        controller.Apply(controller.Plan(new PositionRequest(TaskbarEdge.Top)));
        controller.Apply(controller.Plan(new PositionRequest(TaskbarEdge.Left)));

        Assert.Equal(TaskbarEdge.Bottom, StuckRects.ReadEdge(_backups.Backup!.StuckRects3));
        Assert.Null(_backups.Backup.OldTaskbar);
    }

    [Fact]
    public void RestorePutsEverythingBack()
    {
        _epFolder = true;
        _registry.SetDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.TaskbarAl, 1);
        var controller = Controller();
        controller.SetAlignment(TaskbarAlignment.Left);
        controller.Apply(controller.Plan(new PositionRequest(TaskbarEdge.Right, StartMenuStyle.Windows10)));

        Assert.True(controller.Restore());

        Assert.Equal(TaskbarEdge.Bottom, Edge(RegistryPaths.StuckRects3, RegistryPaths.StuckRects3Value));
        Assert.Equal(TaskbarEdge.Bottom, Edge(RegistryPaths.MMStuckRects3, "MON1"));
        Assert.Null(_registry.GetDword(Hive.CurrentUser, RegistryPaths.ExplorerPatcher, RegistryPaths.OldTaskbar));
        Assert.Null(_registry.GetDword(Hive.CurrentUser, RegistryPaths.ExplorerPatcher, RegistryPaths.OldTaskbarAl));
        Assert.Null(_registry.GetDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.StartShowClassicMode));
        Assert.Equal(1, _registry.GetDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.TaskbarAl));
        Assert.Null(_backups.Backup);
        Assert.Equal(2, _shell.Restarts);
    }

    [Fact]
    public void RestoreWithoutBackupReportsFalse()
    {
        Assert.False(Controller().Restore());
        Assert.Equal(0, _shell.Restarts);
    }

    [Fact]
    public void SetAlignmentWritesExplorerPatcherValueOnlyWhenInstalled()
    {
        Controller().SetAlignment(TaskbarAlignment.Left);
        Assert.Equal(0, _registry.GetDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.TaskbarAl));
        Assert.Null(_registry.GetDword(Hive.CurrentUser, RegistryPaths.ExplorerPatcher, RegistryPaths.OldTaskbarAl));

        _epFolder = true;
        Controller().SetAlignment(TaskbarAlignment.Center);
        Assert.Equal(1, _registry.GetDword(Hive.CurrentUser, RegistryPaths.ExplorerPatcher, RegistryPaths.OldTaskbarAl));
    }
}
