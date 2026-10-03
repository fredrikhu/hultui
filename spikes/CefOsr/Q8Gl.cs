using System.Diagnostics;
using System.Runtime.InteropServices;
using CefOsr.Capi;
using static CefOsr.Native;

namespace CefOsr;

/// <summary>
/// Question 8 in GL: in on_accelerated_paint, on CEF's thread, import the dmabuf as an
/// EGLImage, blit it into a texture of ours, fence, and return. Checks the pixels, whether CEF
/// keeps the fds, and (--bench) what each path costs at 1650×1072 and 60 frames a second.
/// </summary>
internal static unsafe partial class Q8
{
    static uint _srcTex, _srcFbo, _dstTex, _dstFbo;
    static int _dstW, _dstH;
    static bool _glReady;
    static string _glInfo = "";

    /// <summary>The copy, inside the callback. Returns false if the import failed.</summary>
    static bool CopyFrame(cef_accelerated_paint_info_t* info)
    {
        int w = info->extra.coded_size.width, h = info->extra.coded_size.height;
        if (!_glReady)
        {
            _glInfo = Gl.Init();
            uint t, f;
            Gl.glGenTextures(1, &t); _srcTex = t;
            Gl.glGenFramebuffers(1, &f); _srcFbo = f;
            _glReady = true;
        }
        if (w != _dstW || h != _dstH)
        {
            (_dstTex, _dstFbo) = Gl.Target(w, h);
            _dstW = w; _dstH = h;
        }
        var image = Gl.Import(info->planes_0.fd, w, h, info->planes_0.stride, info->planes_0.offset, info->modifier);
        if (image == 0) return false;
        Gl.glBindTexture(Gl.GL_TEXTURE_2D, _srcTex);
        Gl.glEGLImageTargetTexture2DOES(Gl.GL_TEXTURE_2D, image);
        Gl.glBindFramebuffer(Gl.GL_READ_FRAMEBUFFER, _srcFbo);
        Gl.glFramebufferTexture2D(Gl.GL_READ_FRAMEBUFFER, Gl.GL_COLOR_ATTACHMENT0, Gl.GL_TEXTURE_2D, _srcTex, 0);
        Gl.glBindFramebuffer(Gl.GL_DRAW_FRAMEBUFFER, _dstFbo);
        Gl.glBlitFramebuffer(0, 0, w, h, 0, 0, w, h, Gl.GL_COLOR_BUFFER_BIT, Gl.GL_NEAREST);
        // The buffer goes back to CEF's pool when we return: the copy must be done by then.
        var sync = Gl.glFenceSync(Gl.GL_SYNC_GPU_COMMANDS_COMPLETE, 0);
        Gl.glClientWaitSync(sync, Gl.GL_SYNC_FLUSH_COMMANDS_BIT, 1_000_000_000);
        Gl.glDeleteSync(sync);
        Gl.eglDestroyImageKHR(Gl.Display, image);
        return true;
    }

    static byte[] ReadDst()
    {
        var px = new byte[_dstW * _dstH * 4];
        Gl.glBindFramebuffer(Gl.GL_READ_FRAMEBUFFER, _dstFbo);
        fixed (byte* p = px) Gl.glReadPixels(0, 0, _dstW, _dstH, Gl.GL_RGBA, Gl.GL_UNSIGNED_BYTE, p);
        // GL's rows run bottom-up from the framebuffer's origin; the blit kept the dmabuf's
        // row order, so row 0 here is the dmabuf's row 0: the page's top.
        for (int i = 0; i < px.Length; i += 4) (px[i], px[i + 2]) = (px[i + 2], px[i]);   // RGBA → BGRA for Png
        return px;
    }

    [DllImport("libc", SetLastError = true)] static extern int fcntl(int fd, int cmd);

    static int RunGl(Options o)
    {
        if (Host.Initialize([], o) != 1) return 1;
        Host.Pump(() => Host.ContextReady, 15000);

        int frames = 0, failures = 0, lastFd = -1, closedAfter = 0, openAfter = 0;
        double copyMs = 0, maxCopyMs = 0;
        byte[]? first = null;
        var fds = new HashSet<int>();
        Host.AcceleratedPaint += (b, type, p) =>
        {
            var info = (cef_accelerated_paint_info_t*)p;
            if (lastFd >= 0) { if (fcntl(lastFd, 1 /* F_GETFD */) == -1) closedAfter++; else openAfter++; }
            var sw = Stopwatch.StartNew();
            if (!CopyFrame(info)) { failures++; Log($"import failed: EGL 0x{Gl.LastEglError:X}"); return; }
            double ms = sw.Elapsed.TotalMilliseconds;
            if (frames++ > 0) { copyMs += ms; maxCopyMs = Math.Max(maxCopyMs, ms); }
            lastFd = info->planes_0.fd;
            fds.Add(lastFd);
            if (frames == 1) Log($"first frame: {_glInfo}; import + blit + fence {ms:F2} ms (first, includes context creation)");
            if (frames == 8 && first == null) first = ReadDst();
        };
        Host.ViewWidth = 800; Host.ViewHeight = 600; Host.Scale = 1.5f;
        Host.CreateBrowser(Host.DataUrl(StillPageForQ8), sharedTexture: true, sync: false);
        Host.Wait(1500);
        // A still page stops: invalidate a few times to get frames.
        for (int i = 0; i < 10; i++)
        {
            Host.PostUi(() => { });
            Host.Wait(50);
        }
        if (first == null) Log($"only {frames} frames from the still page");
        else
        {
            Png.SaveBgra("results/q8-gl-imported.png", Marshal.UnsafeAddrOfPinnedArrayElement(first, 0), _dstW, _dstH);
            string Px(int x, int y) { int i = (y * _dstW + x) * 4; return $"({x},{y}) BGRA {first[i]:X2} {first[i + 1]:X2} {first[i + 2]:X2} {first[i + 3]:X2}"; }
            Log($"read back from our texture {_dstW}x{_dstH}: {Px(10, 10)}; {Px(149, 149)}; {Px(150, 150)}; {Px(_dstW - 10, _dstH - 10)}; {Px(_dstW - 151, _dstH - 151)}");
        }
        Log($"{frames} frames copied, {failures} imports failed; import + blit + fence avg {(frames > 1 ? copyMs / (frames - 1) : 0):F2} ms, max {maxCopyMs:F2} ms; " +
            $"fds seen {fds.Count} ({string.Join(",", fds)}); the previous callback's fd at the next callback: closed {closedAfter}, still open {openAfter}");
        Host.Wait(300);
        Q1.Shutdown();
        return 0;
    }

