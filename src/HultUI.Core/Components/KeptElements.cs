using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;

namespace HultUI.Core.Components;

/// <summary>
/// The elements a build made, by key, with what each was made from, so the next build can hand
/// back the live element of each one it would make from the same thing. The reconciler puts a
/// live element a build hands back where it was, untouched: no patch and no layout mark, and in
/// a keyed list nothing marked around it. A cursor key on a page of cards then makes the two
/// cards it moved between, not every card on the page.
///
/// <para>At the start of each build that makes these elements, call <see cref="Begin"/> with the
/// owner's count of builds. For each element: ask <see cref="TryKeep"/> with its key and its
/// state, and add the element it hands back; otherwise make the element and add what
/// <see cref="Made"/> returns. The state is everything the element shows and does: its item, the
/// cursor on it, its picture, a setting every element shows. The rules and the traps are in
/// REACTIVITY.md, "Hand back the elements a key does not change".</para>
///
/// <code>
/// _cards.Begin(++_builds);
/// foreach (var (item, i) in items.Index())
/// {
///     var state = new CardState(item, Cursor: i == cursor, picture);
///     if (_cards.TryKeep(item.Id, state, out var kept)) row.AddChild(kept);
///     else row.AddChild(_cards.Made(item.Id, state, Card(item, i == cursor, picture)));
/// }
/// </code>
/// </summary>
/// <remarks>The elements are held weakly: the window holds what it shows, and an element is handed
/// back only while it is there. Held strongly, the elements of a view another view replaced kept the
/// whole old view alive through their parents, as Meridian's request tree kept its Requests view
/// behind every other view (1,350 KB of 5,770 at 200 requests).</remarks>
/// <typeparam name="TState">What an element shows and does besides what never changes. Equal
/// states must make equal elements: a record struct of the inputs the element is made from.</typeparam>
public sealed class KeptElements<TState> where TState : IEquatable<TState>
{
    private struct Entry
    {
        public TState State;
        public WeakElementRef Live;
        /// <summary>The build that last made or handed back the element.</summary>
        public int Build;
    }

    /// <summary>No <see cref="Begin"/> yet: a virtual list's rows, made outside the owner's builds.</summary>
    private const int Unbuilt = int.MinValue;

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<string> _gone = [];
    private int _build = Unbuilt;
    private int _pruneAt = 64;

    /// <summary>How many elements are remembered: those made or handed back, until enough have
    /// gathered that left the window or are older than the build before.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Starts a build of these elements, numbered by the owner's count of its builds. Only what the
    /// build numbered one less made or handed back can be handed back in this one: an element a build
    /// left out was removed, or patched by position into something else, and an owner build that made
    /// none of these elements (another view on screen) may have done that to all of them.
    /// </summary>
    /// <remarks>Elements made outside the owner's builds, such as a virtual list's rows, which are
    /// made in a frame, skip it: then only the checks of <see cref="TryKeep"/> apply, and each element
    /// needs the place it is handed back into.</remarks>
    public void Begin(int build) => _build = build;

    /// <summary>
    /// The live element made for <paramref name="key"/> with an equal <paramref name="state"/>, to add
    /// in place of a new one, if there is one: made or handed back by the build before (see
    /// <see cref="Begin"/>), still in the window, and with <paramref name="place"/> a child of an
    /// element keyed so.
    /// </summary>
    /// <param name="place">The key of the parent the element is added to, when an element can be in
    /// the window under another parent that this build does not replace: a row of cards a virtual list
    /// makes again while the rows around it stay. Null when every parent of these elements is made by
    /// this build, or when an element may move to another parent (a column of a grid that reflows).</param>
    public bool TryKeep(string key, in TState state, [NotNullWhen(true)] out Element? element, string? place = null)
    {
        element = null;
        ref var entry = ref CollectionsMarshal.GetValueRefOrNullRef(_entries, key);
        if (Unsafe.IsNullRef(ref entry)) return false;
        if (_build != Unbuilt && entry.Build != _build - 1) return false;
        if (!EqualityComparer<TState>.Default.Equals(entry.State, state)) return false;
        if (entry.Live.Current is not { IsHosted: true } live) return false;
        if (place is not null && !string.Equals(live.Parent?.Key, place, StringComparison.Ordinal)) return false;
        entry.Build = _build;
        element = live;
        return true;
    }

    /// <summary>
    /// Remembers <paramref name="element"/>, just made for <paramref name="key"/> from
    /// <paramref name="state"/>, and returns it to add. It takes the key, so the reconcile matches it
    /// with the element it replaces, and a ref that follows it to the live element it is patched into
    /// (a weak one: see the remarks on the class).
    /// </summary>
    public T Made<T>(string key, in TState state, T element) where T : Element
    {
        element.Key = key;
        if (_entries.Count >= _pruneAt && !_entries.ContainsKey(key)) Prune();
        ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(_entries, key, out bool known);
        if (!known) entry.Live = new WeakElementRef();
        entry.State = state;
        entry.Build = _build;
        element.WithRef(entry.Live);
        return element;
    }

    /// <summary>
    /// The element in the window for <paramref name="key"/>, as the latest build made or handed it back,
    /// while it is still there; null otherwise. For a change the owner makes to the live element itself,
    /// with no build, such as moving a ring from one card to another; the owner checks that no build of
    /// its own has run since that one without making these elements. The next build compares with the
    /// state the element was made with, and makes it again if that moved. Change a background or a border
    /// through <see cref="Element.Restyle"/>, or the element's hover style puts back the old one.
    /// </summary>
    public Element? Live(string key) =>
        _entries.TryGetValue(key, out var entry) && entry.Build == _build && entry.Live.Current is { IsHosted: true } live
            ? live : null;

    /// <summary>Forgets every element: for a change to something they all show that their states do
    /// not hold.</summary>
    public void Clear() => _entries.Clear();

    // Let go each time the count doubles, so a list scrolled through a thousand items holds about what
    // is on screen: whatever left the window or is older than the build before. Never what the build
    // in progress made or handed back: an element it made is not in the window until the build is
    // reconciled, and one let go here had no Live after it and was made again by the next build.
    private void Prune()
    {
        foreach (var (key, entry) in _entries)
        {
            if (_build != Unbuilt && entry.Build == _build) continue;
            if ((_build != Unbuilt && entry.Build < _build - 1) || entry.Live.Current is not { IsHosted: true })
                _gone.Add(key);
        }
        foreach (var key in _gone) _entries.Remove(key);
        _gone.Clear();
        _pruneAt = Math.Max(64, _entries.Count * 2);
    }
}

/// <summary>
/// A ref that does not keep its element alive, for <see cref="KeptElements{TState}"/>: the patch repoints
/// it at the live element as it does a <see cref="Ref{T}"/> (<see cref="Element.WithRef(IRef)"/>). The
/// non-generic <see cref="WeakReference"/> underneath, which can be derived from, so it is one object of
/// 24 B, as a <c>Ref&lt;Element&gt;</c> is, and one weak GC handle, made once per key.
/// </summary>
internal sealed class WeakElementRef() : WeakReference(null), IRef
{
    /// <summary>The element, while something else (the window) holds it; null once it is collected.</summary>
    public Element? Current => (Element?)Target;

    void IRef.SetTarget(object target) => Target = target;
}
