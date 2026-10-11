namespace StartBarController.Core;

public enum CliAction
{
    Gui,
    Help,
    Status,
    Apply,
    Restore,
}

public sealed record CliOptions
{
    public CliAction Action { get; init; } = CliAction.Gui;

    public TaskbarEdge? Edge { get; init; }

    public StartMenuStyle StartMenu { get; init; } = StartMenuStyle.Keep;

    public TaskbarAlignment? Alignment { get; init; }

    public bool? AutoHide { get; init; }

    public bool ForceNative { get; init; }

    public bool NoRestart { get; init; }

    public string? Error { get; init; }
}

public static class CommandLine
{
    public const string Usage = """
        StartBarController - customize the Windows taskbar.

        Run without arguments to open the window. Command-line use:

          StartBarController --status
          StartBarController --edge <top|bottom|left|right> [--start-menu <keep|win10|win11>]
                             [--force-native] [--no-restart]
          StartBarController --align <left|center>
          StartBarController --autohide <on|off>
          StartBarController --restore [--no-restart]

        --edge, --align and --autohide can be combined in one call.
        --force-native  write the edge to the registry even where Windows 11 does not support it
        --no-restart    do not restart Explorer (changes to the edge apply after the next restart)
        """;

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return new CliOptions();
        }

        var options = new CliOptions { Action = CliAction.Apply };
        bool status = false, restore = false;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i].ToLowerInvariant();
            string? Next() => i + 1 < args.Count ? args[++i].ToLowerInvariant() : null;

            switch (arg)
            {
                case "-h" or "--help" or "/?":
                    return new CliOptions { Action = CliAction.Help };
                case "--status":
                    status = true;
                    break;
                case "--restore":
                    restore = true;
                    break;
                case "--force-native":
                    options = options with { ForceNative = true };
                    break;
                case "--no-restart":
                    options = options with { NoRestart = true };
                    break;
                case "--edge":
                    TaskbarEdge? edge = Next() switch
                    {
                        "top" => TaskbarEdge.Top,
                        "bottom" => TaskbarEdge.Bottom,
                        "left" => TaskbarEdge.Left,
                        "right" => TaskbarEdge.Right,
                        _ => null,
                    };
                    if (edge is null)
                    {
                        return Fail("--edge expects top, bottom, left or right.");
                    }

                    options = options with { Edge = edge };
                    break;
                case "--start-menu":
                    StartMenuStyle? style = Next() switch
                    {
                        "keep" => StartMenuStyle.Keep,
                        "win10" or "windows10" => StartMenuStyle.Windows10,
                        "win11" or "windows11" => StartMenuStyle.Windows11,
                        _ => null,
                    };
                    if (style is null)
                    {
                        return Fail("--start-menu expects keep, win10 or win11.");
                    }

                    options = options with { StartMenu = style.Value };
                    break;
                case "--align":
                    TaskbarAlignment? align = Next() switch
                    {
                        "left" => TaskbarAlignment.Left,
                        "center" or "centre" => TaskbarAlignment.Center,
                        _ => null,
                    };
                    if (align is null)
                    {
                        return Fail("--align expects left or center.");
                    }

                    options = options with { Alignment = align };
                    break;
                case "--autohide":
                    bool? autoHide = Next() switch
                    {
                        "on" or "true" or "1" => true,
                        "off" or "false" or "0" => false,
                        _ => null,
                    };
                    if (autoHide is null)
                    {
                        return Fail("--autohide expects on or off.");
                    }

                    options = options with { AutoHide = autoHide };
                    break;
                default:
                    return Fail($"Unknown argument '{args[i]}'.");
            }
        }

        bool changes = options.Edge is not null || options.Alignment is not null || options.AutoHide is not null;
        if (restore && (status || changes))
        {
            return Fail("--restore cannot be combined with other actions.");
        }

        if (restore)
        {
            return options with { Action = CliAction.Restore };
        }

        if (status && !changes)
        {
            return options with { Action = CliAction.Status };
        }

        if (!changes)
        {
            return Fail("Nothing to do: pass --edge, --align, --autohide, --status or --restore.");
        }

        return options;
    }

    private static CliOptions Fail(string message) => new() { Action = CliAction.Help, Error = message };
}

/// <summary>Executes parsed command-line options. Returns the process exit code.</summary>
public static class CliRunner
{
    public static int Run(CliOptions options, TaskbarController controller, TextWriter output)
    {
        switch (options.Action)
        {
            case CliAction.Help:
                if (options.Error is not null)
                {
                    output.WriteLine($"Error: {options.Error}");
                    output.WriteLine();
                }

                output.WriteLine(CommandLine.Usage);
                return options.Error is null ? 0 : 2;

            case CliAction.Status:
                output.WriteLine(controller.Inspect().Describe());
                output.WriteLine($"Auto-hide: {(controller.Shell.GetAutoHide() ? "on" : "off")}");
                output.WriteLine($"Backup of original settings: {(controller.HasBackup ? "yes" : "no")}");
                return 0;

            case CliAction.Restore:
                if (!controller.Restore(!options.NoRestart))
                {
                    output.WriteLine("No backup found; nothing to restore.");
                    return 1;
                }

                output.WriteLine("Original taskbar settings restored.");
                return 0;

            case CliAction.Apply:
                // Plan the edge first so an unsupported request changes nothing.
                PositionPlan? plan = options.Edge is TaskbarEdge edge
                    ? controller.Plan(new PositionRequest(edge, options.StartMenu, options.ForceNative))
                    : null;
                if (plan is not null)
                {
                    output.WriteLine(plan.Summary);
                    foreach (string warning in plan.Warnings)
                    {
                        output.WriteLine($"Note: {warning}");
                    }

                    if (!plan.CanApply)
                    {
                        return 1;
                    }
                }

                if (options.Alignment is TaskbarAlignment alignment)
                {
                    controller.SetAlignment(alignment);
                    output.WriteLine($"Alignment set to {alignment.ToString().ToLowerInvariant()}.");
                }

                if (options.AutoHide is bool autoHide)
                {
                    controller.Shell.SetAutoHide(autoHide);
                    output.WriteLine($"Auto-hide {(autoHide ? "enabled" : "disabled")}.");
                }

                if (plan is not null)
                {
                    controller.Apply(plan, !options.NoRestart);
                    output.WriteLine(options.NoRestart
                        ? "Saved. The new position applies the next time Explorer restarts."
                        : "Taskbar moved.");
                }

                return 0;

            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Action, "The GUI is not handled by CliRunner.");
        }
    }
}
