using System.Collections.Concurrent;
using HultUI.Core.Rendering;
using HultUI.WebView.Cef;
using Xunit;

namespace HultUI.WebView.Engines.Tests;

/// <summary>
/// Step 5.2 of the Linux engine: a CEF page's pixels, and what the sample's <c>--check</c>
/// needs besides (a string document, script, messages both ways), driven through the view
/// with a stand-in host and no window. With no window system CEF renders headless; the
/// process's CEF is shared with <see cref="CefEngineTests"/>, so every test here is headless.
/// </summary>
public sealed class CefViewTests : IDisposable
{
    private sealed class TestHost : IWebViewHost
    {
        public PixelFrameSink Pixels { get; } = new();
        public void UseGpuRenderer(IGpuFrameRenderer renderer) { }
        public void RequestRepaint() { }
        public void Post(Action action) => ThreadPool.QueueUserWorkItem(_ => action());
    }

    // A red 100×100 CSS-pixel box at the top-left, a blue one at the bottom-right: at scale
    // 1.5 they are 150 device pixels square (spike L0, question 3).
    private const string Boxes = """
        <!doctype html><title>Boxes</title><style>
        body { margin:0; background:#fff; }
        #r { position:absolute; left:0; top:0; width:100px; height:100px; background:#f00; }
        #b { position:absolute; right:0; bottom:0; width:100px; height:100px; background:#00f; }
        </style><div id="r"></div><div id="b"></div>
        """;

    private readonly CefEngine _engine = new();
    private readonly CefView _view;
    private readonly TestHost _host = new();
    private readonly ConcurrentQueue<WebEngineEvent> _events = new();

    public CefViewTests()
    {
        _view = (CefView)_engine.CreateView(new WebViewOptions());
        _view.Event += _events.Enqueue;
        _view.Attach(_host);
    }

    public void Dispose()
    {
        _view.Dispose();
        _engine.Dispose();
    }

    private async Task ReadyAt(int width, int height, float scale)
    {
        _view.SetPlacement(new WebViewPlacement(0, new PixelRect(0, 0, width, height), scale));
        await Until(() => _events.Any(e => e is WebEngineEvent.Ready or WebEngineEvent.Failed), 30_000);
        Assert.DoesNotContain(_events, e => e is WebEngineEvent.Failed);
    }

