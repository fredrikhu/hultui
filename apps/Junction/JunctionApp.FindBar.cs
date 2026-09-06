using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using Junction.Theme;

namespace Junction;

public partial class JunctionApp
{
    // ── Find in the text on screen (Ctrl+F) ──────────────────────────────
    //
    // Shared by the diff pane and the Files view. Not the command palette, which answers
    // "which command"; this answers "where in this text", so it is a bar over the content,
    // every match is tinted where it sits, and the answer is a position to scroll to.
    //
    // The search itself is HultUI's TextSearch — the same model Vesper's viewer and comparison
    // view use, so a regular expression means the same thing in all four places. What lives
    // here is the bar, the keys, and the things that can only happen after a build.

    /// <summary>One view's find bar: the search, the field, and what is owed to the next
    /// build.</summary>
    private sealed class FindBarState
    {
        public FindBarState(Action onChanged) => Search = new TextSearch(onChanged);

        public readonly TextSearch Search;
        public readonly Ref<TextInputElement> FieldRef = new();
        public bool Open;
        public bool PendingFocus;
        public bool PendingReveal;
        public bool PendingBodyFocus;
        /// <summary>What the results were computed from. The diff pane rebuilds its rows every
        /// frame, so without this the search would rescan the whole file on every repaint.</summary>
        public object? BoundKey;
    }

    /// <summary>The bar Ctrl+F opens here, or null where there is no body of text to search.
    /// The Files view has its own; every other view that shows one shows the diff pane.</summary>
    private FindBarState? ActiveFindBar() => View switch
    {
        JunctionView.Files => _filesFind,
        JunctionView.Commit or JunctionView.Changes => _diffFind,
        _ => _diffFull ? _diffFind : null,
    };

    private void OpenFindBar(FindBarState s)
    {
        s.Open = true;
        s.PendingFocus = true;
        // Find works over the file's text, and blame replaces that text with a gutter view
        // whose rows are a virtual list — nothing for the search to bind to, and its reveal
        // would be scrolling a body no longer on screen. So Ctrl+F comes back to the source
        // rather than opening a bar that could not answer.
        if (ReferenceEquals(s, _filesFind)) _filesBlame = false;
        MarkDirty();
    }

    /// <summary>Closes the bar but not the view. Focus has to go somewhere still in the tree:
    /// the field is about to be unmounted, and an event dispatched to a detached element
    /// reaches only that element rather than bubbling to the handler that reads the app's
    /// keys.</summary>
    private void CloseFindBar(FindBarState s)
    {
        s.Open = false;
        s.Search.SetQuery("");
        // A reveal can now outlive the frame it was asked for, so drop any that is still
        // owed — the bar is gone and its query with it.
        s.PendingReveal = false;
        s.PendingBodyFocus = true;
        MarkDirty();
    }

    private void FindNav(FindBarState s, int dir)
    {
        s.Search.Move(dir);
        s.PendingReveal = true;
        MarkDirty();
    }

    /// <summary>
    /// The keys the bar owns. True when the key was consumed.
    ///
    /// <para>Checked before the view's own shortcuts, because those are routed ahead of the
    /// focused field — without this, <c>w</c> typed into the query would toggle wrapping.</para>
    /// </summary>
    private bool HandleFindBarKey(KeyDownEvent e, FindBarState s)
    {
        bool ctrl = e.Modifiers.HasFlag(KeyModifiers.Ctrl) || e.Modifiers.HasFlag(KeyModifiers.Super);
        bool shift = e.Modifiers.HasFlag(KeyModifiers.Shift);
        bool alt = e.Modifiers.HasFlag(KeyModifiers.Alt);

        if (ctrl && e.Key == KeyCode.F) { OpenFindBar(s); return true; }
        if (!s.Open) return false;

        switch (e.Key)
        {
            case KeyCode.Escape: CloseFindBar(s); return true;
            case KeyCode.Enter: FindNav(s, shift ? -1 : 1); return true;
            case KeyCode.C when alt: s.Search.ToggleMatchCase(); s.PendingReveal = true; return true;
            case KeyCode.R when alt: s.Search.ToggleRegex(); s.PendingReveal = true; return true;
            // n / p walk the matches, but only when no field has the keyboard — in one they
            // are letters. The commit composer counts: it is a TextArea, it sits in the same
            // view as the diff, and typing "not quite" into it used to walk the matches
            // instead of writing three of its characters.
            case KeyCode.N when e.Target is not (TextInputElement or TextAreaElement): FindNav(s, 1); return true;
            case KeyCode.P when e.Target is not (TextInputElement or TextAreaElement): FindNav(s, -1); return true;
            default: return false;
        }
    }

