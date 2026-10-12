using StartBarController.Core;
using Forms = System.Windows.Forms;

namespace StartBarController.App;

/// <summary>The notification-area icon, with quick access to the four positions.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly AppHost _host;

    public TrayIcon(AppHost host, Action open, Action<TaskbarEdge> move, Action exit)
    {
        _host = host;
        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => UpdateChecks(menu);
        foreach (TaskbarEdge edge in new[] { TaskbarEdge.Top, TaskbarEdge.Bottom, TaskbarEdge.Left, TaskbarEdge.Right })
        {
            menu.Items.Add(new Forms.ToolStripMenuItem($"Taskbar on the {edge.Lower()}", null, (_, _) => move(edge)) { Tag = edge });
        }

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Open Start Bar Controller", null, (_, _) => open())
        {
            Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold),
        });
        menu.Items.Add(new Forms.ToolStripMenuItem("Exit", null, (_, _) => exit()));

        using Stream icon = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(icon, Forms.SystemInformation.SmallIconSize),
            Text = "Start Bar Controller",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                open();
            }
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private void UpdateChecks(Forms.ContextMenuStrip menu)
    {
        TaskbarEdge current = _host.State.CurrentEdge;
        foreach (Forms.ToolStripItem item in menu.Items)
        {
            if (item is Forms.ToolStripMenuItem { Tag: TaskbarEdge edge } menuItem)
            {
                menuItem.Checked = edge == current;
            }
        }
    }
}
