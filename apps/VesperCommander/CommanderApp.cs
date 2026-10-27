using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using VesperCommander.Theme;

namespace VesperCommander;

/// <summary>
/// Root of Vesper Commander: the Nocturne dual-pane file-manager shell
/// (rail · left pane · right pane · preview · command line) over the real
/// filesystem. Keyboard is the primary interface; the mouse is a convenience.
///
/// <para>Slice 1: real directory listings through a virtualized list, cursor
/// movement, and Enter/.. navigation. Marking, overlays, tabs, drive picker, and
/// the Options window land in following slices. Split across
/// <c>CommanderApp.*.cs</c> partials à la the Junction sample.</para>
/// </summary>
public partial class CommanderApp : Component, IDisposable
{
    private readonly Application _app;
    private readonly Window _window;

    // Manual double-click detection for the file rows (see BuildPane's PointerDown).
    // Keyed by side + entry path so two presses on the same row within the window read
    // as a double-click even though the row element was re-realized in between. (Path, not
    // name: a fed-in result set can list two same-named files, and a press on one followed
    // by a press on the other is two clicks, not a double-click.)
    private const long DoubleClickMs = 400;
    private string? _lastPressPath;
    private char _lastPressSide;
    private long _lastPressTick;
    private bool IsDoublePress(char side, string path)
    {
        long now = Environment.TickCount64;
        bool dbl = _lastPressPath == path && _lastPressSide == side
                   && now - _lastPressTick <= DoubleClickMs;
        _lastPressSide = side;
        _lastPressTick = now;
        _lastPressPath = dbl ? null : path;   // consume, so a third press isn't a second activate
        return dbl;
    }

    // Each pane is a list of tabs; a tab owns its folder, cursor, and mark set.
    // Marks are kept by full path (see MarkKey) and cleared on navigation.
    private sealed class PaneTab(string dir)
    {
        public string Dir = dir;
        public int Cursor;
        public bool Locked;
        public ViewMode View = ViewMode.Detail;   // per-tab list layout, persisted per session
        public SortMode Sort = SortMode.Name;      // per-tab sort column + direction (seeded
        public bool SortDescending;                // from the Options default at tab creation)
        public readonly HashSet<string> Marks = PathSet();
        // Live only while a Shift+navigation sweep is running: the row it started from, and
        // the marks that were already set when it did. Per tab, so switching tabs mid-sweep
        // cannot carry one pane's anchor into another's listing.
        public string? MarkAnchor;
        public HashSet<string>? MarkBase;
        // Non-null while this tab browses a fed-in search result set instead of its folder
        // (CommanderApp.Results.cs). Dir still names the folder the search ran under.
        public ResultListing? Results;
        // Per-tab navigation history (most-recent last). Back holds folders left behind;
        // Fwd holds folders returned-from after a Back. A new forward navigation clears Fwd.
        public readonly List<string> Back = new();
        public readonly List<string> Fwd = new();
    }

    /// <summary>The key an entry is held under in a mark set: its full path.
    ///
    /// <para>Names would be shorter, but a fed-in search result set lists files from many
    /// folders at once, where two rows routinely share a name — and every operation resolves
    /// its targets by <see cref="FileEntry.FullPath"/> anyway. Keying the marks the same way
    /// is what makes "marked" mean one row rather than every row that happens to be called
    /// the same thing.</para></summary>
    private static string MarkKey(FileEntry e) => e.FullPath;

    /// <summary>A set of paths, compared the way the platform compares them.</summary>
    private static HashSet<string> PathSet() => new(OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private List<PaneTab> _tabsL = [];
    private List<PaneTab> _tabsR = [];
    private int _tabL, _tabR;                        // active tab index per pane
    private char _active = 'L';                      // focused pane
    private Settings _settings = new();

    // Options window: edits a draft copy; Save commits + persists + applies.
    private bool _optionsOpen;
    private int _optionsPage;                        // 0 General 1 Panes 2 Keyboard 3 Tools 4 Colors
    private string _optSearch = "";                  // filters settings across all pages when non-empty
    private readonly Ref<HultUI.Core.Elements.TextInputElement> _optSearchRef = new();
    private Settings _draft = new();

    // Incremental "type to find" for the active pane: word characters extend it and
    // jump the cursor to the first prefix match; any nav/Enter/Tab/Escape clears it.
    private string _search = "";
    private string? _notice;   // transient message shown in the command line (cleared on next key)

    // ── Overlays (at most one open) ──────────────────────────────────────
    // Drive picker: the pane it targets ('L'/'R'), plus the highlighted row.
    private char? _drivePicker;
    private int _driveCur;
    private IReadOnlyList<DriveEntry> _drives = [];

    // Command palette (Ctrl+P): the framework's, over Commands().
    private CommandPalette? _palette;

    // Bookmarks: quick jumps for the active pane. Seeded from real folders; the
    // manager (Ctrl+D / rail +) adds/removes them and Ctrl+1..9 jump by index.
    private sealed record Bookmark(string Name, string Path);
    private readonly List<Bookmark> _bookmarks = [];
    private bool _bookmarksOpen;
    private int _bookmarkSel;

    // Confirm dialog (delete, and the copy/move go-ahead) + in-progress transfer.
    /// <summary><paramref name="OnCancel"/> runs when the dialog is dismissed rather than
    /// confirmed — by the Cancel button or Escape, which are the same answer. Most confirms
    /// have nothing to do in that case and leave it null; one that was asked *about* something
    /// (an edit waiting to be written back) needs to record that it has now been answered, or
    /// it asks again the next time it looks.</summary>
    /// <summary><paramref name="CancelLabel"/> null makes it an information dialog: one button,
    /// which is also what Enter and Escape both mean — there is nothing to decide.</summary>
    private sealed record ConfirmSpec(string Title, string Message, string ConfirmLabel, bool Danger,
        Action OnConfirm, Action? OnCancel = null, string? CancelLabel = "Cancel");
    private ConfirmSpec? _confirm;

    // Name prompt (F7 new folder, Shift+F6 rename): a titled dialog over a focused
    // text field. Commit returns an error string (kept inline, dialog stays open) or
    // null (success, dialog closes). Reused for both flows. SelectAll opens with the
    // seeded value selected, so typing replaces it and End or an arrow key edits it.
    private sealed record NamePromptSpec(string Title, string Subtitle, string ConfirmLabel, Func<string, string?> Commit,
        string Placeholder = "Name", bool SelectAll = false);
    private NamePromptSpec? _namePrompt;
    private string _namePromptValue = "";
    private string? _namePromptError;
    private readonly Ref<HultUI.Core.Elements.TextInputElement> _namePromptRef = new();
    private bool _pendingNamePromptFocus;

    private sealed class TransferOp
    {
        public bool IsMove;
        public char Source;
        public List<FileEntry> Items = [];
        public string DestDir = "";
        public int Index;
        public bool? OverwriteAll;   // null = ask each conflict; true/false = apply to rest
        public int Done, Skipped, Failed;

        /// <summary>Packing rather than copying. The overlay is the same shape — a queue of
        /// named items with a byte bar over it — so it is driven by the same op; this changes
        /// the wording and drops the controls that don't apply (there is no per-entry skip,
        /// and no destination to resolve conflicts against).</summary>
        public bool IsPack;

        /// <summary>The archive being written, for the pack overlay's subtitle.</summary>
        public string PackTarget = "";

        /// <summary>The overlay's title for a <see cref="IsPack"/> op — "Packing" a new archive,
        /// "Adding to" one that already exists.</summary>
        public string PackVerb = "Packing";

        /// <summary>What a queue row says while it is the one being written. Separate from
        /// <see cref="PackVerb"/> because the title is a heading and this is a status: "Adding to"
        /// reads as the archive's name following it, which a row has nothing to follow with.</summary>
        public string PackRowVerb = "packing";

        /// <summary>For an add into an archive: the member each row would replace, keyed by row.
        /// The overwrite dialog stats its destination on disk, and an archive member isn't
        /// there — without this the Target column reports every member as absent.</summary>
        public Dictionary<int, (long Size, DateTime Modified)>? ArchiveTargets;

        /// <summary>The backend reports one percentage for the whole run rather than bytes per
        /// member — 7-Zip, which is packing in its own process. The overlay drops the per-file
        /// bar for these: it has nothing to fill it with, and a bar pinned at 0% beside a moving
        /// total reads as a stall rather than as an absence.</summary>
        public bool PackWholeRunOnly;

        // ── Streaming progress (background copy) ─────────────────────────
        public readonly long[] ItemBytes = [];      // per-item total size
        public byte[] Status = [];                   // 0 queued · 1 active · 2 done · 3 skipped · 4 failed
        public long TotalBytes, TotalBytesDone;
        public long FileBytes, FileBytesDone;        // current item
        // For rate / ETA. PausedTick is when the current pause began; PausedTotal is how long
        // earlier pauses lasted. Both come off the elapsed time, or a run held for a minute
        // reports the rate it would have had if it had spent that minute copying — near zero,
        // with an ETA to match.
        public long StartTick, PausedTick, PausedTotal;
        public volatile bool Paused, Cancelled, SkipCurrent;
        public ManualResetEventSlim? Gate;           // set while paused at a conflict (bg waits)
        public bool ConflictOverwrite;               // the choice released through Gate

        /// <summary>Items that failed purely for lack of rights, with the destination they
        /// were headed for — the batch offered for an elevated retry when the run ends.</summary>
        public readonly List<(string Source, string Target, bool IsDir)> Denied = [];

        /// <summary>Temp folder holding archive members extracted so the elevated worker has
        /// a real file to copy (see StageForElevation). Null until one is needed; deleted once
        /// the retry finishes or is declined.</summary>
        public string? Staging;

        public TransferOp() { }
        public TransferOp(List<FileEntry> items)
        {
            Items = items;
            ItemBytes = new long[items.Count];
            Status = new byte[items.Count];
        }
    }
    private TransferOp? _op;          // set once a copy/move is confirmed and running
    private TransferOp? _syncSecondBatch;   // sync runs two copy batches (→ then ←); this is the second
    private bool _overwriteOpen;      // the transfer paused on a conflict awaiting a choice
    private bool _progressOpen;       // the streamed copy-progress overlay is showing

    /// <summary>Load persisted bookmarks; on first run (no file) seed sensible
    /// defaults from the real special folders and persist them.</summary>
    private void InitBookmarks()
    {
        var persisted = BookmarkStore.Load();
        if (persisted.Count > 0)
        {
            foreach (var b in persisted) _bookmarks.Add(new Bookmark(b.Name, b.Path));
            return;
        }
        SeedBookmarks();
        SaveBookmarks();
    }

    private void SeedBookmarks()
    {
        void Add(string name, string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)
                && !_bookmarks.Any(b => PathEquals(b.Path, path)))
                _bookmarks.Add(new Bookmark(name, path));
        }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add("Home", home);
        Add("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        Add("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (home.Length > 0) Add("Downloads", System.IO.Path.Combine(home, "Downloads"));
    }

    private void SaveBookmarks() =>
        BookmarkStore.Save(_bookmarks.Select(b => new BookmarkData(b.Name, b.Path)));

    private static string HotkeyLabel(int index) => index < 9 ? $"Ctrl+{index + 1}" : "";

    // Off-thread directory reads. Keyed by "path#gen" so a Refresh reloads and a
    // navigation supersedes the previous listing without blocking Build().
    // Not readonly: Ctrl+U swaps the two panes wholesale, and a loader belongs to the tabs
    // it read for rather than to a screen position (see SwapPanes).
    private AsyncLoader<DirListing> _listL = new(DirListing.Loading);
    private AsyncLoader<DirListing> _listR = new(DirListing.Loading);
    private int _genL, _genR;
    private bool _loadedL, _loadedR;

    // Per-pane forced state override for the design's review shortcuts
    // (Ctrl+Shift+E/O/D set empty/offline/denied; K clears). Null ⇒ use the real
    // filesystem-derived state.
    private PaneState? _forceStateL, _forceStateR;

    private readonly Ref<VirtualListElement<FileEntry>> _vlistL = new();
    private readonly Ref<VirtualListElement<FileEntry>> _vlistR = new();
    private bool _pendingScrollL, _pendingScrollR;

    // ── Pane scroll across a full-shell view ─────────────────────────────
    // The viewer, the diff and sync replace the panes with the view, and when it closes the
    // panes are built again from nothing — so the lists opened at the top, wherever they had
    // been. The offset of each pane's scrolling element (which one depends on the view mode)
    // is noted on the way in and put back on the first build with the panes on screen again.
    private readonly float[] _paneScroll = new float[2];
    private int _pendingPaneScroll;   // builds left to try the restore; 0 = nothing pending

    /// <summary>Call before opening a full-shell view, while the panes still exist.</summary>
    private void RememberPaneScroll()
    {
        if (FullShellOpen) return;   // sync → diff: the panes are already gone, and noted
        foreach (char side in "LR")
            _paneScroll[Si(side)] = ViewOf(side) switch
            {
                ViewMode.Brief => BriefScroll(side).Current?.ScrollOffsetX ?? 0f,
                ViewMode.Thumbnails => ThumbVList(side).Current?.ScrollOffsetY ?? 0f,
                _ => VList(side).Current?.ScrollOffsetY ?? 0f,
            };
        // A few builds rather than one: the offset clamps against a content size the list
        // may not know on the very first build after the panes come back.
        _pendingPaneScroll = 4;
    }

    private void RestorePaneScroll()
    {
        bool settled = true;
        foreach (char side in "LR")
        {
            float want = _paneScroll[Si(side)];
            if (want <= 0f) continue;
            float got;
            switch (ViewOf(side))
            {
                case ViewMode.Brief:
                    if (BriefScroll(side).Current is not { } bs) { settled = false; continue; }
                    bs.ScrollOffsetX = want; got = bs.ScrollOffsetX; break;
                case ViewMode.Thumbnails:
                    if (ThumbVList(side).Current is not { } tv) { settled = false; continue; }
                    tv.ScrollOffsetY = want; got = tv.ScrollOffsetY; break;
                default:
                    if (VList(side).Current is not { } vl) { settled = false; continue; }
                    vl.ScrollOffsetY = want; got = vl.ScrollOffsetY; break;
            }
            if (got < want) settled = false;   // clamped short — the content isn't measured yet
        }
        _pendingPaneScroll = settled ? 0 : _pendingPaneScroll - 1;
        if (_pendingPaneScroll > 0) MarkViewDirty();
    }

    // Each pane is a Tab-focusable region; `_active` follows which one holds focus
    // (Tab traversal and click-to-focus both route through the same FocusManager, so
    // they stay in sync). Kept per side for programmatic focus on startup / overlay close.
    private readonly Ref<BoxElement> _paneRefL = new();
    private readonly Ref<BoxElement> _paneRefR = new();
    private Ref<BoxElement> PaneRef(char side) => side == 'L' ? _paneRefL : _paneRefR;

    // The side to focus once the panes are back in the tree, set when a full-shell view
    // closes. Deferred because the Refs still point at the panes that view replaced.
    private char? _pendingPaneFocus;

    /// <summary>True while the diff, sync or file viewer has replaced the rail and panes.
    /// They own the whole shell rather than layering over it, so the panes aren't merely
    /// covered — they're gone from the tree.</summary>
    private bool FullShellOpen => _diffOpen || _syncOpen || _viewerOpen;

    // Off-thread text excerpt for the preview column, keyed by the focused file's
    // path + mtime + size (so editing a file refreshes its preview).
    private readonly AsyncLoader<string> _previewLoader = new("");

    // True while the read-only preview TextArea holds keyboard focus (Full mode), so
    // the pane draws the accent ring around it. Driven by its Focus/Blur handlers.
    private bool _previewFocused;

    // TextMate spans for the current preview excerpt, tokenized off the UI thread so
    // scrolling stays smooth — the excerpt first renders plain and the colors pop in when
    // the background tokenize lands. Keyed on the excerpt *instance*: the excerpt loads
    // asynchronously, so keying on the string reference (not the file key) both dedupes
    // per loaded excerpt and keeps text + spans in lock-step (else offsets drift when a
    // freshly-switched file briefly still shows the previous excerpt). Null ⇒ not ready.
    private string? _previewSpanExcerpt;
    private IReadOnlyList<TextSpan>? _previewSpans;

    // Folder sizes the user has measured with Space, keyed by full path. The running
    // total is written from a background walk (Interlocked) and read on the UI thread
    // each render, so the size column counts up live and settles when Done. Touched
    // (added) only on the UI thread; the worker mutates just its own DirSize.
    private sealed class DirSize
    {
        private long _bytes;
        public long Bytes => System.Threading.Interlocked.Read(ref _bytes);
        public void Set(long v) => System.Threading.Interlocked.Exchange(ref _bytes, v);
        public volatile bool Done;
        public volatile bool Cancelled;
    }
    private readonly Dictionary<string, DirSize> _dirSizes = new(StringComparer.Ordinal);

    // Off-thread image decode for the preview (keyed like the text excerpt). The
    // previous decoded image is disposed when a new one lands; superseded in-flight
    // decodes are freed by SKBitmap's finalizer.
    private readonly AsyncLoader<HultUI.Core.Rendering.IImageSource?> _imageLoader = new(null);
    private HultUI.Core.Rendering.IImageSource? _lastImage;

    private static HultUI.Core.Rendering.IImageSource? LoadImage(string path, long size)
    {
        if (size > 24 * 1024 * 1024) return null;   // don't decode very large images
        // Bytes on disk say nothing about pixels: see ImageDecode. Null (an unreadable image,
        // or one whose frame is past the pixel budget) renders as "Cannot preview".
        var bmp = ImageDecode.Bounded(path, ImageDecode.PreviewTargetPx);
        return bmp == null ? null : new HultUI.Rendering.Skia.SkiaImageSource(bmp);
    }

    /// <summary>Read a bounded copy of an archive member's bytes (for preview), or null when
    /// oversized / unreadable. The member stream may be non-seekable, so we copy.</summary>
    private byte[]? ReadMemberBytes(FileEntry e, long maxBytes, string? password = null)
    {
        if (e.Size <= 0 || e.Size > maxBytes) return null;
        if (ArchivePath.TryParse(e.FullPath) is not { } ap) return null;
        try
        {
            // The password is passed in when the caller reads off the UI thread — it takes
            // the lookup on the UI thread first rather than racing the dictionary the
            // password prompt writes to.
            using var s = ArchiveVfs.OpenEntry(ap.ArchiveFile, ap.Internal, password ?? PasswordFor(e.FullPath), _sevenZip);
            if (s == null) return null;
            using var ms = new MemoryStream();
            var buf = new byte[16 * 1024];
            int r;
            while ((r = s.Read(buf, 0, buf.Length)) > 0)
            {
                ms.Write(buf, 0, r);
                if (ms.Length > maxBytes) return null;
            }
            return ms.ToArray();
        }
        catch { return null; }
    }

