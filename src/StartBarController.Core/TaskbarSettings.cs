namespace StartBarController.Core;

public sealed record TaskbarOption(int Value, string Label);

/// <summary>A taskbar setting Windows stores as a DWORD under HKEY_CURRENT_USER.</summary>
public sealed record TaskbarSetting(
    string Id,
    string Title,
    string Description,
    string Key,
    string Name,
    int DefaultValue,
    IReadOnlyList<TaskbarOption> Options,
    int MinBuild = WindowsRelease.Windows11)
{
    /// <summary>True for on/off settings (shown as a toggle switch).</summary>
    public bool IsToggle => Options.Count == 2 && Options[0].Value == 0 && Options[1].Value == 1;

    public bool IsAvailable(int build) => build >= MinBuild;

    public RegistryEdit Edit(int value) => new(Key, Name, RegistryData.FromDword(value));
}

/// <summary>
/// Native Windows 11 taskbar settings. Explorer re-reads these when it receives a
/// "TraySettings" setting-change broadcast, so they apply without a restart.
/// </summary>
public static class TaskbarSettings
{
    private static readonly TaskbarOption[] OnOff = { new(0, "Off"), new(1, "On") };

    public static TaskbarSetting Alignment { get; } = new(
        "alignment", "Taskbar alignment", "Where the Start button and app icons sit along the taskbar.",
        RegistryPaths.Advanced, "TaskbarAl", 1, new TaskbarOption[] { new(0, "Left"), new(1, "Center") });

    public static TaskbarSetting Search { get; } = new(
        "search", "Search", "How Search appears on the taskbar.",
        RegistryPaths.Search, "SearchboxTaskbarMode", 2,
        new TaskbarOption[] { new(0, "Hide"), new(1, "Search icon only"), new(3, "Search icon and label"), new(2, "Search box") });

    public static TaskbarSetting TaskView { get; } = new(
        "taskview", "Task view button", "Show the Task view button.",
        RegistryPaths.Advanced, "ShowTaskViewButton", 1, OnOff);

    public static TaskbarSetting Combine { get; } = new(
        "combine", "Combine taskbar buttons", "Group windows of the same app into one button and hide labels.",
        RegistryPaths.Advanced, "TaskbarGlomLevel", 0,
        new TaskbarOption[] { new(0, "Always, hide labels"), new(1, "When taskbar is full"), new(2, "Never") },
        WindowsRelease.Windows11_23H2);

    public static TaskbarSetting CombineOther { get; } = new(
        "combine-other", "Combine buttons on other taskbars", "The same, for taskbars on other monitors.",
        RegistryPaths.Advanced, "MMTaskbarGlomLevel", 0,
        new TaskbarOption[] { new(0, "Always, hide labels"), new(1, "When taskbar is full"), new(2, "Never") },
        WindowsRelease.Windows11_23H2);

    public static TaskbarSetting Seconds { get; } = new(
        "seconds", "Show seconds in the clock", "Show seconds in the system tray clock.",
        RegistryPaths.Advanced, "ShowSecondsInSystemClock", 0, OnOff, WindowsRelease.Windows11_22H2);

    public static TaskbarSetting Badges { get; } = new(
        "badges", "Show badges on apps", "Show unread counters and other badges on taskbar apps.",
        RegistryPaths.Advanced, "TaskbarBadges", 1, OnOff, WindowsRelease.Windows11_22H2);

    public static TaskbarSetting Flashing { get; } = new(
        "flashing", "Show flashing on apps", "Let apps flash their taskbar button to get attention.",
        RegistryPaths.Advanced, "TaskbarFlashing", 1, OnOff, WindowsRelease.Windows11_23H2);

    public static TaskbarSetting EndTask { get; } = new(
        "endtask", "End task", "Add \"End task\" to the right-click menu of taskbar apps.",
        RegistryPaths.DeveloperSettings, "TaskbarEndTask", 0, OnOff, WindowsRelease.Windows11_23H2);

    public static IReadOnlyList<TaskbarSetting> All { get; } = new[]
    {
        Alignment, Search, TaskView, Combine, CombineOther, Seconds, Badges, Flashing, EndTask,
    };

    public static TaskbarSetting? Find(string id) =>
        All.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
