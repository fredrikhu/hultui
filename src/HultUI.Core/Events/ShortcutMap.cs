namespace HultUI.Core.Events;

/// <summary>
/// The one table of the keys an app answers to: what each shortcut does, which chords it has
/// now, and what it had to begin with. Key handlers match against it, and everything that names a
/// key — an options page, a legend, a tooltip, a menu's shortcut column — reads its text from it,
/// so a key cannot be shown one way and bound another.
/// </summary>
/// <remarks>
/// <para>The table only answers questions; the app still decides when to ask. A handler that
/// walks its overlays and views in priority order keeps doing so, and asks
/// <see cref="Matches(string, KeyDownEvent)"/> where it used to compare <c>e.Key</c> — which
/// brings the exact modifier matching of <see cref="KeyChord"/> with it.</para>
/// <para><b>Groups are where a key means one thing</b> — a view, a panel, the whole window. The
/// same chord may mean different things in different groups; within one it may not, which is
/// what <see cref="Rebind"/> keeps true. A shortcut that is not <see cref="Shortcut.Rebindable"/>
/// still counts: a person cannot rebind a command onto a key the app has taken for itself.</para>
/// <para>Order is display order.</para>
/// </remarks>
public sealed class ShortcutMap
{
    private readonly List<Shortcut> _all = [];
    private readonly Dictionary<string, Shortcut> _byId = new(StringComparer.Ordinal);

    /// <summary>Every shortcut, in the order they were added.</summary>
    public IReadOnlyList<Shortcut> All => _all;

    /// <summary>The groups, in the order they first appear.</summary>
    public IEnumerable<string> Groups => _all.Select(s => s.Group).Distinct();

    /// <summary>The shortcut with this id.</summary>
    public Shortcut this[string id] =>
        _byId.TryGetValue(id, out var s) ? s : throw new KeyNotFoundException($"No shortcut '{id}'.");

    /// <summary>Adds a shortcut. Returns it, so its options can follow:
    /// <c>map.Add("mr.approve", "Merge requests", "Approve or revoke", new(KeyCode.A)).Legend("approve")</c>.</summary>
    public Shortcut Add(string id, string group, string title, params KeyChord[] keys)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (_byId.ContainsKey(id)) throw new ArgumentException($"Shortcut '{id}' is already in the map.", nameof(id));
        var shortcut = new Shortcut(id, group, title, keys);
        _all.Add(shortcut);
        _byId[id] = shortcut;
        return shortcut;
    }

    /// <summary>Whether <paramref name="e"/> is one of the shortcut's chords now.</summary>
    public bool Matches(string id, KeyDownEvent e) => this[id].Matches(e);

    /// <summary>The first shortcut, in table order, that <paramref name="e"/> is — within
    /// <paramref name="group"/> when one is given. Null when it is none of them.</summary>
    public Shortcut? Find(KeyDownEvent e, string? group = null)
    {
        foreach (var s in _all)
            if ((group is null || s.Group == group) && s.Matches(e))
                return s;
        return null;
    }

    /// <summary>The shortcut's keys as text, for a tooltip or a menu's shortcut column:
    /// <c>Ctrl+P · Ctrl+K</c>. Empty when it has none.</summary>
    public string Keys(string id) => this[id].KeysText;

    /// <summary>A panel's legend row: the group's shortcuts that carry a legend word, as key text
    /// and word, in table order.</summary>
    public IEnumerable<(string Keys, string Word)> Legend(string group) =>
        _all.Where(s => s.Group == group && s.LegendWord is not null).Select(s => (s.KeysText, s.LegendWord!));

    // ──── Rebinding ────

    /// <summary>
    /// The shortcut in <paramref name="shortcut"/>'s group that holds <paramref name="chord"/>,
    /// other than <paramref name="shortcut"/> itself; null when the chord is free there.
    /// </summary>
    public Shortcut? Holder(KeyChord chord, Shortcut shortcut) =>
        _all.FirstOrDefault(s => s != shortcut && s.Group == shortcut.Group && s.Keys.Contains(chord));

    /// <summary>
    /// Binds a rebindable shortcut to <paramref name="chord"/> alone, and takes the chord from a
    /// rebindable shortcut in the same group that had it: that one goes back to its own default,
    /// or to nothing when its default is the chord taken. Refused — false, nothing changed —
    /// when the chord belongs to a shortcut that cannot be rebound: it would never fire, or it
    /// would silently replace what the app does on that key.
    /// </summary>
    public bool Rebind(string id, KeyChord chord)
    {
        var shortcut = this[id];
        if (!shortcut.Rebindable) throw new InvalidOperationException($"Shortcut '{id}' cannot be rebound.");
        if (Holder(chord, shortcut) is { Rebindable: false }) return false;

        foreach (var other in _all)
        {
            if (other == shortcut || other.Group != shortcut.Group || !other.Keys.Contains(chord)) continue;
            IReadOnlyList<KeyChord> back = other.Defaults.Contains(chord) ? [] : other.Defaults;
            // Its default may have been taken in the meantime too; then it is left with none
            // rather than given a key that means two things.
            if (back.Any(c => _all.Any(x => x != other && x != shortcut && x.Group == other.Group && x.Keys.Contains(c))))
                back = [];
            other.Keys = back;
        }
        shortcut.Keys = [chord];
        return true;
    }

    /// <summary>Puts every shortcut back to its defaults.</summary>
    public void Reset()
    {
        foreach (var s in _all) s.Keys = s.Defaults;
    }

    /// <summary>
    /// The shortcuts that differ from their defaults, for a settings file: id to chord text, or
    /// to "" for a shortcut left with no key. Only real changes are written, so a default changed
    /// in a later release reaches everyone who had not chosen otherwise.
    /// </summary>
    public Dictionary<string, string> SaveOverrides()
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in _all)
            if (s.Rebindable && !s.Keys.SequenceEqual(s.Defaults))
                overrides[s.Id] = s.Keys.Count == 0 ? "" : s.Keys[0].ToString();
        return overrides;
    }

    /// <summary>
    /// Applies what <see cref="SaveOverrides"/> wrote, over the defaults. Ids no longer in the
    /// map and chords that do not parse are skipped; an override that lands on a chord another
    /// shortcut in its group already holds is dropped, so a hand-edited file cannot make one key
    /// mean two things.
    /// </summary>
    public void LoadOverrides(IReadOnlyDictionary<string, string>? overrides)
    {
        Reset();
        if (overrides is null) return;
        foreach (var (id, text) in overrides)
        {
            if (!_byId.TryGetValue(id, out var s) || !s.Rebindable) continue;
            if (text.Length == 0) { s.Keys = []; continue; }
            if (KeyChord.Parse(text) is not { } chord) continue;
            s.Keys = [chord];
        }
        foreach (var s in _all)
            if (s.Rebindable && s.Keys.Count == 1 && Holder(s.Keys[0], s) is { } other
                && (!other.Rebindable || _all.IndexOf(other) < _all.IndexOf(s)))
                s.Keys = s.Defaults.Contains(s.Keys[0]) ? [] : s.Defaults;
    }

    /// <summary>A copy to edit — an options page's draft — that leaves this map alone until its
    /// overrides are loaded back.</summary>
    public ShortcutMap Clone()
    {
        var copy = new ShortcutMap();
        foreach (var s in _all)
        {
            var c = copy.Add(s.Id, s.Group, s.Title, [.. s.Defaults]);
            c.Rebindable = s.Rebindable;
            c.LegendWord = s.LegendWord;
            c.ShownAs = s.ShownAs;
            c.Keys = s.Keys;
        }
        return copy;
    }
}

