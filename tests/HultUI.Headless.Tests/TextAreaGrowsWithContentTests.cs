using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="TextAreaElement.GrowsWithContent"/>: an area in a flowing document is as tall as
/// its wrapped rows, follows edits at once, and pushes what comes after it down — the page
/// scrolls, the area never does. Without it an area with no height of its own measured zero
/// and drew nothing, and one given a height scrolled a paragraph inside a three-line box.
/// </summary>
public class TextAreaGrowsWithContentTests
{
    private const string Paragraph = "the row has to grow with it or the text is clipped and that reads "
        + "as a fault rather than a narrow pane, which is why this exists at every length";

    [Fact]
    public void AnEmptyGrowingArea_IsOneRowTall()
    {
        using var session = HeadlessSession.Create(width: 400, height: 400);
        var (area, _) = Mount(session, "");
        float row = Row(area);
        Assert.True(row > 0);
        Assert.InRange(area.Layout.AbsoluteContentBox.Height, row - 0.5f, row + 0.5f);
    }

    [Fact]
    public void AGrowingArea_IsAsTallAsItsWrappedRows_AndPushesTheNextSiblingDown()
    {
        using var session = HeadlessSession.Create(width: 400, height: 600);
        var (area, below) = Mount(session, Paragraph + "\nsecond\nthird");
        float row = Row(area);
        int rows = Rows(area);
        Assert.True(rows >= 4, $"expected the paragraph to wrap; it took {rows} rows");
        Assert.InRange(area.Layout.AbsoluteContentBox.Height, rows * row - 0.5f, rows * row + 0.5f);
        Assert.True(below.Layout.AbsoluteBorderBox.Y >= area.Layout.AbsoluteBorderBox.Bottom - 0.5f,
            "the element after the area overlaps it");
    }

    [Fact]
    public void Typing_GrowsTheArea_AndDeleting_ShrinksIt()
    {
        using var session = HeadlessSession.Create(width: 400, height: 600);
        var (area, below) = Mount(session, "one line");
        float oneRow = area.Layout.AbsoluteBorderBox.Height;
        float belowAt = below.Layout.AbsoluteBorderBox.Y;

        session.Window.Window.SetFocus(area);
        area.CursorPosition = area.Text.Length;
        session.Window.KeyDown(KeyCode.Enter);
        session.Window.TypeText("two");
        session.Window.KeyDown(KeyCode.Enter);
        session.Window.TypeText("three");
        Pump(session);

        Assert.Equal("one line\ntwo\nthree", area.Text);
        float row = Row(area);
        Assert.InRange(area.Layout.AbsoluteBorderBox.Height, oneRow + 2 * row - 0.5f, oneRow + 2 * row + 0.5f);
        Assert.InRange(below.Layout.AbsoluteBorderBox.Y, belowAt + 2 * row - 0.5f, belowAt + 2 * row + 0.5f);

        for (int i = 0; i < "\nthree".Length; i++) session.Window.KeyDown(KeyCode.Backspace);
        Pump(session);
        Assert.InRange(area.Layout.AbsoluteBorderBox.Height, oneRow + row - 0.5f, oneRow + row + 0.5f);
    }

    [Fact]
    public void ARebuildWithTheSameText_KeepsTheLinesAndTheCaret()
    {
        using var session = HeadlessSession.Create(width: 400, height: 600);
        var (area, _) = Mount(session, Paragraph);
        area.CursorPosition = 10;
        var patch = TextArea.New(Paragraph).GrowsWithContent();
        patch.FontFamily("Consolas").FontSize(12f);
        area.PatchFrom(patch);
        Pump(session);
        Assert.Equal(10, area.CursorPosition);
        Assert.True(area.GrowsWithContent);
        Assert.InRange(area.Layout.AbsoluteContentBox.Height, Rows(area) * Row(area) - 0.5f, Rows(area) * Row(area) + 0.5f);
    }

    [Fact]
    public void Select_SelectsARange_AndTypingReplacesIt()
    {
        using var session = HeadlessSession.Create(width: 400, height: 600);
        var (area, _) = Mount(session, "Lungs: [ ]. Pleura: [ ].");
        session.Window.Window.SetFocus(area);
        int at = area.Text.IndexOf("[ ]", StringComparison.Ordinal);
        area.Select(at, at + 3);
        Assert.True(area.HasSelection);
        Assert.Equal("[ ]", area.SelectedText);
        session.Window.TypeText("clear");
        Pump(session);
        Assert.Equal("Lungs: clear. Pleura: [ ].", area.Text);

        // Out of range is clamped; equal ends are a caret.
        area.Select(-5, 999);
        Assert.Equal(area.Text, area.SelectedText);
        area.Select(3, 3);
        Assert.False(area.HasSelection);
        Assert.Equal(3, area.CursorPosition);
    }

    [Fact]
    public void ANonGrowingArea_StillTakesOnlyTheHeightItIsGiven()
    {
        using var session = HeadlessSession.Create(width: 400, height: 600);
        var area = TextArea.New(Paragraph + "\n" + Paragraph);
        area.FontFamily("Consolas").FontSize(12f).Height(40f);
        session.Window.SetRoot(Flex.Column(gap: 0).Width(220).Children(area));
        Pump(session);
        Assert.InRange(area.Layout.AbsoluteBorderBox.Height, 39.5f, 40.5f);
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private static (TextAreaElement Area, BoxElement Below) Mount(HeadlessSession session, string text)
    {
        var area = TextArea.New(text).GrowsWithContent();
        area.FontFamily("Consolas").FontSize(12f);
        var below = Box.New().Height(10);
        session.Window.SetRoot(Flex.Column(gap: 0).Width(220).Children(area, below));
        Pump(session);
        return (area, below);
    }

    private static void Pump(HeadlessSession session)
    {
        session.PumpFrames(3);
        session.Window.CapturePixels().Dispose();
        session.PumpFrames(1);
    }

    private static float Row(TextAreaElement area) =>
        HultUI.Core.Rendering.TextMeasure.Measure("X", area.ResolvedInherited).Height;

    private static int Rows(TextAreaElement area) =>
        area.GetLineStarts(area.Layout.AbsoluteContentBox.Width, area.ResolvedInherited).Count;
}
