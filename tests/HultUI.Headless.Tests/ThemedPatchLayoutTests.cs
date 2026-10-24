using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A themed control's padding, border and size are written by the theme on every style pass, and
/// the twin a rebuild hands over has never been themed: its patch copied the builder's padding
/// over the theme's, saw a change, and laid the control out again on every rebuild — with every
/// box around it up to the window, and a diff list next to a toolbar at five or more widths a pass
/// (Junction's Commit view: 20.7 layouts and 1,281 measurements a key). The patch now compares
/// against a twin themed the way the style pass will theme the live control
/// (Element.PatchThemesTwin), and the control marks its own layout when the theme changes a size
/// it writes. These pin both halves: a rebuild that changes nothing lays nothing out, and
/// everything that does change a size — a style, a variant, another theme, a longer value, a row
/// height — is laid out again, through a rebuild or without one.
/// </summary>
public class ThemedPatchLayoutTests
{
    private sealed record Entry(string Name, long Size);

    private static readonly ButtonStyle Roomy = new() { Padding = new Thickness(14, 30, 14, 30) };

    /// <summary>A screen of every control whose patch this concerns, rebuilt on demand.</summary>
    private sealed class Screen : Component
    {
        public ButtonStyle? Style;
        public ButtonVariant Variant = ButtonVariant.Primary;
        public string FieldText = "query";
        public float[] Heights = [20, 30, 20, 40, 20];
        public readonly Ref<ButtonElement> Button = new();
        public readonly Ref<TextInputElement> Field = new();
        public readonly Ref<KbdElement> Kbd = new();
        public readonly Ref<VirtualListElement<int>> List = new();
        public readonly TableView<Entry> Table;
        public readonly Entry[] Entries = [new("a.txt", 100), new("b.txt", 300), new("c.txt", 200)];
        private static readonly int[] Rows = Enumerable.Range(0, 60).ToArray();

        public Screen()
        {
            Table = new TableView<Entry>(
            [
                new TableColumn<Entry> { Id = "name", Header = "Name", Fill = 1, Text = e => e.Name,
                    Compare = (a, b) => string.CompareOrdinal(a.Name, b.Name) },
                new TableColumn<Entry> { Id = "size", Header = "Size", Width = 90, Align = ColumnAlign.End,
                    Text = e => $"{e.Size} B", Compare = (a, b) => a.Size.CompareTo(b.Size) },
            ], MarkDirty);
        }

        public override Element Build()
        {
            var bar = new MenuBarElement();
            foreach (var name in new[] { "File", "Edit" })
            {
                var title = new MenuTitleElement();
                title.AddChild(TextB.New(name));
                bar.AddChild(title);
            }
            var heights = Heights;
            var column = Flex.Column(gap: 4).Children(
                bar,
                Flex.Row(gap: 4).AlignItems(AlignItems.Center).Children(
                    Core.Builders.Button.New("Save", Variant).Style(Style).WithRef(Button),
                    Core.Builders.Button.New(ButtonVariant.Ghost).Children(TextB.New("Ghost")),
                    Core.Builders.Kbd.New("Ctrl+S").WithRef(Kbd),
                    TextInput.New(FieldText).WithRef(Field),
                    new SplitterHandleElement(SplitterAxis.Horizontal).Height(Dimension.Px(20))),
                Table.Header(),
                // Rows in the tree, patched by the rebuild (the table's own body is a list of
                // one-argument rows, made again on every rebuild by contract).
                Table.Row(Entries[0]).Height(Dimension.Px(24)),
                Table.Row(Entries[1]).Height(Dimension.Px(24)));
            // Left out where another theme object would make its rows again and lay the column
            // out: a theme test has to see the one control's own mark.
            if (WithList)
                column.AddChild(VirtualListElement<int>.New()
                    .Items(Rows)
                    .RowHeights((_, i) => heights[i % heights.Length])
                    .RenderWith((i, _) => TextB.New($"row {i}"))
                    .Height(Dimension.Px(160))
                    .WithRef(List));
            return column;
        }

        public bool WithList = true;

        public void Rebuild() => MarkDirty();
    }

    private static (HeadlessSession Session, Screen Screen, Element Root) Mount(bool withList = true)
    {
        var session = HeadlessSession.Create(900, 600);
        var screen = new Screen { WithList = withList };
        Element root = screen.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(3);
        return (session, screen, root);
    }

    private static List<string> LayoutDirty(Element e, List<string>? into = null)
    {
        into ??= [];
        if ((e.DirtyFlags & DirtyFlags.Layout) != 0) into.Add(e.GetType().Name);
        foreach (var c in e.Children) LayoutDirty(c, into);
        return into;
    }

    [Fact]
    public void ARebuildThatChangesNothing_LaysNothingOut()
    {
        var (session, screen, root) = Mount();
        using var _ = session;

        screen.Rebuild();

        // Not one box marked: no layout pass at all.
        Assert.Empty(LayoutDirty(root));
        session.PumpFrames(2);
    }

    [Fact]
    public void ABuildWithAStyleOfMorePadding_LaysTheButtonOutAgain()
    {
        var (session, screen, _) = Mount();
        using var __ = session;
        float before = screen.Button.Current!.Layout.Width;

        screen.Style = Roomy;
        screen.Rebuild();
        session.PumpFrames(2);

        Assert.Equal(before + 28f, screen.Button.Current!.Layout.Width, 2);
    }

