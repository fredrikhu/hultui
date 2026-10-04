using HultUI.Core.Events;

namespace Junction;

/// <summary>
/// The one table of keys the app answers to, as a framework <see cref="ShortcutMap"/>: the key
/// handlers ask it whether a press is a shortcut, and the Options › Keyboard shortcuts pane, the
/// panels' legends and every tooltip that names a key read their text from it — so a key cannot
/// be documented one way and bound another. Order is display order.
/// </summary>
/// <remarks>
/// <para>Matching here is exact about modifiers. The per-view handlers used to switch on the bare
/// key, so Ctrl+C on a selected branch checked it out, Ctrl+L pulled, Ctrl+S and Ctrl+W flipped
/// the diff's layout, and Ctrl+D dropped a commit from a rebase or marked a to-do done; the root
/// handler had to intercept every new Ctrl chord ahead of them to keep it from leaking down.</para>
/// <para>The arrows, Enter and Esc that move through a list or close an overlay stay with the
/// handlers that own them; the table lists them, with <see cref="Shortcut.Shown"/> text, for the
/// pane and the legends.</para>
/// </remarks>
public static class Shortcuts
{
    public static readonly ShortcutMap Map = Build();

    /// <summary>Whether <paramref name="e"/> is the shortcut <paramref name="id"/>.</summary>
    public static bool Pressed(string id, KeyDownEvent e) => Map.Matches(id, e);

    /// <summary>The keys of <paramref name="id"/> as shown — for a tooltip that names its key,
    /// so it cannot drift from the pane.</summary>
    public static string Keys(string id) => Map.Keys(id);

    /// <summary>A panel's legend row: the group's keys that carry a legend word, as key and word
    /// pairs, in table order — less the ones named in <paramref name="except"/>, for a panel
    /// that shows only some of its group's keys in a given state.</summary>
    public static IEnumerable<(string Keys, string Word)> Legend(string group, params string[] except) =>
        Map.All.Where(s => s.Group == group && s.LegendWord is not null && !except.Contains(s.Id))
            .Select(s => (s.KeysText, s.LegendWord!));