/// <summary>One entry in a <see cref="ShortcutMap"/>.</summary>
public sealed class Shortcut
{
    internal Shortcut(string id, string group, string title, KeyChord[] keys)
    {
        Id = id;
        Group = group;
        Title = title;
        Defaults = keys;
        Keys = keys;
    }

    /// <summary>The name code and settings know it by.</summary>
    public string Id { get; }

    /// <summary>Where the keys mean this: a view, a panel, the window.</summary>
    public string Group { get; }

    /// <summary>What it does, as an options page lists it.</summary>
    public string Title { get; }

    /// <summary>The chords it comes with.</summary>
    public IReadOnlyList<KeyChord> Defaults { get; }

    /// <summary>The chords it has now: the defaults, or what a person bound instead.</summary>
    public IReadOnlyList<KeyChord> Keys { get; internal set; }

    /// <summary>Whether a person may bind it to another key. Off by default: an app's key is its
    /// own until it offers it.</summary>
    public bool Rebindable { get; internal set; }

    /// <summary>The word a panel's legend shows beside the key; null keeps it out of legends.</summary>
    public string? LegendWord { get; internal set; }

    /// <summary>Text shown in place of the chords, for a shortcut a list of chords does not say
    /// well: a range (<c>1 – 7</c>), a set of arrows. The chords still do the matching.</summary>
    public string? ShownAs { get; internal set; }

    /// <summary>The keys as shown: <see cref="ShownAs"/>, else the chords joined with " · ".</summary>
    public string KeysText => ShownAs ?? string.Join(" · ", Keys);

    /// <summary>Whether <paramref name="e"/> is one of its chords now.</summary>
    public bool Matches(KeyDownEvent e)
    {
        foreach (var chord in Keys)
            if (chord.Matches(e)) return true;
        return false;
    }

    /// <summary>Lets a person rebind it.</summary>
    public Shortcut CanRebind() { Rebindable = true; return this; }

    /// <summary>Puts it in its group's legend under <paramref name="word"/>.</summary>
    public Shortcut Legend(string word) { LegendWord = word; return this; }

    /// <summary>Shows <paramref name="text"/> instead of the chords.</summary>
    public Shortcut Shown(string text) { ShownAs = text; return this; }
}
