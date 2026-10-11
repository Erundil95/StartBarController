namespace StartBarController.Core;

public enum Hive
{
    CurrentUser,
    LocalMachine,
}

/// <summary>
/// The small slice of the Windows registry this app needs. The real implementation
/// lives in the Windows app; tests use an in-memory fake.
/// </summary>
public interface IRegistry
{
    /// <summary>Returns a REG_DWORD as int, a REG_BINARY as byte[], a REG_SZ as string, or null if absent.</summary>
    object? GetValue(Hive hive, string key, string name);

    void SetDword(Hive hive, string key, string name, int value);

    void SetBinary(Hive hive, string key, string name, byte[] value);

    /// <summary>Deletes a value; does nothing if it does not exist.</summary>
    void DeleteValue(Hive hive, string key, string name);

    IReadOnlyList<string> GetValueNames(Hive hive, string key);

    IReadOnlyList<string> GetSubKeyNames(Hive hive, string key);
}

public static class RegistryExtensions
{
    public static int? GetDword(this IRegistry registry, Hive hive, string key, string name) =>
        registry.GetValue(hive, key, name) as int?;

    public static byte[]? GetBinary(this IRegistry registry, Hive hive, string key, string name) =>
        registry.GetValue(hive, key, name) as byte[];

    public static string? GetString(this IRegistry registry, Hive hive, string key, string name) =>
        registry.GetValue(hive, key, name) as string;

    /// <summary>Writes a DWORD, or deletes the value when <paramref name="value"/> is null.</summary>
    public static void SetOrDeleteDword(this IRegistry registry, Hive hive, string key, string name, int? value)
    {
        if (value is int v)
        {
            registry.SetDword(hive, key, name, v);
        }
        else
        {
            registry.DeleteValue(hive, key, name);
        }
    }
}

/// <summary>Registry locations used by Explorer and ExplorerPatcher.</summary>
public static class RegistryPaths
{
    public const string Explorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    public const string StuckRects3 = Explorer + @"\StuckRects3";
    public const string StuckRects3Value = "Settings";
    public const string MMStuckRects3 = Explorer + @"\MMStuckRects3";
    public const string Advanced = Explorer + @"\Advanced";

    /// <summary>Windows 11 taskbar alignment: 0 = left, 1 = center.</summary>
    public const string TaskbarAl = "TaskbarAl";

    /// <summary>ExplorerPatcher start menu style: 0 = Windows 11, 1 = Windows 10.</summary>
    public const string StartShowClassicMode = "Start_ShowClassicMode";

    public const string ExplorerPatcher = @"Software\ExplorerPatcher";

    /// <summary>ExplorerPatcher taskbar style: 0 = Windows 11, 1 = Windows 10, 2 = Windows 10 (ExplorerPatcher).</summary>
    public const string OldTaskbar = "OldTaskbar";

    /// <summary>ExplorerPatcher classic-taskbar alignment: 0 = at screen edge, 1 = centered.</summary>
    public const string OldTaskbarAl = "OldTaskbarAl";

    public const string Uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
}
