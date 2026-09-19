using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Ream.App.Services;

/// <summary>Turns the blur behind the window on or off.</summary>
internal interface IWindowBackdrop
{
    void Apply(WindowAppearance appearance);
}

/// <summary>What to ask Windows for. Pure, so it can be checked without a window.</summary>
internal readonly record struct BlurPlan(int AccentState)
{
    public const int Disabled = 0;
    public const int BlurBehind = 3;

    public static BlurPlan For(WindowAppearance appearance, int osBuild) =>
        new(appearance.Blur && osBuild >= BlurSupport.MinimumBuild ? BlurBehind : Disabled);
}

/// <summary>
/// The real thing. The window draws its own frame and its own see-through canvas; this only adds the blur behind
/// it, through the (undocumented but long-stable) window composition attribute. It never throws: if Windows
/// won't do it, the window just isn't blurred.
/// </summary>
internal sealed class WindowBackdrop(Window window) : IWindowBackdrop
{
    private const int AccentPolicyAttribute = 19;

    public void Apply(WindowAppearance appearance)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var plan = BlurPlan.For(appearance, Environment.OSVersion.Version.Build);
        var policy = new AccentPolicy { AccentState = plan.AccentState };

        int size = Marshal.SizeOf<AccentPolicy>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, buffer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = AccentPolicyAttribute,
                Data = buffer,
                SizeOfData = size,
            };
            SetWindowCompositionAttribute(handle, ref data);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Debug.WriteLine($"Blur behind not applied: {ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr window, ref WindowCompositionAttributeData data);
}
