using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// Which inputs draw a frame. Every input follows the rule pointer moves always had: a frame
/// when an ordinary (app) handler for an event the input dispatched ran — it may have changed
/// something without marking it — when the hover chain changed, while a drag runs and on the
/// input that ends it, when the focus ring moves, shows or hides, or when something marked
/// itself dirty. Until plan 17's round 4 every other input forced one: a key-up nobody hears,
/// a wheel notch that scrolls nothing and a press on static content each drew a full frame
/// (the up frame was 42–46 % of a key press's frame time in Junction and Vesper Commander).
/// <see cref="PassiveHandlerTests"/> covers the passive registration's side of the rule.
/// </summary>
public class InputFrameTests
{
    private static HeadlessSession Idle(Element root)
    {
        var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(root);
        session.PumpFrame();
        session.PumpFrame();
        return session;
    }

    private static Element Rows(int count)
    {
        var rows = new Element[count];
        for (int i = 0; i < count; i++)
            rows[i] = Box.New().Height(Dimension.Px(30)).FlexShrink(0).Children(Text.New($"Row {i}"));
        return Flex.Column().FlexShrink(0).Children(rows);
    }

    // ── Keys ──

    [Fact]
    public void AKeyUpNobodyHears_DrawsNoFrame_TheKeyDownItsHandlerHeardDoes()
    {
        var root = Flex.Column().FlexGrow(1).Children(Rows(5));
        int downs = 0;
        root.On<KeyDownEvent>(_ => downs++);   // an app's key handler, as Junction's root has
        using var session = Idle(root);

        session.Window.KeyDown(KeyCode.Down);
        Assert.True(session.PumpFrame(), "the key-down ran an app handler, which may have changed anything");
        session.Window.KeyUp(KeyCode.Down);
        Assert.False(session.PumpFrame(), "a key-up no handler hears changed nothing, and drew a frame");
        Assert.Equal(1, downs);
    }

    [Fact]
    public void AKeyNobodyHears_DrawsNoFrame()
    {
        using var session = Idle(Flex.Column().FlexGrow(1).Children(Rows(5)));

        session.Window.KeyDown(KeyCode.A);
        Assert.False(session.PumpFrame(), "a key-down with no handler, no focus and no ring drew a frame");
        session.Window.KeyUp(KeyCode.A);
        Assert.False(session.PumpFrame());
    }

    [Fact]
    public void AnAppKeyUpHandler_StillGetsItsFrame()
    {
        // The safety net, for a handler that may change what is drawn without marking it.
        var root = Flex.Column().FlexGrow(1).Children(Rows(5));
        root.On<KeyUpEvent>(_ => { });
        using var session = Idle(root);

        session.Window.KeyUp(KeyCode.Down);
        Assert.True(session.PumpFrame(), "an app's key-up handler ran and got no frame");
    }

    // ── The wheel ──

    [Fact]
    public void AWheelNotchThatScrollsNothing_DrawsNoFrame()
    {
        var view = ScrollView.New().FlexGrow(1).Children(Rows(3));   // 90 px in 300: nothing to scroll
        using var session = Idle(Flex.Column().FlexGrow(1).Children(view));

        session.Window.Wheel(100, 40, 0, -1);
        Assert.False(session.PumpFrame(), "a notch that scrolled nothing drew a frame");
        Assert.Equal(0, view.ScrollOffsetY);
    }

    [Fact]
    public void AWheelNotchThatScrolls_DrawsAFrame()
    {
        var view = ScrollView.New().FlexGrow(1).Children(Rows(40));
        using var session = Idle(Flex.Column().FlexGrow(1).Children(view));

        session.Window.Wheel(100, 40, 0, -1);
        Assert.True(session.PumpFrame(), "the view scrolled and drew no frame");
        Assert.True(view.ScrollOffsetY > 0);
    }

    [Fact]
    public void AnAppScrollHandler_StillGetsItsFrame()
    {
        var view = ScrollView.New().FlexGrow(1).Children(Rows(3));
        var root = Flex.Column().FlexGrow(1).Children(view);
        root.On<ScrollEvent>(_ => { });
        using var session = Idle(root);

        session.Window.Wheel(100, 40, 0, -1);
        Assert.True(session.PumpFrame(), "an app's wheel handler ran and got no frame");
    }

    // ── The pointer's buttons ──

    [Fact]
    public void APressAndReleaseOnStaticContent_DrawNoFrame()
    {
        using var session = Idle(Flex.Column().FlexGrow(1).Children(Rows(5)));
        session.Window.MoveMouse(100, 40);
        session.PumpFrame();

        session.Window.MouseDown(100, 40);
        Assert.False(session.PumpFrame(), "a press nothing hears drew a frame");
        session.Window.MouseUp(100, 40);
        Assert.False(session.PumpFrame(), "a release and click nothing hears drew a frame");
    }

