using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Selecting a line in the diff by pressing anywhere over it.
/// <para>
/// The rows used to centre their code label, so the label was only as tall as its own text
/// and the few pixels above and below it belonged to the row. A press landing there hit the
/// row, not the label, and began no selection — a thin dead strip along every line, most
/// often met when aiming at the first or last line of a hunk.
/// </para>
/// </summary>
public class DiffSelectionTests
{
    // Measured in pixels from the row's edges rather than fractions of it: the strip a
    // centred label leaves is only a pixel or two deep, so 90% of the way down a 22px row is
    // still well inside the label and says nothing about it.
    //
    // 1.5px in, not 0.5px: half a pixel from the edge can be rounded into the neighbouring
    // row, which makes the test a coin toss under load rather than a statement about this
    // row. 1.5px is unambiguously inside the row and still inside the old dead strip, which
    // ran from the row's edge to roughly 2px in.
    [Theory]
    [InlineData(1.5f)]     // near the top edge, inside the strip a centred label left
    [InlineData(-1.5f)]    // near the bottom edge, likewise
    [InlineData(9f)]       // the middle, which always worked
    public void PressingAnywhereOverALineBeginsASelection(float inset)
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            // Wait for the rows rather than assuming a frame count. The diff arrives through
            // git and the list realizes its rows during layout, so under load a fixed number
            // of pumps can leave nothing on screen — which reads as "the press missed".
            session.PumpUntil(() => FirstCodeLabel(session) is { Layout.AbsoluteBorderBox.Height: > 0 },
                System.TimeSpan.FromSeconds(20));
            var label = FirstCodeLabel(session) ?? throw new Xunit.Sdk.XunitException("no code label on screen");
            // The row's painted box, not the label's: the dead strip is the part of the row
            // the label does not cover, so pressing inside the label proves nothing. The row
            // is found by its relationship to the list rather than by counting parents —
            // wrapping and panning change how many sit between it and the label.
            var list = Field<HultUI.Core.Reactivity.Ref<Element>>(app, "_diffBodyRef").Current!;
            var row = LayoutProbe.RowIn(label, list)
                ?? throw new Xunit.Sdk.XunitException("the label is not inside a row of the list");
            var box = LayoutProbe.Painted(row);
            Assert.True(box.Height > 0, "the row has no height");

            var at = inset >= 0 ? LayoutProbe.InsetFromTop(box, inset)
                                : LayoutProbe.InsetFromBottom(box, -inset);
            session.Window.MouseDown(at.X, at.Y);
            session.PumpFrame();

            // That *this* label began the drag, not merely that something did. "Any label"
            // is satisfied by a neighbouring row picking the press up, which is exactly what
            // happens at a row boundary — so it cannot tell a working row from a press that
            // slid off it.
            Assert.True(label.IsDragging,
                $"a press {at.Y - box.Y:0.0}px into a {box.Height:0}px row went somewhere other than its own line"
                + (AnyLabelDragging(session) ? " (a different line took it)" : " (no line took it)"));
        });
    }

    /// <summary>The first selectable code label the diff has realized.</summary>
    private static TextElement? FirstCodeLabel(HeadlessSession session)
    {
        TextElement? found = null;
        Walk(Root(session));
        return found;

        void Walk(Element? e)
        {
            if (found != null || e is null) return;
            if (e is TextElement { Selectable: true, SelectionIndex: >= 0 } te) { found = te; return; }
            foreach (var c in e.Children) Walk(c);
        }
    }

    /// <summary>The path in the diff header is selectable too. It is the one thing in the
    /// pane a reader regularly wants out of the app — into a terminal, a chat, a bug report —
    /// and "Copy path" in the row's context menu only ever gives the whole of it.</summary>
    [Fact]
    public void TheFileNameInTheDiffHeaderCanBeSelected()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            var header = Descendants(Root(session)).OfType<TextElement>()
                .FirstOrDefault(e => e.Key == "diff-path")
                ?? throw new Xunit.Sdk.XunitException("no diff header path label on screen");

            Assert.True(header.Selectable, "the header path is not selectable");
            Assert.NotEqual("", header.Content);
        });
    }

    private static IEnumerable<Element> Descendants(Element? e)
    {
        if (e is null) yield break;
        yield return e;
        foreach (var c in e.Children)
            foreach (var d in Descendants(c))
                yield return d;
    }

    private static T Field<T>(JunctionApp app, string name) =>
        (T)typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static bool AnyLabelDragging(HeadlessSession session)
    {
        bool dragging = false;
        Walk(Root(session));
        return dragging;

        void Walk(Element? e)
        {
            if (dragging || e is null) return;
            if (e is TextElement { IsDragging: true }) { dragging = true; return; }
            foreach (var c in e.Children) Walk(c);
        }
    }

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window);
}
