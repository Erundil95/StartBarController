using StartBarController.Core.Windhawk;
using static StartBarController.Core.Tests.Samples;

namespace StartBarController.Core.Tests;

public class PositionPlannerTests
{
    private static WindhawkModEdit EditFor(PositionPlan plan, string mod) => Assert.Single(plan.Changes.Windhawk, e => e.ModId == mod);

    [Fact]
    public void AutoUsesWindhawkOnWindows11AndNativeOnWindows10()
    {
        Assert.Equal(PositionEngine.Windhawk, PositionPlanner.Plan(State(Win11_23H2, windhawk: true), new(TaskbarEdge.Top)).Engine);
        Assert.Equal(PositionEngine.Windhawk, PositionPlanner.Plan(State(Win11_23H2), new(TaskbarEdge.Top)).Engine);
        Assert.Equal(PositionEngine.Native, PositionPlanner.Plan(State(Win10), new(TaskbarEdge.Top)).Engine);
    }

    [Fact]
    public void AutoKeepsExplorerPatcherWhenItIsActiveAndWindhawkIsMissing()
    {
        var plan = PositionPlanner.Plan(State(Win11_23H2, ep: true, epStyle: 1), new(TaskbarEdge.Left));
        Assert.Equal(PositionEngine.ExplorerPatcher, plan.Engine);
    }

    [Fact]
    public void WindhawkMissingBlocksWithNeedsWindhawk()
    {
        var plan = PositionPlanner.Plan(State(Win11_23H2), new(TaskbarEdge.Left));
        Assert.Equal(PlanBlocker.NeedsWindhawk, plan.Blocker);
        Assert.False(plan.CanApply);
    }

    [Fact]
    public void MissingModWithoutCliAsksForManualInstall()
    {
        var plan = PositionPlanner.Plan(State(Win11_23H2, windhawk: true), new(TaskbarEdge.Right));
        Assert.Equal(PlanBlocker.NeedsWindhawkMod, plan.Blocker);
        Assert.Equal(WindhawkMods.TaskbarVertical, plan.MissingMod);
    }

    [Fact]
    public void MissingModWithCliIsInstalledAutomatically()
    {
        var plan = PositionPlanner.Plan(State(Win11_23H2, windhawk: true, cli: true), new(TaskbarEdge.Right));
        Assert.True(plan.CanApply);
        Assert.Equal(new[] { WindhawkMods.TaskbarVertical }, plan.Changes.InstallMods);
        Assert.True(EditFor(plan, WindhawkMods.TaskbarVertical).Enabled);
    }

    [Fact]
    public void TopEnablesOnTopModAndDisablesVerticalMod()
    {
        var state = State(Win11_23H2, windhawk: true, mods: new[]
        {
            Mod(WindhawkMods.TaskbarOnTop, enabled: false),
            Mod(WindhawkMods.TaskbarVertical, enabled: true),
        });

        var plan = PositionPlanner.Plan(state, new(TaskbarEdge.Top, Windhawk: new WindhawkOptions { RunningIndicatorsOnTop = true }));

        var onTop = EditFor(plan, WindhawkMods.TaskbarOnTop);
        Assert.True(onTop.Enabled);
        Assert.Equal("top", onTop.Settings["taskbarLocation"]!.Text);
        Assert.Equal(1, onTop.Settings["runningIndicatorsOnTop"]!.Number);
        Assert.False(EditFor(plan, WindhawkMods.TaskbarVertical).Enabled);
        Assert.False(plan.Changes.RestartExplorer);
    }

