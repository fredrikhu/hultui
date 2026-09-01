using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Cross-label (group) text selection driven by the window: a drag across labels
/// sharing a <see cref="TextElement.SelectionGroup"/> selects the span, and Ctrl+C
/// copies it. The subtle cases are inside a scrolled container — row layout is
/// unscrolled (the renderer applies the scroll offset), so the window must hit-test
/// screen points, not compare raw layout Y — and the drag must not "stick" so a plain
/// hover afterwards keeps extending it.
/// </summary>
public class GroupSelectionDragTests
{
    private const float RowH = 20f;

    private static (ScrollViewElement scroll, TextElement[] rows, TextSelectionGroup group) Build(HeadlessSession session)
    {
        var group = new TextSelectionGroup();
        group.Bind("rows", 10, i => $"line number {i}");
        var rows = new TextElement[10];
        for (int i = 0; i < rows.Length; i++)
        {
            var el = TextElement.New($"line number {i}");
            el.Selectable = true;
            el.SelectionGroup = group;
            el.SelectionIndex = i;
            el.Box.Height = RowH;
            el.Flex.FlexShrink = 0;
            rows[i] = el;
        }
        // 10 rows of 20 → 200 of content in a 100-tall viewport.
        var scroll = ScrollView.New().Width(200).Height(100)
            .Children(Flex.Column().Children(rows.Cast<Element>().ToArray()));
        session.Window.SetRoot(scroll);
        session.PumpFrame();
        return (scroll, rows, group);
    }

    // Screen Y of a row's vertical center (layout is unscrolled; render translates by offset).
    private static float ScreenMidY(ScrollViewElement s, TextElement row)
        => row.Layout.AbsoluteBorderBox.Y - s.ScrollOffsetY + RowH / 2f;

    [Fact]
    public void DragWhenScrolled_SelectsBetweenPressedRows_NotFromTop()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var (scroll, rows, _) = Build(session);

        scroll.ScrollOffsetY = 60f;   // rows 0..2 scroll off the top
        session.PumpFrame();

        // Drag from row 4 down to row 6, all on-screen.
        session.Window.MouseDown(40f, ScreenMidY(scroll, rows[4]));
        session.Window.MoveMouse(40f, ScreenMidY(scroll, rows[5]));
        session.Window.MoveMouse(40f, ScreenMidY(scroll, rows[6]));
        session.Window.MouseUp(40f, ScreenMidY(scroll, rows[6]));

