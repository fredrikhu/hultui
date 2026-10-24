using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// What a context menu draws each frame. It draws its rows itself, and a long one (a dropdown of
/// a few hundred branches) runs past the window's edge: the rows the clips leave out are laid out
/// but not drawn, and a frame after the first measures no label again (plan 17, round 4).
/// </summary>
public class ContextMenuPaintTests
{
    /// <summary>Counts what the menu draws and measures; the window is the clip it reports.</summary>
    private sealed class CountingContext(Rect? window) : NullRenderContext, IRenderContext
    {
        public readonly List<string> Drawn = [];
        public int Measured;

        public Rect? ClipBounds => window;

        public override void DrawText(string text, Rect bounds, ResolvedInheritedStyle style,
            TextOverflow overflow, int? maxLines) => Drawn.Add(text);

        // Taller in the face named "Tall", so a changed text style changes the rows.
        public override Size MeasureText(string text, ResolvedInheritedStyle style)
        {
            Measured++;
            return new(text.Length * 7f, style.FontFamily == "Tall" ? 30f : 14f);
        }
    }

    private static ContextMenuElement Menu(int count)
    {
        var items = new ContextMenuItem[count];
        for (int i = 0; i < count; i++)
            items[i] = i % 10 == 9 ? ContextMenuItem.Separator() : new ContextMenuItem { Label = $"Row {i:000}" };
        var menu = new ContextMenuElement(items, new ContextMenuTheme(), closeMenu: () => { });
        StyleResolver.Resolve(menu, Theme.Light);
        new LayoutEngine(new StubMeasureContext()).PerformLayout(menu, 300, 20_000);
        return menu;
    }

    private static void Render(ContextMenuElement menu, IRenderContext ctx) => ElementRenderer.RenderTree(menu, ctx);

    [Fact]
    public void RowsOutsideTheClip_AreLaidOutButNotDrawn()
    {
        var menu = Menu(200);
        var ctx = new CountingContext(new Rect(0, 0, 300, 200));
        Render(menu, ctx);

        // Every row has its place, the last too, far below the clip.
        Assert.True(menu.ItemTop(199) > menu.ItemTop(198) && menu.ItemTop(198) > 2_000);
        // Only the rows that reach into the clip are drawn, the one it cuts included.
        int reaching = Enumerable.Range(0, 200).Count(i => menu.ItemAt(i) is { IsSeparator: false } && menu.ItemTop(i) < 200);
        Assert.InRange(reaching, 5, 15);
        Assert.Equal(reaching, ctx.Drawn.Count);
        Assert.Equal("Row 000", ctx.Drawn[0]);
        Assert.DoesNotContain("Row 198", ctx.Drawn);
    }

    [Fact]
    public void AContextThatCannotSayWhatIsVisible_GetsEveryRow()
    {
        var menu = Menu(200);
        var ctx = new CountingContext(null);
        Render(menu, ctx);

        Assert.Equal(180, ctx.Drawn.Count); // 200 rows, every tenth a separator
    }

    [Fact]
    public void AFrameAfterTheFirst_MeasuresNoLabel_UntilTheTextStyleChanges()
    {
        var menu = Menu(200);
        var first = new CountingContext(new Rect(0, 0, 300, 200));
        Render(menu, first);
        Assert.Equal(180, first.Measured);
        float top = menu.ItemTop(100);

        var next = new CountingContext(new Rect(0, 0, 300, 200));
        Render(menu, next);
        Assert.Equal(0, next.Measured);
        Assert.Equal(top, menu.ItemTop(100));

        // A taller face: every row is measured again and moves down.
        menu.FontFamily("Tall");
        menu.MarkStyleDirty();
        StyleResolver.Resolve(menu, Theme.Light);
        var restyled = new CountingContext(new Rect(0, 0, 300, 200));
        Render(menu, restyled);
        Assert.Equal(180, restyled.Measured);
        Assert.True(menu.ItemTop(100) > top + 1_000, $"row 100 at {menu.ItemTop(100)}, was {top}");
    }
}
