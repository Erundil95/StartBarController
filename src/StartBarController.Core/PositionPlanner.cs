namespace StartBarController.Core;

public enum PositionMethod
{
    /// <summary>The requested edge cannot be applied on this system as configured.</summary>
    None,

    /// <summary>Write the edge into StuckRects3 and restart Explorer.</summary>
    NativeRegistry,

    /// <summary>Use ExplorerPatcher's Windows 10-style taskbar, which supports every edge.</summary>
    ExplorerPatcher,
}

public sealed record PositionRequest(
    TaskbarEdge Edge,
    StartMenuStyle StartMenu = StartMenuStyle.Keep,
    bool ForceNative = false);

public sealed record PositionPlan
{
    public required TaskbarEdge Edge { get; init; }

    public required PositionMethod Method { get; init; }

    public required string Summary { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>New value for ExplorerPatcher's OldTaskbar, or null to leave it alone.</summary>
    public int? SetExplorerPatcherTaskbarStyle { get; init; }

    /// <summary>New value for Start_ShowClassicMode, or null to leave it alone.</summary>
    public int? SetClassicStartMenu { get; init; }

    /// <summary>True when installing ExplorerPatcher would make the request possible.</summary>
    public bool NeedsExplorerPatcher { get; init; }

    public bool CanApply => Method != PositionMethod.None;
}

/// <summary>
/// Decides how (and whether) a taskbar edge can be applied on a given system.
///
/// Background: Windows 10 supports every edge through StuckRects3. Windows 11's
/// rewritten taskbar honours "top" only on 21H2 (with the start menu and flyouts
/// still opening at the bottom) and ignores anything but "bottom" from 22H2 on.
/// ExplorerPatcher restores the Windows 10 taskbar, which reads the same
/// StuckRects3 edge and supports all four sides.
/// </summary>
public static class PositionPlanner
{
    public static PositionPlan Plan(SystemSnapshot system, PositionRequest request)
    {
        int build = system.WindowsBuild;
        TaskbarEdge edge = request.Edge;

        if (!WindowsRelease.IsWindows11(build))
        {
            return new PositionPlan
            {
                Edge = edge,
                Method = PositionMethod.NativeRegistry,
                Summary = $"Windows 10 supports the {Lower(edge)} edge natively; the start menu follows the taskbar.",
            };
        }

        if (system.ClassicTaskbarActive)
        {
            return WithStartMenu(system, request, new PositionPlan
            {
                Edge = edge,
                Method = PositionMethod.ExplorerPatcher,
                Summary = $"ExplorerPatcher's Windows 10-style taskbar is active and will move to the {Lower(edge)} edge.",
            });
        }

        if (edge == TaskbarEdge.Bottom)
        {
            return new PositionPlan
            {
                Edge = edge,
                Method = PositionMethod.NativeRegistry,
                Summary = "Bottom is the default Windows 11 position.",
            };
        }

        if (request.ForceNative)
        {
            return new PositionPlan
            {
                Edge = edge,
                Method = PositionMethod.NativeRegistry,
                Summary = $"Forcing the native registry method for the {Lower(edge)} edge.",
                Warnings = new[]
                {
                    $"{WindowsRelease.Describe(build)} does not officially support this. Explorer may ignore it, " +
                    "reset it, or draw a broken taskbar. Use \"Restore original\" to undo.",
                },
            };
        }

        if (system.ExplorerPatcherInstalled)
        {
            // Style 1 uses the Windows 10 taskbar code still shipped in Explorer; 24H2 removed
            // that code, so there only ExplorerPatcher's own port (style 2) works.
            int style = build >= WindowsRelease.Windows11_24H2 ? 2 : 1;
            return WithStartMenu(system, request, new PositionPlan
            {
                Edge = edge,
                Method = PositionMethod.ExplorerPatcher,
                SetExplorerPatcherTaskbarStyle = style,
                Summary = $"ExplorerPatcher will switch to its Windows 10-style taskbar and dock it to the {Lower(edge)} edge.",
                Warnings = new[]
                {
                    "The Windows 11 taskbar is replaced by ExplorerPatcher's Windows 10-style taskbar. " +
                    "Use \"Restore original\" to go back to the Windows 11 taskbar.",
                },
            });
        }

        if (build < WindowsRelease.Windows11_22H2 && edge == TaskbarEdge.Top)
        {
            return new PositionPlan
            {
                Edge = edge,
                Method = PositionMethod.NativeRegistry,
                Summary = "Windows 11 21H2 can dock its taskbar to the top through the registry.",
                Warnings = new[]
                {
                    "Partial support: the start menu and system flyouts still open at the bottom of the screen. " +
                    "Install ExplorerPatcher for a start menu that follows the taskbar.",
                },
            };
        }

        return new PositionPlan
        {
            Edge = edge,
            Method = PositionMethod.None,
            NeedsExplorerPatcher = true,
            Summary = (build < WindowsRelease.Windows11_22H2
                          ? $"{WindowsRelease.Describe(build)} only supports the bottom and (partially) the top edge. "
                          : $"{WindowsRelease.Describe(build)} only allows the taskbar at the bottom. ") +
                      $"Install ExplorerPatcher (free, open source) to move it to the {Lower(edge)} edge.",
        };
    }

    private static PositionPlan WithStartMenu(SystemSnapshot system, PositionRequest request, PositionPlan plan)
    {
        var warnings = plan.Warnings.ToList();
        int? classic = null;
        bool hasWindows10StartMenu = system.WindowsBuild < WindowsRelease.Windows11_24H2;

        switch (request.StartMenu)
        {
            case StartMenuStyle.Windows10 when hasWindows10StartMenu:
                classic = 1;
                break;
            case StartMenuStyle.Windows10:
                warnings.Add("The Windows 10 start menu was removed in Windows 11 24H2; the Windows 11 start menu stays.");
                break;
            case StartMenuStyle.Windows11:
                classic = 0;
                break;
            case StartMenuStyle.Keep when plan.Edge != TaskbarEdge.Bottom && hasWindows10StartMenu && system.ClassicStartMenu != 1:
                warnings.Add("Tip: pick the Windows 10 start menu style so the start menu opens next to the taskbar on every edge.");
                break;
        }

        if (classic is not null && classic == system.ClassicStartMenu)
        {
            classic = null;
        }

        return plan with { SetClassicStartMenu = classic, Warnings = warnings };
    }

    private static string Lower(TaskbarEdge edge) => edge.ToString().ToLowerInvariant();
}
