using System.Windows;
using System.Windows.Controls;
using StartBarController.Core;

namespace StartBarController.App.Views;

public partial class TaskbarPage : UserControl
{
    private static readonly string[] BehaviorIds = { "alignment", "combine", "combine-other", "badges", "flashing", "endtask" };

    private readonly MainWindow _window;
    private readonly AppHost _host;
    private readonly Dictionary<string, Control> _controls = new();
    private bool _loading = true;

    public TaskbarPage(MainWindow window)
    {
        _window = window;
        _host = window.Host;
        InitializeComponent();
        BuildCards();
        _host.Changed += (_, _) => Update();
        Update();
    }

    private void BuildCards()
    {
        int build = _host.Controller.WindowsBuild;
        var unavailable = new List<string>();
        foreach (TaskbarSetting setting in TaskbarSettings.All)
        {
            if (!setting.IsAvailable(build))
            {
                unavailable.Add(setting.Title);
                continue;
            }

            Control control = setting.IsToggle ? NewToggle(setting) : NewComboBox(setting);
            _controls[setting.Id] = control;
            (BehaviorIds.Contains(setting.Id) ? BehaviorCards : ItemCards).Children.Add(Card(setting.Title, setting.Description, control));
        }

        UnavailableNote.Text = unavailable.Count == 0 ? string.Empty
            : $"Not available on this version of Windows: {string.Join(", ", unavailable)}.";
        UnavailableNote.Visibility = unavailable.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private CheckBox NewToggle(TaskbarSetting setting)
    {
        var toggle = new CheckBox { Style = (Style)FindResource("ToggleSwitch"), VerticalAlignment = VerticalAlignment.Center };
        toggle.Click += (_, _) => _ = Change(setting, toggle.IsChecked == true ? 1 : 0);
        return toggle;
    }

    private ComboBox NewComboBox(TaskbarSetting setting)
    {
        var box = new ComboBox { Width = 220, VerticalAlignment = VerticalAlignment.Center };
        foreach (TaskbarOption option in setting.Options)
        {
            box.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
        }

        box.SelectionChanged += (_, _) =>
        {
            if (!_loading && box.SelectedItem is ComboBoxItem { Tag: int value })
            {
                _ = Change(setting, value);
            }
        };
        return box;
    }

    private Border Card(string title, string description, Control control)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        text.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("CardTitle") });
        text.Children.Add(new TextBlock { Text = description, Style = (Style)FindResource("Caption") });
        grid.Children.Add(text);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return new Border { Style = (Style)FindResource("Card"), Child = grid };
    }

    private void Update()
    {
        _loading = true;
        try
        {
            SystemState state = _host.State;
            AutoHideToggle.IsChecked = _host.AutoHide;
            bool vertical = state.ActiveEngine == PositionEngine.Windhawk && state.CurrentEdge.IsVertical();
            AutoHideToggle.IsEnabled = !vertical;
            AutoHideCaption.Text = vertical
                ? "Not supported while the Windhawk vertical taskbar is in use."
                : "The taskbar slides out of view until you point at its edge.";

            foreach ((string id, Control control) in _controls)
            {
                TaskbarSetting setting = TaskbarSettings.Find(id)!;
                int value = state.GetSetting(setting);
                switch (control)
                {
                    case CheckBox toggle:
                        toggle.IsChecked = value != 0;
                        break;
                    case ComboBox box:
                        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == value);
                        break;
                }
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private Task Change(TaskbarSetting setting, int value) =>
        _window.RunAsync(null, () => _host.ApplyAsync(new ChangeSet { Registry = new[] { setting.Edit(value) } }));

    private void OnAutoHideClick(object sender, RoutedEventArgs e) =>
        _ = _window.RunAsync(null, () => _host.ApplyAsync(new ChangeSet { AutoHide = AutoHideToggle.IsChecked == true }));
}
