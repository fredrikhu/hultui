using HultUI.Core.Builders;
using HultUI.Core.Events;
using HultUI.Core.Rendering;
using HultUI.Headless;
using HultUI.WebView.Testing;
using Xunit;

namespace HultUI.WebView.Tests;

/// <summary>
/// A page off screen gives back the GPU objects its frames went through, for an engine that can
/// make them again (WebView2; the fake behaves like it), in the next frame of the window it was
/// drawn in; and an element drawn while something else hid its page shows the page again.
/// Plan 17 round 9, webview-hidden-keeps-interop: a hidden WebView2 page kept 90.7 MB of this
/// process's GPU memory at 2160×779 px, as much as a shown one.
/// </summary>
public class WebViewHiddenGpuTests
{
    [Fact]
    public void AHiddenPage_GivesItsGpuObjectsBack_InTheWindowsNextFrame_AndDrawsAgainWhenShown()
    {
        using var session = HeadlessSession.Create();
        var renderer = new CountingRenderer();
        using var web = new WebViewController(new FakeWebEngine { GpuRenderer = renderer });
        var sink = Assert.IsType<GpuFrameSink>(web.Frames);
        var surface = new LendingSurface();
        web.SetVisible(true);
        Assert.True(sink.TryRenderFrame(surface, out _));   // drawn on a GL surface: GPU mode

        web.SetVisible(false);
        Assert.True(sink.IsReleaseWanted);
        Assert.Equal(0, renderer.Releases);                 // not outside the window's frame
        GpuFrameSink.ReleaseHidden(surface);                // the window's next frame
        Assert.Equal(1, renderer.Releases);
        Assert.Equal([sink], surface.Released);

        web.SetVisible(true);
        renderer.HasNewFrame = false;
        Assert.False(sink.TryRenderFrame(surface, out _));  // a new target, nothing in it yet
        renderer.HasNewFrame = true;
        Assert.True(sink.TryRenderFrame(surface, out _));
        Assert.False(sink.IsReleased);
    }

    /// <summary>CEF, and any engine outside this repository, does not say its renderer can make its
    /// objects again: hiding its page keeps them, as before.</summary>
    [Fact]
    public void AnEngineThatDoesNotSaySo_KeepsItsGpuObjectsWhileHidden()
    {
        using var session = HeadlessSession.Create();
        var renderer = new CountingRenderer();
        using var web = new WebViewController(new PlainEngine(renderer));
        var sink = Assert.IsType<GpuFrameSink>(web.Frames);
        var surface = new LendingSurface();
        web.SetVisible(true);
        Assert.True(sink.TryRenderFrame(surface, out _));

        web.SetVisible(false);
        Assert.False(sink.IsReleaseWanted);
        GpuFrameSink.ReleaseHidden(surface);
        Assert.Equal(0, renderer.Releases);
        Assert.Empty(surface.Released);
    }

    /// <summary>
    /// Something other than this element hid the page — another element showing the controller
    /// stopped, a rebuild's twin — while this one still draws it. Drawn is on screen: the element
    /// says so again, or WebView2 would keep the page hidden, its capture stopped, under it.
    /// </summary>
    [Fact]
    public void AnElementDrawingAPageHiddenElsewhere_ShowsItAgain()
    {
        using var session = HeadlessSession.Create(400, 300);
        var engine = new FakeWebEngine();
        using var web = new WebViewController(engine);
        var element = WebViewElement.New(web).FlexGrow(1);
        session.Application.SetRoot(Flex.Column().Children(element));
        session.PumpFrames(2);
        var view = engine.Views[0];
        Assert.True(view.IsVisible);

        web.SetVisible(false);
        Assert.False(view.IsVisible);
        element.MarkPaintDirty();
        session.PumpFrames(2);

        Assert.True(view.IsVisible);
        Assert.Equal("Visible True", view.Calls[^1]);
        // And only once: a page shown is not told again by every draw.
        int calls = view.Calls.Count;
        element.MarkPaintDirty();
        session.PumpFrames(2);
        Assert.Equal(calls, view.Calls.Count);
    }

    private sealed class CountingRenderer : IGpuFrameRenderer
    {
        public bool HasNewFrame { get; set; } = true;
        public int Releases { get; private set; }
        public void OnModeDecided(GpuSinkMode mode) { }
        public void PrepareGL(Func<string, nint> getProcAddress) { }
        public bool TryGetFrameSize(out int width, out int height) { width = 320; height = 180; return true; }
        public bool RendersBottomUp => false;
        public bool Render(Func<string, nint> getProcAddress, in GpuFrameTarget target, int width, int height) => HasNewFrame;
        public void ReleaseGL() => Releases++;
    }

    /// <summary>A surface that lends a target, as Vulkan's does, so no GL is resolved.</summary>
    private sealed class LendingSurface : IExternalGLSurface
    {
        public List<object> Released { get; } = [];
        public Func<string, nint> GetProcAddress => _ => 0;
        public void RunExternalGL(Action draw) => draw();
        public bool TryAcquireFrameTarget(object owner, int width, int height, out GpuFrameTarget target)
        {
            target = new GpuFrameTarget(7, 9, GpuFrame.Rgba8);
            return true;
        }
        public void ReleaseFrameTarget(object owner) => Released.Add(owner);
        public IRenderContext RenderContext => throw new NotSupportedException();
        public IMeasureContext MeasureContext => throw new NotSupportedException();
        public void Resize(int widthPx, int heightPx, float scaleFactor) { }
        public void Present() { }
        public void Dispose() { }
    }

    /// <summary>An engine of someone else's: a GPU renderer, and no word on giving it back.</summary>
    private sealed class PlainEngine(IGpuFrameRenderer renderer) : IWebEngine
    {
        public string Name => "Plain";
        public WebEngineAvailability Availability => WebEngineAvailability.Available;
        public IWebEngineView CreateView(WebViewOptions options) => new PlainView(renderer);
        public void Dispose() { }
    }

    private sealed class PlainView(IGpuFrameRenderer renderer) : IWebEngineView
    {
        public WebEngineCapabilities Capabilities => WebEngineCapabilities.None;
        public event Action<WebEngineEvent>? Event { add { } remove { } }
        public void Attach(IWebViewHost host) => host.UseGpuRenderer(renderer);
        public void SetPlacement(in WebViewPlacement placement) { }
        public void SetVisible(bool visible) { }
        public void Navigate(Uri uri) { }
        public void NavigateToString(string html) { }
        public void GoBack() { }
        public void GoForward() { }
        public void Reload() { }
        public void Stop() { }
        public Task<string> ExecuteScriptAsync(string script) => Task.FromResult("null");
        public void PostMessage(string json) { }
        public void SendPointer(in WebPointerInput input) { }
        public void SendKey(in WebKeyInput input) { }
        public void SendText(in WebTextInput input) { }
        public void Focus(FocusReason reason) { }
        public void Blur() { }
        public void Dispose() { }
    }
}
