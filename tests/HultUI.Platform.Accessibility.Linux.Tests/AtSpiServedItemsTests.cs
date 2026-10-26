using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Platform.Accessibility.Linux;
using Xunit;

namespace HultUI.Platform.Accessibility.Linux.Tests;

/// <summary>
/// A list over 2,000 items serves its items on demand: the tree has no node for them, so the list's
/// object answers ChildCount with their number and makes an item's object when a client asks for it
/// (plan 17, round 5, step 14; until then ChildCount was 0 and no item could be reached). A caching
/// client (libatspi, <see cref="AtSpiClientCache"/>) keeps a list of each object's children, sized by
/// the cache-add's child count; these pin that its list stays right as the list's items come and go,
/// past 2,000 and back, and that an item object it holds is disposed when, and only when, its item
/// goes.
/// </summary>
public class AtSpiServedItemsTests
{
    private const int Served = 3_000;

    private static string[] Rows(int count) => [.. Enumerable.Range(0, count).Select(i => $"row {i}")];

    private sealed class Scene
    {
        public readonly VirtualListElement<string> List = VirtualListElement<string>.New();
        public readonly BoxElement Root = BoxElement.New();
        public readonly AtSpiClientCache Client = new();
        public readonly SignalRecorder Recorder = new();
        public readonly LinuxAccessibilityBridge Bridge;
        private AccessibilityNode? _tree;

        public Scene(int count)
        {
            List.Items = Rows(count);
            List.ItemTemplate = s => TextElement.New(s);
            Root.AddChild(TextElement.New("Title"));
            Root.AddChild(List);
            Bridge = new LinuxAccessibilityBridge(new Both(Client, Recorder));
            Apply();
        }

        public IAtSpiTree Tree => Bridge;

        /// <summary>Builds the tree again and applies its difference, as a window's frame does; the
        /// signals it sent, shortened.</summary>
        public List<string> Apply()
        {
            Recorder.Lines.Clear();
            var tree = AccessibilityTreeBuilder.Build(Root);
            Bridge.ApplyChanges(AccessibilityTreeDiffer.Diff(_tree, tree), tree);
            _tree = tree;
            return [.. Recorder.Lines];
        }

        public AtSpiAccessible ListObject => Tree.Find(Path(List.Id))!;

        public string ItemPath(int index) => Path(VirtualItemId.For(List.Id, index));

        public string Item(int index) => VirtualItemId.For(List.Id, index).Value.ToString();

        /// <summary>The list's children as the client reads them.</summary>
        public List<string> Cached() => Client.ChildrenOf(Tree, Path(List.Id));

        /// <summary>The list's children as they are.</summary>
        public List<string> Real() => [.. Enumerable.Range(0, List.Items.Count).Select(ItemPath)];

        public static string Path(ElementId id) => AtSpiConstants.AccessiblePathPrefix + id.Value;
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

    [Fact]
    public void AListOf3000_IsOneObject_AndServesEachItemWhenAsked()
    {
        var scene = new Scene(Served);
        scene.List.SelectedIndex = 1_500;
        scene.Apply();

        var list = scene.ListObject;
        Assert.Equal(Served, list.ChildCount);
        Assert.Equal(3, scene.Tree.All.Count);   // the application, the title, the list
        Assert.Equal(Served, scene.Cached().Count);   // the cache-add sized the client's list
        foreach (int index in new[] { 0, 1_500, Served - 1 })
        {
            var item = scene.Tree.Find(list.ChildPathAt(index)!)!;
            Assert.Equal(($"row {index}", index, list.Path), (item.Name, item.IndexInParent, item.ParentPath));
            Assert.Equal(index == 1_500, item.Selected);
        }
        Assert.Null(list.ChildPathAt(Served));
        Assert.Null(scene.Tree.Find(scene.ItemPath(Served)));
        Assert.Equal(3, scene.Tree.All.Count);
    }

    [Fact]
    public void GoingPast2000Items_KeepsTheItemsObjects_AndGrowsTheClientsList()
    {
        // The item nodes go from the tree, and the list serves the same items at the same paths: none
        // of them is disposed, and the client's list grows to the new number.
        var scene = new Scene(1_500);
        Assert.Equal(scene.Real(), scene.Cached());

        scene.List.Items = Rows(Served);
        var signals = scene.Apply();

        Assert.Equal(
        [
            $"children-changed:add {scene.List.Id.Value} {scene.Item(1_500)} 1500",
            $"cache-add {scene.List.Id.Value} parent=root index=1",
        ], signals);
        Assert.Equal(scene.Real(), scene.Cached());
        Assert.Equal("row 5", scene.Tree.Find(scene.ItemPath(5))!.Name);
    }

