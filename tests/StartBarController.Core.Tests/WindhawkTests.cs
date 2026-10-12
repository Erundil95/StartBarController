using StartBarController.Core.Windhawk;

namespace StartBarController.Core.Tests;

public class WindhawkTests
{
    private const string Install = @"C:\Program Files\Windhawk";

    private readonly FakeRegistry _registry = new();
    private readonly FakeFileSystem _files = new();
    private readonly FakeIni _ini = new();

    private void SetUpInstall(bool portable, bool cli = false)
    {
        _files.Files.Add(Install + @"\windhawk.exe");
        _files.Files.Add(Install + @"\windhawk.ini");
        _ini.Write(Install + @"\windhawk.ini", "Storage", "EnginePath", @"Engine\1.7.3");
        _files.Files.Add(Install + @"\Engine\1.7.3\engine.ini");
        if (portable)
        {
            _ini.Write(Install + @"\Engine\1.7.3\engine.ini", "Storage", "Portable", "1");
            _ini.Write(Install + @"\Engine\1.7.3\engine.ini", "Storage", "AppDataPath", @"%ProgramData%\Windhawk\Engine");
        }
        else
        {
            _ini.Write(Install + @"\Engine\1.7.3\engine.ini", "Storage", "RegistryKey", @"HKEY_LOCAL_MACHINE\SOFTWARE\Windhawk\Engine");
        }

        if (cli)
        {
            _files.Files.Add(Install + @"\windhawk-cli.exe");
        }
    }

    private WindhawkLocator Locator(params string[] defaults) => new(_registry, _files, _ini, defaults);

    [Fact]
    public void LocatesRegistryStorageFromEngineIni()
    {
        SetUpInstall(portable: false);

        var found = Locator(Install).Locate();

        Assert.NotNull(found);
        Assert.False(found.Storage.Portable);
        Assert.Equal(Hive.LocalMachine, found.Storage.RegistryHive);
        Assert.Equal(@"SOFTWARE\Windhawk\Engine\Mods\taskbar-vertical", found.Storage.ModRegistryKey("taskbar-vertical"));
        Assert.Null(found.CliPath);
    }

    [Fact]
    public void LocatesPortableStorageAndCli()
    {
        SetUpInstall(portable: true, cli: true);

        var found = Locator(Install).Locate()!;

        Assert.True(found.Storage.Portable);
        Assert.Equal(@"C:\ProgramData\Windhawk\Engine\Mods\taskbar-on-top.ini", found.Storage.ModIniPath("taskbar-on-top"));
        Assert.Equal(Install + @"\windhawk-cli.exe", found.CliPath);
    }

    [Fact]
    public void FindsInstallFolderThroughUninstallEntry()
    {
        SetUpInstall(portable: false);
        string key = RegistryPaths.Uninstall + @"\Windhawk";
        _registry.SetString(Hive.LocalMachine, key, "DisplayName", "Windhawk");
        _registry.SetString(Hive.LocalMachine, key, "UninstallString", "\"" + Install + "\\uninstall.exe\"");

        Assert.NotNull(Locator().Locate());
    }

    [Fact]
    public void ReturnsNullWhenNotInstalled() => Assert.Null(Locator(Install).Locate());

    [Fact]
    public void RegistryStoreReadsAndAppliesEdits()
    {
        var storage = new WindhawkStorage { RegistryHive = Hive.LocalMachine, RegistryKey = @"SOFTWARE\Windhawk\Engine" };
        string mod = storage.ModRegistryKey(WindhawkMods.TaskbarVertical);
        _registry.SetString(Hive.LocalMachine, mod, "LibraryFileName", "taskbar-vertical_1.4.2_123.dll");
        _registry.SetDword(Hive.LocalMachine, mod, "Disabled", 1);
        _registry.SetString(Hive.LocalMachine, mod + @"\Settings", "taskbarLocation", "left");
        _registry.SetDword(Hive.LocalMachine, mod + @"\Settings", "TaskbarWidth", 80);
        _registry.SetDword(Hive.LocalMachine, mod, "SettingsChangeTime", 2_000_000_000);
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var store = new WindhawkModStore(storage, _registry, _ini, () => now);

        var before = store.Read(WindhawkMods.TaskbarVertical);
        Assert.True(before.Installed);
        Assert.False(before.Enabled);
        Assert.Equal("left", before.GetText("taskbarLocation"));
        Assert.Equal(80, before.GetNumber("TaskbarWidth"));

        store.Apply(new WindhawkModEdit
        {
            ModId = WindhawkMods.TaskbarVertical,
            Enabled = true,
            Settings = { ["taskbarLocation"] = ModSettingValue.Of("right"), ["TaskbarWidth"] = null },
        });

        var after = store.Read(WindhawkMods.TaskbarVertical);
        Assert.True(after.Enabled);
        Assert.Equal("right", after.GetText("taskbarLocation"));
        Assert.Null(after.GetNumber("TaskbarWidth"));
        // The stamp must change even though the stored one is "in the future".
        Assert.Equal(2_000_000_001, _registry.GetDword(Hive.LocalMachine, mod, "SettingsChangeTime"));
    }

