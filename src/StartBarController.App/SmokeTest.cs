using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace StartBarController.App;

/// <summary>
/// CI check (<c>--smoke-test &lt;folder&gt;</c>): opens every page, saves a screenshot of each and
/// exits with 0, or with 1 and error.txt if anything throws. Catches XAML and startup errors
/// that unit tests cannot.
/// </summary>
internal static class SmokeTest
{
    public static async Task RunAsync(MainWindow window, string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            foreach (string page in window.PageNames)
            {
                window.Navigate(page);
                await Settle(window);
                Save(window, Path.Combine(folder, $"{page}.png"));
            }

            window.ShowMessage("This is how messages look.", Severity.Success);
            window.Navigate("position");
            _ = window.ConfirmAsync("Restart Explorer?", "Dialogs look like this.", "Move taskbar");
            await Settle(window);
            Save(window, Path.Combine(folder, "dialog.png"));

            Application.Current.Shutdown(0);
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(folder, "error.txt"), e.ToString());
            Application.Current.Shutdown(1);
        }
    }

    private static async Task Settle(Window window)
    {
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(400);
    }

    private static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        DpiScale dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            (int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);

        // Render over the window background so the Mica/transparent areas are not black.
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            Brush background = window.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? Brushes.White;
            dc.DrawRectangle(background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }

        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }
}
