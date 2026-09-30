using System.Diagnostics;
using System.Runtime.ExceptionServices;
using HultUI.Core.Builders;
using HultUI.Core.Rendering;
using HultUI.Platform.SDL3;
using HultUI.WebView;
using HultUI.WebView.WebView2;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// The WebView2 engine on a real window: the page renders offscreen into the captured
/// visual and its pixels reach the sink, script and messages go both ways, a navigation
/// the app refuses never happens, and pointer input sent to the page lands where it was
/// aimed. Needs the WebView2 Runtime, and a desktop: the window takes the foreground.
/// </summary>
/// <remarks>
/// xUnit runs tests on MTA threads, and WebView2 refuses them, so each test runs its body on
/// a thread of its own marked STA — the thread the application is created on, and so its UI
/// thread. Pages are string documents: no network is needed.
/// </remarks>
[Collection("ui")]
public sealed class WebView2Tests
{
    private sealed class WebView2UiFactAttribute : FactAttribute
    {
        public WebView2UiFactAttribute()
        {
            if (!UiTestGate.Enabled) Skip = UiTestGate.Reason;
            else if (!OperatingSystem.IsWindows()) Skip = "WebView2 is Windows-only.";
            else if (new WebView2Engine().Availability is { IsAvailable: false } a) Skip = "WebView2 unavailable: " + a.Reason;
        }
    }

    private static readonly string Profile = Path.Combine(Path.GetTempPath(), "hultui-webview2-tests");

