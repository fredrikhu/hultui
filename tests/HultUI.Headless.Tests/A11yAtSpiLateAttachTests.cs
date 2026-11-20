// The AT-SPI bridge is Linux's, but with its signals recorded instead of sent and its status
// switched by the test it runs anywhere: nothing here reaches a bus.
#pragma warning disable CA1416
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Linux;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A Linux reader that starts after the app, or <c>org.a11y.Status</c> turned on after the app had the
/// focus: the window builds the tree from nothing, and the AT-SPI bridge took no focus from it. It
/// cleared the node that held the focus and waited for the window to tell the next move, and the window
/// tells only a move, so nothing about the focus was told until the user moved it to another element: a
/// cursor walk in the list that had the focus sent no focused state at all, and a selection move only its
/// selected states (plan 17, round 17). The bridge now takes the focus the tree marks, as UI Automation's
/// does, with no signal at the attach (Orca, as it starts, looks for the focused object in the active
/// window), so a move after a late attach sends what a move after an early one sends.
/// </summary>
public class A11yAtSpiLateAttachTests
{
    private sealed class Status(bool on) : IAtSpiStatus
    {
        private Action<bool>? _changed;
        public bool On { get; private set; } = on;
        public void WatchStatus(Action<bool> changed) { _changed += changed; changed(On); }
        public void Set(bool on) { On = on; _changed?.Invoke(on); }
    }

