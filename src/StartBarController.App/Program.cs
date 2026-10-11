using StartBarController.Core;

namespace StartBarController.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        CliOptions options = CommandLine.Parse(args);
        TaskbarController controller = CreateController();

        if (options.Action == CliAction.Gui)
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(controller));
            return 0;
        }

        // This is a GUI-subsystem exe: borrow the parent console when started from one,
        // otherwise show the output in a message box.
        bool hasConsole = NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);
        TextWriter output = hasConsole ? Console.Out : new StringWriter();
        if (hasConsole)
        {
            output.WriteLine();
        }

        int exitCode;
        try
        {
            exitCode = CliRunner.Run(options, controller, output);
        }
        catch (Exception e)
        {
            output.WriteLine($"Error: {e.Message}");
            exitCode = 1;
        }

        if (!hasConsole)
        {
            MessageBox.Show(output.ToString(), "Start Bar Controller", MessageBoxButtons.OK,
                exitCode == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        return exitCode;
    }

    private static TaskbarController CreateController()
    {
        var registry = new WindowsRegistry();
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var inspector = new SystemInspector(registry, Directory.Exists, new[] { Path.Combine(programFiles, "ExplorerPatcher") });
        string backupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StartBarController", "backup.json");

        return new TaskbarController(registry, new ExplorerShell(), new JsonFileBackupStore(backupPath), inspector,
            Environment.OSVersion.Version.Build);
    }
}