    private string PreviewTextMember(FileEntry e)
    {
        var bytes = ReadMemberBytes(e, 256 * 1024);
        return bytes == null ? "" : FileSystem.PreviewTextFromBytes(bytes);
    }

    private HultUI.Core.Rendering.IImageSource? LoadImageMember(FileEntry e)
    {
        var bytes = ReadMemberBytes(e, 24 * 1024 * 1024);
        if (bytes == null) return null;
        var bmp = ImageDecode.Bounded(bytes, ImageDecode.PreviewTargetPx);
        return bmp == null ? null : new HultUI.Rendering.Skia.SkiaImageSource(bmp);
    }

    private const float RailWidth = 44f;
    private float RowHeight => _settings.Density == Density.Compact ? 20f : 24f;

    public CommanderApp(Application app, Window window, string startDir, string? startDirRight = null)
    {
        _app = app;
        _window = window;
        // Icons rasterize at the display scale, so keep it current — including when the
        // window is dragged to a monitor with different scaling, which arrives as a resize.
        Icons.DisplayScale = window.DisplayScale;
        window.Resized += OnWindowResized;
        // Coming back to the window is when a file taken out to an external editor gets
        // re-checked (CommanderApp.ArchiveEdit.cs).
        window.Activated += OnWindowActivated;
        _settings = SettingsStore.Load();
        SeedFindDefaults();
        // A sideways offset is app state, so moving it has to rebuild the rows.
        _diffPan = new HorizontalPan(MarkDirty);
        _viewerPan = new HorizontalPan(MarkDirty);
        _viewerFind = NewFindBar("Find in file…", _viewerScrollRef, RevealViewerHit);
        _viewerFind.Selection = ViewerFindSelection;
        _diffFind = NewFindBar("Find in comparison…", _diffScrollRef, RevealDiffHit);
        _diffFind.Selection = DiffFindSelection;
        Nocturne.Use(_settings.Theme, _settings.Accent);
        _app.Theme = Nocturne.ToHultTheme();
        RebuildKeymap();
        SevenZip.ExePath = _settings.SevenZipPath;
        _sevenZip = SevenZip.Available();   // probed once — the probe spawns a process

        var session = SessionStore.Load();

        // The window opens where it was closed whether or not the tabs come back with it: that
        // option is about which folders, not about where the window sits. The window is still
        // hidden here, so it opens in place rather than jumping there.
        window.Placement = session?.Window;

        // Restore the last session's tabs when enabled and still valid; else open one
        // tab per pane on the starting folder.
        bool restored = false;
        if (_settings.RestoreTabs && session is { } s)
        {
            // Pair each dir with its saved view mode before filtering, so a dropped
            // (no-longer-existing) folder doesn't misalign the remaining view indices.
            var left = s.Left.Select((d, i) => (d, v: ViewAt(s.LeftViews, i))).Where(x => Restorable(x.d)).ToList();
            var right = s.Right.Select((d, i) => (d, v: ViewAt(s.RightViews, i))).Where(x => Restorable(x.d)).ToList();
            if (left.Count > 0 && right.Count > 0)
            {
                foreach (var (d, v) in left) _tabsL.Add(SeededTab(d, v));
                foreach (var (d, v) in right) _tabsR.Add(SeededTab(d, v));
                _tabL = Math.Clamp(s.LeftActive, 0, _tabsL.Count - 1);
                _tabR = Math.Clamp(s.RightActive, 0, _tabsR.Count - 1);
                restored = true;
            }
        }
        if (!restored)
        {
            _tabsL.Add(SeededTab(startDir, _settings.DefaultView));
            _tabsR.Add(SeededTab(startDirRight ?? startDir, _settings.DefaultView));
        }
        InitBookmarks();

        // Global keys land on the host when nothing is focused.
        Mount().On<KeyDownEvent>(HandleKeyDown);
    }

    /// <summary>Whether a saved tab is worth reopening. A local folder is checked as it always
    /// was — the question costs microseconds, and a folder that has gone away should not come
    /// back as a tab. A remote one is taken on trust: asking cost up to 21 seconds per tab,
    /// serially, on the UI thread before the window existed, and a share that is merely
    /// unreachable right now is exactly the tab you want back. It opens offline, with a
    /// Reconnect button.</summary>
    private static bool Restorable(string dir) => PathProbe.IsRemote(dir) || Directory.Exists(dir);

    // ── Per-side accessors (delegate to the pane's active tab) ───────────
    private List<PaneTab> Tabs(char side) => side == 'L' ? _tabsL : _tabsR;
    private int TabIndex(char side) => side == 'L' ? _tabL : _tabR;
    private void SetTabIndex(char side, int i) { if (side == 'L') _tabL = i; else _tabR = i; }
    private PaneTab ActiveTab(char side) => Tabs(side)[TabIndex(side)];

    private string Dir(char side) => ActiveTab(side).Dir;
    private int Cursor(char side) => ActiveTab(side).Cursor;
    private AsyncLoader<DirListing> Loader(char side) => side == 'L' ? _listL : _listR;
    private int Gen(char side) => side == 'L' ? _genL : _genR;
    private Ref<VirtualListElement<FileEntry>> VList(char side) => side == 'L' ? _vlistL : _vlistR;

    private void SetCursor(char side, int value)
    {
        // Any cursor move that is not part of a Shift sweep ends the sweep, so the next
        // Shift+↓ starts a fresh range from wherever the cursor now is — clicking a row,
        // paging without Shift, or landing somewhere after a folder change all count.
        if (!_shiftMarking) EndMarkSweep(side);
        ActiveTab(side).Cursor = value;
    }
    private HashSet<string> Marks(char side) => ActiveTab(side).Marks;
    private void BumpGen(char side) { if (side == 'L') _genL++; else _genR++; }

    /// <summary>A new tab seeded with the current Options defaults for view mode + sort.</summary>
    private PaneTab SeededTab(string dir, ViewMode view) => new(dir)
        { View = view, Sort = _settings.Sort, SortDescending = _settings.SortDescending };

    private SortMode SortOf(char side) => ActiveTab(side).Sort;
    private bool SortDescOf(char side) => ActiveTab(side).SortDescending;

    /// <summary>Open <paramref name="path"/> as a new tab in the active pane and bring the
    /// window forward — used when a second launch is folded into this instance (single-instance
    /// IPC). Called on the UI thread.</summary>
    public void OpenPathAsTab(string path)
    {
        // A remote path is taken on trust — this runs on the UI thread, and the second launch
        // that sent it has already decided it wants that folder. The pane reports for itself.
        if (!PathProbe.IsRemote(path) && !Directory.Exists(path)) return;
        NewTab(_active, path);
        _window.Raise();
        MarkDirty();
    }

    private ViewMode ViewOf(char side) => ActiveTab(side).View;
    private void SetView(char side, ViewMode mode)
    {
        if (ActiveTab(side).View == mode) return;
        ActiveTab(side).View = mode;
        RequestScroll(side);
        MarkDirty();
    }

    private PaneState PaneStateOf(char side)
    {
        var forced = side == 'L' ? _forceStateL : _forceStateR;
        if (forced is { } f) return f;
        // A fed-in result set has no directory to be offline or denied; it is Ok while it
        // holds anything and Empty once a refresh has taken the last row away.
        if (ActiveTab(side).Results is { } v)
            return v.Entries.Count == 0 ? PaneState.Empty : PaneState.Ok;
        if (IsConnecting(side)) return PaneState.Connecting;
        return Loader(side).Value.State;
    }

    // ── Waiting on a folder ──────────────────────────────────────────────
    // The pane keeps showing the last folder it read while a new one loads (stale-while-
    // loading), which is right for a local read that lands in a frame or two and wrong for a
    // share that may never answer — the rows belong to somewhere else entirely, and they are
    // operable. These three track what is on screen versus what is being waited for.
    private readonly string?[] _shownDir = new string?[2];    // folder the current rows came from
    private readonly string?[] _loadingKey = new string?[2];  // loader key currently being timed
    private readonly long[] _loadStarted = new long[2];       // platform timestamp that key began

    /// <summary>How long a folder may take before the pane says it is still connecting. A local
    /// read is orders of magnitude inside this, so the state belongs to slow things only.
    /// Settable so a test need not spend it.</summary>
    internal int ConnectingDelayMs = 350;

    /// <summary>True while a pane is waiting on a folder it is not already showing, and has
    /// been for long enough to be worth saying so. A refresh of the folder on screen keeps its
    /// rows and never shows this — there is nothing to wait for that is not already there.
    ///
    /// <para>Measured on the application's clock, not the wall's: under a headless session that
    /// advances only when a frame is pumped, so the threshold is a number of frames rather than
    /// a race with whatever else the machine is doing. Timing it with
    /// <c>Environment.TickCount64</c> made four unrelated pane tests pass on their own and fail
    /// in a full run.</para></summary>
    private bool IsConnecting(char side)
    {
        int i = side == 'L' ? 0 : 1;
        if (!Loader(side).IsLoading) return false;
        if (PathEquals(_shownDir[i], Dir(side))) return false;
        return _app.Time.GetElapsedTime(_loadStarted[i]).TotalMilliseconds >= ConnectingDelayMs;
    }

    // One per side, cancelled when the pane stops wanting the listing it started.
    private readonly CancellationTokenSource?[] _loadCts = new CancellationTokenSource?[2];

    /// <summary>The token for the load identified by <paramref name="key"/>, cancelling
    /// whatever the pane was reading before it. The old source is dropped rather than disposed:
    /// its producer may still be inside the syscall and will read the token on the way out.</summary>
    private CancellationToken LoadToken(char side, string key)
    {
        int i = side == 'L' ? 0 : 1;
        if (_loadingKey[i] != key || _loadCts[i] is null)
        {
            _loadCts[i]?.Cancel();
            _loadCts[i] = new CancellationTokenSource();
            _loadingKey[i] = key;
            _loadStarted[i] = _app.Time.GetTimestamp();
            NudgeWhenConnectingIsDue(side);
        }
        return _loadCts[i]!.Token;
    }

    /// <summary>Repaint once the connecting threshold has passed, if the folder is still out.
    /// An app waiting on one produces no rebuilds of its own, so without this the state would
    /// appear only the next time something else happened to redraw — the class of bug where a
    /// pane sits on stale content until you touch it. The still-loading test keeps a navigation
    /// that landed at once from costing a rebuild a third of a second later, which every
    /// navigation otherwise would.</summary>
    private void NudgeWhenConnectingIsDue(char side)
    {
        if (ConnectingDelayMs <= 0) return;   // already due; the build in progress will show it
        // The connecting block is the pane's view: the parts stay.
        _ = Task.Delay(ConnectingDelayMs + 20).ContinueWith(_ =>
            HultUI.Application.InvokeOnMainThread(() => { if (Loader(side).IsLoading) MarkViewDirty(); }));
    }

    /// <summary>The Cancel in the connecting block: stop waiting and go back to the folder the
    /// pane was showing. Only offered when there is one — on a cold start there is nowhere to
    /// return to, and the listing resolves to Offline on its own soon enough.</summary>
    private void CancelPaneLoad(char side)
    {
        int i = side == 'L' ? 0 : 1;
        var back = _shownDir[i];
        _loadCts[i]?.Cancel();
        _loadCts[i] = null;
        _loadingKey[i] = null;
        if (back != null && !PathEquals(back, Dir(side))) NavigateTo(side, back, recordHistory: false);
        else MarkDirty();
    }

    /// <summary>Whether <see cref="CancelPaneLoad"/> has anywhere to go.</summary>
    private bool CanCancelPaneLoad(char side) => _shownDir[side == 'L' ? 0 : 1] != null;

    /// <summary>The current (possibly stale-while-loading) listing for a side,
    /// kicking off / refreshing the off-thread load as a side effect of Build().</summary>
    private IReadOnlyList<FileEntry> Listing(char side)
    {
        // A fed-in result set short-circuits the directory loader entirely: there is no
        // folder to read, and the held set is what the pane is for.
        if (ActiveTab(side).Results is { } results) return ResultRows(side, results);

        var loader = Loader(side);
        var dir = Dir(side);
        var sort = SortOf(side); var desc = SortDescOf(side);   // per-tab; captured for the worker
        var key = $"{dir}#{Gen(side)}";
        var ct = LoadToken(side, key);
        loader.Load(key,
            () => FileSystem.List(dir, _settings.ShowHidden, sort, _settings.DirsFirst, desc, _sevenZip, PasswordFor(dir), ct),
            onLoaded: _ =>
        {
            if (side == 'L') _loadedL = true; else _loadedR = true;
            _shownDir[side == 'L' ? 0 : 1] = dir;   // these rows belong to this folder now
            var listed = loader.Value.Entries;
            // If we ascended, land the cursor on the folder we came out of.
            var want = _pendingFocusChild[side == 'L' ? 0 : 1];
            if (want != null)
            {
                _pendingFocusChild[side == 'L' ? 0 : 1] = null;
                int found = IndexOfName(listed, want);
                if (found >= 0) SetCursor(side, found);
            }
            // Clamp the cursor into the freshly-loaded range and scroll to it.
            SetCursor(side, Math.Clamp(Cursor(side), 0, Math.Max(0, listed.Count - 1)));
            RequestScroll(side);
            // The rows reach the view; this pane's footer reads them too, and builds again on its
            // own (see Part). The rest of its parts show the folder, which the navigation that
            // asked for these rows built them for.
            MarkViewDirty();
        });
        return loader.Value.Entries;
    }

    /// <summary>Keep the OS window title on the folder you are looking at. Vesper draws its
    /// own chrome, so this is not the caption — it is what the task bar, Alt+Tab and every
    /// window switcher show, and "Vesper Commander" three times over tells you nothing about
    /// which of them is the one you want. Folder first, for the same reason.</summary>
    private void SyncWindowTitle()
    {
        var tab = ActiveTab(_active);
        var where = tab.Results != null ? "Results" : TabLabel(tab.Dir);
        // Setting it is a no-op when unchanged, at both this layer and SDL's, so calling it
        // once a build costs nothing and needs no separate invalidation path.
        _window.Title = string.IsNullOrEmpty(where) ? "Vesper Commander" : $"{where} — Vesper Commander";
    }

    // ── Build ────────────────────────────────────────────────────────────
    // How many builds have run. The Brief grid keeps its cells only from the build just before,
    // a resize asks whether the panes the latest build built would come out the same, and the
    // tests count what rebuilds.
    private int _builds;

    public override Element Build()
    {
        _builds++;
        SyncWindowTitle();
        var body = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);

        // Full-shell views (diff / sync / viewer) replace the rail + panes entirely.
        if (FullShellOpen)
        {
            var view = _diffOpen ? BuildDiff() : _syncOpen ? BuildSync() : BuildViewer();
            view.Key = _diffOpen ? "diff" : _syncOpen ? "sync" : "viewer";
            body.AddChild(view);
        }
        else
        {
        // The rail is a part of its own (see Part), keyed by its host.
        body.AddChild(RailPart.Place().FlexShrink(0));
        if (_settings.PreviewPane)
        {
            // The preview stands in for the pane you are *not* browsing, and shows what the
            // pane you are browsing has selected. So it swaps sides with you: press Tab and
            // the column you just left becomes the preview. Anchoring it to one side instead
            // would put it under the cursor half the time and leave you reading a file on the
            // far side of the window from the list you are moving through.
            //
            // Give both a 0 flex-basis so grow splits the row 50/50 regardless of content —
            // otherwise the pane's large content basis starves an empty preview (e.g. on "..")
            // into a sliver.
            var pane = BuildPane(_active).FlexBasis(Dimension.Px(0));
            var pv = BuildPreview(); pv.Key = "preview";
            pv.FlexBasis(Dimension.Px(0));

            // Document order decides Tab order, so the left column is always first.
            if (_active == 'L') { body.AddChild(pane); body.AddChild(VDivider()); body.AddChild(pv); }
            else { body.AddChild(pv); body.AddChild(VDivider()); body.AddChild(pane); }
        }
        else
        {
            body.AddChild(BuildPane('L'));
            body.AddChild(VDivider());
            body.AddChild(BuildPane('R'));
        }
        }
        body.Key = "body";

        // The title bar and the command line are parts of their own (see Part), keyed by their
        // hosts: every child of the root is keyed, so a kept host goes back without a mark.
        var root = Flex.Column(gap: 0)
            .FlexGrow(1)
            .Background(Nocturne.Bg)
            .TextColor(Nocturne.Text)
            .FontSize(12.5f)
            .Children(ChromePart.Place().FlexShrink(0), body, CmdPart.Place().FlexShrink(0));
        // A right-drag mark ends wherever the button is released — pointer-up bubbles
        // to the root even when the release lands off the rows (empty list, footer, …).
        // The Find dialog's corner-grip resize tracks the same way: move updates the size,
        // up ends the drag, both regardless of where the pointer wandered.
        // Passive, and heard only during one of the two: every other move over the window is a
        // move over idle panes, which drew a full frame each while this was an ordinary handler
        // (a handler might have changed something without saying so) and made a move event for
        // it. Both mark what they change: the resize rebuilds, the hold changes nothing on screen.
        root.OnPassive<PointerMoveEvent>(ev =>
        {
            if (_findResizing) UpdateFindResize(ev.Position.X, ev.Position.Y);
            // Watched at the root, not on the row: a drag that leaves the row it started on
            // is exactly the case that must not still count as holding still.
            UpdateContextHold(ev.Position.X, ev.Position.Y);
        }, _rootMoveListening ??= () => _findResizing || _holdArmed);
        root.On<PointerUpEvent>(ev =>
        {
            if (ev.Button == 2) { CancelContextHold(); EndRightMark(); }
            if (_findResizing) _findResizing = false;
        });
        // The sideways scrollbars of the diff and the viewer: a drag ends wherever the button
        // comes up, which is routinely off the thumb.
        _diffPan.AttachDrag(root);
        _viewerPan.AttachDrag(root);

