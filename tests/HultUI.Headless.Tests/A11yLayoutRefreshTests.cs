using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A layout pass refreshed the whole accessibility tree, at most every 100 ms, whether or not it
/// moved anything: a component rebuilt on every frame of a tween patches its elements, a kind of
/// element that is not sure its patch left its size alone (a button, a check box) marks its
/// layout, and a reader listening cost a whole tree every 100 ms for an empty diff. The refresh
/// now comes when an element's box on screen, or its padding or border, is not what it was at the
/// last look (plan 17, round 3, step 15). The proof is a shadow: at the end of every frame where
/// the old rule would have refreshed and the new one left nothing pending, a tree built afresh
/// must equal the applied one (<see cref="Window.SkippedA11yRefreshAudit"/>).
/// </summary>
public class A11yLayoutRefreshTests
{
    private sealed class Audited
    {
        public readonly List<TreeDiff> Audits = [];

        public void AssertEveryAuditEmpty(int atLeast)
        {
            Assert.True(Audits.Count >= atLeast, $"{Audits.Count} skipped refreshes audited, expected at least {atLeast}");
            foreach (var diff in Audits)
                Assert.True(diff.IsEmpty, Describe(diff));
        }

        private static string Describe(TreeDiff diff) =>
            $"a skipped refresh would have told: +{diff.Added.Count} -{diff.Removed.Count} reordered {diff.Reordered.Count}, "
            + string.Join(", ", diff.PropertyChanged.Select(c => $"{c.PropertyName} on {c.Node.Role} '{c.Node.Name}'"));
    }

    private static (HeadlessSession Session, HeadlessAccessibilityBridge Bridge, Audited Audit) Session(int height = 2000)
    {
        var session = HeadlessSession.Create(800, height);
        var bridge = session.Window.InstallAccessibilityBridge();
        var audit = new Audited();
        session.Window.Window.SkippedA11yRefreshAudit = audit.Audits.Add;
        return (session, bridge, audit);
    }

    /// <summary>Lets the mount's refresh come and go.</summary>
    private static void Settle(HeadlessSession session)
    {
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
    }

    /// <summary>Rows of buttons and check boxes, rebuilt on every frame of a tween, as its owner is:
    /// their patch marks their layout, and nothing moves unless a row is made wider.</summary>
    private sealed class Rows : Component
    {
        public int Frame;
        public int WideRow = -1;

        public override Element Build()
        {
            var column = Flex.Column(gap: 2);
            for (int i = 0; i < 60; i++)
            {
                column.AddChild(Flex.Row(gap: 8).Children(
                    Button.New($"Open {i}").Width(Dimension.Px(i == WideRow ? 180 : 120)),
                    Checkbox.New(i % 2 == 0, $"Keep {i}"),
                    TextB.New($"row {i}")));
            }
            // What the tween animates: a colour, which moves nothing.
            column.Background(new Color((byte)(Frame % 255), 40, 40, 255));
            return column;
        }

        public void Tick()
        {
            Frame++;
            MarkDirty();
        }
    }

    [Fact]
    public void ARebuildEveryFrameThatMovesNothing_BuildsNoTree_AndTheShadowFindsNothingToTell()
    {
        var (session, _, audit) = Session();
        using var _ = session;
        var rows = new Rows();
        session.Window.SetRoot(rows.Mount());
        Settle(session);
        int trees = session.Window.AccessibilityTreesBuilt;

        for (int f = 0; f < 150; f++)
        {
            rows.Tick();
            session.PumpFrame(TimeSpan.FromMilliseconds(16));
        }

        // 150 frames, 2.4 s: a tree every 100 ms before (24), none now.
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        audit.AssertEveryAuditEmpty(atLeast: 140);
    }

    [Fact]
    public void ARebuildThatResizesAButton_IsTold()
    {
        var (session, bridge, audit) = Session();
        using var _ = session;
        var rows = new Rows();
        session.Window.SetRoot(rows.Mount());
        Settle(session);
        int trees = session.Window.AccessibilityTreesBuilt;
        bridge.ClearDiffs();

        rows.WideRow = 7;
        rows.Tick();
        session.PumpFrame(TimeSpan.FromMilliseconds(16));

        Assert.Equal(trees + 1, session.Window.AccessibilityTreesBuilt);
        var diff = Assert.Single(bridge.Diffs);
        Assert.Contains(diff.PropertyChanged, c => c.PropertyName == AccessibilityProperties.BoundingRect && c.Node.Name == "Open 7");
        audit.AssertEveryAuditEmpty(atLeast: 0);
    }

