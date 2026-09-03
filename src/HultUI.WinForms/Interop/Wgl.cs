using System.Runtime.InteropServices;

namespace HultUI.WinForms.Interop;

/// <summary>
/// The slice of WGL needed to put an OpenGL context on a window handle we do not own
/// the creation of. Deliberately small: HultUI's Skia surface resolves every GL entry
/// point it needs through <see cref="GetProcAddress"/>, so nothing here knows about
/// OpenGL beyond making a context current and swapping it.
/// </summary>
internal static class Wgl
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormatDescriptor
    {
        public ushort Size, Version;
        public uint Flags;
        public byte PixelType, ColorBits, RedBits, RedShift, GreenBits, GreenShift,
            BlueBits, BlueShift, AlphaBits, AlphaShift, AccumBits, AccumRedBits,
            AccumGreenBits, AccumBlueBits, AccumAlphaBits, DepthBits, StencilBits,
            AuxBuffers, LayerType, Reserved;
        public uint LayerMask, VisibleMask, DamageMask;
    }

    private const uint DrawToWindow = 0x04;
    private const uint SupportOpenGL = 0x20;
    private const uint DoubleBuffer = 0x01;

    [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern int ChoosePixelFormat(IntPtr hdc, ref PixelFormatDescriptor pfd);
    [DllImport("gdi32.dll")] private static extern bool SetPixelFormat(IntPtr hdc, int format, ref PixelFormatDescriptor pfd);
    [DllImport("gdi32.dll")] private static extern int GetPixelFormat(IntPtr hdc);
    [DllImport("gdi32.dll")] internal static extern bool SwapBuffers(IntPtr hdc);
    [DllImport("opengl32.dll")] private static extern IntPtr wglCreateContext(IntPtr hdc);
    [DllImport("opengl32.dll")] internal static extern bool wglMakeCurrent(IntPtr hdc, IntPtr context);
    [DllImport("opengl32.dll")] internal static extern bool wglDeleteContext(IntPtr context);
    [DllImport("opengl32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr wglGetProcAddress(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr LoadLibraryA(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, EntryPoint = "GetProcAddress")]
    private static extern IntPtr GetExport(IntPtr module, string name);

    private static IntPtr _opengl32;

    /// <summary>
    /// Resolves a GL entry point for Skia.
    /// </summary>
    /// <remarks>
    /// The fallback is not defensive coding. <c>wglGetProcAddress</c> returns null for
    /// the GL 1.1 functions that live in <c>opengl32.dll</c> itself — and, per the
    /// documented contract, may return 1, 2, 3 or -1 rather than null on failure. Skia
    /// asks for those functions, so without the second lookup <c>GRContext</c> creation
    /// fails outright rather than degrading.
    /// </remarks>
    internal static IntPtr GetProcAddress(string name)
    {
        var address = wglGetProcAddress(name);
        if (address != IntPtr.Zero && address != 1 && address != 2 && address != 3 && address != -1)
            return address;

        if (_opengl32 == IntPtr.Zero) _opengl32 = LoadLibraryA("opengl32.dll");
        return _opengl32 == IntPtr.Zero ? IntPtr.Zero : GetExport(_opengl32, name);
    }

    /// <summary>
    /// Gives <paramref name="hwnd"/> a double-buffered RGBA8/stencil8 pixel format and
    /// creates a context on it.
    /// </summary>
    /// <remarks>
    /// A window's pixel format can be set only <em>once</em>, for the lifetime of that
    /// handle — which is why a control that recreates its handle has to rebuild all of
    /// this rather than reattach. The format is only chosen when the handle does not
    /// have one already, so re-entering on the same handle is safe.
    /// </remarks>
    internal static (IntPtr Context, IntPtr Dc) CreateContext(IntPtr hwnd)
    {
        var dc = GetDC(hwnd);
        if (dc == IntPtr.Zero)
            throw new InvalidOperationException("GetDC failed for the HultUI control's window handle.");

        try
        {
            if (GetPixelFormat(dc) == 0)
            {
                var descriptor = new PixelFormatDescriptor
                {
                    Size = (ushort)Marshal.SizeOf<PixelFormatDescriptor>(),
                    Version = 1,
                    Flags = DrawToWindow | SupportOpenGL | DoubleBuffer,
                    PixelType = 0,          // RGBA
                    ColorBits = 32,
                    AlphaBits = 8,
                    DepthBits = 0,          // HultUI draws in 2D; Skia wants stencil, not depth
                    StencilBits = 8,
                    LayerType = 0,          // main plane
                };

                int format = ChoosePixelFormat(dc, ref descriptor);
                if (format == 0)
                    throw new InvalidOperationException("ChoosePixelFormat found no double-buffered RGBA format.");
                if (!SetPixelFormat(dc, format, ref descriptor))
                    throw new InvalidOperationException(
                        $"SetPixelFormat failed (Win32 {Marshal.GetLastWin32Error()}). A window's pixel " +
                        "format can only be set once — is something else already drawing on this handle?");
            }

            var context = wglCreateContext(dc);
            if (context == IntPtr.Zero)
                throw new InvalidOperationException($"wglCreateContext failed (Win32 {Marshal.GetLastWin32Error()}).");

            return (context, dc);
        }
        catch
        {
            ReleaseDC(hwnd, dc);
            throw;
        }
    }

    /// <summary>
    /// Turns the driver's vsync off for the current context, returning false when the
    /// extension is unavailable.
    /// </summary>
    /// <remarks>
    /// Measured worth: with vsync on, every <c>SwapBuffers</c> blocks on the compositor
    /// — a median frame of 8.3 ms against 0.26 ms — and N controls in one form serialise
    /// into N frame intervals, because they all swap on the one UI thread. SDL sets swap
    /// interval 0 for the same reason.
    /// </remarks>
    internal static unsafe bool DisableVSync()
    {
        var address = wglGetProcAddress("wglSwapIntervalEXT");
        if (address == IntPtr.Zero || address == 1 || address == 2 || address == 3 || address == -1)
            return false;
        return ((delegate* unmanaged[Stdcall]<int, bool>)address)(0);
    }
}