    [Fact]
    public void EnableOnlyEditDoesNotTouchSettingsStamp()
    {
        var storage = new WindhawkStorage { RegistryHive = Hive.LocalMachine, RegistryKey = @"SOFTWARE\Windhawk\Engine" };
        var store = new WindhawkModStore(storage, _registry, _ini);
        store.Apply(new WindhawkModEdit { ModId = WindhawkMods.TaskbarOnTop, Enabled = false });

        string mod = storage.ModRegistryKey(WindhawkMods.TaskbarOnTop);
        Assert.Equal(1, _registry.GetDword(Hive.LocalMachine, mod, "Disabled"));
        Assert.Null(_registry.GetDword(Hive.LocalMachine, mod, "SettingsChangeTime"));
    }

    [Fact]
    public void PortableStoreUsesIniSections()
    {
        var storage = new WindhawkStorage { Portable = true, ModsIniFolder = @"C:\WH\Mods" };
        string path = storage.ModIniPath(WindhawkMods.TaskbarOnTop);
        _ini.Write(path, "Mod", "LibraryFileName", "x.dll");
        var store = new WindhawkModStore(storage, _registry, _ini, () => DateTimeOffset.FromUnixTimeSeconds(100));

        store.Apply(new WindhawkModEdit
        {
            ModId = WindhawkMods.TaskbarOnTop,
            Enabled = true,
            Settings = { ["taskbarLocation"] = ModSettingValue.Of("top"), ["runningIndicatorsOnTop"] = ModSettingValue.Of(true) },
        });

        Assert.Equal("top", _ini.Read(path, "Settings", "taskbarLocation"));
        Assert.Equal("1", _ini.Read(path, "Settings", "runningIndicatorsOnTop"));
        Assert.Equal("0", _ini.Read(path, "Mod", "Disabled"));
        Assert.Equal("100", _ini.Read(path, "Mod", "SettingsChangeTime"));
        var state = store.Read(WindhawkMods.TaskbarOnTop);
        Assert.True(state.Installed && state.Enabled);
        Assert.Equal(1, state.GetNumber("runningIndicatorsOnTop"));
    }

    [Fact]
    public void DirectExecutorInstallsThroughCliThenEdits()
    {
        var runner = new FakeProcessRunner();
        var installation = Samples.Windhawk(cli: true);
        var executor = new DirectWindhawkExecutor(s => new WindhawkModStore(s, _registry, _ini), runner);

        executor.Execute(installation, new[] { WindhawkMods.TaskbarOnTop },
            new[] { new WindhawkModEdit { ModId = WindhawkMods.TaskbarOnTop, Enabled = true } });

        var call = Assert.Single(runner.Calls);
        Assert.Equal(installation.CliPath, call.File);
        Assert.Equal(new[] { "mod", "install", "taskbar-on-top", "--disabled", "--yes", "--quiet" }, call.Args);
        Assert.Equal(0, _registry.GetDword(Hive.LocalMachine, installation.Storage.ModRegistryKey("taskbar-on-top"), "Disabled"));
    }

    [Fact]
    public void DirectExecutorReportsCliFailure()
    {
        var runner = new FakeProcessRunner { Handler = (_, _) => new ProcessResult(3, "network error") };
        var executor = new DirectWindhawkExecutor(s => new WindhawkModStore(s, _registry, _ini), runner);

        var e = Assert.Throws<InvalidOperationException>(() =>
            executor.Execute(Samples.Windhawk(cli: true), new[] { WindhawkMods.TaskbarOnTop }, Array.Empty<WindhawkModEdit>()));
        Assert.Contains("network error", e.Message);
    }

    [Fact]
    public void RequestValidationRejectsUnknownModsAndSettings()
    {
        new WindhawkRequest { Edits = { new WindhawkModEdit { ModId = "taskbar-vertical", Settings = { ["TaskbarWidth"] = ModSettingValue.Of(60) } } } }
            .Validate();
        Assert.Throws<InvalidOperationException>(() => new WindhawkRequest { InstallMods = { "evil-mod" } }.Validate());
        Assert.Throws<InvalidOperationException>(() => new WindhawkRequest
        {
            Edits = { new WindhawkModEdit { ModId = "taskbar-on-top", Settings = { ["Include"] = ModSettingValue.Of("*") } } },
        }.Validate());
    }

    [Fact]
    public void RequestRoundTripsThroughJson()
    {
        var request = new WindhawkRequest
        {
            InstallMods = { "taskbar-on-top" },
            Edits = { new WindhawkModEdit { ModId = "taskbar-on-top", Enabled = true, Settings = { ["taskbarLocation"] = ModSettingValue.Of("top"), ["x"] = null } } },
        };

        var copy = WindhawkRequest.FromJson(request.ToJson());

        Assert.Equal(request.InstallMods, copy.InstallMods);
        var edit = Assert.Single(copy.Edits);
        Assert.True(edit.Enabled);
        Assert.Equal("top", edit.Settings["taskbarLocation"]!.Text);
        Assert.Null(edit.Settings["x"]);
    }
}
