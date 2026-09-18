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

        ready.Wait();
        return dispatcher!;
    }

    public static void Run(Action action)
    {
        Exception? error = null;
        Host.Value.Invoke(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
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
