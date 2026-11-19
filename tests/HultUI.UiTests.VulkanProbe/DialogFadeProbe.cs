using System.Globalization;
using System.Runtime.InteropServices;
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Dialogs;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

/// <summary>
/// --dialog-fade (with --renderer=default: GL): a page of rows in a 1440x900 window (2160x1350 pixels at 150 %), a
/// dialog the command palette's size opened over it and its fade drawn frame by frame at 1/120 s, then closed and
/// opened again. Prints the process's local video memory (DXGI) before the first open, after it and after the second,
/// the window's and the panel's size in pixels, and the textures, render targets and stencils the window's Skia
/// context holds then (Windows). A fade that drew through a window-sized layer left that layer's texture in Skia's
/// cache for the window's life: 18 MB at 2160x1350 on NVIDIA's GL.
/// </summary>
internal static class DialogFadeProbe
{
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1 / 120.0);

    public static int Run(RendererKind renderer)
    {
        using var app = Foreground.NoActivate(new Application("vulkan-probe", 1440, 900, renderer: renderer));
        var rows = new Element[30];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = Flex.Row(gap: 12).Height(28f).AlignItems(AlignItems.Center).Children(
                Box.New().Width(10f).Height(10f).CornerRadius(5f).Background(Color.FromHex("#8888CC")),
                Text.New($"Commit {i}: a message about the change").FontSize(13f).TextColor(Color.FromHex("#E0E0E0")));
        var root = Flex.Column(gap: 2).FlexGrow(1).Padding(new Thickness(16)).Background(Color.FromHex("#1E1F2A")).Children(rows);
        app.SetRoot(root);
        for (int i = 0; i < 12; i++)
            app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);

        var window = app.PrimaryWindow;
        double before = LocalMB();
        Element? panel = null;
        IDialogController<bool>? dialog = null;
        _ = Dialog.Show<bool>(root.OverlayHost!, c => { dialog = c; return panel = Panel(); });
        Fade(app);
        double first = LocalMB();
        string skiaFirst = Skia(app);

        dialog!.Close(false);
        Fade(app);
        _ = Dialog.Show<bool>(root.OverlayHost!, c => { dialog = c; return panel = Panel(); });
        Fade(app);
        double second = LocalMB();

        float scale = window.DisplayScale;
        var box = panel!.Layout.AbsoluteBorderBox;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"probe: dialog-fade window {MathF.Round(window.LayoutWidth * scale)}x{MathF.Round(window.LayoutHeight * scale)} panel {MathF.Round(box.Width * scale)}x{MathF.Round(box.Height * scale)} local before {before:0.00} first {first:0.00} second {second:0.00} MB; skia {skiaFirst}"));
        dialog!.Close(false);
        return 0;
    }

    // The command palette's panel: 600x460, rounded, clipped to its corners, opaque.
    private static Element Panel()
    {
        var list = Flex.Column(gap: 2).Padding(new Thickness(8));
        for (int i = 0; i < 12; i++)
            list.AddChild(Box.New().Height(30f).CornerRadius(6f).Padding(new Thickness(6, 10, 6, 10))
                .Background(i == 0 ? Color.FromHex("#3A3B55") : Color.FromHex("#26273A"))
                .Children(Text.New($"Commit {i}: a message about the change").FontSize(13f).TextColor(Color.FromHex("#E0E0E0"))));
        var panel = Box.New().Width(600f).Height(460f).CornerRadius(8f).Background(Color.FromHex("#26273A"))
            .BorderColor(Color.FromHex("#3A3B55")).BorderWidth(1f).Children(list);
        panel.Box.OverflowX = Overflow.Hidden;
        panel.Box.OverflowY = Overflow.Hidden;
        return panel;
    }

    // The fade (0.12 s) and a few frames past it, each frame forced: the probe's own clock.
    private static void Fade(Application app)
    {
        for (int i = 0; i < 24; i++)
            app.RunFrame(Frame, forceRender: true);
    }

    private static string Skia(Application app)
    {
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var platformWindow = typeof(Window).GetProperty("PlatformWindow", Private)!.GetValue(app.PrimaryWindow)!;
        var surface = platformWindow.GetType().GetField("_surface", Private)?.GetValue(platformWindow)
                      ?? platformWindow.GetType().GetField("_vulkanSurface", Private)?.GetValue(platformWindow);
        if (surface?.GetType().GetField("_grContext", Private)?.GetValue(surface) is not SkiaSharp.GRContext gr)
            return "no context";
        gr.GetResourceCacheUsage(out int count, out long bytes);
        return string.Create(CultureInfo.InvariantCulture, $"cache {count} resources {bytes / 1048576.0:0.00} MB");
    }

    // ── The process's local video memory, from DXGI (IDXGIAdapter3::QueryVideoMemoryInfo): the kernel's count of
    //    what this process holds on the adapter, whatever API allocated it. Raw vtable slots: IUnknown
    //    QueryInterface 0, Release 2; IDXGIFactory1::EnumAdapters1 12; IDXGIAdapter3::QueryVideoMemoryInfo 14.

    [StructLayout(LayoutKind.Sequential)]
    private struct VideoMemoryInfo { public ulong Budget, CurrentUsage, AvailableForReservation, CurrentReservation; }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters1(nint self, uint index, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QueryInterface(nint self, in Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint Release(nint self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QueryVideoMemoryInfo(nint self, uint node, int group, out VideoMemoryInfo info);

    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(in Guid riid, out nint factory);

    private static T Slot<T>(nint com, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(com), slot * IntPtr.Size));

    /// <summary>Local (dedicated) video memory this process uses, summed over the adapters, in MB; NaN off Windows.</summary>
    private static double LocalMB()
    {
        if (!OperatingSystem.IsWindows()) return double.NaN;
        var iidFactory1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        var iidAdapter3 = new Guid("645967a4-1392-4310-a798-8053ce3e93fd");
        if (CreateDXGIFactory1(in iidFactory1, out nint factory) < 0 || factory == 0) return double.NaN;
        ulong total = 0;
        try
        {
            for (uint i = 0; i < 8; i++)
            {
                if (Slot<EnumAdapters1>(factory, 12)(factory, i, out nint adapter) < 0) break;
                if (Slot<QueryInterface>(adapter, 0)(adapter, in iidAdapter3, out nint adapter3) >= 0)
                {
                    if (Slot<QueryVideoMemoryInfo>(adapter3, 14)(adapter3, 0, 0, out var local) >= 0)
                        total += local.CurrentUsage;
                    Slot<Release>(adapter3, 2)(adapter3);
                }
                Slot<Release>(adapter, 2)(adapter);
            }
        }
        finally { Slot<Release>(factory, 2)(factory); }
        return total / 1048576.0;
    }
}
