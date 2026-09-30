using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HultUI.Core.Accessibility;
using HultUI.WebView.WebView2.Interop;

namespace HultUI.WebView.WebView2;

/// <summary>
/// Finds the windows a screen reader reaches the page through, for the accessibility bridge
/// to join to the app's tree at the web view (<see cref="NativeAccessibleContent"/>).
/// </summary>
/// <remarks>
/// <para>A composited page has no window where it is drawn, but WebView2 keeps two anyway.</para>
/// <para><b>The focus window</b>: a 0×0 child of the app's window (class
/// <c>Chrome_WidgetWin_0</c>) that holds keyboard focus while the page has it. UI Automation
/// lists it under the app window's root, and everything the browser exposes hangs below it —
/// the page's document some nine levels down, under the embedded browser's own views. That is
/// the window whose tree becomes the web view's children.</para>
/// <para><b>The browser window</b>: a top-level window of the browser process (class
/// <c>Chrome_WidgetWin_1</c>), invisible to the eye and to the mouse, kept exactly over the
/// page on screen. Its providers can say which element is at a point; the focus window's
/// cannot, having no area. That is the window a hit test is sent to.</para>
/// <para><b>What is not used.</b> WebView2 has an API for this
/// (<c>ICoreWebView2CompositionController2::AutomationProvider</c>, and
/// <c>GetAutomationProviderForWindow</c> on the environment): a provider to hand UI Automation
/// for the focus window. Measured, it names nothing, has no children, answers no hit test and
/// no focus query, and reports its rectangle in client coordinates; as an override it made the
/// tree worse. It is still what tells this page's focus window from another page's, because
/// the environment returns the same object for the right window.</para>
/// <para>UI thread throughout: WebView2's objects belong to it.</para>
/// </remarks>
internal sealed unsafe class PageAccessibility
{
    private const string FocusWindowClass = "Chrome_WidgetWin_0";
    private const string BrowserWindowClass = "Chrome_WidgetWin_1";

    /// <summary>The page's focus window, once found; zero before.</summary>
    public nint FocusWindow { get; private set; }

    /// <summary>The browser's window over the page, once found; zero before.</summary>
    public nint BrowserWindow { get; private set; }

    /// <summary>Both windows are known and still exist.</summary>
    public bool IsComplete => FocusWindow != 0 && BrowserWindow != 0
        && Native.IsWindow(FocusWindow) && Native.IsWindow(BrowserWindow);

    /// <summary>What the bridge needs: empty until the focus window is found.</summary>
    public NativeAccessibleContent Content => FocusWindow == 0 ? default : new(FocusWindow, BrowserWindow);

    /// <summary>
    /// Looks for whichever window is not known yet, or is gone. WebView2 makes them in its own
    /// time and moves the browser window after the page's bounds, so a miss is not final: call
    /// again later. True when what <see cref="Content"/> returns has changed.
    /// </summary>
    /// <param name="pageOnScreen">The page's rectangle on screen, which the browser window matches.</param>
    public bool Update(nint parentWindow, nint composition, nint environment, uint browserProcess, Native.RECT pageOnScreen)
    {
        var before = Content;
        if (FocusWindow != 0 && !Native.IsWindow(FocusWindow)) FocusWindow = 0;
        if (BrowserWindow != 0 && !Native.IsWindow(BrowserWindow)) BrowserWindow = 0;

        if (FocusWindow == 0 && parentWindow != 0 && composition != 0 && environment != 0)
            FocusWindow = FindFocusWindow(parentWindow, composition, environment);
        if (BrowserWindow == 0 && browserProcess != 0)
            BrowserWindow = FindBrowserWindow(browserProcess, pageOnScreen);
        return Content != before;
    }

    public void Clear()
    {
        FocusWindow = 0;
        BrowserWindow = 0;
    }

    /// <summary>
    /// The child window that is this page's. Each page has one, all of the same class, so they
    /// are told apart by asking the environment for each one's automation provider and
    /// comparing it with this page's (spike W0 found them the same object).
    /// </summary>
    private static nint FindFocusWindow(nint parent, nint composition, nint environment)
    {
        nint pageProvider = PageProvider(composition);
        if (pageProvider == 0) return 0;
        var env4 = Com.TryQueryInterface(environment, ICoreWebView2Environment4.IID);
        var pageIdentity = Com.TryQueryInterface(pageProvider, Com.IID_IUnknown);
        var candidates = new List<nint>();
        var handle = GCHandle.Alloc(new Search(candidates, FocusWindowClass, 0));
        try
        {
            if (env4 == 0) return 0;
            Native.EnumChildWindows(parent, &Collect, GCHandle.ToIntPtr(handle));
            foreach (var window in candidates)
            {
                nint provider;
                int hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)(*(nint**)env4)[ICoreWebView2Environment4.GetAutomationProviderForWindow])(env4, window, &provider);
                if (hr < 0 || provider == 0) continue;
                var identity = Com.TryQueryInterface(provider, Com.IID_IUnknown);
                bool ours = identity == pageIdentity;
                Com.Release(ref identity);
                Com.Release(ref provider);
                if (ours) return window;
            }
            return 0;
        }
        finally
        {
            handle.Free();
            Com.Release(ref pageIdentity);
            Com.Release(ref env4);
            Com.Release(ref pageProvider);
        }
    }

    /// <summary>ICoreWebView2CompositionController2::AutomationProvider, owned by the caller.</summary>
    private static nint PageProvider(nint composition)
    {
        var composition2 = Com.TryQueryInterface(composition, ICoreWebView2CompositionController2.IID);
        if (composition2 == 0) return 0;
        try
        {
            nint provider;
            int hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)composition2)[ICoreWebView2CompositionController2.get_AutomationProvider])(composition2, &provider);
            return hr >= 0 ? provider : 0;
        }
        finally { Com.Release(ref composition2); }
    }

    /// <summary>
    /// The browser process's visible top-level window that lies exactly over the page. Pages
    /// that share a browser process each have one, and its popups are of the same class, so
    /// the rectangle is what picks this page's. Nothing ties it to the focus window that a
    /// program outside the browser can read.
    /// </summary>
    private static nint FindBrowserWindow(uint browserProcess, Native.RECT pageOnScreen)
    {
        var candidates = new List<nint>();
        var handle = GCHandle.Alloc(new Search(candidates, BrowserWindowClass, browserProcess));
        try { Native.EnumWindows(&Collect, GCHandle.ToIntPtr(handle)); }
        finally { handle.Free(); }

        foreach (var window in candidates)
        {
            if (!Native.IsWindowVisible(window) || !Native.GetWindowRect(window, out var r)) continue;
            // A pixel of slack: the browser rounds the page's edges itself.
            if (Math.Abs(r.Left - pageOnScreen.Left) <= 1 && Math.Abs(r.Top - pageOnScreen.Top) <= 1
                && Math.Abs(r.Right - pageOnScreen.Right) <= 1 && Math.Abs(r.Bottom - pageOnScreen.Bottom) <= 1)
                return window;
        }
        return 0;
    }

    private sealed record Search(List<nint> Found, string ClassName, uint Process);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Collect(nint hwnd, nint state)
    {
        var search = (Search)GCHandle.FromIntPtr(state).Target!;
        if (search.Process != 0)
        {
            uint process;
            Native.GetWindowThreadProcessId(hwnd, &process);
            if (process != search.Process) return 1;
        }
        char* name = stackalloc char[64];
        int length = Native.GetClassNameW(hwnd, name, 64);
        if (new ReadOnlySpan<char>(name, length).SequenceEqual(search.ClassName)) search.Found.Add(hwnd);
        return 1;
    }
}
