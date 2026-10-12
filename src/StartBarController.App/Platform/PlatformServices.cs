using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using StartBarController.Core;
using StartBarController.Core.Windhawk;

namespace StartBarController.App.Platform;

/// <summary>INI access through the same Win32 API Windhawk uses.</summary>
internal sealed class WindowsIniFiles : IIniFiles
{
    public string? Read(string path, string section, string key)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        const string Missing = "\u0001missing\u0001";
        char[] buffer = new char[32768];
        uint length = Win32.GetPrivateProfileString(section, key, Missing, buffer, (uint)buffer.Length, path);
        string value = new(buffer, 0, (int)length);
        return value == Missing ? null : value;
    }

    public void Write(string path, string section, string key, string? value)
    {
        if (!Win32.WritePrivateProfileString(section, key, value, path))
        {
            throw new IOException($"Could not write {path}.", Marshal.GetHRForLastWin32Error());
        }
    }

    public IReadOnlyList<string> Keys(string path, string section)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<string>();
        }

        char[] buffer = new char[65536];
        uint length = Win32.GetPrivateProfileString(section, null, null, buffer, (uint)buffer.Length, path);
        return new string(buffer, 0, (int)length).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}

internal sealed class WindowsFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IReadOnlyList<string> GetDirectories(string path) =>
        Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>();

    public string ExpandEnvironmentVariables(string text) => Environment.ExpandEnvironmentVariables(text);
}

internal sealed class ProcessRunner : IProcessRunner
{
    public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            return new ProcessResult(-1, $"{Path.GetFileName(fileName)} did not finish in time.");
        }

        return new ProcessResult(process.ExitCode, (stderr.Result + Environment.NewLine + stdout.Result).Trim());
    }
}

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
                    // Already exiting, or belongs to another session.
                }
            }
        }

        // Winlogon relaunches the shell by itself (AutoRestartShell); start it only if it does not.
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

    public void NotifySettingsChanged() =>
        Win32.SendMessageTimeout(Win32.HWND_BROADCAST, Win32.WM_SETTINGCHANGE, UIntPtr.Zero, "TraySettings",
            Win32.SMTO_ABORTIFHUNG, 1000, out _);

    public bool GetAutoHide()
    {
        var data = NewAppBarData();
        return ((uint)Win32.SHAppBarMessage(Win32.ABM_GETSTATE, ref data) & Win32.ABS_AUTOHIDE) != 0;
    }

    public void SetAutoHide(bool enabled)
    {
        var data = NewAppBarData();
        data.lParam = (IntPtr)(enabled ? Win32.ABS_AUTOHIDE : Win32.ABS_ALWAYSONTOP);
        Win32.SHAppBarMessage(Win32.ABM_SETSTATE, ref data);
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

    private static Win32.APPBARDATA NewAppBarData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<Win32.APPBARDATA>(),
        hWnd = Win32.FindWindow("Shell_TrayWnd", null),
    };
}
