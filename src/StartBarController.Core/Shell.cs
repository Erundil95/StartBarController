using System.Text.Json;
using StartBarController.Core.Windhawk;

namespace StartBarController.Core;

/// <summary>Operations on the running shell that cannot be done through the registry.</summary>
public interface IExplorerShell
{
    /// <summary>
    /// Terminates and restarts explorer.exe so it re-reads taskbar settings. Explorer must be
    /// killed rather than closed cleanly: on a clean exit it saves its current StuckRects3
    /// over the new value.
    /// </summary>
    void RestartExplorer();

    /// <summary>Broadcasts WM_SETTINGCHANGE("TraySettings") so Explorer re-reads taskbar settings.</summary>
    void NotifySettingsChanged();

    bool GetAutoHide();

    void SetAutoHide(bool enabled);
}

/// <summary>Applies Windhawk mod installs and edits.</summary>
public interface IWindhawkExecutor
{
    void Execute(WindhawkInstallation installation, IReadOnlyList<string> installMods, IReadOnlyList<WindhawkModEdit> edits);
}

public sealed record ProcessResult(int ExitCode, string Output);

public interface IProcessRunner
{
    ProcessResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout);
}

/// <summary>
/// A batch of Windhawk work handed to the elevated helper process. The helper re-locates
/// Windhawk itself and only accepts the two taskbar mods and the settings this app manages.
/// </summary>
public sealed record WindhawkRequest
{
    public List<string> InstallMods { get; init; } = new();

    public List<WindhawkModEdit> Edits { get; init; } = new();

    public void Validate()
    {
        foreach (string id in InstallMods.Concat(Edits.Select(e => e.ModId)))
        {
            if (!WindhawkMods.IsKnown(id))
            {
                throw new InvalidOperationException($"Refusing to change unknown Windhawk mod '{id}'.");
            }
        }

        foreach (WindhawkModEdit edit in Edits)
        {
            IReadOnlySet<string> allowed = WindhawkMods.ManagedSettings(edit.ModId);
            foreach (string name in edit.Settings.Keys)
            {
                if (!allowed.Contains(name))
                {
                    throw new InvalidOperationException($"Refusing to change setting '{name}' of '{edit.ModId}'.");
                }
            }
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static WindhawkRequest FromJson(string json) =>
        JsonSerializer.Deserialize<WindhawkRequest>(json) ?? throw new InvalidOperationException("Empty Windhawk request.");
}

/// <summary>Writes Windhawk storage in-process (the caller must have write access to it).</summary>
public sealed class DirectWindhawkExecutor : IWindhawkExecutor
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(5);

    private readonly Func<WindhawkStorage, WindhawkModStore> _storeFactory;
    private readonly IProcessRunner _processes;

    public DirectWindhawkExecutor(Func<WindhawkStorage, WindhawkModStore> storeFactory, IProcessRunner processes)
    {
        _storeFactory = storeFactory;
        _processes = processes;
    }

    public void Execute(WindhawkInstallation installation, IReadOnlyList<string> installMods, IReadOnlyList<WindhawkModEdit> edits)
    {
        foreach (string modId in installMods)
        {
            if (installation.CliPath is null)
            {
                throw new InvalidOperationException(
                    $"Install the \"{WindhawkMods.DisplayName(modId)}\" mod from the Windhawk app first.");
            }

            // Installed disabled; the edit below writes its settings and then enables it.
            ProcessResult result = _processes.Run(installation.CliPath,
                new[] { "mod", "install", modId, "--disabled", "--yes", "--quiet" }, InstallTimeout);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Windhawk could not install \"{WindhawkMods.DisplayName(modId)}\" (exit code {result.ExitCode}). {result.Output}".Trim());
            }
        }

        WindhawkModStore store = _storeFactory(installation.Storage);
        foreach (WindhawkModEdit edit in edits)
        {
            store.Apply(edit);
        }
    }
}
