using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Middle-click autoscroll reaches a <see cref="TextAreaElement"/>.
///
/// <para>It did not, and nothing said so. The gesture looked for a
/// <see cref="ScrollViewElement"/>, which is what carries a list; a text area scrolls its own
/// wrapped rows and is a <c>BoxElement</c>. So every list in every app scrolled under a held
/// middle button and the wall of text those lists exist to open sat still — Meridian's
/// response body being the case that made it obvious, since that is the app's main surface.
/// A reader has no way to know which of the two kinds of scrolling thing they are looking at,
/// which is why the window now matches on <see cref="IAutoScrollable"/> instead of a class.</para>
///
/// <para>The gesture's own mechanics — speed rather than distance, the dead zone, release —
/// are covered once in <see cref="MiddleClickAutoScrollTests"/> and not repeated here. What
/// these add is that the area takes part at all, that it stops where its text ends, and that
/// the press does not do the things a press on text normally does.</para>
/// </summary>
public class TextAreaAutoScrollTests
{
    private const float DeadZone = 14f;   // matches Window.AutoScrollDeadZone
    private const float ViewH = 120f;

    private const string LongText =
        "A line of text long enough to be worth reading, numbered so the assertions can say "
        + "which one they mean";

    [Fact]
    public void HoldingTheMiddleButtonScrollsAReadOnlyArea()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var area = Mount(session, readOnly: true);
        Assert.True(area.CanAutoScroll, "the fixture's text fits — there is nothing to scroll");

        Hold(session, area, dy: DeadZone + 30f, seconds: 0.5);

