using HultUI.Core.Accessibility;

namespace HultUI.Headless;

/// <summary>
/// An always-listening accessibility bridge that records what the framework
/// would send to a real platform bridge (UIA / AT-SPI). Installing it makes
/// the window run its true per-frame a11y build/diff path — the code real
/// assistive technologies depend on — so tests can assert on the emitted
/// diffs without any OS accessibility stack. Install with
/// <see cref="HeadlessWindow.InstallAccessibilityBridge"/>.
/// </summary>
public sealed class HeadlessAccessibilityBridge : IAccessibilityBridge
{
    private readonly List<TreeDiff> _diffs = [];

    public bool IsActive => true;

    /// <summary>The most recently applied full-tree snapshot.</summary>
    public AccessibilityNode? CurrentTree { get; private set; }

    /// <summary>The node most recently reported as focused, or null.</summary>
    public AccessibilityNode? FocusedNode { get; private set; }

    /// <summary>Every diff applied so far, oldest first.</summary>
    public IReadOnlyList<TreeDiff> Diffs => _diffs;

    public void ApplyChanges(TreeDiff diff, AccessibilityNode root)
    {
        _diffs.Add(diff);
        CurrentTree = root;
    }

    public void NotifyFocusChanged(AccessibilityNode? focused) => FocusedNode = focused;

    public void ClearDiffs() => _diffs.Clear();
}
