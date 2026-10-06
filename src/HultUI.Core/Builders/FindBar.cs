using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Reactivity;

namespace HultUI.Core.Builders;

/// <summary>The four glyphs a find bar shows, for an app that draws them from its own icon set
/// (<see cref="FindBar.Icon"/>).</summary>
public enum FindBarIcon
{
    Search,
    Previous,
    Next,
    Close,
}

/// <summary>Text selected in the body a find bar searches, and where it starts in the search's own
/// coordinates — what Ctrl+F seeds the query and the current match from.</summary>
public readonly record struct FindSelection(string Text, int Line, int Lane, int Start);

/// <summary>
/// The find-in-text bar: a field over a <see cref="TextSearch"/>, its match count, the match-case
/// and regular-expression switches, and previous / next / close — plus the keys and the
/// after-build work that make it behave. App state, like <see cref="ToastHost"/>: the owner keeps
/// one per searchable view and
/// <list type="bullet">
/// <item>routes its keys through <see cref="HandleKey"/> before the view's own shortcuts,</item>
/// <item>calls <see cref="Service"/> from <c>OnAfterBuild</c>,</item>
/// <item>and adds <see cref="Build"/> where the bar goes while <see cref="IsOpen"/>.</item>
/// </list>
/// </summary>
/// <remarks>
/// Three apps had grown the same bar around the same search, one per view that shows text, and
/// they had drifted: one had no match-case or regex switches and no close button, one did not
/// give the keyboard back when it closed, one counted past the hit cap as if the count were the
/// whole truth. The keys are Ctrl+F (open, or select the query in an open bar), Enter and
/// Shift+Enter, Alt+C and Alt+R, Escape (close the bar — a view that closes on Escape closes on
/// the next one), and with <see cref="LetterKeys"/> <c>n</c> and <c>p</c> when no text field has
/// the keyboard. Its look is the theme's <see cref="Styling.FindBarTheme"/>.
/// </remarks>
public sealed class FindBar
{
    private readonly Action _onChanged;
    private readonly Ref<TextInputElement> _field = new();
    private bool _pendingFocus;
    private bool _pendingReveal;
    private bool _pendingBodyFocus;

    /// <param name="onChanged">Runs whenever the bar or its search changes — normally the
    /// owner's <c>MarkDirty</c>.</param>
    public FindBar(Action onChanged) : this(new TextSearch(onChanged), onChanged) { }

    /// <summary>A bar over a search something else owns — a terminal searches its own scrollback.</summary>
    public FindBar(TextSearch search, Action onChanged)
    {
        Search = search;
        _onChanged = onChanged;
    }

    public TextSearch Search { get; }
    public bool IsOpen { get; private set; }

    /// <summary>The field's placeholder.</summary>
    public string Placeholder { get; set; } = "Find…";

    /// <summary>The field's size. A text field in a flex row needs an explicit height, or its
    /// content box collapses and it paints nothing.</summary>
    public float FieldWidth { get; set; } = 280f;
    public float FieldHeight { get; set; } = 30f;

    /// <summary>The selection in the body, if any — Ctrl+F then searches for its first line and
    /// starts at it rather than at the first occurrence, so opening the bar does not scroll the
    /// reader away from what they selected. Null: the bar keeps the query it had.</summary>
    public Func<FindSelection?>? Selection { get; set; }

    /// <summary>The element showing the searched text. It gets the keyboard back when the bar
    /// closes — the field is about to unmount, and a key sent to a detached element reaches
    /// nothing — and a reveal waits until it is on screen.</summary>
    public Func<Element?>? Body { get; set; }

    /// <summary>Brings <see cref="TextSearch.Current"/> on screen. Run after the frame is laid
    /// out (see <see cref="RevealAfterLayout"/>).</summary>
    public Action? Reveal { get; set; }

    /// <summary>
    /// Whether <see cref="Reveal"/> waits for the frame to be laid out. <c>OnAfterBuild</c> runs
    /// before the layout pass, so a reveal that reads a viewport height there reads the last
    /// frame's — or zero, on the frame the text first appears — and scrolls to the wrong place or
    /// not at all. False for a reveal that defers itself, such as
    /// <see cref="ScrollViewElement.ScrollRangeIntoView"/>, which then lands on the same frame.
    /// </summary>
    public bool RevealAfterLayout { get; set; } = true;

    /// <summary>Whether <c>n</c> and <c>p</c> walk the matches while the bar is open and no text
    /// field has the keyboard. In a field they are letters.</summary>
    public bool LetterKeys { get; set; }

