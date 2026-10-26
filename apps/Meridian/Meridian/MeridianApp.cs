using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using Meridian.Core;
using Meridian.Theme;

namespace Meridian;

/// <summary>The five views on the rail, keys 1–5 (the order the design fixed).</summary>
public enum MeridianView { Requests, Runner, Discovery, History, Environments }

/// <summary>
/// Root of Meridian, the HTTP/API client: an Azimuth-themed shell — title bar,
/// 48dip view rail, five views — over a collection of plain text files on disk.
///
/// <para>This is the P0 scaffold: the shell shape, the front door, and the view
/// switching are real; the views themselves land phase by phase (see
/// IMPLEMENTATION-PLAN.md). A view whose content hasn't landed shows its designed
/// empty state, never a dead control.</para>
/// </summary>
public partial class MeridianApp : Component, IDisposable
{
    private readonly Application _app;
    private readonly Window _window;
    private readonly IFileDialogs _fileDialogs;

    [Reactive] public partial MeridianView View { get; set; }

    /// <summary>The opened collection, or null while the front door is up.</summary>
    private CollectionWorkspace? _workspace;
    private CollectionTree? _tree;
    private CollectionWatcher? _watcher;

    // All environments, loaded as editable documents (the compare table edits
    // every column). _envName is the active one — null is the legal
    // "no environment" state.
    private readonly List<EnvFileRef> _envFiles = [];
    private readonly Dictionary<string, EnvDocument> _envDocs = new(StringComparer.Ordinal);
    private string? _envName;
    private EnvDocument? _env => _envName != null ? _envDocs.GetValueOrDefault(_envName) : null;
    internal string? ActiveEnvName => _envName;

    private readonly IKeyring _keyring;

    // The one secret whose value is momentarily revealed, and until when.
    private (string Key, long UntilTs)? _reveal;

    // Open requests. One engine for the whole session — it owns the connection
    // pool, the cookie jar, and the workspace's trusted certificates.
    internal readonly HttpEngine Engine;
    private readonly List<RequestTab> _tabs = [];
    private int _activeTab = -1;

    internal sealed class RequestTab(string path, ReqDocument doc)
    {
        public string Path { get; } = path;
        public ReqDocument Doc { get; set; } = doc;
        public bool Dirty => Doc.Edited;
        public SendResult? Result;
        public CancellationTokenSource? Sending;
        public long SendStartedTs;
        /// <summary>The file changed on disk while this tab had unsaved edits —
        /// show the reload/keep-mine bar.</summary>
        public bool ExternalChanged;
        public EditorTab SubTab = EditorTab.Headers;
        public bool UrlEditing;
        public ResponseTab RespTab = ResponseTab.Body;
        public bool RespRaw;
        /// <summary>The document as it read when the last response arrived —
        /// what the stale-preview banner compares against.</summary>
        public string? ResultSnapshot;
    }

    internal enum EditorTab { Params, Headers, Body, Auth, Assertions, Chain }