    /// <summary>Runs <paramref name="body"/> on a fresh STA thread and rethrows what it threw.</summary>
    private static void OnSta(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private sealed class Page : IDisposable
    {
        public required Application App { get; init; }
        public required WebView2Engine Engine { get; init; }
        public required WebViewController Web { get; init; }

        public bool PumpUntil(Func<bool> condition, double seconds = 20)
        {
            var deadline = Stopwatch.StartNew();
            while (!condition())
            {
                if (deadline.Elapsed.TotalSeconds > seconds) return false;
                App.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
                Thread.Sleep(1);
            }
            return true;
        }

        public T Await<T>(Task<T> task)
        {
            Assert.True(PumpUntil(() => task.IsCompleted), "the task never completed");
            return task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// The window's pixel at a device-pixel position from its top-left, read back from the
        /// front buffer through GL. On a real window the page goes through the GPU, so the pixel
        /// ring holds nothing; the screen is the only honest place to look.
        /// </summary>
        public (int R, int G, int B) Screen(int x, int y)
        {
            var gl = new GL();
            var viewport = new int[4];
            gl.GetIntegerv(0x0BA2 /* GL_VIEWPORT */, viewport);
            gl.ReadBuffer(0x0404 /* GL_FRONT */);
            var px = new byte[4];
            gl.ReadPixels(x, viewport[3] - 1 - y, 1, 1, 0x1908 /* RGBA */, 0x1401 /* UNSIGNED_BYTE */, px);
            return (px[0], px[1], px[2]);
        }

        public (int R, int G, int B) ScreenMiddle()
        {
            var gl = new GL();
            var viewport = new int[4];
            gl.GetIntegerv(0x0BA2, viewport);
            return Screen(viewport[2] / 2, viewport[3] / 2);
        }

        public static bool Near((int R, int G, int B) a, (int R, int G, int B) b) =>
            Math.Abs(a.R - b.R) <= 8 && Math.Abs(a.G - b.G) <= 8 && Math.Abs(a.B - b.B) <= 8;

        public void Dispose()
        {
            Web.Dispose();
            Engine.Dispose();
            App.Dispose();
        }
    }

    private static Page Open(string html)
    {
        var app = new Application(new SdlWindowingPlatform(), "WebView2", 480, 320);
        var engine = new WebView2Engine();
        var web = new WebViewController(engine, new WebViewOptions { UserDataFolder = Profile });
        app.SetRoot(Flex.Column().Children(WebViewElement.New(web).FlexGrow(1)));
        web.NavigateToString(html);
        var page = new Page { App = app, Engine = engine, Web = web };
        Assert.True(page.PumpUntil(() => web.State != WebViewState.Initializing), "the engine never became ready");
        Assert.Equal(WebViewState.Ready, web.State);
        return page;
    }

    [WebView2UiFact]
    public void APage_ReachesTheWindow_WithoutPassingThroughTheCpu() => OnSta(() =>
    {
        // Captured into D3D11, read by GL in place through WGL_NV_DX_interop2, blitted into the
        // frame sink's framebuffer, composited by Skia: the colour on screen proves the chain,
        // and ZeroCopy that no fallback was taken on the way.
        using var page = Open("<html><head><title>Green</title></head><body style='margin:0;background:#00c060'></body></html>");

        Assert.True(page.PumpUntil(() => Page.Near(page.ScreenMiddle(), (0, 192, 96))),
            $"the window's middle was {page.ScreenMiddle()}, expected the page's (0,192,96)");
        Assert.Equal(GpuSinkMode.Gpu, page.Web.SinkMode);
        Assert.True(((WebView2View)page.Web.View).ZeroCopy, "the frame was read back and uploaded, not shared");
        Assert.True(page.PumpUntil(() => page.Web.Title == "Green"));
    });

    [WebView2UiFact]
    public void APageThatChanges_ShowsEachChange() => OnSta(() =>
    {
        // Frames arrive only on change; each must be taken and drawn, not just the first.
        using var page = Open("<html><body style='margin:0;background:#ff00ff'></body></html>");
        Assert.True(page.PumpUntil(() => Page.Near(page.ScreenMiddle(), (255, 0, 255))), $"first colour: {page.ScreenMiddle()}");

        foreach (var (css, rgb) in new[] { ("#2040e0", (32, 64, 224)), ("#e0c020", (224, 192, 32)) })
        {
            page.Web.ExecuteScriptAsync($"document.body.style.background = '{css}'");
            Assert.True(page.PumpUntil(() => Page.Near(page.ScreenMiddle(), rgb)), $"after {css}: {page.ScreenMiddle()}");
        }
    });

    /// <summary>GL 1.1 entry points, which opengl32.dll exports directly; they act on the current context.</summary>
    private sealed class GL
    {
        public GL()
        {
            var opengl32 = System.Runtime.InteropServices.NativeLibrary.Load("opengl32.dll");
            T Load<T>(string name) where T : Delegate =>
                System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<T>(System.Runtime.InteropServices.NativeLibrary.GetExport(opengl32, name));
            ReadBuffer = Load<ReadBufferFn>("glReadBuffer");
            ReadPixels = Load<ReadPixelsFn>("glReadPixels");
            GetIntegerv = Load<GetIntegervFn>("glGetIntegerv");
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)] public delegate void ReadBufferFn(uint mode);
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)] public delegate void ReadPixelsFn(int x, int y, int width, int height, uint format, uint type, byte[] pixels);
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)] public delegate void GetIntegervFn(uint pname, int[] data);

        public ReadBufferFn ReadBuffer;
        public ReadPixelsFn ReadPixels;
        public GetIntegervFn GetIntegerv;
    }

    [WebView2UiFact]
    public void ScriptAndMessages_GoBothWays() => OnSta(() =>
    {
        using var page = Open("""
            <html><body><script>
              chrome.webview.postMessage({ hello: 1 });
              chrome.webview.addEventListener('message', e => document.title = 'got ' + e.data.n);
            </script></body></html>
            """);
        var received = new List<WebMessage>();
        page.Web.MessageReceived += received.Add;

        Assert.Equal("3", page.Await(page.Web.ExecuteScriptAsync("1 + 2")));

        // The page posted on load, maybe before the handler above; ask it again to be sure.
        page.Web.ExecuteScriptAsync("chrome.webview.postMessage({ hello: 2 })");
        Assert.True(page.PumpUntil(() => received.Any(m => m.Json.Contains("\"hello\":2"))), "no message from the page");
        Assert.Null(received.Last().Origin);   // a string document's origin is opaque

        page.Web.PostMessage("{\"n\":7}");
        Assert.True(page.PumpUntil(() => page.Web.Title == "got 7"), $"the page never got the message; title '{page.Web.Title}'");
    });

    [WebView2UiFact]
    public void ANavigationTheAppRefuses_NeverHappens() => OnSta(() =>
    {
        using var page = Open("<html><body>start</body></html>");
        Assert.True(page.PumpUntil(() => !page.Web.IsLoading));
        var before = page.Web.Source;
        WebNavigationResult? result = null;
        page.Web.NavigationStarting += r => { if (r.Uri.Host == "blocked.test") r.Cancel(); };
        page.Web.NavigationCompleted += r => result = r;

        page.Web.Navigate(new Uri("https://blocked.test/"));

        Assert.True(page.PumpUntil(() => result is not null), "the refused navigation never completed");
        Assert.False(result!.IsSuccess);
        Assert.Equal(before, page.Web.Source);
    });

    [WebView2UiFact]
    public void AClickSentToThePage_LandsWhereItWasAimed() => OnSta(() =>
    {
        using var page = Open("""
            <html><body style='margin:0'>
              <div id=left style='position:absolute;left:0;top:0;width:50%;height:100%'></div>
              <div id=right style='position:absolute;left:50%;top:0;width:50%;height:100%'></div>
              <script>
                for (const id of ['left', 'right'])
                  document.getElementById(id).onclick = () => chrome.webview.postMessage(id);
                chrome.webview.postMessage('ready');
              </script>
            </body></html>
            """);
        var received = new List<string>();
        page.Web.MessageReceived += m => received.Add(m.Json);
        // Ready when the page says so: its handlers exist only once its script has run, which a
        // frame on screen does not prove.
        Assert.True(page.PumpUntil(() => received.Contains("\"ready\"") && page.Web.Frames.IsLoaded), "the page never got ready");
        received.Clear();
        int width = page.Web.Frames.IntrinsicSize.Width is var w and > 0 ? (int)w : 480;

        var view = page.Web.View;
        foreach (var x in new[] { width / 4, width * 3 / 4 })
        {
            view.SendPointer(new WebPointerInput(WebPointerAction.Move, x, 40));
            view.SendPointer(new WebPointerInput(WebPointerAction.Down, x, 40, WebPointerButtons.Left, WebPointerButtons.Left, 1));
            view.SendPointer(new WebPointerInput(WebPointerAction.Up, x, 40, WebPointerButtons.Left, WebPointerButtons.None, 1));
        }

        Assert.True(page.PumpUntil(() => received.Count >= 2), $"clicks received: {string.Join(", ", received)}");
        Assert.Equal(["\"left\"", "\"right\""], received.Take(2));
    });

    [WebView2UiFact]
    public void AShortcutPressedInThePage_ReachesTheApp() => OnSta(() =>
    {
        // Keys go to WebView2's own focus window, never through ours. A key message posted to
        // that window, in this process, stands in for the keyboard: nothing reaches the
        // foreground or anyone else's window, which is the line SendKeys crosses.
        var app = new Application(new SdlWindowingPlatform(), "WebView2", 480, 320);
        var engine = new WebView2Engine();
        var web = new WebViewController(engine, new WebViewOptions { UserDataFolder = Profile });
        var pressed = new List<HultUI.Core.Events.KeyCode>();
        var element = WebViewElement.New(web).FlexGrow(1);
        app.SetRoot(Flex.Column().Children(element).OnKeyDown(e =>
        {
            pressed.Add(e.Key);
            e.StopPropagation = true;
        }));
        web.NavigateToString("<html><body><input autofocus></body></html>");
        using var page = new Page { App = app, Engine = engine, Web = web };
        Assert.True(page.PumpUntil(() => web.State == WebViewState.Ready && !web.IsLoading && web.Frames.IsLoaded));

        element.Focus();
        page.PumpUntil(() => false, seconds: 0.3);
        nint focusWindow = FocusWindowOf(app.PrimaryWindow.NativeHandle);
        Assert.NotEqual(0, focusWindow);

        const uint WM_KEYDOWN = 0x100, WM_KEYUP = 0x101;
        const nint VK_F2 = 0x71;
        nint down = 1 | (0x3C << 16), up = unchecked((nint)(1 | (0x3C << 16) | (1 << 30) | (1u << 31)));
        PostMessage(focusWindow, WM_KEYDOWN, VK_F2, down);
        PostMessage(focusWindow, WM_KEYUP, VK_F2, up);

        Assert.True(page.PumpUntil(() => pressed.Contains(HultUI.Core.Events.KeyCode.F2), seconds: 5),
            $"the app never saw F2; it saw [{string.Join(", ", pressed)}]");
    });

    [WebView2UiFact]
    public void ASelectsList_OpensUnderItsField_AndStillDoesAfterTheWindowMoves() => OnSta(() =>
    {
        // The list of a <select> is not part of the page's picture: the browser opens a window
        // of its own for it, on screen, where it believes the field is. It knows the page's
        // place in our window from the bounds we give it, and our window's place on screen
        // only from being told when that changes.
        using var page = Open("""
            <html><body style='margin:0'>
              <select id=s style='position:absolute;left:40px;top:60px;width:220px;height:32px;font-size:16px'>
                <option>One</option><option>Two</option><option>Three</option><option>Four</option>
              </select>
              <script>chrome.webview.postMessage('ready');</script>
            </body></html>
            """);
        var received = new List<string>();
        page.Web.MessageReceived += m => received.Add(m.Json);
        Assert.True(page.PumpUntil(() => received.Contains("\"ready\"") && page.Web.Frames.IsLoaded), "the page never got ready");

        var window = page.App.PrimaryWindow;
        nint handle = window.NativeHandle;
        float scale = window.DisplayScale;
        GetWindowThreadProcessId(FocusWindowOf(handle), out uint browser);
        Assert.NotEqual(0u, browser);

        // The page fills the window, so a point in the page is the same point in the client area.
        int fieldLeft = (int)(40 * scale), fieldBottom = (int)((60 + 32) * scale);
        int clickX = (int)((40 + 110) * scale), clickY = (int)((60 + 16) * scale);
        int awayX = (int)(400 * scale), awayY = (int)(250 * scale);

        (int dx, int dy) OpenListAndMeasure(string when)
        {
            var before = BrowserWindows(browser);
            Click(page.Web.View, clickX, clickY);
            nint list = 0;
            Assert.True(page.PumpUntil(() => (list = BrowserWindows(browser).FirstOrDefault(w => !before.Contains(w))) != 0, seconds: 5),
                $"{when}: no list window opened");
            // It is placed when it is shown; give it a moment to stop moving.
            page.PumpUntil(() => false, seconds: 0.3);
            GetWindowRect(list, out var r);
            var origin = new POINT();
            ClientToScreen(handle, ref origin);

            Click(page.Web.View, awayX, awayY);
            Assert.True(page.PumpUntil(() => !BrowserWindows(browser).Contains(list), seconds: 5), $"{when}: the list never closed");
            return (r.Left - (origin.X + fieldLeft), r.Top - (origin.Y + fieldBottom));
        }

        // Under the field's bottom left corner, give or take the list's own border and shadow.
        var first = OpenListAndMeasure("before the move");
        int slack = (int)(16 * scale);
        Assert.True(Math.Abs(first.dx) <= slack && Math.Abs(first.dy) <= slack,
            $"the list opened ({first.dx},{first.dy}) px from the field's bottom left corner");

        GetWindowRect(handle, out var frame);
        SetWindowPos(handle, 0, frame.Left + 90, frame.Top + 70, 0, 0, 0x0001 | 0x0004 | 0x0010 /* NOSIZE | NOZORDER | NOACTIVATE */);
        page.PumpUntil(() => false, seconds: 0.5);

        // The same offset from the field as before: the list went with the window.
        var second = OpenListAndMeasure("after the move");
        Assert.True(Math.Abs(second.dx - first.dx) <= 2 && Math.Abs(second.dy - first.dy) <= 2,
            $"before the move the list was ({first.dx},{first.dy}) px from the field; after moving the window by (90,70) it was ({second.dx},{second.dy})");
    });

    [WebView2UiFact]
    public void ARightClick_AsksTheAppForAMenu_WithThePagesEntries_AndAChoiceIsCarriedOut() => OnSta(() =>
    {
        using var page = Open("""
            <html><body style='margin:0'>
              <input id=t value='hello world' style='position:absolute;left:20px;top:20px;width:200px;height:30px'>
              <script>chrome.webview.postMessage('ready');</script>
            </body></html>
            """);
        var received = new List<string>();
        page.Web.MessageReceived += m => received.Add(m.Json);
        Assert.True(page.PumpUntil(() => received.Contains("\"ready\"") && page.Web.Frames.IsLoaded), "the page never got ready");
        float scale = page.App.PrimaryWindow.DisplayScale;
        GetWindowThreadProcessId(FocusWindowOf(page.App.PrimaryWindow.NativeHandle), out uint browser);
        var before = BrowserWindows(browser);

        // The app takes the menu over, so the test can choose without a pointer on screen.
        WebContextMenuRequest? asked = null;
        page.Web.ContextMenuRequested += r => { r.Handled = true; asked = r; };
        int x = (int)(100 * scale), y = (int)(35 * scale);
        var view = page.Web.View;
        view.SendPointer(new WebPointerInput(WebPointerAction.Move, x, y));
        view.SendPointer(new WebPointerInput(WebPointerAction.Down, x, y, WebPointerButtons.Right, WebPointerButtons.Right, 1));
        view.SendPointer(new WebPointerInput(WebPointerAction.Up, x, y, WebPointerButtons.Right, WebPointerButtons.None, 1));
        Assert.True(page.PumpUntil(() => asked is not null, seconds: 5), "no context menu was asked for");

        var names = asked!.EngineItems.Select(i => i.Name).ToList();
        Assert.True(asked.Target.IsEditable, "the input was not reported as editable");
        Assert.Contains("selectAll", names);
        Assert.Contains("paste", names);
        Assert.DoesNotContain(asked.Items, i => i.Label.Contains('&'));
        // Where the page says the menu was asked for: the click, in the page's device pixels.
        Assert.True(Math.Abs(asked.X - x) <= 2 && Math.Abs(asked.Y - y) <= 2, $"asked at ({asked.X},{asked.Y}); clicked at ({x},{y})");
        // WebView2's own menu did not open.
        page.PumpUntil(() => false, seconds: 0.3);
        Assert.Empty(BrowserWindows(browser).Except(before));

        asked.Select(asked.EngineItems.First(i => i.Name == "selectAll"));
        var selected = "";
        Assert.True(page.PumpUntil(() =>
        {
            selected = page.Await(page.Web.ExecuteScriptAsync("document.getElementById('t').selectionEnd - document.getElementById('t').selectionStart"));
            return selected == "11";
        }, seconds: 5), $"Select all selected {selected} characters, not 11");
    });

    [WebView2UiFact]
    public void ThePagesDialogs_ComeToTheApp_AndTheirAnswersReachThePage() => OnSta(() =>
    {
        using var page = Open("<html><body><script>chrome.webview.postMessage('ready');</script></body></html>");
        var received = new List<string>();
        page.Web.MessageReceived += m => received.Add(m.Json);
        Assert.True(page.PumpUntil(() => received.Contains("\"ready\"")), "the page never got ready");
        GetWindowThreadProcessId(FocusWindowOf(page.App.PrimaryWindow.NativeHandle), out uint browser);
        var before = BrowserWindows(browser);

        var asked = new List<WebScriptDialogRequest>();
        page.Web.ScriptDialogRequested += d =>
        {
            asked.Add(d);
            if (d.Kind == WebScriptDialogKind.Prompt) d.Accept("from the app");
            else if (d.Kind == WebScriptDialogKind.Confirm) d.Accept();
            else d.Handled = true; // the alert is answered below, after a look around
        };

        page.Web.ExecuteScriptAsync("""
            setTimeout(() => {
              const c = confirm('Sure?');
              const p = prompt('Name?', 'Ann');
              alert('Done');
              chrome.webview.postMessage(`confirm:${c} prompt:${p}`);
            }, 0)
            """);
        Assert.True(page.PumpUntil(() => asked.Count == 3, seconds: 5), $"dialogs asked: {asked.Count}");
        Assert.Equal([WebScriptDialogKind.Confirm, WebScriptDialogKind.Prompt, WebScriptDialogKind.Alert], asked.Select(d => d.Kind));
        Assert.Equal("Sure?", asked[0].Message);
        Assert.Equal("Ann", asked[1].DefaultText);

        // The page's script is stopped at the alert, and WebView2 has not drawn one of its own.
        page.PumpUntil(() => false, seconds: 0.3);
        Assert.DoesNotContain(received, m => m.StartsWith("\"confirm:"));
        Assert.Empty(BrowserWindows(browser).Except(before));

        asked[2].Accept();
        Assert.True(page.PumpUntil(() => received.Contains("\"confirm:true prompt:from the app\""), seconds: 5),
            $"the page said: {string.Join(", ", received)}");
    });

    [WebView2UiFact]
    public void APermission_IsTheAppsToGive_AndDeniedWhenItSaysNothing() => OnSta(() =>
    {
        // Notifications, because asking has no side effect: the answer is all there is.
        const string Ask = "Notification.requestPermission().then(r => chrome.webview.postMessage('notifications:' + r))";
        string Answer(Action<WebPermissionRequest>? handler, List<WebPermissionRequest> asked)
        {
            using var page = Open("<html><body><script>chrome.webview.postMessage('ready');</script></body></html>");
            var received = new List<string>();
            page.Web.MessageReceived += m => received.Add(m.Json);
            Assert.True(page.PumpUntil(() => received.Contains("\"ready\"")), "the page never got ready");
            page.Web.PermissionRequested += p => { asked.Add(p); handler?.Invoke(p); };
            page.Web.ExecuteScriptAsync(Ask);
            Assert.True(page.PumpUntil(() => received.Any(m => m.StartsWith("\"notifications:")), seconds: 10), "the page never heard");
            return received.Last();
        }

        var asked = new List<WebPermissionRequest>();
        Assert.Equal("\"notifications:denied\"", Answer(null, asked));
        Assert.Equal(WebPermission.Notifications, Assert.Single(asked).Permission);

        // The next page to ask asks the app again. (This does not prove the engine's
        // SavesInProfile = false: a page loaded from a string has no origin a permission could be
        // remembered against, and the test passes without the setting too. WebView2 documents
        // that it remembers host answers by default; the setting is there on that word.)
        Assert.Equal("\"notifications:granted\"", Answer(p => p.Allow(), asked));
        Assert.Equal(2, asked.Count);
    });

    private const string DownloadPage = """
        <html><body>
          <a id=d href='data:text/plain,hello%20download' download='hello.txt'>get</a>
          <script>chrome.webview.postMessage('ready');</script>
        </body></html>
        """;

    // One download per page: a page that starts a second one without a click in between is
    // stopped by the browser's own guard against automatic downloads.

    [WebView2UiFact]
    public void ADownloadNobodyAnswers_IsCancelled() => OnSta(() =>
    {
        using var page = Open(DownloadPage);
        var received = new List<string>();
        page.Web.MessageReceived += m => received.Add(m.Json);
        Assert.True(page.PumpUntil(() => received.Contains("\"ready\"")), "the page never got ready");

        WebDownloadRequest? asked = null;
        page.Web.DownloadRequested += r => asked = r;
        page.Web.ExecuteScriptAsync("document.getElementById('d').click()");

        Assert.True(page.PumpUntil(() => asked is not null, seconds: 10), "no download was asked for");
        Assert.Equal("hello.txt", asked!.SuggestedFileName);
        Assert.Equal(WebDownloadState.Cancelled, asked.Download.State);
    });

    [WebView2UiFact]
    public void ADownload_ArrivesWhereTheAppSays() => OnSta(() =>
    {
        using var page = Open(DownloadPage);
        var received = new List<string>();
        page.Web.MessageReceived += m => received.Add(m.Json);
        Assert.True(page.PumpUntil(() => received.Contains("\"ready\"")), "the page never got ready");

        var path = Path.Combine(Path.GetTempPath(), $"hultui-download-{Guid.NewGuid():N}.txt");
        WebDownload? saved = null;
        page.Web.DownloadRequested += r => saved = r.SaveTo(path);
        page.Web.ExecuteScriptAsync("document.getElementById('d').click()");
        try
        {
            Assert.True(page.PumpUntil(() => saved is { State: WebDownloadState.Completed or WebDownloadState.Failed }, seconds: 10),
                $"the download is {saved?.State.ToString() ?? "not asked for"}");
            Assert.Equal(WebDownloadState.Completed, saved!.State);
            Assert.Equal("hello download", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    });

    private static void Click(IWebEngineView view, int x, int y)
    {
        view.SendPointer(new WebPointerInput(WebPointerAction.Move, x, y));
        view.SendPointer(new WebPointerInput(WebPointerAction.Down, x, y, WebPointerButtons.Left, WebPointerButtons.Left, 1));
        view.SendPointer(new WebPointerInput(WebPointerAction.Up, x, y, WebPointerButtons.Left, WebPointerButtons.None, 1));
    }

    /// <summary>The browser process's visible top-level windows: its page windows, and whatever it has popped up.</summary>
    private static HashSet<nint> BrowserWindows(uint process)
    {
        var found = new HashSet<nint>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner == process && IsWindowVisible(hwnd)) found.Add(hwnd);
            return true;
        }, 0);
        return found;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, nint lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref POINT point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    private static nint FocusWindowOf(nint parent)
    {
        nint found = 0;
        EnumChildWindows(parent, (hwnd, _) =>
        {
            var name = new System.Text.StringBuilder(64);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() == "Chrome_WidgetWin_0") { found = hwnd; return false; }
            return true;
        }, 0);
        return found;
    }

    private delegate bool EnumProc(nint hwnd, nint lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumProc proc, nint lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassName(nint hwnd, System.Text.StringBuilder name, int max);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