        // Overlays render above the shell, each keyed so conditional presence
        // reconciles by identity (a keyless swap would bleed retained state — focus,
        // caret, scroll — across overlay kinds).
        if (_drivePicker is char dp)
        {
            var o = BuildDrivePicker(dp); o.Key = "drive-picker"; root.AddChild(o);
        }
        if (_findOpen)
        {
            var o = BuildFind(); o.Key = "find"; root.AddChild(o);
        }
        if (_packOpen)
        {
            var o = BuildPack(); o.Key = "pack"; root.AddChild(o);
        }
        if (_renameOpen)
        {
            var o = BuildRename(); o.Key = "rename"; root.AddChild(o);
        }
        if (_pwOpen)
        {
            var o = BuildPasswordPrompt(); o.Key = "password"; root.AddChild(o);
        }
        if (_bookmarksOpen)
        {
            var o = BuildBookmarks(); o.Key = "bookmarks"; root.AddChild(o);
        }
        if (_progressOpen && _op is { } pop)
        {
            var o = BuildProgress(pop); o.Key = "progress"; root.AddChild(o);
        }
        if (_confirm is { } confirm)
        {
            var o = BuildConfirm(confirm); o.Key = "confirm"; root.AddChild(o);
        }
        if (_overwriteOpen && _op is { } op && op.Index < op.Items.Count)
        {
            var o = BuildOverwrite(op); o.Key = "overwrite"; root.AddChild(o);
        }
        if (_optionsOpen)
        {
            var o = BuildOptions(); o.Key = "options"; root.AddChild(o);
        }
        if (_namePrompt is { } np)
        {
            var o = BuildNamePrompt(np); o.Key = "name-prompt"; root.AddChild(o);
        }
        return root;
    }

    protected override void OnAfterBuild()
    {
        SettleWrappedRowWidths();

        // Returning from a full-shell view: its Close ran while the diff/sync tree was
        // still up, so the pane it wants to focus only exists as of this build.
        if (_pendingPaneFocus is char paneSide)
        {
            _pendingPaneFocus = null;
            PaneRef(paneSide).Current?.Focus();
        }
        if (_pendingPaneScroll > 0 && !FullShellOpen) RestorePaneScroll();
        if (_pendingScrollL) { _pendingScrollL = false; ScrollCursorIntoView('L'); }
        if (_pendingScrollR) { _pendingScrollR = false; ScrollCursorIntoView('R'); }
        if (_pendingNamePromptFocus)
        {
            _pendingNamePromptFocus = false;
            _namePromptRef.Current?.Focus();
            if (_namePrompt is { SelectAll: true }) _namePromptRef.Current?.SelectAll();
        }
        if (_pendingFindFocus) { _pendingFindFocus = false; _findPatternRef.Current?.Focus(); }
        if (_pendingCmdFocus) { _pendingCmdFocus = false; _cmdRef.Current?.Focus(); }
        if (_pendingPackFocus) { _pendingPackFocus = false; _packNameRef.Current?.Focus(); }
        if (_pendingRenameFocus) { _pendingRenameFocus = false; _renNameRef.Current?.Focus(); }
        if (_pendingPwFocus) { _pendingPwFocus = false; _pwRef.Current?.Focus(); }
        if (_pendingDiffScroll)
        {
            if (_diffScrollRef.Current is { } sv && _diffChangeIdx < _diffChangeRows.Count)
            {
                // Stays pending when the answer isn't available yet — see DiffChangeTop.
                if (DiffChangeTop(sv) is { } top)
                {
                    _pendingDiffScroll = false;
                    sv.ScrollOffsetY = Math.Max(0, top - 2 * DiffLineH);
                }
            }
            else _pendingDiffScroll = false;
        }
        _viewerFind.Service();
        _diffFind.Service();
        if (_pendingFindScroll)
        {
            _pendingFindScroll = false;
            // Reveal the selected result. Small sets render in an inline ScrollView, large
            // sets in a VirtualList — only one exists per build, so try both. Fixed row
            // height ⇒ the selected band is index * RowHeight (no need to read the element).
            float top = _findSel * RowHeight;
            if (_findScrollRef.Current is { } fsv) fsv.ScrollRangeIntoView(top, RowHeight);
            else _findVList.Current?.ScrollRangeIntoView(top, RowHeight);
        }

        // Keep a browsing region focused so Tab traverses panes/preview predictably.
        // The framework consumes Tab for focus traversal; with nothing focused the
        // first Tab would just land on the already-active pane and appear to do nothing.
        // Skipped while a full-shell view owns the screen: the panes are unmounted there,
        // and a Ref keeps pointing at the element it last saw, so this would focus a
        // detached pane. Events dispatched to a detached element reach only that element —
        // they never bubble to the host's global handler — which left the diff and sync
        // views unable to see their own Esc.
        else if (!AnyOverlayOpen && !FullShellOpen && _window.FocusedElement is null)
            PaneRef(_active).Current?.Focus();
    }

    private void RequestScroll(char side)
    {
        if (side == 'L') _pendingScrollL = true; else _pendingScrollR = true;
    }

    private void ScrollCursorIntoView(char side)
    {
        // Brief scrolls horizontally by column; Detail and Thumbnails scroll vertically.
        if (ViewOf(side) == ViewMode.Brief) { ScrollBriefToCursor(side); return; }
        if (ViewOf(side) == ViewMode.Thumbnails) { ScrollThumbToCursor(side); return; }
        VList(side).Current?.ScrollRangeIntoView(Cursor(side) * RowHeight, RowHeight);
    }

    // ── Navigation ───────────────────────────────────────────────────────
    private void NavigateTo(char side, string path, string? focusChildName = null, bool recordHistory = true)
    {
        var tab = ActiveTab(side);
        // Record the folder we're leaving so Back can return to it. Skip on a no-op
        // (same folder) and on history replay (GoBack/GoForward set recordHistory:false).
        if (recordHistory && !PathEquals(tab.Dir, path))
        {
            tab.Back.Add(tab.Dir);
            if (tab.Back.Count > 64) tab.Back.RemoveAt(0);
            tab.Fwd.Clear();   // a fresh forward move invalidates the redo stack
        }
        tab.Dir = path;
        tab.Results = null;   // navigating anywhere leaves a fed-in result set behind
        BumpGen(side);
        // Cursor reset now; the loader's onLoaded re-clamps once the listing lands.
        // If we ascended, try to land on the folder we came from.
        SetCursor(side, 0);
        Marks(side).Clear();
        _search = "";
        _previewFocused = false;
        _pendingFocusChild[side == 'L' ? 0 : 1] = focusChildName;
        // Another folder is this pane's tab strip, trail, column header and footer and nothing
        // else's: the other pane, the rail and the title bar stay (a filter cleared or a pane
        // made active on the way here reach the parts that show them through MarkViewDirty).
        PaneDirty(side);
    }

    private bool CanGoBack(char side) => ActiveTab(side).Back.Count > 0;
    private bool CanGoForward(char side) => ActiveTab(side).Fwd.Count > 0;

    /// <summary>Alt+← — return to the folder we last left in this tab.</summary>
    private void GoBack(char side)
    {
        var tab = ActiveTab(side);
        if (tab.Back.Count == 0) return;
        string target = tab.Back[^1];
        tab.Back.RemoveAt(tab.Back.Count - 1);
        tab.Fwd.Add(tab.Dir);
        NavigateTo(side, target, recordHistory: false);
    }

    /// <summary>Alt+→ — redo a navigation undone by <see cref="GoBack"/>.</summary>
    private void GoForward(char side)
    {
        var tab = ActiveTab(side);
        if (tab.Fwd.Count == 0) return;
        string target = tab.Fwd[^1];
        tab.Fwd.RemoveAt(tab.Fwd.Count - 1);
        tab.Back.Add(tab.Dir);
        NavigateTo(side, target, recordHistory: false);
    }

    /// <summary>Ctrl+G — prompt for a path (relative or absolute) and jump the pane there.
    /// Opens on the pane's own folder, selected: typing replaces it, End appends to it.</summary>
    private void OpenGotoPrompt(char side)
    {
        _active = side;
        _namePromptValue = Dir(side);
        _namePromptError = null;
        _namePrompt = new NamePromptSpec("Go to path", Dir(side), "Go",
            path => GotoPath(side, path), Placeholder: "Absolute, or relative to this folder", SelectAll: true);
        _pendingNamePromptFocus = true;
        MarkDirty();
    }

    /// <summary>Resolve <paramref name="raw"/> and navigate <paramref name="side"/> to it.
    /// Relative paths (incl. "..") resolve against the pane's folder; "~" and %VAR% expand;
    /// a file target lands on the file in its parent; an archive target browses inside.
    /// Returns an error string (dialog stays open) or null on success.
    ///
    /// <para>A <i>remote</i> target is resolved off the UI thread instead (see
    /// <see cref="StartRemoteGoto"/>) and this returns null with the prompt left open: asking
    /// the filesystem about an unreachable host costs up to 21 seconds, and this runs from a
    /// key press. A local path answers in microseconds and keeps the straight line.</para></summary>
    private string? GotoPath(char side, string raw)
    {
        var input = raw.Trim().Trim('"');
        if (input.Length == 0) return "Enter a path.";

        string? resolved = ResolveTypedPath(side, input, out var resolveError);
        if (resolved == null) return resolveError;

        if (PathProbe.IsRemote(resolved))
        {
            StartRemoteGoto(side, resolved);
            return null;   // CommitNamePrompt sees the probe and holds the dialog open
        }
        return LandOn(side, resolved, PathProbe.LocalKind(resolved), () => ArchiveTargetExists(resolved));
    }

    /// <summary>How long a typed path gets to answer before the prompt offers to go anyway.
    /// Settable so a test need not wait it out.</summary>
    internal TimeSpan GotoProbeBudget = TimeSpan.FromSeconds(10);

    // Non-null while a typed remote path is being resolved: the prompt's busy state, and the
    // guard that keeps a second Enter from starting a second probe. Owned by the UI thread.
    private CancellationTokenSource? _gotoProbe;

    /// <summary>Resolve a remote target off the UI thread, then land on it. The prompt (or,
    /// from the command line, the notice line) reports whatever comes back.</summary>
    private void StartRemoteGoto(char side, string resolved)
    {
        var cts = new CancellationTokenSource();
        _gotoProbe = cts;
        _namePromptError = null;
        MarkDirty();
        _ = ProbeAndLand(side, resolved, cts);
    }

    private async Task ProbeAndLand(char side, string resolved, CancellationTokenSource cts)
    {
        PathKind kind;
        bool archive = false;
        try
        {
            kind = await PathProbe.KindAsync(resolved, GotoProbeBudget, cts.Token).ConfigureAwait(false);
            // The archive fallback stats the archive file, which is on the same share — off
            // the UI thread with the rest of it rather than on the way back.
            if (kind == PathKind.Missing)
                archive = await Task.Run(() => ArchiveTargetExists(resolved), cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { cts.Dispose(); return; }   // Esc — the close already tidied up
        catch { kind = PathKind.Missing; }

        HultUI.Application.InvokeOnMainThread(() =>
        {
            bool mine = ReferenceEquals(_gotoProbe, cts);
            cts.Dispose();
            if (!mine) return;          // cancelled, or superseded by a newer probe
            _gotoProbe = null;

            var err = LandOn(side, resolved, kind, () => archive);
            if (err != null)
            {
                // From the command line there is no dialog to hold the message.
                if (_namePrompt != null) _namePromptError = err; else _notice = err;
            }
            else if (_namePrompt != null) _namePrompt = null;
            MarkDirty();
        });
    }

    /// <summary>Act on what a typed path turned out to be. <paramref name="archiveTarget"/> is
    /// deferred because answering it stats the disk, and only the not-found branch asks.</summary>
    private string? LandOn(char side, string resolved, PathKind kind, Func<bool> archiveTarget)
    {
        switch (kind)
        {
            case PathKind.Directory:
                NavigateTo(side, resolved);
                return null;

            case PathKind.File:
                if (ArchiveVfs.IsArchiveFile(resolved)) { NavigateTo(side, resolved); return null; }
                // A plain file: land in its folder with the cursor on it.
                var parent = System.IO.Path.GetDirectoryName(resolved);
                if (parent == null) return $"Not a folder: {resolved}";
                NavigateTo(side, parent, focusChildName: System.IO.Path.GetFileName(resolved));
                return null;

            case PathKind.Unreachable:
            case PathKind.TimedOut:
                // The host never answered, so whether the folder is there is still unknown —
                // and a modal dialog is the worst place to wait to find out. Go, and let the
                // pane say "not available" with the Reconnect button already under the cursor.
                NavigateTo(side, resolved);
                return null;

            default:
                // Inside an archive the member dirs aren't on disk — navigate if the archive
                // file itself exists and let the listing surface any bad member path.
                if (archiveTarget()) { NavigateTo(side, resolved); return null; }
                return $"Path not found: {resolved}";
        }
    }

    /// <summary>True when the path descends into an archive file that is really there. Stats
    /// the disk — on a remote path, call it off the UI thread.</summary>
    private static bool ArchiveTargetExists(string resolved) =>
        ArchivePath.TryParse(resolved) is { } ap && File.Exists(ap.ArchiveFile);

    /// <summary>Turn a path the user typed into an absolute one, without asking whether it
    /// exists. "~" and %VAR% expand, a relative path (incl. "..") resolves against
    /// <paramref name="side"/>'s folder, and a bare drive letter means that drive's root —
    /// left alone it would resolve against the process's per-drive working directory, which
    /// is almost never the folder anyone means. Null with <paramref name="error"/> set when
    /// the text can't be made into a path at all.
    ///
    /// <para>Shared by Ctrl+G and the F5 / F6 destination box so both read a typed path the
    /// same way.</para></summary>
    private string? ResolveTypedPath(char side, string input, out string error)
    {
        error = "";
        try
        {
            var p = Environment.ExpandEnvironmentVariables(input);
            if (p == "~") p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            else if (p.StartsWith("~/") || p.StartsWith("~\\"))
                p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p[2..]);
            if (p.Length == 2 && p[1] == ':' && char.IsAsciiLetter(p[0]))
                p += System.IO.Path.DirectorySeparatorChar;
            return System.IO.Path.GetFullPath(p, Dir(side));
        }
        catch (Exception ex) { error = ex.Message; return null; }
    }

    /// <summary>Ctrl+Shift+C — put the pane's current folder path on the clipboard. Uses the
    /// window's text clipboard (cross-platform), so it works on Linux too.</summary>
    private void CopyCurrentPath(char side)
    {
        try { _window.Clipboard.SetText(Dir(side)); } catch { /* clipboard busy — ignore */ }
    }

    // Name to select once a pending listing finishes loading (used when ascending
    // to highlight the directory we came out of). Index 0 = L, 1 = R.
    private readonly string?[] _pendingFocusChild = new string?[2];

    /// <summary>Enter/open the cursor entry: descend into a directory, ascend on "..".</summary>
    private void ActivateCursor(char side)
    {
        var list = Rows(side);
        int cur = Cursor(side);
        if (cur < 0 || cur >= list.Count) return;
        var e = list[cur];

        if (e.IsParent)
        {
            // In a fed-in result set ".." is the way out, not a folder above this one.
            if (ExitResults(side)) return;
            var leaving = new DirectoryInfo(Dir(side)).Name;
            NavigateTo(side, e.FullPath, focusChildName: leaving);
        }
        else if (e.IsDir)
        {
            NavigateTo(side, e.FullPath);
        }
        else if (ArchiveVfs.IsArchiveFile(e.FullPath) && (File.Exists(e.FullPath) || InArchive(side)))
        {
            NavigateTo(side, e.FullPath);   // browse into the archive — incl. a nested one (a member)
        }
        else
        {
            FileOps.OpenInShell(e.FullPath);   // open with the OS default handler
        }
    }

    /// <summary>F4 — open the focused file in its configured external editor. Images use
    /// the ImageEditor template (Paint by default), everything else the Editor template
    /// (VS Code by default). Directories and ".." are ignored.</summary>
    private void EditFocused(char side)
    {
        var list = Rows(side);
        int cur = Cursor(side);
        if (cur < 0 || cur >= list.Count) return;
        var e = list[cur];
        if (e.IsDir || e.IsParent) return;
        string template = Icons.KindFor(e) == IconKind.Image ? _settings.ImageEditor : _settings.Editor;
        // An archive member has no file on disk to hand over: it is unpacked first, and offered
        // back to the archive when Vesper next comes to the foreground.
        if (ArchivePath.InArchive(e.FullPath)) { EditArchiveMember(e, template); return; }
        FileOps.LaunchEditor(template, e.FullPath);
    }

    private void MoveCursor(char side, int delta, bool clearSearch = false)
    {
        if (clearSearch) _search = "";   // arrows keep the filter; nav within the matches
        var list = Rows(side);
        if (list.Count == 0) return;
        // A Shift sweep anchors before the move, so the range covers the row the cursor
        // started on as well as the one it lands on.
        if (_shiftMarking) BeginMarkSweep(side, list);
        int next = Math.Clamp(Cursor(side) + delta, 0, list.Count - 1);
        // The cursor is the view's alone: the parts stay (a sweep's marks reach the footer, and a
        // cleared filter the footer and the command line, through MarkViewDirty's compare).
        if (next == Cursor(side))
        {
            // Already at the end of the list: the row under the cursor still joins the
            // range, so Shift+End on the last row marks it rather than doing nothing.
            if (_shiftMarking) ApplyMarkSweep(side, list);
            MarkViewDirty();
            return;
        }
        SetCursor(side, next);
        if (_shiftMarking) ApplyMarkSweep(side, list);
        RequestScroll(side);
        MarkViewDirty();
    }

    private static int IndexOfName(IReadOnlyList<FileEntry> list, string name)
    {
        for (int i = 0; i < list.Count; i++)
            if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>The row holding <paramref name="path"/>. Used wherever a row has to be found
    /// again after the list was rebuilt or recycled — a fed-in result set lists many folders
    /// at once, so a name can name several rows and only the path names one.</summary>
    private static int IndexOfPath(IReadOnlyList<FileEntry> list, string? path)
    {
        if (path == null) return -1;
        for (int i = 0; i < list.Count; i++)
            if (PathEquals(list[i].FullPath, path)) return i;
        return -1;
    }

    private int PageSize(char side)
    {
        var h = VList(side).Current?.Layout.AbsoluteContentBox.Height ?? (10 * RowHeight);
        return Math.Max(1, (int)(h / RowHeight) - 1);
    }

    // ── Marking ──────────────────────────────────────────────────────────

    /// <summary>True only while a Shift+navigation key is moving the cursor, which is what
    /// tells <see cref="MoveCursor"/> to drag the marks along behind it.</summary>
    private bool _shiftMarking;

    /// <summary>The navigation keys Shift extends a mark range with. Left/Right are in the
    /// list for the grid views, where they are motion; in Detail they mean something else
    /// entirely and <see cref="ShiftNav"/> declines them.</summary>
    private static bool IsMarkNavKey(KeyCode key) =>
        key is KeyCode.Up or KeyCode.Down or KeyCode.Left or KeyCode.Right
            or KeyCode.PageUp or KeyCode.PageDown or KeyCode.Home or KeyCode.End;

    /// <summary>
    /// Shift + a navigation key: move the cursor and mark everything between where the
    /// sweep began and where it now is. Returns false for keys this pane's view does not
    /// treat as motion, which then go on to mean whatever they usually mean.
    /// </summary>
    private bool ShiftNav(char side, KeyCode key)
    {
        _shiftMarking = true;
        try
        {
            if (ViewNav(side, key)) return true;   // Brief / Thumbnail: column and grid motion
            switch (key)
            {
                case KeyCode.Down: MoveCursor(side, 1); return true;
                case KeyCode.Up: MoveCursor(side, -1); return true;
                case KeyCode.PageDown: MoveCursor(side, PageSize(side)); return true;
                case KeyCode.PageUp: MoveCursor(side, -PageSize(side)); return true;
                case KeyCode.Home: MoveCursor(side, int.MinValue / 2); return true;
                case KeyCode.End: MoveCursor(side, int.MaxValue / 2); return true;
                default: return false;             // Left/Right in Detail are not motion
            }
        }
        finally { _shiftMarking = false; }
    }

    /// <summary>
    /// Remembers where a sweep started: the row the cursor sits on, and the marks that were
    /// already set. Both are needed to re-derive the answer from scratch on every step, which
    /// is what lets the range shrink again when the user turns around — a sweep that only
    /// ever added marks could not take them back, and one that cleared everything would
    /// throw away marks made before the sweep began. Does nothing once a sweep is running.
    /// </summary>
    private void BeginMarkSweep(char side, IReadOnlyList<FileEntry> list)
    {
        var tab = ActiveTab(side);
        if (tab.MarkAnchor != null && IndexOfPath(list, tab.MarkAnchor) >= 0) return;
        int cur = Cursor(side);
        if (cur < 0 || cur >= list.Count) return;
        tab.MarkAnchor = list[cur].FullPath;
        tab.MarkBase = PathSet();
        foreach (var key in tab.Marks) tab.MarkBase.Add(key);
    }

    /// <summary>Marks = what was marked before the sweep, plus every row between the anchor
    /// and the cursor. The ".." row never takes a mark, here as everywhere else.</summary>
    private void ApplyMarkSweep(char side, IReadOnlyList<FileEntry> list)
    {
        var tab = ActiveTab(side);
        if (tab.MarkBase is null) return;
        int anchor = IndexOfPath(list, tab.MarkAnchor);
        int cur = Cursor(side);
        if (anchor < 0 || cur < 0 || cur >= list.Count) return;

        tab.Marks.Clear();
        foreach (var key in tab.MarkBase) tab.Marks.Add(key);
        for (int i = Math.Min(anchor, cur); i <= Math.Max(anchor, cur); i++)
            if (!list[i].IsParent) tab.Marks.Add(MarkKey(list[i]));
    }

    private void EndMarkSweep(char side)
    {
        var tab = ActiveTab(side);
        tab.MarkAnchor = null;
        tab.MarkBase = null;
    }

    /// <summary>Toggle the mark on the focused row (never the ".." parent). When
    /// <paramref name="advance"/> (Insert), move the cursor down one afterwards.</summary>
    private void ToggleMark(char side, bool advance)
    {
        var list = Rows(side);
        int cur = Cursor(side);
        if (cur < 0 || cur >= list.Count) return;
        var e = list[cur];
        EndMarkSweep(side);   // a deliberate toggle is a new starting point, not part of a range
        if (!e.IsParent)
        {
            var marks = Marks(side);
            if (!marks.Remove(MarkKey(e))) marks.Add(MarkKey(e));
        }
        // The move builds the window, the mark already in it: a MarkDirty after it built the
        // window a second time for every Insert. A mark shows in the rows and the footer's
        // count, which MarkViewDirty compares.
        if (advance) MoveCursor(side, 1, clearSearch: false);
        else MarkViewDirty();
    }

    /// <summary>Ctrl+A: mark every row in the pane (never the ".." parent) — or, when
    /// every row is already marked, unmark them all (the toggle every editor's
    /// select-all-again teaches). While a type-to-find filter is active, only the rows it
    /// shows are marked or unmarked, so marks hidden by the filter survive the toggle.</summary>
    private void MarkAll(char side)
    {
        var marks = Marks(side);
        var rows = Rows(side).Where(e => !e.IsParent).ToList();
        if (rows.Count == 0) return;
        EndMarkSweep(side);
        bool allMarked = rows.All(e => marks.Contains(MarkKey(e)));
        foreach (var e in rows)
        {
            if (allMarked) marks.Remove(MarkKey(e));
            else marks.Add(MarkKey(e));
        }
        MarkViewDirty();   // the rows and the footer's count (compared)
    }

    /// <summary>Ctrl+Shift+A: prompt for a pattern and mark the files it matches.</summary>
    private void OpenMarkPatternPrompt(char side)
    {
        if (AnyOverlayOpen) return;
        _namePromptValue = "";
        _namePromptError = null;
        _namePrompt = new NamePromptSpec("Mark files by pattern", Dir(side), "Mark",
            p => MarkByPattern(side, p), Placeholder: "e.g. *.txt — wildcards * and ?");
        _pendingNamePromptFocus = true;
        MarkDirty();
    }

    /// <summary>Mark the files matching <paramref name="raw"/> — additive, like marking by
    /// hand, and files only (a pattern speaks about files; folders stay untouched). Matching
    /// is the find dialog's: glob with wildcards, extension filter for a leading dot,
    /// substring otherwise. Returns an error string (prompt stays open) or null.</summary>
    private string? MarkByPattern(char side, string raw)
    {
        var pattern = raw.Trim();
        if (pattern.Length == 0) return "Enter a pattern.";
        var opts = new FindEngine.Options(pattern, null, Recursive: false, MatchCase: false,
            Regex: false, Hidden: false);
        var marks = Marks(side);
        int matched = 0;
        foreach (var e in Rows(side))
        {
            if (e.IsParent || e.IsDir) continue;
            if (!FindEngine.NameMatches(e.Name, opts)) continue;
            marks.Add(MarkKey(e));
            matched++;
        }
        if (matched == 0) return $"No files here match \"{pattern}\"";
        MarkDirty();
        return null;
    }

    // ── Right-drag marking ───────────────────────────────────────────────
    // Press the RIGHT button on a row and drag: every row the pointer crosses is
    // marked (or unmarked). The first row sets the direction — if it was unmarked we
    // add marks, otherwise we remove them — so a right-drag over a mixed range makes
    // it uniform, matching Total Commander. Rows recycle, so we track by path and
    // handle each row once per drag; the release is caught at the root (below).
    private bool _rmarkActive;
    // When the move handlers on the root and on each row act (see OnPassive): made once, so a
    // build hands every row the same one.
    private Func<bool>? _rootMoveListening, _rowMoveListening;
    private char _rmarkSide;
    private bool _rmarkAdd;
    private readonly HashSet<string> _rmarkSeen = PathSet();

    private void BeginRightMark(char side, FileEntry e)
    {
        _active = side;
        _rmarkActive = true;
        _rmarkSide = side;
        _rmarkSeen.Clear();
        _rmarkAdd = !Marks(side).Contains(MarkKey(e));   // direction from the first row
        ApplyRightMark(side, e);
    }

    private void ApplyRightMark(char side, FileEntry e)
    {
        if (!_rmarkActive || side != _rmarkSide || e.IsParent) return;
        if (!_rmarkSeen.Add(MarkKey(e))) return;     // each row toggles once per drag
        var marks = Marks(side);
        if (_rmarkAdd) marks.Add(MarkKey(e)); else marks.Remove(MarkKey(e));
        MarkViewDirty();   // the rows and the footer's count (compared)
    }

    private void EndRightMark()
    {
        if (!_rmarkActive) return;
        _rmarkActive = false;
        _rmarkSeen.Clear();
    }

    /// <summary>TextMate (Dark+) spans for the preview excerpt, or null while they're still
    /// being tokenized (or the language is unsupported) — the caller then renders the excerpt
    /// plain. Tokenizing runs on a thread-pool thread so it never blocks scrolling; the result
    /// is applied on the UI thread and only if the excerpt is still the current one.
    /// <paramref name="name"/> selects the grammar by extension.</summary>
    private IReadOnlyList<TextSpan>? PreviewSpans(string name, string excerpt)
    {
        if (ReferenceEquals(excerpt, _previewSpanExcerpt))
            return _previewSpans;   // already tokenized (or in flight → still null)

        _previewSpanExcerpt = excerpt;
        _previewSpans = null;
        System.Threading.Tasks.Task.Run(() =>
        {
            var spans = HultUI.Syntax.SyntaxHighlighter.SpansForText(name, excerpt);
            Application.InvokeOnMainThread(() =>
            {
                if (!ReferenceEquals(excerpt, _previewSpanExcerpt)) return;   // user moved on
                _previewSpans = spans;
                MarkViewDirty();   // repaint the excerpt with its colors
            });
        });
        return null;
    }

    // ── Folder size measure (Space) ──────────────────────────────────────
    /// <summary>Measured (or so-far) total for a Space-measured folder plus whether the
    /// walk has finished, or null when it hasn't been measured. Read while rendering.</summary>
    private (long bytes, bool done)? MeasuredDir(FileEntry e) =>
        e.IsDir && !e.IsParent && _dirSizes.TryGetValue(e.FullPath, out var ds)
            ? (ds.Bytes, ds.Done) : null;

    /// <summary>Kick off the recursive size walk for a folder. The result is cached, so
    /// a folder that's already measured (or being measured) is left alone — re-pressing
    /// Space to unmark it doesn't recompute. A refresh / content change clears the cache.</summary>
    private void StartDirSize(FileEntry e)
    {
        if (!e.IsDir || e.IsParent) return;
        if (_dirSizes.ContainsKey(e.FullPath)) return;   // cached (running or done)
        var ds = new DirSize();
        _dirSizes[e.FullPath] = ds;
        string path = e.FullPath;
        System.Threading.Tasks.Task.Run(() =>
        {
            long total = FileSystem.DirectorySize(path,
                cancelled: () => ds.Cancelled,
                report: n => { ds.Set(n); Repaint(sizes: true); });
            ds.Set(total);
            ds.Done = true;
            Application.InvokeOnMainThread(MarkDirty);
        });
    }

    /// <summary>Space over a folder measures its size (Total Commander style — the mark
    /// toggles too); over a file it just marks.</summary>
    private void MeasureFocusedDir(char side)
    {
        var list = Rows(side);
        int cur = Cursor(side);
        if (cur >= 0 && cur < list.Count) StartDirSize(list[cur]);
    }

    /// <summary>Alt+Shift+Enter: measure every folder in the active pane at once. Each
    /// gets its own cached background walk (already-measured folders are skipped).</summary>
    private void MeasureAllDirs(char side)
    {
        foreach (var e in Listing(side)) StartDirSize(e);
        MarkDirty();
    }

    /// <summary>Drop cached folder measurements for the direct children of
    /// <paramref name="dir"/> (any in-flight walk is cancelled), so a refresh or a
    /// content change re-measures on the next Space instead of showing a stale total.</summary>
    private void ClearMeasuredIn(string dir)
    {
        if (_dirSizes.Count == 0) return;
        var stale = _dirSizes.Keys
            .Where(k => PathEquals(System.IO.Path.GetDirectoryName(k), dir)).ToList();
        foreach (var k in stale) { _dirSizes[k].Cancelled = true; _dirSizes.Remove(k); }
    }

    /// <summary>The entries a file operation targets: the mark set, or the focused
    /// row when nothing is marked (never the ".." parent).</summary>
    private IReadOnlyList<FileEntry> OperationTargets(char side)
    {
        // Marks act on the full listing (a marked entry hidden by the filter still counts);
        // the cursor fallback indexes the filtered view.
        var marks = Marks(side);
        if (marks.Count > 0)
            return Listing(side).Where(e => !e.IsParent && marks.Contains(MarkKey(e))).ToList();
        var rows = Rows(side);
        int cur = Cursor(side);
        if (cur >= 0 && cur < rows.Count && !rows[cur].IsParent) return [rows[cur]];
        return [];
    }

    // ── Clipboard (Ctrl+C / Ctrl+X / Ctrl+V) ─────────────────────────────
    // Full paths of files cut and awaiting paste — rendered ghosted in their pane
    // until paste, Esc, or a new cut/copy clears them. Cut interops with Explorer.
    private readonly HashSet<string> _cutPending = new(StringComparer.OrdinalIgnoreCase);

    private void CopyToClipboard(bool cut)
    {
        var items = OperationTargets(_active);
        if (items.Count == 0) return;
        var paths = items.Select(e => e.FullPath).ToList();
        Clipboard.SetFiles(paths, cut);
        _cutPending.Clear();
        if (cut) foreach (var p in paths) _cutPending.Add(p);
        MarkDirty();
    }

    private void PasteFromClipboard()
    {
        if (!Clipboard.TryGetFiles(out var paths, out bool cut) || paths.Count == 0) return;
        char side = _active;
        if (LockedBlock(side, "paste into a locked tab") || ResultsBlock(side)) return;
        bool intoArchive = InArchive(side);
        if (intoArchive && !CanAddToArchive(side)) return;
        var dest = Dir(side);
        var items = new List<FileEntry>();
        foreach (var p in paths)
        {
            if (!FileOps.Exists(p)) continue;
            // The same-folder check is a disk one: an archive's contents are never the clipboard
            // paths' own folder, so it has nothing to say about a paste going into one.
            if (!intoArchive && PathEquals(System.IO.Path.GetDirectoryName(p) ?? "", dest)) continue;
            items.Add(FileOps.EntryFromPath(p));
        }
        if (items.Count == 0) return;

        if (intoArchive)
        {
            StartArchiveAdd(side, Dir(side), items, move: cut);
            if (cut) { Clipboard.Clear(); _cutPending.Clear(); }
            return;
        }

        var op = new TransferOp(items) { IsMove = cut, Source = side, DestDir = dest };
        _op = op;
        _progressOpen = true;
        MarkDirty();
        System.Threading.Tasks.Task.Run(() => RunTransfer(op));
        if (cut)
        {
            Clipboard.Clear();             // a cut is consumed by its paste
            _cutPending.Clear();
        }
    }

    // ── Type-to-find filter ──────────────────────────────────────────────
    // Typing in a pane narrows it to the matching entries (a live filter, so every match is
    // visible at once). Arrows move within the filtered set; Esc clears and restores the
    // cursor to where it was. Only the active pane filters (the search text is per-focus).
    // The filter narrows the active pane's rows, and shows in its footer's counts and in the
    // command line, which MarkViewDirty compares: the headers, the rail and the title bar stay.
    private void ExtendSearch(char side, char ch)
    {
        int anchor = CursorListingPos(side);
        _search += ch;
        ReclampFilterCursor(side, anchor);
        RequestScroll(side);
        MarkViewDirty();
    }

    private void BackspaceSearch(char side)
    {
        if (_search.Length == 0) return;
        int anchor = CursorListingPos(side);
        _search = _search[..^1];
        ReclampFilterCursor(side, anchor);
        RequestScroll(side);
        MarkViewDirty();
    }

    /// <summary>Clear the filter, moving the cursor back onto the same entry in the full
    /// listing so you keep your place.</summary>
    private void ExitSearch(char side)
    {
        if (_search.Length == 0) return;
        string? keep = CursorEntryPath(side);
        _search = "";
        int i = IndexOfPath(Listing(side), keep);
        if (i >= 0) SetCursor(side, i);
        RequestScroll(side);
    }

    private bool SearchMatch(string name) => FilterMatch(name, _search);

    /// <summary>True when <paramref name="name"/> matches the filter <paramref name="search"/>:
    /// a substring by default, or the file extension when the text starts with "." (".cs" →
    /// *.cs). An empty filter matches everything.</summary>
    internal static bool FilterMatch(string name, string search)
    {
        if (search.Length == 0) return true;
        if (search.Length > 1 && search[0] == '.')
            return ExtensionOf(name).StartsWith(search, StringComparison.OrdinalIgnoreCase);
        return name.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The entries shown for a side: the full listing, or — while a filter is active
    /// in the active pane — only the matches (".." is always kept so you can still go up).
    /// Cursor, navigation, rendering, and the footer all index this.</summary>
    private IReadOnlyList<FileEntry> Rows(char side)
    {
        var raw = Listing(side);
        if (side != _active || _search.Length == 0) return raw;
        var filtered = new List<FileEntry>(raw.Count);
        foreach (var e in raw) if (e.IsParent || SearchMatch(e.Name)) filtered.Add(e);
        return filtered;
    }

    /// <summary>The full path of the row under the cursor — the row's identity everywhere it
    /// has to be found again (see <see cref="IndexOfPath"/>).</summary>
    private string? CursorEntryPath(char side)
    {
        var rows = Rows(side);
        int c = Cursor(side);
        return c >= 0 && c < rows.Count ? rows[c].FullPath : null;
    }

    /// <summary>The cursor's index in the *full* listing (not the filtered view), so a filter
    /// change can anchor the new cursor to where you actually were. 0 when the cursor is off
    /// the list.</summary>
    private int CursorListingPos(char side)
    {
        int i = IndexOfPath(Listing(side), CursorEntryPath(side));
        return i < 0 ? 0 : i;
    }

    /// <summary>After the filter changes, move the cursor to the first match at or after where
    /// it was (<paramref name="anchor"/> is the old position in the full listing) — so typing a
    /// filter walks forward from your place instead of snapping to the top or jumping up to a
    /// match above you. If the current entry still matches it's that first match, so it stays.
    /// When every match is above the anchor, fall back to the nearest one.</summary>
    private void ReclampFilterCursor(char side, int anchor)
    {
        var full = Listing(side);
        int firstAtOrAfter = -1, lastBefore = -1, row = -1;
        for (int i = 0; i < full.Count; i++)
        {
            var e = full[i];
            if (!(e.IsParent || SearchMatch(e.Name))) continue;   // not in the filtered view
            row++;                                                 // its index within Rows(side)
            if (e.IsParent) continue;                              // ".." is never a landing target
            if (i >= anchor) { firstAtOrAfter = row; break; }
            lastBefore = row;
        }
        int idx = firstAtOrAfter >= 0 ? firstAtOrAfter : Math.Max(0, lastBefore);
        int count = Rows(side).Count;
        SetCursor(side, Math.Clamp(idx, 0, Math.Max(0, count - 1)));
    }

    /// <summary>The extension including the leading dot (".zip"), or "" when the name has none.
    /// A dotfile (".gitignore") counts as its own extension.</summary>
    internal static string ExtensionOf(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[dot..];
    }

    /// <summary>Map a KeyDown (with its Shift state) to the character it contributes to the
    /// incremental search, or '\0' when the key isn't a printable name character. Symbols use
    /// the US-QWERTY shifted layout — the same assumption the fixed-symbol shortcuts already
    /// make — so Shift+8 types '*' (a wildcard) rather than '8'.</summary>
    internal static char SearchChar(KeyCode key, bool shift) => key switch
    {
        >= KeyCode.A and <= KeyCode.Z => (char)('a' + (key - KeyCode.A)),
        >= KeyCode.Digit0 and <= KeyCode.Digit9 => shift ? ")!@#$%^&*("[key - KeyCode.Digit0] : (char)('0' + (key - KeyCode.Digit0)),
        KeyCode.Period => shift ? '>' : '.',
        KeyCode.Comma => shift ? '<' : ',',
        KeyCode.Minus => shift ? '_' : '-',
        KeyCode.Equals => shift ? '+' : '=',
        KeyCode.Slash => shift ? '?' : '/',
        KeyCode.Backslash => shift ? '|' : '\\',
        KeyCode.Semicolon => shift ? ':' : ';',
        KeyCode.Quote => shift ? '"' : '\'',
        KeyCode.BracketLeft => shift ? '{' : '[',
        KeyCode.BracketRight => shift ? '}' : ']',
        KeyCode.Backtick => shift ? '~' : '`',
        _ => '\0',
    };

    // ── Overlay open/close ───────────────────────────────────────────────
    private void OpenDrivePicker(char side)
    {
        _drives = FileSystem.Drives();
        _drivePicker = side;
        _driveCur = Math.Max(0, IndexOfDrive(Dir(side)));
        MarkDirty();
    }

    private int IndexOfDrive(string dir) => Math.Max(0, VolumeIndexOf(dir));

    /// <summary>
    /// The listed volume <paramref name="path"/> is on: the longest drive root that contains it,
    /// or -1. A volume is the mount a path is under, not its path root — on Linux every root is
    /// "/", so picking "/" followed the other pane onto whatever mount it was on, and picking the
    /// mount it really was on never did; Windows mounts volumes in folders too. A path no listed
    /// root contains (an unmapped share) falls back to its path root.
    /// </summary>
    private int VolumeIndexOf(string path)
    {
        int best = -1, bestLength = -1;
        for (int i = 0; i < _drives.Count; i++)
        {
            var root = _drives[i].Root;
            if (PathProbe.IsUnder(path, root) && root.Length > bestLength) { best = i; bestLength = root.Length; }
        }
        if (best >= 0) return best;
        var pathRoot = System.IO.Path.GetPathRoot(path);
        for (int i = 0; i < _drives.Count; i++)
            if (PathEquals(_drives[i].Root, pathRoot)) return i;
        return -1;
    }


    private bool AnyOverlayOpen => _drivePicker != null || _palette is { IsOpen: true } || _bookmarksOpen
        || _confirm != null || _overwriteOpen || _progressOpen || _optionsOpen || _namePrompt != null
        || _findOpen || _packOpen || _pwOpen || _renameOpen || _historyPopover is { IsOpen: true };

    private void CloseOverlays()
    {
        _drivePicker = null;
        _palette?.Close();
        _bookmarksOpen = false;
        _confirm = null;
        _optionsOpen = false;
        _optSearch = "";
        _namePrompt = null;
        _packOpen = false;
        _pwOpen = false;
        _renameOpen = false;
        _historyPopover?.Close();
        if (_findOpen) { _findOpen = false; StopFind(); PersistFindDefaults(); }
        MarkDirty();
    }

    // ── File operations: copy (F5) / move (F6) / delete (F8) ─────────────
    /// <summary>Selection wording shared by the dialogs: an unmarked focus row shows
    /// its quoted name; a mark set shows "N item(s)".</summary>
    private string SelectionLabel(char side)
    {
        var targets = OperationTargets(side);
        if (Marks(side).Count == 0)
            return targets.Count == 1 ? $"\"{targets[0].Name}\"" : "the selection";
        return Marks(side).Count == 1 ? "1 item" : $"{Marks(side).Count} items";
    }

    /// <summary>True (and posts a transient notice) when the given pane's active tab is
    /// locked read-only — gates the write ops (delete/rename/new-folder/copy/move/paste).</summary>
    private bool LockedBlock(char side, string action)
    {
        if (!ActiveTab(side).Locked) return false;
        _notice = $"Tab is locked (read-only) — can't {action}";
        MarkDirty();
        return true;
    }

    /// <summary>The items a copy / move should actually carry to <paramref name="dest"/>.
    ///
    /// <para>From a folder that is the same thing as the whole selection, and copying a folder
    /// onto itself is a no-op the pane refuses outright. From a fed-in result set there is no
    /// single source folder — the rows come from many — so the rule applies per row instead:
    /// a hit that already lives in the destination is dropped, and the rest go. Without that,
    /// a move would take a file's own folder as its target and (with overwrite) recycle the
    /// file on the way to replacing it with itself.</para></summary>
    private List<FileEntry> TransferItemsFrom(char side, List<FileEntry> items, string dest)
    {
        if (!InResults(side))
            return PathEquals(dest, Dir(side)) ? [] : items;
        var kept = items.Where(e => !PathEquals(System.IO.Path.GetDirectoryName(e.FullPath), dest)).ToList();
        if (kept.Count == 0 && items.Count > 0)
        {
            _notice = "Those results are already in the other pane's folder";
            MarkDirty();
        }
        return kept;
    }

    // ── Move (F6): synchronous confirm + transfer (moves are usually instant) ──
    private void StartMove()
    {
        char side = _active, other = side == 'L' ? 'R' : 'L';
        var items = OperationTargets(side).ToList();
        if (items.Count == 0) return;
        var dest = Dir(other);
        items = TransferItemsFrom(side, items, dest);
        if (items.Count == 0) return;
        if (LockedBlock(side, "move out of a locked tab") || LockedBlock(other, "move into a locked tab")) return;
        // Moving *out* of an archive is still refused: it would mean extracting and then deleting
        // members, two writes to different places, and F5 already copies out.
        if (InArchiveBlock(side)) return;
        if (ResultsBlock(other)) return;                             // nothing can be moved *into* results

        // An archive destination is not special-cased here any more: it is just a path the
        // dialog can be pointed at, and CommitTransfer routes it to the add worker.
        OpenTransferPrompt(side, items, dest, move: true);
    }

    // ── The F5 / F6 destination dialog ───────────────────────────────────
    // Both keys stop here before anything is written. The other pane's folder is filled in,
    // so Enter still means what the key has always meant — but it is a text field, which is
    // the point: a copy can be aimed somewhere neither pane is standing without walking a
    // pane there first. Move used to show a fixed yes/no confirm and copy showed nothing at
    // all; they are one dialog now with a different verb.
    //
    // The path is read the same way Ctrl+G reads one (ResolveTypedPath), so "..", "~",
    // %TEMP% and a relative name all work, resolved against the *source* pane's folder.

    // The exact destination the last confirm offered to create. A missing folder takes two
    // confirms rather than one: a typo in a path is a quiet way to scatter empty folders
    // across a disk, and the second press costs nothing when the folder really is new.
    private string? _transferCreateOffered;

    private void OpenTransferPrompt(char side, List<FileEntry> items, string dest, bool move, bool extract = false)
    {
        string verb = extract ? "Extract" : move ? "Move" : "Copy";
        string label = items.Count == 1 ? $"\"{items[0].Name}\"" : $"{items.Count} items";
        _namePromptValue = dest;
        _namePromptError = null;
        _transferCreateOffered = null;
        _namePrompt = new NamePromptSpec(
            $"{verb} {label}",
            $"from {Dir(side)}",
            verb,
            target => CommitTransfer(side, items, target, move, extract),
            Placeholder: "Destination folder");
        _pendingNamePromptFocus = true;
        MarkDirty();
    }

    /// <summary>Commit the F5 / F6 dialog: resolve what was typed, then start the transfer.
    /// Returns an error string (the dialog stays open showing it) or null on success.</summary>
    private string? CommitTransfer(char side, List<FileEntry> items, string rawTarget, bool move, bool extract)
    {
        var typed = rawTarget.Trim().Trim('"');
        if (typed.Length == 0) return "Enter a destination folder.";
        if (ResolveTypedPath(side, typed, out var resolveError) is not { } dest) return resolveError;

        // Into an archive: a different backend (it repacks rather than writing files), so the
        // dialog hands off to the add worker instead of starting a transfer. Extracting there
        // is refused — the add writers read their sources from disk, and an archive member has
        // no disk file, so archive-to-archive would have to stage through a temp copy.
        if (ArchivePath.InArchive(dest))
        {
            if (extract) return "Extract to a real folder — one archive can't be poured into another.";
            if (ArchiveAddBlock(dest) is { } why) return why;
            StartArchiveAdd(side, dest, items, move);
            return null;
        }
        if (!extract && PathEquals(dest, Dir(side)) && !InResults(side))
            return items.Count == 1 ? "That is where it already is." : "That is where they already are.";

        if (!Directory.Exists(dest))
        {
            if (!PathEquals(_transferCreateOffered, dest))
            {
                _transferCreateOffered = dest;
                return $"{dest} doesn't exist — {(extract ? "Extract" : move ? "Move" : "Copy")} again to create it.";
            }
            try { Directory.CreateDirectory(dest); }
            catch (Exception ex) { return FileOps.Clean(ex.Message); }
        }

        if (extract) { StartArchiveTransfer(items, dest, side); return null; }

        items = TransferItemsFrom(side, items, dest);
        if (items.Count == 0) return "Nothing left to send to that folder.";

        var op = new TransferOp(items) { IsMove = move, Source = side, DestDir = dest };
        _op = op;
        _progressOpen = true;
        MarkDirty();
        System.Threading.Tasks.Task.Run(() => RunTransfer(op));
        return null;
    }

    // ── Copy (F5): background streamed transfer with the progress overlay ──
    private void StartCopy()
    {
        if (InArchive(_active)) { StartExtract(); return; }   // F5 inside an archive = extract-out
        char side = _active, other = side == 'L' ? 'R' : 'L';
        var items = OperationTargets(side).ToList();
        if (items.Count == 0) return;
        var dest = Dir(other);
        items = TransferItemsFrom(side, items, dest);
        if (items.Count == 0) return;

        if (LockedBlock(other, "copy into a locked tab")) return;
        if (ResultsBlock(other)) return;
        // Copying *into* an archive used to skip the dialog and start packing on the keypress —
        // the one destination F5 never asked about. It is just a path now; CommitTransfer sees
        // it is inside an archive and routes to the add worker, and the archives that can't
        // take one (rar, or a format needing 7-Zip) say so in the dialog.
        OpenTransferPrompt(side, items, dest, move: false);
    }

    /// <summary>The copy/move worker — runs off the UI thread, reporting progress and
    /// blocking on the overwrite gate when it needs a decision. A same-volume move is an
    /// instant rename; a cross-volume move streams the copy then deletes the source.</summary>
    private void RunTransfer(TransferOp op)
    {
        // Size the queue up front so the total bar is meaningful.
        for (int i = 0; i < op.Items.Count; i++) op.ItemBytes[i] = FileOps.EntrySize(op.Items[i]);
        op.TotalBytes = op.ItemBytes.Sum();
        op.StartTick = Environment.TickCount64;
        Repaint();

        for (op.Index = 0; op.Index < op.Items.Count && !op.Cancelled; op.Index++)
        {
            int i = op.Index;
            var e = op.Items[i];
            op.Status[i] = 1;                     // active
            op.FileBytes = op.ItemBytes[i];
            op.FileBytesDone = 0;
            op.SkipCurrent = false;
            Repaint();
            WaitWhilePaused(op);

            var destPath = System.IO.Path.Combine(op.DestDir, e.Name);
            bool exists = FileOps.Exists(destPath);
            bool overwrite = false;

            if (exists)
            {
                // Resolve the conflict per the setting; only "Ask" prompts.
                if (_settings.Conflict == ConflictMode.KeepNewer)
                {
                    overwrite = FileOps.SourceNewer(e.FullPath, destPath);
                    if (!overwrite) { op.Status[i] = 3; op.Skipped++; op.TotalBytesDone += op.ItemBytes[i]; Repaint(); continue; }
                }
                else if (_settings.Conflict == ConflictMode.AutoRename)
                {
                    e = e with { Name = FileOps.UniqueName(op.DestDir, e.Name) };
                    overwrite = false;                       // copying under a fresh name
                }
                else
                {
                    if (op.OverwriteAll == null)
                    {
                        // Pause and ask; the UI releases the gate with the choice.
                        // Re-surface the progress overlay too (in case it was backgrounded).
                        op.Gate = new ManualResetEventSlim(false);
                        Application.InvokeOnMainThread(() => { _progressOpen = true; _overwriteOpen = true; MarkDirty(); });
                        op.Gate.Wait();
                        op.Gate.Dispose(); op.Gate = null;
                        if (op.Cancelled) break;
                        overwrite = op.ConflictOverwrite;
                    }
                    else overwrite = op.OverwriteAll.Value;

                    if (!overwrite)
                    {
                        op.Status[i] = 3; op.Skipped++;          // skipped
                        op.TotalBytesDone += op.ItemBytes[i];    // keep the total bar honest
                        Repaint();
                        continue;
                    }
                }
            }

            string? err;
            if (op.IsMove && FileOps.SameVolume(e.FullPath, destPath))
            {
                // Same-volume move: an atomic rename, no byte streaming — fill the bar.
                err = FileOps.Move(e, op.DestDir, overwrite);
                if (err == null) { op.FileBytesDone = op.FileBytes; op.TotalBytesDone += op.ItemBytes[i]; }
            }
            else
            {
                err = FileOps.CopyStreamed(e, op.DestDir, overwrite,
                    onBytes: n => { op.FileBytesDone += n; op.TotalBytesDone += n; WaitWhilePaused(op); Repaint(); },
                    cancelled: () => op.Cancelled || op.SkipCurrent,
                    sevenZip: _sevenZip, password: PasswordFor(e.FullPath));
                // A cross-volume move deletes the source once the copy lands cleanly.
                if (err == null && op.IsMove && !op.SkipCurrent && !op.Cancelled)
                    err = FileOps.DeletePermanent(e.FullPath, e.IsDir);
            }

            if (op.SkipCurrent && !op.Cancelled)
            {
                op.Status[i] = 3; op.Skipped++;
                op.TotalBytesDone += Math.Max(0, op.ItemBytes[i] - op.FileBytesDone);
            }
            else if (err == "cancelled") { break; }
            else if (err != null)
            {
                op.Status[i] = 4; op.Failed++;
                // Remember permission failures so the finish can offer an elevated retry.
                // An archive member has no path the worker could open, so it is extracted to
                // a temp file first and the retry copies that: only the write side of the
                // failed copy ever needed rights — reading the archive did not.
                if (FileOps.IsDenied(err))
                {
                    var retrySource = ArchivePath.InArchive(e.FullPath) ? StageForElevation(op, e) : e.FullPath;
                    if (retrySource != null)
                        op.Denied.Add((retrySource, System.IO.Path.Combine(op.DestDir, e.Name), e.IsDir));
                }
            }
            else { op.Status[i] = 2; op.Done++; }           // done
            Repaint();
        }

        Application.InvokeOnMainThread(FinishTransfer);
    }

    /// <summary>Extract one archive member to a private temp folder and return the real
    /// path, so an elevated retry has something it can copy. Returns null when the extraction
    /// itself fails — then there is nothing to offer and the item stays failed.
    ///
    /// <para>Staging runs unprivileged on purpose. Reading the archive never needed rights, so
    /// doing it here keeps the elevated step a plain copy between two plain paths: no archive
    /// backend and no password go anywhere near the administrator process.</para></summary>
    private string? StageForElevation(TransferOp op, FileEntry e)
    {
        try
        {
            op.Staging ??= Directory.CreateDirectory(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "VesperStaging-" + Guid.NewGuid().ToString("N"))).FullName;
            var staged = System.IO.Path.Combine(op.Staging, e.Name);
            if (FileOps.Exists(staged)) return staged;        // already extracted in this run
            var err = FileOps.CopyStreamed(e, op.Staging, overwrite: true,
                onBytes: _ => { }, cancelled: () => op.Cancelled,
                sevenZip: _sevenZip, password: PasswordFor(e.FullPath));
            return err == null ? staged : null;
        }
        catch { return null; }
    }

    /// <summary>Remove a transfer's staging folder. Best-effort: a leftover temp folder is
    /// harmless, and it is under the OS's own temp directory either way.</summary>
    private static void ClearStaging(TransferOp op)
    {
        var dir = op.Staging;
        op.Staging = null;
        if (dir == null) return;
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
    }

    private static void WaitWhilePaused(TransferOp op)
    {
        while (op.Paused && !op.Cancelled && !op.SkipCurrent)
            System.Threading.Thread.Sleep(50);
    }

    // Throttled repaint request from a background worker (~40 ms cadence): a copy or move, a pack,
    // an add into an archive, a folder-size walk. On the application's clock, which a headless
    // test moves by hand, not the wall's.
    //
    // View-only (MarkViewDirty): the progress dialog and the rows are the view's, and of the parts
    // only two show a worker's progress — the command line a backgrounded transfer's percent and
    // count (CmdKey), and the footers a folder's measured size, which a walk says it moved with
    // sizes: true. A full MarkDirty here built the app and all eleven parts on every tick, about 25
    // ticks a second for the whole copy: 211 KB a tick with the copy in the background (29 KB
    // view-only), 284 KB with its dialog open (100 KB; the dialog is built with the view).
    private long _lastRepaintTick;
    private bool _repainted;
    private volatile bool _sizesMoved;
    private Action? _progressDirty;

    private void Repaint(bool sizes = false)
    {
        if (sizes) _sizesMoved = true;
        long now = _app.Time.GetTimestamp();
        if (_repainted && _app.Time.GetElapsedTime(_lastRepaintTick, now) < RepaintInterval) return;
        _repainted = true;
        _lastRepaintTick = now;
        Application.InvokeOnMainThread(_progressDirty ??= ProgressDirty);
    }

    private static readonly TimeSpan RepaintInterval = TimeSpan.FromMilliseconds(40);

    /// <summary>A worker's tick, on the UI thread: the view, the footers when a folder's measured
    /// size moved, and the command line through MarkViewDirty's compare.</summary>
    private void ProgressDirty()
    {
        if (_sizesMoved)
        {
            _sizesMoved = false;
            _paneParts[0].FootRevision.Value++;
            _paneParts[1].FootRevision.Value++;
        }
        MarkViewDirty();
    }

    /// <summary>Resolve an overwrite conflict from the dialog. For the background
    /// copy this just records the choice and releases the worker's gate; for the
    /// synchronous move it applies and continues inline.</summary>
    private void OverwriteDecision(bool overwrite, bool applyToRest)
    {
        var op = _op;
        if (op == null) { _overwriteOpen = false; MarkDirty(); return; }
        if (applyToRest) op.OverwriteAll = overwrite;
        _overwriteOpen = false;

        if (op.Gate is { } gate)                 // background transfer: hand the choice back
        {
            op.ConflictOverwrite = overwrite;
            MarkDirty();
            gate.Set();
            return;
        }
        MarkDirty();
    }

    private void FinishTransfer()
    {
        var op = _op;
        _op = null;
        _overwriteOpen = false;
        _progressOpen = false;
        if (op != null)
        {
            _genL++; _genR++;               // both panes may have changed
            Marks(op.Source).Clear();
            ClearMeasuredIn(Dir(op.Source)); ClearMeasuredIn(op.DestDir);   // totals may be stale
            // A generation bump only re-reads a folder; a fed-in result set that just had
            // rows moved out of it has to drop them itself.
            RefreshResultsBothSides();
        }

        // Sync's second batch (the ← direction) starts once the → batch finishes.
        if (_syncSecondBatch is { } next)
        {
            _syncSecondBatch = null;
            _op = next;
            _progressOpen = true;
            MarkDirty();
            System.Threading.Tasks.Task.Run(() => RunTransfer(next));
            return;
        }

        // Items that failed only for lack of rights: offer to redo just those elevated.
        // Only carries the "apply to all" overwrite choice — a per-item "yes" isn't
        // re-asked here, so such an item reports "target exists" rather than silently
        // overwriting something the user didn't approve in this batch.
        // A staged batch is always *copied* out of the temp folder, never moved out of it:
        // its sources are files we made, and clearing them is our cleanup rather than the
        // worker's. (Moving out of an archive is refused up front anyway — see StartMove.)
        if (op is { Denied.Count: > 0 } && !op.Cancelled)
        {
            bool staged = op.Staging != null;
            bool offered = OfferElevation(
                Job(op.IsMove && !staged ? "move" : "copy", op.Denied, overwrite: op.OverwriteAll ?? false),
                op.IsMove ? "Move" : "Copy",
                onDone: () => ClearStaging(op),
                onCancel: () => ClearStaging(op));
            if (!offered) ClearStaging(op);
        }
        else if (op != null) ClearStaging(op);
        MarkDirty();
    }

    private void ToggleTransferPause()
    {
        if (_op is not { } op) return;
        if (op.Paused) op.PausedTotal += Environment.TickCount64 - op.PausedTick;   // resuming
        else op.PausedTick = Environment.TickCount64;
        op.Paused = !op.Paused;
        MarkDirty();
    }

    /// <summary>Hide the progress overlay but keep the copy running (a conflict or
    /// completion re-surfaces it). The command line shows a compact indicator.</summary>
    private void BackgroundTransfer()
    {
        if (_op != null) { _progressOpen = false; MarkDirty(); }
    }

    /// <summary>Skip the item being copied. Not offered while packing — the writer streams
    /// into the archive, so a member abandoned half-written would stay in it looking whole.</summary>
    private void SkipCurrentFile()
    {
        if (_op is { IsPack: true }) return;
        if (_op is { } op) { op.SkipCurrent = true; if (op.Gate is { } g) { op.ConflictOverwrite = false; g.Set(); } MarkDirty(); }
    }

    private void CancelTransfer()
    {
        if (_op is { } op) { op.Cancelled = true; if (op.Gate is { } g) g.Set(); }
        // The worker marshals FinishTransfer; for the sync move, finish now. A pack always has
        // a worker, and it finishes through FinishPack — clearing _op from here would leave it
        // reporting into an op nothing is showing, and then finish a second time.
        if (_op is { Gate: null, IsPack: false } && !_progressOpen) FinishTransfer();
        else MarkDirty();
    }

    /// <summary>Delete (F8 / Delete): recycle the targets after a confirm. Inside an
    /// archive it removes the selection from the archive instead.</summary>
    private void StartDelete()
    {
        char side = _active;
        if (InArchive(side)) { StartArchiveDelete(); return; }
        var items = OperationTargets(side).ToList();
        if (items.Count == 0) return;
        if (LockedBlock(side, "delete here")) return;
        // "Confirm before delete" off ⇒ act immediately.
        if (!_settings.ConfirmDelete) { DoDelete(side, items); return; }
        var dest = _settings.Recycle ? "\nItems are sent to the Recycle Bin." : "\nItems are permanently deleted.";
        _confirm = new ConfirmSpec(
            $"Delete {(items.Count == 1 ? "item" : "items")}",
            $"Delete {SelectionLabel(side)}?{dest}",
            "Delete", Danger: true,
            () => DoDelete(side, items));
        MarkDirty();
    }

    private void DoDelete(char side, List<FileEntry> items)
    {
        var denied = new List<(string, string, bool)>();
        var inUse = new List<string>();
        string? other = null;
        foreach (var e in items)
        {
            var err = _settings.Recycle
                ? FileOps.Recycle(e.FullPath, e.IsDir)
                : FileOps.DeletePermanent(e.FullPath, e.IsDir);
            if (FileOps.IsDenied(err)) denied.Add((e.FullPath, e.FullPath, e.IsDir));
            else if (FileOps.IsInUse(err)) inUse.Add(e.FullPath);
            else if (err != null) other ??= $"Couldn't delete {e.Name}: {FileOps.Clean(err)}";
        }
        _confirm = null;
        _genL++; _genR++;
        Marks(side).Clear();
        ClearMeasuredIn(Dir(side));       // a deleted folder's cached total is now stale
        RefreshResultsBothSides();        // deleted hits leave a fed-in set
        MarkDirty();
        // One answer at a time: rights first (that one has a fix to offer), then what is
        // holding the file (that one has a name to give), then anything else.
        if (denied.Count > 0) OfferElevation(Job("delete", denied, recycle: _settings.Recycle), "Delete");
        else if (inUse.Count > 0) ReportInUse(inUse, "delete");
        else if (other != null) Notice(other);
    }

    // ── Bookmarks ────────────────────────────────────────────────────────
    private void OpenBookmarks()
    {
        _drivePicker = null; _palette?.Close();
        _bookmarksOpen = true;
        _bookmarkSel = Math.Clamp(_bookmarkSel, 0, Math.Max(0, _bookmarks.Count - 1));
        MarkDirty();
    }

    /// <summary>Jump the active pane to the Nth bookmark (Ctrl+1..9 / rail buttons).</summary>
    private void JumpBookmark(int index)
    {
        if (index >= 0 && index < _bookmarks.Count)
            NavigateTo(_active, _bookmarks[index].Path);
    }

    private void OpenBookmarkInPane(int index, char side)
    {
        if (index < 0 || index >= _bookmarks.Count) return;
        _active = side;
        var path = _bookmarks[index].Path;
        CloseOverlays();
        NavigateTo(side, path);
    }

    /// <summary>Add the active pane's current folder as a bookmark.</summary>
    private void AddBookmark()
    {
        var dir = Dir(_active);
        if (_bookmarks.Any(b => PathEquals(b.Path, dir))) return;
        var name = new DirectoryInfo(dir.TrimEnd(
            System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)).Name;
        _bookmarks.Add(new Bookmark(name.Length > 0 ? name : dir, dir));
        _bookmarkSel = _bookmarks.Count - 1;
        SaveBookmarks();
        MarkDirty();
    }

    private void RemoveBookmark(int index)
    {
        if (index < 0 || index >= _bookmarks.Count) return;
        _bookmarks.RemoveAt(index);
        _bookmarkSel = Math.Clamp(_bookmarkSel, 0, Math.Max(0, _bookmarks.Count - 1));
        SaveBookmarks();
        MarkDirty();
    }

    private void HandleBookmarksKey(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Escape: CloseOverlays(); break;
            case KeyCode.Up: _bookmarkSel = Math.Max(0, _bookmarkSel - 1); MarkViewDirty(); break;
            case KeyCode.Down: _bookmarkSel = Math.Min(_bookmarks.Count - 1, _bookmarkSel + 1); MarkViewDirty(); break;
            case KeyCode.Enter: OpenBookmarkInPane(_bookmarkSel, _active); break;
        }
    }

    // ── Command palette ──────────────────────────────────────────────────
    /// <summary>The framework's palette over <see cref="Commands"/>: a row's key chip comes from
    /// the live keymap, and the palette key closes it again.</summary>
    private CommandPalette PaletteDialog => _palette ??= new CommandPalette(Commands)
    {
        Placeholder = "Type a command…",
        ToggleKeys = _keys[nameof(Cmd.Palette)].Keys,
        GroupStyle = PaletteGroupStyle.None,
    };

    private void TogglePalette()
    {
        if (!PaletteDialog.IsOpen) _drivePicker = null;
        PaletteDialog.Toggle(_window);
        MarkDirty();
    }

    /// <summary>A palette row: label, the keys that run it without the palette (null for none),
    /// and what it does.</summary>
    private static PaletteCommand C(string label, string? keys, Action run) => new(label, run) { Keys = keys };

    /// <summary>The caption for a rebindable command's chord, read from the live keymap rather
    /// than written out here — a palette row that still advertises the default after the user
    /// has rebound it is worse than one showing nothing. Null when the command is unbound,
    /// which the palette shows as no chip at all.</summary>
    private string? Key(Cmd cmd) =>
        KeyText(cmd) is { Length: > 0 } keys ? keys : null;

    /// <summary>All palette commands in display order (before query filtering). Everything the
    /// keymap can reach is here, so the palette is a complete second route to the app's
    /// commands and not a shortlist; the structural keys (Tab, Backspace, Ctrl+A) that aren't
    /// rebindable keep their literal captions.</summary>
    private IReadOnlyList<PaletteCommand> Commands()
    {
        char side = _active, other = _active == 'L' ? 'R' : 'L';
        // What the file operations would act on, so their rows say how much is at stake rather
        // than making the user close the palette to check. Counted once: the list is rebuilt on
        // every keystroke of the query.
        int targets = OperationTargets(side).Count(e => !e.IsParent);
        string n = $"{targets} item{(targets == 1 ? "" : "s")}";
        return
        [
            // Navigation. Backspace also goes up, but it is structural and can't be rebound —
            // the chip shows the rebindable chord, which is the one that can stop being true.
            C("Go up one folder", Key(Cmd.GoUpFolder), () => GoUp(side)),
            C("Enter folder under cursor", Key(Cmd.IntoFolder), () => DescendCursor(side)),
            C("Go to path…", Key(Cmd.GotoPath), () => OpenGotoPrompt(side)),
            C("Open drive…", "Alt+F1", () => OpenDrivePicker(side)),
            C("Bookmarks…", Key(Cmd.Bookmarks), OpenBookmarks),
            C("Refresh", Key(Cmd.Refresh), () => RefreshPane(side)),

            // Panes, tabs and view.
            C($"{(_settings.ShowHidden ? "Hide" : "Show")} hidden files", Key(Cmd.ToggleHidden), ToggleHidden),
            C("Swap left and right panes", Key(Cmd.Swap), SwapPanes),
            C("Open this folder in the other pane", Key(Cmd.OpenInOther), () => NavigateTo(other, Dir(side))),
            C("Focus other pane", "Tab", () => { _active = other; }),
            C("New tab", Key(Cmd.NewTab), () => NewTab(side)),
            C("Close tab", Key(Cmd.CloseTab), () => CloseTab(side, TabIndex(side))),
            C("View: Detail", Key(Cmd.ViewDetail), () => SetView(side, ViewMode.Detail)),
            C("View: Brief", Key(Cmd.ViewBrief), () => SetView(side, ViewMode.Brief)),
            C("View: Thumbnails", Key(Cmd.ViewThumbnails), () => SetView(side, ViewMode.Thumbnails)),
            C("Toggle preview panel", Key(Cmd.Preview), TogglePreview),

            // File operations.
            C($"Copy to other pane ({n})", Key(Cmd.Copy), StartCopy),
            C($"Move to other pane ({n})", Key(Cmd.Move), StartMove),
            C($"Delete ({n})", Key(Cmd.Delete), StartDelete),
            C("New folder…", Key(Cmd.NewFolder), () => OpenNewFolderPrompt(side)),
            C("Rename…", Key(Cmd.Rename), () => OpenRenamePrompt(side)),
            C($"Multi-rename… ({targets} files)", Key(Cmd.MultiRename), OpenRename),

            // Archives.
            C($"Pack into archive… ({n})", Key(Cmd.Pack), OpenPack),
            C("Extract archive…", Key(Cmd.Extract), ExtractCursorArchive),

            // Tools.
            C("Find files…", Key(Cmd.Find), () => OpenFind(side)),
            C("Compare files (diff)", Key(Cmd.Diff), () => OpenDiff(side)),
            C("View file", Key(Cmd.View), () => OpenViewer(side)),
            C("Edit file in external editor", Key(Cmd.Edit), () => EditFocused(side)),
            C("Synchronize folders", Key(Cmd.Sync), OpenSync),
            C("Open terminal here", null, OpenTerminalHere),
            C("Open in git client", null, OpenGitClientHere),
            C("Properties…", Key(Cmd.Properties), () => ShowProperties(side)),
            C("Context menu…", Key(Cmd.ContextMenu), () => ShowContextMenuAtCursor(side)),
            C("Who has this file open?", Key(Cmd.Holders), () => ShowHolders(side)),

            // Selection and totals.
            C("Mark all", "Ctrl+A", () => MarkAll(side)),
            C("Mark files by pattern…", Key(Cmd.MarkPattern), () => OpenMarkPatternPrompt(side)),
            C("Measure all folder sizes", Key(Cmd.MeasureAll), () => MeasureAllDirs(side)),

            C("Open Options…", Key(Cmd.Options), OpenOptions),
        ];
    }

    // ── Navigation / view actions (also palette targets) ─────────────────
    private void GoUp(char side)
    {
        // Backspace out of a fed-in result set returns to the folder it was searched from,
        // rather than climbing above it.
        if (ExitResults(side)) { MarkDirty(); return; }
        var dir = Dir(side);
        // A share root's parent is its server — "\\server\share" is a path root, so the test
        // below would stop there and leave the share with no way out but the go-to prompt.
        if (PathProbe.UncParentOf(dir) is { } host)
        {
            NavigateTo(side, host, ShareNameOf(dir));
            return;
        }
        // Already at a drive / server root → stay put. (Trimming the trailing separator off
        // "C:\" leaves "C:", which Directory.GetParent resolves relative to the process's
        // working directory — i.e. it would jump to the parent of the launch folder.)
        if (PathEquals(dir, System.IO.Path.GetPathRoot(dir) ?? dir)) return;
        var parent = Directory.GetParent(dir.TrimEnd(
            System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        if (parent == null) return;
        var leaving = new DirectoryInfo(dir).Name;
        NavigateTo(side, parent.FullName, leaving);
    }

    /// <summary>The share's own name out of "\\server\share", so climbing to the server lands
    /// the cursor on the share just left — what every other ascent does. <c>DirectoryInfo.Name</c>
    /// answers "\\server\share" for one of these, which would match nothing in the listing.</summary>
    private static string ShareNameOf(string shareRoot)
    {
        var trimmed = shareRoot.Replace('/', '\\').TrimEnd('\\');
        int slash = trimmed.LastIndexOf('\\');
        return slash > 1 ? trimmed[(slash + 1)..] : trimmed;
    }

    /// <summary>Ctrl+R. A folder is re-read by bumping the pane's generation; a fed-in result
    /// set has no folder to re-read, so its held rows are re-stat'd instead (re-running the
    /// search would answer a different question — see <see cref="RefreshResults"/>).</summary>
    private void RefreshPane(char side)
    {
        ClearMeasuredIn(Dir(side));
        RefreshResults(side);
        if (side == 'L') _genL++; else _genR++;
        MarkDirty();
    }

    private void ToggleHidden()
    {
        _settings.ShowHidden = !_settings.ShowHidden;
        SettingsStore.Save(_settings);
        _genL++; _genR++;
        MarkDirty();
    }

    // ── Options window ───────────────────────────────────────────────────
    private void OpenOptions()
    {
        _draft = _settings.Clone();
        _draftKeys = null; _rebindCmd = null; _rebindRefusal = null;
        _optSearch = "";
        _optionsOpen = true;
        MarkDirty();
    }

    private void SaveOptions()
    {
        string wasTheme = Nocturne.Theme.SyntaxTheme;
        bool textFaceChanged = _draft.TextFont != _settings.TextFont
            || Math.Abs(_draft.TextFontSize - _settings.TextFontSize) > 0.001f;
        _draft.TextFont ??= "";
        _draft.TextFontSize = Math.Clamp(_draft.TextFontSize, Settings.MinTextFontSize, Settings.MaxTextFontSize);
        _settings = _draft.Clone();
        // The monospace advance and line height were proven at the old face; every wrapped
        // row height in the viewer and the diff is measured against them.
        if (textFaceChanged) InvalidateTextMetrics();
        SettingsStore.Save(_settings);
        SeedFindDefaults();                         // the find dialog adopts the new defaults
        RebuildKeymap();                            // apply any rebinds
        Nocturne.Use(_settings.Theme, _settings.Accent);   // repaint from the chosen scheme
        Icons.InvalidateCache();                    // icons bake the ramp — re-rasterize
        _app.Theme = Nocturne.ToHultTheme();        // refresh built-in widgets
        if (Nocturne.Theme.SyntaxTheme != wasTheme) RetokenizeCode();
        _optionsOpen = false;
        _genL++; _genR++;          // re-sort / re-filter with the new settings
        MarkDirty();
    }

    /// <summary>
    /// Throw away syntax spans coloured by the previous theme and ask for them again. Spans
    /// are plain colours once handed out — nothing about them says which theme produced
    /// them — so a scheme change leaves every tokenized view carrying the old palette until
    /// something re-reads it.
    /// </summary>
    private void RetokenizeCode()
    {
        _previewSpanExcerpt = null;              // the preview re-asks on its next build
        _previewSpans = null;
        if (_viewerOpen) StartViewerHighlight(_viewerLines);
        if (_diffOpen) ComputeDiff();            // the diff does not keep the lines it tokenized
    }

    private void ResetOptions() { _draft = new Settings(); _draftKeys = null; _rebindCmd = null; MarkDirty(); }

    /// <summary>Clicking a column header. Same column flips direction; a new column
    /// adopts a sensible default (Name ascending, Size/Date descending — big & newest
    /// first). Sort is global (both panes); persisted and re-applied by bumping gen.</summary>
    /// <summary>Clicking a column header sorts that pane's active tab — same column flips the
    /// direction, a new column switches to it (size/date default to descending). Sort is
    /// per-tab; the Options "Default sort" only seeds new tabs.</summary>
    private void SetSort(char side, SortMode col)
    {
        var tab = ActiveTab(side);
        if (tab.Sort == col)
            tab.SortDescending = !tab.SortDescending;
        else
        {
            tab.Sort = col;
            tab.SortDescending = col is SortMode.Size or SortMode.Date;
        }
        BumpGen(side);   // re-sort this pane with the new key/direction
        MarkDirty();
    }

    /// <summary>Retry a pane's listing (offline/denied "Reconnect"/"Retry" buttons).</summary>
    private void RetryPane(char side)
    {
        if (side == 'L') _forceStateL = null; else _forceStateR = null;
        BumpGen(side);
        MarkDirty();
    }

    // ── Name prompt (F7 new folder · Shift+F6 rename) ────────────────────
    /// <summary>F7: prompt for a folder name, seeded blank.</summary>
    private void OpenNewFolderPrompt(char side)
    {
        _active = side;
        if (LockedBlock(side, "create a folder here") || ResultsBlock(side)) return;
        // Inside a writable archive F7 makes a folder there instead of being refused;
        // CanAddToArchive posts the reason for the ones that can't take one.
        bool inArchive = InArchive(side);
        if (inArchive && !CanAddToArchive(side)) return;
        _namePromptValue = "";
        _namePromptError = null;
        _namePrompt = new NamePromptSpec("New folder", Dir(side), "Create",
            name => inArchive ? CreateFolderInArchive(side, name) : CreateFolder(side, name));
        _pendingNamePromptFocus = true;
        MarkDirty();
    }

    /// <summary>Shift+F6: prompt to rename the focused entry, seeded with its name.</summary>
    private void OpenRenamePrompt(char side)
    {
        var list = Rows(side);
        int cur = Cursor(side);
        if (cur < 0 || cur >= list.Count) return;
        var e = list[cur];
        if (e.IsParent) return;                    // never rename ".."
        if (LockedBlock(side, "rename here")) return;
        // Inside an archive the member is renamed in place — the one edit besides delete
        // that needs no bytes from outside. Refused with a reason where the format can't.
        if (InArchive(side)) { OpenArchiveRenamePrompt(side, e); return; }
        _active = side;
        _namePromptValue = e.Name;
        _namePromptError = null;
        _namePrompt = new NamePromptSpec("Rename", e.IsDir ? "folder" : "file", "Rename",
            newName => RenameEntry(side, e, newName));
        _pendingNamePromptFocus = true;
        MarkDirty();
    }

    /// <summary>Commit the name prompt: run its action; on error keep the dialog open
    /// with the message, on success close it.</summary>
    private void CommitNamePrompt()
    {
        if (_namePrompt is not { } spec) return;
        if (_gotoProbe != null) return;              // a probe is already out — one Enter is enough
        var err = spec.Commit(_namePromptValue);
        // Commit may have started an off-thread probe (a remote path). Then there is no answer
        // yet: hold the dialog open in its busy state and let the probe close it.
        if (_gotoProbe != null) { MarkDirty(); return; }
        if (err != null) { _namePromptError = FileOps.Clean(err); MarkDirty(); return; }
        _namePrompt = null;
        MarkDirty();
    }

    private void CloseNamePrompt()
    {
        // Esc during a probe abandons it: the continuation finds the field cleared and drops
        // its result rather than navigating a dialog the user has already dismissed.
        var probe = _gotoProbe;
        _gotoProbe = null;
        probe?.Cancel();
        _namePrompt = null;
        MarkDirty();
    }

    /// <summary>Dismiss the confirm dialog — the Cancel button and Escape are the same answer,
    /// and a spec that cares about being answered "no" hears it here.</summary>
    private void CancelConfirm()
    {
        var spec = _confirm;
        _confirm = null;
        spec?.OnCancel?.Invoke();
        MarkDirty();
    }

    /// <summary>Create a folder in <paramref name="side"/> and land on it. Returns an
    /// error string (surfaced in the dialog) or null on success.</summary>
    private string? CreateFolder(char side, string name)
    {
        name = name.Trim();
        if (name.Length == 0) return "Enter a name";
        var full = System.IO.Path.Combine(Dir(side), name);
        if (FileOps.Exists(full)) return "A file or folder with that name already exists";
        try { Directory.CreateDirectory(full); }
        catch (UnauthorizedAccessException ex)
        {
            // No rights in this folder — close the prompt and offer to create it elevated.
            return OfferElevation(Job("mkdir", [(full, full, true)]), "New folder") ? null : ex.Message;
        }
        catch (Exception ex) { return ex.Message; }
        BumpGen(side);
        _pendingFocusChild[side == 'L' ? 0 : 1] = name;
        return null;
    }

    /// <summary>F7 inside an archive: add the folder as a member and land on it. Returns an
    /// error string (surfaced in the dialog) or null on success.
    ///
    /// <para>Written straight through rather than on the pack worker: an empty folder is a
    /// name and no bytes, so there is nothing to show a progress bar about.</para></summary>
    private string? CreateFolderInArchive(char side, string name)
    {
        name = name.Trim();
        if (name.Length == 0) return "Enter a name";
        if (ArchivePath.TryParse(Dir(side)) is not { } ap) return "Not inside an archive";

        var err = ArchiveVfs.CreateFolder(ap.ArchiveFile, ap.Internal, name,
            PasswordFor(Dir(side)), _sevenZip);
        if (err != null) return err;

        BumpGen(side);
        // Land on the top level of what was made, which for a nested "a/b" is "a".
        _pendingFocusChild[side == 'L' ? 0 : 1] = name.Replace('\\', '/').Trim('/').Split('/')[0];
        return null;
    }

    /// <summary>Rename <paramref name="e"/> and land on the new name. Returns an error
    /// string (surfaced in the dialog) or null on success (including a no-op).</summary>
    private string? RenameEntry(char side, FileEntry e, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0) return "Enter a name";
        if (string.Equals(newName, e.Name, StringComparison.Ordinal)) return null;   // unchanged
        var err = FileOps.Rename(e, newName);
        if (FileOps.IsDenied(err))
        {
            var target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(e.FullPath) ?? "", newName);
            if (OfferElevation(Job("rename", [(e.FullPath, target, e.IsDir)]), "Rename")) return null;
        }
        // "In use" names the program, inline, where the message would otherwise have said
        // only that there was one.
        if (FileOps.IsInUse(err)) return InUseMessage(e.FullPath);
        if (err != null) return err;
        // In a fed-in result set the renamed row has to be swapped in by hand: there is no
        // folder read to pick the new name up, and its old path no longer resolves.
        ReplaceResultEntry(side, e.FullPath,
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(e.FullPath) ?? "", newName));
        BumpGen(side);
        Marks(side).Clear();
        _pendingFocusChild[side == 'L' ? 0 : 1] = newName;
        return null;
    }

    /// <summary>Ctrl+U — exchange the two panes. Everything a pane <i>is</i> travels: its whole
    /// tab list (and so each tab's folder, cursor, marks, view mode, sort, lock and Back/Fwd
    /// history), the listing already read for it, and the review-state override.
    ///
    /// <para>Swapping the loaders and generation counters as well is what makes the exchange
    /// instant rather than a reload. <c>_active</c> travels with them: the folder you were
    /// working in is now on the other side of the screen, and leaving the cursor behind put
    /// it in the folder you were <i>not</i> looking at — the next keypress acted on the wrong
    /// listing. Left behind are the element Refs, which name screen positions rather than
    /// panes; the keyboard is handed to the pane element now holding the active side, and the
    /// scroll offsets those elements carry belong to the old side, so both panes are asked to
    /// re-reveal their cursor.</para></summary>
    private void SwapPanes()
    {
        ExitSearch(_active);
        _active = _active == 'L' ? 'R' : 'L';
        _previewFocused = false;
        // The panes are rebuilt by this MarkDirty; focus the one that ends up active
        // afterwards, when the element the Ref names is the one on screen.
        _pendingPaneFocus = _active;
        (_tabsL, _tabsR) = (_tabsR, _tabsL);
        (_tabL, _tabR) = (_tabR, _tabL);
        // The listing, its generation and its loaded flag belong with the tabs they were read
        // for. Without this the pane kept the *other* side's entries until a fresh read landed
        // — a folder shown under the wrong breadcrumb, and briefly operable.
        (_listL, _listR) = (_listR, _listL);
        (_genL, _genR) = (_genR, _genL);
        (_loadedL, _loadedR) = (_loadedR, _loadedL);
        (_forceStateL, _forceStateR) = (_forceStateR, _forceStateL);
        (_pendingFocusChild[0], _pendingFocusChild[1]) = (_pendingFocusChild[1], _pendingFocusChild[0]);
        // What each side is showing and what it is still waiting for travel with the listing
        // above — otherwise a pane that swapped in mid-load would time the other side's wait.
        (_shownDir[0], _shownDir[1]) = (_shownDir[1], _shownDir[0]);
        (_loadingKey[0], _loadingKey[1]) = (_loadingKey[1], _loadingKey[0]);
        (_loadStarted[0], _loadStarted[1]) = (_loadStarted[1], _loadStarted[0]);
        (_loadCts[0], _loadCts[1]) = (_loadCts[1], _loadCts[0]);
        RequestScroll('L'); RequestScroll('R');
        MarkDirty();
    }

    // ── Tabs ─────────────────────────────────────────────────────────────
    /// <summary>Short tab label: the folder's leaf name (or the drive at a root).</summary>
    private static string TabLabel(string dir)
    {
        var trimmed = dir.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var name = System.IO.Path.GetFileName(trimmed);
        return name.Length > 0 ? name : (trimmed.Length > 0 ? trimmed : dir);
    }

    private void SwitchTab(char side, int index)
    {
        var tabs = Tabs(side);
        if (index < 0 || index >= tabs.Count) return;
        ExitSearch(_active);
        _active = side;
        SetTabIndex(side, index);
        BumpGen(side);          // reload for the tab's folder
        RequestScroll(side);
        MarkDirty();
    }

    /// <summary>Open a new tab in <paramref name="side"/> on <paramref name="dir"/>
    /// (defaults to the active tab's folder), placed just after the active one.</summary>
    private void NewTab(char side, string? dir = null) =>
        InsertTab(side, SeededTab(dir ?? Dir(side), _settings.DefaultView));

    /// <summary>Put <paramref name="tab"/> just after <paramref name="side"/>'s active tab and
    /// make it the active one.</summary>
    private void InsertTab(char side, PaneTab tab)
    {
        ExitSearch(_active);
        var tabs = Tabs(side);
        int at = TabIndex(side) + 1;
        tabs.Insert(at, tab);
        _active = side;
        SetTabIndex(side, at);
        BumpGen(side);
        MarkDirty();
    }

    private void CloseTab(char side, int index)
    {
        var tabs = Tabs(side);
        if (tabs.Count <= 1 || index < 0 || index >= tabs.Count) return;
        tabs.RemoveAt(index);
        int cur = TabIndex(side);
        if (index < cur) cur--;
        SetTabIndex(side, Math.Clamp(cur, 0, tabs.Count - 1));
        BumpGen(side);
        MarkDirty();
    }

    private void CloseOtherTabs(char side, int keep)
    {
        var tabs = Tabs(side);
        if (tabs.Count <= 1 || keep < 0 || keep >= tabs.Count) return;
        var survivor = tabs[keep];
        tabs.Clear();
        tabs.Add(survivor);
        SetTabIndex(side, 0);
        MarkDirty();
    }

    private void DuplicateTab(char side, int index)
    {
        var tabs = Tabs(side);
        if (index < 0 || index >= tabs.Count) return;
        var src = tabs[index];
        // A duplicate keeps the source's view mode and sort — set before the new tab's build,
        // which is the build that shows them: set after it, the trail's view toggle and the
        // column header kept the defaults until something rebuilt the parts.
        var dup = SeededTab(src.Dir, src.View);
        dup.Sort = src.Sort;
        dup.SortDescending = src.SortDescending;
        InsertTab(side, dup);
    }

    /// <summary>Open the tab's folder as a new tab in the other pane.</summary>
    private void OpenTabInOtherPane(char side, int index)
    {
        var tabs = Tabs(side);
        if (index < 0 || index >= tabs.Count) return;
        char other = side == 'L' ? 'R' : 'L';
        NewTab(other, tabs[index].Dir);
    }

    private void ToggleTabLock(char side, int index)
    {
        var tabs = Tabs(side);
        if (index < 0 || index >= tabs.Count) return;
        tabs[index].Locked = !tabs[index].Locked;
        MarkDirty();
    }

    /// <summary>Descend into the folder under the cursor (Ctrl+PageDown); ".." goes up.
    ///
    /// <para>An archive counts as a folder here, the same way it does for Enter: Ctrl+PageDown
    /// is "go into this", and a zip is a thing you go into. The difference from Enter is what
    /// it does <em>not</em> do — a plain file is left alone rather than handed to the shell, so
    /// the chord is safe to lean on while walking a tree.</para></summary>
    private void DescendCursor(char side)
    {
        var list = Rows(side);
        int cur = Cursor(side);
        if (cur < 0 || cur >= list.Count) return;
        var e = list[cur];
        if (e.IsParent) GoUp(side);
        else if (e.IsDir) NavigateTo(side, e.FullPath);
        else if (ArchiveVfs.IsArchiveFile(e.FullPath) && (File.Exists(e.FullPath) || InArchive(side)))
            NavigateTo(side, e.FullPath);   // incl. an archive nested inside the one being browsed
    }

    /// <summary>Show or hide the preview panel (Ctrl+Q); persists.</summary>
    private void TogglePreview()
    {
        _settings.PreviewPane = !_settings.PreviewPane;
        _previewFocused = false;
        SettingsStore.Save(_settings);
        MarkDirty();
    }

    // ── Preview focus ────────────────────────────────────────────────────
    // The preview text is a read-only, selectable TextArea. Tab moves keyboard focus
    // into it (the framework consumes Tab for traversal, so it never reaches the global
    // handler); while focused it handles its own selection, copy, and arrow / page /
    // home / end scrolling. `_previewFocused` (set from its Focus/Blur) drives the ring.

    private void CycleTab(char side, int delta)
    {
        var tabs = Tabs(side);
        if (tabs.Count <= 1) return;
        int next = (TabIndex(side) + delta + tabs.Count) % tabs.Count;
        SwitchTab(side, next);
    }

    private static bool PathEquals(string? a, string? b)
    {
        if (a is null || b is null) return ReferenceEquals(a, b);
        return string.Equals(Norm(a), Norm(b), OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        // Fold alt separators to the platform separator and drop a trailing one, so
        // "C:/x" and "C:\x\" compare equal (paths reach us from both listings and args).
        static string Norm(string p) => p
            .Replace(System.IO.Path.AltDirectorySeparatorChar, System.IO.Path.DirectorySeparatorChar)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar);
    }

    private void HandleDrivePickerKey(char target, KeyDownEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Escape: CloseOverlays(); break;
            case KeyCode.Up: _driveCur = Math.Max(0, _driveCur - 1); MarkViewDirty(); break;
            case KeyCode.Down: _driveCur = Math.Min(_drives.Count - 1, _driveCur + 1); MarkViewDirty(); break;
            case KeyCode.Home: _driveCur = 0; MarkViewDirty(); break;
            case KeyCode.End: _driveCur = _drives.Count - 1; MarkViewDirty(); break;
            case KeyCode.Enter: SelectDrive(target, _driveCur); break;
            // A letter picks its drive outright, the way Total Commander's drive list does —
            // Alt+F1 then "c" is two keystrokes, not a walk down the list.
            case >= KeyCode.A and <= KeyCode.Z:
                int hit = IndexOfDriveLetter((char)('A' + (e.Key - KeyCode.A)));
                if (hit >= 0) { _driveCur = hit; SelectDrive(target, hit); }
                break;
        }
    }

    /// <summary>The drive whose root starts with <paramref name="letter"/>, or -1. Roots that
    /// aren't drive letters at all (a UNC mount) simply never match.</summary>
    private int IndexOfDriveLetter(char letter)
    {
        for (int i = 0; i < _drives.Count; i++)
        {
            var root = _drives[i].Root;
            if (root.Length >= 2 && root[1] == ':' && char.ToUpperInvariant(root[0]) == letter) return i;
        }
        return -1;
    }

    private void SelectDrive(char target, int index)
    {
        if (index >= 0 && index < _drives.Count && _drives[index].Ready)
        {
            _active = target;
            var root = _drives[index].Root;
            CloseOverlays();
            NavigateTo(target, SameDriveTargetFor(target, root));
        }
    }

    /// <summary>Where picking <paramref name="root"/> for <paramref name="target"/> should
    /// actually land. When the other pane is already on that volume, Total Commander opens the
    /// folder it is showing rather than the bare root — switching a pane to a drive is nearly
    /// always about catching up with the other side, and the root is one Backspace away when
    /// it isn't.</summary>
    private string SameDriveTargetFor(char target, string root)
    {
        var otherDir = Dir(target == 'L' ? 'R' : 'L');
        int volume = VolumeIndexOf(otherDir);
        return volume >= 0 && PathEquals(_drives[volume].Root, root) ? otherDir : root;
    }

    // ── Keyboard ─────────────────────────────────────────────────────────
    private void HandleKeyDown(KeyDownEvent e)
    {
        var ctrl = e.Modifiers.HasFlag(KeyModifiers.Ctrl) || e.Modifiers.HasFlag(KeyModifiers.Super);
        var alt = e.Modifiers.HasFlag(KeyModifiers.Alt);
        // Transient — clears on the next key. Only the command line shows it (compared).
        if (_notice != null) { _notice = null; MarkViewDirty(); }

        // Modal overlays own the keyboard while open. The palette keeps its query
        // field focused, so its handler consumes only nav/Enter/Escape and lets text
        // keys fall through to the field.
        // Most-modal first: the overwrite prompt and confirm dialog swallow keys.
        if (_overwriteOpen)
        {
            if (e.Key == KeyCode.Escape) CancelTransfer();
            else if (e.Key == KeyCode.Enter) OverwriteDecision(overwrite: true, applyToRest: false);
            e.StopPropagation = true; return;
        }
        if (_confirm is { } cf)
        {
            if (e.Key == KeyCode.Escape) CancelConfirm();
            else if (e.Key == KeyCode.Enter) cf.OnConfirm();
            e.StopPropagation = true; return;
        }
        if (_progressOpen)
        {
            switch (e.Key)
            {
                case KeyCode.Escape: CancelTransfer(); break;
                case KeyCode.P: ToggleTransferPause(); break;
                case KeyCode.S: SkipCurrentFile(); break;
                case KeyCode.B: BackgroundTransfer(); break;
            }
            e.StopPropagation = true; return;
        }
        // Options: Esc cancels; Up/Down move between the nav pages. Other keys flow
        // to any focused field.
        if (_optionsOpen)
        {
            // Capturing a rebind: the next non-modifier key becomes the command's chord.
            // A key Vesper keeps for itself is refused, and the capture stays open saying so.
            if (_rebindCmd is { } rc)
            {
                if (e.Key == KeyCode.Escape) { _rebindCmd = null; _rebindRefusal = null; }   // Esc cancels
                else if (KeyChord.From(e) is { } chord) RebindTo(rc, chord);
                else { e.StopPropagation = true; return; }   // a modifier on its own: keep waiting
                MarkDirty(); e.StopPropagation = true; return;
            }
            bool inField = e.Target is TextInputElement;
            switch (e.Key)
            {
                // Esc backs out of the search first, then closes the window.
                case KeyCode.Escape:
                    if (_optSearch.Length > 0) _optSearch = "";
                    else _optionsOpen = false;
                    MarkDirty(); e.StopPropagation = true; break;
                // Arrows switch nav pages — but not while the search field owns them (caret move).
                case KeyCode.Down when !inField: _optionsPage = Math.Min(OptionPages.Length - 1, _optionsPage + 1); MarkDirty(); e.StopPropagation = true; break;
                case KeyCode.Up when !inField: _optionsPage = Math.Max(0, _optionsPage - 1); MarkDirty(); e.StopPropagation = true; break;
            }
            return;
        }
        // Name prompt (F7 / Shift+F6): Enter commits, Esc cancels; other keys fall
        // through to the focused text field.
        if (_namePrompt != null)
        {
            if (e.Key == KeyCode.Enter) { CommitNamePrompt(); e.StopPropagation = true; }
            else if (e.Key == KeyCode.Escape) { CloseNamePrompt(); e.StopPropagation = true; }
            return;
        }
        if (_findOpen) { HandleFindKey(e); return; }
        if (_packOpen) { HandlePackKey(e); return; }
        if (_renameOpen) { HandleRenameKey(e); return; }
        if (_pwOpen) { HandlePasswordKey(e); return; }
        if (_bookmarksOpen) { HandleBookmarksKey(e); e.StopPropagation = true; return; }
        if (_drivePicker is char dpKey)
        {
            HandleDrivePickerKey(dpKey, e); e.StopPropagation = true; return;
        }

        // Full-shell views (diff / sync / viewer) capture the keyboard while open.
        if (_diffOpen) { HandleDiffKey(e); return; }
        if (_syncOpen) { HandleSyncKey(e); return; }
        if (_viewerOpen) { HandleViewerKey(e); return; }

        // The command line owns Enter/Esc while active; text falls through to its field.
        if (_cmdActive) { HandleCommandKey(e); return; }

        // Review affordance (prototype): Ctrl+Shift+E/O/D force the active pane's
        // empty/offline/denied view; K clears back to the real state. Checked before
        // Ctrl+D so Shift disambiguates it from the bookmark manager.
        if (_keys.Find(e, Keybindings.Group)?.Id is ("ReviewEmpty" or "ReviewOffline" or "ReviewDenied"
            or "ReviewConnecting" or "ReviewClear") and var review)
        {
            PaneState? s = review switch
            {
                "ReviewEmpty" => PaneState.Empty,
                "ReviewOffline" => PaneState.Offline,
                "ReviewDenied" => PaneState.Denied,
                "ReviewConnecting" => PaneState.Connecting,   // needs no server to look at
                _ => null,
            };
            if (_active == 'L') _forceStateL = s; else _forceStateR = s;
            MarkDirty(); e.StopPropagation = true; return;
        }

        // Ctrl+Shift+C copies the active pane's current folder path to the clipboard
        // (Explorer's "Copy as path"). Skipped in a text field, where it means copy text.
        if (_keys.Matches("CopyPath", e) && e.Target is not (TextInputElement or TextAreaElement))
        {
            CopyCurrentPath(_active); e.StopPropagation = true; return;
        }

        // Rebindable global commands (Ctrl/Alt combos) resolve through the keymap; the
        // fixed combos below aren't rebindable.
        if (MatchHotkey(e, wantGlobal: true, out var gcmd)) { Dispatch(gcmd); e.StopPropagation = true; return; }

        // Ctrl+1..9 jump the active pane to the Nth bookmark.
        if (_keys.Matches("JumpBookmark", e) && e.Digit is { } n)
        {
            JumpBookmark(n - 1); e.StopPropagation = true; return;
        }

        // Ctrl+C / Ctrl+X / Ctrl+V file clipboard — but not while a text field (e.g. the
        // preview TextArea) is focused, where these mean copy/cut/paste text.
        if (e.Target is not (TextInputElement or TextAreaElement)
            && _keys.Find(e, Keybindings.Group)?.Id is ("ClipboardCopy" or "ClipboardCut" or "ClipboardPaste") and var clip)
        {
            if (clip == "ClipboardPaste") PasteFromClipboard();
            else CopyToClipboard(cut: clip == "ClipboardCut");
            e.StopPropagation = true; return;
        }

        // Ctrl+A marks everything in the active pane — except in a text field, where it
        // keeps its select-all-text meaning.
        if (_keys.Matches("MarkAll", e) && e.Target is not (TextInputElement or TextAreaElement))
        {
            MarkAll(_active); e.StopPropagation = true; return;
        }

        // Ctrl+Tab / Ctrl+Shift+Tab cycle tabs.
        if (_keys.Matches("NextTab", e) || _keys.Matches("PreviousTab", e))
        {
            CycleTab(_active, _keys.Matches("PreviousTab", e) ? -1 : 1);
            e.StopPropagation = true; return;
        }

        // Alt+← / Alt+→ move back / forward through the active pane's history.
        if (_keys.Matches("Back", e)) { GoBack(_active); e.StopPropagation = true; return; }
        if (_keys.Matches("Forward", e)) { GoForward(_active); e.StopPropagation = true; return; }

        // Alt+F1 / Alt+F2 open the drive picker for the left / right pane.
        if (_keys.Matches("LeftDrive", e) || _keys.Matches("RightDrive", e))
        {
            OpenDrivePicker(_keys.Matches("LeftDrive", e) ? 'L' : 'R'); e.StopPropagation = true; return;
        }

        var typing = e.Target is TextInputElement or TextAreaElement;
        if (typing) return;

        char side = _active;

        // Backspace goes up a folder (when not editing / searching).
        if (e.Key == KeyCode.Backspace && _search.Length == 0)
        {
            GoUp(side); e.StopPropagation = true; return;
        }

        // Rebindable per-pane commands (bare / Shift chords) resolve through the keymap.
        if (MatchHotkey(e, wantGlobal: false, out var pcmd)) { Dispatch(pcmd); e.StopPropagation = true; return; }

        // Shift + a navigation key extends the marked range. After the keymap, so a user who
        // binds a Shift chord of their own still gets what they bound.
        if (e.Modifiers.HasFlag(KeyModifiers.Shift) && IsMarkNavKey(e.Key) && ShiftNav(side, e.Key))
        { e.StopPropagation = true; return; }

        // Brief / Thumbnail modes remap the arrow + paging keys (column / grid motion)
        // before the Detail switch, so e.g. Left navigates instead of opening the command line.
        if (ViewNav(side, e.Key)) { e.StopPropagation = true; return; }

        switch (e.Key)
        {
            // The active pane shows in the trails, and a filter in the footer and the command
            // line, which MarkViewDirty compares: the rest of the parts stay.
            case KeyCode.Tab:
                ExitSearch(_active); _active = _active == 'L' ? 'R' : 'L';
                MarkViewDirty(); e.StopPropagation = true; return;
            case KeyCode.Escape:
                if (_search.Length > 0) { ExitSearch(side); MarkViewDirty(); e.StopPropagation = true; }
                else if (_cutPending.Count > 0) { _cutPending.Clear(); Clipboard.Clear(); MarkDirty(); e.StopPropagation = true; }
                return;
            case KeyCode.Space:
                ToggleMark(side, advance: false); MeasureFocusedDir(side); e.StopPropagation = true; return;
            case KeyCode.Insert:
                ToggleMark(side, advance: true); e.StopPropagation = true; return;
            case KeyCode.Down:
                MoveCursor(side, 1); e.StopPropagation = true; return;
            case KeyCode.Up:
                MoveCursor(side, -1); e.StopPropagation = true; return;
            case KeyCode.PageDown:
                MoveCursor(side, PageSize(side)); e.StopPropagation = true; return;
            case KeyCode.PageUp:
                MoveCursor(side, -PageSize(side)); e.StopPropagation = true; return;
            case KeyCode.Home:
                MoveCursor(side, int.MinValue / 2); e.StopPropagation = true; return;
            case KeyCode.End:
                MoveCursor(side, int.MaxValue / 2); e.StopPropagation = true; return;
            case KeyCode.Enter:
                ActivateCursor(side); e.StopPropagation = true; return;   // nav clears the filter; file-open keeps it
            case KeyCode.Backspace:
                BackspaceSearch(side); e.StopPropagation = true; return;
            case KeyCode.Left:     // focus the command line (type a shell command)
                OpenCommandLine(side); e.StopPropagation = true; return;
            case KeyCode.Delete:   // alias for the (rebindable) Delete command
                StartDelete(); e.StopPropagation = true; return;
        }

        // Type-to-find: a plain word character starts / extends the incremental
        // search (no Ctrl/Alt, so it doesn't swallow shortcuts added later).
        if (!ctrl && !alt)
        {
            char ch = SearchChar(e.Key, e.Modifiers.HasFlag(KeyModifiers.Shift));
            if (ch != '\0') { ExtendSearch(side, ch); e.StopPropagation = true; }
        }
    }

    /// <summary>Test-hosting seam: true once the visible pane(s) have loaded. With the
    /// preview open only the active pane renders, so only it needs to be loaded.</summary>
    public bool ListingsReady => _settings.PreviewPane
        ? (_active == 'L' ? _loadedL : _loadedR)
        : _loadedL && _loadedR;

    private void OnWindowResized()
    {
        // A scale change retires the cached rasters; rebuild so every element picks up
        // sources cut for the new resolution instead of holding retired ones.
        float scale = _window.DisplayScale;
        bool scaleChanged = MathF.Abs(Icons.DisplayScale - scale) >= 0.001f;
        if (scaleChanged) Icons.DisplayScale = scale;
        // A wrapped view has to reach the build, and it takes both halves of this: the build
        // here reads the width the body had a frame ago, and the settle registered by that
        // build is what corrects it once the frame has been laid out at the new size. Without
        // this call nothing rebuilds at all on a resize, so the settle is never registered
        // and never runs — it is only ever scheduled from a build.
        if (scaleChanged || (_settings.DiffWrap && (_diffOpen || _viewerOpen)))
        {
            MarkDirty();
            return;
        }
        // The Brief and Thumbnail grids are sized from the window too: tiles per row, rows per
        // column and the columns realized are decided in the build, and the grid's width (and
        // Brief's height) is given explicitly. Left alone, a grid keeps the old window's geometry
        // until something else rebuilds — a key press, a decode. Most resize steps change only
        // the size, though, and a build of the whole window for it made every cell of the grid
        // again (750 KB a step in Brief): so a build only when one would make something the last
        // one did not — the grid's rows, columns or tiles per row, or a pane's path trail — and
        // otherwise the live grid takes the new size. Behind a full-shell view the panes are not
        // shown, and a grid view's resize rebuilds as it did.
        //
        // The path trail is decided in the build in every view: Detail too drops and brings back
        // its leading segments with the pane's width. A Detail pane has nothing else a build
        // sizes from the window, so it builds only when its trail would change — before, it
        // never built on a resize, and the trail kept the old window's truncation until a key.
        bool gridView = ViewOf('L') is ViewMode.Brief or ViewMode.Thumbnails
                     || ViewOf('R') is ViewMode.Brief or ViewMode.Thumbnails;
        if (gridView && FullShellOpen) { MarkDirty(); return; }
        if (WindowFitChanged()) MarkDirty();
        else if (gridView) FitGridsInPlace();
    }

    /// <summary>
    /// Asks for one more build when the wrapped rows on screen were measured at a width the
    /// body no longer has.
    /// </summary>
    /// <remarks>
    /// <para>Every other row in this app is a fixed height, which is why an ordinary resize
    /// does not rebuild at all: layout alone repositions them. A wrapped row is the exception
    /// — its height is measured in the build, at the column width, and handed to the
    /// virtualized body, which stacks the rows from those numbers. Left alone after a resize,
    /// the rows sit at the old widths while each line lays itself out at the new one, and
    /// every line that gains a visual line draws over the row beneath it. The row track is
    /// auto and would have grown to fit; it is the list forcing the measured height that
    /// makes it visible.</para>
    /// <para>Marking dirty from the resize itself is not enough, and that is exactly why this
    /// runs after the frame: the width comes from the body's laid-out box, so a build started
    /// before layout still reads the width the body had a frame ago. Reading it once the
    /// frame is laid out is what makes the second build measure at the size on screen — and
    /// it covers the first open too, where the body has no box yet and the width falls back
    /// to the whole window's.</para>
    /// <para>It cannot spin: a build measures at the width the body actually has, so after
    /// one the two agree. The settled-width fields are only a guard for the case where a
    /// rebuild somehow does not take. Junction's diff had the same gap and the same fix.</para>
    /// </remarks>
    private void SettleWrappedRowWidths()
    {
        if (!_settings.DiffWrap || !(_diffOpen || _viewerOpen)) return;
        bool diff = _diffOpen;
        _window.RequestPostFrameCallback(() =>
        {
            // Re-read inside the callback: the view can be closed between the build and the
            // frame landing, and a stale answer would rebuild for a body that is gone.
            if (!_settings.DiffWrap) return;
            if (diff ? !_diffOpen : !_viewerOpen) return;

            float live = diff ? DiffCodeWidth() : ViewerCodeWidth();
            float measured = diff ? _diffHeightsWidth : _viewerHeightsWidth;
            float asked = diff ? _diffSettledWidth : _viewerSettledWidth;
            if (Math.Abs(live - measured) <= 0.5f) return;
            if (Math.Abs(live - asked) <= 0.5f) return;   // already asked, at this width

            if (diff) _diffSettledWidth = live; else _viewerSettledWidth = live;
            MarkDirty();
        });
    }

    // The column width each view last asked for a corrective rebuild at — see above.
    private float _diffSettledWidth = float.NaN;
    private float _viewerSettledWidth = float.NaN;

    public void Dispose()
    {
        _window.Resized -= OnWindowResized;
        _window.Activated -= OnWindowActivated;
        SaveSession();
        (_lastImage as IDisposable)?.Dispose();
        _thumbsCache?.Dispose();
        _holdTimer?.Dispose();
        CleanupArchiveEdits();      // remove the F4 edit copies
        ArchiveVfs.CleanupTemp();   // remove any nested-archive temp extractions
    }

    /// <summary>The view mode saved at index <paramref name="i"/> (or Detail when absent).</summary>
    private static ViewMode ViewAt(List<int>? views, int i) =>
        views != null && i < views.Count && Enum.IsDefined((ViewMode)views[i]) ? (ViewMode)views[i] : ViewMode.Detail;

    /// <summary>Persist the current tabs (dirs + per-tab view mode) so "Restore tabs on
    /// start" can bring them back exactly.
    ///
    /// <para>A fed-in search result set is deliberately not persisted: it is a snapshot of a
    /// walk, and restoring it a week later would show a list of files that may no longer be
    /// there and were never re-searched for. Such a tab saves the folder the search ran under,
    /// so it comes back as that folder — which is where Backspace would have taken it.</para></summary>
    private void SaveSession() => SessionStore.Save(new SessionData(
        _tabsL.Select(t => t.Dir).ToList(), _tabL,
        _tabsR.Select(t => t.Dir).ToList(), _tabR,
        _tabsL.Select(t => (int)t.View).ToList(),
        _tabsR.Select(t => (int)t.View).ToList(),
        _window.Placement));
}
