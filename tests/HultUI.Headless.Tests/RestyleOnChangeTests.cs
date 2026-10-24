using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A patch marked its element's whole subtree for style, so every rebuild re-resolved the kept
/// rows of its lists, the parts of its controls and everything it patched — and ran each one's
/// ApplyTheme — whether or not anything they inherit had changed (Junction History: 314 of 314
/// resolves a key came out the same). A patch now marks only the element it patched; the style
/// pass resolves a child when the child is marked or when what it inherits changed; and an
/// element moved under another parent is resolved there. These pin both halves.
/// </summary>
public class RestyleOnChangeTests
{
    private sealed record Entry(int Id, string Name);

    /// <summary>A leaf that counts the style passes that reach it.</summary>
    private sealed class Themed : Element
    {
        public int Themings;
        protected internal override void ApplyTheme(Theme theme) => Themings++;
        internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx) => new(10, 10);
    }

    /// <summary>A list of item rows under a box whose font size the owner sets.</summary>
    private sealed class Pane : Component
    {
        public float FontSize = 13f;
        public int Tick;
        public readonly List<Themed> Leaves = [];
        public readonly Ref<VirtualListElement<Entry>> List = new();
        private readonly List<Entry> _items = Enumerable.Range(0, 100).Select(i => new Entry(i, $"entry {i}")).ToList();

        public override Element Build()
        {
            _ = Tick;
            return Flex.Column().FlexGrow(1).FontSize(FontSize).Children(
                TextB.New("heading"),
                VirtualListElement<Entry>.New()
                    .ItemHeight(30f)
                    .Items(_items)
                    .RenderWith((e, _) =>
                    {
                        var leaf = new Themed();
                        Leaves.Add(leaf);
                        return Flex.Row(gap: 4).Children(leaf, TextB.New(e.Name));
                    })
                    .FlexGrow(1)
                    .WithRef(List));
        }

        public void Rebuild() => MarkDirty();
    }

    private static (HeadlessSession Session, Pane Pane) Mount()
    {
        var session = HeadlessSession.Create(400, 300);
        var pane = new Pane();
        session.Application.SetRoot(pane.Mount());
        session.PumpFrames(3);
        return (session, pane);
    }

    private static TextElement RowText(Pane pane, int row) =>
        (TextElement)pane.List.Current!.Children[1 + row].Children[1];

    [Fact]
    public void ARebuildThatChangesNothingInherited_LeavesKeptRowsUnresolved()
    {
        var (session, pane) = Mount();
        using var _ = session;
        Assert.True(pane.Leaves.Count > 5);
        int before = pane.Leaves.Sum(l => l.Themings);

        pane.Tick++;
        pane.Rebuild();
        session.PumpFrames(2);

        // The rows were kept (none made), and no style pass reached them.
        Assert.Equal(before, pane.Leaves.Sum(l => l.Themings));
    }

    [Fact]
    public void ARebuildThatChangesWhatTheRowsInherit_ResolvesThemAgain()
    {
        var (session, pane) = Mount();
        using var _ = session;
        var text = RowText(pane, 0);
        Assert.Equal(13f, text.ResolvedInherited.FontSize);
        float width = text.Layout.Width;

        pane.FontSize = 20f;
        pane.Rebuild();
        session.PumpFrames(2);

        Assert.Same(text, RowText(pane, 0));   // kept, not made again
        Assert.Equal(20f, text.ResolvedInherited.FontSize);
        Assert.True(text.Layout.Width > width, $"the kept row's label kept its width {width}");
    }

    /// <summary>One element the owner holds and places under one of two boxes of different font
    /// sizes, the way an app places a persistent part: the build moves it, the reconcile lands it.</summary>
    private sealed class Mover : Component
    {
        public bool Second;
        public readonly TextElement Held = TextB.New("held");

        public override Element Build()
        {
            var first = Flex.Row().FontSize(10f).Children(Second ? Array.Empty<Element>() : new Element[] { Held });
            var second = Flex.Row().FontSize(20f).Children(Second ? new Element[] { Held } : Array.Empty<Element>());
            first.Key = "first";
            second.Key = "second";
            return Flex.Column().Children(first, second);
        }

        public void Move() { Second = !Second; MarkDirty(); }
    }

    [Fact]
    public void AnElementMovedUnderAnotherParent_InheritsFromIt()
    {
        using var session = HeadlessSession.Create(300, 200);
        var mover = new Mover();
        session.Application.SetRoot(mover.Mount());
        session.PumpFrames(2);
        Assert.Equal(10f, mover.Held.ResolvedInherited.FontSize);

        mover.Move();
        session.PumpFrames(2);
        Assert.Equal(20f, mover.Held.ResolvedInherited.FontSize);

        mover.Move();
        session.PumpFrames(2);
        Assert.Equal(10f, mover.Held.ResolvedInherited.FontSize);
    }

    /// <summary>The same, but the box it lands in is a new one each build (its key changes), so
    /// the reconcile adds the box whole instead of patching the old one.</summary>
    private sealed class NewHome : Component
    {
        public int Generation;
        public readonly TextElement Held = TextB.New("held");

        public override Element Build()
        {
            var home = Flex.Row().FontSize(Generation % 2 == 0 ? 10f : 20f).Children(Held);
            home.Key = $"home-{Generation}";
            return Flex.Column().Children(home);
        }

        public void Next() { Generation++; MarkDirty(); }
    }

    [Fact]
    public void AnElementMovedIntoANewContainer_InheritsFromIt()
    {
        using var session = HeadlessSession.Create(300, 200);
        var owner = new NewHome();
        session.Application.SetRoot(owner.Mount());
        session.PumpFrames(2);
        Assert.Equal(10f, owner.Held.ResolvedInherited.FontSize);

        owner.Next();
        session.PumpFrames(2);
        Assert.Equal(20f, owner.Held.ResolvedInherited.FontSize);
    }

    /// <summary>A parent whose build places a child component it keeps (a persistent part, as
    /// Junction's chrome and Aperture's player are placed) in a box made fresh each build.</summary>
    private sealed class Shell : Component
    {
        public int Tick;
        public readonly Inner Part = new();
        private ComponentHost? _host;

        public override Element Build()
        {
            _ = Tick;
            _host ??= Part.Mount();
            return Flex.Column().Children(TextB.New($"shell"), Flex.Row().Children(_host));
        }

        public void Rebuild() { Tick++; MarkDirty(); }
    }

    private sealed class Inner : Component
    {
        public readonly List<Themed> Leaves = [];

        public override Element Build()
        {
            var row = Flex.Row();
            for (int i = 0; i < 20; i++)
            {
                var leaf = new Themed();
                Leaves.Add(leaf);
                row.AddChild(leaf);
            }
            return row;
        }
    }

    [Fact]
    public void AParentsRebuild_LeavesAPersistentChildComponentUnresolved()
    {
        using var session = HeadlessSession.Create(400, 200);
        var shell = new Shell();
        session.Application.SetRoot(shell.Mount());
        session.PumpFrames(2);
        int before = shell.Part.Leaves.Sum(l => l.Themings);
        Assert.True(before >= 20);

        shell.Rebuild();
        session.PumpFrames(2);

        // The part was moved out of the old row and into the new one, and put back under a
        // parent like the one it left: nothing it inherits changed, and no style pass reached
        // its elements (the new row's mount used to mark everything under it).
        Assert.Equal(before, shell.Part.Leaves.Sum(l => l.Themings));
    }

    [Fact]
    public void AnElementMovedByHand_InheritsFromItsNewParent()
    {
        using var session = HeadlessSession.Create(300, 200);
        var held = TextB.New("held");
        var small = Flex.Row().FontSize(10f).Children(held);
        var large = Flex.Row().FontSize(20f);
        session.Window.SetRoot(Flex.Column().Children(small, large));
        session.PumpFrames(2);
        Assert.Equal(10f, held.ResolvedInherited.FontSize);

        large.AddChild(held);
        session.PumpFrames(2);

        Assert.Equal(20f, held.ResolvedInherited.FontSize);
    }

    [Fact]
    public void AThemeSwitch_StillReachesEveryElement()
    {
        var (session, pane) = Mount();
        using var _ = session;
        session.Application.Theme = Theme.Dark;
        session.PumpFrames(2);

        // A switch to another theme object remakes an item-rows list's rows; every leaf still on
        // screen, kept or made, has been themed since.
        var list = pane.List.Current!;
        for (int i = 1; i < list.Children.Count; i++)
            Assert.True(((Themed)list.Children[i].Children[0]).Themings > 0);
        Assert.Equal(Theme.Dark.TextColor, RowText(pane, 0).ResolvedInherited.TextColor);
    }
}
