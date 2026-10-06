using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Layout;

/// <summary>
/// Layout is a function of the tree and the window, not of the windows before it. The engine
/// remembers measurements between passes and sizes (MeasureCache — including for a range of
/// heights), so a remembered answer used where it no longer holds would show here: random trees
/// are laid out through a run of window sizes that grow and shrink, and after each one must match
/// the same tree built fresh and laid out at that size alone, box for box.
/// </summary>
public class LayoutPathIndependenceTests
{
    private static readonly (float W, float H)[] Sizes =
    [
        (800, 600), (420, 600), (800, 140), (260, 90), (800, 600), (1400, 2200), (500, 300), (500, 301), (800, 600),
    ];

    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 1000).Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void ARandomTreeLaysOutAsIfLaidOutFresh(int seed)
    {
        var tree = Build(seed);
        var engine = new LayoutEngine(new StubMeasureContext());

        foreach (var (w, h) in Sizes)
        {
            engine.PerformLayout(tree, w, h);

            var fresh = Build(seed);
            new LayoutEngine(new StubMeasureContext()).PerformLayout(fresh, w, h);

            var a = Flatten(tree).ToList();
            var b = Flatten(fresh).ToList();
            Assert.Equal(b.Count, a.Count);
            for (int i = 0; i < a.Count; i++)
                Assert.True(Same(a[i].Layout, b[i].Layout),
                    $"seed {seed}, window {w}×{h}, element {i} ({a[i].GetType().Name} {a[i].Name}): " +
                    $"laid out {Describe(a[i].Layout)} after earlier sizes, {Describe(b[i].Layout)} fresh");
        }
    }

    /// <summary>
    /// Sizes and places within the parent exactly. The window-space position only to a hundredth
    /// of a pixel: after a cache hit it is moved by the difference instead of summed afresh from
    /// the root, and float addition rounds differently that way round (19.660004 against 19.66).
    /// </summary>
    private static bool Same(LayoutResult a, LayoutResult b) =>
        a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height
        && Near(a.AbsoluteX, b.AbsoluteX) && Near(a.AbsoluteY, b.AbsoluteY);

    // Equal first: a box pushed past an unbounded edge sits at infinity, and ∞ − ∞ is NaN.
    private static bool Near(float a, float b) => a == b || Math.Abs(a - b) < 0.01f;

    private static string Describe(LayoutResult l) =>
        $"(x {l.X:R}, y {l.Y:R}, {l.Width:R}×{l.Height:R}, at {l.AbsoluteX:R},{l.AbsoluteY:R})";

    /// <summary>A random tree, the same for the same seed.</summary>
    private static Element Build(int seed)
    {
        var rng = new Random(seed);
        int id = 0;
        var root = Node(rng, depth: 0, ref id);
        return root;
    }

    private static Element Node(Random rng, int depth, ref int id)
    {
        int kind = depth >= 4 ? rng.Next(2) : rng.Next(10);
        Element e = kind switch
        {
            0 => Text.New(new string('w', 1 + rng.Next(40)).Replace("www", "w w")),
            1 => Box.New(),
            7 when depth < 3 => ScrollView.New(),
            8 when depth < 3 => Grid.Of(rng.Next(2) == 0 ? "auto 1fr" : "60px 1fr 2fr", gap: rng.Next(3) * 4),
            _ => rng.Next(2) == 0 ? Flex.Row(gap: rng.Next(3) * 4) : Flex.Column(gap: rng.Next(3) * 4),
        };
        e.Name = $"n{id++}";

        if (e is TextElement text)
        {
            if (rng.Next(4) == 0) text.MaxLines(1 + rng.Next(2));
            if (rng.Next(6) == 0) text.TextOverflow(TextOverflow.Ellipsis);
        }

        // Sizes: mostly auto; sometimes pixels, percentages, min and max.
        if (rng.Next(5) == 0) e.Width(rng.Next(2) == 0 ? Dimension.Px(20 + rng.Next(300)) : Dimension.Percent(10 + rng.Next(90)));
        if (rng.Next(5) == 0) e.Height(rng.Next(2) == 0 ? Dimension.Px(10 + rng.Next(200)) : Dimension.Percent(10 + rng.Next(90)));
        if (rng.Next(8) == 0) e.MinWidth(rng.Next(2) == 0 ? Dimension.Px(rng.Next(200)) : Dimension.Percent(rng.Next(60)));
        if (rng.Next(8) == 0) e.MinHeight(rng.Next(2) == 0 ? Dimension.Px(rng.Next(150)) : Dimension.Percent(rng.Next(60)));
        if (rng.Next(8) == 0) e.MaxWidth(rng.Next(2) == 0 ? Dimension.Px(40 + rng.Next(400)) : Dimension.Percent(30 + rng.Next(70)));
        if (rng.Next(8) == 0) e.MaxHeight(rng.Next(2) == 0 ? Dimension.Px(30 + rng.Next(300)) : Dimension.Percent(30 + rng.Next(70)));
        if (rng.Next(12) == 0) e.AspectRatio(0.5f + (float)rng.NextDouble() * 2);
        if (rng.Next(3) == 0) e.Padding(rng.Next(12));
        if (rng.Next(6) == 0) e.Margin(rng.Next(8));

        // Flex item behaviour.
        if (rng.Next(3) == 0) e.FlexGrow(rng.Next(3));
        if (rng.Next(4) == 0) e.FlexShrink(rng.Next(3));
        if (rng.Next(8) == 0) e.FlexBasis(rng.Next(2) == 0 ? Dimension.Px(rng.Next(200)) : Dimension.Percent(rng.Next(80)));
        if (rng.Next(6) == 0) e.AlignSelf((AlignSelf)rng.Next(6));

        if (e is BoxElement box && kind >= 2)
        {
            if (rng.Next(3) == 0) box.AlignItems((AlignItems)rng.Next(5));
            if (rng.Next(3) == 0) box.JustifyContent((JustifyContent)rng.Next(6));
            if (rng.Next(5) == 0) box.FlexWrap(FlexWrap.Wrap);
            if (rng.Next(5) == 0) box.AlignContent((AlignContent)rng.Next(6));

            int count = 1 + rng.Next(4);
            var children = new Element[count];
            for (int i = 0; i < count; i++)
            {
                children[i] = Node(rng, depth + 1, ref id);
                if (box.GridRead.HasTemplate)
                    children[i].GridCell(i / 2, i % 2);
            }
            box.Children(children);
        }
        return e;
    }

    private static IEnumerable<Element> Flatten(Element e)
    {
        yield return e;
        foreach (var c in e.Children)
            foreach (var d in Flatten(c))
                yield return d;
    }
}
