using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using VesperCommander.Theme;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── Search results as a browsable pane ("feed to listbox") ───────────
    // After Alt+F7, the find dialog can hand its hit list to a pane. The pane then serves
    // that held set instead of a directory, so everything the pane already does — marking,
    // F5/F6, F8, Ctrl+M, sorting, the view modes — operates on results that may span dozens
    // of folders. This is a *view* over real files, never a place: nothing can be written
    // into it (see <see cref="ResultsBlock"/>), and it lives only for the session.
    //
    // The tab keeps its Dir pointing at the folder the search ran under, so free space, the
    // command line, Ctrl+G and ".." all still have a real folder to speak about — the fed
    // set is layered over that folder rather than replacing the tab's sense of where it is.

    /// <summary>A fed-in result set a pane is browsing. Entries are real files from any
    /// number of folders, so every row carries its own full path and nothing may be resolved
    /// against the pane's folder.</summary>
    private sealed class ResultListing(string root, string query, List<FileEntry> entries, int skippedInArchive)
    {
        public readonly string Root = root;              // the folder the search ran under
        public readonly string Query = query;            // what was searched for, for the crumb
        public List<FileEntry> Entries = entries;
        public readonly int SkippedInArchive = skippedInArchive;

        // Build() asks for the rows every frame, so the sorted view is cached and only
        // recomputed when the tab's sort changes (a header click) or the set itself does
        // (Replace drops the cache).
        private List<FileEntry>? _rows;
        private SortMode _rowsSort;
        private bool _rowsDesc, _rowsDirsFirst;

        public IReadOnlyList<FileEntry> Rows(SortMode sort, bool desc, bool dirsFirst)
        {
            if (_rows != null && _rowsSort == sort && _rowsDesc == desc && _rowsDirsFirst == dirsFirst)
                return _rows;
            var rows = new List<FileEntry>(Entries.Count + 1)
            {
                // Leads the list exactly as a folder's parent row does — activating it (or
                // Backspace) is the way back out to the folder the search ran under.
                new("..", Root, IsDir: true, 0, DateTime.Now, IsParent: true),
            };
            rows.AddRange(FileSystem.Sort(Entries, sort, dirsFirst, desc));
            _rows = rows;
            _rowsSort = sort; _rowsDesc = desc; _rowsDirsFirst = dirsFirst;
            return rows;
        }

        /// <summary>Swap the entry set in (a refresh / prune) and drop the sorted cache.</summary>
        public void Replace(List<FileEntry> entries) { Entries = entries; _rows = null; }
    }

    private ResultListing? ResultsOf(char side) => ActiveTab(side).Results;
    private bool InResults(char side) => ActiveTab(side).Results != null;

    /// <summary>The tinted treatment for a fed-in result set — the archive crumb's opposite
    /// number in the same family, so "this pane is not a plain folder" reads the same way
    /// twice without the two ever being confused for each other.</summary>
    private static readonly Color ResultsTeal = Color.FromHex("#6bc0d9");

    // ── Feeding ──────────────────────────────────────────────────────────
    /// <summary>Hand the find dialog's hits to <paramref name="side"/> as a virtual folder.
    /// Archive members are left out: their writes go through the archive VFS, which move,
    /// delete and rename can't drive, and a listing that silently refuses half its rows is
    /// worse than one that says which rows it dropped.</summary>
    private void FeedResultsToPane(char side)
    {
        DrainFindResults();
        if (_findResults.Count == 0) return;

        string root = Dir(_findSide);
        var entries = new List<FileEntry>(_findResults.Count);
        int inArchive = 0;
        foreach (var h in _findResults)
        {
            if (ArchivePath.InArchive(h.FullPath)) { inArchive++; continue; }
            entries.Add(new FileEntry(h.Name, h.FullPath, h.IsDir, h.Size, h.Modified, h.Attributes));
        }
        if (entries.Count == 0)
        {
            _notice = inArchive > 0
                ? "Every match is inside an archive — those can't be fed to a pane"
                : "Nothing to feed to the pane";
            MarkDirty();
            return;
        }

        string query = _findPattern.Length > 0 ? _findPattern
            : _findContains.Length > 0 ? $"“{_findContains}”" : "all files";

        var tab = ActiveTab(side);
        tab.Results = new ResultListing(root, query, entries, inArchive);
        tab.Dir = root;              // the pane still has a real folder behind it
        tab.Cursor = 0;
        tab.Marks.Clear();
        _search = "";

        _findOpen = false;
        StopFind();
        _active = side;
        BumpGen(side);               // so leaving the results re-reads the folder
        RequestScroll(side);
        _notice = $"{entries.Count} result{(entries.Count == 1 ? "" : "s")} in this pane · Backspace returns to the folder"
            + (inArchive > 0 ? $" · {inArchive} inside archives left out" : "");
        PaneRef(side).Current?.Focus();
        MarkDirty();
    }

    /// <summary>Leave a fed-in result set, landing on the folder the search ran under.
    /// Returns false when the pane wasn't showing one (so callers can fall through to their
    /// normal behavior).</summary>
    private bool ExitResults(char side)
    {
        var tab = ActiveTab(side);
        if (tab.Results is not { } v) return false;
        tab.Results = null;
        _search = "";
        tab.Marks.Clear();
        tab.Cursor = 0;
        NavigateTo(side, v.Root, recordHistory: false);
        return true;
    }

    // ── Serving the listing ──────────────────────────────────────────────
    /// <summary>The rows a fed-in pane shows: the held set in the tab's sort order, behind
    /// a ".." row that leaves.</summary>
    private IReadOnlyList<FileEntry> ResultRows(char side, ResultListing v) =>
        v.Rows(SortOf(side), SortDescOf(side), _settings.DirsFirst);

    /// <summary>Ctrl+R over a fed-in set, and the settle after any file operation: re-read
    /// each entry from disk, dropping the ones that are gone. The set is a snapshot of a walk
    /// that already happened — re-running the search would be a different question — so the
    /// refresh is per-entry rather than a new find.</summary>
    private void RefreshResults(char side)
    {
        if (ActiveTab(side).Results is not { } v) return;
        var kept = new List<FileEntry>(v.Entries.Count);
        foreach (var e in v.Entries) if (Restat(e) is { } fresh) kept.Add(fresh);
        v.Replace(kept);
        // Marks and the cursor index both survive: marks are keyed by path, and the loader's
        // clamp doesn't run here, so clamp the cursor ourselves.
        SetCursor(side, Math.Clamp(Cursor(side), 0, Math.Max(0, ResultRows(side, v).Count - 1)));
    }

    /// <summary>Both panes' fed-in sets after an operation that may have moved or removed
    /// files. A pane showing a folder is refreshed by its generation bump instead.</summary>
    private void RefreshResultsBothSides() { RefreshResults('L'); RefreshResults('R'); }

    /// <summary>An entry re-read from disk, or null when it no longer exists. A read that
    /// throws keeps the row: a locked or momentarily unreadable file has not gone away, and
    /// dropping it would quietly shrink the result set.</summary>
    private static FileEntry? Restat(FileEntry e)
    {
        try
        {
            if (e.IsDir)
            {
                var di = new DirectoryInfo(e.FullPath);
                return di.Exists ? e with { Modified = di.LastWriteTime, Attributes = di.Attributes } : null;
            }
            var fi = new FileInfo(e.FullPath);
            return fi.Exists ? e with { Size = fi.Length, Modified = fi.LastWriteTime, Attributes = fi.Attributes } : null;
        }
        catch { return e; }
    }

    /// <summary>Replace one entry after an in-place rename, so the fed-in row shows its new
    /// name instead of a path that no longer resolves.</summary>
    private void ReplaceResultEntry(char side, string oldPath, string newPath)
    {
        if (ActiveTab(side).Results is not { } v) return;
        var kept = new List<FileEntry>(v.Entries.Count);
        foreach (var e in v.Entries)
        {
            if (!PathEquals(e.FullPath, oldPath)) { kept.Add(e); continue; }
            if (Restat(FileOps.EntryFromPath(newPath)) is { } fresh) kept.Add(fresh);
        }
        v.Replace(kept);
    }

    // ── Write gating ─────────────────────────────────────────────────────
    /// <summary>True (and posts a notice) when something would be written *into* a fed-in
    /// result set. A result set is a view of files that live elsewhere — there is no folder
    /// for a copy, a move or a new folder to land in. Mirrors <see cref="LockedBlock"/> and
    /// <see cref="InArchiveBlock"/>; reading out of the set is unrestricted.</summary>
    private bool ResultsBlock(char side)
    {
        if (!InResults(side)) return false;
        _notice = "Search results aren't a folder — Backspace first, then write there";
        MarkDirty();
        return true;
    }

    // ── Breadcrumb ───────────────────────────────────────────────────────
    /// <summary>The search root as the results breadcrumb shows it, collapsed the way the path
    /// trail collapses to the width the window leaves it.</summary>
    private string ResultsRootLabel(ResultListing v) =>
        CollapsePath(v.Root, Math.Max(60f, BreadcrumbBudget() - 130f - EstTextWidth(v.Query, mono: true)));

    /// <summary>The breadcrumb a fed-in pane shows in place of the path trail: a pinned teal
    /// results pill, then the folder the search ran under — clicking either leaves.</summary>
    private Element BuildResultsBreadcrumb(char side, bool active, ResultListing v)
    {
        var row = Flex.Row(gap: 6).FlexGrow(1).AlignItems(AlignItems.Center)
            .FlexWrap(FlexWrap.NoWrap).Overflow(Overflow.Hidden);
        row.AutomationId = $"results-crumb-{side}";
        row.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
        row.AccessibleName = "Search results — leave";

        int shown = v.Entries.Count;
        var pill = Box.New().FlexShrink(0)
            .Padding(new Thickness(1, 6, 1, 6)).CornerRadius(Nocturne.RadiusSm)
            .Background(Nocturne.Alpha(ResultsTeal, active ? 0.16 : 0.10))
            .Cursor(CursorType.Pointer)
            .Tooltip($"Search results for {v.Query} — Backspace returns to {v.Root}");
        var inner = Flex.Row(gap: 5).AlignItems(AlignItems.Center);
        inner.AddChild(Ui.Icon(Glyphs.Search, 11f, ResultsTeal));
        inner.AddChild(Ui.Mono($"{shown} result{(shown == 1 ? "" : "s")}", 11.5f, ResultsTeal)
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        pill.AddChild(inner);
        pill.OnClick(_ => { _active = side; ExitResults(side); MarkDirty(); });
        row.AddChild(pill.HoverBg(Nocturne.Alpha(ResultsTeal, 0.22), Nocturne.Alpha(ResultsTeal, 0.10)));

        row.AddChild(Text.New("in").FlexShrink(0).FontSize(11f).TextColor(Nocturne.Neutral600));

        // The search root, collapsed the way the path trail collapses — it is the way out,
        // so it says where "back" goes rather than merely decorating the pill.
        string rootLabel = ResultsRootLabel(v);
        NoteFitCrumbs(side, -1, rootLabel);
        var back = Box.New().FlexShrink(1).Overflow(Overflow.Hidden)
            .Padding(new Thickness(1, 5, 1, 5)).CornerRadius(4f).Cursor(CursorType.Pointer)
            .Tooltip(v.Root)
            .Children(Ui.Mono(rootLabel, 11.5f, Nocturne.Neutral400)
                .MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        back.OnClick(_ => { _active = side; ExitResults(side); MarkDirty(); });
        row.AddChild(back.HoverBg(Nocturne.Neutral900, Color.Transparent));

        row.AddChild(Box.New().FlexGrow(1));
        // An explicit way out for the mouse, next to the trail it replaced.
        var leave = Box.New().FlexShrink(0).Cursor(CursorType.Pointer)
            .Padding(new Thickness(1, 5, 1, 5)).CornerRadius(4f)
            .Tooltip("Leave the results (Backspace)")
            .Children(Ui.Icon(Glyphs.Close, 11f, Nocturne.Neutral500));
        leave.AutomationId = $"results-leave-{side}";
        leave.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
        leave.AccessibleName = "Leave search results";
        leave.OnClick(_ => { _active = side; ExitResults(side); MarkDirty(); });
        row.AddChild(leave.HoverBg(Nocturne.Neutral900, Color.Transparent));

        var outer = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(7, 12, 5, 12));
        outer.AddChild(row);
        outer.AddChild(ViewToggle(side));
        return outer;
    }

    /// <summary>Where a fed-in row actually lives, shown beside its name: the folder relative
    /// to the search root ("src\vfs"), or "" for a row that sits in the root itself. The rows
    /// of a result set span folders, so the name alone doesn't identify the file.</summary>
    private static string ResultSource(ResultListing v, FileEntry e)
    {
        var parent = System.IO.Path.GetDirectoryName(e.FullPath) ?? "";
        if (parent.Length == 0 || PathEquals(parent, v.Root)) return "";
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return parent.StartsWith(v.Root, cmp)
            ? parent[v.Root.Length..].TrimStart(System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar)
            : parent;
    }
}
