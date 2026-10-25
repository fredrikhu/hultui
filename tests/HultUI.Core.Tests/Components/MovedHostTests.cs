using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Core.Tests.Components;

/// <summary>
/// A persistent child component handed to its parent's next build is moved into the tree the
/// build makes and put back by the reconcile. When it lands where it was, among the same
/// siblings, nothing about its parent changed, and nothing is marked: not the parent's layout,
/// not its structure — the flag that makes a window rebuild a listening screen reader's whole
/// accessibility tree. Junction's chrome parts moved three hosts a build and turned every
/// selection move into a new tree (round 4, step 5).
/// </summary>
// Every rebuild here goes through MarkDirty, which runs the build at once: a signal write
// schedules through the process-wide reactive queue, which a test class running in parallel can
// be holding in a batch.
[Collection("Reactive")]
public class MovedHostTests
{
    private sealed class Part : Component
    {
        private int _value;
        public int Builds { get; private set; }

        public void Set(int value)
        {
            _value = value;
            MarkDirty();
        }

        public override Element Build()
        {
            Builds++;
            return Text.New($"part {_value}");
        }
    }

    /// <summary>A shell keeping <see cref="Part"/>: in row "a" or row "b" (Where), with an extra
    /// keyed child when Extra is set, or a plain label in the part's place (Plain), the control.</summary>
    private sealed class Shell(Part part) : Component
    {
        public int Where;
        public bool Extra;
        public bool Plain { get; init; }

        public void Rebuild() => MarkDirty();

        public override Element Build()
        {
            var a = Flex.Row(); a.Key = "a";
            var b = Flex.Row(); b.Key = "b";
            Element held = Plain ? Text.New("plain") : part.Mount();
            (Where == 0 ? a : b).AddChild(held);
            var label = Text.New("label"); label.Key = "label";
            (Where == 0 ? a : b).AddChild(label);
            var root = Flex.Column().Children(a, b);
            if (Extra)
            {
                var extra = Text.New("extra"); extra.Key = "extra";
                a.AddChild(extra);
            }
            return root;
        }
    }

    private static (Element Top, Element Live) Mount(Shell shell)
    {
        var top = Box.New().Children(shell.Mount());
        var live = shell.Mount().Children[0];
        Clear(top);
        return (top, live);
    }

    /// <summary>What a drawn frame leaves: nothing marked.</summary>
    private static void Clear(Element e)
    {
        e.DirtyFlags = DirtyFlags.None;
        foreach (var c in e.Children) Clear(c);
    }

    private static Element Row(Element live, string key) => live.Children.Single(c => c.Key == key);

    [Fact]
    public void AHostPutBackWhereItWas_MarksNothingAboutItsParent()
    {
        var part = new Part();
        var shell = new Shell(part);
        var (top, live) = Mount(shell);
        var host = part.Mount();
        var a = Row(live, "a");

        // The control: the same shell with a plain label where the part is.
        var control = new Shell(new Part()) { Plain = true };
        var (_, controlLive) = Mount(control);
        control.Rebuild();
        var controlA = Row(controlLive, "a");

        shell.Rebuild();

        Assert.Same(a, host.Parent);
        Assert.Same(host, a.Children[0]);
        Assert.Equal(1, part.Builds);
        Assert.Equal(DirtyFlags.None, a.DirtyFlags & DirtyFlags.Children);
        Assert.Equal(DirtyFlags.None, top.DirtyFlags & DirtyFlags.Children);
        // Whatever the patch of an unchanged row marks, and nothing more.
        Assert.Equal(controlA.DirtyFlags, a.DirtyFlags);
        Assert.Equal(DirtyFlags.None, host.DirtyFlags & (DirtyFlags.Layout | DirtyFlags.Children));

        // Still the live part: it rebuilds in place.
        part.Set(1);
        Assert.Equal(2, part.Builds);
        Assert.Equal("part 1", ((TextElement)host.Children[0]).Content);
    }

    [Fact]
    public void AHostMovedToAnotherParent_MarksTheParentItLeft()
    {
        var part = new Part();
        var shell = new Shell(part);
        var (_, live) = Mount(shell);
        var host = part.Mount();
        var a = Row(live, "a");
        var b = Row(live, "b");

        shell.Where = 1;
        shell.Rebuild();

        Assert.Same(b, host.Parent);
        Assert.Equal(DirtyFlags.Layout | DirtyFlags.Children, a.DirtyFlags & (DirtyFlags.Layout | DirtyFlags.Children));
        Assert.Equal(DirtyFlags.Layout | DirtyFlags.Children, b.DirtyFlags & (DirtyFlags.Layout | DirtyFlags.Children));
        Assert.DoesNotContain(host, a.Children);
        Assert.Equal(1, part.Builds);
    }

