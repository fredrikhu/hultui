using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using HultUI.Core.Events;
using HultUI.Core.Rendering;
using HultUI.WebView.Cef;
using HultUI.WebView.Cef.Interop;
using Xunit;

namespace HultUI.WebView.Engines.Tests;

/// <summary>
/// Step 5.4 of the Linux engine: navigation and its outcome, new windows, the page's process,
/// the four requests the page waits on, and drags. A small HTTP server on the loopback gives
/// pages with a status, a redirect and a download. Each request is answered here as the
/// controller (or the application through it) would.
/// </summary>
public sealed class CefPolicyTests : IDisposable
{
    /// <summary>What the controller does with a navigation when nothing cancels it. Every test
    /// that drives a <see cref="CefView"/> without a controller subscribes this.</summary>
    internal static void AllowNavigation(WebEngineEvent e)
    {
        if (e is WebEngineEvent.NavigationStarting s) s.Request.Complete();
    }

    private sealed class TestHost : IWebViewHost
    {
        public PixelFrameSink Pixels { get; } = new();
        public void UseGpuRenderer(IGpuFrameRenderer renderer) { }
        public void RequestRepaint() { }
        public void Post(Action action) => ThreadPool.QueueUserWorkItem(_ => action());
    }

    private sealed class Server : IDisposable
    {
        private readonly HttpListener _listener = new();
        public readonly string Root;
        public static readonly byte[] File = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("0123456789", 5000)));

        public Server()
        {
            for (int attempt = 0; ; attempt++)
            {
                int port = Random.Shared.Next(20_000, 60_000);
                Root = $"http://127.0.0.1:{port}/";
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add(Root);
                try { _listener.Start(); break; }
                catch (HttpListenerException) when (attempt < 10) { }
            }
            _ = Serve();
        }

        private async Task Serve()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext c;
                try { c = await _listener.GetContextAsync(); }
                catch { return; }
                _ = Task.Run(() => Answer(c));   // one at a time would hold every page behind a slow one
            }
        }

        private async Task Answer(HttpListenerContext c)
        {
            try
            {
                var r = c.Response;
                switch (c.Request.Url!.AbsolutePath)
                {
                    case "/redirect":
                        r.StatusCode = 302;
                        r.RedirectLocation = Root + "ok";
                        break;
                    case "/slow":
                        await Task.Delay(2000);
                        Write(r, "<title>slow</title>");
                        break;
                    case "/drops":
                        Write(r, Drops);
                        break;
                    case "/missing":
                        r.StatusCode = 404;
                        Write(r, "<title>missing</title>Not here");
                        break;
                    case "/file.bin":
                        r.ContentType = "application/octet-stream";
                        r.AddHeader("Content-Disposition", "attachment; filename=\"file.bin\"");
                        r.ContentLength64 = File.Length;
                        r.OutputStream.Write(File);
                        break;
                    default:
                        Write(r, $"<!doctype html><title>{c.Request.Url.AbsolutePath.Trim('/')}</title><p>Page</p>");
                        break;
                }
                r.Close();
            }
            catch { }
        }

        private static void Write(HttpListenerResponse r, string html)
        {
            var bytes = Encoding.UTF8.GetBytes(html);
            r.ContentType = "text/html; charset=utf-8";
            r.ContentLength64 = bytes.Length;
            r.OutputStream.Write(bytes);
        }

        public void Dispose() => _listener.Close();
    }

    private readonly CefEngine _engine = new();
    private readonly CefView _view;
    private readonly TestHost _host = new();
    private readonly Server _server = new();
    private readonly ConcurrentQueue<WebEngineEvent> _events = new();
    private const float Scale = 1.5f;

    /// <summary>Navigations to cancel, by the last part of their path.</summary>
    private readonly ConcurrentDictionary<string, bool> _refuse = new();
    private bool _holdNavigations;

    public CefPolicyTests()
    {
        _view = (CefView)_engine.CreateView(new WebViewOptions());
        _view.Event += _events.Enqueue;
        _view.Event += e =>
        {
            if (e is not WebEngineEvent.NavigationStarting s || _holdNavigations) return;
            if (_refuse.ContainsKey(s.Request.Uri.AbsolutePath.Trim('/'))) s.Request.Cancel();
            s.Request.Complete();
        };
        _view.Attach(_host);
    }

    public void Dispose()
    {
        _view.Dispose();
        _engine.Dispose();
        _server.Dispose();
    }

    private async Task Ready()
    {
        _view.SetPlacement(new WebViewPlacement(0, new PixelRect(0, 0, 900, 600), Scale));
        await Until(() => _events.Any(e => e is WebEngineEvent.Ready or WebEngineEvent.Failed), 30_000);
        Assert.DoesNotContain(_events, e => e is WebEngineEvent.Failed);
    }

    private async Task Load(string html, string title)
    {
        _view.NavigateToString(html);
        await Until(() => _events.OfType<WebEngineEvent.TitleChanged>().Any(t => t.Title == title));
        await Until(() => _host.Pixels.IntrinsicSize.Width == 900);
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

    private static async Task Until(Func<Task<bool>> done, int ms = 10_000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (!await done())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException();
            await Task.Delay(50);
        }
    }

    private async Task<T> Next<T>(Func<T, bool>? where = null, int ms = 10_000) where T : WebEngineEvent
    {
        T? found = null;
        await Until(() => (found = _events.OfType<T>().FirstOrDefault(e => where?.Invoke(e) ?? true)) is not null, ms);
        return found!;
    }

    private Task<string> Eval(string script) => _view.ExecuteScriptAsync(script);

    private async Task<string> Title() => JsonSerializer.Deserialize<string>(await Eval("document.title"))!;

    private static int D(float css) => (int)MathF.Round(css * Scale);

    private void Click(float cssX, float cssY, WebPointerButtons button = WebPointerButtons.Left)
    {
        _view.SendPointer(new WebPointerInput(WebPointerAction.Move, D(cssX), D(cssY)));
        _view.SendPointer(new WebPointerInput(WebPointerAction.Down, D(cssX), D(cssY), button, button, 1));
        _view.SendPointer(new WebPointerInput(WebPointerAction.Up, D(cssX), D(cssY), button, WebPointerButtons.None, 1));
    }

    // ──── Navigation ────

    [CefFact]
    public async Task ANavigation_StartsAndCompletes_UnderOneId_WithItsStatus()
    {
        await Ready();
        _view.Navigate(new Uri(_server.Root + "ok"));
        var started = await Next<WebEngineEvent.NavigationStarting>(s => s.Request.Uri.AbsolutePath == "/ok");
        Assert.False(started.Request.IsRedirect);
        var done = await Next<WebEngineEvent.NavigationCompleted>(c => c.NavigationId == started.Request.NavigationId);
        Assert.True(done.IsSuccess);
        Assert.Equal(200, done.HttpStatus);
        Assert.Null(done.Error);

        _view.Navigate(new Uri(_server.Root + "missing"));
        started = await Next<WebEngineEvent.NavigationStarting>(s => s.Request.Uri.AbsolutePath == "/missing");
        done = await Next<WebEngineEvent.NavigationCompleted>(c => c.NavigationId == started.Request.NavigationId);
        Assert.False(done.IsSuccess);   // as WebView2: the server's error page loaded, the navigation failed
        Assert.Equal(404, done.HttpStatus);
    }

    [CefFact]
    public async Task ARedirect_IsAskedAgain_UnderTheSameId()
    {
        await Ready();
        _view.Navigate(new Uri(_server.Root + "redirect"));
        var first = await Next<WebEngineEvent.NavigationStarting>(s => s.Request.Uri.AbsolutePath == "/redirect");
        var second = await Next<WebEngineEvent.NavigationStarting>(s => s.Request.Uri.AbsolutePath == "/ok");
        Assert.True(second.Request.IsRedirect);
        Assert.Equal(first.Request.NavigationId, second.Request.NavigationId);
        var done = await Next<WebEngineEvent.NavigationCompleted>(c => c.NavigationId == first.Request.NavigationId);
        Assert.True(done.IsSuccess);
        await Next<WebEngineEvent.SourceChanged>(s => s.Source?.AbsolutePath == "/ok");
    }

    [CefFact]
    public async Task ACancelledNavigation_CompletesCancelled_AndThePageStays()
    {
        await Ready();
        await Load("<title>here</title>", "here");
        _refuse["ok"] = true;
        _view.Navigate(new Uri(_server.Root + "ok"));
        var started = await Next<WebEngineEvent.NavigationStarting>(s => s.Request.Uri.AbsolutePath == "/ok");
        var done = await Next<WebEngineEvent.NavigationCompleted>(c => c.NavigationId == started.Request.NavigationId);
        Assert.False(done.IsSuccess);
        Assert.Equal("Cancelled", done.Error);
        await Task.Delay(300);
        Assert.Single(_events.OfType<WebEngineEvent.NavigationCompleted>(), c => c.NavigationId == started.Request.NavigationId);
        Assert.Equal("here", await Title());
    }

    [CefFact]
    public async Task ANavigationReplaced_WhileLoading_EndsCancelled_AndTheNewOneCompletes()
    {
        await Ready();
        _view.Navigate(new Uri(_server.Root + "slow"));
        var slow = await Next<WebEngineEvent.NavigationStarting>(s => s.Request.Uri.AbsolutePath == "/slow");
        await Task.Delay(100);   // its request is out, its response two seconds away
        _view.Navigate(new Uri(_server.Root + "ok"));
        var ok = await Next<WebEngineEvent.NavigationStarting>(s => s.Request.Uri.AbsolutePath == "/ok");
        var done = await Next<WebEngineEvent.NavigationCompleted>(c => c.NavigationId == ok.Request.NavigationId);
        Assert.True(done.IsSuccess);
        var replaced = await Next<WebEngineEvent.NavigationCompleted>(c => c.NavigationId == slow.Request.NavigationId);
        Assert.Equal("Cancelled", replaced.Error);
        await Task.Delay(2500);
        Assert.Single(_events.OfType<WebEngineEvent.NavigationCompleted>(), c => c.NavigationId == ok.Request.NavigationId);
        Assert.Single(_events.OfType<WebEngineEvent.NavigationCompleted>(), c => c.NavigationId == slow.Request.NavigationId);
        Assert.Equal("ok", await Title());
    }

    [CefFact]
    public async Task AFailedNavigation_IsNamedAsWebView2NamesIt()
    {
        await Ready();
        _view.Navigate(new Uri("http://nowhere.invalid/"));
        var started = await Next<WebEngineEvent.NavigationStarting>(s => s.Request.Uri.Host == "nowhere.invalid");
        var done = await Next<WebEngineEvent.NavigationCompleted>(c => c.NavigationId == started.Request.NavigationId, 20_000);
        Assert.False(done.IsSuccess);
        Assert.Equal("HostNameNotResolved", done.Error);
    }

    [Fact]
    public void ErrorNames_AreWebView2s()
    {
        Assert.Equal("Cancelled", CefView.ErrorText(cef_errorcode_t.ERR_ABORTED));
        Assert.Equal("CannotConnect", CefView.ErrorText(cef_errorcode_t.ERR_CONNECTION_REFUSED));
        Assert.Equal("CertificateExpired", CefView.ErrorText(cef_errorcode_t.ERR_CERT_DATE_INVALID));
        Assert.Equal("Timeout", CefView.ErrorText(cef_errorcode_t.ERR_TIMED_OUT));
        Assert.Equal("Unknown", CefView.ErrorText(cef_errorcode_t.ERR_FAILED));
    }

    [CefFact]
    public async Task ANavigationLeftWaiting_IsCancelledWhenTheViewGoes()
    {
        await Ready();
        _holdNavigations = true;
        _view.Navigate(new Uri(_server.Root + "ok"));
        await Next<WebEngineEvent.NavigationStarting>(s => s.Request.Uri.AbsolutePath == "/ok");
        // CEF's thread waits for the answer; the view going answers it, so CEF's thread is free.
        _view.Dispose();
        Assert.True(CefRuntime.Post(() => { }));
        var free = new TaskCompletionSource();
        CefRuntime.Post(() => free.TrySetResult());
        await free.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // ──── New windows, the page's process ────

    [CefFact]
    public async Task ANewWindow_IsRefused_AndAsked()
    {
        await Ready();
        await Load("""
            <title>links</title><style>body{margin:0}a{position:absolute;left:10px;top:10px;font:40px sans-serif}</style>
            <a href="https://example.invalid/away" target="_blank">away</a>
            """, "links");
        // A click the page has not yet laid out for is lost (rarely, with every test class
        // starting pages at once): click again until the page asks, as a person would.
        var clicked = DateTime.MinValue;
        await Until(() =>
        {
            if (_events.Any(e => e is WebEngineEvent.NewWindowRequested)) return true;
            if (DateTime.UtcNow - clicked > TimeSpan.FromMilliseconds(700)) { Click(40, 30); clicked = DateTime.UtcNow; }
            return false;
        }, 15_000);
        var asked = await Next<WebEngineEvent.NewWindowRequested>();
        Assert.Equal(new Uri("https://example.invalid/away"), asked.Uri);
        Assert.True(asked.IsUserInitiated);
        Assert.Equal("links", await Title());
    }

    [CefFact]
    public async Task ThePagesProcessEnding_IsReported_AndAReloadBringsItBack()
    {
        await Ready();
        await Load("<title>alive</title>", "alive");
        _view.Navigate(new Uri("chrome://kill"));
        var failed = await Next<WebEngineEvent.ProcessFailed>(ms: 20_000);
        Assert.True(failed.CanReload);
        _view.Navigate(new Uri(_server.Root + "back"));
        await Next<WebEngineEvent.TitleChanged>(t => t.Title == "back", 20_000);
    }

    // ──── Context menu ────

    private const string Field = """
        <title>field</title><style>body{margin:0}input{position:absolute;left:20px;top:20px;width:300px;height:30px}</style>
        <input id="t" value="hello world">
        """;

    [CefFact]
    public async Task TheContextMenu_IsTheEnginesEntries_AndAChoiceIsCarriedOut()
    {
        await Ready();
        await Load(Field, "field");
        await Eval("t.focus(); t.setSelectionRange(0, 5)");
        Click(30, 35, WebPointerButtons.Right);   // on the selection, which a right click keeps
        var asked = (await Next<WebEngineEvent.ContextMenuRequested>()).Request;
        Assert.True(asked.Target.IsEditable);
        Assert.True(asked.Target.Kind.HasFlag(WebContextMenuTargetKind.SelectedText));
        Assert.Equal("hello", asked.Target.SelectionText);
        Assert.InRange(asked.X, D(25), D(35));
        Assert.InRange(asked.Y, D(30), D(40));
        var names = asked.Items.Select(i => i.Name).ToList();
        Assert.Contains("cut", names);
        Assert.Contains("copy", names);
        Assert.Contains("paste", names);
        Assert.Contains("selectAll", names);
        Assert.DoesNotContain("back", names);   // in the engine's entries, left out of the standard menu
        asked.Select(asked.Items.First(i => i.Name == "selectAll"));
        await Until(async () => await Eval("[t.selectionStart, t.selectionEnd]") == "[0,11]");

        // Dismissed: nothing happens, and the next one is asked as freshly.
        Click(60, 35, WebPointerButtons.Right);
        await Until(() => _events.OfType<WebEngineEvent.ContextMenuRequested>().Count() == 2);
        _events.OfType<WebEngineEvent.ContextMenuRequested>().Last().Request.Dismiss();
        Assert.Equal("[0,11]", await Eval("[t.selectionStart, t.selectionEnd]"));
    }

    [Fact]
    public void MenuEntries_AreNamedAsWebView2NamesThem()
    {
        Assert.Equal("selectAll", CefView.MenuName((int)cef_menu_id_t.MENU_ID_SELECT_ALL));
        Assert.Equal("pasteAndMatchStyle", CefView.MenuName((int)cef_menu_id_t.MENU_ID_PASTE_MATCH_STYLE));
        Assert.Equal("spellCheck", CefView.MenuName((int)cef_menu_id_t.MENU_ID_SPELLCHECK_SUGGESTION_0 + 2));
        Assert.Equal("viewPageSource", CefView.MenuName((int)cef_menu_id_t.MENU_ID_VIEW_SOURCE));
    }

    // ──── Script dialogs ────

    [CefFact]
    public async Task ScriptDialogs_WaitForTheirAnswers()
    {
        await Ready();
        await Load("<title>dialogs</title>", "dialogs");

        _ = Eval("setTimeout(() => document.title = 'c:' + confirm('Sure?'))");
        var confirm = (await Next<WebEngineEvent.ScriptDialogRequested>(d => d.Request.Kind == WebScriptDialogKind.Confirm)).Request;
        Assert.Equal("Sure?", confirm.Message);
        Assert.Null(confirm.Origin);   // a string document's origin is opaque
        confirm.Accept();
        await Next<WebEngineEvent.TitleChanged>(t => t.Title == "c:true");

        _ = Eval("setTimeout(() => document.title = 'p:' + prompt('Name?', 'Ada'))");
        var prompt = (await Next<WebEngineEvent.ScriptDialogRequested>(d => d.Request.Kind == WebScriptDialogKind.Prompt)).Request;
        Assert.Equal("Ada", prompt.DefaultText);
        prompt.Accept("Grace");
        await Next<WebEngineEvent.TitleChanged>(t => t.Title == "p:Grace");

        _ = Eval("setTimeout(() => document.title = 'q:' + prompt('Again?'))");
        await Until(() => _events.OfType<WebEngineEvent.ScriptDialogRequested>().Count() == 3);
        _events.OfType<WebEngineEvent.ScriptDialogRequested>().Last().Request.Cancel();
        await Next<WebEngineEvent.TitleChanged>(t => t.Title == "q:null");

        _ = Eval("setTimeout(() => { alert('Hi'); document.title = 'a:done'; })");
        var alert = (await Next<WebEngineEvent.ScriptDialogRequested>(d => d.Request.Kind == WebScriptDialogKind.Alert)).Request;
        Assert.Equal("Hi", alert.Message);
        alert.Accept();
        await Next<WebEngineEvent.TitleChanged>(t => t.Title == "a:done");
    }

    [CefFact]
    public async Task LeavingAPageThatAsks_WaitsForTheAnswer()
    {
        await Ready();
        _view.Navigate(new Uri(_server.Root + "asks"));
        await Next<WebEngineEvent.TitleChanged>(t => t.Title == "asks");
        await Eval("addEventListener('beforeunload', e => { e.preventDefault(); e.returnValue = ''; })");
        // beforeunload asks only after the person has used the page; a click can be lost while
        // the page settles, so click until the page counts as used.
        await Until(async () =>
        {
            Click(100, 100);
            return await Eval("navigator.userActivation.hasBeenActive") == "true";
        });

        _view.Navigate(new Uri(_server.Root + "next"));
        var ask = (await Next<WebEngineEvent.ScriptDialogRequested>(d => d.Request.Kind == WebScriptDialogKind.BeforeUnload)).Request;
        Assert.Equal(new Uri(_server.Root.TrimEnd('/')), ask.Origin);
        ask.Cancel();   // stay
        await Task.Delay(500);
        Assert.Equal("asks", await Title());

        _view.Navigate(new Uri(_server.Root + "next"));
        await Until(() => _events.OfType<WebEngineEvent.ScriptDialogRequested>().Count(d => d.Request.Kind == WebScriptDialogKind.BeforeUnload) == 2);
        _events.OfType<WebEngineEvent.ScriptDialogRequested>().Last().Request.Accept();
        await Next<WebEngineEvent.TitleChanged>(t => t.Title == "next");
    }

    [CefFact]
    public async Task ADialogLeftOpen_IsAnsweredWhenTheViewGoes()
    {
        await Ready();
        await Load("<title>open</title>", "open");
        _ = Eval("setTimeout(() => alert('left'))");
        await Next<WebEngineEvent.ScriptDialogRequested>();
        _view.Dispose();
        var closed = new TaskCompletionSource();
        CefRuntime.Post(() => closed.TrySetResult());
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // ──── Downloads ────

    [CefFact]
    public async Task ADownload_IsAsked_AndSavedWhereTold()
    {
        await Ready();
        var folder = Directory.CreateTempSubdirectory("hultui-cef-download-");
        try
        {
            _view.Navigate(new Uri(_server.Root + "file.bin"));
            var asked = (await Next<WebEngineEvent.DownloadRequested>()).Request;
            Assert.Equal("file.bin", asked.SuggestedFileName);
            Assert.Equal("application/octet-stream", asked.MimeType);
            Assert.Equal(Server.File.Length, asked.TotalBytes);
            var path = Path.Combine(folder.FullName, "saved.bin");
            var download = asked.SaveTo(path);
            await Until(() => download.State == WebDownloadState.Completed, 20_000);
            Assert.Equal(Server.File.Length, download.BytesReceived);
            Assert.Equal(Server.File, File.ReadAllBytes(path));
        }
        finally { folder.Delete(recursive: true); }
    }

    [CefFact]
    public async Task ADownloadNobodyWants_IsNotSaved()
    {
        await Ready();
        await Load("<title>stay</title>", "stay");
        _view.Navigate(new Uri(_server.Root + "file.bin"));
        var asked = (await Next<WebEngineEvent.DownloadRequested>()).Request;
        asked.Cancel();
        Assert.Equal(WebDownloadState.Cancelled, asked.Download.State);
        await Task.Delay(500);
        Assert.Equal("stay", await Title());   // a download is no navigation away
        var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "file.bin");
        Assert.False(File.Exists(defaultFolder));
    }

    // ──── Permissions ────

    /// <summary>The process's CEF gets a fake camera and microphone (CEF reads its switches
    /// once, at its start), so a page can ask for them where the machine has none.</summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void FakeMediaDevices()
        => Environment.SetEnvironmentVariable("HULTUI_CEF_SWITCHES", "use-fake-device-for-media-stream");

    private const string AskMedia = "navigator.mediaDevices.getUserMedia({audio:true, video:true})"
        + ".then(s => { s.getTracks().forEach(t => t.stop()); document.title = 'm:ok' + __n++; }, e => document.title = 'm:' + e.name + __n++)";

    [CefFact]
    public async Task TheCameraAndMicrophone_AreAskedOneByOne_AndGivenOnlyIfBothAre()
    {
        await Ready();
        _view.Navigate(new Uri(_server.Root + "asks"));
        await Next<WebEngineEvent.TitleChanged>(t => t.Title == "asks");
        await Eval("window.__n = 0");

        _ = Eval(AskMedia);
        await Until(() => _events.OfType<WebEngineEvent.PermissionRequested>().Count() == 2);
        var asked = _events.OfType<WebEngineEvent.PermissionRequested>().Select(p => p.Request).ToList();
        Assert.Equal([WebPermission.Microphone, WebPermission.Camera], asked.Select(r => r.Permission));
        Assert.All(asked, r => Assert.Equal(new Uri(_server.Root.TrimEnd('/')), r.Origin));
        asked[0].Allow();
        asked[1].Deny();
        await Next<WebEngineEvent.TitleChanged>(t => t.Title == "m:NotAllowedError0");

        _ = Eval(AskMedia);
        await Until(() => _events.OfType<WebEngineEvent.PermissionRequested>().Count() == 4);
        foreach (var p in _events.OfType<WebEngineEvent.PermissionRequested>().Skip(2)) p.Request.Allow();
        await Next<WebEngineEvent.TitleChanged>(t => t.Title == "m:ok1");
    }

    [CefFact]
    public async Task OtherPermissions_AreDeniedByCef_WithoutAsking()
    {
        // CEF's off-screen (Alloy-style) pages have no permission prompts: everything but the
        // camera and microphone is denied before the application hears of it. A Linux limit.
        await Ready();
        _view.Navigate(new Uri(_server.Root + "asks"));
        await Next<WebEngineEvent.TitleChanged>(t => t.Title == "asks");
        Click(100, 100);
        _ = Eval("Notification.requestPermission().then(p => document.title = 'n:' + p)");
        await Next<WebEngineEvent.TitleChanged>(t => t.Title == "n:denied");
        Assert.DoesNotContain(_events, e => e is WebEngineEvent.PermissionRequested);
    }

    // ──── Drags ────

    private const string Drops = """
        <title>drops</title><style>body{margin:0}
        #zone{position:absolute;left:300px;top:50px;width:200px;height:200px;background:#eee}
        #item{position:absolute;left:20px;top:50px;width:100px;height:40px;background:#9cf}
        </style>
        <div id="item" draggable="true">drag me</div><div id="zone"></div>
        <script>
        window.__ev = [];
        for (const n of ['mousedown', 'mouseup']) addEventListener(n, e => __ev.push(n + ' ' + e.clientX + ',' + e.clientY));
        item.addEventListener('dragstart', e => { e.dataTransfer.setData('text/plain', 'from-item'); __ev.push('dragstart'); });
        item.addEventListener('dragend', e => __ev.push('dragend ' + e.dataTransfer.dropEffect));
        zone.addEventListener('dragover', e => { e.preventDefault(); if (__ev.at(-1) !== 'over') __ev.push('over'); });
        zone.addEventListener('drop', e => {
          e.preventDefault();
          const files = [...e.dataTransfer.files].map(f => f.name).join(',');
          __ev.push('drop ' + (files || e.dataTransfer.getData('text/plain')));
        });
        </script>
        """;

    private async Task<List<string>> Recorded()
    {
        await Task.Delay(200);
        var json = await Eval("JSON.stringify(__ev.splice(0))");
        return JsonSerializer.Deserialize<List<string>>(JsonSerializer.Deserialize<string>(json)!)!;
    }

    [CefFact]
    public async Task FilesDraggedIn_AreDroppedWhereThePageTakesThem()
    {
        await Ready();
        _view.Navigate(new Uri(_server.Root + "drops"));
        await Next<WebEngineEvent.TitleChanged>(t => t.Title == "drops");
        await Until(() => _host.Pixels.IntrinsicSize.Width == 900);
        var file = Path.Combine(Path.GetTempPath(), $"hultui-drop-{Environment.ProcessId}.txt");
        File.WriteAllText(file, "dropped");
        try
        {
            WebDragInput At(WebDragAction a, float x, float y) => new(a, D(x), D(y), 0, [file], null, KeyModifiers.None);
            _view.SendDrag(At(WebDragAction.Enter, 100, 300));
            Assert.False(_view.SendDrag(At(WebDragAction.Over, 100, 300)));
            // Over the zone the page takes it; CEF's answer comes after, so ask until it has.
            // Entering the zone is a dragenter there; the step after is its dragover, as in a browser.
            _view.SendDrag(At(WebDragAction.Over, 400, 150));
            _view.SendDrag(At(WebDragAction.Over, 401, 150));
            // The page has the drag over its zone, and says so (CEF's answer comes after the step).
            await Until(async () => await Eval("__ev.includes('over')") == "true");
            await Until(() => _view.SendDrag(At(WebDragAction.Over, 401, 150)));
            _view.SendDrag(At(WebDragAction.Drop, 401, 150));
            await Until(async () => await Eval("__ev.some(e => e.startsWith('drop'))") == "true");
            Assert.Contains($"drop {Path.GetFileName(file)}", await Recorded());
        }
        finally { File.Delete(file); }
    }

    [CefFact]
    public async Task FilesNamedOnlyAtTheDrop_AreDroppedToo()
    {
        // As SDL gives a drag on Linux: positions first, the files with the drop.
        await Ready();
        _view.Navigate(new Uri(_server.Root + "drops"));
        await Next<WebEngineEvent.TitleChanged>(t => t.Title == "drops");
        await Until(() => _host.Pixels.IntrinsicSize.Width == 900);
        var file = Path.Combine(Path.GetTempPath(), $"hultui-late-{Environment.ProcessId}.txt");
        File.WriteAllText(file, "late");
        try
        {
            WebDragInput At(WebDragAction a, float x, IReadOnlyList<string> files) => new(a, D(x), D(150), 0, files, null, KeyModifiers.None);
            _view.SendDrag(At(WebDragAction.Enter, 400, []));
            _view.SendDrag(At(WebDragAction.Over, 400, []));
            _view.SendDrag(At(WebDragAction.Over, 401, []));
            await Until(async () => await Eval("__ev.includes('over')") == "true");
            _view.SendDrag(At(WebDragAction.Drop, 401, [file]));
            await Until(async () => await Eval("__ev.some(e => e.startsWith('drop'))") == "true");
            Assert.Contains($"drop {Path.GetFileName(file)}", await Recorded());
        }
        finally { File.Delete(file); }
    }

    [CefFact]
    public async Task ADragThePageStarts_IsCarriedToADropOnThePage()
    {
        await Ready();
        await Load(Drops, "drops");
        _view.SendPointer(new WebPointerInput(WebPointerAction.Move, D(60), D(70)));
        _view.SendPointer(new WebPointerInput(WebPointerAction.Down, D(60), D(70), WebPointerButtons.Left, WebPointerButtons.Left, 1));
        await Task.Delay(50);
        for (int x = 60; x <= 400; x += 20)
        {
            _view.SendPointer(new WebPointerInput(WebPointerAction.Move, D(x), D(70 + (x - 60) / 4), Held: WebPointerButtons.Left));
            await Task.Delay(15);
        }
        // The pointer keeps moving over the zone, as a hand does, until the page has the drag there.
        int jiggle = 0;
        try
        {
            await Until(async () =>
            {
                _view.SendPointer(new WebPointerInput(WebPointerAction.Move, D(400 + jiggle++ % 2), D(150), Held: WebPointerButtons.Left));
                return await Eval("__ev.includes('over')") == "true";
            });
        }
        catch (TimeoutException) { Assert.Fail("The page had: " + await Eval("JSON.stringify(__ev)")); }
        _view.SendPointer(new WebPointerInput(WebPointerAction.Up, D(400), D(150), WebPointerButtons.Left, WebPointerButtons.None, 1));
        await Until(async () => await Eval("__ev.some(e => e.startsWith('dragend'))") == "true");
        var ev = await Recorded();
        Assert.Contains("dragstart", ev);
        Assert.Contains("drop from-item", ev);
        Assert.Contains(ev, e => e.StartsWith("dragend") && !e.EndsWith("none"));

        // And the pointer is the page's again.
        await Eval("window.__clicked = 0; zone.onclick = () => __clicked++");
        Click(400, 150);
        await Until(async () => await Eval("__clicked") == "1");
    }
}
