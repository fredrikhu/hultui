namespace Junction;

/// <summary>
/// The one table of keys the app answers to. The Options › Keyboard shortcuts pane is drawn
/// from it, and the panels' legends and the tooltips that name a key read it too, so a key
/// cannot be documented one way and bound another. Order is display order.
/// </summary>
public static class Shortcuts
{
    public sealed record Entry(string Group, string Keys, string What);

    public static readonly IReadOnlyList<Entry> All =
    [
        new("Global", "1 – 7", "Overview, History, Commit, Changes, Branches, Tags, Files"),
        new("Global", "Ctrl P", "Search & commands"),
        new("Global", "Ctrl ,", "Options"),
        new("Global", "Ctrl F · f", "Find in the diff or file on screen"),
        new("Global", "`", "Show or hide the sidebar"),
        new("Global", "Alt ↑ ↓ · Alt N", "Previous / next repository · the Nth"),
        new("Global", "Ctrl N", "The Nth workspace"),
        new("Global", "Esc", "Close the topmost overlay"),

        new("Diff", "s", "Unified or split"),
        new("Diff", "w", "Wrap long lines"),
        new("Diff", "i", "Ignore whitespace changes"),
        new("Diff", "F11", "Fullscreen diff"),
        new("Diff", "Ctrl D", "Open in the editor"),
        new("Diff", "Ctrl ↑ ↓", "Previous / next commit"),
        new("Diff", "m", "Show or hide the commit message body"),

        new("Branches", "/", "Search"),
        new("Branches", "↑ ↓ ↵", "Move · check out"),
        new("Branches", "p", "Push"),
        new("Branches", "m", "Create or open the merge request"),

        new("Merge requests", "/", "Search"),
        new("Merge requests", "↑ ↓ ↵", "Move · open"),
        new("Merge requests", "c", "Checkout locally"),
        new("Merge requests", "r", "Review in the diff"),
        new("Merge requests", "a", "Approve or revoke"),
        new("Merge requests", "o", "Open in the browser"),
        new("Merge requests", "Esc", "Back · close"),

        new("Review", "↑ ↓", "Move the caret"),
        new("Review", "⇧ ↑ ↓", "Extend the range"),
        new("Review", "c", "Comment on the line or range"),
        new("Review", "n p", "Next / previous unresolved thread"),
        new("Review", "e", "Expand or collapse every thread"),
        new("Review", "Ctrl ↵", "Post the comment"),
        new("Review", "Esc", "Close the composer · clear the range · leave"),

        new("To do", "↑ ↓ ↵", "Move · open"),
        new("To do", "d", "Mark done"),
    ];

    /// <summary>The groups, in display order.</summary>
    public static IEnumerable<string> Groups => All.Select(e => e.Group).Distinct();

    /// <summary>The keys bound to one description in one group — for a tooltip that names
    /// its key, so it cannot drift from the pane.</summary>
    public static string Keys(string group, string what) =>
        All.FirstOrDefault(e => e.Group == group && e.What == what)?.Keys ?? "";
}
