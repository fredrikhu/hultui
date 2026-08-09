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
    // In pixels from the row's edges, not fractions of it: the strip is only a pixel or two
    // deep, so 90% of the way down a 20px row is still comfortably inside the label and says
    // nothing. The first and last pixel of the row are the ones in question.
    [Theory]
    [InlineData(0.5f)]     // the very top pixel of the row
    [InlineData(-0.5f)]    // the very bottom pixel (measured back from the bottom edge)
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
            // The row's box, not the label's: the dead strip is the part of the row the
            // label does not cover, so pressing inside the label proves nothing. The label
            // sits in the pan cell, which sits in the row.
            var row = label.Parent?.Parent ?? throw new Xunit.Sdk.XunitException("the label has no row");
            var box = row.Layout.AbsoluteBorderBox;
            Assert.True(box.Height > 0, "the row has no height");

            float y = inset >= 0 ? box.Y + inset : box.Bottom + inset;
            session.Window.MouseDown(box.X + box.Width / 2, y);
            session.PumpFrame();

            Assert.True(AnyLabelDragging(session),
                $"a press {y - box.Y:0.0}px into a {box.Height:0}px row began no selection");
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
