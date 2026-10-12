namespace StartBarController.Core;

public enum Hive
{
    CurrentUser,
    LocalMachine,
}

/// <summary>
/// The slice of the Windows registry this app needs. The real implementation lives in
/// the Windows app; tests use an in-memory fake.
/// </summary>
public interface IRegistry
{
    /// <summary>Returns a REG_DWORD as int, a REG_BINARY as byte[], a REG_SZ as string, or null if absent.</summary>
    object? GetValue(Hive hive, string key, string name);

    void SetDword(Hive hive, string key, string name, int value);

    void SetBinary(Hive hive, string key, string name, byte[] value);

    void SetString(Hive hive, string key, string name, string value);

    /// <summary>Deletes a value; does nothing if it does not exist.</summary>
    void DeleteValue(Hive hive, string key, string name);

    IReadOnlyList<string> GetValueNames(Hive hive, string key);

    IReadOnlyList<string> GetSubKeyNames(Hive hive, string key);
}

public enum RegistryKind
{
    Dword,
    Binary,
    String,
}

/// <summary>A typed registry value, serializable for backups and the elevated helper.</summary>
public sealed record RegistryData
{
    public RegistryKind Kind { get; init; }

    public int Dword { get; init; }

    public byte[]? Binary { get; init; }

    public string? Text { get; init; }

    public static RegistryData FromDword(int value) => new() { Kind = RegistryKind.Dword, Dword = value };

    public static RegistryData FromBinary(byte[] value) => new() { Kind = RegistryKind.Binary, Binary = value };

    public static RegistryData FromString(string value) => new() { Kind = RegistryKind.String, Text = value };

    public static RegistryData? FromObject(object? value) => value switch
    {
        int i => FromDword(i),
        byte[] b => FromBinary(b),
        string s => FromString(s),
        _ => null,
    };

    public bool Equals(RegistryData? other) =>
        other is not null && Kind == other.Kind && Dword == other.Dword && Text == other.Text &&
        (Binary ?? Array.Empty<byte>()).AsSpan().SequenceEqual(other.Binary ?? Array.Empty<byte>());

    public override int GetHashCode() => HashCode.Combine(Kind, Dword, Text, Binary?.Length);
}

/// <summary>Sets (or, with a null value, deletes) one HKEY_CURRENT_USER value.</summary>
public sealed record RegistryEdit(string Key, string Name, RegistryData? Value)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Id => $@"{Key}\{Name}".ToLowerInvariant();
}

public static class RegistryExtensions
{
    public static int? GetDword(this IRegistry registry, Hive hive, string key, string name) =>
        registry.GetValue(hive, key, name) as int?;

    public static byte[]? GetBinary(this IRegistry registry, Hive hive, string key, string name) =>
        registry.GetValue(hive, key, name) as byte[];

    public static string? GetString(this IRegistry registry, Hive hive, string key, string name) =>
        registry.GetValue(hive, key, name) as string;

    public static void Write(this IRegistry registry, Hive hive, string key, string name, RegistryData? value)
    {
        switch (value)
        {
            case null:
                registry.DeleteValue(hive, key, name);
                break;
            case { Kind: RegistryKind.Dword }:
                registry.SetDword(hive, key, name, value.Dword);
                break;
            case { Kind: RegistryKind.Binary }:
                registry.SetBinary(hive, key, name, value.Binary ?? Array.Empty<byte>());
                break;
            default:
                registry.SetString(hive, key, name, value.Text ?? string.Empty);
                break;
        }
    }

    public static void Apply(this IRegistry registry, RegistryEdit edit) =>
        registry.Write(Hive.CurrentUser, edit.Key, edit.Name, edit.Value);

    public static RegistryData? Read(this IRegistry registry, Hive hive, string key, string name) =>
        RegistryData.FromObject(registry.GetValue(hive, key, name));

    /// <summary>Splits "HKEY_LOCAL_MACHINE\SOFTWARE\X" (or "HKLM\...") into a hive and sub key.</summary>
    public static bool TryParseRoot(string? path, out Hive hive, out string subKey)
    {
        hive = Hive.CurrentUser;
        subKey = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        int slash = path.IndexOf('\\');
        string root = slash < 0 ? path : path[..slash];
        subKey = slash < 0 ? string.Empty : path[(slash + 1)..].Trim('\\');
        switch (root.ToUpperInvariant())
        {
            case "HKEY_LOCAL_MACHINE" or "HKLM":
                hive = Hive.LocalMachine;
                return subKey.Length > 0;
            case "HKEY_CURRENT_USER" or "HKCU":
                hive = Hive.CurrentUser;
                return subKey.Length > 0;
            default:
                return false;
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
    public const string DeveloperSettings = Advanced + @"\TaskbarDeveloperSettings";
    public const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";
    public const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Taskbar edge read by Windows 11 builds with the native taskbar position feature.</summary>
    public const string TaskbarLocation = "TaskbarLocation";

    public const string StartShowClassicMode = "Start_ShowClassicMode";

    public const string ExplorerPatcher = @"Software\ExplorerPatcher";

    /// <summary>ExplorerPatcher taskbar style: 0 = Windows 11, 1 = Windows 10, 2 = Windows 10 (ExplorerPatcher).</summary>
    public const string OldTaskbar = "OldTaskbar";

    /// <summary>ExplorerPatcher classic-taskbar alignment: 0 = at screen edge, 1 = centered.</summary>
    public const string OldTaskbarAl = "OldTaskbarAl";

    public const string Uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Values Explorer only reads at startup; changing them needs an Explorer restart.</summary>
    public static bool NeedsExplorerRestart(RegistryEdit edit) =>
        edit.Key.Equals(StuckRects3, StringComparison.OrdinalIgnoreCase) ||
        edit.Key.Equals(MMStuckRects3, StringComparison.OrdinalIgnoreCase) ||
        edit.Key.Equals(ExplorerPatcher, StringComparison.OrdinalIgnoreCase) && edit.Name.Equals(OldTaskbar, StringComparison.OrdinalIgnoreCase) ||
        edit.Name.Equals(StartShowClassicMode, StringComparison.OrdinalIgnoreCase) ||
        edit.Name.Equals(TaskbarLocation, StringComparison.OrdinalIgnoreCase);
}
