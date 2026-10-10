// The AT-SPI bridge is Linux's, but with its signals recorded instead of sent and its status
// switched by the test it runs anywhere: nothing here reaches a bus.
#pragma warning disable CA1416
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Linux;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// On Linux the AT-SPI bus is reachable in every desktop session, and the bridge was active
/// whenever it registered: with no screen reader running, every change a reader could see
/// rebuilt the tree and went to AT-SPI (a keystroke in a 603-node form 385 KB, against 3.2 KB
/// with no bridge). The bridge is now active only while <c>org.a11y.Status</c> says assistive
/// technology is on, as Qt's is, or while <see cref="AccessibilityClients.Force"/> says so. The
/// window is given the bridge itself, as the factory gives it, so its own polling decides.
/// </summary>
public class A11yAtSpiStatusTests
{
    private sealed class Status(bool on) : IAtSpiStatus
    {
        private Action<bool>? _changed;
        public bool On { get; private set; } = on;
        public bool ReadStatus() => On;
        public void WatchStatus(Action<bool> changed) => _changed += changed;
        public void Set(bool on) { On = on; _changed?.Invoke(on); }
    }

    private sealed class Signals : IAtSpiSignals
    {
        public readonly List<string> Lines = [];
        private static string Short(string path) =>
            path.StartsWith(AtSpiConstants.AccessiblePathPrefix, StringComparison.Ordinal)
                ? path[AtSpiConstants.AccessiblePathPrefix.Length..] : "root";
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) =>
            Lines.Add($"children-changed:{(isAdd ? "add" : "remove")} {Short(parentPath)} {Short(childPath)}");
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) => Lines.Add($"property-change:{atSpiProperty} {Short(path)} {newValue}");
        public void EmitRoleChange(string path, uint role) => Lines.Add($"property-change:accessible-role {Short(path)}");
        public void EmitStateChanged(string path, string state, bool active) => Lines.Add($"state-changed:{state} {Short(path)} {(active ? 1 : 0)}");
        public void EmitTextChanged(string path, bool insert, int offset, string text) => Lines.Add($"text-changed {Short(path)}");
        public void EmitTextCaretMoved(string path, int offset) => Lines.Add($"text-caret-moved {Short(path)}");
        public void EmitTextSelectionChanged(string path) => Lines.Add($"text-selection-changed {Short(path)}");
        public void EmitAnnouncement(string path, string text, int politeness) => Lines.Add($"announcement {Short(path)}");
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) => Lines.Add($"cache-add {Short(node.Path)}");
        public void EmitCacheRemove(string path) => Lines.Add($"cache-remove {Short(path)}");
    }

    private sealed class Rig : IDisposable
    {
        public HeadlessSession Session { get; } = HeadlessSession.Create(800, 600);
        public readonly Signals Signals = new();
        public readonly Status Status;
        public LinuxAccessibilityBridge Bridge { get; }
        public readonly TextInputElement Input = TextInput.New("").Width(Dimension.Px(300));
        public readonly CheckboxElement Box = Checkbox.New(false, "Remember");
        public readonly BoxElement Rows = Flex.Column().FlexShrink(0);

        public Rig(bool on)
        {
            Status = new Status(on);
            Bridge = new LinuxAccessibilityBridge(Signals, status: Status);
            Session.Window.InstallAccessibilityBridge(Bridge);
            var rows = new Element[100];
            for (int i = 0; i < rows.Length; i++) rows[i] = TextB.New($"row {i}");
            Rows.ReplaceChildren(rows);
            Session.Window.SetRoot(Flex.Column(gap: 4).Padding(8).Children(Input, Box, Rows));
            Session.PumpFrames(2);
            Session.Window.Click(Input);
            Session.PumpFrame();
        }

        /// <summary>The objects of the tree the window would build now.</summary>
        public int Nodes => Count(Session.Window.Window.BuildAccessibilityTree()!);

        private static int Count(AccessibilityNode node)
        {
            int n = 1;
            foreach (var child in node.Children) n += Count(child);
            return n;
        }

        public void Edit()
        {
            Session.Window.TypeText("ab");
            Box.IsChecked = !Box.IsChecked;
            Session.PumpFrame();
        }

        public void Dispose()
        {
            Bridge.Dispose();
            Session.Dispose();
            AccessibilityClients.Report(false);
        }
    }

    [Fact]
    public void StatusOff_NothingIsBuiltOrSent()
    {
        AccessibilityClients.Force = null;
        using var rig = new Rig(on: false);
        rig.Edit();
        rig.Session.PumpFrames(3);

        Assert.Equal(0, rig.Bridge.DebugApplyChangesCalls);
        Assert.Empty(rig.Signals.Lines);
        Assert.Equal("ab", rig.Input.Text);
    }

    [Fact]
    public void StatusOn_PublishesTheWholeTree_ThenItsChanges_AndOffAgain_Stops()
    {
        AccessibilityClients.Force = null;
        using var rig = new Rig(on: false);
        rig.Edit();

        rig.Status.Set(true);
        rig.Session.PumpFrame();
        Assert.Equal(1, rig.Bridge.DebugApplyChangesCalls);
        Assert.All(rig.Signals.Lines, l => Assert.StartsWith("cache-add ", l));
        Assert.Equal(rig.Nodes, rig.Signals.Lines.Count);
        Assert.Contains($"cache-add {rig.Input.Id.Value}", rig.Signals.Lines);

        // Then what changes, as before.
        rig.Signals.Lines.Clear();
        rig.Edit();
        Assert.Contains($"property-change:accessible-value {rig.Input.Id.Value} abab", rig.Signals.Lines);
        Assert.Contains($"state-changed:checked {rig.Box.Id.Value} 0", rig.Signals.Lines);

        // Off: nothing more is built or sent.
        rig.Status.Set(false);
        rig.Session.PumpFrame();
        int applied = rig.Bridge.DebugApplyChangesCalls;
        rig.Signals.Lines.Clear();
        rig.Edit();
        rig.Session.PumpFrames(3);
        Assert.Equal(applied, rig.Bridge.DebugApplyChangesCalls);
        Assert.Empty(rig.Signals.Lines);
    }

    [Fact]
    public void OnAgain_PublishesTheTreeAfresh_AndRemovesWhatWentWhileOff()
    {
        AccessibilityClients.Force = null;
        using var rig = new Rig(on: true);
        var gone = rig.Rows.Children[^1];
        rig.Status.Set(false);
        rig.Session.PumpFrame();
        rig.Rows.RemoveChild(gone);
        rig.Edit();
        rig.Signals.Lines.Clear();

        rig.Status.Set(true);
        rig.Session.PumpFrame();

        Assert.Equal($"cache-remove {gone.Id.Value}", rig.Signals.Lines[0]);
        Assert.Equal(rig.Nodes, rig.Signals.Lines.Count(l => l.StartsWith("cache-add ", StringComparison.Ordinal)));
        Assert.Equal(rig.Nodes + 1, rig.Signals.Lines.Count);
        var input = ((IAtSpiTree)rig.Bridge).Find(AtSpiConstants.AccessiblePathPrefix + rig.Input.Id.Value)!;
        Assert.Equal("ab", input.Value);   // typed while off: the object has it now
    }

    [Fact]
    public void TheOverride_IsAlwaysOn_ForAClientThatNeverSetsTheStatus()
    {
        AccessibilityClients.Force = null;
        using var rig = new Rig(on: false);
        try
        {
            AccessibilityClients.Force = true;
            rig.Session.PumpFrame();
            Assert.Equal(rig.Nodes, rig.Signals.Lines.Count);

            rig.Signals.Lines.Clear();
            rig.Edit();
            Assert.Contains($"property-change:accessible-value {rig.Input.Id.Value} ab", rig.Signals.Lines);

            AccessibilityClients.Force = null;
            rig.Session.PumpFrame();
            rig.Signals.Lines.Clear();
            rig.Edit();
            Assert.Empty(rig.Signals.Lines);
        }
        finally { AccessibilityClients.Force = null; }
    }
}
