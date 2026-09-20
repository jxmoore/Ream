using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ream.Tests;

/// <summary>
/// Runs real views in-process on one dedicated UI thread, with the app's real resources loaded,
/// so editor behaviour can be tested without driving anyone's keyboard or screen.
/// </summary>
internal static class Ui
{
    private static readonly Lazy<Dispatcher> Host = new(StartHost);

    private static Dispatcher StartHost()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();

        // WPF's Application constructor queues a call to OnStartup, and the real one loads the user's config and
        // documents, applies their theme and shows a real window - none of which a test may ever do. This switch
        // makes it return immediately, leaving just the app's resources (palette, control styles) loaded.
        AppContext.SetSwitch("Ream.SkipStartup", true);

        var thread = new Thread(() =>
        {
            var app = new Ream.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "Ream test UI thread" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!ready.Wait(HostStartTimeout))
            throw new TimeoutException($"The test UI thread did not start within {HostStartTimeout.TotalSeconds:0}s.");
        return dispatcher!;
    }

    // Settle() pumps the UI thread, which would run another test's queued work nested inside this one.
    // Keyboard focus is process-wide, so overlapping tests interfere; run them strictly one at a time.
    private static readonly object OneTestAtATime = new();

    // A stuck test must fail loudly, naming who is stuck, instead of hanging the whole run (and CI) forever.
    private static readonly TimeSpan HostStartTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(60);
    private static string? _insideNow;

    public static void Run(Action action)
    {
        if (!Monitor.TryEnter(OneTestAtATime, LockTimeout))
            throw new TimeoutException($"Waited {LockTimeout.TotalSeconds:0}s to use the UI thread. It is held by:\n{_insideNow}");

        Exception? error = null;
        bool finished = false;
        try
        {
            _insideNow = new System.Diagnostics.StackTrace(1, fNeedFileInfo: false).ToString();
            Host.Value.Invoke(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
                finished = true;
            }, DispatcherPriority.Normal, CancellationToken.None, RunTimeout);
        }
        finally
        {
            if (finished) _insideNow = null;
            Monitor.Exit(OneTestAtATime);
        }

        if (!finished)
            throw new TimeoutException($"A UI test did not finish within {RunTimeout.TotalSeconds:0}s:\n{_insideNow}");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>Lets queued layout, loaded, and render work run before the test looks at results.</summary>
    public static void Settle() =>
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { });

    /// <summary>Shows content in an off-screen window that never takes focus from whatever the user is doing.</summary>
    public static Window Show(FrameworkElement content, double width = 620, double height = 720)
    {
        var window = new Window
        {
            Content = content,
            Width = width,
            Height = height,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            Background = (Brush)Application.Current.Resources["WindowBackgroundBrush"],
        };
        window.Show();
        Settle();
        return window;
    }

    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    public static T? Ancestor<T>(DependencyObject start) where T : DependencyObject
    {
        for (var d = VisualTreeHelper.GetParent(start); d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is T match) return match;
        return null;
    }

    /// <summary>Renders an element to a bitmap so a test can look at the pixels (and their alpha) it really draws.</summary>
    public static RenderTargetBitmap Render(FrameworkElement element)
    {
        var target = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(element);
        return target;
    }

    /// <summary>One pixel as (alpha, r, g, b), un-premultiplied so colors can be compared with brush colors.</summary>
    public static (byte A, byte R, byte G, byte B) PixelAt(BitmapSource bitmap, int x, int y)
    {
        var px = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        byte a = px[3];
        if (a == 0) return (0, 0, 0, 0);

        byte Un(byte c) => (byte)Math.Min(255, (int)Math.Round(c * 255.0 / a));
        return (a, Un(px[2]), Un(px[1]), Un(px[0]));
    }

    /// <summary>A point in the middle of an element's empty space, in the coordinates of the window being rendered.</summary>
    public static (int X, int Y) CenterOf(FrameworkElement element, FrameworkElement window, double xFraction = 0.5)
    {
        var p = element.TranslatePoint(new Point(element.ActualWidth * xFraction, element.ActualHeight / 2), window);
        return ((int)p.X, (int)p.Y);
    }

    public static void RenderToPng(FrameworkElement element, string path)
    {
        var target = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
