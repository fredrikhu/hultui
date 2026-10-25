using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// The accessibility tree asks a list for every item's label each time it is built — with a
/// reader listening, on every structural change and after every scroll — and the label is the
/// app's code, a string made per call. A list on item rows promises its rows show only their
/// item and selected state; the promise covers the label too: the list keeps each item's label
/// while the item is equal to the one it was made from and the label delegate is the same, and
/// <see cref="VirtualListElement{T}.InvalidateRows"/>, new Items or another delegate make them
/// all again. A one-argument list keeps nothing, as before.
/// </summary>
public class VirtualListItemLabelTests
{
    private sealed record Entry(int Id, string Name);

    private const int Count = 200;

    private sealed class Pane(bool itemRows) : Component
    {
        public List<Entry> Items = Enumerable.Range(0, Count).Select(i => new Entry(i, $"entry {i}")).ToList();
        public int Labelled;
        public Func<Entry, string?> Label = null!;
        public int Builds;
        public readonly Ref<VirtualListElement<Entry>> List = new();

        public Func<Entry, string?> Counting(string prefix) => e => { Labelled++; return prefix + e.Name; };

        public override Element Build()
        {
            var list = VirtualListElement<Entry>.New()
                .ItemHeight(24f)
                .Items(Items)
                .AccessibleItemLabel(Label)
                .FlexGrow(1);
            if (itemRows) list.RenderWith((e, _) => Row(e));
            else list.RenderWith(e => Row(e));
            // A label with a new automation id on every build, so every build changes the tree and
            // has it built again (a new text alone is patched into the tree since plan 17's round 4).
            return Flex.Column().FlexGrow(1).Children(TextB.New("build").AutomationId($"build-{++Builds}"), list.WithRef(List));
        }

        private static Element Row(Entry e) => Flex.Row().Children(TextB.New(e.Name).FontSize(12f));

        public void Rebuild() => MarkDirty();
    }

