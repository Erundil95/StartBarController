namespace StartBarController.Core.Tests;

public class CommandLineTests
{
    [Fact]
    public void NoArgumentsOpensGui() => Assert.Equal(CliAction.Gui, CommandLine.Parse(Array.Empty<string>()).Action);

    [Fact]
    public void ParsesFullEdgeCommand()
    {
        var o = CommandLine.Parse(new[] { "--edge", "LEFT", "--start-menu", "win10", "--no-restart", "--force-native" });
        Assert.Equal(CliAction.Apply, o.Action);
        Assert.Equal(TaskbarEdge.Left, o.Edge);
        Assert.Equal(StartMenuStyle.Windows10, o.StartMenu);
        Assert.True(o.NoRestart);
        Assert.True(o.ForceNative);
        Assert.Null(o.Error);
    }

    [Fact]
    public void ParsesAlignAndAutohide()
    {
        var o = CommandLine.Parse(new[] { "--align", "center", "--autohide", "on" });
        Assert.Equal(TaskbarAlignment.Center, o.Alignment);
        Assert.True(o.AutoHide);
    }

    [Theory]
    [InlineData("--status", CliAction.Status)]
    [InlineData("--restore", CliAction.Restore)]
    [InlineData("--help", CliAction.Help)]
    [InlineData("/?", CliAction.Help)]
    public void ParsesSimpleActions(string arg, CliAction expected) =>
        Assert.Equal(expected, CommandLine.Parse(new[] { arg }).Action);

    [Theory]
    [InlineData("--edge")]
    [InlineData("--edge", "middle")]
    [InlineData("--bogus")]
    [InlineData("--no-restart")]
    [InlineData("--restore", "--edge", "top")]
    public void RejectsInvalidInput(params string[] args)
    {
        var o = CommandLine.Parse(args);
        Assert.Equal(CliAction.Help, o.Action);
        Assert.NotNull(o.Error);
    }

    [Fact]
    public void RunnerReportsUnsupportedEdgeWithoutChangingAnything()
    {
        var registry = new FakeRegistry();
        registry.SetBinary(Hive.CurrentUser, RegistryPaths.StuckRects3, RegistryPaths.StuckRects3Value, Samples.StuckRectsBottom());
        var shell = new FakeShell();
        var controller = new TaskbarController(registry, shell, new MemoryBackupStore(),
            new SystemInspector(registry, _ => false, Array.Empty<string>()), 22631);
        var output = new StringWriter();

        int code = CliRunner.Run(CommandLine.Parse(new[] { "--edge", "top", "--autohide", "on" }), controller, output);

        Assert.Equal(1, code);
        Assert.Contains("ExplorerPatcher", output.ToString());
        Assert.False(shell.AutoHide);
        Assert.Null(registry.GetDword(Hive.CurrentUser, RegistryPaths.Advanced, RegistryPaths.TaskbarAl));
    }

    [Fact]
    public void JsonBackupStoreRoundTrips()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sbc-{Guid.NewGuid():N}", "backup.json");
        var store = new JsonFileBackupStore(path);
        Assert.Null(store.Load());

        store.Save(new TaskbarBackup
        {
            WindowsBuild = 22631,
            StuckRects3 = Samples.StuckRectsBottom(),
            MMStuckRects3 = { ["MON1"] = new byte[] { 1, 2, 3 } },
            TaskbarAl = 0,
        });
        var loaded = store.Load()!;

        Assert.Equal(Samples.StuckRectsBottom(), loaded.StuckRects3);
        Assert.Equal(new byte[] { 1, 2, 3 }, loaded.MMStuckRects3["MON1"]);
        Assert.Equal(0, loaded.TaskbarAl);
        Assert.Null(loaded.OldTaskbar);

        store.Delete();
        Assert.Null(store.Load());
        Directory.Delete(Path.GetDirectoryName(path)!);
    }
}