    /// <summary>A part that rebuilt before its parent's build changed its own text, which marked
    /// layout up the live tree: put back, it keeps that, and its parent has no structural
    /// change to report.</summary>
    [Fact]
    public void AHostThatChangedBeforeTheBuild_IsPutBackWithItsMarks()
    {
        var part = new Part();
        var shell = new Shell(part);
        var (top, live) = Mount(shell);
        var host = part.Mount();
        var a = Row(live, "a");

        part.Set(12345);
        shell.Rebuild();

        Assert.Same(a, host.Parent);
        Assert.Equal("part 12345", ((TextElement)host.Children[0]).Content);
        Assert.Equal(DirtyFlags.Layout, a.DirtyFlags & DirtyFlags.Layout);
        Assert.Equal(DirtyFlags.Layout, top.DirtyFlags & DirtyFlags.Layout);
        Assert.Equal(DirtyFlags.None, a.DirtyFlags & DirtyFlags.Children);
    }

    /// <summary>A part that rebuilds in the middle of its parent's build — while it sits in the
    /// tree that build is making — marks that tree, not the live one: put back, its marks are
    /// carried up from the parent it returns to.</summary>
    [Fact]
    public void APartRebuiltWhileMoved_CarriesItsMarksToTheParentItReturnsTo()
    {
        var part = new Part();
        var shell = new WritingShell(part);
        var top = Box.New().Children(shell.Mount());
        var live = shell.Mount().Children[0];
        Clear(top);
        var host = part.Mount();

        shell.Rebuild(writeDuringBuild: true);

        Assert.Same(live, host.Parent);
        Assert.Equal(2, part.Builds);
        Assert.Equal("part 99999", ((TextElement)host.Children[0]).Content);
        Assert.Equal(DirtyFlags.Layout, live.DirtyFlags & DirtyFlags.Layout);
        Assert.Equal(DirtyFlags.Layout, top.DirtyFlags & DirtyFlags.Layout);
    }

    /// <summary>A shell that can rebuild the part in the middle of its own build.</summary>
    private sealed class WritingShell(Part part) : Component
    {
        private bool _write;

        public void Rebuild(bool writeDuringBuild)
        {
            _write = writeDuringBuild;
            MarkDirty();
            _write = false;
        }

        public override Element Build()
        {
            var root = Flex.Column();
            root.AddChild(part.Mount());
            if (_write) part.Set(99999);
            var label = Text.New("label"); label.Key = "label";
            root.AddChild(label);
            return root;
        }
    }

    [Fact]
    public void ANewSiblingBesideAKeptHost_StillMarksTheStructure()
    {
        var part = new Part();
        var shell = new Shell(part);
        var (_, live) = Mount(shell);
        var host = part.Mount();
        var a = Row(live, "a");

        shell.Extra = true;
        shell.Rebuild();

        Assert.Same(a, host.Parent);
        Assert.Equal(["\0CH", "label", "extra"], a.Children.Select(c => c.Key!.StartsWith("\0CH") ? "\0CH" : c.Key!));
        Assert.Equal(DirtyFlags.Children, a.DirtyFlags & DirtyFlags.Children);
    }

    /// <summary>A build that moves the part into a container it then never returns leaves the
    /// part nowhere the reconcile looks: the parent it left is marked when the pass ends.</summary>
    [Fact]
    public void AHostTheBuildMovesAndDrops_StillMarksTheParentItLeft()
    {
        var part = new Part();
        var shell = new DroppingShell(part);
        var top = Box.New().Children(shell.Mount());
        var live = shell.Mount().Children[0];
        Clear(top);

        shell.Drop = true;
        shell.Rebuild();

        Assert.DoesNotContain(part.Mount(), live.Children);
        Assert.Equal(DirtyFlags.Layout | DirtyFlags.Children, live.DirtyFlags & (DirtyFlags.Layout | DirtyFlags.Children));
    }

    private sealed class DroppingShell(Part part) : Component
    {
        public bool Drop;

        public void Rebuild() => MarkDirty();

        public override Element Build()
        {
            var root = Flex.Column();
            var label = Text.New("label"); label.Key = "label";
            if (Drop)
            {
                var unused = Flex.Row();
                unused.AddChild(part.Mount());
                root.AddChild(label);
            }
            else root.Children(part.Mount(), label);
            return root;
        }
    }

    /// <summary>A container whose only child is the part: the build moves it out, so by the time
    /// the container is reconciled it has no children at all. Put back, it marks nothing either.</summary>
    [Fact]
    public void AContainerWhoseOnlyChildIsAKeptHost_GetsItBackUnmarked()
    {
        var part = new Part();
        var shell = new OnlyChildShell(part);
        var top = Box.New().Children(shell.Mount());
        var live = shell.Mount().Children[0];
        Clear(top);
        var host = part.Mount();
        var holder = live.Children.Single(c => c.Key == "holder");

        shell.Rebuild();

        Assert.Same(holder, host.Parent);
        Assert.Same(host, Assert.Single(holder.Children));
        Assert.Equal(DirtyFlags.None, holder.DirtyFlags & (DirtyFlags.Layout | DirtyFlags.Children));
        Assert.Equal(DirtyFlags.None, top.DirtyFlags & (DirtyFlags.Layout | DirtyFlags.Children));
        Assert.Equal(1, part.Builds);
        part.Set(2);
        Assert.Equal("part 2", ((TextElement)host.Children[0]).Content);
    }

