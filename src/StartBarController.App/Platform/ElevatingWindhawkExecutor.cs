using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using StartBarController.Core;
using StartBarController.Core.Windhawk;

namespace StartBarController.App.Platform;

/// <summary>
/// Runs Windhawk work in-process when the storage is writable, otherwise in an elevated
/// copy of this app (<c>--windhawk-elevated</c>), which costs one UAC prompt.
/// </summary>
internal sealed class ElevatingWindhawkExecutor : IWindhawkExecutor
{
    private readonly DirectWindhawkExecutor _direct;

    public ElevatingWindhawkExecutor(DirectWindhawkExecutor direct) => _direct = direct;

    public void Execute(WindhawkInstallation installation, IReadOnlyList<string> installMods, IReadOnlyList<WindhawkModEdit> edits)
    {
        if (IsElevated() || (installMods.Count == 0 && CanWrite(installation.Storage)))
        {
            _direct.Execute(installation, installMods, edits);
            return;
        }

        var request = new WindhawkRequest { InstallMods = installMods.ToList(), Edits = edits.ToList() };
        string folder = Path.Combine(AppPaths.DataFolder, "requests");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(file, request.ToJson());

        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = $"--windhawk-elevated \"{file}\"",
            };

            using Process process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the elevated helper.");
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                string error = File.Exists(file + ".error") ? File.ReadAllText(file + ".error") : $"exit code {process.ExitCode}";
                throw new InvalidOperationException($"Changing Windhawk's settings failed: {error}");
            }
        }
        catch (Win32Exception e) when (e.NativeErrorCode == Win32.ERROR_CANCELLED)
        {
            throw new OperationCanceledException("Administrator permission was declined, so nothing was changed.");
        }
        finally
        {
            File.Delete(file);
            File.Delete(file + ".error");
        }
    }

    public static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool CanWrite(WindhawkStorage storage)
    {
        if (!storage.Portable)
        {
            return WindowsRegistry.CanWrite(storage.RegistryHive, storage.RegistryKey + @"\Mods");
        }

        try
        {
            Directory.CreateDirectory(storage.ModsIniFolder);
            string probe = Path.Combine(storage.ModsIniFolder, $".sbc-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