    private static async Task Until(Func<bool> done, int ms = 10_000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (!done())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException();
            await Task.Delay(20);
        }
    }

    private async Task<(int Width, int Height, byte[] Pixels)> FrameAfter(Func<bool> loaded, int width)
    {
        await Until(loaded);
        // The newest frame at that width that has the page in it: its top-left is red.
        await Until(() =>
        {
            if (_host.Pixels.IntrinsicSize.Width != width || !_host.Pixels.TryAcquire(out var f)) return false;
            try { return System.Runtime.InteropServices.Marshal.ReadInt32(f.Pixels, (10 * f.Width + 10) * 4) == unchecked((int)0xFFFF0000); }
            finally { _host.Pixels.Release(f); }
        });
        Assert.True(_host.Pixels.TryAcquire(out var frame));
        try
        {
            Assert.Equal(frame.Width * 4, frame.Stride);
            var bytes = new byte[frame.Height * frame.Stride];
            System.Runtime.InteropServices.Marshal.Copy(frame.Pixels, bytes, 0, bytes.Length);
            return (frame.Width, frame.Height, bytes);
        }
        finally { _host.Pixels.Release(frame); }
    }

    private static (byte B, byte G, byte R, byte A) At(byte[] px, int width, int x, int y)
    {
        int i = (y * width + x) * 4;
        return (px[i], px[i + 1], px[i + 2], px[i + 3]);
    }

    [CefFact]
    public async Task APage_PaintsItsPixels_AtThePlacementsScale()
    {
        await ReadyAt(1200, 900, 1.5f);
        _view.NavigateToString(Boxes);
        var (w, h, px) = await FrameAfter(() => _events.OfType<WebEngineEvent.TitleChanged>().Any(t => t.Title == "Boxes"), 1200);

        Assert.Equal((1200, 900), (w, h));
        Assert.Equal((0, 0, 255, 255), At(px, w, 10, 10));        // red, top-left
        Assert.Equal((0, 0, 255, 255), At(px, w, 149, 149));      // still red: 100 CSS px × 1.5
        Assert.Equal((255, 255, 255, 255), At(px, w, 150, 150));  // white past it
        Assert.Equal((255, 0, 0, 255), At(px, w, 1190, 890));     // blue, bottom-right
    }

    [CefFact]
    public async Task ANewScale_RepaintsAtIt()
    {
        await ReadyAt(800, 600, 1f);
        _view.NavigateToString(Boxes);
        await FrameAfter(() => _events.OfType<WebEngineEvent.TitleChanged>().Any(t => t.Title == "Boxes"), 800);

        _view.SetPlacement(new WebViewPlacement(0, new PixelRect(0, 0, 1600, 1200), 2f));
        var (w, h, px) = await FrameAfter(() => true, 1600);
        Assert.Equal((1600, 1200), (w, h));
        Assert.Equal((0, 0, 255, 255), At(px, w, 199, 199));
        Assert.Equal((255, 255, 255, 255), At(px, w, 200, 200));
    }

    [CefFact]
    public async Task Script_AnswersWithJson_AndAThrowFaults()
    {
        await ReadyAt(400, 300, 1f);
        _view.NavigateToString("<title>Script</title>");
        await Until(() => _events.OfType<WebEngineEvent.TitleChanged>().Any(t => t.Title == "Script"));

        Assert.Equal("\"Script\"", await _view.ExecuteScriptAsync("document.title"));
        Assert.Equal("3", await _view.ExecuteScriptAsync("1 + 2"));
        Assert.Equal("{\"a\":[1,\"two\",null]}", await _view.ExecuteScriptAsync("({ a: [1, 'two', null] })"));
        Assert.Equal("null", await _view.ExecuteScriptAsync("undefined"));
        Assert.Equal("\"later\"", await _view.ExecuteScriptAsync("new Promise(r => setTimeout(() => r('later'), 20))"));
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => _view.ExecuteScriptAsync("throw new Error('boom')"));
        Assert.Contains("boom", thrown.Message);
    }

    [CefFact]
    public async Task AStringDocument_IsOpaque_AndItsMessagesHaveNoOrigin()
    {
        await ReadyAt(400, 300, 1f);
        // The srcdoc iframe crashed the renderer when string documents were data: URLs.
        _view.NavigateToString("""
            <title>Opaque</title>
            <iframe srcdoc="<p>inside</p>"></iframe>
            <script>chrome.webview.postMessage({ origin: self.origin, sandboxedCookies: (() => { try { document.cookie; return 'readable'; } catch { return 'blocked'; } })() })</script>
            """);
        await Until(() => _events.OfType<WebEngineEvent.MessageReceived>().Any());
        var message = _events.OfType<WebEngineEvent.MessageReceived>().First();

        Assert.Null(message.Origin);
        // The document's own origin (self.origin; location shows the URL's) is opaque, as the
        // contract says: the CSP sandbox from the scheme handler took.
        Assert.Contains("\"origin\":\"null\"", message.Json);
        Assert.Contains("\"sandboxedCookies\":\"blocked\"", message.Json);
        Assert.Equal("1", await _view.ExecuteScriptAsync("document.querySelectorAll('iframe').length"));
        Assert.DoesNotContain(_events, e => e is WebEngineEvent.ProcessFailed);
    }

    [CefFact]
    public async Task Messages_TravelBothWays()
    {
        await ReadyAt(400, 300, 1f);
        _view.NavigateToString("""
            <title>Echo</title>
            <script>chrome.webview.addEventListener('message', e => chrome.webview.postMessage({ echo: e.data }))</script>
            """);
        await Until(() => _events.OfType<WebEngineEvent.TitleChanged>().Any(t => t.Title == "Echo"));

        _view.PostMessage("{\"ping\":1}");
        await Until(() => _events.OfType<WebEngineEvent.MessageReceived>().Any());
        Assert.Equal("{\"echo\":{\"ping\":1}}", _events.OfType<WebEngineEvent.MessageReceived>().First().Json);
    }
}
