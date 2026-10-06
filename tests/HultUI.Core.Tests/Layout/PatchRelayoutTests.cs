using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Layout;

/// <summary>
/// A rebuilt component patches the elements it keeps (Element.PatchFrom), and a patch that
/// changes nothing about an element's layout leaves its layout standing — so one that does
/// change it must be laid out again. Random trees are laid out, rebuilt with random changes to
/// a fifth of their elements (layout styles, text, lines, overflow), patched through the
/// differ, laid out again, and compared box for box with the changed tree built fresh; four
/// rounds a tree, so a change can land on a box that kept its layout the round before.
/// </summary>
public class PatchRelayoutTests
{
    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 500).Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void APatchedTreeLaysOutAsTheChangedTreeBuiltFresh(int seed)
    {
        var host = Box.New();
        var live = LayoutPathIndependenceTests.Build(seed, controls: false);
        host.AddChild(live);
        var engine = new LayoutEngine(new StubMeasureContext());
        engine.PerformLayout(host, 800, 600);

        for (int round = 1; round <= 4; round++)
        {
            var incoming = Changed(seed, round);
            TreeDiffer.Reconcile(host, live, incoming);
            live = host.Children[0];
            engine.PerformLayout(host, 800, 600);

            var freshHost = Box.New();
            freshHost.AddChild(Changed(seed, round));
            new LayoutEngine(new StubMeasureContext()).PerformLayout(freshHost, 800, 600);

            var a = LayoutPathIndependenceTests.Flatten(host).ToList();
            var b = LayoutPathIndependenceTests.Flatten(freshHost).ToList();
            Assert.Equal(b.Count, a.Count);
            for (int i = 0; i < a.Count; i++)
                Assert.True(LayoutPathIndependenceTests.Same(a[i].Layout, b[i].Layout),
                    $"seed {seed}, round {round}, element {i} ({a[i].GetType().Name} {a[i].Name}): patched " +
                    $"{LayoutPathIndependenceTests.Describe(a[i].Layout)}, fresh {LayoutPathIndependenceTests.Describe(b[i].Layout)}");
        }
    }

    /// <summary>The seed's tree with the changes of every round up to <paramref name="round"/>:
    /// the same tree each time, so the differ patches rather than replaces.</summary>
    private static Element Changed(int seed, int round)
    {
        var tree = LayoutPathIndependenceTests.Build(seed, controls: false);
        for (int r = 1; r <= round; r++)
        {
            var rng = new Random(seed * 31 + r);
            foreach (var e in LayoutPathIndependenceTests.Flatten(tree).ToList())
                if (rng.Next(5) == 0) Change(e, rng);
        }
        return tree;
    }

    private static void Change(Element e, Random rng)
    {
        switch (rng.Next(e is TextElement ? 12 : 10))
        {
            case 0: e.Width(rng.Next(3) switch { 0 => Dimension.Auto, 1 => Dimension.Px(20 + rng.Next(300)), _ => Dimension.Percent(10 + rng.Next(90)) }); break;
            case 1: e.Height(rng.Next(3) switch { 0 => Dimension.Auto, 1 => Dimension.Px(10 + rng.Next(200)), _ => Dimension.Percent(10 + rng.Next(90)) }); break;
            case 2: e.Padding(rng.Next(12)); break;
            case 3: e.Margin(rng.Next(8)); break;
            case 4: e.FlexGrow(rng.Next(3)); break;
            case 5: e.FlexShrink(rng.Next(3)); break;
            case 6: e.MinWidth(Dimension.Px(rng.Next(200))); break;
            case 7: e.MaxHeight(Dimension.Px(30 + rng.Next(300))); break;
            case 8:
                if (e is BoxElement box)
                {
                    box.Flex.Direction = rng.Next(2) == 0 ? FlexDirection.Row : FlexDirection.Column;
                    box.AlignItems((AlignItems)rng.Next(5));
                }
                else e.AspectRatio(0.5f + (float)rng.NextDouble() * 2);
                break;
            case 9:
                if (e is BoxElement b2) b2.JustifyContent((JustifyContent)rng.Next(6)).Gap(rng.Next(3) * 4);
                else e.AlignSelf((AlignSelf)rng.Next(6));
                break;
            case 10: ((TextElement)e).Content = new string('w', 1 + rng.Next(40)).Replace("www", "w w"); break;
            default:
                var text = (TextElement)e;
                if (rng.Next(2) == 0) text.MaxLines(1 + rng.Next(2));
                else text.TextOverflow(rng.Next(2) == 0 ? TextOverflow.Ellipsis : TextOverflow.Clip);
                break;
        }
    }
}
