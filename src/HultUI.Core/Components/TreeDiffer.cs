using HultUI.Core.Elements;

namespace HultUI.Core.Components;

/// <summary>
/// Reconciles an existing element tree against a newly built tree,
/// patching the existing elements in-place where possible.
/// </summary>
internal static class TreeDiffer
{
    /// <summary>
    /// Reconcile a single child slot: patch <paramref name="existing"/> to match
    /// <paramref name="incoming"/>, or replace it under <paramref name="parent"/>.
    /// </summary>
    public static void Reconcile(Element parent, Element existing, Element incoming)
    {
        if (CanPatch(existing, incoming))
        {
            Patch(existing, incoming);
        }
        else
        {
            // Types differ — swap out
            var index = IndexOf(parent, existing);
            parent.RemoveChild(existing);
            parent.InsertChild(index, incoming);
        }
    }

    private static void Patch(Element existing, Element incoming)
    {
        existing.PatchFrom(incoming);
        // After the full patch the visual props hold the new base, so let the
        // element re-snapshot it and re-apply any active interaction states.
        existing.RefreshInteractionAfterPatch();
        ReconcileChildren(existing, incoming);
        // Child reconciliation may have altered a self-managing element's
        // internally-owned subtree (e.g. Tabs); let it re-assert its live state.
        existing.OnReconciled();
    }

    private static void ReconcileChildren(Element existing, Element incoming)
    {
        // Snapshot both child lists — AddChild reparents which modifies the source collection
        var oldSnapshot = existing.Children.ToArray();
        var newSnapshot = incoming.Children.ToArray();

        if (newSnapshot.Length == 0)
        {
            for (int i = oldSnapshot.Length - 1; i >= 0; i--)
                existing.RemoveChild(oldSnapshot[i]);
            return;
        }

        if (oldSnapshot.Length == 0)
        {
            foreach (var child in newSnapshot)
                existing.AddChild(child);
            return;
        }

        bool hasKeys = HasAnyKeys(oldSnapshot) || HasAnyKeys(newSnapshot);
        if (hasKeys)
            ReconcileKeyed(existing, oldSnapshot, newSnapshot);
        else if (oldSnapshot.Length != newSnapshot.Length)
            ReconcilePrefixSuffix(existing, oldSnapshot, newSnapshot);
        else
            ReconcileIndexed(existing, oldSnapshot, newSnapshot);
    }

    private static void ReconcileIndexed(Element parent, Element[] oldChildren, Element[] newChildren)
    {
        int commonLen = Math.Min(oldChildren.Length, newChildren.Length);

        // Patch common positions
        for (int i = 0; i < commonLen; i++)
        {
            var old = parent.Children[i]; // live list — index is stable because we replace in-place
            var incoming = newChildren[i];

            if (CanPatch(old, incoming))
            {
                Patch(old, incoming);
            }
            else
            {
                parent.RemoveChild(old);
                parent.InsertChild(i, incoming);
            }
        }

        // Remove excess old children
        while (parent.Children.Count > newChildren.Length)
            parent.RemoveChild(parent.Children[^1]);

        // Add new excess children
        for (int i = commonLen; i < newChildren.Length; i++)
            parent.AddChild(newChildren[i]);
    }

    private static void ReconcileKeyed(Element parent, Element[] oldChildren, Element[] newChildren)
    {
        var oldByKey = new Dictionary<string, Element>();
        foreach (var old in oldChildren)
        {
            if (old.Key is not null)
                oldByKey[old.Key] = old;
        }

        var resultChildren = new List<Element>(newChildren.Length);

        foreach (var incoming in newChildren)
        {
            Element? matched = null;

            if (incoming.Key is not null && oldByKey.TryGetValue(incoming.Key, out var byKey) && CanPatch(byKey, incoming))
            {
                matched = byKey;
            }

            if (matched is not null)
            {
                Patch(matched, incoming);
                resultChildren.Add(matched);
            }
            else
            {
                resultChildren.Add(incoming);
            }
        }

        parent.ReplaceChildren(resultChildren.ToArray());
    }

    /// <summary>
    /// When child count changes without keys, match from both ends where types
    /// agree, then replace the unmatched middle. This handles the common pattern
    /// of inserting or removing elements in the middle of a list — stable elements
    /// at the edges survive without explicit keys.
    /// </summary>
    private static void ReconcilePrefixSuffix(Element parent, Element[] oldChildren, Element[] newChildren)
    {
        int minLen = Math.Min(oldChildren.Length, newChildren.Length);

        // Match prefix: same-type pairs from the start
        int prefixLen = 0;
        while (prefixLen < minLen && CanPatch(oldChildren[prefixLen], newChildren[prefixLen]))
            prefixLen++;

        // Match suffix: same-type pairs from the end (don't overlap prefix)
        int suffixLen = 0;
        int maxSuffix = minLen - prefixLen;
        while (suffixLen < maxSuffix &&
               CanPatch(oldChildren[oldChildren.Length - 1 - suffixLen],
                        newChildren[newChildren.Length - 1 - suffixLen]))
            suffixLen++;

        // Patch matched prefix in-place
        for (int i = 0; i < prefixLen; i++)
            Patch(parent.Children[i], newChildren[i]);

        // Patch matched suffix in-place (still at original indices)
        for (int i = 0; i < suffixLen; i++)
        {
            int oldIdx = oldChildren.Length - suffixLen + i;
            int newIdx = newChildren.Length - suffixLen + i;
            Patch(parent.Children[oldIdx], newChildren[newIdx]);
        }

        // Remove old middle (reverse order to keep indices stable)
        int oldMiddleEnd = oldChildren.Length - suffixLen;
        for (int i = oldMiddleEnd - 1; i >= prefixLen; i--)
            parent.RemoveChild(parent.Children[i]);

        // Insert new middle
        int newMiddleEnd = newChildren.Length - suffixLen;
        for (int i = prefixLen; i < newMiddleEnd; i++)
            parent.InsertChild(i, newChildren[i]);
    }

    private static bool CanPatch(Element existing, Element incoming) =>
        existing.GetType() == incoming.GetType();

    private static bool HasAnyKeys(Element[] children)
    {
        foreach (var child in children)
        {
            if (child.Key is not null)
                return true;
        }
        return false;
    }

    private static int IndexOf(Element parent, Element child)
    {
        for (int i = 0; i < parent.Children.Count; i++)
        {
            if (ReferenceEquals(parent.Children[i], child))
                return i;
        }
        return parent.Children.Count;
    }
}
