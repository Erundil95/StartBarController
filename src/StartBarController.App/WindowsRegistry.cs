using Microsoft.Win32;
using StartBarController.Core;

namespace StartBarController.App;

internal sealed class WindowsRegistry : IRegistry
{
    public object? GetValue(Hive hive, string key, string name)
    {
        using RegistryKey? k = Open(hive).OpenSubKey(key);
        return k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public void SetDword(Hive hive, string key, string name, int value)
    {
        using RegistryKey k = Open(hive).CreateSubKey(key, writable: true);
        k.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void SetBinary(Hive hive, string key, string name, byte[] value)
    {
        using RegistryKey k = Open(hive).CreateSubKey(key, writable: true);
        k.SetValue(name, value, RegistryValueKind.Binary);
    }

    public void DeleteValue(Hive hive, string key, string name)
    {
        using RegistryKey? k = Open(hive).OpenSubKey(key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    public IReadOnlyList<string> GetValueNames(Hive hive, string key)
    {
        using RegistryKey? k = Open(hive).OpenSubKey(key);
        return k?.GetValueNames() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> GetSubKeyNames(Hive hive, string key)
    {
        using RegistryKey? k = Open(hive).OpenSubKey(key);
        return k?.GetSubKeyNames() ?? Array.Empty<string>();
    }

    private static RegistryKey Open(Hive hive) => RegistryKey.OpenBaseKey(
        hive == Hive.LocalMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
        RegistryView.Registry64);
}
