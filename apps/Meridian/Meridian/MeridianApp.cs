using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using Meridian.Core;
using Meridian.Platform;
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
    private readonly IFolderPicker _folderPicker;

    [Reactive] public partial MeridianView View { get; set; }

    /// <summary>The opened collection, or null while the front door is up.</summary>
    private CollectionWorkspace? _workspace;
    private CollectionTree? _tree;
    private CollectionWatcher? _watcher;

    // The active environment: picked automatically on open ("local" preferred),
    // shown in the titlebar. The switcher popover arrives with P4.
    private EnvDocument? _env;
    private string? _envName;

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
    }

    internal enum EditorTab { Params, Headers, Body }
    internal enum ResponseTab { Body, Headers, Cookies, Timeline }

    // Response pane orientation: right 560dip by default, Ctrl+\ flips to
    // bottom 380dip, and below the breakpoint bottom is forced regardless.
    private const float ForceBottomWindowWidth = 1100f;
    private bool _responseBottomChoice;
    private bool _forceResponseBottom;
    internal bool ResponseAtBottom => _responseBottomChoice || _forceResponseBottom;

    // Small anchored popover (method picker). Junction's idiom.
    private IReadOnlyList<(string Label, Action Run)>? _popover;
    private float _popoverX, _popoverY;

    // Bumped to force a *reactive* rebuild (via the build Effect) rather than the
    // untracked MarkDirty path. Nothing subscribes through it yet in P0, but the
    // async loaders from P1 on will — same arrangement as the other apps.
    private readonly Signal<int> _revision = new(0);
    private void Rerender() => _revision.Value++;

    // Toast.
    private string? _toast;
    private CancellationTokenSource? _toastCts;

    private bool _pickingFolder;

    public MeridianApp(Application app, Window window, IFolderPicker? folderPicker = null,
        ISaveFilePicker? saveFilePicker = null, int viewerCapBytes = 1024 * 1024)
    {
        _app = app;
        _window = window;
        _folderPicker = folderPicker ?? new NativeFolderPicker();
        _saveFilePicker = saveFilePicker ?? new NativeSaveFilePicker();
        Engine = new HttpEngine(viewerCapBytes);
        _responseFind = new FindBarState(MarkDirty);

        View = MeridianView.Requests;

        Ui.DisplayScale = _window.DisplayScale;
        _forceResponseBottom = _window.LayoutWidth < ForceBottomWindowWidth;
        _window.Resized += OnWindowResize;

        // Global keys land on the host when nothing is focused. Materialise the
        // host so the handler is wired.
        Mount().On<KeyDownEvent>(HandleKeyDown);
    }

    private readonly ISaveFilePicker _saveFilePicker;

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

    public override Element Build()
    {
        // Track the revision signal so a Rerender() re-runs Build through the Effect.
        _ = _revision.Value;

        var titleBar = BuildTitleBar(); titleBar.Key = "titlebar";
        var body = BuildBody(); body.Key = "body";
        var root = Flex.Column(gap: 0)
            .FlexGrow(1)
            .Background(Azimuth.Bg)
            .TextColor(Azimuth.Text)
            .FontSize(12f)
            .Children(titleBar, body);

        // Overlays are conditional; keys keep each reconciling only against its
        // own kind rather than by position.
        if (_popover != null)
        {
            var popover = BuildPopover(_popover);
            popover.Key = "popover";
            root.AddChild(popover);
        }
        if (_toast != null)
        {
            var toast = BuildToast(_toast);
            toast.Key = "toast";
            root.AddChild(toast);
        }

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
        ServiceFindBar(_responseFind);
    }

    private Element BuildBody()
    {
        var body = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        var rail = BuildRail(); rail.Key = "rail";
        body.AddChild(rail);

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
            var picked = await _folderPicker.PickFolderAsync(_workspace?.Root);
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

        // Pick the environment: keep the current one if it still exists,
        // else prefer "local", else the first.
        var envs = _tree.Environments;
        var chosen = envs.FirstOrDefault(e => e.Name == _envName)
                  ?? envs.FirstOrDefault(e => e.Name == "local")
                  ?? envs.FirstOrDefault();
        _envName = chosen?.Name;
        _env = chosen != null ? ReadEnv(chosen.Path) : null;
    }

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

    private async void Flash(string message)
    {
        _toastCts?.Cancel();
        var cts = _toastCts = new CancellationTokenSource();
        _toast = message;
        MarkDirty();
        try { await Task.Delay(2600, cts.Token); }
        catch (TaskCanceledException) { return; }
        _toast = null;
        MarkDirty();
    }

    // ── Send / cancel ────────────────────────────────────────────────────

    /// <summary>The resolver for the current environment. Secrets resolve from
    /// the keyring in P4; until then a secret key resolves like a plain one.</summary>
    internal VariableResolver Resolver() => new(environment: _env?.Values);

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
            Flash($"Unresolved in env {_envName ?? "(none)"}: {string.Join(", ", unresolved.Distinct().Select(n => $"{{{{{n}}}}}"))}");
            return;
        }

        var cts = tab.Sending = new CancellationTokenSource();
        tab.SendStartedTs = System.Diagnostics.Stopwatch.GetTimestamp();
        MarkDirty();
        TickWhileSending(tab);
        try
        {
            var spec = new RequestSpec(method, url.Text, headers, bodyText,
                bodyText != null && tab.Doc.Body!.Type == "json" ? "application/json" : null);
            tab.Result = await Task.Run(() => Engine.SendAsync(spec, cts.Token));
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

    /// <summary>Repaints the live elapsed readout while a send is in flight.
    /// Started on the UI thread, so the awaits resume there too.</summary>
    private async void TickWhileSending(RequestTab tab)
    {
        while (tab.Sending != null)
        {
            await Task.Delay(150);
            if (tab.Sending != null) MarkDirty();
        }
    }

    internal TimeSpan SendElapsed(RequestTab tab) =>
        System.Diagnostics.Stopwatch.GetElapsedTime(tab.SendStartedTs);

    // ── Keyboard ─────────────────────────────────────────────────────────

    private void HandleKeyDown(KeyDownEvent e)
    {
        var ctrl = e.Modifiers.HasFlag(KeyModifiers.Ctrl) || e.Modifiers.HasFlag(KeyModifiers.Super);
        var typing = e.Target is TextInputElement or TextAreaElement;

        // The response find bar's keys route ahead of everything else — the
        // Junction discipline, so Esc closes the bar before it cancels a send
        // and n/p walk matches instead of typing.
        if (View == MeridianView.Requests && ActiveTab?.Result?.Response != null
            && HandleFindBarKey(e, _responseFind))
        {
            e.StopPropagation = true;
            return;
        }

        // Ctrl combos work everywhere, including while typing in an input.
        if (ctrl)
        {
            switch (e.Key)
            {
                case KeyCode.O: OpenFolder(); e.StopPropagation = true; return;
                case KeyCode.S: SaveActiveTab(); e.StopPropagation = true; return;
                case KeyCode.W when ActiveTab != null: CloseActiveTab(); e.StopPropagation = true; return;
                case KeyCode.Enter when View == MeridianView.Requests: SendActive(); e.StopPropagation = true; return;
                case KeyCode.Backslash when View == MeridianView.Requests: FlipResponsePane(); e.StopPropagation = true; return;
            }
            return;
        }

        if (e.Key == KeyCode.Escape && ActiveTab?.Sending != null)
        {
            CancelActive();
            e.StopPropagation = true;
            return;
        }

        if (typing) return;

        // Number keys switch views.
        var viewForKey = e.Key switch
        {
            KeyCode.Digit1 => MeridianView.Requests,
            KeyCode.Digit2 => MeridianView.Runner,
            KeyCode.Digit3 => MeridianView.Discovery,
            KeyCode.Digit4 => MeridianView.History,
            KeyCode.Digit5 => MeridianView.Environments,
            _ => (MeridianView?)null,
        };
        if (viewForKey is { } v) { SetView(v); e.StopPropagation = true; }
    }

    public void Dispose()
    {
        _window.Resized -= OnWindowResize;
        _toastCts?.Cancel();
        foreach (var tab in _tabs) tab.Sending?.Cancel();
        _watcher?.Dispose();
        Engine.Dispose();
    }
}
