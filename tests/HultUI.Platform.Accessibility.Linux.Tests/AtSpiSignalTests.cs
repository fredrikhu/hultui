using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Platform.Accessibility.Linux;
using Xunit;

namespace HultUI.Platform.Accessibility.Linux.Tests;

/// <summary>
/// What the AT-SPI bridge sends for a diff, recorded in place of the bus. libatspi keeps a
/// cache of objects and of each one's children, fed by these signals: a cache-add places an
/// object under its parent at its index (in place of what is there), a cache-remove disposes it
/// (it goes defunct and leaves its parent's children), and children-changed moves entries in a
/// parent's list (<see cref="AtSpiClientCache"/>).
/// </summary>
public class AtSpiSignalTests
{
    private static string Id(Element element) => element.Id.Value.ToString();

    private sealed class Driver
    {
        public readonly SignalRecorder Signals = new();
        public readonly LinuxAccessibilityBridge Bridge;
        private readonly Element _root;
        private AccessibilityNode? _tree;

        public Driver(Element root)
        {
            _root = root;
            Bridge = new LinuxAccessibilityBridge(Signals);
            Apply();
            Signals.Lines.Clear();
        }

        /// <summary>Builds the tree again and applies its difference, as a window's frame does.</summary>
        public List<string> Apply()
        {
            Signals.Lines.Clear();
            var tree = AccessibilityTreeBuilder.Build(_root);
            Bridge.ApplyChanges(AccessibilityTreeDiffer.Diff(_tree, tree), tree);
            _tree = tree;
            return [.. Signals.Lines];
        }
    }

    [Fact]
    public void AnObjectThatMovesToAnotherParent_IsMoved_NotRemoved()
    {
        // A plain box has no object: its children belong to the object above it. Named, it is
        // one, and they move under it. The bridge sent their cache-add under the box and then a
        // cache-remove, which in libatspi disposes the object it had just placed.
        var title = TextElement.New("Title");
        var a = TextElement.New("A");
        var b = TextElement.New("B");
        var group = BoxElement.New();
        group.AddChild(a);
        group.AddChild(b);
        var root = BoxElement.New();
        root.AddChild(title);
        root.AddChild(group);
        var driver = new Driver(root);

        group.AccessibleName = "Group";
        Assert.Equal(
        [
            // Each moved object leaves its old parent's list: no cache-remove does it for them.
            $"children-changed:remove root {Id(a)} 1",
            $"children-changed:remove root {Id(b)} 2",
            $"children-changed:add root {Id(group)} 1",
            $"cache-add {Id(group)} parent=root index=1",
            $"cache-add {Id(a)} parent={Id(group)} index=0",
            $"cache-add {Id(b)} parent={Id(group)} index=1",
        ], driver.Apply());

        // And back: the box is gone, and only the box is removed.
        group.AccessibleName = null;
        Assert.Equal(
        [
            $"cache-remove {Id(group)}",
            $"children-changed:remove root {Id(group)} 1",
            // The two end the root's children: one signal, and b's cache-add lands past the end.
            $"children-changed:add root {Id(a)} 1",
            $"cache-add {Id(a)} parent=root index=1",
            $"cache-add {Id(b)} parent=root index=2",
        ], driver.Apply());
    }

    [Fact]
    public void RemovedObjects_AreStillRemoved_OneNudgePerParent()
    {
        var a = TextElement.New("A");
        var b = TextElement.New("B");
        var c = TextElement.New("C");
        var root = BoxElement.New();
        root.AddChild(a);
        root.AddChild(b);
        root.AddChild(c);
        var driver = new Driver(root);

        root.RemoveChild(b);
        root.RemoveChild(c);
        Assert.Equal(
        [
            $"cache-remove {Id(b)}",
            $"cache-remove {Id(c)}",
            $"children-changed:remove root {Id(b)} 1",
        ], driver.Apply());
    }

    // ── Children inserted together (plan 17, round 3, step 15) ──
    //
    // A cache-add places the object at its index in its parent's cached children, in place of
    // whatever was there (libatspi; AtSpiClientCache). Sent before the children-changed signals,
    // an object added in the middle replaced a sibling there, and the one signal per parent put
    // only the first added object back: [A, B, C] → [A, X, Y, B, C] read [A, X, Y] in a caching
    // client, [A, X, B, C] read [A, X, C] (seen live in WSL, every case below). Now gone objects go
    // first, then one children-changed:add per added object in the order of its parent's children
    // — each inserts it at its index — except a run that ends the parent's children, which gets one
    // for its first, and then the cache-adds, which only fill the places those made or grow the end.

    private static TextElement Named(string name) => TextElement.New(name);

    /// <summary>A list of three, as a caching client has it from the first publish.</summary>
    private sealed class Scene
    {
        public readonly TextElement A = Named("A"), B = Named("B"), C = Named("C"), X = Named("X"), Y = Named("Y");
        public readonly BoxElement List = BoxElement.New();
        public readonly LinuxAccessibilityBridge Bridge;
        public readonly AtSpiClientCache Client = new();
        public readonly SignalRecorder Recorder = new();
        private readonly BoxElement _root = BoxElement.New();
        private AccessibilityNode? _tree;

        public Scene()
        {
            List.AccessibleName = "list";
            List.AddChild(A);
            List.AddChild(B);
            List.AddChild(C);
            _root.AddChild(TextElement.New("Title"));
            _root.AddChild(List);
            Bridge = new LinuxAccessibilityBridge(new Both(Client, Recorder));
            Apply();
        }

