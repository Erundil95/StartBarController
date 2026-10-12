using StartBarController.Core.Windhawk;

namespace StartBarController.Core;

public enum PlanBlocker
{
    None,

    /// <summary>Windhawk is not installed.</summary>
    NeedsWindhawk,

    /// <summary>A Windhawk mod must be installed from the Windhawk app (no windhawk-cli.exe to do it for us).</summary>
    NeedsWindhawkMod,

    NeedsExplorerPatcher,

    Unsupported,
}

public sealed record PositionRequest(
    TaskbarEdge Edge,
    PositionEngine Engine = PositionEngine.Auto,
    StartMenuStyle ClassicStartMenu = StartMenuStyle.Keep,
    WindhawkOptions? Windhawk = null);

public sealed record PositionPlan
{
    public required TaskbarEdge Edge { get; init; }

    /// <summary>The engine the plan uses (never Auto).</summary>
    public required PositionEngine Engine { get; init; }

    public required string Summary { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public ChangeSet Changes { get; init; } = ChangeSet.Empty;

    public PlanBlocker Blocker { get; init; }

    /// <summary>With <see cref="PlanBlocker.NeedsWindhawkMod"/>: the mod to install.</summary>
    public string? MissingMod { get; init; }

    public bool CanApply => Blocker == PlanBlocker.None;
}

/// <summary>
/// Decides how a taskbar edge is applied with each engine, including undoing what
/// the other engines may have set, so switching engines never leaves two of them
/// fighting over the taskbar.
/// </summary>
public static class PositionPlanner
{
    public static PositionPlan Plan(SystemState state, PositionRequest request)
    {
        PositionEngine engine = state.ResolveEngine(request.Engine);
        if (!state.IsWindows11 && engine != PositionEngine.Native)
        {
            // Windows 10's own taskbar already supports every edge.
            engine = PositionEngine.Native;
        }

        return engine switch
        {
            PositionEngine.Windhawk => PlanWindhawk(state, request),
            PositionEngine.ExplorerPatcher => PlanExplorerPatcher(state, request),
            _ => PlanNative(state, request),
        };
    }

    private static PositionPlan PlanWindhawk(SystemState state, PositionRequest request)
    {
        TaskbarEdge edge = request.Edge;
        if (state.Windhawk is null)
        {
            return Blocked(edge, PositionEngine.Windhawk, PlanBlocker.NeedsWindhawk,
                "Windhawk isn't installed. It's a free, open-source customization platform; Start Bar Controller " +
                "uses two of its mods to move the real Windows 11 taskbar and start menu.");
        }

        if (state.WindhawkError is not null)
        {
            return Blocked(edge, PositionEngine.Windhawk, PlanBlocker.Unsupported,
                $"Windhawk's settings could not be read: {state.WindhawkError}");
        }

        string? target = WindhawkMods.ForEdge(edge);
        var installs = new List<string>();
        if (target is not null && !state.Mod(target).Installed)
        {
            if (state.Windhawk.CliPath is null)
            {
                return Blocked(edge, PositionEngine.Windhawk, PlanBlocker.NeedsWindhawkMod,
                    $"Install the \"{WindhawkMods.DisplayName(target)}\" mod in Windhawk first " +
                    "(Explore → search for it → Install), then try again.") with { MissingMod = target };
            }

            installs.Add(target);
        }

        var edits = DisableOtherMods(state, target);
        if (target is not null)
        {
            edits.Add(new WindhawkModEdit
            {
                ModId = target,
                Enabled = true,
                Settings = ModSettings(target, edge, (request.Windhawk ?? new WindhawkOptions()).Normalized()),
            });
        }

        // The mods expect Windows' own position at the bottom and the Windows 11 taskbar.
        var registry = new List<RegistryEdit>();
        var warnings = new List<string>();
        if (state.ClassicTaskbarActive)
        {
            registry.Add(new RegistryEdit(RegistryPaths.ExplorerPatcher, RegistryPaths.OldTaskbar, RegistryData.FromDword(0)));
            warnings.Add("ExplorerPatcher's Windows 10 taskbar is switched off so the Windows 11 taskbar comes back.");
        }

        registry.AddRange(StuckRectsEdits(state, TaskbarEdge.Bottom));
        if (state.NativeTaskbarLocation is int native && native != (int)TaskbarEdge.Bottom)
        {
            registry.Add(new RegistryEdit(RegistryPaths.Advanced, RegistryPaths.TaskbarLocation, RegistryData.FromDword((int)TaskbarEdge.Bottom)));
        }

        if (edge.IsVertical())
        {
            warnings.Add("Auto-hide isn't supported with the vertical taskbar mod.");
        }

        if (installs.Count > 0)
        {
            warnings.Add($"Windhawk will download and install the \"{WindhawkMods.DisplayName(target!)}\" mod first.");
        }

        string summary = target is null
            ? "The Windows 11 taskbar goes back to the bottom."
            : $"The Windows 11 taskbar and start menu move to the {edge.Lower()} using the Windhawk mod " +
              $"\"{WindhawkMods.DisplayName(target)}\". It applies within seconds, without restarting Explorer.";

        return new PositionPlan
        {
            Edge = edge,
            Engine = PositionEngine.Windhawk,
            Summary = summary,
            Warnings = warnings,
            Changes = new ChangeSet { Registry = registry, Windhawk = edits, InstallMods = installs },
        };
    }

