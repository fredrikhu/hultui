using System.Runtime.Versioning;
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
/// The window lends a bridge that is done with a diff when the call returns
/// (<see cref="ITreeDiffBorrower"/>: the UIA and AT-SPI bridges) one diff of its own for every patch
/// and every layout refresh made in place, emptied after the call. A new one was about 280 bytes a
/// keystroke, a check box's click or a list cursor's key (plan 17, round 9, step 5). Any other bridge
/// gets a new one each time, which it may keep: the headless bridge records them. A reader is told
/// the same either way.
/// </summary>
public class A11yLentDiffTests
{
    /// <summary>Forwards to a bridge and, while <see cref="Counting"/>, writes down each diff it is
    /// handed and how many entries it held during the call.</summary>
    private class Forwarding(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public readonly List<(TreeDiff Diff, int Entries)> Handed = [];
        public bool Counting;
        public bool IsActive => true;

        public void ApplyChanges(TreeDiff diff, AccessibilityNode root)
        {
            Note(diff);
            inner.ApplyChanges(diff, root);
        }

        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root)
        {
            Note(patch);
            inner.ApplyPatchedProperties(patch, root);
        }

        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);

        private void Note(TreeDiff diff)
        {
            if (Counting) Handed.Add((diff, Entries(diff)));
        }
    }

    /// <summary>The same, done with each diff when the call returns, as the bridges behind it are. It
    /// keeps a reference only to show what such a reference sees after the call.</summary>
    private sealed class Borrowing(IAccessibilityBridge inner) : Forwarding(inner), ITreeDiffBorrower;

    private static int Entries(TreeDiff diff) =>
        diff.Added.Count + diff.Removed.Count + diff.Reordered.Count + diff.PropertyChanged.Count;

    /// <summary>AT-SPI's signals as lines, each path named by the order it first appears in, so two
    /// runs over elements of their own compare.</summary>
    private sealed class Signals : IAtSpiSignals
    {
        public readonly List<string> Lines = [];
        private readonly Dictionary<string, int> _names = [];
        private string N(string path) => _names.TryGetValue(path, out int n) ? $"p{n}" : $"p{_names[path] = _names.Count}";
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) =>
            Lines.Add($"children-changed:{(isAdd ? "add" : "remove")} {N(parentPath)} {N(childPath)} {index}");
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) => Lines.Add($"property-change:{atSpiProperty} {N(path)} {newValue}");
        public void EmitValueChanged(string path, double value) => Lines.Add($"value {N(path)} {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        public void EmitRoleChange(string path, uint role) => Lines.Add($"role {N(path)} {role}");
        public void EmitStateChanged(string path, string state, bool active) => Lines.Add($"state-changed:{state} {N(path)} {(active ? 1 : 0)}");
        public void EmitTextChanged(string path, bool insert, int offset, string text) => Lines.Add($"text-changed {N(path)} {insert} {offset} {text}");
        public void EmitTextCaretMoved(string path, int offset) => Lines.Add($"text-caret-moved {N(path)} {offset}");
        public void EmitTextSelectionChanged(string path) => Lines.Add($"text-selection-changed {N(path)}");
        public void EmitAnnouncement(string path, string text, int politeness) => Lines.Add($"announcement {N(path)} {text}");
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) => Lines.Add($"cache-add {N(node.Path)} {N(node.ParentPath)} {node.IndexInParent}");
        public void EmitCacheRemove(string path) => Lines.Add($"cache-remove {N(path)}");
    }

    /// <summary>A scene: what to mount, what to do before the steps, and the steps.</summary>
    private sealed record Scene(Element Root, Action<HeadlessSession>? Setup, List<Action<HeadlessSession>> Steps);

    public static TheoryData<string> Scenes => new() { "typing", "check box", "wheel", "rows added and removed" };

    private static BoxElement Rows(int n, string prefix = "row")
    {
        var rows = new Element[n];
        for (int i = 0; i < n; i++) rows[i] = OneRow($"{i}", $"{prefix} {i} label");
        return Flex.Column().FlexShrink(0).Children(rows);
    }

    private static BoxElement OneRow(string number, string label) =>
        Flex.Row(gap: 8).FlexShrink(0).Padding(new Thickness(2, 8)).Children(
            TextB.New(number).FontSize(12f).Width(Dimension.Px(40)), TextB.New(label).FontSize(12f));

    private static Scene Make(string which)
    {
        static Element Page(Element content) => Flex.Column(gap: 4).Padding(8).Children(TextB.New("Header"), content);
        switch (which)
        {
            case "typing":
            {
                // A text input's keystrokes: each makes its node again for its value (a leaf's patch).
                var input = TextInput.New("").Width(Dimension.Px(300));
                return new Scene(Page(input), s => s.Window.Click(input),
                [
                    s => s.Window.TypeText("a"), s => s.Window.TypeText("b"), s => s.Window.TypeText("c"),
                    s => s.Window.PressKey(KeyCode.Backspace),
                ]);
            }
            case "check box":
            {
                // A leaf's own toggle: its node is made again and patched in.
                var check = Checkbox.New(false, "Remember");
                return new Scene(Page(Flex.Column().Children(check, Rows(20))), null,
                    [s => s.Window.Click(check), s => s.Window.Click(check), s => s.Window.Click(check)]);
            }
            case "wheel":
            {
                // A scroll: the layout refresh made in place, its changes as patched properties.
                var view = ScrollView.New().Height(Dimension.Px(300)).Children(Rows(100));
                return new Scene(Page(view), null,
                [
                    s => s.Window.Wheel(100, 120, 0, -3), s => s.Window.Wheel(100, 120, 0, -1),
                    s => s.Window.Wheel(100, 120, 0, 2), _ => view.ScrollOffsetY = 400,
                ]);
            }
            case "rows added and removed":
            {
                // Nodes added and removed: the layout refresh made in place, told as a build's diff.
                var rows = Rows(40);
                var view = ScrollView.New().Height(Dimension.Px(300)).Children(rows);
                return new Scene(Page(view), null,
                [
                    _ => rows.InsertChild(5, OneRow("n1", "new one")),
                    _ => rows.RemoveChild(rows.Children[10]),
                    _ => { rows.AddChild(OneRow("n2", "new two")); rows.RemoveChild(rows.Children[0]); },
                ]);
            }
            default:
                throw new ArgumentException(which);
        }
    }

    /// <summary>Mounts the scene behind <paramref name="bridge"/> and plays its steps, each with its
    /// frame 16 ms on and two 120 ms apart (the coalesced layout refresh comes and goes); the diffs
    /// handed over during the steps are written down. The steps build no tree: each is a patch or a
    /// refresh made in place.</summary>
    private static Forwarding Play(string which, Func<HeadlessSession, Forwarding> install)
    {
        using var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge(install(session));
        var scene = Make(which);
        session.Window.SetRoot(scene.Root);
        session.PumpFrames(2);
        scene.Setup?.Invoke(session);
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(300));
        int trees = session.Window.AccessibilityTreesBuilt;
        bridge.Counting = true;
        foreach (var step in scene.Steps)
        {
            step(session);
            session.PumpFrame(TimeSpan.FromMilliseconds(16));
            session.PumpFrame(TimeSpan.FromMilliseconds(120));
            session.PumpFrame(TimeSpan.FromMilliseconds(120));
        }
        bridge.Counting = false;
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.True(bridge.Handed.Count >= scene.Steps.Count, $"{bridge.Handed.Count} diffs over {scene.Steps.Count} steps");
        Assert.All(bridge.Handed, h => Assert.True(h.Entries > 0));
        return bridge;
    }

    /// <summary>The borrower was handed one diff, again and again, empty after each call; the other
    /// a new one each time, which it keeps whole.</summary>
    private static void AssertLentAndNot(Forwarding borrowing, Forwarding forwarding)
    {
        var lent = borrowing.Handed[0].Diff;
        Assert.All(borrowing.Handed, h => Assert.Same(lent, h.Diff));
        Assert.True(lent.IsEmpty);
        Assert.Equal(forwarding.Handed.Count, forwarding.Handed.Select(h => h.Diff).Distinct().Count());
        Assert.All(forwarding.Handed, h => Assert.Equal(h.Entries, Entries(h.Diff)));
    }

    [Theory]
    [MemberData(nameof(Scenes))]
    public void ATSPI_IsLentOneDiff_EmptiedAfterEachCall_AndTellsWhatANewOneTells(string which)
    {
        var lentSignals = new Signals();
        using var lentBridge = new LinuxAccessibilityBridge(lentSignals);
        var borrowing = Play(which, _ => new Borrowing(lentBridge));

        var newSignals = new Signals();
        using var newBridge = new LinuxAccessibilityBridge(newSignals);
        var forwarding = Play(which, _ => new Forwarding(newBridge));

        AssertLentAndNot(borrowing, forwarding);
        Assert.Equal(forwarding.Handed.Count, borrowing.Handed.Count);
        Assert.Equal(newSignals.Lines, lentSignals.Lines);
        Assert.NotEmpty(lentSignals.Lines);
    }

    [WindowsTheory]
    [MemberData(nameof(Scenes))]
    [SupportedOSPlatform("windows")]
    public void UIA_IsLentOneDiff_EmptiedAfterEachCall_AndRaisesWhatANewOneRaises(string which)
    {
        (List<string> Raised, Forwarding Bridge) Run(Func<IAccessibilityBridge, Forwarding> wrap)
        {
            using var hwnd = TestHwnd.Create();
            WindowsAccessibilityBridge? uia = null;
            var names = new Dictionary<ElementId, int>();
            var raised = new List<string>();
            try
            {
                var bridge = Play(which, session =>
                {
                    var window = session.Window.Window;
                    var router = new AccessibilityActionRouter(window);
                    var host = new UiaHostBridge(window, router);
                    window.SetUiaHostBridge(host);
                    window.SetActionRouter(router);
                    uia = new WindowsAccessibilityBridge(hwnd.Handle, host);
                    uia.Raised = r => raised.Add($"{r.Kind} {r.Id} {(r.Element is { } id
                        ? names.TryGetValue(id, out int n) ? $"e{n}" : $"e{names[id] = names.Count}" : "root")}");
                    return wrap(uia);
                });
                return (raised, bridge);
            }
            finally { uia?.Dispose(); }
        }

        var lent = Run(inner => new Borrowing(inner));
        var fresh = Run(inner => new Forwarding(inner));

        AssertLentAndNot(lent.Bridge, fresh.Bridge);
        Assert.Equal(fresh.Raised, lent.Raised);
    }

    [Fact]
    public void TheUiaAndAtSpiBridgesBorrow_TheHeadlessBridgeDoesNot()
    {
        Assert.True(typeof(ITreeDiffBorrower).IsAssignableFrom(typeof(WindowsAccessibilityBridge)));
        Assert.True(typeof(ITreeDiffBorrower).IsAssignableFrom(typeof(LinuxAccessibilityBridge)));
        Assert.False(typeof(ITreeDiffBorrower).IsAssignableFrom(typeof(HeadlessAccessibilityBridge)));
    }

    /// <summary>The headless bridge keeps every diff it is handed (<see cref="HeadlessAccessibilityBridge.Diffs"/>):
    /// each keystroke's patch is a new one, whole after the next.</summary>
    [Fact]
    public void TheHeadlessBridge_GetsANewDiffForEveryPatch_AndKeepsItWhole()
    {
        using var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        var input = TextInput.New("").Width(Dimension.Px(300));
        session.Window.SetRoot(Flex.Column(gap: 4).Padding(8).Children(TextB.New("Header"), input));
        session.PumpFrames(2);
        session.Window.Click(input);
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(300));
        int trees = session.Window.AccessibilityTreesBuilt;
        bridge.ClearDiffs();

        foreach (string key in new[] { "a", "b", "c" })
        {
            session.Window.TypeText(key);
            session.PumpFrame(TimeSpan.FromMilliseconds(16));
        }

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.Equal(3, bridge.Diffs.Count);
        Assert.Equal(3, bridge.Diffs.Distinct().Count());
        Assert.All(bridge.Diffs, diff => Assert.Contains(diff.PropertyChanged, c => c.PropertyName == AccessibilityProperties.Value));
    }
}
