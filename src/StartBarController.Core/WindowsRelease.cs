namespace StartBarController.Core;

/// <summary>Maps a Windows build number to the releases whose taskbar behaves differently.</summary>
public static class WindowsRelease
{
    /// <summary>First Windows 11 build (21H2).</summary>
    public const int Windows11 = 22000;

    /// <summary>Windows 11 22H2. From here the native taskbar ignores any edge but the bottom.</summary>
    public const int Windows11_22H2 = 22621;

    /// <summary>Windows 11 24H2. The stock Windows 10 taskbar and start menu code was removed.</summary>
    public const int Windows11_24H2 = 26100;

    public static bool IsWindows11(int build) => build >= Windows11;

    public static string Describe(int build) => build switch
    {
        < Windows11 => $"Windows 10 (build {build})",
        < Windows11_22H2 => $"Windows 11 21H2 (build {build})",
        < 22631 => $"Windows 11 22H2 (build {build})",
        < Windows11_24H2 => $"Windows 11 23H2 (build {build})",
        < 26200 => $"Windows 11 24H2 (build {build})",
        _ => $"Windows 11 25H2 or later (build {build})",
    };
}
