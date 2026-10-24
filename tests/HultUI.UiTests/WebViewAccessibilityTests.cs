using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace HultUI.UiTests;

using Application = FlaUI.Core.Application;
using Window = FlaUI.Core.AutomationElements.Window;

/// <summary>
/// A screen reader's view of a web page in a HultUI window, through the same UI Automation
/// client interfaces it uses: the WebView sample, real WebView2, real Chromium providers.
/// </summary>
/// <remarks>
/// <para>The page is composited — HultUI draws it, and there is no window where it appears —
/// so nothing about this works by default the way it does for a windowed browser. What is
/// pinned here is what the bridge and the engine add: the page's tree hangs under the web
/// view element and nowhere else, a point over the page resolves to the page's own element,
/// both still hold after the window moves, and focus inside the page is reported as the
/// page's element.</para>
/// <para>The first three were seen to fail first: the tree under the window root with the
/// element empty, hit tests returning the element's box, rectangles left behind by a move.
/// Focus worked before any of this — the browser's focus window reports it — and is here so
/// that joining the trees does not take it away.</para>
/// <para>The focus test makes the sample's window the foreground, and the hit test puts it
/// above every window (<c>HWND_TOPMOST</c>), so these run only with
/// <c>HULTUI_UITESTS_FOREGROUND=1</c> as well as <c>HULTUI_UITESTS=1</c>
/// (<see cref="UiForegroundFactAttribute"/>), on a desktop nobody is using.</para>
/// </remarks>
[Collection("ui")]
public sealed class WebViewAccessibilityTests : IDisposable
{
    private const string PageButton = "Send a message to the app";

    private readonly Application _app;
    private readonly UIA3Automation _automation;
    private readonly Window _window;

