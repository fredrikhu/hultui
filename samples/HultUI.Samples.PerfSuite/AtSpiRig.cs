using HultUI.Core.Accessibility;
using HultUI.Headless;
using HultUI.Platform.Accessibility.Linux;

namespace PerfSuite;

/// <summary>What a scenario's check reads of a real bridge behind the window: UIA's or AT-SPI's.</summary>
interface IBridgeRig
{
    /// <summary>The bridge, as a check's message names it.</summary>
    string Name { get; }

    /// <summary>Changes the bridge has applied, diffs and in-place patches.</summary>
    int Applies { get; }

    /// <summary>Focus moves the bridge was told of.</summary>
    int FocusMoves { get; }
}

/// <summary>
/// The real AT-SPI bridge, in process, its signals counted rather than sent: no bus is opened, so
/// it runs on any desk, as its own tests do. For the rows that time what a Linux screen reader adds
/// on top of the tree build and diff: the bridge's objects, kept or made again on each apply, and
/// the signals it decides to send (not their D-Bus marshalling). The window disposes it with the
/// session. Active whether or not a client listens. It keeps nothing of a diff past the call, as
/// the bridge does, so the window lends it its diff for patches as it lends the bridge
/// (<see cref="ITreeDiffBorrower"/>).
/// </summary>
#pragma warning disable CA1416 // Linux's bridge, with a signal sink and no bus: any OS
sealed class AtSpiRig : IBridgeRig, IAccessibilityBridge, ITreeDiffBorrower, IDisposable
{
    private readonly CountingSignals _signals = new();
    private readonly LinuxAccessibilityBridge _bridge;

    private AtSpiRig() => _bridge = new LinuxAccessibilityBridge(_signals);

    public string Name => "AT-SPI";
    public int Applies { get; private set; }
    public int FocusMoves { get; private set; }

    /// <summary>Signals the bridge has sent.</summary>
    public int Signals => _signals.Count;

    public static AtSpiRig Install(HeadlessWindow window) => window.InstallAccessibilityBridge(new AtSpiRig());

    public bool IsActive => _bridge.IsActive;

    public void ApplyChanges(TreeDiff diff, AccessibilityNode root)
    {
        _bridge.ApplyChanges(diff, root);
        Applies++;
    }

    public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root)
    {
        _bridge.ApplyPatchedProperties(patch, root);
        Applies++;
    }

    public void NotifyFocusChanged(AccessibilityNode? focused)
    {
        _bridge.NotifyFocusChanged(focused);
        FocusMoves++;
    }

    public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous)
    {
        _bridge.NotifyFocusChanged(focused, previous);
        FocusMoves++;
    }

    public void Announce(AccessibilityNode? source, string text, AnnouncementPoliteness politeness)
        => _bridge.Announce(source, text, politeness);

    public void Dispose() => _bridge.Dispose();

    /// <summary>Counts what the bridge sends; a cache-add hands over its object, read by nothing.</summary>
    private sealed class CountingSignals : IAtSpiSignals
    {
        public int Count;
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) => Count++;
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) => Count++;
        public void EmitValueChanged(string path, double value) => Count++;
        public void EmitRoleChange(string path, uint role) => Count++;
        public void EmitStateChanged(string path, string state, bool active) => Count++;
        public void EmitTextChanged(string path, bool insert, int offset, string text) => Count++;
        public void EmitTextCaretMoved(string path, int offset) => Count++;
        public void EmitTextSelectionChanged(string path) => Count++;
        public void EmitAnnouncement(string path, string text, int politeness) => Count++;
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) => Count++;
        public void EmitCacheRemove(string path) => Count++;
    }
}
