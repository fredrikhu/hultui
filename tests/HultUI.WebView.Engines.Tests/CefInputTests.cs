using System.Collections.Concurrent;
using System.Text.Json;
using HultUI.Core.Events;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.WebView.Cef;
using Xunit;

namespace HultUI.WebView.Engines.Tests;

/// <summary>
/// Step 5.3 of the Linux engine: pointer, keys, text and the input method, focus in and out,
/// the cursor, and the <c>&lt;select&gt;</c> popup composited into the page's frame. The page
/// records what it receives; the tests read that by script. 900×600 device pixels at 1.5: the
/// page is 600×400 CSS pixels.
/// </summary>
public sealed class CefInputTests : IDisposable
{
    private sealed class TestHost : IWebViewHost
    {
        public PixelFrameSink Pixels { get; } = new();
        public void UseGpuRenderer(IGpuFrameRenderer renderer) { }
        public void RequestRepaint() { }
        public void Post(Action action) => ThreadPool.QueueUserWorkItem(_ => action());
    }

    private const string Page = """
        <!doctype html><title>Input</title><style>
        body { margin:0; font: 16px sans-serif; }
        #t { position:absolute; left:20px; top:20px; width:200px; height:30px; }
        #b { position:absolute; left:20px; top:70px; width:80px; height:30px; }
        #s { position:absolute; left:20px; top:120px; width:200px; height:30px; }
        #l { position:absolute; left:300px; top:20px; }
        #tall { position:absolute; left:0; top:0; width:10px; height:3000px; }
        </style>
        <div id="tall"></div><input id="t"><button id="b">Button</button>
        <select id="s"><option>one</option><option>two</option><option>three</option><option>four</option></select>
        <a id="l" href="#x">link</a>
        <script>
        window.__ev = [];
        const L = (...a) => __ev.push(a.join(' '));
        for (const n of ['mousedown', 'mouseup', 'click', 'dblclick', 'contextmenu'])
          addEventListener(n, e => L(n, e.button, e.clientX, e.clientY, e.detail, e.target.id || e.target.tagName));
        for (const n of ['keydown', 'keypress', 'keyup'])
          addEventListener(n, e => L(n, JSON.stringify(e.key), e.code || '-', e.keyCode, e.target.id || e.target.tagName), true);
        t.addEventListener('input', e => L('input', JSON.stringify(t.value), e.inputType));
        s.addEventListener('change', () => L('change', s.value));
        for (const n of ['compositionstart', 'compositionupdate', 'compositionend'])
          t.addEventListener(n, e => L(n, JSON.stringify(e.data)));
        </script>
        """;

    private readonly CefEngine _engine = new();
    private readonly CefView _view;
    private readonly TestHost _host = new();
    private readonly ConcurrentQueue<WebEngineEvent> _events = new();
    private const float Scale = 1.5f;

    public CefInputTests()
    {
        _view = (CefView)_engine.CreateView(new WebViewOptions());
        _view.Event += _events.Enqueue;
        _view.Event += CefPolicyTests.AllowNavigation;
        _view.Attach(_host);
    }

    public void Dispose()
    {
        _view.Dispose();
        _engine.Dispose();
    }