    // The extracted-variable store: what runs produced, when. Session state —
    // never serialized (the files hold the *recipes*, not the values).
    internal readonly Dictionary<string, string> Extracted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _extractedRun = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Path, string Variable), int> _extractLastMatched = new();
    private int _runCounter;
    internal enum ResponseTab { Body, Headers, Cookies, Timeline }

    // Response pane orientation: right 560dip by default, Ctrl+\ flips to
    // bottom 380dip, and below the breakpoint bottom is forced regardless.
    //
    // The breakpoint is the narrowest window that leaves the editor EditorMinWidth beside
    // the pane: rail 48 + tree 264 + editor 448 + pane 560 = 1320 dip. 448 holds the six
    // sub-tabs of a request with counts ("Params 4 · Headers 1 · Body · Auth · Assertions 1 ·
    // Chain 3", 445 px with the strip's padding) without scrolling them, and leaves the URL
    // box 227 px beside GET and 203 beside OPTIONS: the URL bar's floor is the method chip
    // (46 px for GET, 70 for OPTIONS), Send with its Ctrl+Enter keycap (135) and 40 px of
    // padding and gaps. It was 1100 until 2026-10-08, when the user approved the move: that
    // left the editor 228 px, the URL bar's floor with almost no URL beside Send.
    internal const float EditorMinWidth = 448f;
    internal const float ForceBottomWindowWidth =
        Azimuth.RailWidth + Azimuth.TreeWidth + EditorMinWidth + Azimuth.ResponseRight;

    // At the bottom the pane is its designed 380 dip wherever the window leaves the editor
    // above it EditorMinHeight; in a shorter window the pane gives way, down to its own floor,
    // and the editor keeps its own. That is every window under 736 dip tall: since the
    // breakpoint moved to 1320 those include a 1080p screen at 150 %, maximized (1280x672),
    // where a fixed 380 left the editor 252 and its sub-editor 112, the Body box 92 px.
    //
    // The editor's floor is its chrome and five form rows: tabs 34 + URL bar 48 + sub-tabs
    // 32 + footer 26, and the sub-editor's 8 px top padding + 5 rows of 32 with 2 px between
    // them = 316 dip. The pane's: status line 32 + its tabs 30 + the body's padding 2 x 8 and
    // six lines of the 12 px body (14 each) = 162 dip. Both add up to 478, which fits under
    // the title bar of the smallest window Meridian allows (540 - 40 = 500): there both floors
    // hold, with 22 dip to spare for the pane. (The two floors are flex minimums, so in a
    // window shorter than that, which only a headless one can be, the pane would run past
    // the bottom rather than take the editor's URL bar.)
    internal const float EditorMinHeight = 34 + 48 + 32 + 26 + Azimuth.S2 + 5 * Azimuth.RowForm + 4 * 2;
    internal const float BottomPaneMinHeight = 32 + 30 + 2 * Azimuth.S2 + 6 * 14;
    internal const int MinWindowWidth = 760;
    internal const int MinWindowHeight = 540;
    private bool _responseBottomChoice;
    private bool _forceResponseBottom;
    internal bool ResponseAtBottom => _responseBottomChoice || _forceResponseBottom;

    // Small anchored popover (method picker). Junction's idiom.
    private IReadOnlyList<(string Label, Action Run)>? _popover;
    private float _popoverX, _popoverY;

    // Bumped to force a *reactive* rebuild (via the build Effect) rather than the
    // untracked MarkDirty path. Nothing subscribes through it yet in P0, but the
    // async loaders from P1 on will — same arrangement as the other apps. The parts are
    // built again too (see Part).
    private readonly Signal<int> _revision = new(0);
    private void Rerender()
    {
        _partRevision.Value++;
        _revision.Value++;
    }

    // Toast.
    private readonly ToastHost _toasts;

    private bool _pickingFolder;

    public MeridianApp(Application app, Window window, IFileDialogs? fileDialogs = null,
        int viewerCapBytes = 1024 * 1024,
        IKeyring? keyring = null)
    {
        _app = app;
        _window = window;
        // The built-in widgets read the Application theme rather than the colours this app
        // paints with, and TextInputElement.ApplyTheme re-reads it on every style pass — so
        // an unthemed Application leaves every text box and text area flat white in an
        // otherwise Azimuth window. Program.cs sets this too, before the first frame; doing
        // it here as well means anything that hosts MeridianApp directly (tests, the
        // screenshot driver) gets the same window the user sees, rather than a frame that
        // reads as a theming bug the app does not have. Mirrors JunctionApp and CommanderApp.
        _app.Theme = Azimuth.ToHultTheme();
        _toasts = new ToastHost(MarkDirty) { Seconds = 2.6f };
        // The window's dialogs are the real ones on a desktop and the scriptable
        // headless pair under test, so nothing needs substituting for either.
        _fileDialogs = fileDialogs ?? window.FileDialogs;
        _keyring = keyring ?? new DefaultKeyring();
        Engine = new HttpEngine(viewerCapBytes);
        _oauth = new OAuth2(Engine);
        _responseFind = NewResponseFind();
        // History's search, walk and filters show in the History view alone (see Part).
        _historyFind = new TextSearch(MarkViewDirty);

        View = MeridianView.Requests;

        Ui.DisplayScale = _window.DisplayScale;
        _forceResponseBottom = _window.LayoutWidth < ForceBottomWindowWidth;
        _window.Resized += OnWindowResize;

        // Global keys land on the host when nothing is focused. Materialise the
        // host so the handler is wired.
        Mount().On<KeyDownEvent>(HandleKeyDown);
    }



    // A monitor change arrives as a resize; retire the icon rasters cached for
    // the old scale and rebuild, or every icon disappears until the next build.
    // The response pane's forced-bottom breakpoint re-evaluates here too.
    private void OnWindowResize()
    {
        bool changed = false;
        float scale = _window.DisplayScale;
        if (MathF.Abs(Ui.DisplayScale - scale) >= 0.001f)
        {
            Ui.DisplayScale = scale;
            changed = true;
        }
        bool force = _window.LayoutWidth < ForceBottomWindowWidth;
        if (force != _forceResponseBottom)
        {
            _forceResponseBottom = force;
            changed = true;
        }
        if (changed) Rerender();
    }

    internal CollectionWorkspace? Workspace => _workspace;

    /// <summary>Builds so far, for tests: each one makes the whole window again.</summary>
    internal int Builds { get; private set; }

    public override Element Build()
    {
        Builds++;
        // Track the revision signal so a Rerender() re-runs Build through the Effect.
        _ = _revision.Value;
        _responsePlaced = false;

        // The title bar is a part of its own (see Part), keyed by its host.
        var titleBar = TitleBarPart.Mount().FlexShrink(0);
        var body = BuildBody(); body.Key = "body";
        // The response panel keeps the readout of a send in flight (TickWhileSending) while it is
        // kept; with no panel on screen there is none.
        if (!_responsePlaced) _sendElapsed.Current = null;
        var root = Flex.Column(gap: 0)
            .FlexGrow(1)
            .Background(Azimuth.Bg)
            .TextColor(Azimuth.Text)
            .FontSize(12f)
            .Children(titleBar, body);

        // Overlays are conditional; keys keep each reconciling only against its
        // own kind rather than by position.
        if (_optionsOpen)
        {
            var options = BuildOptions();
            options.Key = "options";
            root.AddChild(options);
        }
        if (_dialog is { } dialogSpec)
        {
            var dialog = BuildDialog(dialogSpec);
            dialog.Key = "dialog";
            root.AddChild(dialog);
        }
        if (_popover != null)
        {
            var popover = BuildPopover(_popover);
            popover.Key = "popover";
            root.AddChild(popover);
        }
        if (_toasts.Build() is { } toast) root.AddChild(toast);

        return root;
    }

    internal void FlipResponsePane()
    {
        if (_forceResponseBottom) { Flash("Too narrow — the response pane stays at the bottom"); return; }
        _responseBottomChoice = !_responseBottomChoice;
        MarkDirty();
    }

    protected override void OnAfterBuild()
    {
        if (_pendingUrlFocus)
        {
            _pendingUrlFocus = false;
            _urlRef.Current?.Focus();
        }
        if (_pendingDialogFocus)
        {
            _pendingDialogFocus = false;
            _dialogRef.Current?.Focus();
        }
        _responseFind.Service();
    }

    private Element BuildBody()
    {
        var body = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        body.AddChild(RailPart.Mount().FlexShrink(0));

        Element view = View switch
        {
            MeridianView.Requests => BuildRequestsView(),
            MeridianView.Runner => BuildRunnerView(),
            MeridianView.Discovery => BuildDiscoveryView(),
            MeridianView.History => BuildHistoryView(),
            MeridianView.Environments => BuildEnvironmentsView(),
            _ => BuildRequestsView(),
        };
        view.Key = "view-" + View;
        body.AddChild(view);
        return body;
    }

    // ── Actions ──────────────────────────────────────────────────────────

    private void SetView(MeridianView v)
    {
        if (View == v) return;
        View = v;   // reactive → rebuild
    }

    private async void OpenFolder()
    {
        if (_pickingFolder) return;
        _pickingFolder = true;
        try
        {
            var picked = await _fileDialogs.OpenFolderAsync(_workspace?.Root);
            if (picked == null) return;
            OpenWorkspace(picked);
        }
        finally
        {
            _pickingFolder = false;
        }
    }

    /// <summary>Test seam: what the picker callback does once a path is chosen.</summary>
    internal void OpenWorkspace(string path)
    {
        if (CollectionWorkspace.TryOpen(path) is not { } ws)
        {
            Flash($"Not a folder: {path}");
            return;
        }
        _workspace = ws;
        StopResponseWork();
        _tabs.Clear();
        _activeTab = -1;
        RescanWorkspace();
        LoadTrustedCertificates(ws);
        _watcher?.Dispose();
        _watcher = new CollectionWatcher(ws.Root,
            () => Application.InvokeOnMainThread(OnWorkspaceChangedOnDisk));
        SetView(MeridianView.Requests);
        Rerender();
    }

    private void RescanWorkspace()
    {
        if (_workspace is not { } ws) return;
        _tree = CollectionScanner.Scan(ws);

        // Reload every environment document — except ones with unsaved edits,
        // which survive the rescan (the disk copy would clobber the table).
        _envFiles.Clear();
        _envFiles.AddRange(_tree.Environments);
        var gone = _envDocs.Keys.Where(name => _envFiles.All(e => e.Name != name)).ToList();
        foreach (var name in gone) _envDocs.Remove(name);
        foreach (var env in _envFiles)
        {
            if (_envDocs.TryGetValue(env.Name, out var existing) && existing.Edited) continue;
            if (ReadEnv(env.Path) is { } doc) _envDocs[env.Name] = doc;
        }

        // Pick the active environment: keep the current choice while it exists,
        // else prefer "local", else the first. Null (no environment) is kept
        // only if it was an explicit choice — a fresh open picks one.
        if (_envName == null || !_envDocs.ContainsKey(_envName))
        {
            if (!_noEnvironmentChosen)
                _envName = _envDocs.ContainsKey("local") ? "local" : _envFiles.FirstOrDefault()?.Name;
            else
                _envName = null;
        }
    }

    private bool _noEnvironmentChosen;

    internal void SelectEnvironment(string? name)
    {
        _envName = name;
        _noEnvironmentChosen = name == null;
        MarkDirty();
    }

    internal string EnvPath(string name) => _envFiles.First(e => e.Name == name).Path;

    // ── Workspace certificate trust (.meridian/trusted.certs) ────────────
    // One thumbprint per line. The mock's "trust for this workspace" action:
    // scoped to the collection, serialized as text like everything else.

    private static string TrustFile(CollectionWorkspace ws) =>
        Path.Combine(ws.Root, ".meridian", "trusted.certs");

    private void LoadTrustedCertificates(CollectionWorkspace ws)
    {
        if (TryReadFile(TrustFile(ws)) is not { } text) return;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!line.StartsWith('#'))
                Engine.TrustCertificate(line);
    }

    /// <summary>The TLS state's action: trust the rejected certificate for this
    /// workspace, persist it, and send again.</summary>
    internal void TrustAndResend(string thumbprint)
    {
        Engine.TrustCertificate(thumbprint);
        if (_workspace is { } ws)
        {
            try
            {
                var file = TrustFile(ws);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.AppendAllText(file, thumbprint + "\n");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Flash($"Trusted for this session only — could not write: {e.Message}");
            }
        }
        SendActive();
    }

    private static EnvDocument? ReadEnv(string path)
    {
        try { return EnvDocument.Parse(File.ReadAllText(path)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The watcher saw the workspace change (already marshalled to the
    /// main thread). Rescan the tree, and bring open tabs along: a clean tab
    /// reloads silently, a dirty one gets the reload/keep-mine bar.</summary>
    private void OnWorkspaceChangedOnDisk()
    {
        RescanWorkspace();
        foreach (var tab in _tabs)
        {
            string? onDisk = TryReadFile(tab.Path);
            if (onDisk == null || onDisk == tab.Doc.Serialize()) continue;   // our own save, or gone
            if (!tab.Dirty)
                tab.Doc = ReqDocument.Parse(onDisk);
            else
                tab.ExternalChanged = true;
        }
        Rerender();
    }

    private static string? TryReadFile(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    internal RequestTab? ActiveTab => _activeTab >= 0 && _activeTab < _tabs.Count ? _tabs[_activeTab] : null;
    internal int OpenTabCount => _tabs.Count;

    internal void OpenRequest(string path)
    {
        int existing = _tabs.FindIndex(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) { _activeTab = existing; MarkDirty(); return; }
        if (TryReadFile(path) is not { } text) { Flash($"Could not read {Path.GetFileName(path)}"); return; }
        _tabs.Add(new RequestTab(path, ReqDocument.Parse(text)));
        _activeTab = _tabs.Count - 1;
        MarkDirty();
    }

    private void CloseActiveTab()
    {
        if (ActiveTab is not { } tab) return;
        tab.Sending?.Cancel();
        StopResponseWork(tab);
        _tabs.RemoveAt(_activeTab);
        if (_activeTab >= _tabs.Count) _activeTab = _tabs.Count - 1;
        MarkDirty();
    }

    private void SaveActiveTab()
    {
        if (ActiveTab is not { } tab || !tab.Dirty) return;
        try
        {
            File.WriteAllText(tab.Path, tab.Doc.Serialize());
            tab.Doc.MarkSaved();
            tab.ExternalChanged = false;
            RescanWorkspace();   // method/name in the tree may have changed
            MarkDirty();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Flash($"Could not save: {e.Message}");
        }
    }

    private void Flash(string message) => _toasts.Flash(message);

    // ── Send / cancel ────────────────────────────────────────────────────

    /// <summary>The resolver for the current environment. A secret key's file
    /// value is only a placeholder: the real value comes from the keyring (or a
    /// CI-injected environment variable), and a secret this machine doesn't
    /// have stays unresolved — visibly.</summary>
    internal VariableResolver Resolver() =>
        new(environment: EnvironmentValues(), extracted: Extracted);

    /// <summary>The active environment's effective values — file values, with
    /// secrets swapped for their keyring/env-var values (or absent when this
    /// machine has none). What the editor's resolver and the runner both eat.</summary>
    internal Dictionary<string, string> EnvironmentValues()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_env is not { } env || _workspace is not { } ws) return values;
        foreach (var key in env.Keys)
        {
            if (env.IsSecret(key))
            {
                if (_keyring.Get(ws.Root, key) is { } secret) values[key] = secret;
            }
            else if (env.Values.TryGetValue(key, out var value))
                values[key] = value;
        }
        return values;
    }

    private async void SendActive()
    {
        if (ActiveTab is not { } tab || tab.Sending != null) return;
        if (tab.Doc.Method is not { } method || tab.Doc.Url is not { } urlTemplate)
        {
            Flash("This file has no request line");
            return;
        }

        var resolver = Resolver();
        var url = resolver.Resolve(urlTemplate);
        var headers = new List<(string, string)>();
        var unresolved = new List<string>(url.Unresolved);
        foreach (var h in tab.Doc.Headers)
        {
            var value = resolver.Resolve(h.Value);
            unresolved.AddRange(value.Unresolved);
            headers.Add((h.Name, value.Text));
        }
        string? bodyText = null;
        if (tab.Doc.Body is { } body && body.Content.Length > 0)
        {
            var resolved = resolver.Resolve(body.Content);
            unresolved.AddRange(resolved.Unresolved);
            bodyText = resolved.Text;
        }
        if (unresolved.Count > 0)
        {
            // The mock's fresh-clone state: a flagged-secret key gets its own
            // copy, naming the machine-local fix rather than the environment.
            var missingSecret = unresolved.Distinct().FirstOrDefault(n => _env?.IsSecret(n) == true);
            Flash(missingSecret != null
                ? $"secret {missingSecret} not set on this machine — set it in Environments (5)"
                : $"Unresolved in env {_envName ?? "(none)"}: {string.Join(", ", unresolved.Distinct().Select(n => $"{{{{{n}}}}}"))}");
            return;
        }

        // Auth last, so an explicit Authorization header on the request wins
        // and OAuth2 only fetches a token once everything else resolved.
        var cts = tab.Sending = new CancellationTokenSource();
        tab.SendStartedTs = System.Diagnostics.Stopwatch.GetTimestamp();
        MarkDirty();
        TickWhileSending(tab, cts);
        try
        {
            if (await ApplyAuthAsync(tab, resolver, headers, cts.Token) is { } authError)
            {
                Flash(authError);
                return;
            }
            var spec = new RequestSpec(method, ApplyAuthQuery(tab, resolver, url.Text), headers, bodyText,
                bodyText != null && tab.Doc.Body!.Type == "json" ? "application/json" : null);
            tab.Result = await Task.Run(() => Engine.SendAsync(spec, cts.Token));
            tab.ResultSnapshot = tab.Doc.Serialize();
            RecordHistory(method, url.Text, tab.Result);
            if (tab.Result.Response is { } responseData)
            {
                ApplyExtractions(tab, responseData);
                // Exploring by hand is discovery's other input.
                RecordInMap(method, url.Text, responseData);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is UriFormatException or InvalidOperationException or ArgumentException)
        {
            tab.Result = null;
            Flash($"Bad request: {e.Message}");
        }
        finally
        {
            tab.Sending = null;
            MarkDirty();
        }
    }

    private void CancelActive()
    {
        ActiveTab?.Sending?.Cancel();
    }

    /// <summary>The elapsed readout of the shown tab's send in flight, which the tick below
    /// rewrites in place; null when the last build showed none.</summary>
    private readonly Ref<TextElement> _sendElapsed = new();

    /// <summary>Whether the build under way placed the response panel (which sets
    /// <see cref="_sendElapsed"/> when it builds).</summary>
    private bool _responsePlaced;

    /// <summary>
    /// Keeps the elapsed readout current while <paramref name="sending"/> is in flight, by
    /// rewriting the readout on screen when its tab is the one shown. Started on the UI thread,
    /// so the awaits resume there too.
    ///
    /// <para>It rebuilt the whole window every 150 ms instead, about seven times a second for as
    /// long as a request took; and a tick waiting when its send ended and the next one began
    /// went on ticking beside the next one's, so sends in a row stacked rebuilds.</para>
    /// </summary>
    private async void TickWhileSending(RequestTab tab, CancellationTokenSource sending)
    {
        while (tab.Sending == sending)
        {
            await Task.Delay(150);
            if (tab.Sending == sending && ActiveTab == tab && _sendElapsed.Current is { } readout)
                readout.Content = ElapsedText(tab);
        }
    }

    internal TimeSpan SendElapsed(RequestTab tab) =>
        System.Diagnostics.Stopwatch.GetElapsedTime(tab.SendStartedTs);

    /// <summary>The elapsed readout's text: seconds, to a tenth.</summary>
    internal string ElapsedText(RequestTab tab) => $"{SendElapsed(tab).TotalSeconds:0.0} s";

    // ── Keyboard ─────────────────────────────────────────────────────────

    private void HandleKeyDown(KeyDownEvent e)
    {
        var ctrl = e.Modifiers.HasFlag(KeyModifiers.Ctrl) || e.Modifiers.HasFlag(KeyModifiers.Super);
        var typing = e.Target is TextInputElement or TextAreaElement;

        if (_optionsOpen && e.Key == KeyCode.Escape)
        {
            _optionsOpen = false;
            MarkDirty();
            e.StopPropagation = true;
            return;
        }

        // A modal dialog owns Enter/Esc while it is up — before anything else.
        if (_dialog != null)
        {
            if (e.Key == KeyCode.Enter) { SubmitDialog(); e.StopPropagation = true; }
            else if (e.Key == KeyCode.Escape) { CancelDialog(); e.StopPropagation = true; }
            return;
        }

        // The response find bar's keys route ahead of everything else — the
        // Junction discipline, so Esc closes the bar before it cancels a send
        // and n/p walk matches instead of typing.
        if (View == MeridianView.Requests && ActiveTab?.Result?.Response != null
            && _responseFind.HandleKey(e))
        {
            e.StopPropagation = true;
            return;
        }

        // Ctrl combos work everywhere, including while typing in an input. Matched through
        // Shortcuts, which the hints read too: three keys they named did nothing here.
        if (CtrlShortcut(e)) { e.StopPropagation = true; return; }
        if (ctrl) return;

        if (e.Key == KeyCode.Escape && RunnerRunning)
        {
            AbortRun();
            e.StopPropagation = true;
            return;
        }

        if (e.Key == KeyCode.Escape && ActiveTab?.Sending != null)
        {
            CancelActive();
            e.StopPropagation = true;
            return;
        }

        if (typing) return;

        // The History view walks its rows with the arrows (the mock's key map).
        if (View == MeridianView.History && HandleHistoryKey(e))
        {
            e.StopPropagation = true;
            return;
        }

        // Number keys switch views.
        var viewForKey = !Shortcuts.Pressed("view", e) ? null : e.Digit switch
        {
            1 => MeridianView.Requests,
            2 => MeridianView.Runner,
            3 => MeridianView.Discovery,
            4 => MeridianView.History,
            5 => MeridianView.Environments,
            _ => (MeridianView?)null,
        };
        if (viewForKey is { } v) { SetView(v); e.StopPropagation = true; }
    }

    /// <summary>The Ctrl shortcuts, which act from anywhere. True when one acted.</summary>
    private bool CtrlShortcut(KeyDownEvent e)
    {
        if (Shortcuts.Pressed("open", e)) { OpenFolder(); return true; }
        if (Shortcuts.Pressed("palette", e)) { TogglePalette(); return true; }
        if (Shortcuts.Pressed("env", e) && _workspace != null) { OpenEnvSwitcher(); return true; }
        if (Shortcuts.Pressed("save", e))
        {
            if (View == MeridianView.Environments) SaveEnvironments(); else SaveActiveTab();
            return true;
        }
        if (Shortcuts.Pressed("close", e) && ActiveTab != null) { CloseActiveTab(); return true; }
        if (Shortcuts.Pressed("send", e) && View == MeridianView.Requests) { SendActive(); return true; }
        if (Shortcuts.Pressed("flip", e) && View == MeridianView.Requests) { FlipResponsePane(); return true; }
        // From any view, as the palette's row does: it used to run only on the Runner view.
        if (Shortcuts.Pressed("run", e) && _workspace != null && !RunnerRunning)
        {
            SetView(MeridianView.Runner);
            StartRun();
            return true;
        }
        // Advertised by the palette and the front door, and never handled until now.
        if (Shortcuts.Pressed("import", e)) { OpenImportDialog(); return true; }
        return false;
    }

    private void OpenImportDialog() => OpenDialog("Import OpenAPI", "path to openapi.json", ImportSpecFile);

    public void Dispose()
    {
        _window.Resized -= OnWindowResize;
        foreach (var tab in _tabs) tab.Sending?.Cancel();
        StopResponseWork();
        _watcher?.Dispose();
        Engine.Dispose();
    }
}
