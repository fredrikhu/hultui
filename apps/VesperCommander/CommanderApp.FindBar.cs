using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using VesperCommander.Theme;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── Find in the open text (Ctrl+F) ───────────────────────────────────
    //
    // Shared by the file viewer and the comparison view. Deliberately not the Alt+F7 find,
    // which walks folders and answers "which files"; this answers "where in this one", so it
    // is a bar over the text rather than a dialog over the app, every match is tinted where it
    // sits, and the answer is a position to scroll to rather than a list to pick from.
    //
    // The search itself is HultUI's TextSearch. What lives here is the bar, the keys, and the
    // three things that can only happen after a build: focusing the field, putting the
    // keyboard back when the bar closes, and scrolling the current match into view.

    /// <summary>One view's find bar: the search, the field, and what is owed to the next
    /// build. A view owns one of these and passes it to the shared builders below.</summary>
    private sealed class FindBarState
    {
        public FindBarState(Action onChanged) => Search = new TextSearch(onChanged);

        public readonly TextSearch Search;
        public readonly Ref<TextInputElement> FieldRef = new();
        public bool Open;
        public bool PendingFocus;       // focus the field once it is in the tree
        public bool PendingReveal;      // bring the current match on screen
        public bool PendingBodyFocus;   // hand the keyboard back when the field goes away
    }

    private void OpenFindBar(FindBarState s)
    {
        s.Open = true;
        s.PendingFocus = true;
        MarkDirty();
    }

    /// <summary>Closes the bar but not the view. Focus has to go somewhere still in the tree:
    /// the field is about to be unmounted, and an event dispatched to a detached element
    /// reaches only that element — it never bubbles to the handler that reads the view's keys,
    /// so leaving focus on it would make the view deaf.</summary>
    private void CloseFindBar(FindBarState s)
    {
        s.Open = false;
        s.Search.SetQuery("");
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
    /// The keys the bar owns, wherever it is. True when the key was consumed.
    ///
    /// <para>Called before the view's own keys, because the view's are routed ahead of the
    /// focused field — without this, <c>w</c> typed into the query would toggle wrapping. The
    /// caller stops after this returns true, and skips its own letter keys entirely while
    /// <paramref name="typing"/>.</para>
    /// </summary>
    private bool HandleFindBarKey(KeyDownEvent e, FindBarState s, Action closeView)
    {
        bool ctrl = e.Modifiers.HasFlag(KeyModifiers.Ctrl) || e.Modifiers.HasFlag(KeyModifiers.Super);
        bool shift = e.Modifiers.HasFlag(KeyModifiers.Shift);
        bool alt = e.Modifiers.HasFlag(KeyModifiers.Alt);

        if (ctrl && e.Key == KeyCode.F) { OpenFindBar(s); return true; }
        if (!s.Open) return false;

        switch (e.Key)
        {
            // Esc backs out of the search first and the view second — the same two-step the
            // Options dialog uses for its own search field.
            case KeyCode.Escape: CloseFindBar(s); return true;
            case KeyCode.Enter: FindNav(s, shift ? -1 : 1); return true;
            case KeyCode.C when alt: s.Search.ToggleMatchCase(); s.PendingReveal = true; return true;
            case KeyCode.R when alt: s.Search.ToggleRegex(); s.PendingReveal = true; return true;
            default: return false;
        }
    }

    /// <summary>The post-build half: the field cannot be focused before it exists, and the
    /// current match cannot be scrolled to before the rows have been measured.</summary>
    private void ServiceFindBar(FindBarState s, Ref<ScrollViewElement> body, Action reveal)
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
        // Only once the bar is gone from the tree — focusing the body while the field is still
        // mounted would take the keyboard away from what the user just opened.
        if (s.PendingBodyFocus && !s.Open) { s.PendingBodyFocus = false; body.Current?.Focus(); }
        if (s.PendingReveal) { s.PendingReveal = false; reveal(); }
    }

    private Element BuildFindBar(FindBarState s, string placeholder)
    {
        var search = s.Search;
        var row = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(5, 12, 5, 12))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        row.AddChild(Ui.Icon(Glyphs.Search, 13f, Nocturne.On(0.45)));

        // Explicit height: a flex-only TextInput collapses its content box and paints neither
        // text nor placeholder. See Ui.FieldHeight.
        var input = TextInput.New(search.Query).Width(280).Height(Ui.FieldHeight);
        input.PlaceholderText(placeholder);
        input.OnTextChanged(v => { search.SetQuery(v); s.PendingReveal = true; });
        input.WithRef(s.FieldRef);
        row.AddChild(input);

        bool bad = search.Error != null || (search.Query.Length > 0 && !search.HasHits);
        row.AddChild(Ui.Mono(FindStatus(search), 10.5f, bad ? Nocturne.MarkRed : Nocturne.On(0.50))
            .MaxLines(1).FlexShrink(0));

        row.AddChild(Box.New().FlexGrow(1));

        row.AddChild(FindToggle("Aa", search.MatchCase, "Match case · Alt+C",
            () => { search.ToggleMatchCase(); s.PendingReveal = true; MarkDirty(); }));
        // ".*" rather than the word "regex": it is the shortest thing that says which syntax
        // the field is about to be read as, and it fits the same chip as "Aa".
        row.AddChild(FindToggle(".*", search.UseRegex, "Regular expression · Alt+R",
            () => { search.ToggleRegex(); s.PendingReveal = true; MarkDirty(); }));
        row.AddChild(DiffNavBtn(Glyphs.ArrowUp, "p / Shift+Enter", () => FindNav(s, -1)));
        row.AddChild(DiffNavBtn(Glyphs.ArrowDown, "n / Enter", () => FindNav(s, 1)));
        row.AddChild(DiffNavBtn(Glyphs.Close, "Esc", () => CloseFindBar(s)));
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
        // "of 20,000+" when the scan stopped at the cap, so a count that is not the whole
        // truth never reads as one.
        string total = search.Capped ? $"{TextSearch.MaxHits:N0}+" : $"{search.Hits.Count:N0}";
        return $"{search.Index + 1:N0} of {total}";
    }

    private Element FindToggle(string label, bool on, string tip, Action onClick)
    {
        var b = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(3, 7, 3, 7)).CornerRadius(4f)
            .Background(on ? Nocturne.Accent800 : Color.Transparent)
            .BorderColor(on ? Nocturne.Accent600 : Nocturne.Neutral700).BorderWidth(1f)
            .Children(Ui.Mono(label, 10f, on ? Nocturne.Accent200 : Nocturne.Neutral300))
            .Tooltip(tip);
        b.OnClick(_ => onClick());
        return b.HoverBg(Nocturne.Neutral900, Color.Transparent);
    }
}
