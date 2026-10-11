using System.Text.Json;

namespace StartBarController.Core;

/// <summary>Taskbar-related registry values as they were before this app first changed them.</summary>
public sealed class TaskbarBackup
{
    public DateTime CreatedUtc { get; set; }

    public int WindowsBuild { get; set; }

    public byte[]? StuckRects3 { get; set; }

    public Dictionary<string, byte[]> MMStuckRects3 { get; set; } = new();

    public int? OldTaskbar { get; set; }

    public int? OldTaskbarAl { get; set; }

    public int? StartShowClassicMode { get; set; }

    public int? TaskbarAl { get; set; }
}

public interface IBackupStore
{
    TaskbarBackup? Load();

    void Save(TaskbarBackup backup);

    void Delete();
}

public sealed class JsonFileBackupStore : IBackupStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public JsonFileBackupStore(string path) => Path = path;

    public string Path { get; }

    public TaskbarBackup? Load()
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TaskbarBackup>(File.ReadAllText(Path), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(TaskbarBackup backup)
    {
        string? dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(Path, JsonSerializer.Serialize(backup, Options));
    }

    public void Delete()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
    }
}
