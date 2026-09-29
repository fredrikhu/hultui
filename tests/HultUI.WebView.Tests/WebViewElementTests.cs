using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using HultUI.Headless;
using HultUI.WebView.Testing;
using Xunit;

namespace HultUI.WebView.Tests;

/// <summary>
/// <see cref="WebViewElement"/> under the headless host: where it tells the engine the page
/// is, what input reaches the page and in which units, how focus crosses the page's edge,
/// and how it behaves through unmounts and rebuilds.
/// </summary>
public class WebViewElementTests
{
    private static readonly Uri A = new("https://a.test/");

    private sealed class Page : IDisposable
    {
        public required HeadlessSession Session { get; init; }
        public required FakeWebEngine Engine { get; init; }
        public required WebViewController Web { get; init; }
        public required WebViewElement Element { get; init; }
        public FakeWebEngineView View => Engine.Views[0];
        public HeadlessWindow Window => Session.Window;

        public void Dispose()
        {
            Web.Dispose();
            Session.Dispose();
        }
    }

    /// <summary>A 40-unit toolbar over the page, which fills the rest of a 400×300 window.</summary>
    private static Page Show(float scale = 1f, WebEngineCapabilities capabilities = WebEngineCapabilities.None,
        Func<WebViewElement, Element>? around = null, bool recording = false)
    {
        var session = HeadlessSession.Create(400, 300, scale,
            options: recording ? new HeadlessOptions { RecordDrawCalls = true } : null);
        var engine = new FakeWebEngine { Capabilities = capabilities };
        var web = new WebViewController(engine);
        var element = WebViewElement.New(web).FlexGrow(1);
        session.Application.SetRoot(around?.Invoke(element)
            ?? Flex.Column().Children(Box.New().Height(40f).FlexShrink(0), element));
        // One frame draws and works out the placement; the next delivers it.
        session.PumpFrames(2);
        return new Page { Session = session, Engine = engine, Web = web, Element = element };
    }

    private static Element Focusable(string name)
    {
        var box = Box.New().Height(20f).FlexShrink(0);
        box.IsFocusable = true;
        box.Role = AccessibilityRole.Button;
        box.AccessibleName = name;
        return box;
    }

    // ──── Placement and frames ────

    [Fact]
    public void ThePage_IsPlacedInDevicePixels_AfterTheFrame_NotDuringIt()
    {
        using var session = HeadlessSession.Create(400, 300, 1.5f);
        var engine = new FakeWebEngine();
        using var web = new WebViewController(engine);
        var element = WebViewElement.New(web).FlexGrow(1);
        session.Application.SetRoot(Flex.Column().Children(Box.New().Height(40f).FlexShrink(0), element));

        session.PumpFrame();
        Assert.Null(engine.Views[0].Placement);   // worked out in the draw, sent after it

        session.PumpFrame();
        var placement = engine.Views[0].Placement!.Value;
        Assert.Equal(new PixelRect(0, 60, 600, 390), placement.Bounds);
        Assert.Equal(1.5f, placement.Scale);
        Assert.Equal(0, placement.Window);        // headless: no native window
        Assert.True(engine.Views[0].IsVisible);
    }

    [Fact]
    public void ANavigationHeldUntilPlacement_Loads_AndItsFrameIsDrawn()
    {
        using var session = HeadlessSession.Create(400, 300, options: new HeadlessOptions { RecordDrawCalls = true });
        var engine = new FakeWebEngine();
        using var web = new WebViewController(engine);
        web.Navigate(A);                          // before any element: held
        Assert.Equal(WebViewState.Initializing, web.State);

        session.Application.SetRoot(Flex.Column().Children(WebViewElement.New(web).FlexGrow(1)));
        session.PumpFrames(3);

        Assert.Equal(WebViewState.Ready, web.State);
        Assert.Equal(A, web.Source);
        Assert.True(engine.Views[0].FramesPainted > 0);
        session.Window.RenderLog.Clear();
        engine.Views[0].Paint();
        Assert.True(session.PumpFrame());
        Assert.Contains("DrawImage", session.Window.RenderLog.ToString());
    }

    [Fact]
    public void AResize_IsSentAsANewPlacement()
    {
        using var page = Show();
        var before = page.View.Placement!.Value.Bounds;

        page.Element.Height(100f).FlexGrow(0);
        page.Element.MarkLayoutDirty();
        page.Session.PumpFrames(2);

        var after = page.View.Placement!.Value.Bounds;
        Assert.Equal(before.Width, after.Width);
        Assert.Equal(100, after.Height);
    }

    [Fact]
    public void InAnOverlay_ThePlacementIncludesTheOverlaysPosition()
    {
        using var session = HeadlessSession.Create(400, 300, 2f);
        var engine = new FakeWebEngine();
        using var web = new WebViewController(engine);
        session.Application.SetRoot(Box.New());
        session.PumpFrame();

        var popup = Box.New().Padding(10f).Children(WebViewElement.New(web).Width(100f).Height(80f));
        session.Window.Window.ShowOverlay(popup, 50, 30);
        session.PumpFrames(2);

        Assert.Equal(new PixelRect(120, 80, 200, 160), engine.Views[0].Placement!.Value.Bounds);
    }

