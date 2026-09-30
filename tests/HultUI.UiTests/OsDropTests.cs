using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using HultUI.Core.Builders;
using HultUI.Core.Events;
using HultUI.Platform.SDL3;
using HultUI.WebView;
using HultUI.WebView.WebView2;
using HultUI.WebView.WebView2.Interop;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// A drag from another application, played through the window's real OLE drop target — the
/// object Windows would call — with a data object of files, and no mouse: the target is found
/// the way OLE finds it and called as OLE calls it.
/// </summary>
/// <remarks>
/// What is pinned: HultUI's own drop target is the window's, not SDL's; an element knows the
/// files while the drag hovers and its answer reaches the operating system; and a web page is
/// handed the drag, files and all, through WebView2's drop-target half.
/// </remarks>
[Collection("ui")]
public sealed unsafe class OsDropTests
{
    private const uint MK_LBUTTON = 1, DROPEFFECT_ALL = 1 | 2 | 4;

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

    private static bool PumpUntil(Application app, Func<bool> condition, double seconds = 20)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed.TotalSeconds > seconds) return false;
            app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
            Thread.Sleep(1);
        }
        return true;
    }

    /// <summary>The window's registered drop target, as OLE keeps it.</summary>
    private static nint DropTargetOf(nint hwnd)
    {
        var target = GetPropW(hwnd, "OleDropTargetInterface");
        Assert.NotEqual(0, target);
        return target;
    }

    /// <summary>A point in the window, in layout units, on screen.</summary>
    private static POINTL OnScreen(Application app, float x, float y)
    {
        var window = app.PrimaryWindow;
        var point = new POINT { X = (int)(x * window.DisplayScale), Y = (int)(y * window.DisplayScale) };
        ClientToScreen(window.NativeHandle, ref point);
        return new POINTL { X = point.X, Y = point.Y };
    }

    private static uint DragEnter(nint target, nint data, POINTL at)
    {
        uint effect = DROPEFFECT_ALL;
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, POINTL, uint*, int>)(*(nint**)target)[3])(target, data, MK_LBUTTON, at, &effect);
        return effect;
    }

    private static uint DragOver(nint target, POINTL at)
    {
        uint effect = DROPEFFECT_ALL;
        ((delegate* unmanaged[Stdcall]<nint, uint, POINTL, uint*, int>)(*(nint**)target)[4])(target, MK_LBUTTON, at, &effect);
        return effect;
    }

    private static void DragLeave(nint target) =>
        ((delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)target)[5])(target);

    private static uint Drop(nint target, nint data, POINTL at)
    {
        uint effect = DROPEFFECT_ALL;
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, POINTL, uint*, int>)(*(nint**)target)[6])(target, data, MK_LBUTTON, at, &effect);
        return effect;
    }

    [UiFact]
    public void AnElement_KnowsTheFilesWhileTheyHover_AndTheOperatingSystemIsToldItsAnswer() => OnSta(() =>
    {
        var seen = new List<string>();
        using var app = new Application(new SdlWindowingPlatform(), "Drop", 400, 300);
        app.SetRoot(Box.New().FlexGrow(1).Children(
            Box.New().Width(200).Height(100)
                .OnDragOver(e => { seen.Add($"over {e.DragData.Files.Count}"); e.Accepted = e.DragData.HasFiles; })
                .OnDrop(e => seen.Add("drop " + Path.GetFileName(e.DragData.Files[0])))));
        PumpUntil(app, () => false, seconds: 0.3);

        var file = Path.Combine(Path.GetTempPath(), "hultui-drop-a.txt");
        var data = DropData.Create([file], null);
        try
        {
            var target = DropTargetOf(app.PrimaryWindow.NativeHandle);
            // Over the zone: accepted, and the files were there to decide by.
            Assert.NotEqual(0u, DragEnter(target, data, OnScreen(app, 50, 50)));
            Assert.Equal("over 1", seen[0]);
            // Outside it: refused, so the pointer shows no entry.
            Assert.Equal(0u, DragOver(target, OnScreen(app, 300, 250)));
            Assert.NotEqual(0u, DragOver(target, OnScreen(app, 60, 50)));
            Assert.NotEqual(0u, Drop(target, data, OnScreen(app, 60, 50)));
            Assert.Contains("drop hultui-drop-a.txt", seen);
        }
        finally { Marshal.Release(data); }
    });

    [UiFact]
    public void AWebPage_IsOfferedTheDrag_WithItsFiles_AndItsAnswerReachesTheOperatingSystem() => OnSta(() =>
    {
        if (new WebView2Engine().Availability is { IsAvailable: false }) return;
        using var app = new Application(new SdlWindowingPlatform(), "Drop", 480, 320);
        using var engine = new WebView2Engine();
        using var web = new WebViewController(engine, new WebViewOptions
        {
            UserDataFolder = Path.Combine(Path.GetTempPath(), "hultui-webview2-tests"),
        });
        var received = new List<string>();
        web.MessageReceived += m => received.Add(m.Json);
        app.SetRoot(Flex.Column().Children(WebViewElement.New(web).FlexGrow(1)));
        web.NavigateToString("""
            <html><body style='margin:0;height:100vh'>
              <script>
                document.body.addEventListener('dragenter', e => chrome.webview.postMessage('enter:' + e.dataTransfer.types.join('|')));
                document.body.addEventListener('dragover', e => e.preventDefault());
                chrome.webview.postMessage('ready');
              </script>
            </body></html>
            """);
        Assert.True(PumpUntil(app, () => received.Contains("\"ready\"")), "the page never got ready");

        var data = DropData.Create([Path.Combine(Path.GetTempPath(), "hultui-drop-page.txt")], null);
        try
        {
            var target = DropTargetOf(app.PrimaryWindow.NativeHandle);
            var at = OnScreen(app, 200, 150);
            // A drag flicked into the middle of a drop zone: OLE calls DragOver on every mouse
            // event from the moment the drag enters, before the page has even seen it. The
            // page's acceptance (it cancels its dragover) must still reach OLE, and soon: with
            // every call passed on to WebView2 it took 4 s here, and half a second by hand.
            var clock = Stopwatch.StartNew();
            DragEnter(target, data, at);
            Assert.True(PumpUntil(app, () => DragOver(target, at) != 0, seconds: 1),
                $"the page's acceptance had not reached the operating system after {clock.ElapsedMilliseconds} ms");
            Console.Error.WriteLine($"[timing] accepted after {clock.ElapsedMilliseconds} ms");
            Assert.Contains("\"enter:Files\"", received);
            DragLeave(target);
        }
        finally { Marshal.Release(data); }
        // Not pinned: the drop itself. Played like this, with no DoDragDrop behind it, the
        // browser never finishes the drag — the page never sees drop or dragleave — while a
        // real drag from File Explorer completes (checked by hand; docs/design/WEBVIEW.md,
        // step 6).
    });

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint GetPropW(nint hwnd, string name);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref POINT point);
}