    [Fact]
    public void AnAppClickHandler_StillGetsItsFrame()
    {
        int clicks = 0;
        var root = Flex.Column().FlexGrow(1).Children(Rows(5));
        root.OnClick(_ => clicks++);
        using var session = Idle(root);
        session.Window.MoveMouse(100, 40);
        session.PumpFrame();

        session.Window.MouseDown(100, 40);
        session.PumpFrame();
        session.Window.MouseUp(100, 40);
        Assert.True(session.PumpFrame(), "an app's click handler ran and got no frame");
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void TheReleaseThatEndsADrag_DrawsAFrame_SoTheGhostGoes()
    {
        var source = Box.New().Width(Dimension.Px(80)).Height(Dimension.Px(30)).Background(new Color(40, 80, 200)).Draggable();
        using var session = Idle(Flex.Column().FlexGrow(1).Children(source, Rows(3)));

        session.Window.MouseDown(40, 15);
        session.PumpFrame();
        for (int i = 1; i <= 5; i++)
        {
            session.Window.MoveMouse(40 + i * 10, 15 + i * 20);
            Assert.True(session.PumpFrame(), "the ghost follows the pointer");
        }
        session.Window.MouseUp(90, 115);
        Assert.True(session.PumpFrame(), "the drag ended with no frame: its ghost stays on the screen");
    }

    // ── Text ──

    [Fact]
    public void TextWithNothingToTypeInto_DrawsNoFrame_TextTypedIntoAField_Does()
    {
        var input = TextInput.New("").Width(Dimension.Px(200));
        using var session = Idle(Flex.Column().FlexGrow(1).Children(input, Rows(3)));

        session.Window.TypeText("a");
        Assert.False(session.PumpFrame(), "text with no focused field drew a frame");

        input.Focus();
        session.PumpFrame();
        session.Window.TypeText("b");
        Assert.True(session.PumpFrame(), "the field took the text and drew no frame");
        Assert.Equal("b", input.Text);
    }

    // ── The focus ring, which the window draws ──

    private static BoxElement Focusable()
    {
        var box = Box.New().Width(Dimension.Px(80)).Height(Dimension.Px(30));
        box.IsFocusable = true;
        box.ShowsFocusVisual = true;
        return box;
    }

    [Fact]
    public void TabMovesTheRing_OntoAnElementThatDrawsNothingOfItsOwn()
    {
        // No handler, no focus style: the ring is the window's alone, and moves with the focus.
        var first = Focusable();
        var second = Focusable();
        using var session = Idle(Flex.Column(gap: 12).Padding(12).Children(first, second));

        session.Window.KeyDown(KeyCode.Tab);
        Assert.Same(first, session.Window.Window.FocusedElement);
        Assert.True(session.PumpFrame(), "the ring came to the first element without a frame");
        session.Window.KeyUp(KeyCode.Tab);
        Assert.False(session.PumpFrame());

        session.Window.KeyDown(KeyCode.Tab);
        Assert.Same(second, session.Window.Window.FocusedElement);
        Assert.True(session.PumpFrame(), "the ring moved without a frame");
    }

    [Fact]
    public void ThePointerHidesTheRing_AndTheKeyboardShowsItAgain()
    {
        var box = Focusable();
        using var session = Idle(Flex.Column(gap: 12).Padding(12).Children(box, Rows(2)));
        session.Window.PressKey(KeyCode.Tab);
        session.PumpFrame();
        var at = box.WindowBorderBox;
        session.Window.MoveMouse(at.X + 10, at.Y + 10);
        session.PumpFrame();

        // A press on the element that has the focus keeps it there: only the ring goes.
        session.Window.MouseDown(at.X + 10, at.Y + 10);
        Assert.Same(box, session.Window.Window.FocusedElement);
        Assert.True(session.PumpFrame(), "the pointer took over and the ring stayed on the screen");
        session.Window.MouseUp(at.X + 10, at.Y + 10);
        Assert.False(session.PumpFrame());

        // A key that means something on its own is the keyboard back: the ring shows.
        session.Window.KeyDown(KeyCode.A);
        Assert.True(session.PumpFrame(), "the keyboard took over and the ring did not come back");
        session.Window.KeyUp(KeyCode.A);
        Assert.False(session.PumpFrame());
    }

    // ── The window's own keys ──

    [Fact]
    public void F12_ShowsTheHudWithAFrame()
    {
        using var session = Idle(Flex.Column().FlexGrow(1).Children(Rows(5)));

        session.Window.KeyDown(KeyCode.F12);
        Assert.True(session.PumpFrame(), "F12 turned the HUD on and drew no frame");
        Assert.True(session.Window.Window.DevTools.IsEnabled);
    }
}
