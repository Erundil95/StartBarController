using StartBarController.Core.Windhawk;

namespace StartBarController.Core;

/// <summary>Everything one user action changes, applied (and backed up) as a unit.</summary>
public sealed record ChangeSet
{
    public IReadOnlyList<RegistryEdit> Registry { get; init; } = Array.Empty<RegistryEdit>();

    public IReadOnlyList<WindhawkModEdit> Windhawk { get; init; } = Array.Empty<WindhawkModEdit>();

    /// <summary>Windhawk mods to install first (needs windhawk-cli.exe).</summary>
    public IReadOnlyList<string> InstallMods { get; init; } = Array.Empty<string>();

    public bool? AutoHide { get; init; }

    public bool RestartExplorer => Registry.Any(RegistryPaths.NeedsExplorerRestart);

    public bool IsEmpty => Registry.Count == 0 && Windhawk.Count == 0 && InstallMods.Count == 0 && AutoHide is null;

    public static ChangeSet Empty { get; } = new();

    public ChangeSet Merge(ChangeSet other) => new()
    {
        Registry = Registry.Concat(other.Registry).ToList(),
        Windhawk = Windhawk.Concat(other.Windhawk).ToList(),
        InstallMods = InstallMods.Concat(other.InstallMods).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        AutoHide = other.AutoHide ?? AutoHide,
    };
}