    private static ShortcutMap Build()
    {
        const KeyModifiers ctrl = KeyModifiers.Ctrl, alt = KeyModifiers.Alt, shift = KeyModifiers.Shift;
        static KeyChord K(KeyCode key, KeyModifiers modifiers = KeyModifiers.None) => new(key, modifiers);
        static KeyChord[] Digits(int from, int to, KeyModifiers modifiers = KeyModifiers.None) =>
            Enumerable.Range(from, to - from + 1).Select(n => new KeyChord(KeyCode.Digit0 + (ushort)n, modifiers)).ToArray();

        var map = new ShortcutMap();

        const string global = "Global";
        map.Add("view", global, "Overview, History, Commit, Changes, Branches, Tags, Files", Digits(1, 7)).Shown("1 – 7");
        map.Add("palette", global, "Search & commands", K(KeyCode.P, ctrl), K(KeyCode.K, ctrl));
        map.Add("options", global, "Options", K(KeyCode.Comma, ctrl));
        map.Add("find", global, "Find in the diff or file on screen", K(KeyCode.F, ctrl), K(KeyCode.F));
        map.Add("sidebar", global, "Show or hide the sidebar", K(KeyCode.Backtick));
        map.Add("repo", global, "Previous / next repository · the Nth",
            [K(KeyCode.Up, alt), K(KeyCode.Down, alt), .. Digits(1, 9, alt)]).Shown("Alt+↑ ↓ · Alt+1 – 9");
        map.Add("workspace", global, "The Nth workspace", Digits(1, 9, ctrl)).Shown("Ctrl+1 – 9");
        // The symbols, not the keys: + has a key of its own on a Swedish keyboard, where the
        // US = key is a dead accent and the US - key is +, which zoomed out.
        map.Add("zoom.in", global, "Zoom in", KeyChord.Of('+', ctrl), KeyChord.Of('=', ctrl));
        map.Add("zoom.out", global, "Zoom out", KeyChord.Of('-', ctrl));
        map.Add("zoom.reset", global, "Actual size", K(KeyCode.Digit0, ctrl));
        map.Add("close", global, "Close the topmost overlay", K(KeyCode.Escape));

        const string diff = "Diff";
        map.Add("diff.split", diff, "Unified or split", K(KeyCode.S));
        map.Add("diff.wrap", diff, "Wrap long lines", K(KeyCode.W));
        map.Add("diff.whitespace", diff, "Ignore whitespace changes", K(KeyCode.I));
        map.Add("diff.fullscreen", diff, "Fullscreen diff", K(KeyCode.F11));
        map.Add("diff.editor", diff, "Open in the editor", K(KeyCode.D, ctrl));
        map.Add("diff.open", diff, "Open the file", K(KeyCode.O, ctrl));
        map.Add("commit.step", diff, "Previous / next commit", K(KeyCode.Up, ctrl), K(KeyCode.Down, ctrl)).Shown("Ctrl+↑ ↓");
        map.Add("commit.message", diff, "Show or hide the commit message body", K(KeyCode.M));

        map.Add("history.search", "History", "Search", K(KeyCode.Slash));
        map.Add("files.search", "Files", "Find a file", K(KeyCode.Slash));

        const string branches = "Branches";
        map.Add("branches.search", branches, "Search", K(KeyCode.Slash));
        map.Add("branches.move", branches, "Move · check out", K(KeyCode.Up), K(KeyCode.Down), K(KeyCode.Enter)).Shown("↑ ↓ ↵");
        map.Add("branches.checkout", branches, "Check out", K(KeyCode.C));
        map.Add("branches.push", branches, "Push", K(KeyCode.P));
        map.Add("branches.pull", branches, "Pull", K(KeyCode.L));
        map.Add("branches.mr", branches, "Create or open the merge request", K(KeyCode.M));
        map.Add("branches.include", branches, "Include in or exclude from history", K(KeyCode.I));
        map.Add("branches.tree", branches, "Folders or one flat list", K(KeyCode.T));
        map.Add("branches.folder", branches, "Close · open a folder", K(KeyCode.Left), K(KeyCode.Right)).Shown("← →");

        const string tags = "Tags";
        map.Add("tags.search", tags, "Search", K(KeyCode.Slash));
        map.Add("tags.checkout", tags, "Check out", K(KeyCode.C));
        map.Add("tags.push", tags, "Push", K(KeyCode.P));
        map.Add("tags.tree", tags, "Folders or one flat list", K(KeyCode.T));
        map.Add("tags.folder", tags, "Close · open a folder", K(KeyCode.Left), K(KeyCode.Right)).Shown("← →");

        const string mrs = "Merge requests";
        map.Add("mr.search", mrs, "Search", K(KeyCode.Slash)).Legend("search");
        map.Add("mr.move", mrs, "Move · open", K(KeyCode.Up), K(KeyCode.Down), K(KeyCode.Enter)).Shown("↑ ↓ ↵").Legend("open");
        map.Add("mr.checkout", mrs, "Checkout locally", K(KeyCode.C)).Legend("checkout locally");
        map.Add("mr.review", mrs, "Review in the diff", K(KeyCode.R)).Legend("review");
        map.Add("mr.approve", mrs, "Approve or revoke", K(KeyCode.A)).Legend("approve");
        map.Add("mr.browser", mrs, "Open in the browser", K(KeyCode.O)).Legend("open in browser");
        map.Add("mr.back", mrs, "Back · close", K(KeyCode.Escape)).Legend("back");

        const string review = "Review";
        map.Add("review.caret", review, "Move the caret", K(KeyCode.Up), K(KeyCode.Down)).Shown("↑ ↓").Legend("caret");
        map.Add("review.extend", review, "Extend the range", K(KeyCode.Up, shift), K(KeyCode.Down, shift)).Shown("Shift+↑ ↓").Legend("extend range");
        map.Add("review.comment", review, "Comment on the line or range", K(KeyCode.C)).Legend("comment");
        map.Add("review.next", review, "Next unresolved thread", K(KeyCode.N)).Legend("next unresolved");
        map.Add("review.previous", review, "Previous unresolved thread", K(KeyCode.P)).Legend("previous");
        map.Add("review.expand", review, "Expand or collapse every thread", K(KeyCode.E)).Legend("expand all");
        map.Add("review.post", review, "Post the comment", K(KeyCode.Enter, ctrl));
        map.Add("review.back", review, "Close the composer · clear the range · leave", K(KeyCode.Escape));

        const string rebase = "Rebase";
        map.Add("rebase.select", rebase, "Select a commit", K(KeyCode.Up), K(KeyCode.Down)).Shown("↑ ↓").Legend("select");
        map.Add("rebase.move", rebase, "Move the commit", K(KeyCode.Up, alt), K(KeyCode.Down, alt)).Shown("Alt+↑ ↓").Legend("move");
        map.Add("rebase.pick", rebase, "Pick", K(KeyCode.P));
        map.Add("rebase.reword", rebase, "Reword", K(KeyCode.R));
        map.Add("rebase.edit", rebase, "Edit", K(KeyCode.E));
        map.Add("rebase.squash", rebase, "Squash", K(KeyCode.S));
        map.Add("rebase.fixup", rebase, "Fixup", K(KeyCode.F));
        map.Add("rebase.drop", rebase, "Drop", K(KeyCode.D));
        map.Add("rebase.start", rebase, "Start the rebase", K(KeyCode.Enter));
        map.Add("rebase.cancel", rebase, "Cancel", K(KeyCode.Escape));

        const string todo = "To do";
        map.Add("todo.move", todo, "Move · open", K(KeyCode.Up), K(KeyCode.Down), K(KeyCode.Enter)).Shown("↑ ↓ ↵").Legend("open");
        map.Add("todo.done", todo, "Mark done", K(KeyCode.D)).Legend("done");
        map.Add("todo.close", todo, "Close", K(KeyCode.Escape)).Legend("close");

        return map;
    }
}
