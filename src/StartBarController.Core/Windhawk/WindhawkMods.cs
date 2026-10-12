namespace StartBarController.Core.Windhawk;

/// <summary>
/// The Windhawk mods this app drives. Both are by m417z, published in the official
/// Windhawk repository under GPL-3.0, and keep the real Windows 11 taskbar and
/// start menu. They switch to Windows' own position support on builds that have it.
/// </summary>
public static class WindhawkMods
{
    /// <summary>"Taskbar on top for Windows 11": top or bottom.</summary>
    public const string TaskbarOnTop = "taskbar-on-top";

    /// <summary>"Vertical Taskbar for Windows 11": left or right.</summary>
    public const string TaskbarVertical = "taskbar-vertical";

    public static IReadOnlyList<string> All { get; } = new[] { TaskbarOnTop, TaskbarVertical };

    public static string DisplayName(string modId) => modId switch
    {
        TaskbarOnTop => "Taskbar on top for Windows 11",
        TaskbarVertical => "Vertical Taskbar for Windows 11",
        _ => modId,
    };

    /// <summary>The mod that places the taskbar on <paramref name="edge"/>, or null for the default bottom.</summary>
    public static string? ForEdge(TaskbarEdge edge) => edge switch
    {
        TaskbarEdge.Top => TaskbarOnTop,
        TaskbarEdge.Left or TaskbarEdge.Right => TaskbarVertical,
        _ => null,
    };

    /// <summary>Setting names this app writes, per mod. Anything else in the mod's settings is left alone.</summary>
    public static IReadOnlySet<string> ManagedSettings(string modId) => modId switch
    {
        TaskbarOnTop => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "taskbarLocation", "taskbarLocationSecondary", "runningIndicatorsOnTop", "startMenuAnimationAdjust",
            "useNativeTaskbar",
        },
        TaskbarVertical => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "taskbarLocation", "TaskbarWidth", "taskbarLocationSecondary", "jumpListAlignment", "startMenuAlignment",
            "startMenuAnimationAdjust", "useNativeTaskbar",
        },
        _ => new HashSet<string>(),
    };

    public static bool IsKnown(string modId) => All.Contains(modId, StringComparer.OrdinalIgnoreCase);
}

/// <summary>A Windhawk mod setting: a string or a number (booleans are stored as 0/1).</summary>
public sealed record ModSettingValue
{
    public string? Text { get; init; }

    public int? Number { get; init; }

    public static ModSettingValue Of(string text) => new() { Text = text };

    public static ModSettingValue Of(int number) => new() { Number = number };

    public static ModSettingValue Of(bool flag) => new() { Number = flag ? 1 : 0 };

    public override string ToString() => Text ?? Number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
}

/// <summary>Enables/disables a mod and sets (or, with null, removes) some of its settings.</summary>
public sealed record WindhawkModEdit
{
    public required string ModId { get; init; }

    public bool? Enabled { get; init; }

    public Dictionary<string, ModSettingValue?> Settings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record WindhawkModState
{
    public required string ModId { get; init; }

    public bool Installed { get; init; }

    public bool Enabled { get; init; }

    public string? Version { get; init; }

    public IReadOnlyDictionary<string, ModSettingValue> Settings { get; init; } =
        new Dictionary<string, ModSettingValue>(StringComparer.OrdinalIgnoreCase);

    public string? GetText(string name) => Settings.TryGetValue(name, out var v) ? v.Text : null;

    public int? GetNumber(string name) =>
        !Settings.TryGetValue(name, out var v) ? null
        : v.Number ?? (int.TryParse(v.Text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : null);
}
