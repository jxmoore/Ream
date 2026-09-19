using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Ream.App.Services;

/// <summary>Makes the window's own chrome - title bar, border, blur behind it - match the theme.</summary>
internal interface IWindowBackdrop
{
    void Apply(WindowAppearance appearance);
}

/// <summary>What to tell the desktop window manager for one appearance on one Windows build. Null means "leave it".</summary>
internal readonly record struct ChromePlan(
    bool? DarkMode,
    uint? CaptionColor,
    uint? TextColor,
    uint? BorderColor,
    int? BackdropType,
    bool ExtendFrameIntoClient,
    bool TransparentCanvas);

/// <summary>
/// Decides which window-chrome features exist on this Windows build and what to set them to. Pure, so it can be
/// checked without a window: each feature arrived in a particular build and older ones must simply be skipped.
/// </summary>
internal static class ChromePlanner
{
    // COLORREF sentinels from dwmapi.h.
    public const uint ColorDefault = 0xFFFFFFFF;

    public const int BackdropNone = 1;
    public const int BackdropAcrylic = 3;

    public const int DarkModeBuild = 19041;     // Windows 10 2004
    public const int ChromeColorBuild = 22000;  // Windows 11 21H2: caption/text/border colors
    public const int BackdropBuild = SystemBackdropSupport.MinimumBuild; // Windows 11 22H2

    public static ChromePlan Plan(WindowAppearance appearance, int osBuild)
    {
        bool acrylic = appearance.SeeThrough && osBuild >= BackdropBuild;

        return new ChromePlan(
            DarkMode: osBuild >= DarkModeBuild ? !appearance.IsLight : null,

            // Solid canvas: a title bar the same color as it. See-through: leave the bar to the same backdrop
            // material, since a solid caption color can't be translucent.
            CaptionColor: osBuild >= ChromeColorBuild ? (acrylic ? ColorDefault : ToColorRef(appearance.Canvas)) : null,
            TextColor: osBuild >= ChromeColorBuild ? ToColorRef(appearance.Text) : null,
            BorderColor: osBuild >= ChromeColorBuild ? (acrylic ? ColorDefault : ToColorRef(appearance.Canvas)) : null,

            BackdropType: osBuild >= BackdropBuild ? (acrylic ? BackdropAcrylic : BackdropNone) : null,
            ExtendFrameIntoClient: acrylic,
            TransparentCanvas: acrylic);
    }

    /// <summary>Win32 COLORREF: 0x00BBGGRR.</summary>
    public static uint ToColorRef(Color color) => (uint)(color.B << 16 | color.G << 8 | color.R);
}

/// <summary>The real thing: sets the attributes on a WPF window's handle. Never throws - a missing feature just doesn't apply.</summary>
internal sealed class WindowBackdrop(Window window) : IWindowBackdrop
{
    private const int UseImmersiveDarkMode = 20;
    private const int BorderColorAttribute = 34;
    private const int CaptionColorAttribute = 35;
    private const int TextColorAttribute = 36;
    private const int SystemBackdropTypeAttribute = 38;

    public void Apply(WindowAppearance appearance)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var plan = ChromePlanner.Plan(appearance, Environment.OSVersion.Version.Build);

        try
        {
            if (plan.DarkMode is { } dark) Set(handle, UseImmersiveDarkMode, dark ? 1 : 0);
            if (plan.CaptionColor is { } caption) Set(handle, CaptionColorAttribute, unchecked((int)caption));
            if (plan.TextColor is { } text) Set(handle, TextColorAttribute, unchecked((int)text));
            if (plan.BorderColor is { } border) Set(handle, BorderColorAttribute, unchecked((int)border));
            if (plan.BackdropType is { } backdrop) Set(handle, SystemBackdropTypeAttribute, backdrop);

            // Blur shows through only where the window is both extended into the client area and drawn transparent.
            var margins = plan.ExtendFrameIntoClient ? new Margins(-1, -1, -1, -1) : default;
            DwmExtendFrameIntoClientArea(handle, ref margins);

            if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target)
                target.BackgroundColor = plan.TransparentCanvas ? Colors.Transparent : appearance.Canvas;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Debug.WriteLine($"Window chrome not applied: {ex.Message}");
        }
    }

    private static void Set(IntPtr handle, int attribute, int value) =>
        DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins(int left, int right, int top, int bottom)
    {
        public int Left = left, Right = right, Top = top, Bottom = bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
}
