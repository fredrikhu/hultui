using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A virtualised list realizes its rows from a template, and until now every rebuild of the
/// tree around it realized them again from scratch — the same rows, as new objects. That is
/// invisible for a row of labels and fatal for a row holding a text field: the field the
/// reader was typing into was replaced, mid-word, by a fresh one with no focus and no caret.
/// A row that carries a <see cref="Element.Key"/> is now reconciled with its next realization
/// instead, the way the reconciler treats a keyed child anywhere else in the tree.
/// </summary>
public class VirtualListKeyedRowTests
{
    [Fact]
    public void AKeyedRowKeepsItsFieldAndItsFocusThroughARebuild()
    {
        using var session = HeadlessSession.Create(400, 300);
        var host = new Rows();
        var root = host.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(3);

        var field = First<TextAreaElement>(root)!;
        field.Focus();
        session.PumpFrame();
        session.Window.TypeText("abc");
        session.PumpFrame();
        Assert.Equal("abc", field.Text);
        Assert.Equal("abc", host.Note);
        var plainBefore = First<TextElement>(root, t => t.Content == "row 5")!;

        // The rebuild everything else in an app does on every keystroke.
        host.Rebuild();
        session.PumpFrames(3);

        var after = First<TextAreaElement>(root)!;
        Assert.Same(field, after);
        Assert.Same(field, session.Window.Window.FocusedElement);
        Assert.Equal(3, field.CursorPosition);
        session.Window.TypeText("d");
        session.PumpFrame();
        Assert.Equal("abcd", field.Text);

        // The baseline this is measured against: an unkeyed row is realized anew each time.
        Assert.NotSame(plainBefore, First<TextElement>(root, t => t.Content == "row 5"));
    }

    [Fact]
    public void AKeyedRowScrolledAwayIsNotHeldForever()
    {
        using var session = HeadlessSession.Create(400, 300);
        var host = new Rows();
        var root = host.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(3);
        var field = First<TextAreaElement>(root)!;

        // Scroll the keyed row (index 2) well out of the viewport and rebuild there.
        var list = First<VirtualListElement<int>>(root)!;
        list.ScrollOffsetY = 30f * 30;
        host.Rebuild();
        session.PumpFrames(3);
        Assert.Null(First<TextAreaElement>(root));

        // Back at the top it is a new row: what was held back was let go once a window of
        // rows was realized without it.
        list.ScrollOffsetY = 0f;
        session.PumpFrames(3);
        var again = First<TextAreaElement>(root);
        Assert.NotNull(again);
        Assert.NotSame(field, again);
    }

    private sealed class Rows : Component
    {
        public string Note = "";

        public override Element Build()
        {
            var list = VirtualListElement<int>.New()
                .ItemHeight(30f)
                .Items(Enumerable.Range(0, 60).ToArray())
                .RenderWith(Row)
                .FlexGrow(1);
            return Box.New().Width(400).Height(300).Children(list);
        }

        private Element Row(int i)
        {
            if (i == 2)
            {
                var area = TextArea.New(Note).OnTextChanged(t => Note = t).Height(Dimension.Px(26)).FlexGrow(1);
                var row = Flex.Row(gap: 4).Children(area);
                row.Key = "editor";
                return row;
            }
            return Flex.Row(gap: 4).Children(HultUI.Core.Builders.Text.New($"row {i}"));
        }

        public void Rebuild() => MarkDirty();
    }

    private static T? First<T>(Element root, Func<T, bool>? match = null) where T : Element
    {
        if (root is T hit && (match is null || match(hit))) return hit;
        foreach (var child in root.Children)
            if (First(child, match) is { } found) return found;
        return null;
    }
}
