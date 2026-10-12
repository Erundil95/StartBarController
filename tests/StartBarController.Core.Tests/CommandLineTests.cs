namespace StartBarController.Core.Tests;

public class CommandLineTests
{
    [Fact]
    public void NoArgumentsOpensGui() => Assert.Equal(CliAction.Gui, CommandLine.Parse(Array.Empty<string>()).Action);

    [Fact]
    public void TrayStartsGuiHidden()
    {
        var o = CommandLine.Parse(new[] { "--tray" });
        Assert.Equal(CliAction.Gui, o.Action);
        Assert.True(o.StartInTray);
    }

    [Fact]
    public void ParsesEdgeEngineSettingsAndAutohide()
    {
        var o = CommandLine.Parse(new[] { "--edge", "LEFT", "--engine", "windhawk", "--set", "search=0", "--set", "alignment=1", "--autohide", "off", "--no-restart" });
        Assert.Equal(CliAction.Apply, o.Action);
        Assert.Equal(TaskbarEdge.Left, o.Edge);
        Assert.Equal(PositionEngine.Windhawk, o.Engine);
        Assert.Equal(2, o.Settings.Count);
        Assert.Equal(("search", 0), (o.Settings[0].Setting.Id, o.Settings[0].Value));
        Assert.False(o.AutoHide);
        Assert.True(o.NoRestart);
        Assert.Null(o.Error);
    }

    [Theory]
    [InlineData("--status", CliAction.Status)]
    [InlineData("--restore", CliAction.Restore)]
    [InlineData("--list-settings", CliAction.ListSettings)]
    [InlineData("--help", CliAction.Help)]
    [InlineData("--uninstall", CliAction.Uninstall)]
    public void ParsesSimpleActions(string arg, CliAction expected) =>
        Assert.Equal(expected, CommandLine.Parse(new[] { arg }).Action);

    [Fact]
    public void ParsesElevatedHelper()
    {
        var o = CommandLine.Parse(new[] { "--windhawk-elevated", @"C:\t\r.json" });
        Assert.Equal(CliAction.WindhawkElevated, o.Action);
        Assert.Equal(@"C:\t\r.json", o.RequestFile);
    }

    [Theory]
    [InlineData("--edge")]
    [InlineData("--edge", "middle")]
    [InlineData("--engine", "windhawk")]
    [InlineData("--set", "search")]
    [InlineData("--set", "search=7")]
    [InlineData("--set", "nope=1")]
    [InlineData("--bogus")]
    [InlineData("--no-restart")]
    [InlineData("--restore", "--edge", "top")]
    [InlineData("--status", "--tray")]
    public void RejectsInvalidInput(params string[] args)
    {
        var o = CommandLine.Parse(args);
        Assert.Equal(CliAction.Help, o.Action);
        Assert.NotNull(o.Error);
    }

    [Fact]
    public void RunnerReportsBlockedEdgeWithoutChangingAnything()
    {
        var registry = new FakeRegistry();
        registry.SetBinary(Hive.CurrentUser, RegistryPaths.StuckRects3, RegistryPaths.StuckRects3Value, Samples.StuckRectsBottom());
        var shell = new FakeShell();
        var controller = new TaskbarController(
            new SystemInspector(registry, _ => false, Array.Empty<string>(), () => null,
                s => new Windhawk.WindhawkModStore(s, registry, new FakeIni()), Samples.Win11_23H2),
            registry, shell, new JsonFile<TaskbarBackup>(Path.Combine(Path.GetTempPath(), $"sbc-{Guid.NewGuid():N}.json")),
            new DirectWindhawkExecutor(s => new Windhawk.WindhawkModStore(s, registry, new FakeIni()), new FakeProcessRunner()));
        var output = new StringWriter();

        int code = CliRunner.Run(CommandLine.Parse(new[] { "--edge", "top", "--autohide", "on" }), controller, new AppSettings(), output);

        Assert.Equal(1, code);
        Assert.Contains("Windhawk", output.ToString());
        Assert.False(shell.AutoHide);
    }
}