    [Fact]
    public void TheWindowHasNoNativeHandle_Headless()
    {
        using var session = HeadlessSession.Create();
        Assert.Equal(0, session.Window.Window.NativeHandle);
    }

    // ──── Pointer ────

    [Fact]
    public void PointerInput_ReachesThePage_InDevicePixelsFromItsTopLeft()
    {
        using var page = Show(scale: 1.5f);

        page.Window.MoveMouse(100, 100);
        page.Window.MouseDown(100, 100);
        page.Window.MouseUp(102, 101);

        var p = page.View.Pointers;
        var move = p.First(i => i.Action == WebPointerAction.Move);
        Assert.Equal((150, 90), (move.X, move.Y));   // (100, 100 - 40) × 1.5

        var down = p.Single(i => i.Action == WebPointerAction.Down);
        Assert.Equal(WebPointerButtons.Left, down.Button);
        Assert.Equal(WebPointerButtons.Left, down.Held);
        Assert.Equal(1, down.ClickCount);

        var up = p.Single(i => i.Action == WebPointerAction.Up);
        Assert.Equal((153, 91), (up.X, up.Y));
        Assert.Equal(WebPointerButtons.None, up.Held);
    }

    [Fact]
    public void ADragThatLeavesThePage_KeepsReachingIt_UntilRelease()
    {
        using var page = Show();

        page.Window.MouseDown(100, 100);
        page.Window.MoveMouse(100, 10);          // over the toolbar, outside the page
        page.Window.MouseUp(100, 10);

        var outside = page.View.Pointers.Where(i => i.Y < 0).ToList();
        Assert.Contains(outside, i => i.Action == WebPointerAction.Move && i.Held == WebPointerButtons.Left);
        Assert.Contains(outside, i => i.Action == WebPointerAction.Up);
    }

    [Fact]
    public void AReleaseWhosePressThePageNeverSaw_IsNotForwarded()
    {
        using var page = Show();

        page.Window.MouseUp(100, 100);

        Assert.DoesNotContain(page.View.Pointers, i => i.Action == WebPointerAction.Up);
    }

    [Fact]
    public void TheWheel_ScrollsThePage_NotTheScrollViewAroundIt()
    {
        ScrollViewElement? scroll = null;
        using var page = Show(around: element =>
        {
            scroll = ScrollView.New();
            scroll.Children(Flex.Column().Children(
                element.FlexGrow(0).Height(200f),
                Box.New().Height(800f).FlexShrink(0)));
            return Flex.Column().Children(scroll.FlexGrow(1));
        });

        page.Window.Wheel(100, 100, 0, -2);

        var wheel = page.View.Pointers.Single(i => i.Action == WebPointerAction.Wheel);
        Assert.Equal(-2f, wheel.WheelY);
        Assert.Equal(0f, scroll!.ScrollOffsetY);
    }

    // ──── Focus ────

    [Fact]
    public void TabIntoThePage_StartsAtItsFirstStop_ShiftTab_AtItsLast_AndAClickWhereClicked()
    {
        var first = Focusable("first");
        var last = Focusable("last");
        using var page = Show(capabilities: WebEngineCapabilities.NativeKeyboard,
            around: element => Flex.Column().Children(first, element, last));

        first.Focus();
        page.Window.PressKey(KeyCode.Tab);
        Assert.True(page.Element.IsFocused);
        Assert.Equal(FocusReason.Next, page.View.LastFocusReason);

        last.Focus();
        page.Window.PressKey(KeyCode.Tab, KeyModifiers.Shift);
        Assert.True(page.Element.IsFocused);
        Assert.Equal(FocusReason.Previous, page.View.LastFocusReason);

        last.Focus();
        Assert.Equal("Blur", page.View.Calls[^1]);
        page.Window.Click(page.Element);
        Assert.Equal(FocusReason.Pointer, page.View.LastFocusReason);
    }

    [Fact]
    public void TabbingOutOfThePage_MovesFocusOnToTheWindowsNextElement()
    {
        var first = Focusable("first");
        var last = Focusable("last");
        using var page = Show(around: element => Flex.Column().Children(first, element, last));
        page.Element.Focus();

        page.View.RaiseFocusLeaving(forward: true);
        Assert.True(last.IsFocused);
        Assert.False(page.View.HasFocus);

        page.Element.Focus();
        page.View.RaiseFocusLeaving(forward: false);
        Assert.True(first.IsFocused);
    }

    [Fact]
    public void TabbingOutOfAPageWithNothingElseFocusable_WrapsInsideThePage()
    {
        using var page = Show(around: element => Flex.Column().Children(element));
        page.Element.Focus();

        page.View.RaiseFocusLeaving(forward: true);

        Assert.True(page.Element.IsFocused);
        Assert.Equal(FocusReason.Next, page.View.LastFocusReason);
        Assert.Equal("Focus Next", page.View.Calls[^1]);
    }