    /// <summary>Draws the bar's glyphs from the app's icon set. Unset, or answering null for a
    /// glyph, the buttons show text arrows and a cross and there is no magnifier.</summary>
    public Func<FindBarIcon, Element?>? Icon { get; set; }

    /// <summary>Runs as the bar opens, before the selection is read — for a view that has to
    /// show its searchable text first.</summary>
    public Action? Opening { get; set; }

    /// <summary>Opens the bar with the keyboard in its field, seeded from <see cref="Selection"/>;
    /// on an open bar, puts the keyboard back in the field with the query selected whole, so
    /// what is typed next replaces it.</summary>
    public void Open()
    {
        Opening?.Invoke();
        IsOpen = true;
        _pendingFocus = true;
        if (Selection?.Invoke() is { } selected && TextSearch.QueryFromSelection(selected.Text) is { } query)
        {
            Search.SetQuery(query);
            Search.SeekTo(selected.Line, selected.Lane, selected.Start);
            _pendingReveal = true;
        }
        _onChanged();
    }

    /// <summary>Closes the bar and clears the query; the keyboard goes back to <see cref="Body"/>.</summary>
    public void Close()
    {
        IsOpen = false;
        Search.SetQuery("");
        // A reveal still owed is for a query that is gone.
        _pendingReveal = false;
        _pendingBodyFocus = true;
        _onChanged();
    }

    public void Next() => Move(1);
    public void Previous() => Move(-1);

    /// <summary>Brings the current match on screen at the next <see cref="Service"/> — for an
    /// owner whose text changed under a search that is still open. Only asks: it does not
    /// rebuild, so it is safe from inside the owner's <c>Build</c>.</summary>
    public void RevealCurrent() => _pendingReveal = true;

    private void Move(int dir)
    {
        Search.Move(dir);
        _pendingReveal = true;
        _onChanged();
    }

    private void ToggleMatchCase()
    {
        Search.ToggleMatchCase();
        _pendingReveal = true;
        _onChanged();
    }

    private void ToggleRegex()
    {
        Search.ToggleRegex();
        _pendingReveal = true;
        _onChanged();
    }

    /// <summary>
    /// The keys the bar owns; true when it used the key, and the caller should then stop it.
    /// Route a view's keys here before its own shortcuts: those are handled ahead of the focused
    /// field, and a <c>w</c> typed into the query must not toggle the view's wrapping.
    /// </summary>
    public bool HandleKey(KeyDownEvent e)
    {
        bool ctrl = (e.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Super)) != 0;
        bool alt = (e.Modifiers & KeyModifiers.Alt) != 0;
        char letter = e.ShortcutLetter;

        if (ctrl && !alt && letter == 'f') { Open(); return true; }
        if (!IsOpen) return false;

        switch (e.Key)
        {
            case KeyCode.Escape: Close(); return true;
            case KeyCode.Enter: Move((e.Modifiers & KeyModifiers.Shift) != 0 ? -1 : 1); return true;
        }
        if (alt && !ctrl && letter == 'c') { ToggleMatchCase(); return true; }
        if (alt && !ctrl && letter == 'r') { ToggleRegex(); return true; }

