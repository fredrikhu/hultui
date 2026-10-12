using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A virtual list makes its rows while it paints, after the style pass, and resolves them there
/// itself. Rows from the one-argument template were resolved under the theme's defaults rather
/// than under the list: a row inherited nothing an ancestor of the list set, and the next style
/// pass, which resolves them under the list like any other children, changed their font size
/// (the theme's 16 px to the 12–13 px the apps set above their lists) and laid out every box and
/// icon of every row again. They now inherit from the list, as rows on item rows already did.
/// </summary>
public class VirtualListRowStyleTests
{
    private const float RowHeight = 24f;
    private static readonly Color Inherited = new(200, 30, 30);

    private sealed record Entry(int Id, string Name);

    /// <summary>A leaf whose measurement says when layout asked for it again.</summary>
    private sealed class CountingLeaf : Element
    {
        public int Calls;

        internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
        {
            Calls++;
            return new Size(10, 10);
        }
    }

    /// <summary>A list whose owner rebuilds it, with rows from the one-argument template that set
    /// no text style of their own.</summary>
    private sealed class Pane : Component
    {
        public int Selected;
        public int Made;
        public readonly Ref<VirtualListElement<Entry>> List = new();
        private readonly List<Entry> _items = Enumerable.Range(0, 200).Select(i => new Entry(i, $"entry {i}")).ToList();

        public override Element Build()
        {
            int selected = Selected;
            return Flex.Column().FlexGrow(1).Children(
                VirtualListElement<Entry>.New()
                    .ItemHeight(RowHeight)
                    .Items(_items)
                    .RenderWith(e =>
                    {
                        Made++;
                        return Flex.Row(gap: 4)
                            .Background(e.Id == selected ? new Color(40, 90, 200) : Color.Transparent)
                            .Children(new CountingLeaf(), TextB.New(e.Name));
                    })
                    .FlexGrow(1)
                    .WithRef(List));
        }

        public void Select(int id) { Selected = id; MarkDirty(); }
    }

    private static (HeadlessSession Session, Pane Pane) Mount()
    {
        var session = HeadlessSession.Create(400, 300);
        try
        {
            var pane = new Pane();
            // What the apps do: a text size and colour set above the list for everything in it.
            session.Application.SetRoot(Flex.Column().FlexGrow(1).FontSize(13f).TextColor(Inherited)
                .Children(pane.Mount()));
            session.PumpFrames(3);
            return (session, pane);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private static List<Element> Rows(Pane pane)
    {
        var list = pane.List.Current!;
        return Enumerable.Range(1, list.Children.Count - 1).Select(i => list.Children[i]).ToList();
    }

    [Fact]
    public void RowsOfAOneArgumentList_InheritWhatIsSetAboveTheList()
    {
        var (session, pane) = Mount();
        using var _ = session;
        Assert.True(Rows(pane).Count > 5);

        // The rows a rebuild makes, as they are drawn: made in the paint, after the style pass.
        pane.Select(3);
        session.PumpFrames(2);

        Assert.All(Rows(pane), row =>
        {
            Assert.Equal(13f, row.ResolvedInherited.FontSize);
            var label = (TextElement)row.Children[1];
            Assert.Equal(13f, label.ResolvedInherited.FontSize);
            Assert.Equal(Inherited, label.ResolvedInherited.TextColor);
        });
    }

    /// <summary>The cost the old resolution had: the style pass of every rebuild found each row on
    /// screen resolved at another size, marked all of it for layout, and the layout pass measured
    /// those rows again just before the paint replaced them. Now the rows a rebuild replaces are
    /// left as they were laid out.</summary>
    [Fact]
    public void ARebuild_DoesNotMeasureTheRowsItReplaces()
    {
        var (session, pane) = Mount();
        using var _ = session;
        pane.Select(1);
        session.PumpFrames(2);
        var replaced = Rows(pane).Select(r => (CountingLeaf)r.Children[0]).ToList();
        Assert.True(replaced.Count > 5);
        foreach (var leaf in replaced) leaf.Calls = 0;

        pane.Made = 0;
        pane.Select(2);
        session.PumpFrames(2);

        Assert.Equal(replaced.Count, pane.Made);
        Assert.DoesNotContain(Rows(pane), r => replaced.Contains(r.Children[0]));
        Assert.All(replaced, leaf => Assert.Equal(0, leaf.Calls));
    }
}
