using System.Collections.Concurrent;
using System.Text.Json;
using HultUI.Core.Accessibility;
using HultUI.Core.Rendering;
using HultUI.WebView.Cef;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.WebView.Engines.Tests;

/// <summary>
/// Step 5.6 live: a CEF page's accessibility tree, on when asked for, mirrored as the
/// platform reads it, where the page draws it; and assistive technology's actions on it,
/// carried out through DevTools. 900×600 device pixels at 1.5.
/// </summary>
public sealed class CefAccessibilityTests : IDisposable
{
    private sealed class TestHost : IWebViewHost
    {
        public PixelFrameSink Pixels { get; } = new();
        public void UseGpuRenderer(IGpuFrameRenderer renderer) { }
        public void RequestRepaint() { }
        public void Post(Action action) => ThreadPool.QueueUserWorkItem(_ => action());
    }

    private const string Form = """
        <!doctype html><title>Form</title><style>body{margin:0;font:16px sans-serif}
        #b{position:absolute;left:40px;top:120px;width:120px;height:40px}
        #tall{height:3000px}</style>
        <h1>Settings</h1>
        <label>Name <input id="t" value="Ada"></label>
        <label><input type="checkbox" id="c"> Remember me</label>
        <button id="b" onclick="document.title = 'clicked'">Press me</button>
        <div id="tall"></div>
        """;

    private readonly CefEngine _engine = new();
    private readonly CefView _view;
    private readonly ConcurrentQueue<WebEngineEvent> _events = new();
    private const float Scale = 1.5f;

    private readonly ITestOutputHelper _output;

    public CefAccessibilityTests(ITestOutputHelper output)
    {
        _output = output;
        _view = (CefView)_engine.CreateView(new WebViewOptions());
        _view.Event += _events.Enqueue;
        _view.Event += CefPolicyTests.AllowNavigation;
        _view.Attach(new TestHost());
    }

    public void Dispose()
    {
        _view.Dispose();
        _engine.Dispose();
    }

    private async Task Load(bool accessible = true)
    {
        // Asked before the browser exists, as a controller made while someone listens asks
        // (that call used to be dropped when CEF was not running yet).
        if (accessible) _view.SetAccessibilityEnabled(true);
        _view.SetPlacement(new WebViewPlacement(0, new PixelRect(0, 0, 900, 600), Scale));
        await Until(() => _events.Any(e => e is WebEngineEvent.Ready or WebEngineEvent.Failed), 30_000);
        _view.NavigateToString(Form);
        await Until(() => _events.OfType<WebEngineEvent.TitleChanged>().Any(t => t.Title == "Form"));
    }

    private static async Task Until(Func<Task<bool>> done, int ms = 10_000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (!await done())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException();
            await Task.Delay(50);
        }
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

    private static IEnumerable<AccessibleContentNode> Walk(IEnumerable<AccessibleContentNode> nodes) =>
        nodes.SelectMany(n => Walk(n.Children).Prepend(n));

    private AccessibleContentNode? Find(string name) => Walk(_view.AccessibleTree).FirstOrDefault(n => n.Name == name);

    private async Task<AccessibleContentNode> Arrived(string name, Func<AccessibleContentNode, bool>? state = null)
    {
        try { await Until(() => Find(name) is { } n && (state?.Invoke(n) ?? true)); }
        catch (TimeoutException) { Assert.Fail($"no \"{name}\" in:\n{CefAccessibleTreeTests.Describe(_view.AccessibleTree)}"); }
        return Find(name)!;
    }

    private async Task<double[]> Rect(string selector)
    {
        var json = await _view.ExecuteScriptAsync($"(r => [r.x, r.y, r.width, r.height])(document.querySelector('{selector}').getBoundingClientRect())");
        return JsonSerializer.Deserialize<double[]>(json)!;
    }

