namespace StartBarController.Core;

/// <summary>Operations on the running shell that cannot be done through the registry.</summary>
public interface IExplorerShell
{
    /// <summary>
    /// Terminates and restarts explorer.exe so it re-reads taskbar settings. The process
    /// must be killed rather than closed cleanly: on a clean exit Explorer writes its
    /// current StuckRects3 back and would overwrite the new edge.
    /// </summary>
    void RestartExplorer();

    bool GetAutoHide();

    void SetAutoHide(bool enabled);
}
