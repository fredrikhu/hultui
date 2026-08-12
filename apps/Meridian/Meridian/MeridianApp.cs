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

    // Bumped to force a *reactive* rebuild (via the build Effect) rather than the
    // untracked MarkDirty path. Nothing subscribes through it yet in P0, but the
    // async loaders from P1 on will — same arrangement as the other apps.
    private readonly Signal<int> _revision = new(0);
    private void Rerender() => _revision.Value++;

    // Toast.
    private string? _toast;
    private CancellationTokenSource? _toastCts;

    private bool _pickingFolder;

    public MeridianApp(Application app, Window window, IFolderPicker? folderPicker = null)
    {
        _app = app;
        _window = window;
        _folderPicker = folderPicker ?? new NativeFolderPicker();

        View = MeridianView.Requests;

        Ui.DisplayScale = _window.DisplayScale;
        _window.Resized += OnWindowResize;

        // Global keys land on the host when nothing is focused. Materialise the
        // host so the handler is wired.
        Mount().On<KeyDownEvent>(HandleKeyDown);
    }

    // A monitor change arrives as a resize; retire the icon rasters cached for
    // the old scale and rebuild, or every icon disappears until the next build.
    private void OnWindowResize()
    {
        float scale = _window.DisplayScale;
        if (MathF.Abs(Ui.DisplayScale - scale) < 0.001f) return;
        Ui.DisplayScale = scale;
        Rerender();
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
        if (_toast != null)
        {
            var toast = BuildToast(_toast);
            toast.Key = "toast";
            root.AddChild(toast);
        }

        return root;
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
            if (CollectionWorkspace.TryOpen(picked) is not { } ws)
            {
                Flash($"Not a folder: {picked}");
                return;
            }
            _workspace = ws;
            SetView(MeridianView.Requests);
            Rerender();
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
        SetView(MeridianView.Requests);
        Rerender();
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

    // ── Keyboard ─────────────────────────────────────────────────────────

    private void HandleKeyDown(KeyDownEvent e)
    {
        var ctrl = e.Modifiers.HasFlag(KeyModifiers.Ctrl) || e.Modifiers.HasFlag(KeyModifiers.Super);
        var typing = e.Target is TextInputElement or TextAreaElement;

        if (ctrl && e.Key == KeyCode.O)
        {
            OpenFolder();
            e.StopPropagation = true;
            return;
        }

        if (typing || ctrl) return;

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
    }
}