    private sealed class Signals : IAtSpiSignals
    {
        public readonly List<string> Lines = [];
        private static string Short(string path) => path[(path.LastIndexOf('/') + 1)..];
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) => Lines.Add($"children-changed {Short(childPath)}");
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) => Lines.Add($"property-change:{atSpiProperty} {Short(path)}");
        public void EmitValueChanged(string path, double value) => Lines.Add($"property-change:accessible-value {Short(path)}");
        public void EmitRoleChange(string path, uint role) => Lines.Add($"property-change:accessible-role {Short(path)}");
        public void EmitStateChanged(string path, string state, bool active) => Lines.Add($"state-changed:{state} {Short(path)} {(active ? 1 : 0)}");
        public void EmitTextChanged(string path, bool insert, int offset, string text) => Lines.Add($"text-changed {Short(path)}");
        public void EmitTextCaretMoved(string path, int offset) => Lines.Add($"text-caret-moved {Short(path)}");
        public void EmitTextSelectionChanged(string path) => Lines.Add($"text-selection-changed {Short(path)}");
        public void EmitAnnouncement(string path, string text, int politeness) => Lines.Add($"announcement {Short(path)}");
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) => Lines.Add($"cache-add {Short(node.Path)}");
        public void EmitCacheRemove(string path) => Lines.Add($"cache-remove {Short(path)}");

        /// <summary>The focused and selected states sent, in order.</summary>
        public List<string> States() => Lines.Where(l => l.StartsWith("state-changed:focused", StringComparison.Ordinal)
            || l.StartsWith("state-changed:selected", StringComparison.Ordinal)).ToList();
    }

    /// <summary>A window with the real bridge, active while its status says assistive technology is on,
    /// as the factory gives it; off until <see cref="Attach"/> for a reader that starts late.</summary>
    private sealed class Rig : IDisposable
    {
        public HeadlessSession Session { get; } = HeadlessSession.Create(600, 500);
        public readonly Signals Signals = new();
        public readonly Status Status;
        public LinuxAccessibilityBridge Bridge { get; }

        public Rig(bool on)
        {
            AccessibilityClients.Force = null;
            Status = new Status(on);
            Bridge = new LinuxAccessibilityBridge(Signals, status: Status);
            Session.Window.InstallAccessibilityBridge(Bridge);
        }

        /// <summary>A reader starts: the status turns on, and the next frame builds the tree from nothing.</summary>
        public void Attach()
        {
            Status.Set(true);
            Session.PumpFrame();
        }

        /// <summary>The reader quits: the window drops its tree.</summary>
        public void Detach()
        {
            Status.Set(false);
            Session.PumpFrame();
        }

        /// <summary>The object of <paramref name="id"/> as a client finds it by its path: a node's, or an
        /// item served on demand.</summary>
        public AtSpiAccessible Object(ElementId id) => ((IAtSpiTree)Bridge).Find(AtSpiAccessible.PathFor(id))!;

        public void Dispose()
        {
            Bridge.Dispose();
            Session.Dispose();
            AccessibilityClients.Report(false);
        }
    }

    private static string Focused(ElementId id, bool on) => $"state-changed:focused {id.Value} {(on ? 1 : 0)}";
    private static string Selected(ElementId id, bool on) => $"state-changed:selected {id.Value} {(on ? 1 : 0)}";
    private static ElementId Item(Element list, int index) => VirtualItemId.For(list.Id, index);

    private static void Settle(HeadlessSession session)
    {
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(150));
        session.PumpFrame(TimeSpan.FromMilliseconds(150));
    }

    /// <summary>The focus lands on <paramref name="focus"/>, and the reader is there already
    /// (<paramref name="late"/> false) or starts after it (true). Then nothing a test checks is left.</summary>
    private static Rig Start(bool late, Element root, Element focus)
    {
        var rig = new Rig(on: !late);
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        focus.Focus();
        rig.Session.PumpFrame();
        if (late)
        {
            rig.Signals.Lines.Clear();
            rig.Attach();
            // The client finds the focus in the tree it is handed: nothing is told of it at the attach.
            Assert.NotEmpty(rig.Signals.Lines);
            Assert.All(rig.Signals.Lines, l => Assert.StartsWith("cache-", l));
        }
        rig.Signals.Lines.Clear();
        return rig;
    }

    /// <summary>A list that holds the focus itself, between two buttons. With <paramref name="cursor"/>
    /// its keyboard cursor is apart from its selection (<see cref="VirtualListElement{T}.CurrentIndex"/>).</summary>
    private static (ButtonElement Before, VirtualListElement<string> List, ButtonElement After, Element Root) FocusableList(
        int count, int selected, bool cursor = false)
    {
        var items = Enumerable.Range(0, count).Select(i => $"file {i}").ToList();
        var list = VirtualListElement<string>.New().ItemHeight(20).Items(items).FlexGrow(1);
        if (cursor)
            list.CurrentIndex(selected).RenderWithState((t, state) => Flex.Row()
                .Background(state.IsCurrent ? Color.Gray : Color.Transparent)
                .Children(TextB.New(t).FontSize(12f)));
        else
            list.SelectedIndex(selected).RenderWith(t => Flex.Row().Children(TextB.New(t).FontSize(12f)));
        list.IsFocusable = true;
        list.AccessibleName = "Files";
        var before = Button.New("Before");
        var after = Button.New("After");
        return (before, list, after, Flex.Column().Children(before, list, after));
    }

    // ── A list that has the focus ──

    [Theory]
    [InlineData(false, 1_000)]
    [InlineData(true, 1_000)]
    [InlineData(false, AccessibilityTreeBuilder.MaxEagerVirtualItems + 500)]   // served on demand
    [InlineData(true, AccessibilityTreeBuilder.MaxEagerVirtualItems + 500)]
    public void ACursorWalk_InTheListThatHasTheFocus_IsToldTheSame_WhenTheReaderStartedAfterIt(bool late, int count)
    {
        var (_, list, _, root) = FocusableList(count, selected: 0, cursor: true);
        using var rig = Start(late, root, list);
        // The client finds the focus on the list's current item, not on the list.
        Assert.True(rig.Object(Item(list, 0)).Focused);
        Assert.False(rig.Object(list.Id).Focused);

        for (int i = 1; i <= 3; i++)
        {
            rig.Signals.Lines.Clear();
            list.CurrentIndex = i;
            rig.Session.PumpFrame();
            Assert.Equal([Focused(Item(list, i - 1), false), Focused(Item(list, i), true)], rig.Signals.States());
            Assert.False(rig.Object(Item(list, i - 1)).Focused);
            Assert.True(rig.Object(Item(list, i)).Focused);
        }
    }

    [Theory]
    [InlineData(false, 1_000)]
    [InlineData(true, 1_000)]
    [InlineData(false, AccessibilityTreeBuilder.MaxEagerVirtualItems + 500)]
    [InlineData(true, AccessibilityTreeBuilder.MaxEagerVirtualItems + 500)]
    public void ASelectionDown_InTheListThatHasTheFocus_IsToldTheSame_WhenTheReaderStartedAfterIt(bool late, int count)
    {
        var (_, list, _, root) = FocusableList(count, selected: 3);
        using var rig = Start(late, root, list);
        Assert.True(rig.Object(Item(list, 3)).Focused);
        Assert.False(rig.Object(list.Id).Focused);

        // What the app does on Down.
        list.SelectedIndex = 4;
        rig.Session.PumpFrame();
        Assert.Equal(
        [
            Selected(Item(list, 3), false), Selected(Item(list, 4), true),
            Focused(Item(list, 3), false), Focused(Item(list, 4), true),
        ], rig.Signals.States());
        Assert.True(rig.Object(Item(list, 4)).Focused);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATabOutOfTheList_IsToldTheSame_WhenTheReaderStartedAfterIt(bool late)
    {
        var (_, list, after, root) = FocusableList(40, selected: 5);
        using var rig = Start(late, root, list);

        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.Equal([Focused(Item(list, 5), false), Focused(after.Id, true)], rig.Signals.States());
        Assert.False(rig.Object(Item(list, 5)).Focused);
        Assert.True(rig.Object(after.Id).Focused);
    }

    // ── A text field, a tree, a menu ──

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATextFieldThatHasTheFocus_IsToldTheSame_WhenTheReaderStartedAfterIt(bool late)
    {
        var fields = new[] { TextInput.New("one"), TextInput.New("two"), TextInput.New("three") };
        using var rig = Start(late, Flex.Column(gap: 4).Children(fields), fields[1]);
        Assert.True(rig.Object(fields[1].Id).Focused);

        rig.Session.Window.TypeText("x");
        rig.Session.PumpFrame();
        Assert.Contains($"property-change:accessible-value {fields[1].Id.Value}", rig.Signals.Lines);
        Assert.Empty(rig.Signals.States());

        rig.Signals.Lines.Clear();
        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.Equal([Focused(fields[1].Id, false), Focused(fields[2].Id, true)], rig.Signals.States());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATreeThatHasTheFocus_IsToldTheSame_WhenTheReaderStartedAfterIt(bool late)
    {
        // As Junction's Files tab: rows of elements, each a tree item, a folder saying whether it is open.
        var open = new bool[3];
        var after = Button.New("After");
        var tree = VirtualListElement<int>.New().ItemHeight(20).Items(Enumerable.Range(0, 3).ToList())
            .RenderWith(i =>
            {
                var row = Flex.Row().Children(TextB.New($"folder {i}").FontSize(12f));
                row.Key = $"{i}{(open[i] ? "#e" : "")}";
                row.Role = AccessibilityRole.TreeItem;
                row.AccessibleExpanded = open[i];
                return row;
            })
            .FlexGrow(1);
        tree.RowsAreComposite = true;
        tree.Role = AccessibilityRole.Tree;
        tree.IsFocusable = true;
        tree.AccessibleName = "Files";
        using var rig = Start(late, Flex.Column().Children(tree, after), tree);
        Assert.True(rig.Object(tree.Id).Focused);

        // A folder opens: its row is made again, and the focus stays where it is.
        open[1] = true;
        tree.InvalidateRows();
        for (int i = 0; i < 3; i++) rig.Session.PumpFrame(TimeSpan.FromMilliseconds(150));
        Assert.NotEmpty(rig.Signals.Lines);
        Assert.Empty(rig.Signals.States());
        Assert.True(rig.Object(tree.Id).Focused);

        rig.Signals.Lines.Clear();
        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.Equal([Focused(tree.Id, false), Focused(after.Id, true)], rig.Signals.States());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMenuOpenWhenTheReaderStarts_HoldsItsFocus_AndWalksAsOne(bool late)
    {
        var opener = Button.New("Commit actions");
        var root = Flex.Column().Children(opener, TextB.New("History"));
        using var rig = new Rig(on: !late);
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        opener.Focus();
        rig.Session.PumpFrame();
        rig.Signals.Lines.Clear();
        ContextMenuItem Entry(string label) => new() { Label = label, Action = () => { } };
        rig.Session.Window.Window.OpenMenu([Entry("Copy hash"), Entry("Revert")], new Point(20, 40), new MenuRequest { HighlightFirst = true });
        rig.Session.PumpFrame();
        if (late)
        {
            rig.Signals.Lines.Clear();
            rig.Attach();
        }
        // The menu holds the reader's focus: the window tells it so, as the menu opens or as the reader starts.
        var menu = Walk(rig.Session.Window.AccessibilityTree).First(n => n.Role == AccessibilityRole.Menu);
        Assert.Equal([Focused(opener.Id, false), Focused(VirtualItemId.For(menu.ElementId, 0), true)], rig.Signals.States());

        rig.Signals.Lines.Clear();
        rig.Session.Window.PressKey(KeyCode.Down);
        rig.Session.PumpFrame();
        var entry = VirtualItemId.For(menu.ElementId, 1);
        Assert.Equal([Focused(VirtualItemId.For(menu.ElementId, 0), false), Focused(entry, true)], rig.Signals.States());

        rig.Signals.Lines.Clear();
        rig.Session.Window.PressKey(KeyCode.Escape);
        rig.Session.PumpFrame();
        Assert.Equal([Focused(entry, false), Focused(opener.Id, true)], rig.Signals.States());
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var n in Walk(child)) yield return n;
    }

    // ── A reader that quits and starts again ──

    [Theory]
    [InlineData(1_000)]
    [InlineData(AccessibilityTreeBuilder.MaxEagerVirtualItems + 500)]
    public void AReaderThatQuitsAndStartsAgain_HearsTheNextMove_WhereTheFocusIsNow(int count)
    {
        var (before, list, _, root) = FocusableList(count, selected: 3);
        using var rig = Start(late: false, root, before);
        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.Equal([Focused(before.Id, false), Focused(Item(list, 3), true)], rig.Signals.States());

        // While no reader listens the focus leaves the list and comes back, and the selection moves.
        rig.Detach();
        before.Focus();
        rig.Session.PumpFrame();
        list.Focus();
        list.SelectedIndex = 7;
        rig.Session.PumpFrame();
        rig.Signals.Lines.Clear();
        rig.Attach();
        Assert.Empty(rig.Signals.States());
        Assert.True(rig.Object(Item(list, 7)).Focused);
        Assert.False(rig.Object(Item(list, 3)).Focused);
        Assert.False(rig.Object(before.Id).Focused);

        rig.Signals.Lines.Clear();
        list.SelectedIndex = 8;
        rig.Session.PumpFrame();
        Assert.Equal(
        [
            Selected(Item(list, 7), false), Selected(Item(list, 8), true),
            Focused(Item(list, 7), false), Focused(Item(list, 8), true),
        ], rig.Signals.States());
    }

    // ── A move the window tells as the tree is built ──

    [Fact]
    public void AFieldFocusedBeforeTheFirstTree_IsToldItHasTheFocus_WhenTheTreeIsBuilt()
    {
        // An app that focuses its first field as it starts, with the reader there already: the window
        // has no tree yet, and tells the move once the tree has the field's node. The bridge took the
        // same focus from that tree; the move is told all the same, once.
        using var rig = new Rig(on: true);
        var fields = new[] { TextInput.New("one"), TextInput.New("two") };
        rig.Session.Window.SetRoot(Flex.Column(gap: 4).Children(fields));
        fields[1].Focus();
        rig.Session.PumpFrame();

        Assert.Equal([Focused(fields[1].Id, true)], rig.Signals.States());
        Assert.True(rig.Object(fields[1].Id).Focused);
        rig.Signals.Lines.Clear();
        rig.Session.PumpFrame();
        Assert.Empty(rig.Signals.States());
    }
}