    [Theory]
    [InlineData(TaskbarEdge.Left, "left")]
    [InlineData(TaskbarEdge.Right, "right")]
    public void VerticalEdgesConfigureTheVerticalMod(TaskbarEdge edge, string location)
    {
        var state = State(Win11_24H2, windhawk: true, mods: Mod(WindhawkMods.TaskbarVertical, enabled: false));
        var options = new WindhawkOptions { VerticalWidth = 120, StartMenuAlignment = "center", JumpListAlignment = "bogus" };

        var edit = EditFor(PositionPlanner.Plan(state, new(edge, Windhawk: options)), WindhawkMods.TaskbarVertical);

        Assert.Equal(location, edit.Settings["taskbarLocation"]!.Text);
        Assert.Equal(120, edit.Settings["TaskbarWidth"]!.Number);
        Assert.Equal("center", edit.Settings["startMenuAlignment"]!.Text);
        Assert.Equal("top", edit.Settings["jumpListAlignment"]!.Text); // invalid value normalized
        Assert.All(edit.Settings.Keys, k => Assert.Contains(k, WindhawkMods.ManagedSettings(WindhawkMods.TaskbarVertical)));
    }

    [Fact]
    public void BottomDisablesActiveModsOnly()
    {
        var state = State(Win11_23H2, windhawk: true, mods: new[]
        {
            Mod(WindhawkMods.TaskbarOnTop, enabled: true),
            Mod(WindhawkMods.TaskbarVertical, enabled: false),
        });

        var plan = PositionPlanner.Plan(state, new(TaskbarEdge.Bottom));

        var edit = Assert.Single(plan.Changes.Windhawk);
        Assert.Equal(WindhawkMods.TaskbarOnTop, edit.ModId);
        Assert.False(edit.Enabled);
        Assert.Empty(plan.Changes.InstallMods);
    }

    [Fact]
    public void WindhawkUndoesExplorerPatcherAndStuckRects()
    {
        var state = State(Win11_23H2, windhawk: true, ep: true, epStyle: 2, mods: Mod(WindhawkMods.TaskbarOnTop, enabled: false)) with
        {
            StuckRects3 = StuckRects.WithEdge(StuckRectsBottom(), TaskbarEdge.Left),
        };

        var plan = PositionPlanner.Plan(state, new(TaskbarEdge.Top, PositionEngine.Windhawk));

        Assert.Contains(plan.Changes.Registry, e => e.Name == RegistryPaths.OldTaskbar && e.Value!.Dword == 0);
        var stuck = Assert.Single(plan.Changes.Registry, e => e.Key == RegistryPaths.StuckRects3);
        Assert.Equal(TaskbarEdge.Bottom, StuckRects.ReadEdge(stuck.Value!.Binary));
        Assert.True(plan.Changes.RestartExplorer);
    }

    [Theory]
    [InlineData(Win11_23H2, 1)]
    [InlineData(Win11_24H2, 2)]
    public void ExplorerPatcherSwitchesToClassicTaskbar(int build, int style)
    {
        var plan = PositionPlanner.Plan(State(build, ep: true, epStyle: 0), new(TaskbarEdge.Left, PositionEngine.ExplorerPatcher));

        Assert.Equal(PositionEngine.ExplorerPatcher, plan.Engine);
        Assert.Contains(plan.Changes.Registry, e => e.Name == RegistryPaths.OldTaskbar && e.Value!.Dword == style);
        var stuck = Assert.Single(plan.Changes.Registry, e => e.Key == RegistryPaths.StuckRects3);
        Assert.Equal(TaskbarEdge.Left, StuckRects.ReadEdge(stuck.Value!.Binary));
        Assert.True(plan.Changes.RestartExplorer);
    }

    [Fact]
    public void ExplorerPatcherDisablesWindhawkModsAndSetsStartMenu()
    {
        var state = State(Win11_23H2, windhawk: true, ep: true, epStyle: 1, mods: Mod(WindhawkMods.TaskbarVertical));

        var plan = PositionPlanner.Plan(state, new(TaskbarEdge.Top, PositionEngine.ExplorerPatcher, StartMenuStyle.Windows10));

        Assert.False(Assert.Single(plan.Changes.Windhawk).Enabled);
        Assert.Contains(plan.Changes.Registry, e => e.Name == RegistryPaths.StartShowClassicMode && e.Value!.Dword == 1);
    }