    private static (HeadlessSession Session, Pane Pane, HeadlessAccessibilityBridge Bridge) Mount(bool itemRows = true)
    {
        var session = HeadlessSession.Create(400, 300);
        try
        {
            var pane = new Pane(itemRows);
            pane.Label = pane.Counting("");
            var bridge = session.Window.InstallAccessibilityBridge();
            session.Application.SetRoot(pane.Mount());
            session.PumpFrames(3);
            return (session, pane, bridge);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>Rebuilds the pane and pumps until the window has built a new tree.</summary>
    private static void RebuildAndBuildTree(HeadlessSession session, Pane pane)
    {
        int trees = session.Window.AccessibilityTreesBuilt;
        pane.Rebuild();
        session.PumpFrames(2);
        Assert.True(session.Window.AccessibilityTreesBuilt > trees, "no accessibility tree was built");
    }

    private static List<string?> ItemNames(HeadlessAccessibilityBridge bridge)
    {
        var found = new List<string?>();
        Walk(bridge.CurrentTree!);
        return found;

        void Walk(AccessibilityNode node)
        {
            if (node.Role == AccessibilityRole.ListItem) found.Add(node.Name);
            foreach (var child in node.Children) Walk(child);
        }
    }

    [Fact]
    public void AnUnchangedItem_KeepsItsLabel_FromOneTreeToTheNext()
    {
        var (session, pane, bridge) = Mount();
        using var _ = session;
        Assert.Equal(Count, pane.Labelled);
        pane.Labelled = 0;

        RebuildAndBuildTree(session, pane);
        RebuildAndBuildTree(session, pane);

        Assert.Equal(0, pane.Labelled);
        var names = ItemNames(bridge);
        Assert.Equal(Count, names.Count);
        Assert.Equal("entry 7", names[7]);
    }

    [Fact]
    public void AChangedItem_IsLabelledAgain_AndAnEqualOneKeepsItsLabel()
    {
        var (session, pane, bridge) = Mount();
        using var _ = session;
        pane.Labelled = 0;

        var items = pane.Items.ToList();      // a new list, as a reload makes
        items[5] = new Entry(5, "entry 5 renamed");
        items[6] = new Entry(6, "entry 6");   // a new object, equal to the old
        pane.Items = items;
        RebuildAndBuildTree(session, pane);

        Assert.Equal(1, pane.Labelled);
        Assert.Equal("entry 5 renamed", ItemNames(bridge)[5]);
    }

    [Fact]
    public void NewItemsOnTheMountedList_AreAllLabelledAgain()
    {
        var (session, pane, _) = Mount();
        using var __ = session;
        pane.Labelled = 0;

        pane.List.Current!.Items = pane.Items.ToList();   // equal items, but told as new
        RebuildAndBuildTree(session, pane);

        Assert.Equal(Count, pane.Labelled);
    }

    [Fact]
    public void InvalidateRows_LabelsEveryItemAgain()
    {
        var (session, pane, _) = Mount();
        using var __ = session;
        pane.Labelled = 0;

        pane.List.Current!.InvalidateRows();
        RebuildAndBuildTree(session, pane);

        Assert.Equal(Count, pane.Labelled);
    }

    [Fact]
    public void AnotherLabelDelegate_LabelsEveryItemAgain()
    {
        var (session, pane, bridge) = Mount();
        using var _ = session;
        pane.Labelled = 0;

        pane.Label = pane.Counting("now ");
        RebuildAndBuildTree(session, pane);

        Assert.Equal(Count, pane.Labelled);
        Assert.Equal("now entry 9", ItemNames(bridge)[9]);
    }

    /// <summary>The one-argument template promises nothing, so its labels are asked for on
    /// every tree, as they always were — also a tree that a scroll brings, with no rebuild of
    /// the owner to make the list again.</summary>
    [Fact]
    public void AOneArgumentList_LabelsEveryItemOnEveryTree()
    {
        var (session, pane, _) = Mount(itemRows: false);
        using var __ = session;
        pane.Labelled = 0;

        RebuildAndBuildTree(session, pane);
        Assert.Equal(Count, pane.Labelled);

        Assert.Equal(Count * ScrollAndBuildTree(session, pane), pane.Labelled - Count);
    }

    /// <summary>Scrolls the list a row, with no rebuild, and pumps past the 100 ms a scroll's
    /// refresh of the tree waits; returns the trees built.</summary>
    private static int ScrollAndBuildTree(HeadlessSession session, Pane pane)
    {
        int trees = session.Window.AccessibilityTreesBuilt;
        pane.List.Current!.ScrollOffsetY += 24f;
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        int built = session.Window.AccessibilityTreesBuilt - trees;
        Assert.True(built > 0, "the scroll built no accessibility tree");
        return built;
    }

    [Fact]
    public void AnUnchangedItem_KeepsItsLabel_ThroughATreeAScrollBrings()
    {
        var (session, pane, _) = Mount();
        using var __ = session;
        pane.Labelled = 0;

        ScrollAndBuildTree(session, pane);

        Assert.Equal(0, pane.Labelled);
    }

    /// <summary>A UI Automation client reads a long list's labels on its own threads. Those reads
    /// are answered without touching what the list keeps — neither read from it nor written to
    /// it, since the UI thread may be changing it — so each makes its label.</summary>
    [Fact]
    public void ALabelReadOffTheUIThread_IsNeitherKeptNorTakenFromWhatIsKept()
    {
        var (session, pane, _) = Mount();
        using var __ = session;
        var list = pane.List.Current!;
        var source = (IAccessibleItemSource)list;
        list.Items = pane.Items.ToList();   // nothing kept
        pane.Labelled = 0;

        Assert.Equal("entry 3", source.GetItemLabel(3));
        Assert.Equal("entry 3", source.GetItemLabel(3));
        Assert.Equal(1, pane.Labelled);     // made once on the UI thread, then kept

        Assert.Equal("entry 3", OffTheUIThread(() => source.GetItemLabel(3)));
        Assert.Equal("entry 4", OffTheUIThread(() => source.GetItemLabel(4)));
        Assert.Equal(3, pane.Labelled);     // off the UI thread, the kept label is not read

        Assert.Equal("entry 4", source.GetItemLabel(4));
        Assert.Equal(4, pane.Labelled);     // and the one made there was not kept

        // Blocking on purpose: an await would come back through the session's own context.
        static string? OffTheUIThread(Func<string?> read) => Task.Run(read).GetAwaiter().GetResult();
    }
}