        public void Apply()
        {
            Recorder.Lines.Clear();
            var tree = AccessibilityTreeBuilder.Build(_root);
            Bridge.ApplyChanges(AccessibilityTreeDiffer.Diff(_tree, tree), tree);
            _tree = tree;
        }

        /// <summary>The list's children as the client reads them, by element.</summary>
        public List<string> Cached() =>
            [.. Client.ChildrenOf(Bridge, Path(List)).Select(p => Name(p))];

        /// <summary>The list's children as they are.</summary>
        public List<string> Real() => [.. List.Children.Select(e => ((TextElement)e).Content)];

        private string Name(string path) =>
            ((IAtSpiTree)Bridge).Find(path)?.Name ?? path;

        public static string Path(Element e) => AtSpiConstants.AccessiblePathPrefix + e.Id.Value;
    }

    /// <summary>Signals to two listeners.</summary>
    private sealed class Both(IAtSpiSignals a, IAtSpiSignals b) : IAtSpiSignals
    {
        public void EmitChildrenChanged(string p, string c, int i, bool add) { a.EmitChildrenChanged(p, c, i, add); b.EmitChildrenChanged(p, c, i, add); }
        public void EmitPropertyChange(string path, string prop, string v) { a.EmitPropertyChange(path, prop, v); b.EmitPropertyChange(path, prop, v); }
        public void EmitValueChanged(string path, double v) { a.EmitValueChanged(path, v); b.EmitValueChanged(path, v); }
        public void EmitRoleChange(string path, uint role) { a.EmitRoleChange(path, role); b.EmitRoleChange(path, role); }
        public void EmitStateChanged(string path, string s, bool on) { a.EmitStateChanged(path, s, on); b.EmitStateChanged(path, s, on); }
        public void EmitTextChanged(string path, bool ins, int o, string t) { a.EmitTextChanged(path, ins, o, t); b.EmitTextChanged(path, ins, o, t); }
        public void EmitTextCaretMoved(string path, int o) { a.EmitTextCaretMoved(path, o); b.EmitTextCaretMoved(path, o); }
        public void EmitTextSelectionChanged(string path) { a.EmitTextSelectionChanged(path); b.EmitTextSelectionChanged(path); }
        public void EmitAnnouncement(string path, string t, int p) { a.EmitAnnouncement(path, t, p); b.EmitAnnouncement(path, t, p); }
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) { a.EmitCacheAdd(tree, node); b.EmitCacheAdd(tree, node); }
        public void EmitCacheRemove(string path) { a.EmitCacheRemove(path); b.EmitCacheRemove(path); }
    }

    public static TheoryData<string> InsertCases => new()
    {
        "middle1", "middle2", "front2", "end2", "apart2", "middleAndEnd", "replace", "removeAppend", "removeInsert", "split",
    };

    [Theory]
    [MemberData(nameof(InsertCases))]
    public void ChildrenAddedTogether_AreWhereTheyAre_InACachingClient(string which)
    {
        var s = new Scene();
        Assert.Equal(["A", "B", "C"], s.Cached());

        Element[] kids = which switch
        {
            "middle1" => [s.A, s.X, s.B, s.C],
            "middle2" => [s.A, s.X, s.Y, s.B, s.C],
            "front2" => [s.X, s.Y, s.A, s.B, s.C],
            "end2" => [s.A, s.B, s.C, s.X, s.Y],
            "apart2" => [s.A, s.X, s.B, s.Y, s.C],
            "middleAndEnd" => [s.A, s.X, s.B, s.C, s.Y],
            "replace" => [s.A, s.X, s.C],
            "removeAppend" => [s.A, s.C, s.X],
            "removeInsert" => [s.X, s.B, s.Y],
            "split" => [s.A, s.X, s.B, s.C],
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        };
        s.List.ReplaceChildren(kids);
        s.Apply();
        if (which == "split")
        {
            s.List.ReplaceChildren(s.A, s.X, s.Y, s.B, s.C);
            s.Apply();
        }

        Assert.Equal(s.Real(), s.Cached());
    }

    [Fact]
    public void TwoInsertedInTheMiddle_AreTwoSignals_EachAtItsIndex_BeforeTheirCacheAdds()
    {
        var s = new Scene();
        s.List.ReplaceChildren(s.A, s.X, s.Y, s.B, s.C);
        s.Apply();

        string list = SignalRecorder.Short(Scene.Path(s.List));
        Assert.Equal(
        [
            $"children-changed:add {list} {Id(s.X)} 1",
            $"children-changed:add {list} {Id(s.Y)} 2",
            $"cache-add {Id(s.X)} parent={list} index=1",
            $"cache-add {Id(s.Y)} parent={list} index=2",
        ], s.Recorder.Lines);
    }

    [Fact]
    public void ABulkAppend_IsStillOneSignal()
    {
        var s = new Scene();
        var more = Enumerable.Range(0, 50).Select(i => (Element)TextElement.New($"row {i}")).ToArray();
        s.List.ReplaceChildren([s.A, s.B, s.C, .. more]);
        s.Apply();

        Assert.Single(s.Recorder.Lines, l => l.StartsWith("children-changed:add", StringComparison.Ordinal));
        Assert.Equal(50, s.Recorder.Lines.Count(l => l.StartsWith("cache-add", StringComparison.Ordinal)));
        Assert.Equal(s.Real(), s.Cached());
    }
}