    private async Task Load()
    {
        _view.SetPlacement(new WebViewPlacement(0, new PixelRect(0, 0, 900, 600), Scale));
        await Until(() => _events.Any(e => e is WebEngineEvent.Ready or WebEngineEvent.Failed), 30_000);
        _view.NavigateToString(Page);
        await Until(() => _events.OfType<WebEngineEvent.TitleChanged>().Any(t => t.Title == "Input"));
        await Until(() => _host.Pixels.IntrinsicSize.Width == 900);
        _view.Focus(FocusReason.Programmatic);
        await Task.Delay(100);
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

    /// <summary>What the page recorded since last asked, once it has stopped changing.</summary>
    private async Task<List<string>> Recorded(int settleMs = 200)
    {
        await Task.Delay(settleMs);
        var json = await _view.ExecuteScriptAsync("JSON.stringify(__ev.splice(0))");
        return JsonSerializer.Deserialize<List<string>>(JsonSerializer.Deserialize<string>(json)!)!;
    }

    private Task<string> Eval(string script) => _view.ExecuteScriptAsync(script);

    // CSS pixels → the device pixels the element sends.
    private static int D(float css) => (int)MathF.Round(css * Scale);

    private void Click(float cssX, float cssY, WebPointerButtons button = WebPointerButtons.Left, int count = 1)
    {
        _view.SendPointer(new WebPointerInput(WebPointerAction.Move, D(cssX), D(cssY)));
        _view.SendPointer(new WebPointerInput(WebPointerAction.Down, D(cssX), D(cssY), button, button, count));
        _view.SendPointer(new WebPointerInput(WebPointerAction.Up, D(cssX), D(cssY), button, WebPointerButtons.None, count));
    }

    private void Press(KeyCode key, string? text = null, KeyModifiers modifiers = KeyModifiers.None, ushort scanCode = 0)
    {
        _view.SendKey(new WebKeyInput(true, key, modifiers, ScanCode: scanCode));
        if (text is not null) _view.SendText(new WebTextInput(text));
        _view.SendKey(new WebKeyInput(false, key, modifiers, ScanCode: scanCode));
    }

    [CefFact]
    public async Task Clicks_LandWhereTheyAreSent_InCssPixels()
    {
        await Load();
        Click(60, 85);
        Click(60, 85, count: 1);
        Click(60, 85, count: 2);
        Click(60, 85, WebPointerButtons.Right);
        var ev = await Recorded();
        Assert.Contains("mousedown 0 60 85 1 b", ev);
        Assert.Contains("click 0 60 85 1 b", ev);
        Assert.Contains("dblclick 0 60 85 2 b", ev);
        Assert.Contains("contextmenu 2 60 85 0 b", ev);
    }

    [CefFact]
    public async Task TheWheel_ScrollsThePage_DownForANotchTowardTheUser()
    {
        await Load();
        _view.SendPointer(new WebPointerInput(WebPointerAction.Wheel, D(300), D(200), WheelY: -1));
        await Until(async () => int.Parse(await Eval("Math.round(scrollY)")) >= 100);
        _view.SendPointer(new WebPointerInput(WebPointerAction.Wheel, D(300), D(200), WheelY: 1));
        await Until(async () => int.Parse(await Eval("Math.round(scrollY)")) == 0);
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

    [CefFact]
    public async Task Typing_GivesThePageWhatABrowserWould()
    {
        await Load();
        Click(120, 35);   // into the field
        await Recorded();

        Press(KeyCode.A, "a", scanCode: 0x04);
        Press(KeyCode.B, "B", KeyModifiers.Shift, scanCode: 0x05);
        Press(KeyCode.C, "c");                                   // no scan code: the named key's
        Press(KeyCode.Unknown, "1", scanCode: 0x59);             // the keypad's 1, known only by its scan code
        var ev = await Recorded();

        Assert.Contains("keydown \"a\" KeyA 65 t", ev);
        Assert.Contains("keypress \"a\" KeyA 97 t", ev);
        Assert.Contains("keydown \"B\" KeyB 66 t", ev);
        Assert.Contains("keydown \"c\" KeyC 67 t", ev);
        Assert.Contains("keydown \"1\" Numpad1 97 t", ev);
        await Until(async () => await Eval("t.value") == "\"aBc1\"");

        // Keys go to the renderer as input, script through DevTools: wait for one to show in the other.
        Press(KeyCode.Backspace, scanCode: 0x2a);
        await Until(async () => await Eval("t.value") == "\"aBc\"");
        Press(KeyCode.Enter, scanCode: 0x28);
        ev = await Recorded();
        Assert.Contains("keydown \"Enter\" Enter 13 t", ev);
        Assert.Contains("keypress \"Enter\" Enter 13 t", ev);

        Press(KeyCode.A, modifiers: KeyModifiers.Ctrl, scanCode: 0x04);   // select all, which Chromium does itself
        await Until(async () => await Eval("[t.selectionStart, t.selectionEnd]") == "[0,3]");
    }

    [CefFact]
    public async Task TheInputMethod_Composes_Commits_AndSaysWhereItIs()
    {
        await Load();
        Click(120, 35);
        await Recorded();

        _view.SendText(new WebTextInput("にほん", IsComposition: true, SegmentStart: 3));
        await Until(() => _events.Any(e => e is WebEngineEvent.InputAreaChanged));
        var area = _events.OfType<WebEngineEvent.InputAreaChanged>().Last().Area;
        // In the field, in device pixels: the field is at (20, 20) CSS, 200×30.
        Assert.InRange(area.X, D(20), D(220));
        Assert.InRange(area.Y, D(20), D(50));

        _view.SendText(new WebTextInput("日本"));
        var ev = await Recorded();
        Assert.Contains("compositionstart \"\"", ev);
        Assert.Contains("compositionupdate \"にほん\"", ev);
        Assert.Contains("compositionend \"日本\"", ev);
        Assert.Equal("\"日本\"", await Eval("t.value"));

        // Text that came from no key (a dead key's result, an on-screen keyboard): inserted, no keypress.
        _view.SendText(new WebTextInput("é"));
        ev = await Recorded();
        Assert.Contains("input \"日本é\" insertText", ev);
        Assert.DoesNotContain(ev, e => e.StartsWith("keypress"));
    }

    [CefFact]
    public async Task FocusComingIn_StartsAtTheFirstOrLastStop_AndTabbingPastTheEndLeaves()
    {
        await Load();
        _view.Focus(FocusReason.Next);
        await Until(async () => await Eval("document.activeElement.id") == "\"t\"");
        _view.Focus(FocusReason.Previous);
        await Until(async () => await Eval("document.activeElement.id") == "\"l\"");

        Press(KeyCode.Tab, scanCode: 0x2b);   // past the last stop
        await Until(() => _events.OfType<WebEngineEvent.FocusLeaving>().Any(f => f.Forward));
    }

    [CefFact]
    public async Task TheCursor_FollowsWhatIsUnderThePointer()
    {
        await Load();
        _view.SendPointer(new WebPointerInput(WebPointerAction.Move, D(310), D(28)));   // the link
        await Until(() => _events.OfType<WebEngineEvent.CursorChanged>().Any(c => c.Cursor == CursorType.Pointer));
        _view.SendPointer(new WebPointerInput(WebPointerAction.Move, D(120), D(35)));   // the field
        await Until(() => _events.OfType<WebEngineEvent.CursorChanged>().Any(c => c.Cursor == CursorType.Text));
    }

    [CefFact]
    public async Task ASelectsList_IsDrawnOverThePage_AndChoosesWhereItIsClicked()
    {
        await Load();
        int Pixel(int x, int y)
        {
            if (!_host.Pixels.TryAcquire(out var f)) return 0;
            try { return System.Runtime.InteropServices.Marshal.ReadInt32(f.Pixels, (y * f.Width + x) * 4); }
            finally { _host.Pixels.Release(f); }
        }
        // Below the select, the page is white: the list, when it opens, is not.
        int belowX = D(40), belowY = D(165);
        try { await Until(() => Pixel(belowX, belowY) == unchecked((int)0xFFFFFFFF)); }
        catch (TimeoutException) { Assert.Fail($"before: 0x{Pixel(belowX, belowY):X8}"); }

        Click(120, 135);
        try { await Until(() => Pixel(belowX, belowY) != unchecked((int)0xFFFFFFFF)); }
        catch (TimeoutException) { Assert.Fail($"after the click: 0x{Pixel(belowX, belowY):X8}; recorded {string.Join(" | ", await Recorded())}"); }

        // The list's rows follow the field: "three" is the third.
        Click(40, 150 + 2 * 22 + 10);
        var ev = await Recorded(400);
        Assert.Contains("change three", ev);
        await Until(() => Pixel(belowX, belowY) == unchecked((int)0xFFFFFFFF));
    }
}