    public WebViewAccessibilityTests()
    {
        _app = Application.Launch(SamplePath.WebView());
        _automation = new UIA3Automation();
        _window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(15))
            ?? throw new InvalidOperationException("sample window did not appear");
    }

    /// <summary>
    /// The web view element, by the AutomationId the sample gives it. The window is on screen
    /// before its first tree is, so the first look can come up empty.
    /// </summary>
    private AutomationElement Page() =>
        Retry.WhileNull(() => _window.FindFirstDescendant(cf => cf.ByAutomationId("page")),
            timeout: TimeSpan.FromSeconds(10), interval: TimeSpan.FromMilliseconds(100)).Result
        ?? throw new InvalidOperationException("no web view element in the window's tree");

    /// <summary>
    /// The page's button, once the browser has built its tree — it starts when the first
    /// client asks, so the first look usually finds nothing.
    /// </summary>
    private AutomationElement Button()
    {
        var found = Retry.WhileNull(
            () => Page().FindFirstDescendant(cf => cf.ByName(PageButton)),
            timeout: TimeSpan.FromSeconds(20), interval: TimeSpan.FromMilliseconds(250));
        return found.Result ?? throw new InvalidOperationException("the page's button never appeared under the web view element");
    }

    [UiForegroundFact]
    public void ThePagesTree_HangsUnderTheWebViewElement_AndNowhereElse()
    {
        var button = Button();
        Assert.Equal(ControlType.Button, button.ControlType);

        // The browser's window is a child of ours, and UI Automation's own idea is to list it
        // under the window root. It has to have moved, not been copied.
        var walker = _automation.TreeWalkerFactory.GetRawViewWalker();
        var underRoot = new List<string>();
        for (var child = walker.GetFirstChild(_window); child is not null; child = walker.GetNextSibling(child))
            underRoot.Add(child.Properties.ClassName.ValueOrDefault ?? "");
        Assert.DoesNotContain(underRoot, c => c.StartsWith("Chrome_", StringComparison.Ordinal));

        // And the element is a control a reader stops at, with its place and size on screen —
        // the browser's window for it has neither.
        var page = Page();
        Assert.True(page.Properties.IsControlElement.Value);
        var box = page.BoundingRectangle;
        Assert.True(box.Width > 100 && box.Height > 100, $"web view element is {box}");
        Assert.True(box.Contains(button.BoundingRectangle), $"button {button.BoundingRectangle} outside the page {box}");
    }

    [UiForegroundFact]
    public void APointOverThePage_ResolvesToThePagesOwnElement()
    {
        var button = Button().BoundingRectangle;
        var page = Page().BoundingRectangle;

        using (OnTop())
        {
            var atButton = HitTest(Centre(button), e => e.Properties.Name.ValueOrDefault == PageButton);
            Assert.True(atButton.Properties.Name.ValueOrDefault == PageButton && atButton.ControlType == ControlType.Button,
                "at the button: " + Describe(atButton));

            // Nothing is in the page's bottom right corner but the document itself.
            var atNothing = _automation.FromPoint(new System.Drawing.Point(page.Right - 20, page.Bottom - 20));
            Assert.True(atNothing.ControlType == ControlType.Document, "at an empty corner: " + Describe(atNothing));
        }
    }

    [UiForegroundFact]
    public void MovingTheWindow_TakesThePagesElementsWithIt()
    {
        var before = Button().BoundingRectangle;
        var handle = _window.Properties.NativeWindowHandle.Value;
        GetWindowRect(handle, out var frame);

        const int dx = 70, dy = 45;
        SetWindowPos(handle, IntPtr.Zero, frame.Left + dx, frame.Top + dy, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

        // The browser reports its elements in screen coordinates and only knows where the
        // window is when the engine tells it.
        System.Drawing.Rectangle after = default;
        bool moved = Retry.WhileFalse(() =>
        {
            after = Button().BoundingRectangle;
            return Math.Abs(after.X - (before.X + dx)) <= 1 && Math.Abs(after.Y - (before.Y + dy)) <= 1;
        }, timeout: TimeSpan.FromSeconds(5), interval: TimeSpan.FromMilliseconds(100)).Result;
        Assert.True(moved, $"button was at {before}, window moved by ({dx},{dy}), button now at {after}");

        using (OnTop())
        {
            var atButton = HitTest(Centre(after), e => e.Properties.Name.ValueOrDefault == PageButton);
            Assert.True(atButton.Properties.Name.ValueOrDefault == PageButton, "at the button after the move: " + Describe(atButton));
        }
    }

    [UiForegroundFact]
    public void FocusInsideThePage_IsReportedAsThePagesElement()
    {
        var button = Button();
        HostedInput.EnsureForeground(_window.Properties.NativeWindowHandle.Value);

        // Focus goes to the web view the way a reader sends it, and on into the page.
        Page().Focus();
        button.Focus();

        string? name = null;
        bool focused = Retry.WhileFalse(() =>
        {
            name = _automation.FocusedElement()?.Properties.Name.ValueOrDefault;
            return name == PageButton;
        }, timeout: TimeSpan.FromSeconds(5), interval: TimeSpan.FromMilliseconds(100)).Result;
        Assert.True(focused, $"the focused element is \"{name}\"");
    }

    /// <summary>
    /// The element at a point, asked again for a few seconds until it is the one expected.
    /// The engine finds the browser's hit-test window a moment after the page's tree — it
    /// waits for the browser to move that window over the page — and until then a point over
    /// the page is answered with the web view element, which is right, only not yet precise.
    /// </summary>
    private AutomationElement HitTest(System.Drawing.Point point, Func<AutomationElement, bool> expected)
    {
        AutomationElement hit = _automation.FromPoint(point);
        for (int i = 0; i < 30 && !expected(hit); i++)
        {
            Thread.Sleep(100);
            hit = _automation.FromPoint(point);
        }
        return hit;
    }

    /// <summary>
    /// What a hit test found, for a failure message. An element of some other application —
    /// the sample was not on top — is only said to be that, not named.
    /// </summary>
    private string Describe(AutomationElement element)
    {
        if (element.Properties.ProcessId.ValueOrDefault != _app.ProcessId
            && element.Properties.FrameworkId.ValueOrDefault != "Chrome")
            return "an element of another application (the sample was not the window under the point)";
        return $"{element.Properties.ControlType.ValueOrDefault} \"{element.Properties.Name.ValueOrDefault}\" " +
               $"class \"{element.Properties.ClassName.ValueOrDefault}\" at {element.Properties.BoundingRectangle.ValueOrDefault}";
    }

    private static System.Drawing.Point Centre(System.Drawing.Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    /// <summary>
    /// Puts the sample above other windows without giving it the keyboard: a hit test starts
    /// from the window that is under the point on screen.
    /// </summary>
    private IDisposable OnTop()
    {
        var handle = _window.Properties.NativeWindowHandle.Value;
        SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        Thread.Sleep(200);
        return new Restore(() => SetWindowPos(handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE));
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    public void Dispose()
    {
        _automation.Dispose();
        try { _app.Close(); } catch { /* best effort */ }
        _app.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static readonly IntPtr HWND_TOPMOST = -1, HWND_NOTOPMOST = -2;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
