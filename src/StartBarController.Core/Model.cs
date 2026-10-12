namespace StartBarController.Core;

/// <summary>
/// Screen edge the taskbar is docked to. Values match the edge byte in the StuckRects3
/// blob, the Advanced\TaskbarLocation value and the Win32 ABE_* constants.
/// </summary>
public enum TaskbarEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

/// <summary>How the taskbar gets moved.</summary>
public enum PositionEngine
{
    /// <summary>Pick the best engine available on this system.</summary>
    Auto,

    /// <summary>Windhawk mods that move the real Windows 11 taskbar and start menu.</summary>
    Windhawk,

    /// <summary>ExplorerPatcher's Windows 10-style taskbar.</summary>
    ExplorerPatcher,

    /// <summary>
    /// Windows' own setting: StuckRects3 on Windows 10, Advanced\TaskbarLocation on
    /// Windows 11 preview builds that have the taskbar position feature.
    /// </summary>
    Native,
}

/// <summary>Start menu style to request when ExplorerPatcher drives the taskbar.</summary>
public enum StartMenuStyle
{
    Keep,

    /// <summary>Windows 10 start menu; opens next to the taskbar on any edge.</summary>
    Windows10,

    Windows11,
}

public static class EdgeExtensions
{
    public static string Lower(this TaskbarEdge edge) => edge.ToString().ToLowerInvariant();

    public static bool IsVertical(this TaskbarEdge edge) => edge is TaskbarEdge.Left or TaskbarEdge.Right;

    public static bool TryParse(string? text, out TaskbarEdge edge)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "left": edge = TaskbarEdge.Left; return true;
            case "top": edge = TaskbarEdge.Top; return true;
            case "right": edge = TaskbarEdge.Right; return true;
            case "bottom": edge = TaskbarEdge.Bottom; return true;
            default: edge = TaskbarEdge.Bottom; return false;
        }
    }
}
