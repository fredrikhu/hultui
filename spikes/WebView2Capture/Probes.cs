using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using DPoint = System.Drawing.Point;

namespace WebView2Capture;

/// <summary>The spike's questions, each answered by measurement rather than by reading docs.</summary>
internal static class Probes
{
    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private static async Task<bool> WaitUntil(Func<bool> condition, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) return false;
            await Task.Delay(2);
        }
        return true;
    }

    private static async Task<bool> NavigateAndWait(Host h, string html)
    {
        var tcs = new TaskCompletionSource<bool>();
        void OnCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult(e.IsSuccess);
        h.Web.NavigationCompleted += OnCompleted;
        h.Web.NavigateToString(html);
        await Task.WhenAny(tcs.Task, Task.Delay(5000));
        h.Web.NavigationCompleted -= OnCompleted;
        return tcs.Task.IsCompleted && tcs.Task.Result;
    }

    private static bool Matches(uint bgra, int r, int g, int b) =>
        Math.Abs((int)((bgra >> 16) & 0xFF) - r) <= 2 &&
        Math.Abs((int)((bgra >> 8) & 0xFF) - g) <= 2 &&
        Math.Abs((int)(bgra & 0xFF) - b) <= 2;

    // ── 1. Does a visual attached to no window render and capture, and where does the page land in it? ──

    public static async Task Placement(Host h, Action<string> log)
    {
        if (!await WaitUntil(() => h.PageRect.Width > 1, 5000))
            throw new InvalidOperationException("the page element was never laid out");
        log($"page element at {h.PageRect} (client px); capturing the whole root visual");

        // Capture everything, uncropped, so the page's offset inside the visual is visible.
        h.Capture.CropX = h.Capture.CropY = h.Capture.CropW = h.Capture.CropH = 0;

        // Capture delivers a frame only when the page changes, so a frame missed is never
        // sent again: the inspector re-arms itself inside the callback, before navigating.
        (int X0, int Y0, int X1, int Y1, int W, int H)? found = null;
        int blankFrames = 0;
        Action<nint, int, int, int>? inspect = null;
        inspect = (ptr, w, hgt, pitch) =>
        {
            var result = FindColour(ptr, w, hgt, pitch, 255, 0, 255);
            if (result is not null) { found = result; return; }
            if (blankFrames++ == 0) DumpBmp(ptr, w, hgt, pitch, "results/placement-first-frame.bmp");
            h.Capture.InspectNext = inspect;
        };
        h.Capture.InspectNext = inspect;

        var sw = Stopwatch.StartNew();
        await NavigateAndWait(h, "<html><body style='margin:0;background:#ff00ff'></body></html>");
        long navMs = sw.ElapsedMilliseconds;
        await WaitUntil(() => found is not null, 6000);
        h.Capture.InspectNext = null;
        log($"PLACEMENT: {blankFrames} frame(s) without the page before the first with it");

        var stats = h.Capture.TakeStats();
        if (found is null)
        {
            log($"PLACEMENT: no magenta frame within 6 s (frames captured: {stats.Frames}, navigation {navMs} ms). " +
                "A visual attached to no window target does not render, or is not captured.");
            throw new InvalidOperationException("detached visual produced no page frames");
        }

        var f = found.Value;
        log($"PLACEMENT: first page frame {sw.ElapsedMilliseconds} ms after navigate (navigation completed at {navMs} ms), " +
            $"{stats.Frames} frames; frame {f.W}x{f.H}, page at ({f.X0},{f.Y0})-({f.X1},{f.Y1})");
        h.BoundsOffsetsVisual = Math.Abs(f.X0 - h.PageRect.X) <= 1 && Math.Abs(f.Y0 - h.PageRect.Y) <= 1;
        log(h.BoundsOffsetsVisual
            ? "PLACEMENT: WebView2 offsets its visual by Bounds.X/Y inside the root visual -> capture is cropped"
            : "PLACEMENT: the page sits at the root visual's origin regardless of Bounds.X/Y");
        h.ApplyBounds();
    }

    /// <summary>Writes a top-down 32-bit BMP, half size, so a frame can be looked at.</summary>
    public static unsafe void DumpBmp(nint ptr, int w, int h, int pitch, string relativePath)
    {
        int ow = w / 2, oh = h / 2;
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", relativePath);
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        int imageSize = ow * oh * 4;
        bw.Write((ushort)0x4D42); bw.Write(54 + imageSize); bw.Write(0); bw.Write(54);
        bw.Write(40); bw.Write(ow); bw.Write(-oh); bw.Write((ushort)1); bw.Write((ushort)32);
        bw.Write(0); bw.Write(imageSize); bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);
        for (int y = 0; y < oh; y++)
        {
            uint* row = (uint*)((byte*)ptr + (y * 2) * pitch);
            for (int x = 0; x < ow; x++) bw.Write(row[x * 2] | 0xFF000000u);
        }
        Console.WriteLine($"  dumped {w}x{h} frame to {Path.GetFullPath(path)}; centre 0x{*(uint*)((byte*)ptr + (h / 2) * pitch + (w / 2) * 4):X8}");
    }

    private static unsafe (int, int, int, int, int, int)? FindColour(nint ptr, int w, int h, int pitch, int r, int g, int b)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
        {
            uint* row = (uint*)((byte*)ptr + y * pitch);
            for (int x = 0; x < w; x++)
                if (Matches(row[x], r, g, b))
                {
                    if (x < x0) x0 = x;
                    if (y < y0) y0 = y;
                    if (x > x1) x1 = x;
                    if (y > y1) y1 = y;
                }
        }
        return x1 >= 0 ? (x0, y0, x1, y1, w, h) : null;
    }

    // ── 2. How long from a change in the page to a captured frame? ──

    public static async Task Latency(Host h, Action<string> log)
    {
        await NavigateAndWait(h, "<html><body style='margin:0;background:rgb(0,0,0)'></body></html>");
        await Task.Delay(300);
        h.Capture.LogCenter = true;

        var scriptMs = new List<double>();
        var frameMs = new List<double>();
        var afterScriptMs = new List<double>();
        int misses = 0;
        for (int i = 1; i <= 40; i++)
        {
            int r = i % 2 == 0 ? 255 : 0, g = (i * 5) % 256, b = 255 - r;
            h.Capture.ClearCenterLog();
            long t0 = Stopwatch.GetTimestamp();
            await h.Web.ExecuteScriptAsync($"document.body.style.background='rgb({r},{g},{b})'");
            long t1 = Stopwatch.GetTimestamp();

            long t2 = 0;
            await WaitUntil(() =>
            {
                foreach (var (ticks, bgra) in h.Capture.SnapshotCenterLog())
                    if (Matches(bgra, r, g, b)) { t2 = ticks; return true; }
                return false;
            }, 1000);

            if (t2 == 0) { misses++; continue; }
            scriptMs.Add(Ms(t1 - t0));
            frameMs.Add(Ms(t2 - t0));
            afterScriptMs.Add(Ms(t2 - t1));
            await Task.Delay(40);
        }
        h.Capture.LogCenter = false;

        static string Dist(List<double> v)
        {
            if (v.Count == 0) return "n/a";
            v.Sort();
            return $"median {v[v.Count / 2]:F1}, p90 {v[(int)(v.Count * 0.9)]:F1}, max {v[^1]:F1} ms";
        }
        log($"LATENCY (40 colour changes, {misses} missed): script round trip {Dist(scriptMs)}");
        log($"LATENCY: change requested -> frame captured {Dist(frameMs)}");
        log($"LATENCY: script completed -> frame captured {Dist(afterScriptMs)}");
    }

    // ── 3. What does a continuously animating page cost, composited vs windowed? ──

    private const string AnimationPage = """
        <html><head><style>
        body { margin:0; height:100vh; overflow:hidden;
               background: linear-gradient(90deg, #203040, #406080, #203040); background-size: 400% 100%;
               animation: pan 3s linear infinite; }
        .spin { position:absolute; left:40%; top:30%; width:200px; height:200px; background:#e0a020;
                animation: spin 1s linear infinite; }
        @keyframes pan { from { background-position: 0% 0 } to { background-position: 400% 0 } }
        @keyframes spin { to { transform: rotate(360deg) } }
        </style></head><body><div class="spin"></div></body></html>
        """;

    private static (double Ours, double Browser) CpuMs(Host h)
    {
        double ours = Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds, browser = 0;
        foreach (var info in h.Env.GetProcessInfos())
        {
            try { browser += Process.GetProcessById(info.ProcessId).TotalProcessorTime.TotalMilliseconds; }
            catch (ArgumentException) { }
        }
        return (ours, browser);
    }

    private static async Task<(double Ours, double Browser)> MeasureCpu(Host h, int ms)
    {
        var c0 = CpuMs(h);
        var sw = Stopwatch.StartNew();
        await Task.Delay(ms);
        var c1 = CpuMs(h);
        double secs = sw.Elapsed.TotalSeconds;
        return ((c1.Ours - c0.Ours) / secs, (c1.Browser - c0.Browser) / secs);
    }

    public static async Task Animation(Host h, Action<string> log)
    {
        await NavigateAndWait(h, AnimationPage);
        await Task.Delay(700);
        h.Capture.TakeStats();
        var sw = Stopwatch.StartNew();
        var cpu = await MeasureCpu(h, 5000);
        var s = h.Capture.TakeStats();
        double secs = sw.Elapsed.TotalSeconds;
        log($"ANIMATION composited {h.PageRect.Width}x{h.PageRect.Height}: {s.Frames / secs:F1} captured fps, " +
            $"copy avg {s.CopyMsTotal / Math.Max(1, s.Frames):F2} ms max {s.CopyMsMax:F2} ms; " +
            $"CPU ours {cpu.Ours:F0} ms/s, browser processes {cpu.Browser:F0} ms/s (display {h.Window.RefreshRate} Hz)");
    }

    public static async Task Windowed(Host h, Action<string> log)
    {
        h.Cc.IsVisible = false;
        var wc = await h.Env.CreateCoreWebView2ControllerAsync(h.Hwnd);
        try
        {
            wc.Bounds = h.PageRect;
            wc.IsVisible = true;
            var tcs = new TaskCompletionSource();
            wc.CoreWebView2.NavigationCompleted += (_, _) => tcs.TrySetResult();
            wc.CoreWebView2.NavigateToString(AnimationPage);
            await Task.WhenAny(tcs.Task, Task.Delay(5000));
            await Task.Delay(700);
            var cpu = await MeasureCpu(h, 5000);
            log($"ANIMATION windowed baseline, same page and size: CPU ours {cpu.Ours:F0} ms/s, browser processes {cpu.Browser:F0} ms/s");
        }
        finally
        {
            wc.Close();
            h.Cc.IsVisible = true;
        }
    }

    // ── 4. Where does keyboard focus go in visual hosting? ──

    private static List<string> ChildWindows(nint parent)
    {
        var list = new List<string>();
        Native.EnumChildWindows(parent, (hwnd, _) =>
        {
            Native.GetWindowRect(hwnd, out var r);
            list.Add($"0x{hwnd:X} '{Native.ClassOf(hwnd)}' parent=0x{Native.GetParent(hwnd):X} visible={Native.IsWindowVisible(hwnd)} {r}");
            return true;
        }, 0);
        return list;
    }

    public static async Task FocusWindows(Host h, Action<string> log)
    {
        await NavigateAndWait(h, "<html><body><input id=i autofocus placeholder='type here'></body></html>");
        var children = ChildWindows(h.Hwnd);
        log($"FOCUS: {children.Count} child window(s) under the SDL window before focus:");
        foreach (var c in children) log("  " + c);

        int activated = 0;
        void OnActivated() => activated++;
        h.Window.Activated += OnActivated;
        h.Cc.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        await Task.Delay(300);
        log($"FOCUS: Window.Activated fired {activated} time(s) on MoveFocus into the page");
        nint focus = Native.GetFocus();
        log($"FOCUS: after MoveFocus, GetFocus()=0x{focus:X} '{(focus != 0 ? Native.ClassOf(focus) : "")}', " +
            $"foreground=0x{Native.GetForegroundWindow():X} (ours 0x{h.Hwnd:X})");
        foreach (var c in ChildWindows(h.Hwnd)) log("  " + c);
        var active = await h.Web.ExecuteScriptAsync("document.activeElement && document.activeElement.id");
        log($"FOCUS: page activeElement id = {active}, document.hasFocus() = {await h.Web.ExecuteScriptAsync("document.hasFocus()")}");

        activated = 0;
        Native.SetFocus(h.Hwnd);
        await Task.Delay(200);
        h.Window.Activated -= OnActivated;
        log($"FOCUS: Window.Activated fired {activated} time(s) on SetFocus back to ours");
        log($"FOCUS: after SetFocus(ours), GetFocus()=0x{Native.GetFocus():X}, document.hasFocus() = {await h.Web.ExecuteScriptAsync("document.hasFocus()")}");
    }

    // ── 5. What does the page's UIA root say about itself? ──

    public static async Task Accessibility(Host h, Action<string> log)
    {
        await NavigateAndWait(h, "<html><head><title>A11y probe</title></head><body><h1>Heading</h1><button>Press</button></body></html>");
        await Task.Delay(300);
        object? ap = AutomationProvider(h.Cc);
        log($"A11Y: AutomationProvider is {(ap is null ? "null" : ap.GetType().Name)}; " +
            $"IRawElementProviderSimple={ap is IRawElementProviderSimple}, IRawElementProviderFragment={ap is IRawElementProviderFragment}");
        static string Try(Func<object?> f)
        {
            try { var v = f(); return v switch { null => "null", string str => $"'{str}'", _ when v.GetType().IsCOMObject => "object", _ => v.ToString() ?? "?" }; }
            catch (Exception ex) { return ex.GetType().Name + (ex is COMException c ? $" 0x{c.HResult:X8}" : ""); }
        }
        if (ap is IRawElementProviderSimple simple)
            log($"A11Y simple: ProviderOptions={Try(() => simple.ProviderOptions)}, Name={Try(() => simple.GetPropertyValue(30005))}, " +
                $"ControlType={Try(() => simple.GetPropertyValue(30003))}, HostRawElementProvider={Try(() => simple.HostRawElementProvider)}");
        if (ap is IRawElementProviderFragment frag)
        {
            log($"A11Y fragment: Navigate(Parent)={Try(() => frag.Navigate(0))}, Navigate(FirstChild)={Try(() => frag.Navigate(3))}, " +
                $"FragmentRoot={Try(() => frag.FragmentRoot)}, BoundingRectangle={Try(() => frag.BoundingRectangle)}");
            Native.POINT origin = default;
            Native.ClientToScreen(h.Hwnd, ref origin);
            log($"A11Y: for comparison the page is at screen ({origin.X + h.PageRect.X},{origin.Y + h.PageRect.Y}) {h.PageRect.Width}x{h.PageRect.Height}");
        }
        // The environment's HWND-override route (ICoreWebView2Environment4) keyed by the focus window.
        nint focusWindow = 0;
        Native.EnumChildWindows(h.Hwnd, (hwnd, _) => { if (Native.ClassOf(hwnd) == "Chrome_WidgetWin_0") focusWindow = hwnd; return true; }, 0);
        var viaWindow = AutomationProviderForWindow(h.Env, focusWindow);
        log($"A11Y: GetAutomationProviderForWindow(0x{focusWindow:X}) -> {(viaWindow is null ? "null" : "object")}, same as controller's: {(viaWindow is not null && ap is not null ? SameIdentity(viaWindow, ap) : "n/a")}");

        // Step 4 questions: does the page provider hit-test and report focus, and does telling
        // WebView2 where its parent is put its rectangle on the screen?
        if (ap is IRawElementProviderFragment f2)
        {
            h.Cc.NotifyParentWindowPositionChanged();
            await Task.Delay(300);
            log($"A11Y after NotifyParentWindowPositionChanged: BoundingRectangle={Try(() => f2.BoundingRectangle)}, FirstChild={Try(() => f2.Navigate(3))}");
        }
        log($"A11Y: IRawElementProviderFragmentRoot={ap is IRawElementProviderFragmentRoot}");
        if (ap is IRawElementProviderFragmentRoot fr)
        {
            var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonDocument.Parse(await h.Web.ExecuteScriptAsync(
                "JSON.stringify(document.querySelector('button').getBoundingClientRect())")).RootElement.GetString()!).RootElement;
            double scale = h.Cc.RasterizationScale;
            Native.POINT origin = default;
            Native.ClientToScreen(h.Hwnd, ref origin);
            double sx = origin.X + h.PageRect.X + (json.GetProperty("x").GetDouble() + json.GetProperty("width").GetDouble() / 2) * scale;
            double sy = origin.Y + h.PageRect.Y + (json.GetProperty("y").GetDouble() + json.GetProperty("height").GetDouble() / 2) * scale;
            object? hit = null;
            string hitText = Try(() => hit = fr.ElementProviderFromPoint(sx, sy));
            log($"A11Y: ElementProviderFromPoint(button centre {sx:0},{sy:0}) -> {hitText}");
            if (hit is null)
            {
                // The stub's own rectangle is client-relative; is its hit test too?
                double cx = sx - origin.X, cy = sy - origin.Y;
                hitText = Try(() => hit = fr.ElementProviderFromPoint(cx, cy));
                log($"A11Y: ElementProviderFromPoint(client-relative {cx:0},{cy:0}) -> {hitText}");
            }
            for (int i = 0; hit is not null && i < 8; i++)
            {
                string name = hit is IRawElementProviderSimple hs ? Try(() => hs.GetPropertyValue(30005)) : "?";
                string type = hit is IRawElementProviderSimple ht ? Try(() => ht.GetPropertyValue(30003)) : "?";
                string rect = hit is IRawElementProviderFragment hf ? Try(() => hf.BoundingRectangle) : "?";
                log($"  up {i}: name={name} type={type} rect={rect} isRoot={SameIdentity(hit, ap)}");
                hit = hit is IRawElementProviderFragment up ? up.Navigate(0) : null;
            }
            h.Cc.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
            await h.Web.ExecuteScriptAsync("document.querySelector('button').focus()");
            await Task.Delay(300);
            object? focus = null;
            string focusText = Try(() => focus = fr.GetFocus());
            string focusName = focus is IRawElementProviderSimple fs ? Try(() => fs.GetPropertyValue(30005)) : "-";
            log($"A11Y: GetFocus -> {focusText} name={focusName}");
        }
    }

    /// <summary>ICoreWebView2CompositionController2::get_AutomationProvider (vtable slot 11), which
    /// the managed wrapper does not expose.</summary>
    private static unsafe object? AutomationProvider(CoreWebView2CompositionController cc)
    {
        var field = cc.GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Select(f => f.GetValue(cc)).FirstOrDefault(v => v is not null && System.Runtime.InteropServices.Marshal.IsComObject(v));
        if (field is null) return null;
        nint unk = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(field);
        Guid iid = new("0b6a3d24-49cb-4806-ba20-b5e0734a7b26");
        if (System.Runtime.InteropServices.Marshal.QueryInterface(unk, in iid, out var cc2) < 0) return null;
        nint provider;
        var get = (delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)cc2)[11];
        int hr = get(cc2, &provider);
        System.Runtime.InteropServices.Marshal.Release(cc2);
        System.Runtime.InteropServices.Marshal.Release(unk);
        return hr < 0 || provider == 0 ? null : System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(provider);
    }

    private static unsafe object? AutomationProviderForWindow(CoreWebView2Environment env, nint hwnd)
    {
        var native = env.GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Select(f => f.GetValue(env)).FirstOrDefault(v => v is not null && System.Runtime.InteropServices.Marshal.IsComObject(v));
        if (native is null || hwnd == 0) return null;
        nint unk = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(native);
        Guid iid = new("20944379-6dcf-41d6-a0a0-abc0fc50de0d");   // ICoreWebView2Environment4
        if (System.Runtime.InteropServices.Marshal.QueryInterface(unk, in iid, out var env4) < 0) return null;
        // IUnknown(3) + Environment(5) + Environment2(1) + Environment3(2) = slot 11, counted from WebView2.idl.
        nint provider;
        var get = (delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)(*(nint**)env4)[11];
        int hr = get(env4, hwnd, &provider);
        System.Runtime.InteropServices.Marshal.Release(env4);
        System.Runtime.InteropServices.Marshal.Release(unk);
        return hr < 0 || provider == 0 ? null : System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(provider);
    }

    private static bool SameIdentity(object a, object b)
    {
        nint ua = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(a), ub = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(b);
        System.Runtime.InteropServices.Marshal.Release(ua); System.Runtime.InteropServices.Marshal.Release(ub);
        return ua == ub;
    }

    // ── 6. Does a <select> popup open where the element is on screen? ──

    private static Dictionary<nint, string> BrowserTopLevelWindows(Host h)
    {
        var pids = h.Env.GetProcessInfos().Select(p => (uint)p.ProcessId).ToHashSet();
        var map = new Dictionary<nint, string>();
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pids.Contains(pid) && Native.IsWindowVisible(hwnd))
            {
                Native.GetWindowRect(hwnd, out var r);
                map[hwnd] = $"'{Native.ClassOf(hwnd)}' {r}";
            }
            return true;
        }, 0);
        return map;
    }

    private static void Click(Host h, DPoint p)
    {
        h.Cc.SendMouseInput(CoreWebView2MouseEventKind.Move, CoreWebView2MouseEventVirtualKeys.None, 0, p);
        h.Cc.SendMouseInput(CoreWebView2MouseEventKind.LeftButtonDown, CoreWebView2MouseEventVirtualKeys.LeftButton, 0, p);
        h.Cc.SendMouseInput(CoreWebView2MouseEventKind.LeftButtonUp, CoreWebView2MouseEventVirtualKeys.None, 0, p);
    }

    public static async Task SelectPopup(Host h, Action<string> log)
    {
        await NavigateAndWait(h, """
            <html><body style='margin:0'>
            <select id=s style='position:absolute;left:40px;top:60px;width:220px;height:32px;font-size:16px'>
            <option>One</option><option>Two</option><option>Three</option><option>Four</option><option>Five</option>
            </select></body></html>
            """);
        await Task.Delay(300);
        var json = JsonDocument.Parse(JsonDocument.Parse(await h.Web.ExecuteScriptAsync(
            "JSON.stringify(document.getElementById('s').getBoundingClientRect())")).RootElement.GetString()!).RootElement;
        double sx = json.GetProperty("x").GetDouble(), sy = json.GetProperty("y").GetDouble();
        double sw = json.GetProperty("width").GetDouble(), sh = json.GetProperty("height").GetDouble();
        double scale = h.Cc.RasterizationScale;
        int cx = (int)((sx + sw / 2) * scale), cy = (int)((sy + sh / 2) * scale);

        Native.POINT origin = default;
        Native.ClientToScreen(h.Hwnd, ref origin);
        int expectX = origin.X + h.PageRect.X + (int)(sx * scale), expectY = origin.Y + h.PageRect.Y + (int)((sy + sh) * scale);
        log($"POPUP: select at css ({sx},{sy}) {sw}x{sh}; expecting its popup near screen ({expectX},{expectY})");

        foreach (var parentRelative in new[] { false, true })
        {
            h.MouseParentRelative = parentRelative;
            var before = BrowserTopLevelWindows(h);
            Click(h, h.WebPointFromPagePixels(cx, cy));
            await Task.Delay(800);
            var opened = BrowserTopLevelWindows(h).Where(kv => !before.ContainsKey(kv.Key)).ToList();
            var activeId = await h.Web.ExecuteScriptAsync("document.activeElement && document.activeElement.id");
            log($"POPUP: mouse points {(parentRelative ? "parent-relative" : "bounds-relative")}: activeElement={activeId}, " +
                $"{opened.Count} new browser window(s)");
            foreach (var (hwnd, desc) in opened) log($"  0x{hwnd:X} {desc}");

            // Close whatever opened by clicking empty page space.
            Click(h, h.WebPointFromPagePixels((int)(400 * scale), (int)(300 * scale)));
            await Task.Delay(400);
            if (opened.Count > 0 || activeId == "\"s\"") break;
        }
        h.MouseParentRelative = false;
    }
}
