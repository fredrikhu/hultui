using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace HultUI.Headless.Tests;

// Inside a HultUI.* namespace a bare `Text` binds to the HultUI.Text *namespace*.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// VirtualListElement's opt-in keyboard scrolling. The interesting contract is what it
/// does NOT take: a chorded key must bubble so application shortcuts still work while
/// the list has focus (a hand-rolled version of this swallowed Ctrl+arrows and broke
/// commit stepping in Junction).
/// </summary>
public class VirtualListKeyboardScrollTests
{
    private const int RowHeight = 20;
    private const int Rows = 200;
    private const int ViewportHeight = 200;

    private static (HeadlessSession Session, VirtualListElement<int> List) Make(bool keyboardScroll)
    {
        var session = HeadlessSession.Create(width: 300, height: ViewportHeight);
        var list = VirtualListElement<int>.New()
            .ItemHeight(RowHeight)
            .Items(Enumerable.Range(0, Rows).ToArray())
            .RenderWith(i => Text.New($"row {i}"))
            .FlexGrow(1)
            .KeyboardScroll(keyboardScroll);
        list.IsFocusable = true;
        list.TabIndex = 0;
        session.Window.SetRoot(list);
        session.PumpFrame();
        list.Focus();
        session.PumpFrame();
        return (session, list);
    }

    [Fact]
    public void Down_ScrollsByOneRow()
    {
        var (session, list) = Make(keyboardScroll: true);
        using var _ = session;

        session.Window.PressKey(KeyCode.Down);
        session.PumpFrame();

        Assert.Equal(RowHeight, list.ScrollOffsetY);
    }

    [Fact]
    public void Up_StopsAtTheTop()
    {
        var (session, list) = Make(keyboardScroll: true);
        using var _ = session;

        session.Window.PressKey(KeyCode.Up);
        session.PumpFrame();

        Assert.Equal(0, list.ScrollOffsetY);   // clamped, not negative
    }

    [Fact]
    public void PageDown_ScrollsAViewportKeepingOneRowOfContext()
    {
        var (session, list) = Make(keyboardScroll: true);
        using var _ = session;

        session.Window.PressKey(KeyCode.PageDown);
        session.PumpFrame();

        Assert.True(list.ScrollOffsetY > RowHeight, $"expected a page, got {list.ScrollOffsetY}");
        Assert.True(list.ScrollOffsetY <= ViewportHeight, $"overshot a viewport: {list.ScrollOffsetY}");
    }

    [Fact]
    public void EndThenHome_JumpToBothEnds()
    {
        var (session, list) = Make(keyboardScroll: true);
        using var _ = session;

        session.Window.PressKey(KeyCode.End);
        session.PumpFrame();
        var atEnd = list.ScrollOffsetY;
        Assert.True(atEnd > 0, "End did not scroll");
        Assert.True(atEnd <= Rows * RowHeight, "End scrolled past the content");

        session.Window.PressKey(KeyCode.Home);
        session.PumpFrame();
        Assert.Equal(0, list.ScrollOffsetY);
    }

    [Theory]
    [InlineData(KeyModifiers.Ctrl)]
    [InlineData(KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Super)]
    [InlineData(KeyModifiers.Shift)]
    public void ChordedArrows_AreLeftForTheApp(KeyModifiers modifier)
    {
        var (session, list) = Make(keyboardScroll: true);
        using var _ = session;

        session.Window.PressKey(KeyCode.Down, modifier);
        session.PumpFrame();

        Assert.Equal(0, list.ScrollOffsetY);
    }

    [Fact]
    public void LockStates_DoNotSuppressScrolling()
    {
        // KeyModifiers carries CapsLock/NumLock, but those are lock states rather than
        // held keys — treating them as a chord would silently disable scrolling for
        // anyone with NumLock on.
        var (session, list) = Make(keyboardScroll: true);
        using var _ = session;

        session.Window.PressKey(KeyCode.Down, KeyModifiers.NumLock | KeyModifiers.CapsLock);
        session.PumpFrame();

        Assert.Equal(RowHeight, list.ScrollOffsetY);
    }

    [Fact]
    public void Disabled_ByDefault()
    {
        var (session, list) = Make(keyboardScroll: false);
        using var _ = session;

        session.Window.PressKey(KeyCode.Down);
        session.PumpFrame();

        Assert.Equal(0, list.ScrollOffsetY);
    }

    [Fact]
    public void UnrelatedKeys_Bubble()
    {
        var (session, list) = Make(keyboardScroll: true);
        using var _ = session;
        bool sawEscape = false;
        list.On<KeyDownEvent>(e => { if (e.Key == KeyCode.Escape) sawEscape = true; });

        session.Window.PressKey(KeyCode.Escape);
        session.PumpFrame();

        Assert.True(sawEscape, "a key the list doesn't scroll on must still reach other handlers");
        Assert.Equal(0, list.ScrollOffsetY);
    }
}
