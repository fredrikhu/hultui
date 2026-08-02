using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
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
    private Settings _draft = new();

    // Incremental "type to find" for the active pane: word characters extend it and
    // jump the cursor to the first prefix match; any nav/Enter/Tab/Escape clears it.
    private string _search = "";

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
    private sealed record NamePromptSpec(string Title, string Subtitle, string ConfirmLabel, Func<string, string?> Commit);
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

        public TransferOp() { }
        public TransferOp(List<FileEntry> items)
        {
            Items = items;
            ItemBytes = new long[items.Count];
            Status = new byte[items.Count];
        }
    }
    private TransferOp? _op;          // set once a copy/move is confirmed and running
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

    // Off-thread text excerpt for the preview column, keyed by the focused file's
    // path + mtime + size (so editing a file refreshes its preview).
    private readonly AsyncLoader<string> _previewLoader = new("");

    // True while the read-only preview TextArea holds keyboard focus (Full mode), so
    // the pane draws the accent ring around it. Driven by its Focus/Blur handlers.
    private bool _previewFocused;

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
        try { return HultUI.Rendering.Skia.SkiaImageSource.FromFile(path); }
        catch { return null; }
    }

    private const float RailWidth = 44f;
    private float PreviewWidth => _settings.PreviewWidth;
    private float RowHeight => _settings.Density == Density.Compact ? 20f : 24f;

    public CommanderApp(Application app, Window window, string startDir, string? startDirRight = null)
    {
        _app = app;
        _window = window;
        _settings = SettingsStore.Load();
        Nocturne.SetAccent(_settings.Accent);
        _app.Theme = Nocturne.ToHultTheme();
        _tabsL.Add(new PaneTab(startDir));
        _tabsR.Add(new PaneTab(startDirRight ?? startDir));
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

    private PaneState PaneStateOf(char side) =>
        (side == 'L' ? _forceStateL : _forceStateR) ?? Loader(side).Value.State;

    /// <summary>The current (possibly stale-while-loading) listing for a side,
    /// kicking off / refreshing the off-thread load as a side effect of Build().</summary>
    private IReadOnlyList<FileEntry> Listing(char side)
    {
        var loader = Loader(side);
        var dir = Dir(side);
        loader.Load($"{dir}#{Gen(side)}",
            () => FileSystem.List(dir, _settings.ShowHidden, _settings.Sort, _settings.DirsFirst, _settings.SortDescending),
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
        body.Key = "body";

        var root = Flex.Column(gap: 0)
            .FlexGrow(1)
            .Background(Nocturne.Bg)
            .TextColor(Nocturne.Text)
            .FontSize(12.5f)
            .Children(BuildChrome(), body, BuildCommandLine());
        // A right-drag mark ends wherever the button is released — pointer-up bubbles
        // to the root even when the release lands off the rows (empty list, footer, …).
        root.On<PointerUpEvent>(ev => { if (ev.Button == 2) EndRightMark(); });

        // Overlays render above the shell, each keyed so conditional presence
        // reconciles by identity (a keyless swap would leak handlers across kinds).
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
        if (_pendingScrollL) { _pendingScrollL = false; ScrollCursorIntoView('L'); }
        if (_pendingScrollR) { _pendingScrollR = false; ScrollCursorIntoView('R'); }
        if (_pendingPaletteFocus) { _pendingPaletteFocus = false; _paletteRef.Current?.Focus(); }
        if (_pendingNamePromptFocus) { _pendingNamePromptFocus = false; _namePromptRef.Current?.Focus(); }
        if (_pendingFindFocus) { _pendingFindFocus = false; _findPatternRef.Current?.Focus(); }

        // Keep a browsing region focused so Tab traverses panes/preview predictably.
        // The framework consumes Tab for focus traversal; with nothing focused the
        // first Tab would just land on the already-active pane and appear to do nothing.
        else if (!AnyOverlayOpen && _window.FocusedElement is null)
            PaneRef(_active).Current?.Focus();
    }

    private void RequestScroll(char side)
    {
        if (side == 'L') _pendingScrollL = true; else _pendingScrollR = true;
    }

    private void ScrollCursorIntoView(char side)
    {
        // Fixed row height ⇒ band math, no per-row layout needed.
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

    // Name to select once a pending listing finishes loading (used when ascending
    // to highlight the directory we came out of). Index 0 = L, 1 = R.
    private readonly string?[] _pendingFocusChild = new string?[2];

    /// <summary>Enter/open the cursor entry: descend into a directory, ascend on "..".</summary>
    private void ActivateCursor(char side)
    {
        var list = Listing(side);
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
        var list = Listing(side);
        int cur = Cursor(side);
        if (cur < 0 || cur >= list.Count) return;
        var e = list[cur];
        if (e.IsDir || e.IsParent) return;
        string template = Icons.KindFor(e) == IconKind.Image ? _settings.ImageEditor : _settings.Editor;
        FileOps.LaunchEditor(template, e.FullPath);
    }

    private void MoveCursor(char side, int delta, bool clearSearch = true)
    {
        if (clearSearch) _search = "";
        var list = Listing(side);
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
        var list = Listing(side);
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
        var list = Listing(side);
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
        var list = Listing(side);
        var marks = Marks(side);
        if (marks.Count > 0)
            return list.Where(e => !e.IsParent && marks.Contains(e.Name)).ToList();
        int cur = Cursor(side);
        if (cur >= 0 && cur < list.Count && !list[cur].IsParent) return [list[cur]];
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
        if (cut)
        {
            ProcessMove();                 // synchronous move (reuses the overwrite gate)
            Clipboard.Clear();             // a cut is consumed by its paste
            _cutPending.Clear();
        }
        else
        {
            _progressOpen = true;
            MarkDirty();
            System.Threading.Tasks.Task.Run(() => RunCopy(op));
        }
    }

    // ── Incremental search ───────────────────────────────────────────────
    private void ExtendSearch(char side, char ch)
    {
        _search += ch;
        JumpToSearch(side);
        MarkDirty();
    }

    private void BackspaceSearch(char side)
    {
        if (_search.Length == 0) return;
        _search = _search[..^1];
        if (_search.Length > 0) JumpToSearch(side);
        MarkDirty();
    }

    /// <summary>Move the cursor to the first entry whose name starts with the search
    /// string (case-insensitive), skipping the ".." parent.</summary>
    private void JumpToSearch(char side)
    {
        var list = Listing(side);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].IsParent) continue;
            if (list[i].Name.StartsWith(_search, StringComparison.OrdinalIgnoreCase))
            {
                SetCursor(side, i);
                RequestScroll(side);
                return;
            }
        }
    }

    /// <summary>Map a KeyDown to the search character it contributes, or '\0' if it
    /// isn't a word character (letters, digits, and a few common name punctuation).</summary>
    private static char SearchChar(KeyCode key) => key switch
    {
        >= KeyCode.A and <= KeyCode.Z => (char)('a' + (key - KeyCode.A)),
        >= KeyCode.Digit0 and <= KeyCode.Digit9 => (char)('0' + (key - KeyCode.Digit0)),
        KeyCode.Period => '.',
        KeyCode.Minus => '-',
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
        || _findOpen;

    private void CloseOverlays()
    {
        _drivePicker = null;
        _paletteOpen = false;
        _bookmarksOpen = false;
        _confirm = null;
        _optionsOpen = false;
        _namePrompt = null;
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

    // ── Move (F6): synchronous confirm + transfer (moves are usually instant) ──
    private void StartMove()
    {
        char side = _active, other = side == 'L' ? 'R' : 'L';
        var items = OperationTargets(side).ToList();
        if (items.Count == 0) return;
        var dest = Dir(other);
        if (PathEquals(dest, Dir(side))) return;

        _confirm = new ConfirmSpec(
            $"Move {(items.Count == 1 ? "item" : "items")}",
            $"Move {SelectionLabel(side)} to\n{dest}",
            "Move", Danger: true,
            () =>
            {
                _confirm = null;
                _op = new TransferOp(items) { IsMove = true, Source = side, DestDir = dest };
                ProcessMove();
            });
        MarkDirty();
    }

    private void ProcessMove()
    {
        var op = _op;
        if (op == null) return;
        while (op.Index < op.Items.Count)
        {
            var e = op.Items[op.Index];
            var destPath = System.IO.Path.Combine(op.DestDir, e.Name);
            bool exists = FileOps.Exists(destPath);
            if (exists && op.OverwriteAll == null) { _overwriteOpen = true; MarkDirty(); return; }
            if (exists && op.OverwriteAll == false) { op.Skipped++; op.Index++; continue; }
            var err = FileOps.Move(e, op.DestDir, overwrite: exists);
            if (err != null) op.Failed++; else op.Done++;
            op.Index++;
        }
        FinishTransfer();
    }

    // ── Copy (F5): background streamed transfer with the progress overlay ──
    private void StartCopy()
    {
        char side = _active, other = side == 'L' ? 'R' : 'L';
        var items = OperationTargets(side).ToList();
        if (items.Count == 0) return;
        var dest = Dir(other);
        if (PathEquals(dest, Dir(side))) return;

        var op = new TransferOp(items) { IsMove = false, Source = side, DestDir = dest };
        _op = op;
        _progressOpen = true;
        MarkDirty();
        System.Threading.Tasks.Task.Run(() => RunCopy(op));
    }

    /// <summary>The copy worker — runs off the UI thread, reporting progress and
    /// blocking on the overwrite gate when it needs a decision.</summary>
    private void RunCopy(TransferOp op)
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

            var err = FileOps.CopyStreamed(e, op.DestDir, overwrite,
                onBytes: n => { op.FileBytesDone += n; op.TotalBytesDone += n; WaitWhilePaused(op); Repaint(); },
                cancelled: () => op.Cancelled || op.SkipCurrent);

            if (op.SkipCurrent && !op.Cancelled)
            {
                op.Status[i] = 3; op.Skipped++;
                op.TotalBytesDone += Math.Max(0, op.ItemBytes[i] - op.FileBytesDone);
            }
            else if (err == "cancelled") { break; }
            else if (err != null) { op.Status[i] = 4; op.Failed++; }
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

        if (op.Gate is { } gate)                 // background copy: hand the choice back
        {
            op.ConflictOverwrite = overwrite;
            MarkDirty();
            gate.Set();
            return;
        }

        // Synchronous move.
        if (overwrite) { var err = FileOps.Move(op.Items[op.Index], op.DestDir, true); if (err != null) op.Failed++; else op.Done++; }
        else op.Skipped++;
        op.Index++;
        ProcessMove();
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

    /// <summary>Delete (F8 / Delete): recycle the targets after a confirm.</summary>
    private void StartDelete()
    {
        char side = _active;
        var items = OperationTargets(side).ToList();
        if (items.Count == 0) return;
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
        foreach (var e in items)
        {
            if (_settings.Recycle) FileOps.Recycle(e.FullPath, e.IsDir);
            else FileOps.DeletePermanent(e.FullPath, e.IsDir);
        }
        _confirm = null;
        _genL++; _genR++;
        Marks(side).Clear();
        ClearMeasuredIn(Dir(side));       // a deleted folder's cached total is now stale
        MarkDirty();
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
            new("Open drive…", "Alt+F1", () => OpenDrivePicker(side)),
            new("Refresh", "Ctrl+R", () => RefreshPane(side)),
            new($"{(_settings.ShowHidden ? "Hide" : "Show")} hidden files", "Ctrl+H", ToggleHidden),
            new("Swap left and right panes", "Ctrl+U", SwapPanes),
            new("Open this folder in the other pane", "=", () => NavigateTo(other, Dir(side))),
            new("Focus other pane", "Tab", () => { _active = other; }),
            new("New folder…", "F7", () => OpenNewFolderPrompt(side)),
            new("Find files…", "Alt+F7", () => OpenFind(side)),
            new("Edit file in external editor", "F4", () => EditFocused(side)),
            new("Rename…", "Shift+F6", () => OpenRenamePrompt(side)),
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
        var parent = Directory.GetParent(Dir(side).TrimEnd(
            System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        if (parent == null) return;
        var leaving = new DirectoryInfo(Dir(side)).Name;
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
        _optionsOpen = true;
        MarkDirty();
    }

    private void SaveOptions()
    {
        _settings = _draft.Clone();
        SettingsStore.Save(_settings);
        Nocturne.SetAccent(_settings.Accent);      // retheme the accent ramp
        _app.Theme = Nocturne.ToHultTheme();        // refresh built-in widgets
        _optionsOpen = false;
        _genL++; _genR++;          // re-sort / re-filter with the new settings
        MarkDirty();
    }

    private void ResetOptions() { _draft = new Settings(); MarkDirty(); }

    /// <summary>Clicking a column header. Same column flips direction; a new column
    /// adopts a sensible default (Name ascending, Size/Date descending — big & newest
    /// first). Sort is global (both panes); persisted and re-applied by bumping gen.</summary>
    private void SetSort(SortMode col)
    {
        if (_settings.Sort == col)
            _settings.SortDescending = !_settings.SortDescending;
        else
        {
            _settings.Sort = col;
            _settings.SortDescending = col is SortMode.Size or SortMode.Date;
        }
        SettingsStore.Save(_settings);
        _genL++; _genR++;   // re-sort both panes with the new key/direction
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
        var list = Listing(side);
        int cur = Cursor(side);
        if (cur < 0 || cur >= list.Count) return;
        var e = list[cur];
        if (e.IsParent) return;                    // never rename ".."
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
        if (err != null) { _namePromptError = err; MarkDirty(); return; }
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
        if (err != null) return err;
        BumpGen(side);
        Marks(side).Clear();
        _pendingFocusChild[side == 'L' ? 0 : 1] = newName;
        return null;
    }

    private void SwapPanes()
    {
        (_tabsL, _tabsR) = (_tabsR, _tabsL);
        (_tabL, _tabR) = (_tabR, _tabL);
        _genL++; _genR++;
        _search = "";
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
        _active = side;
        SetTabIndex(side, index);
        _search = "";
        BumpGen(side);          // reload for the tab's folder
        RequestScroll(side);
        MarkDirty();
    }

    /// <summary>Open a new tab in <paramref name="side"/> on <paramref name="dir"/>
    /// (defaults to the active tab's folder), placed just after the active one.</summary>
    private void NewTab(char side, string? dir = null)
    {
        var tabs = Tabs(side);
        int at = TabIndex(side) + 1;
        tabs.Insert(at, new PaneTab(dir ?? Dir(side)));
        _active = side;
        SetTabIndex(side, at);
        _search = "";
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
        NewTab(side, tabs[index].Dir);
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
        var list = Listing(side);
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
        }
    }

    private void SelectDrive(char target, int index)
    {
        if (index >= 0 && index < _drives.Count && _drives[index].Ready)
        {
            _active = target;
            var root = _drives[index].Root;
            CloseOverlays();
            NavigateTo(target, root);
        }
    }

    // ── Keyboard ─────────────────────────────────────────────────────────
    private void HandleKeyDown(KeyDownEvent e)
    {
        var ctrl = e.Modifiers.HasFlag(KeyModifiers.Ctrl) || e.Modifiers.HasFlag(KeyModifiers.Super);
        var alt = e.Modifiers.HasFlag(KeyModifiers.Alt);

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
            switch (e.Key)
            {
                case KeyCode.Escape: _optionsOpen = false; MarkDirty(); e.StopPropagation = true; break;
                case KeyCode.Down: _optionsPage = Math.Min(OptionPages.Length - 1, _optionsPage + 1); MarkDirty(); e.StopPropagation = true; break;
                case KeyCode.Up: _optionsPage = Math.Max(0, _optionsPage - 1); MarkDirty(); e.StopPropagation = true; break;
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
        if (_bookmarksOpen) { HandleBookmarksKey(e); e.StopPropagation = true; return; }
        if (_drivePicker is char dpKey)
        {
            HandleDrivePickerKey(dpKey, e); e.StopPropagation = true; return;
        }

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

        // ⌘P / Ctrl+P toggles the command palette from anywhere.
        if (ctrl && e.Key == KeyCode.P) { TogglePalette(); e.StopPropagation = true; return; }

        // Ctrl+D opens the bookmark manager.
        if (ctrl && e.Key == KeyCode.D) { OpenBookmarks(); e.StopPropagation = true; return; }

        // Ctrl+, opens the Options window.
        if (ctrl && e.Key == KeyCode.Comma) { OpenOptions(); e.StopPropagation = true; return; }

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

        // Tabs: Ctrl+T new · Ctrl+W close · Ctrl+Tab / Ctrl+Shift+Tab cycle.
        if (ctrl && e.Key == KeyCode.T) { NewTab(_active); e.StopPropagation = true; return; }
        if (ctrl && e.Key == KeyCode.W) { CloseTab(_active, TabIndex(_active)); e.StopPropagation = true; return; }
        if (ctrl && e.Key == KeyCode.Tab)
        {
            CycleTab(_active, e.Modifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            e.StopPropagation = true; return;
        }

        // Folder navigation: Ctrl+PageUp goes up one folder, Ctrl+PageDown descends
        // into the selected folder (Total Commander style).
        if (ctrl && e.Key == KeyCode.PageUp) { GoUp(_active); e.StopPropagation = true; return; }
        if (ctrl && e.Key == KeyCode.PageDown) { DescendCursor(_active); e.StopPropagation = true; return; }

        // Ctrl+Q cycles the preview panel: off → side → full.
        if (ctrl && e.Key == KeyCode.Q) { CyclePreview(); e.StopPropagation = true; return; }

        // Alt+← / Alt+→ move back / forward through the active pane's history.
        if (alt && e.Key == KeyCode.Left) { GoBack(_active); e.StopPropagation = true; return; }
        if (alt && e.Key == KeyCode.Right) { GoForward(_active); e.StopPropagation = true; return; }

        // Alt+F1 / Alt+F2 open the drive picker for the left / right pane (global,
        // so they fire even from a focused field once fields exist).
        if (alt && e.Key is KeyCode.F1 or KeyCode.F2)
        {
            OpenDrivePicker(e.Key == KeyCode.F1 ? 'L' : 'R'); e.StopPropagation = true; return;
        }

        // Alt+F7 opens the find-files dialog scoped to the active pane's folder.
        if (alt && e.Key == KeyCode.F7) { OpenFind(_active); e.StopPropagation = true; return; }

        // Alt+Shift+Enter measures every folder in the active pane at once.
        if (alt && e.Modifiers.HasFlag(KeyModifiers.Shift) && e.Key == KeyCode.Enter)
        {
            MeasureAllDirs(_active); e.StopPropagation = true; return;
        }

        var typing = e.Target is TextInputElement or TextAreaElement;
        if (typing) return;

        char side = _active;

        // Backspace goes up a folder (when not editing / searching).
        if (e.Key == KeyCode.Backspace && _search.Length == 0)
        {
            GoUp(side); e.StopPropagation = true; return;
        }

        switch (e.Key)
        {
            case KeyCode.Tab:
                _search = ""; _active = _active == 'L' ? 'R' : 'L';
                MarkDirty(); e.StopPropagation = true; return;
            case KeyCode.Escape:
                if (_search.Length > 0) { _search = ""; MarkDirty(); e.StopPropagation = true; }
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
                _search = ""; ActivateCursor(side); e.StopPropagation = true; return;
            case KeyCode.Backspace:
                BackspaceSearch(side); e.StopPropagation = true; return;
            case KeyCode.F4:
                EditFocused(side); e.StopPropagation = true; return;
            case KeyCode.F5:
                StartCopy(); e.StopPropagation = true; return;
            case KeyCode.F6:
                if (e.Modifiers.HasFlag(KeyModifiers.Shift)) OpenRenamePrompt(side);
                else StartMove();
                e.StopPropagation = true; return;
            case KeyCode.F7:
                OpenNewFolderPrompt(side); e.StopPropagation = true; return;
            case KeyCode.F8 or KeyCode.Delete:
                StartDelete(); e.StopPropagation = true; return;
        }

        // Type-to-find: a plain word character starts / extends the incremental
        // search (no Ctrl/Alt, so it doesn't swallow shortcuts added later).
        if (!ctrl && !alt)
        {
            char ch = SearchChar(e.Key);
            if (ch != '\0') { ExtendSearch(side, ch); e.StopPropagation = true; }
        }
    }

    /// <summary>Test-hosting seam: true once the visible pane(s) have loaded. In Full
    /// preview only the active pane renders, so only it needs to be loaded.</summary>
    public bool ListingsReady => _settings.Preview == PreviewMode.Full
        ? (_active == 'L' ? _loadedL : _loadedR)
        : _loadedL && _loadedR;

    public void Dispose()
    {
        (_lastImage as IDisposable)?.Dispose();
    }
}
