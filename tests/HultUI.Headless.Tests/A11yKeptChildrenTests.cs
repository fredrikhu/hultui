// The AT-SPI bridge is Linux's, but with its signals recorded instead of sent it runs anywhere:
// nothing here reaches a bus.
#pragma warning disable CA1416
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A component that keeps live elements between builds hands them back to its next build, and the
/// reconciler puts them back where they were without marking their container's structure — also
/// when the build handed back every child of a container, which leaves it with none by the time it
/// is reconciled (plan 17, round 5). Vesper's Brief view keeps its cells so: with a reader listening,
/// every cursor key built the whole accessibility tree, because the columns whose cells were all
/// kept marked their structure as the cells came back one by one. Each case runs twice, once as it
/// is and once with a change in the same frame that cannot be patched (an automation id), so the
/// tree is rebuilt; what each bridge tells, and the trees, must be the same.
/// </summary>
public class A11yKeptChildrenTests
{
    /// <summary>Three columns of six cells. A cell is handed back as the live element while the
    /// cursor's place on it is what it was made with; the cursor's cell and the one it left are made
    /// again (and patched onto the live ones). One column can be handed back in reverse.</summary>
    private sealed class Grid : Component
    {
        public const int Columns = 3, PerColumn = 6;
        public int Cursor;
        public bool ReverseMiddle;
        public int Builds;
        private readonly Dictionary<string, (Element Cell, bool Cursor)> _live = [];

        public void Rebuild() => MarkDirty();

        public override Element Build()
        {
            Builds++;
            var columns = new Element[Columns];
            for (int c = 0; c < Columns; c++)
            {
                var column = Flex.Column(gap: 2).Width(Dimension.Px(120));
                column.Key = $"col{c}";
                for (int r = 0; r < PerColumn; r++)
                    column.AddChild(Cell(c * PerColumn + (ReverseMiddle && c == 1 ? PerColumn - 1 - r : r)));
                columns[c] = column;
            }
            return Flex.Row(gap: 8).Children(columns);
        }

        private Element Cell(int i)
        {
            bool cursor = i == Cursor;
            string key = $"cell{i}";
            if (_live.TryGetValue(key, out var kept) && kept.Cursor == cursor) return kept.Cell;
            var cell = Box.New().Width(Dimension.Px(110)).Height(Dimension.Px(20))
                .Background(cursor ? new Color(51, 102, 204) : Color.Transparent)
                .AccessibleName(cursor ? $"file {i}, current" : $"file {i}");
            cell.Key = key;
            return cell;
        }

        /// <summary>The cells as they are on screen now: the elements the next build hands back.</summary>
        protected internal override void OnAfterBuild()
        {
            if (Host is not { Children.Count: > 0 } host) return;
            foreach (var column in host.Children[0].Children)
                foreach (var cell in column.Children)
                    _live[cell.Key!] = (cell, cell.AccessibleName!.EndsWith(", current"));
        }
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

    /// <summary>A real bridge, reported active whether or not a client listens; keeps the tree.</summary>
    private sealed class Keeping(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public AccessibilityNode? Tree;
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) { Tree = root; inner.ApplyChanges(diff, root); }
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) { Tree = root; inner.ApplyPatchedProperties(patch, root); }
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    private sealed record Run(List<string> Told, List<string> Trees, int[] TreesPerStep, int[] BuildsPerStep);

    private static readonly (string Name, Action<Grid> Change)[] Steps =
    [
        ("rebuild, nothing changed", _ => { }),
        ("cursor down", g => g.Cursor = 1),
        ("cursor to the last column", g => g.Cursor = 13),
        ("rebuild again", _ => { }),
        ("middle column reversed", g => g.ReverseMiddle = true),
        ("middle column in order", g => g.ReverseMiddle = false),
        ("cursor back to the first cell", g => g.Cursor = 0),
    ];

    /// <summary>The steps whose rebuild hands every changed cell back in its place: no structure
    /// changed, so no tree.</summary>
    private static readonly HashSet<string> NoStructure = ["rebuild, nothing changed", "cursor down", "cursor to the last column", "rebuild again", "cursor back to the first cell"];