    [Fact]
    public void RowsAListMakesAsItIsDrawn_AreToldByTheCoalescedRefresh()
    {
        // The rows of a list of controls are made in the render pass, after the frame's look at
        // the boxes: what they moved asks for the refresh at the end of the frame.
        var (session, bridge, audit) = Session(600);
        using var _ = session;
        var list = VirtualListElement<string>.New().ItemHeight(30)
            .RenderWith(t => Button.New(t))
            .Items(Enumerable.Range(0, 40).Select(i => $"film {i}").ToList());
        list.RowsAreComposite = true;
        session.Window.SetRoot(Flex.Column().Children(TextB.New("Library"), list.FlexGrow(1)));
        Settle(session);
        Assert.Contains("film 3", Names(bridge.CurrentTree!));

        list.Items = Enumerable.Range(0, 40).Select(i => $"show {i}").ToList();
        session.PumpFrame(TimeSpan.FromMilliseconds(16));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));

        var names = Names(bridge.CurrentTree!);
        Assert.Contains("show 3", names);
        Assert.DoesNotContain("film 3", names);
        audit.AssertEveryAuditEmpty(atLeast: 0);
    }

    /// <summary>A screen rebuilt as a whole on every change, as Junction's is: a toolbar, a filter
    /// field, options, a section that opens, and a list on item rows whose selection moves.</summary>
    private sealed class Screen : Component
    {
        public string Filter = "";
        public readonly bool[] Options = new bool[12];
        public bool Open;
        public int Selected = -1;
        public int Tick;
        public readonly List<string> Items = Enumerable.Range(0, 300).Select(i => $"commit {i}").ToList();

        public override Element Build()
        {
            var toolbar = Flex.Row(gap: 6).Children(
                Button.New("Fetch"), Button.New("Pull"), Button.New("Push"),
                TextB.New($"filter: {Filter}"));
            var options = Flex.Column(gap: 2);
            for (int i = 0; i < Options.Length; i++) options.AddChild(Checkbox.New(Options[i], $"option {i}"));
            var section = Expander.New("Details", TextB.New("Inside the section"));
            section.IsExpanded = Open;
            var list = VirtualListElement<string>.New().ItemHeight(24)
                .RenderWith((t, selected) => Flex.Row().Children(TextB.New(selected ? "> " + t : t)))
                .Items(Items).Height(Dimension.Px(240));
            list.SelectedIndex = Selected;
            // What a tween would animate between the changes: a colour.
            return Flex.Column(gap: 6).Background(new Color((byte)(Tick % 255), 30, 30, 255))
                .Children(toolbar, options, section, list);
        }

        public void Rebuild() => MarkDirty();
    }

    [Fact]
    public void TheShadow_FindsNothingToTell_AcrossAScreenRebuiltOnEveryChange()
    {
        var (session, _, audit) = Session(900);
        using var _ = session;
        var screen = new Screen();
        session.Window.SetRoot(screen.Mount());
        Settle(session);

        void Step(Action change, int ms = 16)
        {
            change();
            screen.Rebuild();
            session.PumpFrame(TimeSpan.FromMilliseconds(ms));
        }

        for (int i = 0; i < 30; i++) Step(() => screen.Tick++);                       // moves nothing
        foreach (char c in "needle") Step(() => screen.Filter += c);                  // a label grows
        for (int i = 0; i < 12; i++) Step(() => screen.Options[i] = !screen.Options[i]); // a toggle, in place
        for (int i = 0; i < 20; i++) Step(() => screen.Selected = i, 60);             // the selection walks
        Step(() => screen.Open = true, 120);                                          // the section opens
        for (int i = 0; i < 10; i++) Step(() => screen.Tick++);
        Step(() => screen.Open = false, 120);
        session.Window.SetClientSize(820, 880);
        for (int i = 0; i < 10; i++) Step(() => screen.Tick++, 30);
        session.PumpFrame(TimeSpan.FromMilliseconds(120));

        audit.AssertEveryAuditEmpty(atLeast: 40);
    }

    /// <summary>A list on item rows whose owner rebuilds with its items.</summary>
    private sealed class History : Component
    {
        public List<string> Items = Enumerable.Range(0, 300).Select(i => $"commit {i}").ToList();

        public override Element Build() =>
            Flex.Column().Children(
                TextB.New("History"),
                VirtualListElement<string>.New().ItemHeight(24)
                    .RenderWith((t, selected) => Flex.Row().Children(TextB.New(t)))
                    .Items(Items).Height(Dimension.Px(240)));

        public void Rebuild() => MarkDirty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnItemChangedOffScreen_IsTold_ThoughNoRowMoved(bool sameList)
    {
        // Its row is not made, the list's box stays: only the item's label, in the item nodes a
        // list publishes for every item, says it changed.
        var (session, bridge, audit) = Session(600);
        using var _ = session;
        var history = new History();
        session.Window.SetRoot(history.Mount());
        Settle(session);

        if (sameList) history.Items[200] = "amended 200";
        else history.Items = [.. history.Items.Take(200), "amended 200", .. history.Items.Skip(201)];
        history.Rebuild();
        session.PumpFrame(TimeSpan.FromMilliseconds(16));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));

        Assert.Contains("amended 200", Names(bridge.CurrentTree!));
        audit.AssertEveryAuditEmpty(atLeast: 0);
    }

    [Fact]
    public void ABoxThatMovesInAnOverlay_IsTold()
    {
        // An overlay in the tree (a menu, a dialog) is laid out apart from the window's elements:
        // its boxes are looked at too. A layout change in one used to be told only when the page
        // under it happened to be laid out in the same frame.
        var (session, bridge, audit) = Session(600);
        using var _ = session;
        var root = Flex.Column().Children(TextB.New("Page"));
        session.Window.SetRoot(root);
        Settle(session);
        var panel = Box.New().Width(Dimension.Px(100)).Height(Dimension.Px(20)).AccessibleName("panel");
        root.OverlayHost!.ShowOverlay(Box.New().Width(200).Children(panel, Button.New("OK")), 250, 10);
        Settle(session);
        bridge.ClearDiffs();

        panel.Width(Dimension.Px(140));
        panel.MarkLayoutDirty();
        session.PumpFrame(TimeSpan.FromMilliseconds(16));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));

        Assert.Contains(bridge.Diffs.SelectMany(d => d.PropertyChanged),
            c => c.PropertyName == AccessibilityProperties.BoundingRect && c.Node.Name == "panel");
        audit.AssertEveryAuditEmpty(atLeast: 0);
    }

    private static List<string?> Names(AccessibilityNode node)
    {
        var names = new List<string?>();
        void Walk(AccessibilityNode n)
        {
            names.Add(n.Name);
            foreach (var c in n.Children) Walk(c);
        }
        Walk(node);
        return names;
    }
}
