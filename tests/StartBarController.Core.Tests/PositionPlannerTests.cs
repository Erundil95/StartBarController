namespace StartBarController.Core.Tests;

public class PositionPlannerTests
{
    private const int Win10 = 19045;
    private const int Win11_21H2 = 22000;
    private const int Win11_23H2 = 22631;
    private const int Win11_24H2 = 26100;

    [Theory]
    [InlineData(TaskbarEdge.Left)]
    [InlineData(TaskbarEdge.Top)]
    [InlineData(TaskbarEdge.Right)]
    public void Windows10SupportsEveryEdgeNatively(TaskbarEdge edge)
    {
        var plan = PositionPlanner.Plan(Samples.System(Win10), new PositionRequest(edge));
        Assert.Equal(PositionMethod.NativeRegistry, plan.Method);
        Assert.Null(plan.SetExplorerPatcherTaskbarStyle);
    }

    [Theory]
    [InlineData(TaskbarEdge.Left)]
    [InlineData(TaskbarEdge.Top)]
    [InlineData(TaskbarEdge.Right)]
    public void Windows11WithoutExplorerPatcherBlocksNonBottomEdges(TaskbarEdge edge)
    {
        var plan = PositionPlanner.Plan(Samples.System(Win11_23H2), new PositionRequest(edge));
        Assert.False(plan.CanApply);
        Assert.True(plan.NeedsExplorerPatcher);
    }

    [Fact]
    public void Windows11BottomIsAlwaysNative()
    {
        var plan = PositionPlanner.Plan(Samples.System(Win11_24H2), new PositionRequest(TaskbarEdge.Bottom));
        Assert.Equal(PositionMethod.NativeRegistry, plan.Method);
    }

    [Fact]
    public void Windows11_21H2TopIsNativeWithWarning()
    {
        var plan = PositionPlanner.Plan(Samples.System(Win11_21H2), new PositionRequest(TaskbarEdge.Top));
        Assert.Equal(PositionMethod.NativeRegistry, plan.Method);
        Assert.Contains(plan.Warnings, w => w.Contains("start menu", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Windows11_21H2LeftStillNeedsExplorerPatcher()
    {
        var plan = PositionPlanner.Plan(Samples.System(Win11_21H2), new PositionRequest(TaskbarEdge.Left));
        Assert.True(plan.NeedsExplorerPatcher);
    }

    [Fact]
    public void ForceNativeOverridesTheBlockWithAWarning()
    {
        var plan = PositionPlanner.Plan(Samples.System(Win11_23H2), new PositionRequest(TaskbarEdge.Left, ForceNative: true));
        Assert.Equal(PositionMethod.NativeRegistry, plan.Method);
        Assert.NotEmpty(plan.Warnings);
    }

    [Theory]
    [InlineData(Win11_21H2, 1)]
    [InlineData(Win11_23H2, 1)]
    [InlineData(Win11_24H2, 2)]
    public void InstalledExplorerPatcherSwitchesToClassicTaskbar(int build, int expectedStyle)
    {
        var plan = PositionPlanner.Plan(Samples.System(build, ep: true, epStyle: 0), new PositionRequest(TaskbarEdge.Left));
        Assert.Equal(PositionMethod.ExplorerPatcher, plan.Method);
        Assert.Equal(expectedStyle, plan.SetExplorerPatcherTaskbarStyle);
    }

    [Fact]
    public void ActiveClassicTaskbarIsNotReconfigured()
    {
        var plan = PositionPlanner.Plan(Samples.System(Win11_23H2, ep: true, epStyle: 2), new PositionRequest(TaskbarEdge.Right));
        Assert.Equal(PositionMethod.ExplorerPatcher, plan.Method);
        Assert.Null(plan.SetExplorerPatcherTaskbarStyle);
    }

    [Fact]
    public void Windows10StartMenuIsRequestedThroughExplorerPatcher()
    {
        var plan = PositionPlanner.Plan(Samples.System(Win11_23H2, ep: true, epStyle: 1),
            new PositionRequest(TaskbarEdge.Top, StartMenuStyle.Windows10));
        Assert.Equal(1, plan.SetClassicStartMenu);
    }

    [Fact]
    public void Windows10StartMenuIsNotWrittenOn24H2()
    {
        var plan = PositionPlanner.Plan(Samples.System(Win11_24H2, ep: true, epStyle: 2),
            new PositionRequest(TaskbarEdge.Top, StartMenuStyle.Windows10));
        Assert.Null(plan.SetClassicStartMenu);
        Assert.Contains(plan.Warnings, w => w.Contains("24H2"));
    }

    [Fact]
    public void UnchangedStartMenuStyleIsNotRewritten()
    {
        var plan = PositionPlanner.Plan(Samples.System(Win11_23H2, ep: true, epStyle: 1, classicStart: 1),
            new PositionRequest(TaskbarEdge.Top, StartMenuStyle.Windows10));
        Assert.Null(plan.SetClassicStartMenu);
    }

    [Fact]
    public void KeepSuggestsWindows10StartMenuForSideEdges()
    {
        var plan = PositionPlanner.Plan(Samples.System(Win11_23H2, ep: true, epStyle: 1),
            new PositionRequest(TaskbarEdge.Left));
        Assert.Contains(plan.Warnings, w => w.StartsWith("Tip:", StringComparison.Ordinal));
    }

    [Fact]
    public void StartMenuIsIgnoredOnNativeMethod()
    {
        var plan = PositionPlanner.Plan(Samples.System(Win10), new PositionRequest(TaskbarEdge.Top, StartMenuStyle.Windows10));
        Assert.Null(plan.SetClassicStartMenu);
    }
}
