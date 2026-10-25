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
