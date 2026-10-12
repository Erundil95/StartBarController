using System.Globalization;

namespace StartBarController.Core;

public enum CliAction
{
    Gui,
    Help,
    Status,
    ListSettings,
    Apply,
    Restore,

    /// <summary>Internal: the elevated helper that writes Windhawk storage.</summary>
    WindhawkElevated,

    /// <summary>Internal: run by the uninstaller.</summary>
    Uninstall,
}

public sealed record CliOptions
{
    public CliAction Action { get; init; } = CliAction.Gui;

    public bool StartInTray { get; init; }

    public TaskbarEdge? Edge { get; init; }

    public PositionEngine? Engine { get; init; }

    public IReadOnlyList<(TaskbarSetting Setting, int Value)> Settings { get; init; } = Array.Empty<(TaskbarSetting, int)>();

    public bool? AutoHide { get; init; }

    public bool NoRestart { get; init; }

    public string? RequestFile { get; init; }

    public string? Error { get; init; }
}

public static class CommandLine
{
    public const string Usage = """
        Start Bar Controller - move and customize the Windows taskbar.

        Run without arguments to open the app. Command-line use:

          StartBarController --status
          StartBarController --edge <top|bottom|left|right> [--engine <auto|windhawk|explorerpatcher|native>]
          StartBarController --set <setting>=<value> [--set ...]
          StartBarController --autohide <on|off>
          StartBarController --restore
          StartBarController --list-settings
          StartBarController --tray               start in the notification area

        --edge, --set and --autohide can be combined. Add --no-restart to skip restarting
        Explorer when a change needs it (it then applies at the next restart).
        """;

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return new CliOptions();
        }

        var options = new CliOptions { Action = CliAction.Apply };
        var settings = new List<(TaskbarSetting, int)>();
        bool status = false, restore = false, tray = false, list = false;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i].ToLowerInvariant();
            string? Next() => i + 1 < args.Count ? args[++i] : null;

            switch (arg)
            {
                case "-h" or "--help" or "/?":
                    return new CliOptions { Action = CliAction.Help };
                case "--status":
                    status = true;
                    break;
                case "--list-settings":
                    list = true;
                    break;
                case "--restore":
                    restore = true;
                    break;
                case "--tray":
                    tray = true;
                    break;
                case "--no-restart":
                    options = options with { NoRestart = true };
                    break;
                case "--edge":
                    if (!EdgeExtensions.TryParse(Next(), out TaskbarEdge edge))
                    {
                        return Fail("--edge expects top, bottom, left or right.");
                    }

                    options = options with { Edge = edge };
                    break;
                case "--engine":
                    PositionEngine? engine = Next()?.ToLowerInvariant() switch
                    {
                        "auto" => PositionEngine.Auto,
                        "windhawk" => PositionEngine.Windhawk,
                        "explorerpatcher" or "ep" => PositionEngine.ExplorerPatcher,
                        "native" or "windows" => PositionEngine.Native,
                        _ => null,
                    };
                    if (engine is null)
                    {
                        return Fail("--engine expects auto, windhawk, explorerpatcher or native.");
                    }

                    options = options with { Engine = engine };
                    break;
                case "--set":
                    string? pair = Next();
                    int eq = pair?.IndexOf('=') ?? -1;
                    TaskbarSetting? setting = eq > 0 ? TaskbarSettings.Find(pair![..eq]) : null;
                    if (setting is null || !int.TryParse(pair![(eq + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                    {
                        return Fail("--set expects <setting>=<number>; see --list-settings.");
                    }

                    if (setting.Options.All(o => o.Value != value))
                    {
                        return Fail($"'{value}' is not a valid value for {setting.Id}; see --list-settings.");
                    }

                    settings.Add((setting, value));
                    break;
                case "--autohide":
                    bool? autoHide = Next()?.ToLowerInvariant() switch
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
                case "--windhawk-elevated":
                    return Next() is { } file
                        ? new CliOptions { Action = CliAction.WindhawkElevated, RequestFile = file }
                        : Fail("--windhawk-elevated expects a file.");
                case "--uninstall":
                    return new CliOptions { Action = CliAction.Uninstall };
                default:
                    return Fail($"Unknown argument '{args[i]}'.");
            }
        }

        options = options with { Settings = settings };
        bool changes = options.Edge is not null || settings.Count > 0 || options.AutoHide is not null;
        int actions = (status ? 1 : 0) + (restore ? 1 : 0) + (list ? 1 : 0) + (tray ? 1 : 0) + (changes ? 1 : 0);
        if (actions > 1)
        {
            return Fail("Use one of --status, --restore, --list-settings, --tray or a change per call.");
        }

        if (options.Engine is not null && options.Edge is null)
        {
            return Fail("--engine is used together with --edge.");
        }

        return options with
        {
            Action = restore ? CliAction.Restore
                : status ? CliAction.Status
                : list ? CliAction.ListSettings
                : tray ? CliAction.Gui
                : changes ? CliAction.Apply
                : CliAction.Help,
            StartInTray = tray,
            Error = actions == 0 && !tray ? "Nothing to do." : null,
        };
    }

    private static CliOptions Fail(string message) => new() { Action = CliAction.Help, Error = message };
}

/// <summary>Executes parsed command-line options. Returns the process exit code.</summary>
public static class CliRunner
{
    public static int Run(CliOptions options, TaskbarController controller, AppSettings settings, TextWriter output)
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
            {
                SystemState state = controller.Inspect();
                output.WriteLine(state.Describe());
                output.WriteLine($"Windhawk: {(state.WindhawkInstalled ? "installed" : "not installed")}" +
                                 $" · ExplorerPatcher: {(state.ExplorerPatcherInstalled ? "installed" : "not installed")}");
                foreach (string mod in Windhawk.WindhawkMods.All)
                {
                    var m = state.Mod(mod);
                    output.WriteLine($"  {Windhawk.WindhawkMods.DisplayName(mod)}: " +
                                     (!m.Installed ? "not installed" : m.Enabled ? "enabled" : "disabled"));
                }

                output.WriteLine($"Auto-hide: {(controller.Shell.GetAutoHide() ? "on" : "off")}");
                output.WriteLine($"Backup of original settings: {(controller.HasBackup ? "yes" : "no")}");
                return 0;
            }

            case CliAction.ListSettings:
            {
                SystemState state = controller.Inspect();
                foreach (TaskbarSetting s in TaskbarSettings.All)
                {
                    string availability = s.IsAvailable(state.WindowsBuild) ? "" : " (needs a newer Windows 11)";
                    output.WriteLine($"{s.Id}: {s.Title}{availability} - now {state.GetSetting(s)}");
                    output.WriteLine("    " + string.Join(", ", s.Options.Select(o => $"{o.Value} = {o.Label}")));
                }

                return 0;
            }

            case CliAction.Restore:
                if (!controller.Restore(!options.NoRestart))
                {
                    output.WriteLine("Nothing to restore.");
                    return 1;
                }

                output.WriteLine("Original taskbar settings restored.");
                return 0;

            case CliAction.Apply:
            {
                SystemState state = controller.Inspect();
                ChangeSet changes = ChangeSet.Empty;

                if (options.Edge is TaskbarEdge edge)
                {
                    PositionPlan plan = controller.Plan(new PositionRequest(
                        edge, options.Engine ?? settings.Engine, settings.ClassicStartMenu, settings.Windhawk), state);
                    output.WriteLine(plan.Summary);
                    foreach (string warning in plan.Warnings)
                    {
                        output.WriteLine($"Note: {warning}");
                    }

                    if (!plan.CanApply)
                    {
                        return 1;
                    }

                    changes = changes.Merge(plan.Changes);
                }

                foreach ((TaskbarSetting setting, int value) in options.Settings)
                {
                    if (!setting.IsAvailable(state.WindowsBuild))
                    {
                        output.WriteLine($"Note: \"{setting.Title}\" needs a newer Windows 11; it may have no effect.");
                    }

                    changes = changes.Merge(new ChangeSet { Registry = new[] { setting.Edit(value) } });
                }

                if (options.AutoHide is bool autoHide)
                {
                    changes = changes with { AutoHide = autoHide };
                }

                controller.Apply(changes, state, !options.NoRestart);
                output.WriteLine(options.NoRestart && changes.RestartExplorer
                    ? "Saved. Some changes apply the next time Explorer restarts."
                    : "Done.");
                return 0;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Action, "Not a command-line action.");
        }
    }
}
