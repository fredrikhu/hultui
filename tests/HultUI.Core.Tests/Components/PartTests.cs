using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;
using Xunit;

namespace HultUI.Core.Tests.Components;

/// <summary>
/// <see cref="Part"/>: a piece of an owner's build that the owner keeps and places. Its rule is
/// Vesper's: a revision a full rebuild moves is a plain counter, read where the owner's build
/// places the part, so the part builds in that build, before the owner's OnAfterBuild.
/// </summary>
[Collection("Reactive")]
public class PartTests
{
    /// <summary>An owner with a bar and a label beside it, in the shape an app has.</summary>
    private sealed class Owner : Component
    {
        public readonly PartGroup Parts = new();
        public readonly Signal<int> View = new(0);
        public readonly Signal<int> Own = new(0);
        public readonly Signal<int> Shown = new(0);
        public string Title = "first";
        public bool Bar = true;
        public bool Stale;
        public int OwnerBuilds, AfterBuilds;
        public string? SeenAfterBuild;
        public readonly List<string> Order = [];
        private Part? _bar;

        public Part BarPart => _bar ??= new Part(Parts, () =>
        {
            Order.Add("bar");
            return Box.New().Children(Text.New(Title + Shown.Value));
        }, Own, () => AfterBuilds++);

        public TextElement BarText => (TextElement)BarPart.Mount().Children[0].Children[0];

        public override Element Build()
        {
            OwnerBuilds++;
            Order.Add("owner");
            var root = Box.New();
            if (Bar) root.AddChild(BarPart.Place(Stale));
            var label = Text.New("view " + View.Value);
            label.Key = "view";
            root.AddChild(label);
            return root;
        }

        protected internal override void OnAfterBuild()
        {
            Order.Add("after");
            if (_bar?.Mount() is { Children.Count: > 0 } host) SeenAfterBuild = ((TextElement)host.Children[0].Children[0]).Content;
        }

        public new void MarkDirty()
        {
            Parts.Invalidate();
            base.MarkDirty();
        }

        public void MarkViewDirty() => base.MarkDirty();
    }

    [Fact]
    public void AViewOnlyBuild_LeavesThePartAsItIs()
    {
        var owner = new Owner();
        owner.Mount();
        var bar = owner.BarPart.Mount().Children[0];
        int builds = owner.BarPart.Builds;

        owner.View.Value++;
        owner.MarkViewDirty();

        Assert.Equal(builds, owner.BarPart.Builds);
        Assert.Same(bar, owner.BarPart.Mount().Children[0]);
    }

    [Fact]
    public void AnInvalidatedGroup_BuildsThePartWhereItIsPlaced_BeforeTheOwnersOnAfterBuild()
    {
        var owner = new Owner();
        owner.Mount();
        owner.Order.Clear();

        owner.Title = "second";
        owner.MarkDirty();

        Assert.Equal(["owner", "bar", "after"], owner.Order);
        Assert.Equal("second0", owner.SeenAfterBuild);
        Assert.Equal("second0", owner.BarText.Content);
    }

    [Fact]
    public void InvalidatingAlone_BuildsNothing_UntilThePartIsNextPlaced()
    {
        var owner = new Owner();
        owner.Mount();
        int builds = owner.BarPart.Builds;

        owner.Title = "second";
        owner.Parts.Invalidate();
        Assert.Equal(builds, owner.BarPart.Builds);

        owner.MarkViewDirty();
        Assert.Equal(builds + 1, owner.BarPart.Builds);
        Assert.Equal("second0", owner.BarText.Content);
    }

    [Fact]
    public void AStalePlace_BuildsThePart()
    {
        var owner = new Owner();
        owner.Mount();
        int builds = owner.BarPart.Builds;

        owner.Title = "second";
        owner.Stale = true;
        owner.MarkViewDirty();

        Assert.Equal(builds + 1, owner.BarPart.Builds);
        Assert.Equal("second0", owner.BarText.Content);
    }

    [Fact]
    public void ItsOwnRevision_BuildsThePartAlone()
    {
        var owner = new Owner();
        owner.Mount();
        int builds = owner.BarPart.Builds, ownerBuilds = owner.OwnerBuilds;

        owner.Title = "second";
        owner.Own.Value++;

        Assert.Equal(builds + 1, owner.BarPart.Builds);
        Assert.Equal(ownerBuilds, owner.OwnerBuilds);
        Assert.Equal("second0", owner.BarText.Content);
    }

    /// <summary>In a batch, as in an event: the owner's build places the part, which builds there,
    /// before the owner's OnAfterBuild, and the build its own revision asked for finds nothing new.</summary>
    [Fact]
    public void ItsOwnRevisionInABatch_BuildsOnce_WhereTheOwnerPlacesIt()
    {
        var owner = new Owner();
        owner.Mount();
        int builds = owner.BarPart.Builds;

        owner.Title = "second";
        ReactiveRuntime.Batch(() =>
        {
            owner.Own.Value++;
            owner.MarkViewDirty();
        });

        Assert.Equal(builds + 1, owner.BarPart.Builds);
        Assert.Equal("second0", owner.SeenAfterBuild);
    }

    [Fact]
    public void ASignalItsBuildReads_BuildsThePartAlone()
    {
        var owner = new Owner();
        owner.Mount();
        int ownerBuilds = owner.OwnerBuilds;

        owner.Shown.Value = 7;

        Assert.Equal(ownerBuilds, owner.OwnerBuilds);
        Assert.Equal("first7", owner.BarText.Content);
    }

    [Fact]
    public void AfterBuild_RunsAfterEachBuildButTheFirst()
    {
        var owner = new Owner();
        owner.Mount();
        Assert.Equal(0, owner.AfterBuilds);

        owner.MarkDirty();
        Assert.Equal(1, owner.AfterBuilds);
        owner.Own.Value++;
        Assert.Equal(2, owner.AfterBuilds);
    }

    /// <summary>Taken out of the tree the part is not built; placed again it shows what is true.</summary>
    [Fact]
    public void APartPlacedAgain_ShowsWhatMovedWhileItWasOut()
    {
        var owner = new Owner();
        var root = new BoxElement();
        root.AddChild(owner.Mount());
        owner.Bar = false;
        owner.MarkDirty();
        int builds = owner.BarPart.Builds;

        owner.Title = "second";
        owner.MarkDirty();
        Assert.Equal(builds, owner.BarPart.Builds);

        owner.Bar = true;
        owner.MarkDirty();
        Assert.Equal("second0", owner.BarText.Content);

        owner.Shown.Value = 3;
        Assert.Equal("second3", owner.BarText.Content);
    }

    [Fact]
    public void TheGroup_CountsTheBuildsOfAllItsParts()
    {
        var group = new PartGroup();
        var one = new Part(group, () => Box.New());
        var two = new Part(group, () => Box.New());
        one.Mount();
        two.Mount();
        group.Invalidate();
        one.Place();
        two.Place();
        one.Place();

        Assert.Equal(4, group.Builds);
        Assert.Equal(2, one.Builds);
        Assert.Equal(2, two.Builds);
    }
}
