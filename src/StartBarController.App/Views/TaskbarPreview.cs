using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StartBarController.Core;

namespace StartBarController.App.Views;

/// <summary>
/// A drawing of a monitor with the taskbar and start menu on <see cref="Edge"/>.
/// Hovering an edge highlights it; clicking raises <see cref="EdgeClicked"/>.
/// </summary>
internal sealed class TaskbarPreview : FrameworkElement
{
    public static readonly DependencyProperty EdgeProperty = DependencyProperty.Register(
        nameof(Edge), typeof(TaskbarEdge), typeof(TaskbarPreview),
        new FrameworkPropertyMetadata(TaskbarEdge.Bottom, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CenteredProperty = DependencyProperty.Register(
        nameof(Centered), typeof(bool), typeof(TaskbarPreview),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private TaskbarEdge? _hover;

    public TaskbarPreview()
    {
        Cursor = Cursors.Hand;
        ToolTip = "Click an edge to move the taskbar there";
    }

    public event EventHandler<TaskbarEdge>? EdgeClicked;

    public TaskbarEdge Edge
    {
        get => (TaskbarEdge)GetValue(EdgeProperty);
        set => SetValue(EdgeProperty, value);
    }

    /// <summary>Centered icons (Windows 11 default) or start-aligned.</summary>
    public bool Centered
    {
        get => (bool)GetValue(CenteredProperty);
        set => SetValue(CenteredProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 360 : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? 220 : availableSize.Height);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        TaskbarEdge? edge = EdgeAt(e.GetPosition(this));
        if (edge != _hover)
        {
            _hover = edge;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (EdgeAt(e.GetPosition(this)) is TaskbarEdge edge)
        {
            EdgeClicked?.Invoke(this, edge);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        Rect screen = ScreenRect();
        if (screen.Width <= 0 || screen.Height <= 0)
        {
            return;
        }

        // Bezel and stand.
        var bezel = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
        dc.DrawRoundedRectangle(bezel, null, Inflate(screen, 6), 10, 10);
        dc.DrawRectangle(bezel, null, new Rect(screen.X + screen.Width / 2 - 22, screen.Bottom + 6, 44, 10));
        dc.DrawRoundedRectangle(bezel, null, new Rect(screen.X + screen.Width / 2 - 50, screen.Bottom + 15, 100, 6), 3, 3);

        // Wallpaper, loosely after the Windows 11 bloom.
        var wallpaper = new RadialGradientBrush(Color.FromRgb(0x7F, 0xB8, 0xF0), Color.FromRgb(0x1E, 0x4F, 0x9E))
        {
            GradientOrigin = new Point(0.65, 0.55),
            Center = new Point(0.6, 0.6),
            RadiusX = 0.9,
            RadiusY = 0.9,
        };
        dc.PushClip(new RectangleGeometry(screen, 4, 4));
        dc.DrawRectangle(wallpaper, null, screen);

        Brush accent = TryFindResource("AccentFillColorDefaultBrush") as Brush ?? Brushes.DodgerBlue;
        if (_hover is TaskbarEdge hover && hover != Edge)
        {
            var glow = accent.Clone();
            glow.Opacity = 0.45;
            dc.DrawRectangle(glow, null, BarRect(screen, hover));
        }

        DrawTaskbar(dc, screen, accent);
        dc.Pop();
    }

    private void DrawTaskbar(DrawingContext dc, Rect screen, Brush accent)
    {
        TaskbarEdge edge = Edge;
        Rect bar = BarRect(screen, edge);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xE8, 0x20, 0x20, 0x20)), null, bar);

        bool vertical = edge.IsVertical();
        const double icon = 9, gap = 6;
        int count = 6;
        double length = count * icon + (count - 1) * gap;
        double along = vertical ? bar.Height : bar.Width;
        double start = Centered ? (along - length) / 2 : 8;
        var iconBrush = new SolidColorBrush(Color.FromArgb(0xDD, 0xF0, 0xF0, 0xF0));
        Rect startButton = Rect.Empty;
        for (int i = 0; i < count; i++)
        {
            double offset = start + i * (icon + gap);
            Rect r = vertical
                ? new Rect(bar.X + (bar.Width - icon) / 2, bar.Y + offset, icon, icon)
                : new Rect(bar.X + offset, bar.Y + (bar.Height - icon) / 2, icon, icon);
            dc.DrawRoundedRectangle(i == 0 ? accent : iconBrush, null, r, 2, 2);
            if (i == 0)
            {
                startButton = r;
            }
        }

        // The start menu opens from the start button, next to the taskbar.
        const double w = 70, h = 82, margin = 5;
        Rect menu = edge switch
        {
            TaskbarEdge.Top => new Rect(Clamp(startButton.X - 6, screen.Left + margin, screen.Right - w - margin), bar.Bottom + margin, w, h),
            TaskbarEdge.Left => new Rect(bar.Right + margin, Clamp(startButton.Y - 6, screen.Top + margin, screen.Bottom - h - margin), w, h),
            TaskbarEdge.Right => new Rect(bar.Left - w - margin, Clamp(startButton.Y - 6, screen.Top + margin, screen.Bottom - h - margin), w, h),
            _ => new Rect(Clamp(startButton.X - 6, screen.Left + margin, screen.Right - w - margin), bar.Top - h - margin, w, h),
        };
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xF0, 0x2B, 0x2B, 0x2B)), null, menu, 5, 5);
        var tile = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                dc.DrawRoundedRectangle(tile, null, new Rect(menu.X + 8 + col * 14, menu.Y + 10 + row * 14, 8, 8), 1.5, 1.5);
            }
        }

        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), null,
            new Rect(menu.X + 8, menu.Bottom - 16, w - 16, 6), 3, 3);
    }

    private Rect ScreenRect()
    {
        // 16:10 screen centred in the control, leaving room for bezel and stand.
        double maxW = ActualWidth - 16, maxH = ActualHeight - 34;
        double width = Math.Min(maxW, maxH * 1.6);
        double height = width / 1.6;
        return new Rect((ActualWidth - width) / 2, 8, Math.Max(0, width), Math.Max(0, height));
    }

    private static Rect BarRect(Rect screen, TaskbarEdge edge)
    {
        const double thickness = 16;
        return edge switch
        {
            TaskbarEdge.Top => new Rect(screen.X, screen.Y, screen.Width, thickness),
            TaskbarEdge.Left => new Rect(screen.X, screen.Y, thickness, screen.Height),
            TaskbarEdge.Right => new Rect(screen.Right - thickness, screen.Y, thickness, screen.Height),
            _ => new Rect(screen.X, screen.Bottom - thickness, screen.Width, thickness),
        };
    }

    /// <summary>The edge nearest to <paramref name="point"/>, or null outside the screen.</summary>
    private TaskbarEdge? EdgeAt(Point point)
    {
        Rect screen = ScreenRect();
        if (!Inflate(screen, 6).Contains(point))
        {
            return null;
        }

        // Normalise so a wide screen does not favour top/bottom.
        double left = (point.X - screen.Left) / screen.Width, right = 1 - left;
        double top = (point.Y - screen.Top) / screen.Height, bottom = 1 - top;
        double min = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        return min == top ? TaskbarEdge.Top : min == bottom ? TaskbarEdge.Bottom : min == left ? TaskbarEdge.Left : TaskbarEdge.Right;
    }

    private static Rect Inflate(Rect rect, double by)
    {
        rect.Inflate(by, by);
        return rect;
    }

    private static double Clamp(double value, double min, double max) => max < min ? min : Math.Clamp(value, min, max);
}
