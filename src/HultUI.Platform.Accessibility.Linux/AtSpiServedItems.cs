using System.Collections.Concurrent;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;

namespace HultUI.Platform.Accessibility.Linux;

/// <summary>
/// The items of a list whose items are served on demand (over 2,000 of them:
/// <see cref="AccessibilityNode.IsVirtualItemContainer"/>), which the tree has no node for. The list's
/// object answers ChildCount with their number, and an item's object is made when a client first asks
/// for it (the list's GetChildAtIndex, or the item's path) and kept while the list is served, so the
/// same item answers from the same object. What an item answers is read when it is asked: its name
/// and whether it is checked from the list (as UI Automation's on-demand items read them), whether it
/// is selected, how many items there are and where one is from the list's node
/// (<see cref="AccessibilityNode.Served"/>), as their events told them. Until plan 17's round 5 the
/// list's object said it had no children, and no item could be reached.
/// </summary>
/// <remarks>
/// Kept across snapshots while the list stays served on demand, so an item object a client holds stays
/// the one it holds; the bridge points it at the list's object in each snapshot (<see cref="List"/>).
/// It remembers which items a client may hold — the objects it made, the items named in a signal, all
/// of them once a client took the whole list (GetChildren) — so when items go with the list's end the
/// bridge tells the client those are gone (a cache-remove, which libatspi disposes them on), and only
/// those. A list of 3,000 items is one object until a client walks into it, and one more for each item
/// it reaches.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class AtSpiServedItems
{
    private readonly uint _slot;
    private readonly Func<int, AtSpiAccessible> _make;
    // Made on the bus thread when a client asks; let go on the UI thread when the item goes.
    private readonly ConcurrentDictionary<int, AtSpiAccessible> _made = new();
    // Items named in a signal and not made (yet): a client may hold them too. UI thread.
    private readonly ConcurrentDictionary<int, byte> _named = new();
    // A client took every item's path at once (GetChildren).
    private volatile bool _allHandedOut;
    private AtSpiAccessible _list = null!;

    /// <param name="listId">The list's element.</param>
    /// <param name="slot">The list's owner slot, which its items' ids pack (<see cref="VirtualItemId"/>).</param>
    public AtSpiServedItems(ElementId listId, uint slot)
    {
        ListId = listId;
        _slot = slot;
        _make = Make;
    }

    public ElementId ListId { get; }

    /// <summary>The list's object in the published snapshot: set on the UI thread as a snapshot is
    /// made, read on the bus thread.</summary>
    public AtSpiAccessible List
    {
        get => Volatile.Read(ref _list);
        set => Volatile.Write(ref _list, value);
    }

    /// <summary>How many items the list has, as its node carries them.</summary>
    public int Count => List.Node?.Served.Count ?? 0;

    public ElementId IdOf(int index) => VirtualItemId.ForSlot(_slot, index);

    public string PathOf(int index) => AtSpiAccessible.PathFor(IdOf(index));

    /// <summary>Item <paramref name="index"/>'s object, made the first time a client asks; null past
    /// the list's end. Any thread.</summary>
    public AtSpiAccessible? Item(int index) => (uint)index < (uint)Count ? _made.GetOrAdd(index, _make) : null;

    /// <summary>A client was handed every item's path at once (GetChildren). Bus thread.</summary>
    public void HandedOutAll() => _allHandedOut = true;

    /// <summary>Item <paramref name="index"/> was named in a signal: a client may hold it now. UI thread.</summary>
    public void Named(int index)
    {
        if (!_made.ContainsKey(index)) _named.TryAdd(index, 0);
    }

    /// <summary>
    /// The items from <paramref name="count"/> on went with the list's end (it had
    /// <paramref name="before"/>): the paths of those a client may hold, ascending, added to
    /// <paramref name="gone"/>, and their objects let go. UI thread.
    /// </summary>
    public void TakeGone(int count, int before, List<string> gone)
    {
        int start = gone.Count;
        if (_allHandedOut)
            for (int i = count; i < before; i++) gone.Add(PathOf(i));
        foreach (var index in _made.Keys)
            if (index >= count && _made.TryRemove(index, out _) && !_allHandedOut) gone.Add(PathOf(index));
        foreach (var index in _named.Keys)
            if (index >= count && _named.TryRemove(index, out _) && !_allHandedOut) gone.Add(PathOf(index));
        if (!_allHandedOut && gone.Count - start > 1)
        {
            // In item order, each once: the ids of one list's items sort as their indices.
            gone.Sort(start, gone.Count - start, ItemOrder.Instance);
            int at = start + 1;
            for (int i = start + 1; i < gone.Count; i++)
                if (gone[i] != gone[at - 1]) gone[at++] = gone[i];
            gone.RemoveRange(at, gone.Count - at);
        }
    }

    /// <summary>The paths of one list's items in item order: the same length, so ordinal order is
    /// numeric order only when their digits are as many; compared by length first.</summary>
    private sealed class ItemOrder : IComparer<string>
    {
        public static readonly ItemOrder Instance = new();

        public int Compare(string? a, string? b) =>
            a!.Length != b!.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
    }

    private AtSpiAccessible Make(int index)
    {
        _named.TryRemove(index, out _);
        var list = List;
        return new AtSpiAccessible
        {
            ElementId = IdOf(index),
            Path = PathOf(index),
            IsRoot = false,
            Role = AtSpiConstants.MapRole(AccessibilityRole.ListItem),
            ItemOf = this,
            ItemIndex = index,
            Selectable = true,
            Scale = list.Scale,
            ParentPath = list.Path,
            IndexInParent = index,
            Action = AtSpiActionKind.Click,
        };
    }

    // ── What an item's object answers, read when a client asks (bus thread) ──

    /// <summary>The item's name: its list's label for it, read now, as UI Automation reads it.</summary>
    public string LabelOf(int index)
    {
        try { return List.Node?.ItemSource?.GetItemLabel(index) ?? ""; }
        catch { return ""; }
    }

    /// <summary>Checked or not, for an item that is a choice; null for one that is not.</summary>
    public bool? ToggledOf(int index)
    {
        try { return List.Node?.ItemSource?.ItemIsToggled(index); }
        catch { return null; }
    }

    /// <summary>Selected, as the list's node carries it: what the selection's events told.</summary>
    public bool SelectedOf(int index) => List.Node?.Served.IsSelected(index) == true;

    /// <summary>Out of the list's viewport, at the scroll the list's node carries.</summary>
    public bool OffscreenOf(int index) =>
        List.Node is not { } node || !node.TryGetServedItemBox(index, out _, out bool offscreen) || offscreen;

    /// <summary>The item's box in window-relative device pixels, as an object of a node answers it.</summary>
    public (int X, int Y, int Width, int Height) BoxOf(int index)
    {
        var list = List;
        if (list.Node is not { } node || !node.TryGetServedItemBox(index, out var rect, out _)) return default;
        float scale = list.Scale;
        return ((int)MathF.Round(rect.X * scale), (int)MathF.Round(rect.Y * scale),
            (int)MathF.Round(rect.Width * scale), (int)MathF.Round(rect.Height * scale));
    }
}