    /// <summary>
    /// The post-build half: the field cannot be focused before it exists, and the current
    /// match cannot be scrolled to before the frame has been laid out.
    ///
    /// <para>The reveal goes through a post-frame callback for that second reason. OnAfterBuild
    /// runs <i>before</i> the layout pass, so at this point the body's viewport height is
    /// whatever the previous frame left — and on the frame a file first appears, there is no
    /// previous frame and the measurements are all zero. Scrolling then either lands in the
    /// wrong place or, when the line height has not been measured either, does nothing at all.</para>
    /// </summary>
    private void ServiceFindBar(FindBarState s, Func<Element?> body, Action reveal)
    {
        // Ctrl+F on a bar that is already open lands here too. The query is still in the
        // field, and the person pressed the key to search for something else, so it is
        // selected whole: the next keystroke replaces it rather than appending to it. On a
        // freshly opened bar the field is empty and the selection is nothing.
        if (s.PendingFocus && s.FieldRef.Current is { } field)
        {
            s.PendingFocus = false;
            field.Focus();
            field.SelectAll();
        }
        if (s.PendingBodyFocus && !s.Open) { s.PendingBodyFocus = false; body()?.Focus(); }
        // Hold the request until there is a body to receive it. The reveal runs after the
        // frame — scrolling during Build would precede the layout pass and clamp to 0 — but
        // the callback gives up quietly when the pane is still a placeholder, and the flag
        // used to be cleared the moment the callback was *queued*. A reveal that fell due one
        // frame before the file's text arrived was therefore dropped and never retried: the
        // search reported its hits and nothing scrolled. The body landing triggers its own
        // rebuild, which is when this comes back round.
        //
        // "Is there a body" cannot be just a null check: a Ref keeps pointing at an element
        // after it unmounts, so between a file being selected and its text arriving, body()
        // answers with the previous file's TextArea. Spending the reveal there loses it the
        // same way — the callback scrolls a detached element, and the real body lands with
        // nothing owed.
        if (s.PendingReveal && body() is { } b && _window.IsInTree(b))
        {
            s.PendingReveal = false;
            _window.RequestPostFrameCallback(reveal);
        }
    }


    private Element BuildFindBar(FindBarState s, string placeholder)
    {
        var search = s.Search;
        var row = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(5, 12, 5, 12))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        row.AddChild(Ui.Icon(Icons.MagnifyingGlass, 13f, Nocturne.On(0.45)));

        // Explicit height: a flex-only TextInput collapses its content box and paints neither
        // text nor placeholder.
        var input = TextInput.New(search.Query).Width(280).Height(30f);
        input.PlaceholderText(placeholder);
        input.OnTextChanged(v => { search.SetQuery(v); s.PendingReveal = true; });
        input.WithRef(s.FieldRef);
        row.AddChild(input);

        bool bad = search.Error != null || (search.Query.Length > 0 && !search.HasHits);
        row.AddChild(Ui.Mono(FindStatus(search), 10.5f, bad ? Nocturne.DangerText : Nocturne.On(0.50))
            .MaxLines(1).FlexShrink(0));

        row.AddChild(Box.New().FlexGrow(1));

        row.AddChild(FindToggle("Aa", search.MatchCase, "Match case · Alt+C",
            () => { search.ToggleMatchCase(); s.PendingReveal = true; MarkDirty(); }));
        // ".*" rather than the word "regex": the shortest thing that says which syntax the
        // field is about to be read as, and it fits the same chip as "Aa".
        row.AddChild(FindToggle(".*", search.UseRegex, "Regular expression · Alt+R",
            () => { search.ToggleRegex(); s.PendingReveal = true; MarkDirty(); }));
        row.AddChild(Ui.IconButton(Icons.ArrowUp, () => FindNav(s, -1), "Previous · p / Shift+Enter", 26f));
        row.AddChild(Ui.IconButton(Icons.ArrowDown, () => FindNav(s, 1), "Next · n / Enter", 26f));
        row.AddChild(Ui.IconButton(Icons.X, () => CloseFindBar(s), "Close · Esc", 26f));
        return row;
    }

    private static string FindStatus(TextSearch search)
    {
        if (search.Query.Length == 0) return "";
        // A pattern that will not compile, or one abandoned for taking too long, is not the
        // same as one that found nothing — saying "no matches" would send someone looking for
        // the text rather than at what they typed.
        if (search.Error is { } why) return why;
        if (!search.HasHits) return "no matches";
        string total = search.Capped ? $"{TextSearch.MaxHits:N0}+" : $"{search.Hits.Count:N0}";
        return $"{search.Index + 1:N0} of {total}";
    }

    private Element FindToggle(string label, bool on, string tip, Action onClick)
    {
        var b = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(3, 7, 3, 7))
            .CornerRadius(Nocturne.RadiusSm)
            .Background(on ? Nocturne.Accent800 : Color.Transparent)
            .BorderColor(on ? Nocturne.Accent600 : Nocturne.Neutral700).BorderWidth(1f)
            .Children(Ui.Mono(label, 10f, on ? Nocturne.Accent200 : Nocturne.Neutral300))
            .Tooltip(tip);
        b.OnClick(_ => onClick());
        return b;
    }
}
