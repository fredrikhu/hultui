using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Selecting in one long label inside a scroll view — a details pane, the text of a dialog.
/// Layout is unscrolled and the scroll is a translate at paint, so the pointer has to be
/// taken back through every scroll view above the label before it is a place in its text. A label
/// of one line hides a mistake there, because any height maps to its only line; that is why the
/// row-per-label viewers never showed it.
/// </summary>
public class ScrolledLabelSelectionTests
{
    private const int Lines = 40;

    private static (ScrollViewElement Scroll, TextElement Label) Build(HeadlessSession session)
    {
        var label = TextElement.New(string.Join("\n", Enumerable.Range(0, Lines).Select(i => $"line {i:00}")));
        label.Selectable = true;
        var scroll = ScrollView.Vertical().Width(200).Height(100).Children(label);
        session.Window.SetRoot(scroll);
        session.PumpFrame();
        return (scroll, label);
    }

    private static float LineHeight(TextElement label) => label.Layout.AbsoluteContentBox.Height / Lines;

    [Fact]
    public void ADragInAScrolledView_SelectsTheLineUnderThePointer()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var (scroll, label) = Build(session);
        float line = LineHeight(label);
        scroll.ScrollOffsetY = 20 * line;   // line 20 at the top of the view
        session.PumpFrame();

        // Across the middle of the second line on screen, which is line 21.
        float y = scroll.Layout.AbsoluteContentBox.Y + line * 1.5f;
        session.Window.MouseDown(0f, y);
        session.Window.MoveMouse(150f, y);
        session.Window.MouseUp(150f, y);

        Assert.Equal("line 21", label.SelectedText);
    }

    // ── Held past the edge, the view scrolls and the selection goes with it ──

    [Fact]
    public void ADragHeldBelowTheView_ScrollsIt_AndTheSelectionFollows()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var (scroll, label) = Build(session);
        float line = LineHeight(label);
        int onScreen = (int)(100 / line);
        float top = scroll.Layout.AbsoluteContentBox.Y;

        session.Window.MouseDown(0f, top + line / 2);
        session.Window.MoveMouse(150f, 150f);   // 50 below the 100-tall view, inside the window
        for (int i = 0; i < 30; i++) session.PumpFrame();   // held still: the frames scroll

        Assert.True(scroll.ScrollOffsetY > 0, "the view should have scrolled toward the pointer");
        int selected = label.SelectedText.Split('\n').Length;
        Assert.True(selected > onScreen + 1,
            $"the selection should have followed past the {onScreen} lines first on screen; it covers {selected}");
        session.Window.MouseUp(150f, 150f);
    }

    [Fact]
    public void ADragHeldInsideTheView_LeavesItWhereItIs()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var (scroll, label) = Build(session);
        float line = LineHeight(label);
        float top = scroll.Layout.AbsoluteContentBox.Y;

        session.Window.MouseDown(0f, top + line / 2);
        session.Window.MoveMouse(150f, top + line * 3.5f);
        for (int i = 0; i < 30; i++) session.PumpFrame();

        Assert.Equal(0f, scroll.ScrollOffsetY);
        Assert.Equal(4, label.SelectedText.Split('\n').Length);
        session.Window.MouseUp(150f, top + line * 3.5f);
    }
}
