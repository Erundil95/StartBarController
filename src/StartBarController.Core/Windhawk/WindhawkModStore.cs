using System.Globalization;

namespace StartBarController.Core.Windhawk;

/// <summary>
/// Reads and writes a mod's configuration in Windhawk's storage, using the layout the
/// Windhawk engine reads: registry key "&lt;root&gt;\Mods\&lt;id&gt;" (values Disabled,
/// LibraryFileName, SettingsChangeTime; sub key Settings), or for portable installs
/// "Mods\&lt;id&gt;.ini" with [Mod] and [Settings] sections. The engine watches this
/// storage and reloads a mod when it changes; it applies new settings when
/// SettingsChangeTime changes.
/// </summary>
public sealed class WindhawkModStore
{
    private const string Disabled = "Disabled";
    private const string LibraryFileName = "LibraryFileName";
    private const string Version = "Version";
    private const string SettingsChangeTime = "SettingsChangeTime";

    private readonly WindhawkStorage _storage;
    private readonly IRegistry _registry;
    private readonly IIniFiles _ini;
    private readonly Func<DateTimeOffset> _clock;

    public WindhawkModStore(WindhawkStorage storage, IRegistry registry, IIniFiles ini, Func<DateTimeOffset>? clock = null)
    {
        _storage = storage;
        _registry = registry;
        _ini = ini;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public WindhawkModState Read(string modId)
    {
        var settings = new Dictionary<string, ModSettingValue>(StringComparer.OrdinalIgnoreCase);

        if (_storage.Portable)
        {
            string path = _storage.ModIniPath(modId);
            foreach (string key in _ini.Keys(path, "Settings"))
            {
                if (_ini.Read(path, "Settings", key) is { } text)
                {
                    settings[key] = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                        ? ModSettingValue.Of(n)
                        : ModSettingValue.Of(text);
                }
            }

            return new WindhawkModState
            {
                ModId = modId,
                Installed = !string.IsNullOrEmpty(_ini.Read(path, "Mod", LibraryFileName)),
                Enabled = !IsTrue(_ini.Read(path, "Mod", Disabled)),
                Version = _ini.Read(path, "Mod", Version),
                Settings = settings,
            };
        }

        string modKey = _storage.ModRegistryKey(modId);
        foreach (string name in _registry.GetValueNames(_storage.RegistryHive, modKey + @"\Settings"))
        {
            switch (_registry.GetValue(_storage.RegistryHive, modKey + @"\Settings", name))
            {
                case int n:
                    settings[name] = ModSettingValue.Of(n);
                    break;
                case string s:
                    settings[name] = ModSettingValue.Of(s);
                    break;
            }
        }

        return new WindhawkModState
        {
            ModId = modId,
            Installed = !string.IsNullOrEmpty(_registry.GetString(_storage.RegistryHive, modKey, LibraryFileName)),
            Enabled = _registry.GetDword(_storage.RegistryHive, modKey, Disabled) is not (int and not 0),
            Version = _registry.GetString(_storage.RegistryHive, modKey, Version),
            Settings = settings,
        };
    }

    /// <summary>Applies an edit. Settings are written before the mod is enabled, so it loads with them.</summary>
    public void Apply(WindhawkModEdit edit)
    {
        if (edit.Settings.Count > 0)
        {
            foreach ((string name, ModSettingValue? value) in edit.Settings)
            {
                WriteSetting(edit.ModId, name, value);
            }

            int? previous = ReadInt(edit.ModId, SettingsChangeTime);
            WriteInt(edit.ModId, SettingsChangeTime, NextChangeTime(_clock(), previous));
        }

        if (edit.Enabled is bool enabled)
        {
            WriteInt(edit.ModId, Disabled, enabled ? 0 : 1);
        }
    }

    /// <summary>
    /// Unix seconds as a positive 32-bit value, or previous + 1 when that is not later,
    /// so the engine always sees a new stamp (it only reapplies settings when the stamp changes).
    /// </summary>
    internal static int NextChangeTime(DateTimeOffset now, int? previous)
    {
        int stamp = (int)(now.ToUnixTimeSeconds() & 0x7fff_ffff);
        return previous is int p && p >= stamp ? (p + 1) & 0x7fff_ffff : stamp;
    }

    private void WriteSetting(string modId, string name, ModSettingValue? value)
    {
        if (_storage.Portable)
        {
            _ini.Write(_storage.ModIniPath(modId), "Settings", name, value?.ToString());
            return;
        }

        string key = _storage.ModRegistryKey(modId) + @"\Settings";
        if (value is null)
        {
            _registry.DeleteValue(_storage.RegistryHive, key, name);
        }
        else if (value.Number is int n)
        {
            _registry.SetDword(_storage.RegistryHive, key, name, n);
        }
        else
        {
            _registry.SetString(_storage.RegistryHive, key, name, value.Text ?? string.Empty);
        }
    }

    private int? ReadInt(string modId, string name) => _storage.Portable
        ? int.TryParse(_ini.Read(_storage.ModIniPath(modId), "Mod", name), out int v) ? v : null
        : _registry.GetDword(_storage.RegistryHive, _storage.ModRegistryKey(modId), name);

    private void WriteInt(string modId, string name, int value)
    {
        if (_storage.Portable)
        {
            _ini.Write(_storage.ModIniPath(modId), "Mod", name, value.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            _registry.SetDword(_storage.RegistryHive, _storage.ModRegistryKey(modId), name, value);
        }
    }

    private static bool IsTrue(string? text) => int.TryParse(text, out int v) && v != 0;
}
