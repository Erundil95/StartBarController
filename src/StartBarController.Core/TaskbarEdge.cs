namespace StartBarController.Core;

/// <summary>
/// Screen edge the taskbar is docked to. Values match both the edge byte stored in
/// the StuckRects3 registry blob and the Win32 ABE_* constants.
/// </summary>
public enum TaskbarEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

public enum TaskbarAlignment
{
    Left,
    Center,
}

/// <summary>Start menu style to request when ExplorerPatcher is driving the taskbar.</summary>
public enum StartMenuStyle
{
    /// <summary>Leave whatever is configured today.</summary>
    Keep,

    /// <summary>Windows 10 start menu; it opens next to the taskbar on any edge.</summary>
    Windows10,

    /// <summary>Windows 11 start menu.</summary>
    Windows11,
}
