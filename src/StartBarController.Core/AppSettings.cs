using System.Text.Json;
using System.Text.Json.Serialization;

namespace StartBarController.Core;

/// <summary>Options for the Windhawk taskbar mods, kept by this app and written to the mods.</summary>
public sealed record WindhawkOptions
{
    /// <summary>Vertical taskbar width in pixels (Windows 11 uses 48 without labels).</summary>
    public int VerticalWidth { get; init; } = 80;

    /// <summary>Start menu position along a vertical taskbar: windowsDefault, top, center or bottom.</summary>
    public string StartMenuAlignment { get; init; } = "windowsDefault";

    /// <summary>Jump list position along a vertical taskbar: top, center or bottom.</summary>
    public string JumpListAlignment { get; init; } = "top";

    public bool RunningIndicatorsOnTop { get; init; }

    /// <summary>Make the start menu slide in from the taskbar's edge.</summary>
    public bool AdjustStartMenuAnimation { get; init; }

    /// <summary>Let the mods use Windows' own position support on builds that have it.</summary>
    public bool UseNativeTaskbar { get; init; } = true;

    public static IReadOnlyList<string> StartMenuAlignments { get; } = new[] { "windowsDefault", "top", "center", "bottom" };

    public static IReadOnlyList<string> JumpListAlignments { get; } = new[] { "top", "center", "bottom" };

    public WindhawkOptions Normalized() => this with
    {
        VerticalWidth = Math.Clamp(VerticalWidth, 40, 400),
        StartMenuAlignment = StartMenuAlignments.Contains(StartMenuAlignment) ? StartMenuAlignment : "windowsDefault",
        JumpListAlignment = JumpListAlignments.Contains(JumpListAlignment) ? JumpListAlignment : "top",
    };
}

public sealed record AppSettings
{
    public PositionEngine Engine { get; init; } = PositionEngine.Auto;

    /// <summary>Start menu style used with ExplorerPatcher.</summary>
    public StartMenuStyle ClassicStartMenu { get; init; } = StartMenuStyle.Windows10;

    public WindhawkOptions Windhawk { get; init; } = new();

    public bool ShowTrayIcon { get; init; } = true;

    /// <summary>Closing the window keeps the app in the notification area.</summary>
    public bool CloseToTray { get; init; } = true;
}

/// <summary>Reads and writes a JSON file, tolerating a missing or damaged file.</summary>
public sealed class JsonFile<T> where T : class
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public JsonFile(string path) => Path = path;

    public string Path { get; }

    public T? Load()
    {
        try
        {
            return File.Exists(Path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(Path), Options) : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(T value)
    {
        string? dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Write then rename so a crash never leaves a half-written file.
        string temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, Path, overwrite: true);
    }

    public void Delete()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
    }

    public static string Serialize(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
