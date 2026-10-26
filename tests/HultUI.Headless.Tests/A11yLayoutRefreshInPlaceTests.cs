using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A coalesced layout refresh (a scroll, a resize, a label that moved others, a list's selection
/// walked at a reader's pace) builds no tree where a build would make the same nodes: the window walks
/// the elements against the applied tree by the builder's rules and writes what changed in place — a
/// box moved in its scroll views' content, a node gone out of view, a scroll view's offset, a leaf made
/// again for its own properties — and hands the bridge the changes a build's diff would report (plan
/// 17, round 5, step 13). Each scene runs twice, as it is and with a build forced in every step's
/// frames, through the recording bridge, AT-SPI with its signals written down and UI Automation on a
/// hidden window: the bridges are told the same, in the same order, and the trees are the same after
/// every step, box for box; the run as it is builds no tree, or builds one where a build makes other
/// nodes (an id or a count that does not match).
/// </summary>
public class A11yLayoutRefreshInPlaceTests
{
    /// <summary>Counts patches and keeps the applied tree; a real bridge behind it.</summary>
    private sealed class Counting(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public int Patches;
        public AccessibilityNode? Tree;
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) { Tree = root; inner.ApplyChanges(diff, root); }
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root)
        {
            Tree = root;
            Patches++;
            inner.ApplyPatchedProperties(patch, root);
        }
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    /// <summary>What a diff or a patch names, as lines: each change's property, node and what its
    /// previous node says of it; the structural parts as counts. The forcer's automation id, which only
    /// the forced run changes, is left out.</summary>
    private sealed class Recording(Func<ulong, string> map) : IAccessibilityBridge
    {
        public readonly List<string> Lines = [];
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root)
        {
            if (diff.Added.Count + diff.Removed.Count + diff.Reordered.Count > 0)
                Lines.Add($"structure +{diff.Added.Count} -{diff.Removed.Count} r{diff.Reordered.Count}");
            foreach (var c in diff.PropertyChanged)
            {
                if (c.PropertyName == AccessibilityProperties.AutomationId) continue;
                Lines.Add($"{c.PropertyName} {map(c.Node.ElementId.Value)} prev={c.PropertyName switch
                {
                    AccessibilityProperties.ScrollPosition => $"{c.Previous?.ScrollOffset}",
                    AccessibilityProperties.RangeValue => $"{c.Previous?.Range}",
                    _ => "",
                }}");
            }
        }
        public void NotifyFocusChanged(AccessibilityNode? focused) { }
    }

    private sealed class Signals(Func<string, string> map) : IAtSpiSignals
    {
        public readonly List<string> Lines = [];
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) =>
            Lines.Add($"children-changed:{(isAdd ? "add" : "remove")} {map(parentPath)} {map(childPath)} {index}");
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) => Lines.Add($"property-change:{atSpiProperty} {map(path)} {newValue}");
        public void EmitValueChanged(string path, double value) => Lines.Add($"property-change:accessible-value {map(path)} {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        public void EmitRoleChange(string path, uint role) => Lines.Add($"property-change:accessible-role {map(path)} {role}");
        public void EmitStateChanged(string path, string state, bool active) => Lines.Add($"state-changed:{state} {map(path)} {(active ? 1 : 0)}");
        public void EmitTextChanged(string path, bool insert, int offset, string text) => Lines.Add($"text-changed {map(path)} {insert} {offset} {text}");
        public void EmitTextCaretMoved(string path, int offset) => Lines.Add($"text-caret-moved {map(path)} {offset}");
        public void EmitTextSelectionChanged(string path) => Lines.Add($"text-selection-changed {map(path)}");
        public void EmitAnnouncement(string path, string text, int politeness) => Lines.Add($"announcement {map(path)}");
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) => Lines.Add($"cache-add {map(node.Path)} {map(node.ParentPath)} {node.IndexInParent}");
        public void EmitCacheRemove(string path) => Lines.Add($"cache-remove {map(path)}");
    }

    /// <summary>A scene: what to mount, the steps, and a box whose automation id the forced run changes
    /// in each step's frames, to have the tree built there instead.</summary>
    private sealed class Scene
    {
        public required Element Root;
        public readonly BoxElement Forcer = Box.New().Width(Dimension.Px(10)).Height(Dimension.Px(10)).AccessibleName("forcer");
        public readonly List<(string Name, Action<HeadlessSession>? Before, Action<HeadlessSession> Change)> Steps = [];

        /// <summary>A change, its frame 16 ms on, and two 120 ms apart: the coalesced refresh has come
        /// (on its leading edge, the last one 240 ms back) and gone.</summary>
        public void Step(string name, Action change) => Steps.Add((name, null, _ => change()));

        public void Step(string name, Action<HeadlessSession> change) => Steps.Add((name, null, change));
    }

    public static TheoryData<string> InPlace => new()
    {
        "wheel", "item rows walk", "one-argument walk", "label edit in rows", "resize", "sticky header",
        "nested scroll views", "overlay over a scrolled root", "content in a scroll view", "list items relabelled",
        "list selection and a ticking label",
    };

    public static TheoryData<string, string> FallsBack => new()
    {
        { "items added to a list", "a list's items" },
        { "click handler added to a box", "children" },
    };

    private static BoxElement Rows(int n, string prefix = "row")
    {
        var rows = new Element[n];
        for (int i = 0; i < n; i++)
            rows[i] = Flex.Row(gap: 8).FlexShrink(0).Padding(new Thickness(2, 8)).Children(
                TextB.New($"{i}").FontSize(12f).Width(Dimension.Px(40)), TextB.New($"{prefix} {i} label").FontSize(12f));
        return Flex.Column().FlexShrink(0).Children(rows);
    }

    private sealed record Commit(int Index, string Subject);

    private static List<Commit> Commits(int n) => Enumerable.Range(0, n).Select(i => new Commit(i, $"Commit {i}")).ToList();

    private static VirtualListElement<Commit> CommitList(List<Commit> items, bool itemRows) =>
        itemRows
            ? VirtualListElement<Commit>.New().ItemHeight(24).Items(items).AccessibleItemLabel(c => c.Subject)
                .RenderWith((c, selected) => Flex.Row().Padding(new Thickness(2, 8)).Children(TextB.New(c.Subject).FontSize(12f)))
                .Height(Dimension.Px(240))
            : VirtualListElement<Commit>.New().ItemHeight(24).Items(items).AccessibleItemLabel(c => $"{c.Subject}, #{c.Index}")
                .RenderWith(c => Flex.Row().Padding(new Thickness(2, 8)).Children(TextB.New(c.Subject).FontSize(12f)))
                .Height(Dimension.Px(240));

    /// <summary>Described content (seam W11): a page of two nodes, the second inside the first.</summary>
    private sealed class Described : BoxElement, IAccessibleContentSource
    {
        public IReadOnlyList<AccessibleContentNode> AccessibleContent { get; } =
        [
            new(1, AccessibilityRole.Document, "Page", new Rect(0, 0, 100, 60))
            {
                Children = [new(42, AccessibilityRole.Button, "Press me", new Rect(10, 10, 50, 20)) { IsFocusable = true }],
            },
        ];
        public float AccessibleContentScale => 1f;
        public void PerformAccessibleAction(int key, AccessibleContentAction action, string? value) { }
    }

    private static Scene Make(string which)
    {
        Scene With(Element content, Action<Scene> steps)
        {
            var scene = new Scene { Root = null! };
            scene.Root = Flex.Column(gap: 4).Padding(8).Children(TextB.New("Header"), content, scene.Forcer, Rows(20, "tail"));
            steps(scene);
            return scene;
        }

        switch (which)
        {
            case "wheel":
            {
                var view = ScrollView.New().Height(Dimension.Px(300)).Children(Rows(100));
                return With(view, s =>
                {
                    s.Step("down 3", session => session.Window.Wheel(100, 120, 0, -3));
                    s.Step("down 1", session => session.Window.Wheel(100, 120, 0, -1));
                    s.Step("up 2", session => session.Window.Wheel(100, 120, 0, 2));
                    s.Step("to 400", () => view.ScrollOffsetY = 400);
                });
            }
            case "item rows walk" or "one-argument walk":
            {
                var list = CommitList(Commits(200), itemRows: which == "item rows walk");
                return With(list, s =>
                {
                    for (int i = 8; i < 13; i++)
                    {
                        int at = i;
                        s.Step($"select {at}", () => { list.SelectedIndex = at; list.ScrollRangeIntoView(at * 24f, 24f); });
                    }
                });
            }
            case "label edit in rows":
            {
                var rows = Rows(40);
                var label = (TextElement)rows.Children[3].Children[1];
                var view = ScrollView.New().Height(Dimension.Px(300)).Children(rows);
                return With(view, s =>
                {
                    s.Step("longer", () => label.Content += " that is longer");
                    s.Step("longer still", () => label.Content += " and longer still");
                    s.Step("short", () => label.Content = "short");
                });
            }
            case "resize":
                return With(ScrollView.New().FlexGrow(1).Children(Rows(40)), s =>
                {
                    s.Step("narrower", session => session.Window.SetClientSize(700, 600));
                    s.Step("shorter", session => session.Window.SetClientSize(700, 450));
                    s.Step("back", session => session.Window.SetClientSize(800, 600));
                });
            case "sticky header":
            {
                var header = Box.New().Height(Dimension.Px(30)).AccessibleName("Section").Sticky();
                var view = ScrollView.Vertical().Height(Dimension.Px(200)).Children(
                    Flex.Column().FlexShrink(0).Children(Rows(3, "above"), header, Rows(30)));
                return With(view, s =>
                {
                    s.Step("to 40", () => view.ScrollOffsetY = 40);
                    s.Step("to 200", () => view.ScrollOffsetY = 200);
                    s.Step("to 260", () => view.ScrollOffsetY = 260);
                    s.Step("to 0", () => view.ScrollOffsetY = 0);
                });
            }
            case "nested scroll views":
            {
                var inner = ScrollView.New().Height(Dimension.Px(100)).Children(Rows(20, "inner"));
                var outer = ScrollView.New().Height(Dimension.Px(300)).Children(
                    Flex.Column().FlexShrink(0).Children(Rows(10, "before"), inner, Rows(30, "after")));
                return With(outer, s =>
                {
                    s.Step("outer 50", () => outer.ScrollOffsetY = 50);
                    s.Step("inner 30", () => inner.ScrollOffsetY = 30);
                    s.Step("both", () => { outer.ScrollOffsetY = 120; inner.ScrollOffsetY = 75.5f; });
                });
            }
            case "overlay over a scrolled root":
            {
                var root = ScrollView.New().FlexGrow(1).Children(Rows(80));
                var check = Checkbox.New(false, "Remember");
                var menu = Flex.Column(gap: 4).Padding(8).Children(TextB.New("Menu"), Button.New("One"), check);
                var scene = new Scene { Root = root };
                scene.Steps.Add(("open", session =>
                {
                    session.Window.Window.ShowOverlay(menu, 300, 40);
                    session.PumpFrame();
                    session.PumpFrame(TimeSpan.FromMilliseconds(300));
                }, _ => root.ScrollOffsetY = 100));
                scene.Step("scroll", () => root.ScrollOffsetY = 260);
                scene.Step("scroll and toggle", () => { root.ScrollOffsetY = 30; check.IsChecked = true; });
                // The forcer, which a scene with its own root has not mounted, under the root's column.
                ((BoxElement)root.Children[0]).AddChild(scene.Forcer);
                return scene;
            }
            case "content in a scroll view":
            {
                var described = new Described { AccessibleName = "Content" };
                described.Box.Width = 200;
                described.Box.Height = 80;
                var spacer = Box.New().Height(Dimension.Px(20));
                var view = ScrollView.New().Height(Dimension.Px(200)).Children(
                    Flex.Column().FlexShrink(0).Children(Rows(5), spacer, described, Rows(20)));
                return With(view, s =>
                {
                    s.Step("scroll", () => view.ScrollOffsetY = 60);
                    s.Step("move it", () => { spacer.Box.Height = Dimension.Px(70); spacer.MarkLayoutDirty(); });
                    s.Step("both", () => { spacer.Box.Height = Dimension.Px(10); spacer.MarkLayoutDirty(); view.ScrollOffsetY = 20; });
                });
            }
            case "list items relabelled":
            {
                var items = Commits(50);
                var list = CommitList(items, itemRows: true);
                return With(list, s =>
                {
                    s.Step("rename 3", () => { items = [.. items]; items[3] = items[3] with { Subject = "Renamed 3" }; list.Items = items; });
                    s.Step("rename 40, select it", () =>
                    {
                        items = [.. items];
                        items[40] = items[40] with { Subject = "Renamed 40" };
                        list.Items = items;
                        list.SelectedIndex = 40;
                    });
                });
            }
            case "list selection and a ticking label":
            {
                var list = CommitList(Commits(100), itemRows: true);
                var elapsed = TextB.New("0:00").FontSize(12f);
                return With(Flex.Column().Children(elapsed, list), s =>
                {
                    for (int i = 1; i <= 4; i++)
                    {
                        int at = i;
                        s.Step($"tick {at}", () => { elapsed.Content = $"0:{at:00}"; list.SelectedIndex = at * 3; });
                    }
                });
            }
            case "items added to a list":
            {
                var items = Commits(30);
                var list = CommitList(items, itemRows: true);
                return With(list, s =>
                {
                    s.Step("one more", () => { items = [.. items, new Commit(30, "Commit 30")]; list.Items = items; });
                    s.Step("two fewer", () => { items = items[..^2]; list.Items = items; });
                });
            }
            case "click handler added to a box":
            {
                var box = Box.New().Padding(4).Children(TextB.New("Open"));
                var label = TextB.New("Status");
                return With(Flex.Column().Children(label, box), s =>
                    s.Step("handler", () => { box.On<ClickEvent>(_ => { }); label.Content = "Status: ready"; }));
            }
            default:
                throw new ArgumentException(which);
        }
    }

    /// <summary>What a run told and built: the bridge's lines, a dump of the tree after each step,
    /// and per step the trees built and the refreshes made in place.</summary>
    private sealed record Run(List<string> Told, List<string> Trees, int[] TreesPerStep, int[] InPlacePerStep, string? Mismatch);

    private static Run Play(string which, bool forceRebuild,
        Func<Func<ulong, string>, (IAccessibilityBridge Bridge, Func<List<string>> Told)> bridge)
    {
        using var session = HeadlessSession.Create(800, 600);
        var scene = Make(which);
        ulong baseId = MinId(scene.Root);
        string Map(ulong raw) => raw == 0 ? "root"
            : VirtualItemId.TryDecode(new ElementId(raw), out var list, out int index) ? $"v{list.Value - baseId}#{index}"
            : ContentNodeId.TryDecode(new ElementId(raw), out var owner, out int key) ? $"c{owner.Value - baseId}#{key}"
            : $"e{raw - baseId}";
        var (inner, told) = bridge(Map);
        var counting = new Counting(inner);
        session.Window.InstallAccessibilityBridge(counting);
        session.Window.SetRoot(scene.Root);
        session.PumpFrames(2);
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(300));
        told().Clear();
        var trees = new List<string>();
        var perStep = new int[scene.Steps.Count];
        var inPlace = new int[scene.Steps.Count];
        int flip = 0;
        var window = session.Window.Window;
        for (int i = 0; i < scene.Steps.Count; i++)
        {
            var (name, before, change) = scene.Steps[i];
            before?.Invoke(session);
            int built = window.AccessibilityTreesBuilt, refreshed = window.AccessibilityLayoutRefreshesInPlace;
            told().Add($"== {name}");
            change(session);
            if (forceRebuild) scene.Forcer.AutomationId = $"forced-{flip++}";
            session.PumpFrame(TimeSpan.FromMilliseconds(16));
            session.PumpFrame(TimeSpan.FromMilliseconds(120));
            session.PumpFrame(TimeSpan.FromMilliseconds(120));
            perStep[i] = window.AccessibilityTreesBuilt - built;
            inPlace[i] = window.AccessibilityLayoutRefreshesInPlace - refreshed;
            trees.Add(Dump(counting.Tree!, Map));
        }
        return new Run([.. told()], trees, perStep, inPlace, window.AccessibilityLayoutRefreshMismatch);
    }

    private static ulong MinId(Element e)
    {
        ulong min = e.Id.Value;
        foreach (var child in e.Children) min = Math.Min(min, MinId(child));
        return min;
    }

    /// <summary>Everything a node carries but its automation id, its box on screen and in its scroll
    /// views' content to the bit, its scroll view's, and that each child's parent is it.</summary>
    private static string Dump(AccessibilityNode root, Func<ulong, string> map)
    {
        var sb = new StringBuilder();
        Walk(root, 0);
        return sb.ToString();

        static string Bits(Rect r) =>
            $"{BitConverter.SingleToInt32Bits(r.X):x8}{BitConverter.SingleToInt32Bits(r.Y):x8}{BitConverter.SingleToInt32Bits(r.Width):x8}{BitConverter.SingleToInt32Bits(r.Height):x8}";

        void Walk(AccessibilityNode n, int depth)
        {
            sb.Append(' ', depth).Append(n.Parent is null ? "root" : map(n.ElementId.Value)).Append(' ').Append(n.Role)
                .Append(" n=").Append(n.Name).Append(" d=").Append(n.Description).Append(" r=").Append(n.BoundingRect)
                .Append(' ').Append(Bits(n.BoundingRect)).Append(" u=").Append(Bits(n.UnscrolledRect))
                .Append(" s=").Append(n.States).Append(" off=").Append(n.IsOffscreen).Append(" v=").Append(n.Value)
                .Append(" range=").Append(n.Range).Append(" scroll=").Append(n.ScrollOffset).Append(" extent=").Append(n.ScrollExtent)
                .Append(" items=").Append(n.ItemSource is null ? "-" : "list").Append(" kids=").Append(n.Children.Count).Append('\n');
            foreach (var c in n.Children)
            {
                Assert.Same(n, c.Parent);
                Walk(c, depth + 1);
            }
        }
    }

    private static (IAccessibilityBridge, Func<List<string>>) Recorded(Func<ulong, string> map)
    {
        var recording = new Recording(map);
        return (recording, () => recording.Lines);
    }

    private static (IAccessibilityBridge, Func<List<string>>) AtSpi(Func<ulong, string> map)
    {
        const string prefix = "/org/a11y/atspi/accessible/";
        var signals = new Signals(path => path.StartsWith(prefix) && ulong.TryParse(path.AsSpan(prefix.Length), out var raw) ? map(raw) : path);
        return (new LinuxAccessibilityBridge(signals), () => signals.Lines);
    }

    [Theory]
    [MemberData(nameof(InPlace))]
    public void ALayoutRefresh_TellsWhatABuildTells_AndBuildsNoTree(string which)
    {
        var inPlace = Play(which, forceRebuild: false, Recorded);
        var built = Play(which, forceRebuild: true, Recorded);
        Assert.Equal(built.Told, inPlace.Told);
        Assert.Equal(built.Trees, inPlace.Trees);
        Assert.All(inPlace.TreesPerStep, trees => Assert.Equal(0, trees));
        Assert.All(inPlace.InPlacePerStep, refreshes => Assert.True(refreshes > 0));
        // Something was told on the way: the scenes change what a reader hears.
        Assert.True(inPlace.Told.Count > inPlace.TreesPerStep.Length);
    }

    [Theory]
    [MemberData(nameof(InPlace))]
    public void ATSPI_ALayoutRefresh_TellsWhatABuildTells_AndBuildsNoTree(string which)
    {
        var inPlace = Play(which, forceRebuild: false, AtSpi);
        var built = Play(which, forceRebuild: true, AtSpi);
        Assert.Equal(built.Told, inPlace.Told);
        Assert.Equal(built.Trees, inPlace.Trees);
        Assert.All(inPlace.TreesPerStep, trees => Assert.Equal(0, trees));
    }

    [WindowsTheory]
    [MemberData(nameof(InPlace))]
    [SupportedOSPlatform("windows")]
    public void UIA_ALayoutRefresh_RaisesWhatABuildRaises_AndBuildsNoTree(string which)
    {
        var inPlace = PlayUia(which, forceRebuild: false);
        var built = PlayUia(which, forceRebuild: true);
        Assert.Equal(built.Told, inPlace.Told);
        Assert.Equal(built.Trees, inPlace.Trees);
        Assert.All(inPlace.TreesPerStep, trees => Assert.Equal(0, trees));
    }

    [Theory]
    [MemberData(nameof(FallsBack))]
    public void ALayoutRefreshWhereABuildMakesOtherNodes_BuildsTheTree(string which, string mismatch)
    {
        var run = Play(which, forceRebuild: false, Recorded);
        Assert.All(run.TreesPerStep, trees => Assert.Equal(1, trees));
        Assert.All(run.InPlacePerStep, refreshes => Assert.Equal(0, refreshes));
        Assert.Equal(mismatch, run.Mismatch);
        var built = Play(which, forceRebuild: true, Recorded);
        Assert.Equal(built.Told, run.Told);
        Assert.Equal(built.Trees, run.Trees);
    }

    /// <summary>A refresh a build makes other nodes for, again and again — a list its owner filters
    /// as one types — pays for a walk and a build each time. After 1, 2, 3 or 4 such refreshes in a
    /// row, the next 1, 3, 7 or 15 build at once, and one made in place starts the count again.</summary>
    [Fact]
    public void RefreshesThatKeepFallingBack_BuildWithoutAWalk_UntilOneIsMadeInPlace()
    {
        using var session = HeadlessSession.Create(800, 600);
        session.Window.InstallAccessibilityBridge();
        var items = Commits(30);
        var list = CommitList(items, itemRows: true);
        var view = ScrollView.New().Height(Dimension.Px(200)).Children(Rows(40));
        session.Window.SetRoot(Flex.Column(gap: 4).Children(list, view));
        session.PumpFrames(2);
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(300));
        var window = session.Window.Window;
        int trees = window.AccessibilityTreesBuilt, inPlace = window.AccessibilityLayoutRefreshesInPlace,
            skipped = window.AccessibilityLayoutRefreshesSkipped;
        (int Trees, int InPlace, int Skipped) Since() => (window.AccessibilityTreesBuilt - trees,
            window.AccessibilityLayoutRefreshesInPlace - inPlace, window.AccessibilityLayoutRefreshesSkipped - skipped);
        void Refresh(Action change)
        {
            change();
            session.PumpFrame(TimeSpan.FromMilliseconds(16));
            session.PumpFrame(TimeSpan.FromMilliseconds(120));
            session.PumpFrame(TimeSpan.FromMilliseconds(120));
        }
        void Filter() { items = items.Count > 20 ? items[..^1] : [.. items, new Commit(items.Count, "again")]; list.Items = items; }

        // Walks at the 1st, 3rd and 7th: 1 + 3 + 5 built without one.
        for (int i = 0; i < 12; i++) Refresh(Filter);
        Assert.Equal((12, 0, 9), Since());
        Assert.Equal("a list's items", window.AccessibilityLayoutRefreshMismatch);

        // Scrolls: the two refreshes left build, and the walk that comes next is made in place.
        for (int i = 1; i <= 3; i++) Refresh(() => view.ScrollOffsetY = 40 * i);
        Assert.Equal((14, 1, 11), Since());

        // The count starts again: a walk, then one built without.
        Refresh(Filter);
        Refresh(Filter);
        Assert.Equal((16, 1, 12), Since());
    }

    /// <summary>AT-SPI's objects read their box and offscreen state from their nodes, which a layout
    /// refresh moves in place: a scroll and a move make no snapshot, the objects stay, and each answers
    /// where its node is now — what a snapshot made of the tree as it is would answer.</summary>
    [Fact]
    public void ATSPI_ALayoutRefresh_KeepsTheObjects_AndTheyAnswerWhereTheNodesAreNow()
    {
        using var session = HeadlessSession.Create(800, 600);
        var signals = new Signals(path => path);
        using var linux = new LinuxAccessibilityBridge(signals);
        session.Window.InstallAccessibilityBridge(linux);
        var rows = Rows(60);
        var label = (TextElement)rows.Children[2].Children[1];
        var view = ScrollView.New().Height(Dimension.Px(300)).Children(rows);
        session.Window.SetRoot(Flex.Column(gap: 4).Padding(8).Children(TextB.New("Header"), view));
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(300));
        var tree = (IAtSpiTree)linux;
        var objects = tree.All.ToList();
        int applies = linux.DebugApplyChangesCalls;

        view.ScrollOffsetY = 230;
        session.PumpFrame(TimeSpan.FromMilliseconds(300));
        label.Content = "a label long enough to be another size";
        session.PumpFrame(TimeSpan.FromMilliseconds(300));

        Assert.Equal(applies, linux.DebugApplyChangesCalls);
        Assert.Equal(objects.Count, tree.All.Count);
        foreach (var acc in objects) Assert.Same(acc, tree.Find(acc.Path));
        // What a snapshot of the tree as it is now answers, object for object.
        var fresh = new LinuxAccessibilityBridge(new Signals(path => path));
        fresh.Publish(session.Window.Window.BuildAccessibilityTree()!);
        foreach (var acc in tree.All)
        {
            var now = ((IAtSpiTree)fresh).Find(acc.Path)!;
            Assert.Equal((now.Box, now.Offscreen, now.Name), (acc.Box, acc.Offscreen, acc.Name));
        }
        Assert.Contains(objects, acc => acc.Offscreen);
        Assert.Contains(objects, acc => !acc.Offscreen && acc.Name.StartsWith("row 1"));
    }

    /// <summary>A run with the UIA bridge on a hidden window: the events it raises, with what each
    /// node's wrapper answers for the property raised and for its box.</summary>
    [SupportedOSPlatform("windows")]
    private static Run PlayUia(string which, bool forceRebuild)
    {
        using var hwnd = TestHwnd.Create();
        WindowsAccessibilityBridge? uia = null;
        var lines = new List<string>();
        try
        {
            return Play(which, forceRebuild, map =>
            {
                uia = new WindowsAccessibilityBridge(hwnd.Handle, new NoHost());
                uia.Raised = r =>
                {
                    string value = "";
                    if (r.Kind == UiaRaisedKind.Property && r.Element is { } id && uia.NativeFragment(id) is var native && native != IntPtr.Zero)
                    {
                        try
                        {
                            var wrapper = (UiaFragment)NativeProvider.LogicOf(native)!;
                            wrapper.GetPropertyValue(r.Id, out var v);
                            value = v.Vt switch
                            {
                                UiaVariant.VT_BSTR => "s:" + v.AsString(),
                                UiaVariant.VT_BOOL => "b:" + v.ShortValue,
                                UiaVariant.VT_I4 => "i:" + v.IntValue,
                                UiaVariant.VT_R8 => "d:" + v.DoubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                _ => "vt" + v.Vt,
                            };
                            v.Free();
                            value += $" at {wrapper.Bounds}";
                        }
                        finally { Marshal.Release(native); }
                    }
                    lines.Add($"{r.Kind} {r.Id} {(r.Element is { } e ? map(e.Value) : "root")} {value}");
                };
                return (uia, () => lines);
            });
        }
        finally { uia?.Dispose(); }
    }

    /// <summary>A host with nothing behind it: the bridge's events, not actions a client asks for.</summary>
    private sealed class NoHost : IUiaHostBridge
    {
        public void InvokeElement(ElementId id) { }
        public void ToggleElement(ElementId id) { }
        public void SelectElement(ElementId id) { }
        public void SetValue(ElementId id, string value) { }
        public void RequestFocus(ElementId id) { }
        public float ContentScale => 1f;
        public string? GetValue(ElementId id) => null;
        public bool? GetToggleState(ElementId id) => null;
        public int GetItemCount(ElementId listId) => 0;
        public string? GetItemLabel(ElementId listId, int index) => null;
        public bool GetItemSelected(ElementId listId, int index) => false;
        public int GetSelectedIndex(ElementId listId) => -1;
        public bool GetCanSelectMultiple(ElementId listId) => false;
        public IReadOnlyList<int> GetSelectedIndices(ElementId listId) => [];
        public void SelectItemOnly(ElementId listId, int index) { }
        public void SetItemSelected(ElementId listId, int index, bool selected) { }
        public int FindItemIndex(ElementId listId, int startAfter, AccessibleItemQuery query) => -1;
        public bool GetItemOffscreen(ElementId listId, int index) => false;
        public bool TryGetItemLayout(ElementId listId, int index, out Rect layoutRect) { layoutRect = default; return false; }
        public bool TryGetScrollInfo(ElementId id, out ScrollInfo info) { info = default; return false; }
        public void SetScrollPercent(ElementId id, double horizontalPercent, double verticalPercent) { }
        public void ScrollByAmount(ElementId id, int horizontalAmount, int verticalAmount) { }
        public bool TryGetRangeValue(ElementId id, out RangeValueInfo info) { info = default; return false; }
        public void SetRangeValue(ElementId id, double value) { }
    }
}
