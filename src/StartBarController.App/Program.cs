using StartBarController.App.Platform;
using StartBarController.Core;

namespace StartBarController.App;

internal static class Program
{
    private const string MutexName = @"Local\StartBarController";
    private const string ShowEventName = @"Local\StartBarController.Show";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--smoke-test", string folder])
        {
            var smoke = new App(startInTray: false, new EventWaitHandle(false, EventResetMode.AutoReset), folder);
            smoke.InitializeComponent();
            return smoke.Run();
        }

        CliOptions options = CommandLine.Parse(args);
        return options.Action switch
        {
            CliAction.Gui => RunGui(options.StartInTray),
            CliAction.WindhawkElevated => RunElevatedHelper(options.RequestFile!),
            CliAction.Uninstall => RunUninstall(),
            _ => RunCli(options),
        };
    }

    private static int RunGui(bool startInTray)
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool first);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!first)
        {
            // Already running: bring that window up instead.
            showEvent.Set();
            return 0;
        }

        var app = new App(startInTray, showEvent);
        app.InitializeComponent();
        return app.Run();
    }

    private static int RunCli(CliOptions options)
    {
        // A GUI-subsystem exe: borrow the parent console when started from one.
        bool hasConsole = Win32.AttachConsole(Win32.ATTACH_PARENT_PROCESS);
        TextWriter output = hasConsole ? Console.Out : new StringWriter();
        if (hasConsole)
        {
            output.WriteLine();
        }

        int exitCode;
        try
        {
            exitCode = CliRunner.Run(options, AppServices.CreateController(), AppServices.LoadSettings(), output);
        }
        catch (Exception e)
        {
            output.WriteLine($"Error: {e.Message}");
            exitCode = 1;
        }

        if (!hasConsole)
        {
            System.Windows.MessageBox.Show(output.ToString(), "Start Bar Controller");
        }

        return exitCode;
    }

    /// <summary>
    /// Runs elevated: applies a Windhawk request written by the unelevated app. Windhawk is
    /// located afresh and the request is validated against the two taskbar mods.
    /// </summary>
    private static int RunElevatedHelper(string requestFile)
    {
        try
        {
            WindhawkRequest request = WindhawkRequest.FromJson(File.ReadAllText(requestFile));
            request.Validate();
            var windhawk = AppServices.LocateWindhawk() ?? throw new InvalidOperationException("Windhawk isn't installed.");
            AppServices.DirectExecutor().Execute(windhawk, request.InstallMods, request.Edits);
            return 0;
        }
        catch (Exception e)
        {
            try
            {
                File.WriteAllText(requestFile + ".error", e.Message);
            }
            catch (IOException)
            {
            }

            return 1;
        }
    }

    /// <summary>Called by the uninstaller: put the taskbar back and remove the autostart entry.</summary>
    private static int RunUninstall()
    {
        try
        {
            Autostart.Set(false);
            AppServices.CreateController().Restore();
        }
        catch (Exception)
        {
            // Never block an uninstall.
        }

        return 0;
    }
}
