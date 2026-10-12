using StartBarController.Core.Windhawk;

namespace StartBarController.Core;

public sealed class WindhawkModBackup
{
    public bool Installed { get; set; }

    public bool Enabled { get; set; }

    /// <summary>Original values of the settings this app changed (null: the setting was absent).</summary>
    public Dictionary<string, ModSettingValue?> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The original value of everything this app has changed, recorded the first time each
/// value is touched. Restoring writes those originals back.
/// </summary>
public sealed class TaskbarBackup
{
    public DateTime CreatedUtc { get; set; }

    public int WindowsBuild { get; set; }

    /// <summary>Original registry values, keyed by <see cref="RegistryEdit.Id"/>.</summary>
    public Dictionary<string, RegistryEdit> Registry { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, WindhawkModBackup> Windhawk { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => Registry.Count == 0 && Windhawk.Count == 0;

    /// <summary>Adds the current value of everything <paramref name="changes"/> touches that is not backed up yet.</summary>
    public void Record(ChangeSet changes, SystemState state, IRegistry registry)
    {
        foreach (RegistryEdit edit in changes.Registry)
        {
            if (!Registry.ContainsKey(edit.Id))
            {
                Registry[edit.Id] = edit with { Value = registry.Read(Hive.CurrentUser, edit.Key, edit.Name) };
            }
        }

        foreach (WindhawkModEdit edit in changes.Windhawk)
        {
            WindhawkModState current = state.Mod(edit.ModId);
            if (!Windhawk.TryGetValue(edit.ModId, out WindhawkModBackup? mod))
            {
                Windhawk[edit.ModId] = mod = new WindhawkModBackup { Installed = current.Installed, Enabled = current.Installed && current.Enabled };
            }

            foreach (string name in edit.Settings.Keys)
            {
                if (!mod.Settings.ContainsKey(name))
                {
                    mod.Settings[name] = current.Settings.TryGetValue(name, out ModSettingValue? value) ? value : null;
                }
            }
        }
    }

    /// <summary>The changes that put every recorded value back.</summary>
    public ChangeSet RestoreChanges() => new()
    {
        Registry = Registry.Values.ToList(),
        Windhawk = Windhawk.Select(pair => pair.Value.Installed
                ? new WindhawkModEdit { ModId = pair.Key, Enabled = pair.Value.Enabled, Settings = new(pair.Value.Settings, StringComparer.OrdinalIgnoreCase) }
                // A mod this app installed is just turned off; its settings stay valid for later use.
                : new WindhawkModEdit { ModId = pair.Key, Enabled = false })
            .ToList(),
    };
}
