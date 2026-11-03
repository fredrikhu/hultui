using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A row's margins are laid out inside its place in a <see cref="VirtualListElement{T}"/>: the place
/// is the height the list gives the item, and the row is that place less its margins, as a column
/// lays out a child with margins. The command palette gives its rows the list's padding this way
/// instead of a box around each. Before, the list dropped a row's margins: the row filled its place.
/// </summary>
public class VirtualListRowMarginTests
{
    private static readonly Thickness RowMargin = new(3, 20, 5, 6);   // top, right, bottom, left

    private static float Slot(int index, bool unequal) => unequal && index % 3 == 0 ? 44f : 30f;

    /// <summary>A pane over <paramref name="count"/> numbers whose rows have <see cref="RowMargin"/>,
    /// on item rows; <see cref="Taller"/> adds to every place's height on the next build.</summary>
    private sealed class Pane(int count, bool unequal) : Component
    {
        public readonly Ref<VirtualListElement<int>> List = new();
        public readonly List<int> Clicked = [];
        public float Taller;
        public int Made;
        private readonly List<int> _items = Enumerable.Range(0, count).ToList();

        public override Element Build()
        {
            float taller = Taller;
            var list = VirtualListElement<int>.New()
                .Items(_items)
                .RenderWith((i, _) => Row(i))
                .FlexGrow(1);
            if (unequal || taller > 0) list.RowHeights((i, _) => Slot(i, unequal) + taller);
            else list.ItemHeight(30f);
            return Flex.Column().FlexGrow(1).Children(list.WithRef(List));
        }

        private Element Row(int i)
        {
            Made++;
            var row = Flex.Row().Padding(new Thickness(2, 8)).Background(new Color(40, 90, 200))
                .Margin(RowMargin).AutomationId($"row-{i}")
                .Children(TextB.New($"row {i}").FontSize(12f));
            row.OnClick(_ => Clicked.Add(i));
            return row;
        }

        public void Rebuild() => MarkDirty();
    }

    private static (HeadlessSession Session, Pane Pane) Mount(bool unequal)
    {
        var session = HeadlessSession.Create(400, 300);
        var pane = new Pane(200, unequal);
        session.Application.SetRoot(pane.Mount());
        session.PumpFrames(3);
        return (session, pane);
    }

    private static List<(int Index, Element Row)> Rows(Pane pane) =>
        pane.List.Current!.Children.Where(c => c.AutomationId?.StartsWith("row-", StringComparison.Ordinal) == true)
            .Select(c => (int.Parse(c.AutomationId!["row-".Length..]), c)).ToList();

    /// <summary>Each realized row stands in its place less its margins.</summary>
    private static void AssertInPlaces(Pane pane, bool unequal, float taller = 0f)
    {
        var list = pane.List.Current!;
        var view = list.Layout.AbsoluteContentBox;
        var rows = Rows(pane);
        Assert.InRange(rows.Count, 8, 14);
        foreach (var (index, row) in rows)
        {
            float top = 0;
            for (int k = 0; k < index; k++) top += Slot(k, unequal) + taller;
            float slot = Slot(index, unequal) + taller;
            var box = row.Layout.AbsoluteBorderBox;
            Assert.Equal((view.X + 6, view.Y + top + 3, view.Width - 26, slot - 8),
                (box.X, box.Y, box.Width, box.Height));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARowWithMargins_IsItsPlaceLessThem_AndTheListAsLongAsItsPlaces(bool unequal)
    {
        var (session, pane) = Mount(unequal);
        using var _s = session;
        var list = pane.List.Current!;

        AssertInPlaces(pane, unequal);
        float total = 0;
        for (int i = 0; i < 200; i++) total += Slot(i, unequal);
        Assert.Equal(total, list.ContentHeight, 0.01f);

        // Scrolled to rows made since, and back.
        list.ScrollOffsetY = 1_000;
        session.PumpFrames(2);
        Assert.Contains(Rows(pane), r => r.Index > 30);
        AssertInPlaces(pane, unequal);
        list.ScrollOffsetY = 0;
        session.PumpFrames(2);
        AssertInPlaces(pane, unequal);
    }

    [Fact]
    public void KeptRows_TakeTheirNewPlacesLessTheirMargins()
    {
        var (session, pane) = Mount(unequal: true);
        using var _s = session;
        var before = Rows(pane).ToDictionary(r => r.Index, r => r.Row);
        int made = pane.Made;

        pane.Taller = 6f;
        pane.Rebuild();
        session.PumpFrames(2);

        // The same rows, kept, each in its taller place.
        Assert.Equal(made, pane.Made);
        Assert.All(Rows(pane).Where(r => r.Index < 6), r => Assert.Same(before[r.Index], r.Row));
        AssertInPlaces(pane, unequal: true, taller: 6f);
    }

    [Fact]
    public void APressInARowsMargin_ReachesNoRow()
    {
        var (session, pane) = Mount(unequal: false);
        using var _s = session;
        var view = pane.List.Current!.Layout.AbsoluteContentBox;
        float middle = view.Y + 2 * 30 + 3 + 11;   // row 2's box, halfway down

        // Apart, so none is taken for the second of a double click.
        void Press(float x, float y)
        {
            session.Window.Click(x, y);
            session.PumpFrame(TimeSpan.FromSeconds(1));
        }
        Press(view.X + 3, middle);                  // its left margin
        Press(view.Right - 15, middle);             // its right margin, short of the scroll bar
        Press(view.X + 100, view.Y + 2 * 30 + 1);   // its top margin
        Press(view.X + 100, view.Y + 3 * 30 - 2);   // its bottom margin
        Assert.Empty(pane.Clicked);

        Press(view.X + 100, middle);
        Assert.Equal([2], pane.Clicked);
    }
}