    [CefFact]
    public async Task ThePagesTree_ArrivesAsThePlatformReadsIt_WhereThePageDrawsIt()
    {
        await Load();
        var button = await Arrived("Press me");
        _output.WriteLine(CefAccessibleTreeTests.Describe(_view.AccessibleTree));
        Assert.Equal(AccessibilityRole.Document, Assert.Single(_view.AccessibleTree).Role);
        Assert.Equal(AccessibilityRole.Button, button.Role);
        Assert.True(button.IsFocusable);
        Assert.Equal(AccessibilityRole.Heading, (await Arrived("Settings")).Role);
        Assert.Equal("Ada", (await Arrived("Name")).Value);
        Assert.False((await Arrived("Remember me")).IsToggled);

        // Device pixels: the page's CSS pixels at 1.5.
        var css = await Rect("#b");
        Assert.Equal(css[0] * Scale, button.Bounds.X, 1.0);
        Assert.Equal(css[1] * Scale, button.Bounds.Y, 1.0);
        Assert.Equal(css[2] * Scale, button.Bounds.Width, 1.0);
        Assert.Equal(css[3] * Scale, button.Bounds.Height, 1.0);
    }

    [CefFact]
    public async Task ScrollingThePage_MovesItsNodes()
    {
        await Load();
        var before = await Arrived("Press me");
        _view.SendPointer(new WebPointerInput(WebPointerAction.Move, 300, 300));
        _view.SendPointer(new WebPointerInput(WebPointerAction.Wheel, 300, 300, WheelY: -1));
        // A wheel scrolls smoothly, over several frames: wait for it to settle.
        string last = "0";
        await Until(async () =>
        {
            var now = await _view.ExecuteScriptAsync("scrollY");
            bool settled = now != "0" && now == last;
            last = now;
            await Task.Delay(150);
            return settled;
        });
        var scrolled = double.Parse(last, System.Globalization.CultureInfo.InvariantCulture);
        var css = await Rect("#b");
        // The tree follows the last step once its update has been rebuilt.
        var after = await Arrived("Press me", n => Math.Abs(n.Bounds.Y - css[1] * Scale) <= 1);
        Assert.Equal(before.Bounds.Y - scrolled * Scale, after.Bounds.Y, 1.0);
    }

    [CefFact]
    public async Task AssistiveTechnologysActions_AreCarriedOut()
    {
        await Load();
        _view.PerformAccessibleAction((await Arrived("Press me")).Key, AccessibleContentAction.Invoke, null);
        await Until(() => _events.OfType<WebEngineEvent.TitleChanged>().Any(t => t.Title == "clicked"));

        _view.PerformAccessibleAction((await Arrived("Remember me")).Key, AccessibleContentAction.Toggle, null);
        await Arrived("Remember me", n => n.IsToggled == true);

        _view.PerformAccessibleAction((await Arrived("Name")).Key, AccessibleContentAction.SetValue, "Grace");
        await Arrived("Name", n => n.Value == "Grace");
        Assert.Equal("\"Grace\"", await _view.ExecuteScriptAsync("t.value"));

        _view.PerformAccessibleAction((await Arrived("Press me")).Key, AccessibleContentAction.Focus, null);
        await Arrived("Press me", n => n.IsFocused);
        Assert.Equal("\"b\"", await _view.ExecuteScriptAsync("document.activeElement.id"));
    }

    [CefFact]
    public async Task TheTree_IsOff_UntilAskedFor_AndGoesWhenTurnedOff()
    {
        await Load(accessible: false);
        await Task.Delay(500);
        Assert.Empty(_view.AccessibleTree);
        Assert.Equal(0, _view.AccessibleMirrorSize);

        _view.SetAccessibilityEnabled(true);
        await Arrived("Press me");
        _view.SetAccessibilityEnabled(false);
        await Until(() => _view.AccessibleTree.Count == 0);
        Assert.Equal(0, _view.AccessibleMirrorSize);
        Assert.Contains(_events, e => e is WebEngineEvent.AccessibleTreeChanged);
    }
}