    internal static Dictionary<string, ModSettingValue?> ModSettings(string modId, TaskbarEdge edge, WindhawkOptions options)
    {
        var settings = new Dictionary<string, ModSettingValue?>(StringComparer.OrdinalIgnoreCase)
        {
            ["taskbarLocationSecondary"] = ModSettingValue.Of("sameAsPrimary"),
            ["startMenuAnimationAdjust"] = ModSettingValue.Of(options.AdjustStartMenuAnimation),
            ["useNativeTaskbar"] = ModSettingValue.Of(options.UseNativeTaskbar),
        };

        if (modId == WindhawkMods.TaskbarVertical)
        {
            settings["taskbarLocation"] = ModSettingValue.Of(edge == TaskbarEdge.Right ? "right" : "left");
            settings["TaskbarWidth"] = ModSettingValue.Of(options.VerticalWidth);
            settings["jumpListAlignment"] = ModSettingValue.Of(options.JumpListAlignment);
            settings["startMenuAlignment"] = ModSettingValue.Of(options.StartMenuAlignment);
        }
        else
        {
            settings["taskbarLocation"] = ModSettingValue.Of(edge == TaskbarEdge.Bottom ? "bottom" : "top");
            settings["runningIndicatorsOnTop"] = ModSettingValue.Of(options.RunningIndicatorsOnTop);
        }

        return settings;
    }

    private static PositionPlan PlanExplorerPatcher(SystemState state, PositionRequest request)
    {
        TaskbarEdge edge = request.Edge;
        if (!state.ExplorerPatcherInstalled)
        {
            return Blocked(edge, PositionEngine.ExplorerPatcher, PlanBlocker.NeedsExplorerPatcher,
                "ExplorerPatcher isn't installed.");
        }

        if (state.StuckRects3 is not { Length: >= StuckRects.MinimumLength })
        {
            return MissingStuckRects(edge, PositionEngine.ExplorerPatcher);
        }

        var registry = new List<RegistryEdit>();
        var warnings = new List<string>();
        if (!state.ClassicTaskbarActive)
        {
            // Style 1 is the Windows 10 taskbar still in Explorer; 24H2 removed it, leaving only EP's own port (2).
            int style = state.WindowsBuild >= WindowsRelease.Windows11_24H2 ? 2 : 1;
            registry.Add(new RegistryEdit(RegistryPaths.ExplorerPatcher, RegistryPaths.OldTaskbar, RegistryData.FromDword(style)));
            warnings.Add("The Windows 11 taskbar is replaced by ExplorerPatcher's Windows 10-style taskbar.");
        }

        registry.AddRange(StuckRectsEdits(state, edge, force: true));

        bool hasWindows10StartMenu = state.WindowsBuild < WindowsRelease.Windows11_24H2;
        int? classic = request.ClassicStartMenu switch
        {
            StartMenuStyle.Windows10 when hasWindows10StartMenu => 1,
            StartMenuStyle.Windows11 => 0,
            _ => null,
        };
        if (request.ClassicStartMenu == StartMenuStyle.Windows10 && !hasWindows10StartMenu)
        {
            warnings.Add("The Windows 10 start menu was removed in Windows 11 24H2; the Windows 11 start menu stays.");
        }

        if (classic is int c && c != state.ClassicStartMenu)
        {
            registry.Add(new RegistryEdit(RegistryPaths.Advanced, RegistryPaths.StartShowClassicMode, RegistryData.FromDword(c)));
        }

        return new PositionPlan
        {
            Edge = edge,
            Engine = PositionEngine.ExplorerPatcher,
            Summary = $"ExplorerPatcher's Windows 10-style taskbar docks to the {edge.Lower()}. Explorer restarts to apply it.",
            Warnings = warnings,
            Changes = new ChangeSet { Registry = registry, Windhawk = DisableOtherMods(state, null) },
        };
    }