        // In a text field — the query, or a commit message beside the text — n and p are letters.
        if (LetterKeys && !ctrl && !alt && e.Target is not (TextInputElement or TextAreaElement { ReadOnly: false } or RichTextEditorElement { ReadOnly: false }))
        {
            if (letter == 'n') { Next(); return true; }
            if (letter == 'p') { Previous(); return true; }
        }
        return false;
    }

    /// <summary>
    /// The work that has to wait for a build — call it from the owner's <c>OnAfterBuild</c>.
    /// The field cannot take the keyboard before it exists, the body cannot get it back before the
    /// field is gone, and a match cannot be scrolled to before there is a body to scroll.
    /// </summary>
    public void Service()
    {
        if (_pendingFocus && _field.Current is { } field)
        {
            _pendingFocus = false;
            field.Focus();
            field.SelectAll();
        }

        // Only once the bar has left the tree: focusing the body while the field is mounted
        // would take the keyboard from what was just opened.
        if (_pendingBodyFocus && !IsOpen)
        {
            _pendingBodyFocus = false;
            Body?.Invoke()?.Focus();
        }

        if (!_pendingReveal || Reveal is not { } reveal) return;
        if (!RevealAfterLayout)
        {
            _pendingReveal = false;
            reveal();
            return;
        }

        // Held until there is a body on screen to receive it. "Is there a body" cannot be a null
        // check: a Ref goes on pointing at an element after it unmounts, so between a file being
        // chosen and its text arriving the body is still the last file's, and a reveal spent on it
        // is lost — the search reports hits and nothing scrolls. The body landing rebuilds, and
        // this comes round again.
        if (Body?.Invoke() is { } body && HostOf(body) is { } host && host.IsInTree(body))
        {
            _pendingReveal = false;
            host.RequestPostFrameCallback(reveal);
        }
    }

    /// <summary>The window an element is in. Not just its own <see cref="Element.OverlayHost"/>:
    /// the style pass hands that down, and it runs after <c>OnAfterBuild</c> — so a body that
    /// mounted in this very build has none yet, though an ancestor that was there before does.</summary>
    private static IOverlayHost? HostOf(Element element)
    {
        for (var e = element; e is not null; e = e.Parent)
            if (e.OverlayHost is { } host) return host;
        return null;
    }

    /// <summary>The bar, to add to the owner's tree while <see cref="IsOpen"/>.</summary>
    public Element Build()
    {
        var search = Search;
        var bar = new FindBarElement { MatchCase = search.MatchCase, UseRegex = search.UseRegex };
        bar.Flex.Direction = Layout.FlexDirection.Row;
        bar.Flex.AlignItems = Layout.AlignItems.Center;
        bar.Flex.FlexShrink = 0;
        // Its own keys, while the keyboard is in it — enough for an owner with nothing else to
        // route. An owner that routes HandleKey itself, for when the body has the keyboard, loses
        // nothing: a key the bar used here is stopped, and one it did not use it declines again.
        bar.On<KeyDownEvent>(e => { if (HandleKey(e)) e.StopPropagation = true; });

        if (Icon?.Invoke(FindBarIcon.Search) is { } magnifier)
            bar.AddChild(magnifier);

        var field = TextInput.New(search.Query).Width(FieldWidth).Height(FieldHeight);
        field.PlaceholderText(Placeholder);
        field.OnTextChanged(v => { search.SetQuery(v); _pendingReveal = true; });
        field.AccessibleName = "Find";
        field.AutomationId = "find-input";
        field.WithRef(_field);
        bar.AddChild(field);

        var status = TextElement.New(Status(search)).MaxLines(1);
        status.Flex.FlexShrink = 0;
        status.AutomationId = "find-status";
        bar.StatusIsBad = search.Error is not null || (search.Query.Length > 0 && !search.HasHits);
        bar.AddChild(status);

        bar.AddChild(Box.New().FlexGrow(1));

        bar.AddChild(Toggle("Aa", "Match case", "Alt+C", "find-case", ToggleMatchCase));
        bar.AddChild(Toggle(".*", "Regular expression", "Alt+R", "find-regex", ToggleRegex));
        bar.AddChild(NavButton(FindBarIcon.Previous, "↑", "Previous match", "Shift+Enter", "find-prev", Previous));
        bar.AddChild(NavButton(FindBarIcon.Next, "↓", "Next match", "Enter", "find-next", Next));
        bar.AddChild(NavButton(FindBarIcon.Close, "×", "Close", "Esc", "find-close", Close));
        return bar;
    }

    private ButtonElement Toggle(string label, string name, string key, string id, Action toggle)
    {
        var b = Button.New(label, ButtonVariant.Secondary);
        b.AccessibleName = name;
        b.AutomationId = id;
        b.Tooltip($"{name} · {key}");
        b.OnClick(_ => toggle());
        return b;
    }

    private ButtonElement NavButton(FindBarIcon icon, string glyph, string name, string key, string id, Action run)
    {
        var b = Icon?.Invoke(icon) is { } drawn
            ? Button.New(ButtonVariant.Secondary).Children(drawn)
            : Button.New(glyph, ButtonVariant.Secondary);
        b.AccessibleName = name;
        b.AutomationId = id;
        b.Tooltip(LetterKeys && icon != FindBarIcon.Close
            ? $"{name} · {(icon == FindBarIcon.Next ? "n" : "p")} / {key}"
            : $"{name} · {key}");
        b.OnClick(_ => run());
        return b;
    }

    /// <summary>
    /// What the count says. A pattern that does not compile, or one abandoned for taking too long,
    /// is not the same as one that found nothing — "no matches" would send someone looking for the
    /// text rather than at what they typed. A scan that stopped at the cap says so with a "+",
    /// so a count that is not the whole truth never reads as one.
    /// </summary>
    public static string Status(TextSearch search)
    {
        if (search.Query.Length == 0) return "";
        if (search.Error is { } why) return why;
        if (!search.HasHits) return "no matches";
        string total = search.Capped ? $"{TextSearch.MaxHits:N0}+" : $"{search.Hits.Count:N0}";
        return $"{search.Index + 1:N0} of {total}";
    }
}