    // ──── Keys ────

    [Fact]
    public void Keys_AreForwarded_ToAnEngineThatTakesThem_TabIncluded()
    {
        var after = Focusable("after");
        using var page = Show(around: element => Flex.Column().Children(element, after));
        page.Window.Click(page.Element);

        page.Window.PressKey(KeyCode.A, KeyModifiers.Ctrl);
        page.Window.PressKey(KeyCode.Tab);
        page.Window.TypeText("hé");

        Assert.Equal(
            [new WebKeyInput(true, KeyCode.A, KeyModifiers.Ctrl), new WebKeyInput(false, KeyCode.A, KeyModifiers.Ctrl),
             new WebKeyInput(true, KeyCode.Tab, KeyModifiers.None), new WebKeyInput(false, KeyCode.Tab, KeyModifiers.None)],
            page.View.Keys);
        Assert.Equal(["h", "é"], page.View.Texts.Select(t => t.Text));
        Assert.True(page.Element.IsFocused);    // the page decides when Tab leaves it
    }

    [Fact]
    public void Keys_AreNotForwarded_ToAnEngineWithItsOwnKeyboard()
    {
        var after = Focusable("after");
        using var page = Show(capabilities: WebEngineCapabilities.NativeKeyboard,
            around: element => Flex.Column().Children(element, after));
        page.Window.Click(page.Element);

        page.Window.PressKey(KeyCode.Tab);

        Assert.Empty(page.View.Keys);
        Assert.True(after.IsFocused);
    }

    // ──── Title, cursor, accessibility ────

    [Fact]
    public void ThePagesTitle_NamesIt_UnlessTheAppDid()
    {
        using var page = Show();
        page.Engine.Pages[A] = new FakePage("Docs home", Color.White);
        page.Web.Navigate(A);
        page.Session.PumpFrame();

        Assert.NotNull(page.Window.FindByRole(AccessibilityRole.Group, "Docs home"));

        page.Element.AccessibleName = "Help";
        page.View.RaiseTitle("Another title");
        page.Session.PumpFrame();
        Assert.NotNull(page.Window.FindByRole(AccessibilityRole.Group, "Help"));
    }

    [Fact]
    public void TheCursor_IsThePages()
    {
        using var page = Show();

        page.View.RaiseCursor(CursorType.Pointer);

        Assert.Equal(CursorType.Pointer, page.Element.Inherited.Cursor);
    }

    // ──── Lifetime ────

    [Fact]
    public void Unmounting_HidesThePage_AndRemounting_ShowsItWithoutReloading()
    {
        var panel = Box.New();
        using var page = Show(around: element =>
        {
            panel.FlexGrow(1).Children(element);
            return Flex.Column().Children(panel);
        });
        page.Web.Navigate(A);
        var root = panel.Parent!;

        root.RemoveChild(panel);
        Assert.False(page.View.IsVisible);
        Assert.False(page.Element.IsListening);

        root.AddChild(panel);
        page.Session.PumpFrames(2);
        Assert.True(page.View.IsVisible);
        Assert.Single(page.View.Calls, c => c.StartsWith("Navigate", StringComparison.Ordinal));
        Assert.Equal(A, page.Web.Source);
    }

    /// <summary>A component that builds a fresh web view element on every rebuild.</summary>
    private sealed class Rebuilding(WebViewController web) : Component
    {
        public Signal<int> Generation { get; } = new(0);
        public List<WebViewElement> Built { get; } = [];

        public override Element Build()
        {
            _ = Generation.Value;
            var element = WebViewElement.New(web).Width(300f).Height(200f);
            Built.Add(element);
            return Box.New().Children(element);
        }
    }

    private static object?[] Listeners(WebViewController web)
    {
        var field = typeof(WebViewController).GetField(nameof(WebViewController.FrameArrived), BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WebViewController.FrameArrived is no longer a field-like event");
        return ((Delegate?)field.GetValue(web))?.GetInvocationList().Select(d => d.Target).ToArray() ?? [];
    }

    [Fact]
    public void ARebuild_KeepsOneListeningElement_AndThePagesCursorAndName()
    {
        using var session = HeadlessSession.Create(400, 300);
        var engine = new FakeWebEngine();
        using var web = new WebViewController(engine);
        var view = new Rebuilding(web);
        session.Application.SetRoot(view.Mount());
        session.PumpFrames(2);
        engine.Views[0].RaiseCursor(CursorType.Text);
        engine.Views[0].RaiseTitle("Kept");

        for (int i = 1; i <= 3; i++)
        {
            view.Generation.Value = i;
            session.PumpFrame();
        }

        var live = view.Built[0];
        Assert.All(view.Built.Skip(1), twin => Assert.False(twin.IsListening));
        Assert.Same(live, Assert.Single(Listeners(web)));
        Assert.Equal(CursorType.Text, live.Inherited.Cursor);
        Assert.Equal("Kept", live.AccessibleName);
        Assert.Single(engine.Views);
    }
}