    private static PositionPlan PlanNative(SystemState state, PositionRequest request)
    {
        TaskbarEdge edge = request.Edge;
        int build = state.WindowsBuild;
        var warnings = new List<string>();
        var registry = new List<RegistryEdit>();

        if (!state.IsWindows11)
        {
            if (state.StuckRects3 is not { Length: >= StuckRects.MinimumLength })
            {
                return MissingStuckRects(edge, PositionEngine.Native);
            }

            return new PositionPlan
            {
                Edge = edge,
                Engine = PositionEngine.Native,
                Summary = $"Windows 10 docks the taskbar to the {edge.Lower()} natively; the start menu follows it. Explorer restarts to apply it.",
                Changes = new ChangeSet { Registry = StuckRectsEdits(state, edge, force: true) },
            };
        }

        if (state.ClassicTaskbarActive)
        {
            registry.Add(new RegistryEdit(RegistryPaths.ExplorerPatcher, RegistryPaths.OldTaskbar, RegistryData.FromDword(0)));
        }

        string summary;
        if (build < WindowsRelease.Windows11_22H2 && edge is TaskbarEdge.Top or TaskbarEdge.Bottom)
        {
            registry.AddRange(StuckRectsEdits(state, edge));
            summary = $"Windows 11 21H2 docks its taskbar to the {edge.Lower()} through the registry. Explorer restarts to apply it.";
            if (edge == TaskbarEdge.Top)
            {
                warnings.Add("Partial support: the start menu and system flyouts still open at the bottom.");
            }
        }
        else
        {
            registry.AddRange(StuckRectsEdits(state, TaskbarEdge.Bottom));
            if (edge != TaskbarEdge.Bottom || state.NativeTaskbarLocation is not null)
            {
                registry.Add(new RegistryEdit(RegistryPaths.Advanced, RegistryPaths.TaskbarLocation, RegistryData.FromDword((int)edge)));
            }

            summary = edge == TaskbarEdge.Bottom
                ? "The Windows 11 taskbar goes back to the bottom."
                : $"Windows' own taskbar position setting is set to {edge.Lower()}. Explorer restarts to apply it.";
            if (edge != TaskbarEdge.Bottom)
            {
                warnings.Add("Only Windows 11 preview builds with the new taskbar position feature honour this; " +
                             "on other builds the taskbar stays at the bottom. Use the Windhawk engine there.");
            }
        }

        return new PositionPlan
        {
            Edge = edge,
            Engine = PositionEngine.Native,
            Summary = summary,
            Warnings = warnings,
            Changes = new ChangeSet { Registry = registry, Windhawk = DisableOtherMods(state, null) },
        };
    }

    /// <summary>Disables the position mods that are active, except <paramref name="keep"/>.</summary>
    private static List<WindhawkModEdit> DisableOtherMods(SystemState state, string? keep) =>
        WindhawkMods.All
            .Where(id => id != keep && state.IsModActive(id))
            .Select(id => new WindhawkModEdit { ModId = id, Enabled = false })
            .ToList();

    /// <summary>
    /// Edits that put <paramref name="edge"/> into StuckRects3 and every MMStuckRects3 blob.
    /// Without <paramref name="force"/>, blobs already on that edge are left alone.
    /// </summary>
    private static List<RegistryEdit> StuckRectsEdits(SystemState state, TaskbarEdge edge, bool force = false)
    {
        var edits = new List<RegistryEdit>();
        if (state.StuckRects3 is { Length: >= StuckRects.MinimumLength } primary &&
            (force || StuckRects.ReadEdge(primary) != edge))
        {
            edits.Add(new RegistryEdit(RegistryPaths.StuckRects3, RegistryPaths.StuckRects3Value,
                RegistryData.FromBinary(StuckRects.WithEdge(primary, edge))));
        }

        foreach ((string name, byte[] blob) in state.MMStuckRects3)
        {
            if (blob.Length >= StuckRects.MinimumLength && (force || StuckRects.ReadEdge(blob) != edge))
            {
                edits.Add(new RegistryEdit(RegistryPaths.MMStuckRects3, name, RegistryData.FromBinary(StuckRects.WithEdge(blob, edge))));
            }
        }

        return edits;
    }

    private static PositionPlan MissingStuckRects(TaskbarEdge edge, PositionEngine engine) => Blocked(edge, engine,
        PlanBlocker.Unsupported,
        "The taskbar's StuckRects3 settings were not found in the registry. Sign out and back in, then try again.");

    private static PositionPlan Blocked(TaskbarEdge edge, PositionEngine engine, PlanBlocker blocker, string summary) => new()
    {
        Edge = edge,
        Engine = engine,
        Blocker = blocker,
        Summary = summary,
    };
}
