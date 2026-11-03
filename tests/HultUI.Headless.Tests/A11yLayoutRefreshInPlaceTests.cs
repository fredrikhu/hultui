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

    /// <summary>What a diff or a patch names, as lines: each node added with its parent, each removed
    /// with its parent and its place among that parent's children as they were, each parent reordered,
    /// each change's property, node and what its previous node says of it. The forcer's automation id,
    /// which only the forced run changes, is left out.</summary>
    private sealed class Recording(Func<ulong, string> map) : IAccessibilityBridge
    {
        public readonly List<string> Lines = [];
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root)
        {
            string Id(AccessibilityNode? n) => n is null ? "-" : n.Parent is null ? "root" : map(n.ElementId.Value);
            foreach (var a in diff.Added) Lines.Add($"+ {Id(a.Node)} {a.Node.Role} {a.Node.Name} under {Id(a.Parent)}");
            foreach (var r in diff.Removed)
            {
                int at = -1;
                for (int i = 0; r.Parent is { } p && i < p.Children.Count; i++)
                    if (p.Children[i].ElementId == r.Node.ElementId) at = i;
                Lines.Add($"- {Id(r.Node)} under {Id(r.Parent)} at {at}");
            }
            foreach (var r in diff.Reordered) Lines.Add($"reordered {Id(r)}");
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
        "list selection and a ticking label", "items added to a list", "click handler added to a box",
    };

    /// <summary>The scenes whose steps add or remove nodes: a build's diff has them, and the walk tells
    /// them so (plan 17, round 7, step 7).</summary>
    private static readonly HashSet<string> Structural = ["items added to a list", "click handler added to a box"];

    public static TheoryData<string, string> FallsBack => new()
    {
        { "items past 2,000", "a list's items" },
        { "a node with children named", "a node with children changed" },
    };

    private static BoxElement Rows(int n, string prefix = "row")
    {
        var rows = new Element[n];
        for (int i = 0; i < n; i++) rows[i] = OneRow($"{i}", $"{prefix} {i} label");
        return Flex.Column().FlexShrink(0).Children(rows);
    }

    private static BoxElement OneRow(string number, string label) =>
        Flex.Row(gap: 8).FlexShrink(0).Padding(new Thickness(2, 8)).Children(
            TextB.New(number).FontSize(12f).Width(Dimension.Px(40)), TextB.New(label).FontSize(12f));

    /// <summary>A list whose rows are buttons a reader reads as its own (RowsAreComposite), as
    /// Aperture's grid and Junction's Changes are: the owner's cursor is in the items, so a row the
    /// cursor reaches or leaves is another item, and the list makes it again.</summary>
    private static VirtualListElement<Cursored> CompositeList(int count, int cursor)
    {
        var list = VirtualListElement<Cursored>.New().ItemHeight(24).Items(CursorItems(count, cursor))
            .RenderWith((c, _) => Button.New(c.Cursor ? $"{c.Commit.Subject}, cursor" : c.Commit.Subject))
            .Height(Dimension.Px(240));
        list.RowsAreComposite = true;
        return list;
    }

    private sealed record Cursored(Commit Commit, bool Cursor);

    private static List<Cursored> CursorItems(int count, int cursor) =>
        Enumerable.Range(0, count).Select(i => new Cursored(new Commit(i, $"Commit {i}"), i == cursor)).ToList();

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
            case "rows added and removed":
            {
                var rows = Rows(40);
                var view = ScrollView.New().Height(Dimension.Px(300)).Children(rows);
                return With(view, s =>
                {
                    s.Step("one in at 5", () => rows.InsertChild(5, OneRow("n1", "new one")));
                    s.Step("row 10 gone", () => rows.RemoveChild(rows.Children[10]));
                    s.Step("two at the end, the first gone", () =>
                    {
                        rows.AddChild(OneRow("n2", "new two"));
                        rows.AddChild(OneRow("n3", "new three"));
                        rows.RemoveChild(rows.Children[0]);
                    });
                    s.Step("scrolled, one in at 20", () => { view.ScrollOffsetY = 200; rows.InsertChild(20, OneRow("n4", "new four")); });
                });
            }
            case "rows moved":
            {
                var rows = Rows(30);
                var view = ScrollView.New().Height(Dimension.Px(300)).Children(rows);
                void Move(int from, int to)
                {
                    var row = rows.Children[from];
                    rows.RemoveChild(row);
                    rows.InsertChild(to, row);
                }
                return With(view, s =>
                {
                    s.Step("3 after 8", () => Move(3, 8));
                    s.Step("the last first", () => Move(rows.Children.Count - 1, 0));
                    s.Step("two swapped, one gone", () => { Move(6, 7); rows.RemoveChild(rows.Children[12]); });
                });
            }
            case "a row made again":
            {
                var rows = Rows(30);
                var label = (TextElement)rows.Children[12].Children[1];
                var view = ScrollView.New().Height(Dimension.Px(300)).Children(rows);
                return With(view, s =>
                {
                    s.Step("row 4 made again", () => { rows.RemoveChild(rows.Children[4]); rows.InsertChild(4, OneRow("4", "row 4 label")); });
                    s.Step("one in at 2, a label after it renamed", () => { rows.InsertChild(2, OneRow("n", "new")); label.Content = "row 12, renamed"; });
                });
            }
            case "composite list walk":
            {
                var list = CompositeList(200, cursor: 7);
                return With(list, s =>
                {
                    for (int i = 8; i < 13; i++)
                    {
                        int at = i;
                        s.Step($"cursor to {at}", () => { list.Items = CursorItems(200, at); list.ScrollRangeIntoView(at * 24f, 24f); });
                    }
                });
            }
            case "composite list wheel":
            {
                var list = CompositeList(200, cursor: 2);
                return With(list, s =>
                {
                    s.Step("down 3", session => session.Window.Wheel(100, 120, 0, -3));
                    s.Step("down 1", session => session.Window.Wheel(100, 120, 0, -1));
                    s.Step("up 2", session => session.Window.Wheel(100, 120, 0, 2));
                });
            }
            case "rows changed under an overlay":
            {
                var rows = Rows(30);
                var root = ScrollView.New().FlexGrow(1).Children(rows);
                var menu = Flex.Column(gap: 4).Padding(8).Children(TextB.New("Menu"), Button.New("One"));
                var scene = new Scene { Root = root };
                scene.Steps.Add(("open", session =>
                {
                    session.Window.Window.ShowOverlay(menu, 300, 40);
                    session.PumpFrame();
                    session.PumpFrame(TimeSpan.FromMilliseconds(300));
                }, _ => rows.InsertChild(3, OneRow("n", "new under the menu"))));
                scene.Step("one gone, scrolled", () => { rows.RemoveChild(rows.Children[5]); root.ScrollOffsetY = 60; });
                rows.AddChild(scene.Forcer);
                return scene;
            }
            case "items past 2,000":
            {
                var list = CommitList(Commits(1_990), itemRows: true);
                return With(list, s =>
                {
                    s.Step("past 2,000", () => list.Items = Commits(2_010));
                    s.Step("back under", () => list.Items = Commits(1_990));
                });
            }
            case "a node with children named":
            {
                // A tab named by the text inside it, with a close button of its own: its name is its label's.
                var label = TextB.New("Inbox").FontSize(12f);
                var item = Flex.Row(gap: 8).AccessibleRole(AccessibilityRole.Tab).Children(label, Button.New("Close"));
                return With(Flex.Column().Children(item), s =>
                {
                    s.Step("renamed", () => label.Content = "Inbox, 3 unread");
                    s.Step("renamed again", () => label.Content = "Inbox, 12 unread messages");
                });
            }
            default:
                throw new ArgumentException(which);
        }
    }

    /// <summary>What a run told and built: the bridge's lines, a dump of the tree after each step,
    /// and per step the trees built and the refreshes made in place.</summary>
    private sealed record Run(List<string> Told, List<string> Trees, int[] TreesPerStep, int[] InPlacePerStep, string? Mismatch,
        int Structural);

    /// <summary>A scene's run. Forced, every refresh builds the tree (and the forcer's automation id
    /// changes in each step's frames, as it did before the window had a switch for it).</summary>
    private static Run Play(string which, bool forceRebuild,
        Func<Func<ulong, string>, (IAccessibilityBridge Bridge, Func<List<string>> Told)> bridge)
    {
        using var session = HeadlessSession.Create(800, 600);
        session.Window.Window.AccessibilityBuildsEveryRefresh = forceRebuild;
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
        int structural = window.AccessibilityStructureRefreshesInPlace;
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
        return new Run([.. told()], trees, perStep, inPlace, window.AccessibilityLayoutRefreshMismatch,
            window.AccessibilityStructureRefreshesInPlace - structural);
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
        AssertNoTree(inPlace);
        Assert.All(inPlace.InPlacePerStep, refreshes => Assert.True(refreshes > 0));
        // Something was told on the way: the scenes change what a reader hears.
        Assert.True(inPlace.Told.Count > inPlace.TreesPerStep.Length);
        // Nodes added or removed, told as a build's diff tells them, with nothing built but the new ones.
        Assert.Equal(Structural.Contains(which), inPlace.Structural > 0);
        if (Structural.Contains(which))
            Assert.Contains(inPlace.Told, l => l.StartsWith("+ ") || l.StartsWith("- ") || l.StartsWith("reordered "));
        Assert.All(built.TreesPerStep, trees => Assert.True(trees > 0));
    }

    private static void AssertNoTree(Run run) =>
        Assert.True(run.TreesPerStep.All(t => t == 0),
            $"trees a step {string.Join(",", run.TreesPerStep)}; the last walk that did not match: {run.Mismatch}");

    [Theory]
    [MemberData(nameof(InPlace))]
    public void ATSPI_ALayoutRefresh_TellsWhatABuildTells_AndBuildsNoTree(string which)
    {
        var inPlace = Play(which, forceRebuild: false, AtSpi);
        var built = Play(which, forceRebuild: true, AtSpi);
        Assert.Equal(built.Told, inPlace.Told);
        Assert.Equal(built.Trees, inPlace.Trees);
        AssertNoTree(inPlace);
        Assert.Equal(Structural.Contains(which), inPlace.Structural > 0);
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
        AssertNoTree(inPlace);
        Assert.Equal(Structural.Contains(which), inPlace.Structural > 0);
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

    /// <summary>A refresh a build makes other nodes for than a walk can, again and again — a list its
    /// owner filters as one types, across the 2,000 items it serves on demand from — pays for a walk and
    /// a build each time. After 1, 2, 3 or 4 such refreshes in a row, the next 1, 3, 7 or 15 build at
    /// once, and one made in place starts the count again.</summary>
    [Fact]
    public void RefreshesThatKeepFallingBack_BuildWithoutAWalk_UntilOneIsMadeInPlace()
    {
        using var session = HeadlessSession.Create(800, 600);
        session.Window.InstallAccessibilityBridge();
        var items = Commits(2_010);
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
        // 2,010 and 1,990 items in turn: served on demand, then a node each.
        void Filter() { items = items.Count > 2_000 ? items[..1_990] : Commits(2_010); list.Items = items; }

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
