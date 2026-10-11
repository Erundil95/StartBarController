using StartBarController.Core;

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

internal sealed class FakeShell : IExplorerShell
{
    public int Restarts { get; private set; }

    public bool AutoHide { get; set; }

    public void RestartExplorer() => Restarts++;

    public bool GetAutoHide() => AutoHide;

    public void SetAutoHide(bool enabled) => AutoHide = enabled;
}

internal sealed class MemoryBackupStore : IBackupStore
{
    public TaskbarBackup? Backup { get; set; }

    public TaskbarBackup? Load() => Backup;

    public void Save(TaskbarBackup backup) => Backup = backup;

    public void Delete() => Backup = null;
}

internal static class Samples
{
    /// <summary>A 48-byte StuckRects3 blob as Windows 11 writes it, docked to the bottom.</summary>
    public static byte[] StuckRectsBottom() => new byte[]
    {
        0x30, 0x00, 0x00, 0x00, 0xFE, 0xFF, 0xFF, 0xFF, 0x7A, 0xF4, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00,
        0x3C, 0x00, 0x00, 0x00, 0x30, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x04, 0x00, 0x00,
        0x80, 0x07, 0x00, 0x00, 0x38, 0x04, 0x00, 0x00, 0x60, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
    };

    public static SystemSnapshot System(int build, bool ep = false, int? epStyle = null, int? classicStart = null) => new()
    {
        WindowsBuild = build,
        ExplorerPatcherInstalled = ep,
        ExplorerPatcherTaskbarStyle = epStyle,
        ClassicStartMenu = classicStart,
        CurrentEdge = TaskbarEdge.Bottom,
    };
}