        Assert.True(area.ScrollOffsetY > 0,
            "holding the middle button below the anchor did not scroll the text area");
    }

    /// <summary>Editable too. Nothing about the gesture depends on the text being read-only,
    /// and an editor is where a reader is most likely to reach for it.</summary>
    [Fact]
    public void AnEditableAreaScrollsAsWell()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var area = Mount(session, readOnly: false);

        Hold(session, area, dy: DeadZone + 30f, seconds: 0.5);

        Assert.True(area.ScrollOffsetY > 0, "an editable area did not scroll");
    }

    [Fact]
    public void ScrollingUpAgainReturnsToTheTop()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var area = Mount(session, readOnly: true);

        Hold(session, area, dy: DeadZone + 40f, seconds: 0.5);
        float down = area.ScrollOffsetY;
        Assert.True(down > 0, "the first leg did not scroll");

        Hold(session, area, dy: -(DeadZone + 40f), seconds: 0.5);

        Assert.True(area.ScrollOffsetY < down, "holding above the anchor did not scroll back up");
    }

    /// <summary>Runs off the end of the text rather than past it. The area clamps its own
    /// offset, which is the half of <see cref="IAutoScrollable"/> the interface cannot check
    /// for an implementer.</summary>
    [Fact]
    public void ItStopsWhereTheTextEnds()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var area = Mount(session, readOnly: true);

        Hold(session, area, dy: DeadZone + 200f, seconds: 4.0);   // far more than the text is tall

        float last = area.ScrollOffsetY;
        Assert.True(last > 0, "nothing scrolled at all");
        Hold(session, area, dy: DeadZone + 200f, seconds: 1.0);
        Assert.Equal(last, area.ScrollOffsetY, 3);   // already at the end, and stayed there
    }

    /// <summary>A short area is not claimed, so the press reaches the app exactly as before.
    /// This is what keeps the change from taking the middle button away from a one-line note
    /// that some app made mean something.</summary>
    [Fact]
    public void AnAreaWithNothingToScrollLeavesTheMiddleButtonAlone()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var area = Mount(session, readOnly: true, text: "one short line");
        Assert.False(area.CanAutoScroll);

        int presses = 0;
        area.On<PointerDownEvent>(e => { if (e.Button == 1) presses++; });
        session.PumpFrame();

        Hold(session, area, dy: DeadZone + 30f, seconds: 0.4);

        Assert.Equal(0f, area.ScrollOffsetY, 3);
        Assert.Equal(1, presses);
    }

    /// <summary>Opting out works here as it does on a list.</summary>
    [Fact]
    public void AutoScrollOffLeavesTheAreaAlone()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var area = Mount(session, readOnly: true);
        area.MiddleClickAutoScroll = false;
        session.PumpFrame();

        Hold(session, area, dy: DeadZone + 30f, seconds: 0.5);

        Assert.Equal(0f, area.ScrollOffsetY, 3);
    }

    /// <summary>The press does not put the caret where the pointer is, and dragging with the
    /// middle button held selects nothing. A text area's primary-button press does both, and
    /// a gesture that scrolled the reader's place away while quietly re-anchoring their
    /// selection would be worse than one that did nothing.</summary>
    [Fact]
    public void TheGestureDoesNotMoveTheCaretOrSelect()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var area = Mount(session, readOnly: false);
        area.Focus();
        session.PumpFrame();
        int caretBefore = area.CursorPosition;

        Hold(session, area, dy: DeadZone + 40f, seconds: 0.5);

        Assert.True(area.ScrollOffsetY > 0, "the gesture did not run, so this proves nothing");
        Assert.Equal(caretBefore, area.CursorPosition);
        Assert.Equal("", area.SelectedText);
    }

    /// <summary>An area inside a scrolling list resolves to the area. The walk goes outwards
    /// from the element under the pointer and takes the first thing that answers, so an inner
    /// surface wins — which is what a pointer inside it means.</summary>
    [Fact]
    public void AnAreaInsideAListScrollsTheAreaRatherThanTheList()
    {
        using var session = HeadlessSession.Create(width: 400, height: 400);
        var area = NewArea(LinesOf(60), readOnly: true).Height(ViewH);
        var filler = Box.New().Height(600).FlexShrink(0);
        var list = ScrollView.New().Height(200).Children(Flex.Column().Children(area, filler));
        session.Window.SetRoot(list);
        session.PumpFrames(2);

        Assert.True(area.CanAutoScroll && list.CanAutoScroll, "both need somewhere to go");

        var box = area.Layout.AbsoluteContentBox;
        float x = box.X + box.Width / 2f, anchor = box.Y + 10f;
        session.Window.MouseDown(x, anchor, MouseButton.Middle);
        session.Window.MoveMouse(x, anchor + DeadZone + 20f);
        Pump(session, 0.4);
        session.Window.MouseUp(x, anchor + DeadZone + 20f, MouseButton.Middle);

        Assert.True(area.ScrollOffsetY > 0, "the inner area did not scroll");
        Assert.Equal(0f, list.ScrollOffsetY, 3);
    }

    // -- Harness --------------------------------------------------------

    private static string LinesOf(int count) =>
        string.Join("\n", Enumerable.Range(0, count).Select(i => $"{i:00}. {LongText}"));

    private static TextAreaElement NewArea(string text, bool readOnly)
    {
        var area = TextArea.New(text);
        area.ReadOnly = readOnly;
        area.FontFamily("Consolas").FontSize(12f);
        return area;
    }

    private static TextAreaElement Mount(HeadlessSession session, bool readOnly, string? text = null)
    {
        var area = NewArea(text ?? LinesOf(60), readOnly).Height(ViewH).Width(300);
        session.Window.SetRoot(Flex.Column(gap: 0).Children(area));
        session.PumpFrames(2);
        return area;
    }

    /// <summary>Presses the middle button in the middle of the area, parks the pointer
    /// <paramref name="dy"/> away from that anchor, and pumps without moving it again.</summary>
    private static void Hold(HeadlessSession session, TextAreaElement area, float dy, double seconds)
    {
        var box = area.Layout.AbsoluteContentBox;
        float x = box.X + box.Width / 2f;
        float anchor = box.Y + box.Height / 2f;
        session.Window.MouseDown(x, anchor, MouseButton.Middle);
        session.Window.MoveMouse(x, anchor + dy);
        Pump(session, seconds);
        session.Window.MouseUp(x, anchor + dy, MouseButton.Middle);
    }

    private static void Pump(HeadlessSession session, double seconds)
    {
        int frames = (int)Math.Round(seconds * 60);
        for (int i = 0; i < frames; i++) session.PumpFrame();
    }
}