    [Fact]
    public void ComingBackUnder2000Items_PutsTheItemNodesInTheClientsList_AndDisposesTheItemsPastTheEnd()
    {
        var scene = new Scene(Served);
        var list = scene.ListObject;
        string held = list.ChildPathAt(Served - 1)!, kept = list.ChildPathAt(5)!;
        Assert.Equal(Served, scene.Cached().Count);

        scene.List.Items = Rows(1_500);
        var signals = scene.Apply();

        // Each item node's cache-add fills its place; the item held past the new end is disposed; the
        // list, published again, sizes the client's list to its 1,500 children.
        Assert.Equal(1_500, signals.Count(s => s.StartsWith("cache-add ", StringComparison.Ordinal)
            && s.Contains($"parent={scene.List.Id.Value}", StringComparison.Ordinal)));
        Assert.Equal(
        [
            $"cache-remove {scene.Item(Served - 1)}",
            $"children-changed:remove {scene.List.Id.Value} {scene.Item(1_500)} 1500",
            $"cache-add {scene.List.Id.Value} parent=root index=1",
        ], signals.Where(s => !s.Contains($"parent={scene.List.Id.Value}", StringComparison.Ordinal)));
        Assert.Equal(scene.Real(), scene.Cached());
        Assert.Null(scene.Tree.Find(held));
        Assert.Equal("row 5", scene.Tree.Find(kept)!.Name);
    }

    [Fact]
    public void ItemsGoingWithTheEnd_DisposeOnlyTheObjectsAClientMayHold()
    {
        var scene = new Scene(Served);
        var list = scene.ListObject;
        list.ChildPathAt(2_990);
        list.ChildPathAt(10);

        scene.List.Items = Rows(2_980);
        Assert.Equal(
        [
            $"cache-remove {scene.Item(2_990)}",
            $"children-changed:remove {scene.List.Id.Value} {scene.Item(2_980)} 2980",
            $"cache-add {scene.List.Id.Value} parent=root index=1",
        ], scene.Apply());
        Assert.Equal(scene.Real(), scene.Cached());

        // A client that took every child at once (GetChildren) may hold any of them.
        list = scene.ListObject;
        list.Served!.HandedOutAll();
        scene.List.Items = Rows(2_977);
        Assert.Equal(
        [
            $"cache-remove {scene.Item(2_977)}",
            $"cache-remove {scene.Item(2_978)}",
            $"cache-remove {scene.Item(2_979)}",
            $"children-changed:remove {scene.List.Id.Value} {scene.Item(2_977)} 2977",
            $"cache-add {scene.List.Id.Value} parent=root index=1",
        ], scene.Apply());
        Assert.Equal(scene.Real(), scene.Cached());

        // Grown: one signal for the first that came, and the client's list sized to the new number.
        scene.List.Items = Rows(Served);
        Assert.Equal(
        [
            $"children-changed:add {scene.List.Id.Value} {scene.Item(2_977)} 2977",
            $"cache-add {scene.List.Id.Value} parent=root index=1",
        ], scene.Apply());
        Assert.Equal(scene.Real(), scene.Cached());
    }

    [Fact]
    public void AListThatGoes_TakesTheItemsAClientHeldWithIt()
    {
        var scene = new Scene(Served);
        // The items' ids pack the list's owner slot, which it gives back as it unmounts: read first.
        string seven = SignalRecorder.Short(scene.ListObject.ChildPathAt(7)!);
        string twoThousand = SignalRecorder.Short(scene.ListObject.ChildPathAt(2_000)!);

        scene.Root.RemoveChild(scene.List);
        var signals = scene.Apply();

        Assert.Contains($"cache-remove {scene.List.Id.Value}", signals);
        Assert.Contains($"cache-remove {seven}", signals);
        Assert.Contains($"cache-remove {twoThousand}", signals);
        Assert.Equal(3, signals.Count(s => s.StartsWith("cache-remove", StringComparison.Ordinal)));
    }

    [Fact]
    public void ASelectionMove_IsToldOnTheItems_AsAListOfItemNodesTellsIt()
    {
        string[] Moves(int count)
        {
            var scene = new Scene(count);
            scene.List.SelectedIndex = 3;
            scene.Apply();
            var told = new List<string>();
            foreach (int index in new[] { 4, 1_200, 1_199, -1 })
            {
                scene.List.SelectedIndex = index;
                told.AddRange(scene.Apply());
            }
            return [.. told.Select(ByIndex)];
        }

        // An item's id, by its index.
        static string ByIndex(string line)
        {
            var parts = line.Split(' ');
            return parts.Length == 3 && ulong.TryParse(parts[1], out var id)
                && VirtualItemId.TryDecode(new ElementId(id), out _, out int index) ? $"{parts[0]} item{index} {parts[2]}" : line;
        }

        var eager = Moves(1_500);
        Assert.Equal(
        [
            "state-changed:selected item3 0", "state-changed:selected item4 1",
            "state-changed:selected item4 0", "state-changed:selected item1200 1",
            "state-changed:selected item1199 1", "state-changed:selected item1200 0",
            "state-changed:selected item1199 0",
        ], eager);
        Assert.Equal(eager, Moves(Served));
    }
}
