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
    // Keyed by side + entry name so two presses on the same row within the window read
    // as a double-click even though the row element was re-realized in between.
    private const long DoubleClickMs = 400;
    private string? _lastPressName;
    private char _lastPressSide;
    private long _lastPressTick;
    private bool IsDoublePress(char side, string name)
    {
        long now = Environment.TickCount64;
        bool dbl = _lastPressName == name && _lastPressSide == side
                   && now - _lastPressTick <= DoubleClickMs;
        _lastPressSide = side;
        _lastPressTick = now;
        _lastPressName = dbl ? null : name;   // consume, so a third press isn't a second activate
        return dbl;
    }

    // Each pane is a list of tabs; a tab owns its folder, cursor, and mark set.
    // Marks are kept by name (they survive row recycling) and cleared on navigation.
    private sealed class PaneTab(string dir)
    {
        public string Dir = dir;
        public int Cursor;
        public bool Locked;
        public ViewMode View = ViewMode.Detail;   // per-tab list layout, persisted per session
        public SortMode Sort = SortMode.Name;      // per-tab sort column + direction (seeded
        public bool SortDescending;                // from the Options default at tab creation)
        public readonly HashSet<string> Marks = new(StringComparer.Ordinal);
        // Per-tab navigation history (most-recent last). Back holds folders left behind;
        // Fwd holds folders returned-from after a Back. A new forward navigation clears Fwd.
        public readonly List<string> Back = new();
        public readonly List<string> Fwd = new();
    }
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

    // Command palette (Ctrl+P): a fuzzy list of actions over a focused query field.
    private bool _paletteOpen;
    private string _paletteQuery = "";
    private int _paletteSel;
    private readonly Ref<HultUI.Core.Elements.TextInputElement> _paletteRef = new();
    private bool _pendingPaletteFocus;

    // Bookmarks: quick jumps for the active pane. Seeded from real folders; the
    // manager (Ctrl+D / rail +) adds/removes them and Ctrl+1..9 jump by index.
    private sealed record Bookmark(string Name, string Path);
    private readonly List<Bookmark> _bookmarks = [];
    private bool _bookmarksOpen;
    private int _bookmarkSel;

    // Confirm dialog (delete, and the copy/move go-ahead) + in-progress transfer.
    private sealed record ConfirmSpec(string Title, string Message, string ConfirmLabel, bool Danger, Action OnConfirm);
    private ConfirmSpec? _confirm;

    // Name prompt (F7 new folder, Shift+F6 rename): a titled dialog over a focused
    // text field. Commit returns an error string (kept inline, dialog stays open) or
    // null (success, dialog closes). Reused for both flows.
    private sealed record NamePromptSpec(string Title, string Subtitle, string ConfirmLabel, Func<string, string?> Commit,
        string Placeholder = "Name");
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

        // ── Streaming progress (background copy) ─────────────────────────
        public readonly long[] ItemBytes = [];      // per-item total size
        public byte[] Status = [];                   // 0 queued · 1 active · 2 done · 3 skipped · 4 failed
        public long TotalBytes, TotalBytesDone;
        public long FileBytes, FileBytesDone;        // current item
        public long StartTick, PausedTick, PausedTotal;   // for rate / ETA (excludes paused time)
        public volatile bool Paused, Cancelled, SkipCurrent;
        public ManualResetEventSlim? Gate;           // set while paused at a conflict (bg waits)
        public bool ConflictOverwrite;               // the choice released through Gate

        /// <summary>Items that failed purely for lack of rights, with the destination they
        /// were headed for — the batch offered for an elevated retry when the run ends.</summary>
        public readonly List<(string Source, string Target, bool IsDir)> Denied = [];

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
    private readonly AsyncLoader<DirListing> _listL = new(DirListing.Loading);
    private readonly AsyncLoader<DirListing> _listR = new(DirListing.Loading);
    private int _genL, _genR;
    private bool _loadedL, _loadedR;

    // Per-pane forced state override for the design's review shortcuts
    // (Ctrl+Shift+E/O/D set empty/offline/denied; K clears). Null ⇒ use the real
    // filesystem-derived state.
    private PaneState? _forceStateL, _forceStateR;

    private readonly Ref<VirtualListElement<FileEntry>> _vlistL = new();
    private readonly Ref<VirtualListElement<FileEntry>> _vlistR = new();
    private bool _pendingScrollL, _pendingScrollR;

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
    private float PreviewWidth => _settings.PreviewWidth;
    private float RowHeight => _settings.Density == Density.Compact ? 20f : 24f;

    public CommanderApp(Application app, Window window, string startDir, string? startDirRight = null)
    {
        _app = app;
        _window = window;
        // Icons rasterize at the display scale, so keep it current — including when the
        // window is dragged to a monitor with different scaling, which arrives as a resize.
        Icons.DisplayScale = window.DisplayScale;
        window.Resized += OnWindowResized;
        _settings = SettingsStore.Load();
        // A sideways offset is app state, so moving it has to rebuild the rows.
        _diffPan = new HorizontalPan(MarkDirty);
        _viewerPan = new HorizontalPan(MarkDirty);
        _viewerFind = new FindBarState(MarkDirty);
        _diffFind = new FindBarState(MarkDirty);
        Nocturne.SetAccent(_settings.Accent);
        _app.Theme = Nocturne.ToHultTheme();
        RebuildKeymap();
        SevenZip.ExePath = _settings.SevenZipPath;
        _sevenZip = SevenZip.Available();   // probed once — the probe spawns a process

        // Restore the last session's tabs when enabled and still valid; else open one
        // tab per pane on the starting folder.
        bool restored = false;
        if (_settings.RestoreTabs && SessionStore.Load() is { } s)
        {
            // Pair each dir with its saved view mode before filtering, so a dropped
            // (no-longer-existing) folder doesn't misalign the remaining view indices.
            var left = s.Left.Select((d, i) => (d, v: ViewAt(s.LeftViews, i))).Where(x => Directory.Exists(x.d)).ToList();
            var right = s.Right.Select((d, i) => (d, v: ViewAt(s.RightViews, i))).Where(x => Directory.Exists(x.d)).ToList();
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

    private void SetCursor(char side, int value) => ActiveTab(side).Cursor = value;
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
        if (!Directory.Exists(path)) return;
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

    private PaneState PaneStateOf(char side) =>
        (side == 'L' ? _forceStateL : _forceStateR) ?? Loader(side).Value.State;

    /// <summary>The current (possibly stale-while-loading) listing for a side,
    /// kicking off / refreshing the off-thread load as a side effect of Build().</summary>
    private IReadOnlyList<FileEntry> Listing(char side)
    {
        var loader = Loader(side);
        var dir = Dir(side);
        var sort = SortOf(side); var desc = SortDescOf(side);   // per-tab; captured for the worker
        loader.Load($"{dir}#{Gen(side)}",
            () => FileSystem.List(dir, _settings.ShowHidden, sort, _settings.DirsFirst, desc, _sevenZip, PasswordFor(dir)),
            onLoaded: _ =>
        {
            if (side == 'L') _loadedL = true; else _loadedR = true;
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
            MarkDirty();
        });
        return loader.Value.Entries;
    }

    // ── Build ────────────────────────────────────────────────────────────
    public override Element Build()
    {
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
        var rail = BuildRail(); rail.Key = "rail";
        body.AddChild(rail);
        if (_settings.Preview == PreviewMode.Full)
        {
            // Full preview: only the active pane browses; the preview fills the rest.
            // Give both a 0 flex-basis so grow splits the row 50/50 regardless of
            // content — otherwise the pane's large content basis starves an empty
            // preview (e.g. on "..") into a sliver.
            // Both panes and the preview are Tab-focusable; in Full mode only the active
            // pane is built, so Tab cycles pane ↔ preview (document order: pane, preview).
            body.AddChild(BuildPane(_active).FlexBasis(Dimension.Px(0)));
            body.AddChild(VDivider());
            var pv = BuildPreview(full: true); pv.Key = "preview";
            pv.FlexBasis(Dimension.Px(0));
            body.AddChild(pv);
        }
        else
        {
            body.AddChild(BuildPane('L'));
            body.AddChild(VDivider());
            body.AddChild(BuildPane('R'));
            if (_settings.Preview == PreviewMode.Normal)
            {
                body.AddChild(VDivider());
                var pv = BuildPreview(full: false); pv.Key = "preview"; body.AddChild(pv);
            }
        }
        }
        body.Key = "body";

        var root = Flex.Column(gap: 0)
            .FlexGrow(1)
            .Background(Nocturne.Bg)
            .TextColor(Nocturne.Text)
            .FontSize(12.5f)
            .Children(BuildChrome(), body, BuildCommandLine());
        // A right-drag mark ends wherever the button is released — pointer-up bubbles
        // to the root even when the release lands off the rows (empty list, footer, …).
        // The Find dialog's corner-grip resize tracks the same way: move updates the size,
        // up ends the drag, both regardless of where the pointer wandered.
        root.On<PointerMoveEvent>(ev =>
        {
            if (_findResizing) UpdateFindResize(ev.Position.X, ev.Position.Y);
            // Watched at the root, not on the row: a drag that leaves the row it started on
            // is exactly the case that must not still count as holding still.
            UpdateContextHold(ev.Position.X, ev.Position.Y);
        });
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
        if (_paletteOpen)
        {
            var o = BuildPalette(); o.Key = "palette"; root.AddChild(o);
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
        if (_historyPop is { } hp)
        {
            var o = BuildHistoryPopover(hp); o.Key = "history-pop"; root.AddChild(o);
        }
        return root;
    }

    protected override void OnAfterBuild()
    {
        // Returning from a full-shell view: its Close ran while the diff/sync tree was
        // still up, so the pane it wants to focus only exists as of this build.
        if (_pendingPaneFocus is char paneSide)
        {
            _pendingPaneFocus = null;
            PaneRef(paneSide).Current?.Focus();
        }
        if (_pendingScrollL) { _pendingScrollL = false; ScrollCursorIntoView('L'); }
        if (_pendingScrollR) { _pendingScrollR = false; ScrollCursorIntoView('R'); }
        if (_pendingPaletteFocus) { _pendingPaletteFocus = false; _paletteRef.Current?.Focus(); }
        if (_pendingNamePromptFocus) { _pendingNamePromptFocus = false; _namePromptRef.Current?.Focus(); }
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
        ServiceFindBar(_viewerFind, _viewerScrollRef, RevealViewerHit);
        ServiceFindBar(_diffFind, _diffScrollRef, RevealDiffHit);
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
        BumpGen(side);
        // Cursor reset now; the loader's onLoaded re-clamps once the listing lands.
        // If we ascended, try to land on the folder we came from.
        SetCursor(side, 0);
        Marks(side).Clear();
        _search = "";
        _previewFocused = false;
        _pendingFocusChild[side == 'L' ? 0 : 1] = focusChildName;
        MarkDirty();
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

    /// <summary>Ctrl+G — prompt for a path (relative or absolute) and jump the pane there.</summary>
    private void OpenGotoPrompt(char side)
    {
        _active = side;
        _namePromptValue = "";
        _namePromptError = null;
        _namePrompt = new NamePromptSpec("Go to path", Dir(side), "Go",
            path => GotoPath(side, path), Placeholder: "Absolute, or relative to this folder");
        _pendingNamePromptFocus = true;
        MarkDirty();
    }

    /// <summary>Resolve <paramref name="raw"/> and navigate <paramref name="side"/> to it.
    /// Relative paths (incl. "..") resolve against the pane's folder; "~" and %VAR% expand;
    /// a file target lands on the file in its parent; an archive target browses inside.
    /// Returns an error string (dialog stays open) or null on success.</summary>
    private string? GotoPath(char side, string raw)
    {
        var input = raw.Trim().Trim('"');
        if (input.Length == 0) return "Enter a path.";

        string resolved;
        try
        {
            var p = Environment.ExpandEnvironmentVariables(input);
            if (p == "~") p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            else if (p.StartsWith("~/") || p.StartsWith("~\\"))
                p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p[2..]);
            // A bare drive ("d:") is drive-relative — it would resolve against the process's
            // per-drive working directory, not the root the user almost certainly means.
            if (p.Length == 2 && p[1] == ':' && char.IsAsciiLetter(p[0]))
                p += System.IO.Path.DirectorySeparatorChar;
            resolved = System.IO.Path.GetFullPath(p, Dir(side));
        }
        catch (Exception ex) { return ex.Message; }

        if (Directory.Exists(resolved))
        {
            NavigateTo(side, resolved);
            return null;
        }
        if (File.Exists(resolved))
        {
            if (ArchiveVfs.IsArchiveFile(resolved)) { NavigateTo(side, resolved); return null; }
            // A plain file: land in its folder with the cursor on it.
            var parent = System.IO.Path.GetDirectoryName(resolved);
            if (parent == null) return $"Not a folder: {resolved}";
            NavigateTo(side, parent, focusChildName: System.IO.Path.GetFileName(resolved));
            return null;
        }
        // Inside an archive the member dirs aren't on disk — navigate if the archive file
        // itself exists and let the listing surface any bad member path.
        if (ArchivePath.TryParse(resolved) is { } ap && File.Exists(ap.ArchiveFile))
        {
            NavigateTo(side, resolved);
            return null;
        }
        return $"Path not found: {resolved}";
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
        FileOps.LaunchEditor(template, e.FullPath);
    }

    private void MoveCursor(char side, int delta, bool clearSearch = false)
    {
        if (clearSearch) _search = "";   // arrows keep the filter; nav within the matches
        var list = Rows(side);
        if (list.Count == 0) return;
        int next = Math.Clamp(Cursor(side) + delta, 0, list.Count - 1);
        if (next == Cursor(side)) { MarkDirty(); return; }
        SetCursor(side, next);
        RequestScroll(side);
        MarkDirty();
    }

    private static int IndexOfName(IReadOnlyList<FileEntry> list, string name)
    {
        for (int i = 0; i < list.Count; i++)
            if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private int PageSize(char side)
    {
        var h = VList(side).Current?.Layout.AbsoluteContentBox.Height ?? (10 * RowHeight);
        return Math.Max(1, (int)(h / RowHeight) - 1);
    }

    // ── Marking ──────────────────────────────────────────────────────────
    /// <summary>Toggle the mark on the focused row (never the ".." parent). When
    /// <paramref name="advance"/> (Insert), move the cursor down one afterwards.</summary>
    private void ToggleMark(char side, bool advance)
    {
        var list = Rows(side);
        int cur = Cursor(side);
        if (cur < 0 || cur >= list.Count) return;
        var e = list[cur];
        if (!e.IsParent)
        {
            var marks = Marks(side);
            if (!marks.Remove(e.Name)) marks.Add(e.Name);
        }
        if (advance) MoveCursor(side, 1, clearSearch: false);
        MarkDirty();
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
        bool allMarked = rows.All(e => marks.Contains(e.Name));
        foreach (var e in rows)
        {
            if (allMarked) marks.Remove(e.Name);
            else marks.Add(e.Name);
        }
        MarkDirty();
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
            marks.Add(e.Name);
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
    // it uniform, matching Total Commander. Rows recycle, so we track by name and
    // handle each row once per drag; the release is caught at the root (below).
    private bool _rmarkActive;
    private char _rmarkSide;
    private bool _rmarkAdd;
    private readonly HashSet<string> _rmarkSeen = new(StringComparer.Ordinal);

    private void BeginRightMark(char side, FileEntry e)
    {
        _active = side;
        _rmarkActive = true;
        _rmarkSide = side;
        _rmarkSeen.Clear();
        _rmarkAdd = !Marks(side).Contains(e.Name);   // direction from the first row
        ApplyRightMark(side, e);
    }

    private void ApplyRightMark(char side, FileEntry e)
    {
        if (!_rmarkActive || side != _rmarkSide || e.IsParent) return;
        if (!_rmarkSeen.Add(e.Name)) return;         // each row toggles once per drag
        var marks = Marks(side);
        if (_rmarkAdd) marks.Add(e.Name); else marks.Remove(e.Name);
        MarkDirty();
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
                MarkDirty();   // repaint the excerpt with its colors
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
                report: n => { ds.Set(n); Repaint(); });
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
            return Listing(side).Where(e => !e.IsParent && marks.Contains(e.Name)).ToList();
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
        if (LockedBlock(side, "paste into a locked tab") || InArchiveBlock(side)) return;
        var dest = Dir(side);
        var items = new List<FileEntry>();
        foreach (var p in paths)
        {
            if (!FileOps.Exists(p)) continue;
            if (PathEquals(System.IO.Path.GetDirectoryName(p) ?? "", dest)) continue;   // same-folder paste = no-op
            items.Add(FileOps.EntryFromPath(p));
        }
        if (items.Count == 0) return;

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
    private void ExtendSearch(char side, char ch)
    {
        int anchor = CursorListingPos(side);
        _search += ch;
        ReclampFilterCursor(side, anchor);
        RequestScroll(side);
        MarkDirty();
    }

    private void BackspaceSearch(char side)
    {
        if (_search.Length == 0) return;
        int anchor = CursorListingPos(side);
        _search = _search[..^1];
        ReclampFilterCursor(side, anchor);
        RequestScroll(side);
        MarkDirty();
    }

    /// <summary>Clear the filter, moving the cursor back onto the same entry in the full
    /// listing so you keep your place.</summary>
    private void ExitSearch(char side)
    {
        if (_search.Length == 0) return;
        string? keep = CursorEntryName(side);
        _search = "";
        if (keep != null) { int i = IndexOfName(Listing(side), keep); if (i >= 0) SetCursor(side, i); }
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

    private string? CursorEntryName(char side)
    {
        var rows = Rows(side);
        int c = Cursor(side);
        return c >= 0 && c < rows.Count ? rows[c].Name : null;
    }

    /// <summary>The cursor's index in the *full* listing (not the filtered view), so a filter
    /// change can anchor the new cursor to where you actually were. 0 when the cursor is off
    /// the list.</summary>
    private int CursorListingPos(char side)
    {
        string? name = CursorEntryName(side);
        int i = name != null ? IndexOfName(Listing(side), name) : -1;
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

    private int IndexOfDrive(string dir)
    {
        var root = System.IO.Path.GetPathRoot(dir);
        for (int i = 0; i < _drives.Count; i++)
            if (PathEquals(_drives[i].Root, root)) return i;
        return 0;
    }

    private bool AnyOverlayOpen => _drivePicker != null || _paletteOpen || _bookmarksOpen
        || _confirm != null || _overwriteOpen || _progressOpen || _optionsOpen || _namePrompt != null
        || _findOpen || _packOpen || _pwOpen || _renameOpen || _historyPop != null;

    private void CloseOverlays()
    {
        _drivePicker = null;
        _paletteOpen = false;
        _bookmarksOpen = false;
        _confirm = null;
        _optionsOpen = false;
        _optSearch = "";
        _namePrompt = null;
        _packOpen = false;
        _pwOpen = false;
        _renameOpen = false;
        _historyPop = null;
        if (_findOpen) { _findOpen = false; StopFind(); }
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

    // ── Move (F6): synchronous confirm + transfer (moves are usually instant) ──
    private void StartMove()
    {
        char side = _active, other = side == 'L' ? 'R' : 'L';
        var items = OperationTargets(side).ToList();
        if (items.Count == 0) return;
        var dest = Dir(other);
        if (PathEquals(dest, Dir(side))) return;
        if (LockedBlock(side, "move out of a locked tab") || LockedBlock(other, "move into a locked tab")) return;
        if (InArchiveBlock(side) || InArchiveBlock(other)) return;   // read-only in v1

        _confirm = new ConfirmSpec(
            $"Move {(items.Count == 1 ? "item" : "items")}",
            $"Move {SelectionLabel(side)} to\n{dest}",
            "Move", Danger: true,
            () =>
            {
                _confirm = null;
                var op = new TransferOp(items) { IsMove = true, Source = side, DestDir = dest };
                _op = op;
                _progressOpen = true;
                MarkDirty();
                System.Threading.Tasks.Task.Run(() => RunTransfer(op));
            });
        MarkDirty();
    }

    // ── Copy (F5): background streamed transfer with the progress overlay ──
    private void StartCopy()
    {
        if (InArchive(_active)) { StartExtract(); return; }   // F5 inside an archive = extract-out
        char side = _active, other = side == 'L' ? 'R' : 'L';
        var items = OperationTargets(side).ToList();
        if (items.Count == 0) return;
        var dest = Dir(other);
        if (PathEquals(dest, Dir(side))) return;

        if (LockedBlock(other, "copy into a locked tab") || InArchiveBlock(other)) return;
        var op = new TransferOp(items) { IsMove = false, Source = side, DestDir = dest };
        _op = op;
        _progressOpen = true;
        MarkDirty();
        System.Threading.Tasks.Task.Run(() => RunTransfer(op));
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
                if (FileOps.IsDenied(err) && !ArchivePath.InArchive(e.FullPath))
                    op.Denied.Add((e.FullPath, System.IO.Path.Combine(op.DestDir, e.Name), e.IsDir));
            }
            else { op.Status[i] = 2; op.Done++; }           // done
            Repaint();
        }

        Application.InvokeOnMainThread(FinishTransfer);
    }

    private static void WaitWhilePaused(TransferOp op)
    {
        while (op.Paused && !op.Cancelled && !op.SkipCurrent)
            System.Threading.Thread.Sleep(50);
    }

    // Throttled repaint request from the background worker (~40ms cadence).
    private long _lastRepaintTick;
    private void Repaint()
    {
        var now = Environment.TickCount64;
        if (now - _lastRepaintTick < 40) return;
        _lastRepaintTick = now;
        Application.InvokeOnMainThread(MarkDirty);
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
        if (op is { Denied.Count: > 0 } && !op.Cancelled)
        {
            OfferElevation(
                Job(op.IsMove ? "move" : "copy", op.Denied, overwrite: op.OverwriteAll ?? false),
                op.IsMove ? "Move" : "Copy");
        }
        MarkDirty();
    }

    private void ToggleTransferPause()
    {
        if (_op is { } op) { op.Paused = !op.Paused; MarkDirty(); }
    }

    /// <summary>Hide the progress overlay but keep the copy running (a conflict or
    /// completion re-surfaces it). The command line shows a compact indicator.</summary>
    private void BackgroundTransfer()
    {
        if (_op != null) { _progressOpen = false; MarkDirty(); }
    }

    private void SkipCurrentFile()
    {
        if (_op is { } op) { op.SkipCurrent = true; if (op.Gate is { } g) { op.ConflictOverwrite = false; g.Set(); } MarkDirty(); }
    }

    private void CancelTransfer()
    {
        if (_op is { } op) { op.Cancelled = true; if (op.Gate is { } g) g.Set(); }
        // The worker marshals FinishTransfer; for the sync move, finish now.
        if (_op is { Gate: null } && !_progressOpen) FinishTransfer();
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
        foreach (var e in items)
        {
            var err = _settings.Recycle
                ? FileOps.Recycle(e.FullPath, e.IsDir)
                : FileOps.DeletePermanent(e.FullPath, e.IsDir);
            if (FileOps.IsDenied(err)) denied.Add((e.FullPath, e.FullPath, e.IsDir));
        }
        _confirm = null;
        _genL++; _genR++;
        Marks(side).Clear();
        ClearMeasuredIn(Dir(side));       // a deleted folder's cached total is now stale
        MarkDirty();
        if (denied.Count > 0) OfferElevation(Job("delete", denied, recycle: _settings.Recycle), "Delete");
    }

    // ── Bookmarks ────────────────────────────────────────────────────────
    private void OpenBookmarks()
    {
        _drivePicker = null; _paletteOpen = false;
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
            case KeyCode.Up: _bookmarkSel = Math.Max(0, _bookmarkSel - 1); MarkDirty(); break;
            case KeyCode.Down: _bookmarkSel = Math.Min(_bookmarks.Count - 1, _bookmarkSel + 1); MarkDirty(); break;
            case KeyCode.Enter: OpenBookmarkInPane(_bookmarkSel, _active); break;
        }
    }

    // ── Command palette ──────────────────────────────────────────────────
    private void TogglePalette()
    {
        _paletteOpen = !_paletteOpen;
        if (_paletteOpen)
        {
            _drivePicker = null;
            _paletteQuery = "";
            _paletteSel = 0;
            _pendingPaletteFocus = true;
        }
        MarkDirty();
    }

    /// <summary>A palette action: label, optional shortcut caption, and what it does.</summary>
    private readonly record struct Command(string Label, string? Shortcut, Action Run);

    /// <summary>All palette commands in display order (before query filtering).</summary>
    private IReadOnlyList<Command> Commands()
    {
        char side = _active, other = _active == 'L' ? 'R' : 'L';
        return
        [
            new("Go up one folder", "Backspace", () => GoUp(side)),
            new("Go to path…", "Ctrl+G", () => OpenGotoPrompt(side)),
            new("Open drive…", "Alt+F1", () => OpenDrivePicker(side)),
            new("Refresh", "Ctrl+R", () => RefreshPane(side)),
            new($"{(_settings.ShowHidden ? "Hide" : "Show")} hidden files", "Ctrl+H", ToggleHidden),
            new("Swap left and right panes", "Ctrl+U", SwapPanes),
            new("Open this folder in the other pane", "=", () => NavigateTo(other, Dir(side))),
            new("Focus other pane", "Tab", () => { _active = other; }),
            new("New folder…", "F7", () => OpenNewFolderPrompt(side)),
            new("Find files…", "Alt+F7", () => OpenFind(side)),
            new("Compare files (diff)", "F3", () => OpenDiff(side)),
            new("View file", "Shift+F3", () => OpenViewer(side)),
            new("Synchronize folders", "F9", OpenSync),
            new("Open terminal here", null, OpenTerminalHere),
            new("Open in git client", null, OpenGitClientHere),
            new("Edit file in external editor", "F4", () => EditFocused(side)),
            new("Rename…", "Shift+F6", () => OpenRenamePrompt(side)),
            new($"Multi-rename… ({OperationTargets(side).Count(e => !e.IsParent)} files)", "Ctrl+M", OpenRename),
            new("Properties…", "Alt+Enter", () => ShowProperties(side)),
            new("Context menu…", "Menu", () => ShowContextMenuAtCursor(side)),
            new("Mark all", "Ctrl+A", () => MarkAll(side)),
            new("Mark files by pattern…", "Ctrl+Shift+A", () => OpenMarkPatternPrompt(side)),
            new("Measure all folder sizes", "Alt+Shift+Enter", () => MeasureAllDirs(side)),
            new("Cycle preview panel", "Ctrl+Q", CyclePreview),
            new("Open Options…", "Ctrl+,", OpenOptions),
        ];
    }

    /// <summary>Commands matching the current query (case-insensitive substring).</summary>
    private List<Command> FilteredCommands()
    {
        var all = Commands();
        if (_paletteQuery.Length == 0) return all.ToList();
        return all.Where(c => c.Label.Contains(_paletteQuery, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void RunPaletteSelection()
    {
        var cmds = FilteredCommands();
        if (_paletteSel >= 0 && _paletteSel < cmds.Count)
        {
            var run = cmds[_paletteSel].Run;
            CloseOverlays();
            run();
            MarkDirty();
        }
    }

    private void HandlePaletteKey(KeyDownEvent e)
    {
        int count = FilteredCommands().Count;
        switch (e.Key)
        {
            case KeyCode.Escape: CloseOverlays(); e.StopPropagation = true; break;
            case KeyCode.Up: _paletteSel = Math.Max(0, _paletteSel - 1); MarkDirty(); e.StopPropagation = true; break;
            case KeyCode.Down: _paletteSel = Math.Min(Math.Max(0, count - 1), _paletteSel + 1); MarkDirty(); e.StopPropagation = true; break;
            case KeyCode.Enter: RunPaletteSelection(); e.StopPropagation = true; break;
            // Other keys (text) fall through to the focused query field.
        }
    }

    // ── Navigation / view actions (also palette targets) ─────────────────
    private void GoUp(char side)
    {
        var dir = Dir(side);
        // Already at a drive / UNC root → stay put. (Trimming the trailing separator off
        // "C:\" leaves "C:", which Directory.GetParent resolves relative to the process's
        // working directory — i.e. it would jump to the parent of the launch folder.)
        if (PathEquals(dir, System.IO.Path.GetPathRoot(dir) ?? dir)) return;
        var parent = Directory.GetParent(dir.TrimEnd(
            System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        if (parent == null) return;
        var leaving = new DirectoryInfo(dir).Name;
        NavigateTo(side, parent.FullName, leaving);
    }

    private void RefreshPane(char side)
    {
        ClearMeasuredIn(Dir(side));
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
        _optSearch = "";
        _optionsOpen = true;
        MarkDirty();
    }

    private void SaveOptions()
    {
        _settings = _draft.Clone();
        SettingsStore.Save(_settings);
        RebuildKeymap();                            // apply any rebinds
        Nocturne.SetAccent(_settings.Accent);      // retheme the accent ramp
        Icons.InvalidateCache();                    // icons bake the ramp — re-rasterize
        _app.Theme = Nocturne.ToHultTheme();        // refresh built-in widgets
        _optionsOpen = false;
        _genL++; _genR++;          // re-sort / re-filter with the new settings
        MarkDirty();
    }

    private void ResetOptions() { _draft = new Settings(); MarkDirty(); }

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
        if (LockedBlock(side, "create a folder here") || InArchiveBlock(side)) return;
        _namePromptValue = "";
        _namePromptError = null;
        _namePrompt = new NamePromptSpec("New folder", Dir(side), "Create",
            name => CreateFolder(side, name));
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
        if (LockedBlock(side, "rename here") || InArchiveBlock(side)) return;
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
        var err = spec.Commit(_namePromptValue);
        if (err != null) { _namePromptError = FileOps.Clean(err); MarkDirty(); return; }
        _namePrompt = null;
        MarkDirty();
    }

    private void CloseNamePrompt() { _namePrompt = null; MarkDirty(); }

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
        if (err != null) return err;
        BumpGen(side);
        Marks(side).Clear();
        _pendingFocusChild[side == 'L' ? 0 : 1] = newName;
        return null;
    }

    private void SwapPanes()
    {
        ExitSearch(_active);
        (_tabsL, _tabsR) = (_tabsR, _tabsL);
        (_tabL, _tabR) = (_tabR, _tabL);
        _genL++; _genR++;
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
    private void NewTab(char side, string? dir = null)
    {
        ExitSearch(_active);
        var tabs = Tabs(side);
        int at = TabIndex(side) + 1;
        tabs.Insert(at, SeededTab(dir ?? Dir(side), _settings.DefaultView));
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
        NewTab(side, src.Dir);
        var dup = ActiveTab(side);        // a duplicate keeps the source's view mode + sort
        dup.View = src.View;
        dup.Sort = src.Sort;
        dup.SortDescending = src.SortDescending;
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

    /// <summary>Descend into the folder under the cursor (Ctrl+PageDown); ".." goes up.</summary>
    private void DescendCursor(char side)
    {
        var list = Rows(side);
        int cur = Cursor(side);
        if (cur < 0 || cur >= list.Count) return;
        var e = list[cur];
        if (e.IsParent) GoUp(side);
        else if (e.IsDir) NavigateTo(side, e.FullPath);
    }

    /// <summary>Cycle the preview panel Off → Side → Full (Ctrl+Q); persists.</summary>
    private void CyclePreview()
    {
        _settings.Preview = (PreviewMode)(((int)_settings.Preview + 1) % 3);
        _previewFocused = false;
        SettingsStore.Save(_settings);
        MarkDirty();
    }

    // ── Preview focus (Full mode) ────────────────────────────────────────
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
            case KeyCode.Up: _driveCur = Math.Max(0, _driveCur - 1); MarkDirty(); break;
            case KeyCode.Down: _driveCur = Math.Min(_drives.Count - 1, _driveCur + 1); MarkDirty(); break;
            case KeyCode.Home: _driveCur = 0; MarkDirty(); break;
            case KeyCode.End: _driveCur = _drives.Count - 1; MarkDirty(); break;
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
        return PathEquals(System.IO.Path.GetPathRoot(otherDir), root) ? otherDir : root;
    }

    // ── Keyboard ─────────────────────────────────────────────────────────
    private void HandleKeyDown(KeyDownEvent e)
    {
        var ctrl = e.Modifiers.HasFlag(KeyModifiers.Ctrl) || e.Modifiers.HasFlag(KeyModifiers.Super);
        var alt = e.Modifiers.HasFlag(KeyModifiers.Alt);
        if (_notice != null) { _notice = null; MarkDirty(); }   // transient — clears on the next key

        // Modal overlays own the keyboard while open. The palette keeps its query
        // field focused, so its handler consumes only nav/Enter/Escape and lets text
        // keys fall through to the field.
        // Recent-folders popover: Esc closes; Alt+←/→ still navigate (and close it).
        if (_historyPop is { } hpk)
        {
            if (e.Key == KeyCode.Escape) CloseHistoryPopover();
            else if (e.Key == KeyCode.Left && e.Modifiers.HasFlag(KeyModifiers.Alt)) { CloseHistoryPopover(); GoBack(hpk.side); }
            else if (e.Key == KeyCode.Right && e.Modifiers.HasFlag(KeyModifiers.Alt)) { CloseHistoryPopover(); GoForward(hpk.side); }
            e.StopPropagation = true; return;
        }
        // Most-modal first: the overwrite prompt and confirm dialog swallow keys.
        if (_overwriteOpen)
        {
            if (e.Key == KeyCode.Escape) CancelTransfer();
            else if (e.Key == KeyCode.Enter) OverwriteDecision(overwrite: true, applyToRest: false);
            e.StopPropagation = true; return;
        }
        if (_confirm is { } cf)
        {
            if (e.Key == KeyCode.Escape) { _confirm = null; MarkDirty(); }
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
            if (_rebindCmd is { } rc)
            {
                if (Keybindings.IsModifier(e.Key)) { e.StopPropagation = true; return; }
                if (e.Key != KeyCode.Escape) RebindTo(rc, ChordOf(e));   // Esc cancels
                _rebindCmd = null; MarkDirty(); e.StopPropagation = true; return;
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
        if (_paletteOpen) { HandlePaletteKey(e); return; }
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
        if (ctrl && e.Modifiers.HasFlag(KeyModifiers.Shift)
            && e.Key is KeyCode.E or KeyCode.O or KeyCode.D or KeyCode.K)
        {
            PaneState? s = e.Key switch
            {
                KeyCode.E => PaneState.Empty,
                KeyCode.O => PaneState.Offline,
                KeyCode.D => PaneState.Denied,
                _ => null,
            };
            if (_active == 'L') _forceStateL = s; else _forceStateR = s;
            MarkDirty(); e.StopPropagation = true; return;
        }

        // Ctrl+Shift+C copies the active pane's current folder path to the clipboard
        // (Explorer's "Copy as path"). Skipped in a text field, where it means copy text.
        if (ctrl && e.Modifiers.HasFlag(KeyModifiers.Shift) && e.Key == KeyCode.C
            && e.Target is not (TextInputElement or TextAreaElement))
        {
            CopyCurrentPath(_active); e.StopPropagation = true; return;
        }

        // Rebindable global commands (Ctrl/Alt combos) resolve through the keymap; the
        // fixed combos below aren't rebindable.
        if (MatchHotkey(e, wantGlobal: true, out var gcmd)) { Dispatch(gcmd); e.StopPropagation = true; return; }

        // Ctrl+1..9 jump the active pane to the Nth bookmark.
        if (ctrl && e.Key is >= KeyCode.Digit1 and <= KeyCode.Digit9)
        {
            JumpBookmark(e.Key - KeyCode.Digit1); e.StopPropagation = true; return;
        }

        // Ctrl+C / Ctrl+X / Ctrl+V file clipboard — but not while a text field (e.g. the
        // preview TextArea) is focused, where these mean copy/cut/paste text.
        if (ctrl && e.Key is KeyCode.C or KeyCode.X or KeyCode.V
            && e.Target is not (TextInputElement or TextAreaElement))
        {
            if (e.Key == KeyCode.V) PasteFromClipboard();
            else CopyToClipboard(cut: e.Key == KeyCode.X);
            e.StopPropagation = true; return;
        }

        // Ctrl+A marks everything in the active pane — except in a text field, where it
        // keeps its select-all-text meaning. Shift excluded: Ctrl+Shift+A is the (rebindable)
        // mark-by-pattern command, and must not fall through to mark-all if rebound away.
        if (ctrl && e.Key == KeyCode.A && !e.Modifiers.HasFlag(KeyModifiers.Shift)
            && e.Target is not (TextInputElement or TextAreaElement))
        {
            MarkAll(_active); e.StopPropagation = true; return;
        }

        // Ctrl+Tab / Ctrl+Shift+Tab cycle tabs.
        if (ctrl && e.Key == KeyCode.Tab)
        {
            CycleTab(_active, e.Modifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            e.StopPropagation = true; return;
        }

        // Alt+← / Alt+→ move back / forward through the active pane's history.
        if (alt && e.Key == KeyCode.Left) { GoBack(_active); e.StopPropagation = true; return; }
        if (alt && e.Key == KeyCode.Right) { GoForward(_active); e.StopPropagation = true; return; }

        // Alt+F1 / Alt+F2 open the drive picker for the left / right pane.
        if (alt && e.Key is KeyCode.F1 or KeyCode.F2)
        {
            OpenDrivePicker(e.Key == KeyCode.F1 ? 'L' : 'R'); e.StopPropagation = true; return;
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

        // Brief / Thumbnail modes remap the arrow + paging keys (column / grid motion)
        // before the Detail switch, so e.g. Left navigates instead of opening the command line.
        if (ViewNav(side, e.Key)) { e.StopPropagation = true; return; }

        switch (e.Key)
        {
            case KeyCode.Tab:
                ExitSearch(_active); _active = _active == 'L' ? 'R' : 'L';
                MarkDirty(); e.StopPropagation = true; return;
            case KeyCode.Escape:
                if (_search.Length > 0) { ExitSearch(side); MarkDirty(); e.StopPropagation = true; }
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

    /// <summary>Test-hosting seam: true once the visible pane(s) have loaded. In Full
    /// preview only the active pane renders, so only it needs to be loaded.</summary>
    public bool ListingsReady => _settings.Preview == PreviewMode.Full
        ? (_active == 'L' ? _loadedL : _loadedR)
        : _loadedL && _loadedR;

    private void OnWindowResized()
    {
        // A scale change retires the cached rasters; rebuild so every element picks up
        // sources cut for the new resolution instead of holding retired ones.
        float scale = _window.DisplayScale;
        if (MathF.Abs(Icons.DisplayScale - scale) < 0.001f) return;
        Icons.DisplayScale = scale;
        MarkDirty();
    }

    public void Dispose()
    {
        _window.Resized -= OnWindowResized;
        SaveSession();
        (_lastImage as IDisposable)?.Dispose();
        _thumbsCache?.Dispose();
        _holdTimer?.Dispose();
        ArchiveVfs.CleanupTemp();   // remove any nested-archive temp extractions
    }

    /// <summary>The view mode saved at index <paramref name="i"/> (or Detail when absent).</summary>
    private static ViewMode ViewAt(List<int>? views, int i) =>
        views != null && i < views.Count && Enum.IsDefined((ViewMode)views[i]) ? (ViewMode)views[i] : ViewMode.Detail;

    /// <summary>Persist the current tabs (dirs + per-tab view mode) so "Restore tabs on
    /// start" can bring them back exactly.</summary>
    private void SaveSession() => SessionStore.Save(new SessionData(
        _tabsL.Select(t => t.Dir).ToList(), _tabL,
        _tabsR.Select(t => t.Dir).ToList(), _tabR,
        _tabsL.Select(t => (int)t.View).ToList(),
        _tabsR.Select(t => (int)t.View).ToList()));
}
