using System.Diagnostics;
using System.Runtime.InteropServices;
using HultUI.Rendering.Skia;

namespace HultUI.WinForms.Interop;

/// <summary>
/// Warms the graphics stack on a worker thread, so the first
/// <see cref="HultUIControl"/> to appear does not pay for it.
/// </summary>
/// <remarks>
/// <para>Almost all of a first control's cost is one-time and process-wide: loading
/// <c>opengl32.dll</c> and the vendor's ICD, which Windows defers until the first GL
/// call. Measured on this repo's sample, the first surface spent ~130 ms inside context
/// creation and every later one ~4 ms — in the same code, with the driver already
/// resident.</para>
///
/// <para>That load can be forced from any thread, because it is the process that is
/// warmed, not the caller. So a throwaway hidden window is given a pixel format and a
/// context on a worker, made current long enough to bring the driver up, and torn down
/// again. A window of its own, never the real control's: a pixel format can be set only
/// once per window handle, so warming on the handle that will later render would spend
/// the one chance to choose one.</para>
///
/// <para>The text pipeline is warmed alongside it. It needs no GL at all — fonts and
/// shaping — and <see cref="SkiaGLRenderSurface"/> already accepts a prebuilt one, which
/// is why that parameter exists.</para>
///
/// <para>What cannot move is Skia's <c>GRContext</c>: it belongs to the GL context that
/// created it, and a WGL context is current on one thread at a time, so it has to be
/// built on the UI thread against the control's own context. That is the small tail of
/// the cost, not the expensive part.</para>
/// </remarks>
internal static class GraphicsPreload
{
    private const int WS_POPUP = unchecked((int)0x80000000);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int exStyle, string cls, string? name, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);

    private static Thread? _worker;
    private static SkiaTextServices? _text;
    private static readonly Lock Gate = new();

    /// <summary>Milliseconds the worker spent, for diagnostics. Zero until it finishes.</summary>
    internal static double ElapsedMilliseconds { get; private set; }

    /// <summary>Whether the worker got the driver up. False when it was never started, or failed.</summary>
    internal static bool Succeeded { get; private set; }

    internal static void Begin()
    {
        lock (Gate)
        {
            if (_worker is not null) return;   // already warming, or already warm

            _worker = new Thread(Warm)
            {
                IsBackground = true,   // must never hold up process exit
                Name = "HultUI graphics preload",
            };
            _worker.Start();
        }
    }

    /// <summary>
    /// Waits for a preload started earlier, so the driver is never being loaded on two
    /// threads at once. A no-op when none was started.
    /// </summary>
    /// <remarks>
    /// Joining rather than racing costs nothing: the loader serialises the DLL load
    /// anyway, so a UI thread that got there first would simply block for the same time
    /// with none of the head start. Waiting makes the timing predictable instead.
    /// </remarks>
    internal static void Join()
    {
        Thread? worker;
        lock (Gate) worker = _worker;
        worker?.Join();
    }

    /// <summary>
    /// The prebuilt text pipeline, handed over once. Null when no preload ran, which is
    /// the signal for a surface to build its own.
    /// </summary>
    internal static SkiaTextServices? TakeText()
    {
        lock (Gate)
        {
            var text = _text;
            _text = null;
            return text;
        }
    }

    private static void Warm()
    {
        var clock = Stopwatch.StartNew();
        IntPtr window = IntPtr.Zero, dc = IntPtr.Zero, context = IntPtr.Zero;

        try
        {
            // "STATIC" is a pre-registered system class, so there is no class to register
            // and no window procedure of ours to keep alive. Never shown.
            window = CreateWindowExW(0, "STATIC", null, WS_POPUP, 0, 0, 1, 1,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (window == IntPtr.Zero) return;

            (context, dc) = Wgl.CreateContext(window);

            // Making it current is what actually brings the driver up: creating a context
            // can be satisfied without the ICD having finished initialising.
            if (!Wgl.wglMakeCurrent(dc, context)) return;

            // One real call, so a driver that initialises lazily has to finish.
            _ = Wgl.GetProcAddress("glGetString");

            var text = new SkiaTextServices();
            lock (Gate) _text = text;

            Succeeded = true;
        }
        catch (Exception ex)
        {
            // A preload is an optimisation and nothing more: a machine that cannot do it
            // must still start, paying the cost the ordinary way.
            Debug.WriteLine($"[HultUI] graphics preload failed, continuing without it: {ex.Message}");
        }
        finally
        {
            if (context != IntPtr.Zero)
            {
                Wgl.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
                Wgl.wglDeleteContext(context);
            }
            if (window != IntPtr.Zero)
            {
                if (dc != IntPtr.Zero) Wgl.ReleaseDC(window, dc);
                // DestroyWindow only works from the thread that created the window, which
                // is this one — the whole sequence deliberately lives on the worker.
                DestroyWindow(window);
            }
            ElapsedMilliseconds = clock.Elapsed.TotalMilliseconds;
        }
    }
}