    private static Run Play(bool forceRebuild, Func<Func<ulong, string>, (IAccessibilityBridge Bridge, Func<List<string>> Told)> bridge)
    {
        using var session = HeadlessSession.Create(600, 400);
        var grid = new Grid();
        var forcer = Box.New().Width(Dimension.Px(10)).Height(Dimension.Px(10)).AccessibleName("forcer");
        var root = Flex.Column(gap: 4).Padding(8).Children(TextB.New("Files"), grid.Mount(), forcer);
        ulong baseId = MinId(root);
        string Map(ulong raw) => raw == 0 ? "root" : $"e{raw - baseId}";
        var (inner, told) = bridge(Map);
        var keeping = new Keeping(inner);
        session.Window.InstallAccessibilityBridge(keeping);
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(300));
        told().Clear();

        var trees = new List<string>();
        var perStep = new int[Steps.Length];
        var builds = new int[Steps.Length];
        for (int i = 0; i < Steps.Length; i++)
        {
            var (name, change) = Steps[i];
            int built = session.Window.AccessibilityTreesBuilt;
            int b = grid.Builds;
            told().Add($"== {name}");
            change(grid);
            grid.Rebuild();
            if (forceRebuild) forcer.AutomationId = $"forced-{i}";
            session.PumpFrame(TimeSpan.FromMilliseconds(16));
            session.PumpFrame(TimeSpan.FromMilliseconds(300));
            perStep[i] = session.Window.AccessibilityTreesBuilt - built;
            builds[i] = grid.Builds - b;
            trees.Add(Dump(keeping.Tree!, Map));
        }
        return new Run([.. told()], trees, perStep, builds);
    }

    private static ulong MinId(Element e)
    {
        ulong min = e.Id.Value;
        foreach (var child in e.Children) min = Math.Min(min, MinId(child));
        return min;
    }

    /// <summary>Everything a node carries but its automation id, and that each child's parent is it.</summary>
    private static string Dump(AccessibilityNode root, Func<ulong, string> map)
    {
        var sb = new StringBuilder();
        Walk(root, 0);
        return sb.ToString();

        void Walk(AccessibilityNode n, int depth)
        {
            sb.Append(' ', depth).Append(n.Parent is null ? "root" : map(n.ElementId.Value)).Append(' ').Append(n.Role)
                .Append(" n=").Append(n.Name).Append(" r=").Append(n.BoundingRect).Append(" s=").Append(n.States)
                .Append(" sel=").Append(n.IsSelected).Append(" kids=").Append(n.Children.Count).Append('\n');
            foreach (var c in n.Children)
            {
                Assert.Same(n, c.Parent);
                Walk(c, depth + 1);
            }
        }
    }

    private static (IAccessibilityBridge, Func<List<string>>) AtSpi(Func<ulong, string> map)
    {
        const string prefix = "/org/a11y/atspi/accessible/";
        var signals = new Signals(path => path.StartsWith(prefix) && ulong.TryParse(path.AsSpan(prefix.Length), out var raw) ? map(raw) : path);
        return (new LinuxAccessibilityBridge(signals), () => signals.Lines);
    }

    private static void AssertSameAsARebuild(Run kept, Run rebuilt)
    {
        Assert.Equal(rebuilt.Told, kept.Told);
        Assert.Equal(rebuilt.Trees, kept.Trees);
        Assert.All(kept.BuildsPerStep, b => Assert.Equal(1, b));
        for (int i = 0; i < Steps.Length; i++)
        {
            if (NoStructure.Contains(Steps[i].Name))
                Assert.True(kept.TreesPerStep[i] == 0, $"{Steps[i].Name}: {kept.TreesPerStep[i]} trees");
            else
                Assert.True(kept.TreesPerStep[i] >= 1, $"{Steps[i].Name}: no tree for a new order");
        }
    }

    [Fact]
    public void ATSPI_CellsHandedBack_TellWhatARebuildTells_AndBuildNoTree() =>
        AssertSameAsARebuild(Play(forceRebuild: false, AtSpi), Play(forceRebuild: true, AtSpi));

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void UIA_CellsHandedBack_RaiseWhatARebuildRaises_AndBuildNoTree() =>
        AssertSameAsARebuild(PlayUia(forceRebuild: false), PlayUia(forceRebuild: true));

    /// <summary>A run with the UIA bridge on a hidden window: the events it raises.</summary>
    [SupportedOSPlatform("windows")]
    private static Run PlayUia(bool forceRebuild)
    {
        using var hwnd = TestHwnd.Create();
        WindowsAccessibilityBridge? uia = null;
        var lines = new List<string>();
        try
        {
            return Play(forceRebuild, map =>
            {
                uia = new WindowsAccessibilityBridge(hwnd.Handle, new NoHost());
                uia.Raised = r => lines.Add($"{r.Kind} {r.Id} {(r.Element is { } e ? map(e.Value) : "root")}");
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