    [Fact]
    public void ExplorerPatcherOn24H2KeepsWindows11StartMenu()
    {
        var plan = PositionPlanner.Plan(State(Win11_24H2, ep: true, epStyle: 2),
            new(TaskbarEdge.Top, PositionEngine.ExplorerPatcher, StartMenuStyle.Windows10));
        Assert.DoesNotContain(plan.Changes.Registry, e => e.Name == RegistryPaths.StartShowClassicMode);
        Assert.Contains(plan.Warnings, w => w.Contains("24H2"));
    }

    [Fact]
    public void ExplorerPatcherMissingBlocks() =>
        Assert.Equal(PlanBlocker.NeedsExplorerPatcher,
            PositionPlanner.Plan(State(Win11_23H2), new(TaskbarEdge.Left, PositionEngine.ExplorerPatcher)).Blocker);

    [Fact]
    public void Windows10WritesEveryStuckRectsBlob()
    {
        var state = State(Win10) with { MMStuckRects3 = new Dictionary<string, byte[]> { ["MON2"] = StuckRectsBottom() } };
        var plan = PositionPlanner.Plan(state, new(TaskbarEdge.Right, PositionEngine.Windhawk));

        Assert.Equal(PositionEngine.Native, plan.Engine);
        Assert.Equal(2, plan.Changes.Registry.Count);
        Assert.All(plan.Changes.Registry, e => Assert.Equal(TaskbarEdge.Right, StuckRects.ReadEdge(e.Value!.Binary)));
    }

    [Fact]
    public void NativeOn21H2TopUsesStuckRectsWithWarning()
    {
        var plan = PositionPlanner.Plan(State(Win11_21H2), new(TaskbarEdge.Top, PositionEngine.Native));
        Assert.Contains(plan.Changes.Registry, e => e.Key == RegistryPaths.StuckRects3);
        Assert.Contains(plan.Warnings, w => w.Contains("start menu"));
    }

    [Fact]
    public void NativeOnNewBuildsWritesTaskbarLocation()
    {
        var plan = PositionPlanner.Plan(State(Win11_24H2), new(TaskbarEdge.Left, PositionEngine.Native));
        var edit = Assert.Single(plan.Changes.Registry);
        Assert.Equal(RegistryPaths.TaskbarLocation, edit.Name);
        Assert.Equal((int)TaskbarEdge.Left, edit.Value!.Dword);
        Assert.NotEmpty(plan.Warnings);
    }

    [Fact]
    public void MissingStuckRectsBlocksRegistryEngines()
    {
        var state = State(Win10) with { StuckRects3 = null };
        Assert.Equal(PlanBlocker.Unsupported, PositionPlanner.Plan(state, new(TaskbarEdge.Top)).Blocker);
    }

    [Fact]
    public void CurrentEdgeFollowsTheActiveEngine()
    {
        Assert.Equal(TaskbarEdge.Right, State(Win11_23H2, windhawk: true,
            mods: Mod(WindhawkMods.TaskbarVertical, settings: ("taskbarLocation", "right"))).CurrentEdge);
        Assert.Equal(TaskbarEdge.Top, State(Win11_23H2, windhawk: true, mods: Mod(WindhawkMods.TaskbarOnTop)).CurrentEdge);

        var classic = State(Win11_23H2, ep: true, epStyle: 1) with { StuckRects3 = StuckRects.WithEdge(StuckRectsBottom(), TaskbarEdge.Left) };
        Assert.Equal(TaskbarEdge.Left, classic.CurrentEdge);
        Assert.Equal(PositionEngine.ExplorerPatcher, classic.ActiveEngine);

        // Windows 11 22H2+ ignores StuckRects3 without a helper engine.
        var ignored = State(Win11_23H2) with { StuckRects3 = StuckRects.WithEdge(StuckRectsBottom(), TaskbarEdge.Left) };
        Assert.Equal(TaskbarEdge.Bottom, ignored.CurrentEdge);
    }
}
