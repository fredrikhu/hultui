using System.Collections.Concurrent;
using HultUI.Core.Rendering;
using HultUI.WebView;
using HultUI.WebView.Cef;
using HultUI.WebView.Cef.Interop;

/// <summary>
/// <c>--zero-copy</c>: step 5.5 end to end without a window. CEF runs on Wayland's Ozone
/// platform (a compositor must be there), and the view's GPU renderer is driven as HultUI's
/// frame would drive it, from an EGL context of this process's own on this thread: each frame
/// blitted into a target and read back. Prints what each page showed, and how it travelled.
/// </summary>
internal static unsafe class ZeroCopy
{
    private sealed class GpuHost : IWebViewHost
    {
        public PixelFrameSink Pixels { get; } = new();
        public IGpuFrameRenderer? Renderer;
        public int Repaints;
        public void UseGpuRenderer(IGpuFrameRenderer renderer) => Renderer = renderer;
        public void RequestRepaint() => Interlocked.Increment(ref Repaints);
        public void Post(Action action) => ThreadPool.QueueUserWorkItem(_ => action());
    }

    private sealed record Page(CefView View, GpuHost Host, ConcurrentQueue<WebEngineEvent> Events, int Width, int Height)
    {
        public uint Target, Framebuffer;
    }

    public static int Run(CefEngine engine, int width, int height, int count)
    {
        // HultUI's side: a GL context of our own, surfaceless, current on this thread.
        var display = Egl.eglGetPlatformDisplay(Egl.EGL_PLATFORM_SURFACELESS_MESA, 0, null);
        int major, minor;
        Egl.eglInitialize(display, &major, &minor);
        Egl.eglBindAPI(Egl.EGL_OPENGL_ES_API);
        int* attribs = stackalloc int[] { Egl.EGL_CONTEXT_MAJOR_VERSION, 3, Egl.EGL_NONE };
        var context = Egl.eglCreateContext(display, 0, 0, attribs);
        if (Egl.eglMakeCurrent(display, 0, 0, context) == 0) { Console.WriteLine("no GL context here"); return 4; }
        var gl = new GlCalls(Egl.eglGetProcAddress);

        var pages = new List<Page>();
        for (int i = 0; i < count; i++)
        {
            var view = (CefView)engine.CreateView(new WebViewOptions());
            var host = new GpuHost();
            var events = new ConcurrentQueue<WebEngineEvent>();
            view.Event += events.Enqueue;
            view.Event += e => { if (e is WebEngineEvent.NavigationStarting s) s.Request.Complete(); };
            view.Attach(host);
            view.SetPlacement(new WebViewPlacement(0, new PixelRect(0, 0, width, height), 1f) { WindowSystem = "wayland" });
            pages.Add(new Page(view, host, events, width, height));
        }
        Console.WriteLine("renderer registered: " + pages.All(p => p.Host.Renderer is CefFrames));
        if (pages.Any(p => p.Host.Renderer is null)) return 5;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        // Pumps every page's renderer as HultUI's frames would, until done() or the time is up.
        // Synchronous throughout: the context is current on this thread only.
        bool Pump(Func<bool> done, int ms = 10_000)
        {
            var end = clock.ElapsedMilliseconds + ms;
            while (clock.ElapsedMilliseconds < end)
            {
                foreach (var p in pages) Draw(p);
                if (done()) return true;
                Thread.Sleep(16);
            }
            return false;
        }

        void Draw(Page p)
        {
            var r = p.Host.Renderer!;
            if (!r.TryGetFrameSize(out int w, out int h)) return;
            if (p.Target == 0) (p.Target, p.Framebuffer) = gl.Target(w, h);
            r.Render(Egl.eglGetProcAddress, new GpuFrameTarget(p.Framebuffer, p.Target, GlCalls.GL_RGBA8), w, h);
        }

        (byte R, byte G, byte B) At(Page p, int x, int y)
        {
            byte* px = stackalloc byte[4];
            gl.BindFramebuffer(GlCalls.GL_READ_FRAMEBUFFER, p.Framebuffer);
            gl.ReadPixels(x, y, 1, 1, GlCalls.GL_RGBA, GlCalls.GL_UNSIGNED_BYTE, px);
            return (px[0], px[1], px[2]);
        }

        foreach (var p in pages) { p.Host.Renderer!.OnModeDecided(GpuSinkMode.Gpu); p.Host.Renderer!.PrepareGL(Egl.eglGetProcAddress); }
        if (!Pump(() => pages.All(p => p.Events.Any(e => e is WebEngineEvent.Ready or WebEngineEvent.Failed)), 30_000))
        { Console.WriteLine("never ready"); return 6; }

        string[] colours = ["#00ff00", "#ff00ff", "#0000ff", "#ffff00"];
        for (int i = 0; i < pages.Count; i++)
            pages[i].View.NavigateToString($"<title>p{i}</title><style>body{{margin:0;background:{colours[i % 4]}}}" +
                "select{position:absolute;left:10px;top:10px;font-size:20px}</style>" +
                "<select id=s><option>one</option><option>two</option><option>three</option><option>four</option></select>");

        bool Shows(Page p, (byte, byte, byte) colour) => p.Target != 0 && At(p, p.Width - 5, p.Height - 5) == colour;
        (byte, byte, byte) Rgb(string hex) => (Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));

        bool loaded = Pump(() => pages.Select((p, i) => Shows(p, Rgb(colours[i % 4]))).All(x => x));
        for (int i = 0; i < pages.Count; i++)
            Console.WriteLine($"page {i}: {(Shows(pages[i], Rgb(colours[i % 4])) ? "shows its colour" : $"shows {At(pages[i], pages[i].Width - 5, pages[i].Height - 5)}")}" +
                $", {((CefFrames)pages[i].Host.Renderer!).Path ?? "no frame drawn"}");
        if (!loaded) return 7;

        // A still page changed once more by script: its next frame must come too.
        var first = pages[0];
        first.View.ExecuteScriptAsync("document.body.style.background = '#ff8000'").Wait(5000);
        Console.WriteLine("after a change by script: " + (Pump(() => Shows(first, (0xff, 0x80, 0x00)), 5000) ? "shows it" : $"shows {At(first, first.Width - 5, first.Height - 5)}"));

        // The <select>'s list: CEF's popup, drawn over the page in the same frame.
        first.View.SendPointer(new WebPointerInput(WebPointerAction.Move, 30, 20));
        first.View.SendPointer(new WebPointerInput(WebPointerAction.Down, 30, 20, WebPointerButtons.Left, WebPointerButtons.Left, 1));
        first.View.SendPointer(new WebPointerInput(WebPointerAction.Up, 30, 20, WebPointerButtons.Left, WebPointerButtons.None, 1));
        // The list opens below the select, over the orange page: something not orange there.
        bool popup = Pump(() => At(first, 30, 60) != (0xff, 0x80, 0x00), 5000);
        Console.WriteLine($"the select's list: {(popup ? "drawn over the page" : "not seen")} ({At(first, 30, 60)}), popups {CefFramesPopups(first)}");

        // Disposed with the list still open: the browser closes after it (it used to abort here).
        foreach (var p in pages) p.View.Dispose();
        Thread.Sleep(1500);
        Console.WriteLine("done");
        Console.Out.Flush();
        return 0;
    }

    private static string CefFramesPopups(Page p) => ((CefFrames)p.Host.Renderer!).PopupFrames.ToString();
}