    // Question 3's page, but animating a 1-pixel corner so that it keeps painting.
    const string StillPageForQ8 = """
        <html><head><style>
        body { margin:0; background:#fff; font: 24px sans-serif; }
        #r { position:absolute; left:0; top:0; width:100px; height:100px; background:#f00; }
        #b { position:absolute; right:0; bottom:0; width:100px; height:100px; background:#00f; }
        #tick { position:absolute; left:400px; top:300px; width:1px; height:1px; animation: t 1s infinite; }
        @keyframes t { from { background:#fff } to { background:#fefefe } }
        p { position:absolute; left:120px; top:20px; margin:0; }
        </style></head><body><div id="r"></div><div id="b"></div><div id="tick"></div><p>HultUI · CEF · zero-copy</p></body></html>
        """;

    static int Bench(Options o)
    {
        if (Host.Initialize([], o) != 1) return 1;
        Host.Pump(() => Host.ContextReady, 15000);
        Host.ViewWidth = 1650; Host.ViewHeight = 1072; Host.Scale = 1f;
        var me = Process.GetCurrentProcess();
        string mode = "";
        int frames = 0;
        byte[] sink = new byte[1650 * 1072 * 4];
        uint upTex = 0;
        Host.Paint += (b, type, w, h, buf, n, d) =>
        {
            if (type != cef_paint_element_type_t.PET_VIEW) return;
            frames++;
            // The pixel path: what PixelFrameSink takes, a copy of the frame.
            Marshal.Copy(buf, sink, 0, Math.Min(sink.Length, w * h * 4));
            if (mode == "paint+upload")
            {
                // And what Skia's re-upload costs on top: one texture upload per frame.
                if (!_glReady) { _glInfo = Gl.Init(); _glReady = true; }
                if (upTex == 0) { (upTex, _) = Gl.Target(w, h); }
                Gl.glBindTexture(Gl.GL_TEXTURE_2D, upTex);
                fixed (byte* s = sink) Gl.glTexSubImage2D(Gl.GL_TEXTURE_2D, 0, 0, 0, w, h, Gl.GL_BGRA, Gl.GL_UNSIGNED_BYTE, s);
                Gl.glFlush();
            }
        };
        Host.AcceleratedPaint += (b, type, p) =>
        {
            if (type != cef_paint_element_type_t.PET_VIEW) return;
            frames++;
            if (mode == "shared-vk") VkCopyFrame((cef_accelerated_paint_info_t*)p);
            else CopyFrame((cef_accelerated_paint_info_t*)p);
        };

        foreach (var m in new[] { "paint", "paint+upload", "shared", "shared-vk" })
        {
            mode = m;
            nint browser = 0;
            Action<nint> created = b => { if (browser == 0) { AddRef((void*)b); browser = b; } };
            Host.AfterCreated += created;
            Host.CreateBrowser(Host.DataUrl(Q3.AnimatedPage), sharedTexture: m.StartsWith("shared"), sync: false);
            Host.Pump(() => browser != 0, 10000);
            Host.Wait(2000);
            double Theirs() => Procs.Children().Sum(c => CpuMs(c.Pid));
            me.Refresh();
            var c0 = me.TotalProcessorTime; var t0 = Theirs(); frames = 0;
            var clock = Stopwatch.StartNew();
            Host.Wait(5000);
            me.Refresh();
            double s = clock.Elapsed.TotalSeconds;
            Log($"{m,-13} 1650x1072: {frames / s:F1} frames/s; our process {(me.TotalProcessorTime - c0).TotalMilliseconds / s:F0} ms/s CPU, " +
                $"CEF's processes {(Theirs() - t0) / s:F0} ms/s");
            Host.AfterCreated -= created;
            Host.Close((cef_browser_t*)browser);
            Host.Wait(500);
        }
        Q1.Shutdown();
        return 0;
    }
}
