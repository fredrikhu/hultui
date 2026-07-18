using System.Text;
using HultUI.Core.Accessibility;

namespace HultUI.Headless;

// ──── Query API ────
// Queries read a fresh accessibility-tree snapshot on every call — the same
// tree a screen reader walks — so what a test can find is exactly what
// assistive technology can find. An element without a name or role here is
// invisible to both, which makes these queries a soft accessibility check.
public sealed partial class HeadlessWindow
{
    /// <summary>A fresh accessibility-tree snapshot of the current content.</summary>
    public AccessibilityNode AccessibilityTree
        => Window.BuildAccessibilityTree()
           ?? throw new InvalidOperationException("No root element set.");

    /// <summary>First node (depth-first) matching the predicate, or null.</summary>
    public HeadlessElement? Find(Func<AccessibilityNode, bool> predicate)
    {
        foreach (var node in Walk(AccessibilityTree))
        {
            if (predicate(node) && Resolve(node) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>All nodes (depth-first) matching the predicate.</summary>
    public IReadOnlyList<HeadlessElement> FindAll(Func<AccessibilityNode, bool> predicate)
    {
        var results = new List<HeadlessElement>();
        foreach (var node in Walk(AccessibilityTree))
        {
            if (predicate(node) && Resolve(node) is { } found)
                results.Add(found);
        }
        return results;
    }

    /// <summary>First node whose Name or Value equals <paramref name="text"/> (ordinal), or null.</summary>
    public HeadlessElement? FindByText(string text)
        => Find(n => n.Name == text || n.Value == text);

    /// <summary>
    /// Like <see cref="FindByText"/>, but throws with a dump of the full
    /// accessibility tree when nothing matches — the failure message shows
    /// what was actually on screen.
    /// </summary>
    public HeadlessElement GetByText(string text)
        => FindByText(text)
           ?? throw new InvalidOperationException(
               $"No accessible element with text \"{text}\". Tree:\n{DumpAccessibilityTree()}");

    /// <summary>First node with the given role (optionally also matching name), or null.</summary>
    public HeadlessElement? FindByRole(AccessibilityRole role, string? name = null)
        => Find(n => n.Role == role && (name is null || n.Name == name));

    /// <summary>First node with the given AutomationId, or null.</summary>
    public HeadlessElement? FindByAutomationId(string automationId)
        => Find(n => n.AutomationId == automationId);

    /// <summary>
    /// Human-readable dump of the accessibility tree, for debugging failed
    /// queries: one node per line, indented by depth.
    /// </summary>
    public string DumpAccessibilityTree()
    {
        var sb = new StringBuilder();
        Dump(AccessibilityTree, 0, sb);
        return sb.ToString();
    }

    private HeadlessElement? Resolve(AccessibilityNode node)
        => Window.FindElement(node.ElementId) is { } element
            ? new HeadlessElement(this, node, element)
            : null;

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    private static void Dump(AccessibilityNode node, int depth, StringBuilder sb)
    {
        sb.Append(' ', depth * 2).Append(node.Role);
        if (node.Name is not null) sb.Append(" \"").Append(node.Name).Append('"');
        if (node.Value is not null) sb.Append(" value=\"").Append(node.Value).Append('"');
        if (node.AutomationId is not null) sb.Append(" id=").Append(node.AutomationId);
        if (node.IsToggled is { } t) sb.Append(t ? " [checked]" : " [unchecked]");
        if (node.IsFocused) sb.Append(" [focused]");
        if (node.IsVirtualItemContainer) sb.Append(" [virtualized]");
        sb.AppendLine();
        foreach (var child in node.Children)
            Dump(child, depth + 1, sb);
    }
}