        // The off-screen top rows must NOT be selected (the bug anchored the selection
        // to the first row because it compared the screen point to unscrolled layout Y).
        Assert.False(rows[0].HasSelection);
        Assert.False(rows[3].HasSelection);
        // The dragged span is selected.
        Assert.True(rows[4].HasSelection);
        Assert.True(rows[5].HasSelection);
        Assert.True(rows[6].HasSelection);
        Assert.Equal("line number 5", rows[5].SelectedText);   // middle row fully selected
        Assert.False(rows[7].HasSelection);
    }

    [Fact]
    public void HoverAfterDrag_DoesNotKeepSelecting()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var (scroll, rows, _) = Build(session);

        session.Window.MouseDown(40f, ScreenMidY(scroll, rows[1]));
        session.Window.MoveMouse(40f, ScreenMidY(scroll, rows[3]));
        session.Window.MouseUp(40f, ScreenMidY(scroll, rows[3]));

        var anchorSelection = rows[1].SelectedText;

        // Plain hover (no button) over the anchor row must not change its selection —
        // the anchor's drag flag has to be cleared on mouse-up.
        session.Window.MoveMouse(10f, ScreenMidY(scroll, rows[1]));
        session.Window.MoveMouse(90f, ScreenMidY(scroll, rows[1]));

        Assert.Equal(anchorSelection, rows[1].SelectedText);
    }

    [Fact]
    public void SeparateGroups_DoNotSelectAcross()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var groupA = new TextSelectionGroup();
        var groupB = new TextSelectionGroup();
        groupA.Bind("a", 2, i => i == 0 ? "alpha zero" : "alpha one");
        groupB.Bind("b", 1, _ => "beta zero");
        TextElement Row(string t, TextSelectionGroup g, int index)
        {
            var el = TextElement.New(t);
            el.Selectable = true; el.SelectionGroup = g; el.SelectionIndex = index;
            el.Box.Height = RowH; el.Flex.FlexShrink = 0;
            return el;
        }
        var a0 = Row("alpha zero", groupA, 0);
        var a1 = Row("alpha one", groupA, 1);
        var b0 = Row("beta zero", groupB, 0);
        var scroll = ScrollView.New().Width(200).Height(100)
            .Children(Flex.Column().Children(a0, a1, b0));
        session.Window.SetRoot(scroll);
        session.PumpFrame();

        float mid(TextElement r) => r.Layout.AbsoluteBorderBox.Y - scroll.ScrollOffsetY + RowH / 2f;
        session.Window.MouseDown(40f, mid(a0));
        session.Window.MoveMouse(40f, mid(a1));   // extend within group A
        session.Window.MoveMouse(40f, mid(b0));   // then drag into the other group
        session.Window.MouseUp(40f, mid(b0));

        Assert.True(a0.HasSelection);
        Assert.True(a1.HasSelection);
        Assert.False(b0.HasSelection);            // different group is never roped in
    }

    // The case the model-indexed selection exists for. A virtualized list destroys the label
    // the press landed in as soon as it scrolls out of view. With the anchor held as an
    // element that ended the drag — the window could no longer find it, so extending stopped
    // and the copy was truncated to whatever was still on screen. Held as a line number, the
    // anchor cannot be invalidated by recycling.
    [Fact]
    public void ADragSurvivesTheAnchorRowBeingDestroyed()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var (scroll, rows, group) = Build(session);

        session.Window.MouseDown(40f, ScreenMidY(scroll, rows[0]));
        session.Window.MoveMouse(40f, ScreenMidY(scroll, rows[1]));

        // Row 1 is torn out of the tree mid-drag, exactly as recycling does. It ends up in
        // the middle of the finished span, so it should copy whole despite having no element.
        var column = scroll.Children[0];
        column.RemoveChild(rows[1]);
        session.PumpFrame();

        // The drag keeps extending over the rows that remain. (Row 4 is the last one inside
        // the 100-tall viewport; below that there is nothing to hit-test against.)
        session.Window.MoveMouse(40f, ScreenMidY(scroll, rows[4]));
        session.Window.MouseUp(40f, ScreenMidY(scroll, rows[4]));

        Assert.True(group.HasSelection);
        Assert.True(rows[4].HasSelection);
        // The destroyed line is in the copy, because the copy comes from the model.
        Assert.Contains("line number 1", group.SelectedText);
        Assert.Contains("line number 3", group.SelectedText);
    }

    // Copy is keyed off the group holding the selection, not off the focused element, so it
    // works even when the label that had focus has since been recycled away.
    [Fact]
    public void CopyingWorksAfterTheFocusedLabelIsGone()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var (scroll, rows, group) = Build(session);

        session.Window.MouseDown(40f, ScreenMidY(scroll, rows[0]));
        session.Window.MoveMouse(40f, ScreenMidY(scroll, rows[4]));
        session.Window.MouseUp(40f, ScreenMidY(scroll, rows[4]));
        Assert.True(group.HasSelection, "drag should have produced a selection");

        var column = scroll.Children[0];
        column.RemoveChild(rows[0]);      // the pressed (and focused) label goes away
        session.PumpFrame();

        session.Window.KeyDown(KeyCode.C, KeyModifiers.Ctrl);

        var copied = session.Window.Clipboard.GetText() ?? "";
        Assert.Contains("line number 1", copied);
        Assert.Contains("line number 3", copied);
    }

    // ── Word/line select and shift-extend inside a group ─────────────────
    // A grouped label's own double-click and shift handling has to be routed through the
    // group: the label's private anchor/caret are ignored once it derives its highlight
    // from the group, so writing to them selects nothing visible.

    [Fact]
    public void DoubleClick_SelectsTheWordUnderThePointer()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var (scroll, rows, group) = Build(session);

        // "line number 3" — land inside "number".
        float y = ScreenMidY(scroll, rows[3]);
        float x = rows[3].Layout.AbsoluteContentBox.X + CharX(rows[3], "line num");
        session.Window.DoubleClick(x, y);

        Assert.True(group.HasSelection);
        Assert.Equal("number", group.SelectedText);
    }

    [Fact]
    public void TripleClick_SelectsTheWholeLine()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var (scroll, rows, group) = Build(session);

        float y = ScreenMidY(scroll, rows[2]);
        float x = rows[2].Layout.AbsoluteContentBox.X + CharX(rows[2], "line num");
        session.Window.Click(x, y);
        session.Window.Click(x, y);
        session.Window.Click(x, y);

        Assert.Equal("line number 2", group.SelectedText);
    }

    // The second press selects the word and keeps the button down, so the drag after it goes
    // on taking whole words — across rows, since the group spans them.
    [Fact]
    public void DoubleClickDrag_ExtendsByWholeWordsAcrossRows()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var (scroll, rows, group) = Build(session);

        // Rows 0..4 are the ones inside the 100-tall viewport.
        float y1 = ScreenMidY(scroll, rows[1]);
        float x1 = rows[1].Layout.AbsoluteContentBox.X + CharX(rows[1], "line num");
        session.Window.Click(x1, y1);
        session.Window.MouseDown(x1, y1);              // second press: "number" is selected now
        Assert.Equal("number", group.SelectedText);

        // Still down: drag into the middle of "line" two rows below.
        float y3 = ScreenMidY(scroll, rows[3]);
        float x3 = rows[3].Layout.AbsoluteContentBox.X + CharX(rows[3], "li");
        session.Window.MoveMouse(x3, y3);
        session.Window.MouseUp(x3, y3);

        var selected = group.SelectedText;
        Assert.StartsWith("number 1", selected);       // the anchor word stayed whole
        Assert.EndsWith("line", selected);             // and the far end took a whole word too
        Assert.Contains("line number 2", selected);
    }

    [Fact]
    public void ShiftClick_ExtendsFromTheExistingAnchor()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var (scroll, rows, group) = Build(session);

        float x = rows[0].Layout.AbsoluteContentBox.X + 1f;
        session.Window.Click(x, ScreenMidY(scroll, rows[1]));           // anchor on row 1
        session.Window.MouseDown(x, ScreenMidY(scroll, rows[4]), MouseButton.Left, KeyModifiers.Shift);
        session.Window.MouseUp(x, ScreenMidY(scroll, rows[4]), MouseButton.Left, KeyModifiers.Shift);

        Assert.True(group.HasSelection);
        Assert.Contains("line number 2", group.SelectedText);
        Assert.Contains("line number 3", group.SelectedText);
        Assert.False(rows[0].HasSelection);
        Assert.False(rows[5].HasSelection);
    }

    [Fact]
    public void ShiftDown_ExtendsTheSelectionByALine()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var (scroll, rows, group) = Build(session);

        float x = rows[0].Layout.AbsoluteContentBox.X + 1f;
        session.Window.Click(x, ScreenMidY(scroll, rows[1]));

        session.Window.KeyDown(KeyCode.Down, KeyModifiers.Shift);
        session.Window.KeyDown(KeyCode.Down, KeyModifiers.Shift);

        Assert.True(group.HasSelection);
        Assert.Contains("number 1", group.SelectedText);
        Assert.Contains("line number 2", group.SelectedText);
        Assert.True(rows[3].HasSelection);
        Assert.False(rows[4].HasSelection);
    }

    [Fact]
    public void ShiftEnd_ExtendsToTheEndOfTheLine()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var (scroll, rows, group) = Build(session);

        float x = rows[0].Layout.AbsoluteContentBox.X + 1f;
        session.Window.Click(x, ScreenMidY(scroll, rows[2]));
        session.Window.KeyDown(KeyCode.End, KeyModifiers.Shift);

        Assert.EndsWith("number 2", group.SelectedText);
    }

    // Width of a prefix in the label's own font, so a test can aim at a character offset.
    private static float CharX(TextElement row, string prefix) =>
        HultUI.Core.Rendering.TextMeasure.MeasureWidth(prefix, row.ResolvedInherited);
}