    private sealed class OnlyChildShell(Part part) : Component
    {
        public void Rebuild() => MarkDirty();

        public override Element Build()
        {
            var holder = Flex.Row();
            holder.Key = "holder";
            holder.AddChild(part.Mount());
            var label = Text.New("label"); label.Key = "label";
            return Flex.Column().Children(holder, label);
        }
    }

    /// <summary>
    /// An owner that keeps live elements between builds — Vesper's Brief cells — and hands every
    /// one of a container's children back: they come back in place, the same elements, not mounted
    /// again, and the build's own container keeps none of them.
    /// </summary>
    [Fact]
    public void ElementsAllHandedBackInTheirOrder_MarkNothing()
    {
        var shell = new KeepingShell(5);
        var top = Box.New().Children(shell.Mount());
        var column = shell.Mount().Children[0].Children[0];
        var cells = column.Children.ToArray();
        Clear(top);
        int mounts = KeepingShell.Mounts;

        shell.Rebuild();

        Assert.Equal(cells, column.Children);
        Assert.All(cells, c => Assert.Same(column, c.Parent));
        Assert.Equal(mounts, KeepingShell.Mounts);
        Assert.Empty(shell.LastColumn!.Children);
        Assert.Equal(DirtyFlags.None, column.DirtyFlags & (DirtyFlags.Layout | DirtyFlags.Children));
        Assert.Equal(DirtyFlags.None, top.DirtyFlags & (DirtyFlags.Layout | DirtyFlags.Children));
    }

    [Fact]
    public void ElementsAllHandedBackInAnotherOrder_MarkTheStructure()
    {
        var shell = new KeepingShell(5);
        var top = Box.New().Children(shell.Mount());
        var column = shell.Mount().Children[0].Children[0];
        var cells = column.Children.ToArray();
        Clear(top);

        shell.Reverse = true;
        shell.Rebuild();

        Assert.Equal(cells.Reverse(), column.Children);
        Assert.Equal(DirtyFlags.Layout | DirtyFlags.Children, column.DirtyFlags & (DirtyFlags.Layout | DirtyFlags.Children));
        Assert.Equal(DirtyFlags.Children, top.DirtyFlags & DirtyFlags.Children);
    }

    [Fact]
    public void ElementsAllHandedBackWithANewOne_MarkTheStructure()
    {
        var shell = new KeepingShell(5);
        var top = Box.New().Children(shell.Mount());
        var column = shell.Mount().Children[0].Children[0];
        var cells = column.Children.ToArray();
        Clear(top);

        shell.Extra = true;
        shell.Rebuild();

        Assert.Equal(6, column.Children.Count);
        Assert.Equal(cells, column.Children.Take(5));
        Assert.Equal(DirtyFlags.Layout | DirtyFlags.Children, column.DirtyFlags & (DirtyFlags.Layout | DirtyFlags.Children));
    }

    /// <summary>A column of cells made once and handed back by every build, in order or reversed,
    /// with or without one new cell after them.</summary>
    private sealed class KeepingShell(int count) : Component
    {
        [ThreadStatic] public static int Mounts;
        private Element[]? _cells;
        public bool Reverse, Extra;
        public Element? LastColumn;

        public void Rebuild() => MarkDirty();

        public override Element Build()
        {
            _cells ??= Enumerable.Range(0, count).Select(i => (Element)new CountingBox { Key = $"cell{i}" }).ToArray();
            var column = Flex.Column();
            foreach (var cell in Reverse ? _cells.Reverse() : _cells) column.AddChild(cell);
            if (Extra) column.AddChild(Text.New("new"));
            LastColumn = column;
            return Flex.Row().Children(column);
        }

        private sealed class CountingBox : BoxElement
        {
            protected override void OnMounted() => Mounts++;
        }
    }

    /// <summary>Outside a build a move is marked at once, as it always was: only a build's own
    /// reconcile is there to put anything back.</summary>
    [Fact]
    public void AMoveOutsideABuild_MarksTheOldParentAtOnce()
    {
        var item = Box.New();
        var holder = Box.New().Children(item);
        var top = Box.New().Children(holder);
        Clear(top);

        var elsewhere = Box.New();   // not mounted
        elsewhere.AddChild(item);

        Assert.Equal(DirtyFlags.Layout | DirtyFlags.Children, holder.DirtyFlags & (DirtyFlags.Layout | DirtyFlags.Children));
    }
}