    [Fact]
    public void AStyleSetOnTheLiveButton_LaysItOutAgain()
    {
        // No rebuild: only the style pass sees it, and the button marks its own layout.
        var (session, screen, _) = Mount();
        using var __ = session;
        var button = screen.Button.Current!;
        float before = button.Layout.Width;

        button.Style = Roomy;
        session.PumpFrames(2);

        Assert.Equal(before + 28f, button.Layout.Width, 2);
    }

    /// <summary>One size at a time, so no other control's mark lays the one under test out.</summary>
    [Theory]
    [InlineData("button", false)]
    [InlineData("button", true)]
    [InlineData("kbd", false)]
    [InlineData("kbd", true)]
    [InlineData("field", false)]
    [InlineData("field", true)]
    [InlineData("header", false)]
    [InlineData("header", true)]
    [InlineData("cell", false)]
    [InlineData("cell", true)]
    public void AnotherThemesSize_LaysTheControlOutAgain_WithOrWithoutARebuild(string control, bool rebuildFirst)
    {
        var (session, screen, root) = Mount(withList: false);
        using var __ = session;
        // A body row's cell: the row's height is set, so only the cell's own mark lays it out.
        var cell = LayoutProbe.Descendants<TableView<Entry>.CellElement>(root)
            .First(c => c is not TableView<Entry>.HeaderCellElement);
        var header = LayoutProbe.Descendants<TableView<Entry>.HeaderElement>(root).Single();
        (Theme Theme, Func<float> Measure, float Expected) c = control switch
        {
            "button" => (Theme.Light.With(new ButtonTheme { Padding = new Thickness(8, 26, 8, 26) }),
                () => screen.Button.Current!.Layout.Width, screen.Button.Current!.Layout.Width + 20f),
            "kbd" => (Theme.Light.With(new KbdTheme { Padding = new Thickness(2, 11, 2, 11) }),
                () => screen.Kbd.Current!.Layout.Width, screen.Kbd.Current!.Layout.Width + 12f),
            "field" => (Theme.Light.With(new TextInputTheme { Padding = new Thickness(10, 8, 10, 8) }),
                () => screen.Field.Current!.Layout.Height, screen.Field.Current!.Layout.Height + 12f),
            "header" => (Theme.Light.With(new TableTheme { HeaderHeight = 40f }),
                () => header.Layout.Height, 40f),
            _ => (Theme.Light.With(new TableTheme { CellPadding = new Thickness(0, 20) }),
                () => cell.Children[0].Layout.AbsoluteBorderBox.X - cell.Layout.AbsoluteBorderBox.X, 20f),
        };

        // A sample flipping its own light/dark flag rebuilds before it sets the theme.
        if (rebuildFirst) screen.Rebuild();
        session.Application.Theme = c.Theme;
        session.PumpFrames(2);

        Assert.Equal(c.Expected, c.Measure(), 2);
    }

    [Fact]
    public void AnotherVariant_LaysTheButtonOutAgain_WhenItsPaddingDiffers()
    {
        var (session, screen, _) = Mount();
        using var __ = session;
        session.Application.Theme = Theme.Light.With(new ButtonTheme
        {
            Secondary = new ButtonStyle { Padding = new Thickness(8, 40, 8, 40) },
        });
        session.PumpFrames(2);
        float before = screen.Button.Current!.Layout.Width;

        screen.Variant = ButtonVariant.Secondary;
        screen.Rebuild();
        session.PumpFrames(2);

        Assert.Equal(before + 48f, screen.Button.Current!.Layout.Width, 2);
    }

    [Fact]
    public void AFieldGivenALongerValueByARebuild_GrowsWithIt()
    {
        var (session, screen, _) = Mount();
        using var __ = session;
        screen.FieldText = new string('m', 20);
        screen.Rebuild();
        session.PumpFrames(2);
        float wide = screen.Field.Current!.Layout.Width;

        screen.FieldText = new string('m', 30);
        screen.Rebuild();
        session.PumpFrames(2);

        Assert.True(screen.Field.Current!.Layout.Width > wide + 50f,
            $"the field kept its width: {wide} → {screen.Field.Current!.Layout.Width}");
    }

    [Fact]
    public void RowHeightsThatMove_AreLaidOutAgain_AndOnesThatDoNot_AreLeftStanding()
    {
        var (session, screen, root) = Mount();
        using var _ = session;
        var list = screen.List.Current!;
        var second = list.Children[2];
        Assert.Equal(30f, second.Layout.Height);

        // The same heights from a new function: nothing to lay out.
        screen.Heights = [20, 30, 20, 40, 20];
        screen.Rebuild();
        Assert.Empty(LayoutDirty(root));
        session.PumpFrames(2);
        Assert.Same(second, list.Children[2]);

        // A height that moved a little, so the same rows stay on screen: the kept row takes it,
        // and the rows below it move down.
        float thirdY = list.Children[3].Layout.AbsoluteBorderBox.Y;
        var onScreen = list.Children.ToArray();
        screen.Heights = [20, 31, 20, 40, 20];
        screen.Rebuild();
        session.PumpFrames(2);
        Assert.Equal(onScreen, list.Children.ToArray());
        Assert.Equal(31f, second.Layout.Height);
        Assert.Equal(thirdY + 1f, list.Children[3].Layout.AbsoluteBorderBox.Y, 2);
    }
}
