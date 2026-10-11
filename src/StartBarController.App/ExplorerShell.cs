using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using StartBarController.Core;

namespace StartBarController.App;

internal sealed class ExplorerShell : IExplorerShell
{
    public void RestartExplorer()
    {
        foreach (Process process in Process.GetProcessesByName("explorer"))
        {
            using (process)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException)
                {
                    // Already exiting, or belongs to another user session.
                }
            }
        }

        // Winlogon normally relaunches the shell by itself (AutoRestartShell); start it only if it does not.
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (IsExplorerRunning())
            {
                return;
            }

            Thread.Sleep(250);
        }

        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Process.Start(new ProcessStartInfo(explorer) { UseShellExecute = true })?.Dispose();
    }

    public bool GetAutoHide()
    {
        var data = NewAppBarData();
        uint state = (uint)NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref data);
        return (state & NativeMethods.ABS_AUTOHIDE) != 0;
    }

    public void SetAutoHide(bool enabled)
    {
        var data = NewAppBarData();
        data.lParam = (IntPtr)(enabled ? NativeMethods.ABS_AUTOHIDE : NativeMethods.ABS_ALWAYSONTOP);
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETSTATE, ref data);
    }

    private static bool IsExplorerRunning()
    {
        Process[] processes = Process.GetProcessesByName("explorer");
        foreach (Process p in processes)
        {
            p.Dispose();
        }

        return processes.Length > 0;
    }

    private static NativeMethods.APPBARDATA NewAppBarData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.APPBARDATA>(),
        hWnd = NativeMethods.FindWindow("Shell_TrayWnd", null),
    };
}

internal static class NativeMethods
{
    public const uint ABM_GETSTATE = 0x4;
    public const uint ABM_SETSTATE = 0xA;
    public const uint ABS_AUTOHIDE = 0x1;
    public const uint ABS_ALWAYSONTOP = 0x2;
    public const int ATTACH_PARENT_PROCESS = -1;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    public static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachConsole(int dwProcessId);
}
