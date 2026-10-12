using StartBarController.Core.Windhawk;

namespace StartBarController.Core.Tests;

internal sealed class FakeRegistry : IRegistry
{
    private readonly Dictionary<(Hive, string), Dictionary<string, object>> _keys = new();

    public object? GetValue(Hive hive, string key, string name) =>
        _keys.TryGetValue((hive, key.ToLowerInvariant()), out var values) && values.TryGetValue(name, out var v) ? v : null;

    public void SetDword(Hive hive, string key, string name, int value) => Key(hive, key)[name] = value;

    public void SetBinary(Hive hive, string key, string name, byte[] value) => Key(hive, key)[name] = value.ToArray();

    public void SetString(Hive hive, string key, string name, string value) => Key(hive, key)[name] = value;

    public void DeleteValue(Hive hive, string key, string name)
    {
        if (_keys.TryGetValue((hive, key.ToLowerInvariant()), out var values))
        {
            values.Remove(name);
        }
    }

    public IReadOnlyList<string> GetValueNames(Hive hive, string key) =>
        _keys.TryGetValue((hive, key.ToLowerInvariant()), out var values) ? values.Keys.ToList() : new List<string>();

    public IReadOnlyList<string> GetSubKeyNames(Hive hive, string key)
    {
        string prefix = key.ToLowerInvariant() + @"\";
        return _keys.Keys
            .Where(k => k.Item1 == hive && k.Item2.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => k.Item2[prefix.Length..].Split('\\')[0])
            .Distinct()
            .ToList();
    }

    private Dictionary<string, object> Key(Hive hive, string key)
    {
        var id = (hive, key.ToLowerInvariant());
        if (!_keys.TryGetValue(id, out var values))
        {
            _keys[id] = values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        return values;
    }
}

internal sealed class FakeIni : IIniFiles
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);

    public string? Read(string path, string section, string key) =>
        _sections.TryGetValue($"{path}|{section}", out var keys) && keys.TryGetValue(key, out var v) ? v : null;

    public void Write(string path, string section, string key, string? value)
    {
        string id = $"{path}|{section}";
        if (!_sections.TryGetValue(id, out var keys))
        {
            _sections[id] = keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        if (value is null)
        {
            keys.Remove(key);
        }
        else
        {
            keys[key] = value;
        }
    }

    public IReadOnlyList<string> Keys(string path, string section) =>
        _sections.TryGetValue($"{path}|{section}", out var keys) ? keys.Keys.ToList() : new List<string>();
}

internal sealed class FakeFileSystem : IFileSystem
{
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => Files.Contains(path);

    public bool DirectoryExists(string path) => Directories.Contains(path);

    public IReadOnlyList<string> GetDirectories(string path) => Array.Empty<string>();

    public string ExpandEnvironmentVariables(string text) => text.Replace("%ProgramData%", @"C:\ProgramData", StringComparison.OrdinalIgnoreCase);
}

internal sealed class FakeShell : IExplorerShell
{
    public int Restarts { get; private set; }

    public int Notifications { get; private set; }

    public bool AutoHide { get; set; }

    public void RestartExplorer() => Restarts++;

    public void NotifySettingsChanged() => Notifications++;

    public bool GetAutoHide() => AutoHide;

    public void SetAutoHide(bool enabled) => AutoHide = enabled;
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<(string File, IReadOnlyList<string> Args)> Calls { get; } = new();

    public Func<string, IReadOnlyList<string>, ProcessResult> Handler { get; set; } = (_, _) => new ProcessResult(0, "");

    public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        Calls.Add((fileName, arguments));
        return Handler(fileName, arguments);
    }
}

internal static class Samples
{
    public const int Win10 = 19045;
    public const int Win11_21H2 = 22000;
    public const int Win11_23H2 = 22631;
    public const int Win11_24H2 = 26100;

    /// <summary>A 48-byte StuckRects3 blob as Windows 11 writes it, docked to the bottom.</summary>
    public static byte[] StuckRectsBottom() => new byte[]
    {
        0x30, 0x00, 0x00, 0x00, 0xFE, 0xFF, 0xFF, 0xFF, 0x7A, 0xF4, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00,
        0x3C, 0x00, 0x00, 0x00, 0x30, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x04, 0x00, 0x00,
        0x80, 0x07, 0x00, 0x00, 0x38, 0x04, 0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
    };

    public static WindhawkInstallation Windhawk(bool cli = false) => new()
    {
        InstallFolder = @"C:\Program Files\Windhawk",
        AppPath = @"C:\Program Files\Windhawk\windhawk.exe",
        CliPath = cli ? @"C:\Program Files\Windhawk\windhawk-cli.exe" : null,
        Storage = new WindhawkStorage { RegistryHive = Hive.LocalMachine, RegistryKey = @"SOFTWARE\Windhawk\Engine" },
    };

    public static WindhawkModState Mod(string id, bool installed = true, bool enabled = true, params (string, string)[] settings) => new()
    {
        ModId = id,
        Installed = installed,
        Enabled = enabled,
        Settings = settings.ToDictionary(s => s.Item1, s => ModSettingValue.Of(s.Item2), StringComparer.OrdinalIgnoreCase),
    };

    public static SystemState State(
        int build,
        bool windhawk = false,
        bool cli = false,
        bool ep = false,
        int? epStyle = null,
        params WindhawkModState[] mods) => new()
    {
        WindowsBuild = build,
        ExplorerPatcherInstalled = ep,
        ExplorerPatcherTaskbarStyle = epStyle,
        StuckRects3 = StuckRectsBottom(),
        Windhawk = windhawk ? Windhawk(cli) : null,
        WindhawkMods = mods.ToDictionary(m => m.ModId, StringComparer.OrdinalIgnoreCase),
    };
}
