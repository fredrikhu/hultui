using HultUI.Platform.Accessibility.Linux;

namespace HultUI.Platform.Accessibility.Linux.Tests;

/// <summary>
/// Stands in for the a11y bus: every signal the bridge sends, one line each, object paths
/// shortened to the element id ("root" for the application).
/// </summary>
internal sealed class SignalRecorder : IAtSpiSignals
{
    public List<string> Lines { get; } = [];

    public static string Short(string path) =>
        path.StartsWith(AtSpiConstants.AccessiblePathPrefix, StringComparison.Ordinal)
            ? path[AtSpiConstants.AccessiblePathPrefix.Length..]
            : path == AtSpiConstants.RootPath ? "root" : path;

    public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) =>
        Lines.Add($"children-changed:{(isAdd ? "add" : "remove")} {Short(parentPath)} {Short(childPath)} {index}");
    public void EmitPropertyChange(string path, string atSpiProperty, string newValue) =>
        Lines.Add($"property-change:{atSpiProperty} {Short(path)} {newValue}");
    public void EmitValueChanged(string path, double value) =>
        Lines.Add(FormattableString.Invariant($"property-change:accessible-value {Short(path)} {value}"));
    public void EmitRoleChange(string path, uint role) => Lines.Add($"property-change:accessible-role {Short(path)} {role}");
    public void EmitStateChanged(string path, string state, bool active) =>
        Lines.Add($"state-changed:{state} {Short(path)} {(active ? 1 : 0)}");
    public void EmitTextChanged(string path, bool insert, int offset, string text) =>
        Lines.Add($"text-changed:{(insert ? "insert" : "delete")} {Short(path)} {offset} {text}");
    public void EmitTextCaretMoved(string path, int offset) => Lines.Add($"text-caret-moved {Short(path)} {offset}");
    public void EmitTextSelectionChanged(string path) => Lines.Add($"text-selection-changed {Short(path)}");
    public void EmitAnnouncement(string path, string text, int politeness) => Lines.Add($"announcement {Short(path)} {politeness} {text}");
    public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) =>
        Lines.Add($"cache-add {Short(node.Path)} parent={Short(node.ParentPath)} index={node.IndexInParent}");
    public void EmitCacheRemove(string path) => Lines.Add($"cache-remove {Short(path)}");
}
