using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using Meridian.Theme;

namespace Meridian;

public partial class MeridianApp
{
    // ── Parts ────────────────────────────────────────────────────────────
    //
    // The title bar, the rail, the request tree and the response panel are child components of
    // the app, kept from build to build. A keystroke in the URL rebuilt all four with the editor:
    // 237 KB a key with a 25-request collection (72 KB with the parts kept), for elements the key
    // does not change, and the tree's ~4.4 KB a request made every key dearer the bigger the
    // collection (938 KB at 200 requests). A part keeps its elements while the app rebuilds
    // around it, and builds again when something it shows changes:
    //
    //  - a signal or a loader it reads — View (the rail), the response body's text and colour
    //    (the response panel): its own build reads them, so its own effect hears them;
    //  - anything else — the collection and its tree, the open tabs, which one is active and
    //    whether each has unsaved edits, the active tab's send, result and response tab, the
    //    environment, the pane's place, the display scale: _partRevision, which every MarkDirty
    //    and Rerender in the app bumps.
    //
    // Safe by default, then: a rebuild builds the parts too. Only paths that change nothing a part
    // shows say so, with MarkViewDirty — edits to the open request (DocEdited, unless the edit is
    // the one that makes the request unsaved: the tree marks that; a method picked too), the
    // sub-tabs, the URL box opening and closing, a popover opening and closing, History's walk,
    // search and filters, and the environment table's values. The response's own controls — its tabs, pretty/raw and the find bar — rebuild the
    // response alone (ResponseDirty). PartTests holds every part input to what a full build of
    // the parts makes.

    private readonly Signal<int> _partRevision = new(0);
    private readonly Signal<int> _responseRevision = new(0);
    private Part? _titleBarPart, _railPart, _treePart, _responseRightPart, _responseBottomPart;

    /// <summary>Builds of the title bar, rail, tree and response panel so far, for tests.</summary>
    internal int PartBuilds { get; private set; }

    /// <summary>A part: a build of the app's that its own effect runs, reading
    /// <see cref="_partRevision"/> besides whatever the build reads.</summary>
    private sealed class Part(MeridianApp app, Func<MeridianApp, Element> build, Action<MeridianApp>? afterBuild = null)
        : Component
    {
        public override Element Build()
        {
            _ = app._partRevision.Value;
            app.PartBuilds++;
            return build(app);
        }

        // Not after the first build, which runs as the app's build makes the host: the part is
        // not in the window yet, and the app's own OnAfterBuild comes once it is.
        protected override void OnAfterBuild()
        {
            if (afterBuild is not null && Mount().Parent is not null) afterBuild(app);
        }
    }

    // Each part's host is the flex item where the part's root used to be, so it takes the
    // root's place in the layout: a host whose root never shrank never shrinks (FlexShrink(0)
    // where it is placed: left to shrink, the tree and the right pane gave their width to the
    // editor), a root that filled the row's height fills its host's (FlexGrow on the rail, the
    // tree and the right pane), and the pane at the bottom sizes its host from its own height
    // and floor.

    private Part TitleBarPart => _titleBarPart ??= new(this, static app =>
    {
        var bar = app.BuildTitleBar();
        bar.Key = "titlebar";
        return bar;
    });

    private Part RailPart => _railPart ??= new(this, static app =>
    {
        var rail = app.BuildRail();
        rail.Key = "rail";
        rail.FlexGrow(1);
        return rail;
    });

    private Part TreePart => _treePart ??= new(this, static app =>
    {
        // As in BuildResponsePart: a part can hear the revision before the app's rebuild takes
        // it out (outside an event, a signal write runs its effects at once).
        if (app._workspace is null) return Box.New();
        var tree = app.BuildTree();
        tree.Key = "tree";
        tree.FlexGrow(1);
        return tree;
    });

    // One part per place: the host is the pane's identity in the shell, so a flip replaces the
    // pane instead of patching one shape into the other. The find bar's work that waits for a
    // build (its field taking the keyboard, a reveal) runs after the pane's own builds as well,
    // since a find step rebuilds the pane alone.
    private Part ResponsePart(bool bottom) => bottom
        ? _responseBottomPart ??= new(this, static app => app.BuildResponsePart(bottom: true), static app => app._responseFind.Service())
        : _responseRightPart ??= new(this, static app => app.BuildResponsePart(bottom: false), static app => app._responseFind.Service());

    private Element BuildResponsePart(bool bottom)
    {
        _ = _responseRevision.Value;
        _sendElapsed.Current = null;   // set again by the readout of a send in flight, if shown
        // A rebuild that leaves no tab to show takes the pane out; when the parts hear the
        // revision first (outside an event a signal write runs its effects at once, before
        // MarkDirty's rebuild of the app), there is nothing to build until it does.
        if (ActiveTab is not { } tab) return Box.New();
        var panel = BuildResponsePanel(tab, bottom);
        panel.Key = "response-" + (bottom ? "bottom" : "right");
        if (!bottom) panel.FlexGrow(1);
        return panel;
    }

    /// <summary>Rebuilds the window, the parts with it. Hides <c>Component.MarkDirty</c>, so every
    /// rebuild this app asks for — and every helper it hands its MarkDirty to: the toasts — builds
    /// the parts as well.</summary>
    private new void MarkDirty()
    {
        _partRevision.Value++;
        base.MarkDirty();
    }

    /// <summary><see cref="MarkDirty"/> for a change no part shows: the view on screen rebuilds,
    /// the title bar, rail, tree and response panel stay as they are.</summary>
    private void MarkViewDirty() => base.MarkDirty();

    /// <summary>A change only the response panel shows — its tabs, pretty or raw, the find bar:
    /// the panel rebuilds alone.</summary>
    private void ResponseDirty() => _responseRevision.Value++;

    /// <summary>An edit to an open request, made in the editor. The editor shows it and no part
    /// does — the tree's method and name are the file's, read again when it is saved — except the
    /// tree's unsaved dot: the edit that makes the request unsaved rebuilds the parts too.</summary>
    private void DocEdited(RequestTab tab, bool wasDirty)
    {
        if (tab.Dirty != wasDirty) MarkDirty();
        else MarkViewDirty();
    }

    // ── Title bar ────────────────────────────────────────────────────────

    // The in-app wordmark per the sheet: 12px, az-text-muted, no flanking rules.
    // The tracking is the letterform's (0.35em); the wordmark never takes the
    // accent colour.
    private Element BuildTitleBar()
    {
        var bar = Flex.Row(gap: 0)
            .Height(40).FlexShrink(0)
            .AlignItems(AlignItems.Stretch)
            .Background(Azimuth.Surface)
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 1, 0));

        var crumb = Flex.Row(gap: 10).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 0, 0, 16));
        crumb.AddChild(Wordmark(12f, Azimuth.TextMuted));
        if (_workspace is { } ws)
        {
            crumb.AddChild(Text.New("·").FontSize(12f).TextColor(Azimuth.TextFaint));
            crumb.AddChild(Text.New(ws.Name).FontSize(12f).TextColor(Azimuth.TextDim));
        }
        crumb.WindowDrag();
        bar.AddChild(crumb);

        var right = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Center);
        // Draggable filler so the empty middle of the bar drags the window.
        right.AddChild(Flex.Row(gap: 0).FlexGrow(1).AlignSelf(AlignSelf.Stretch).WindowDrag());

        // The environment switcher chip — a real control now that P4 is here.
        if (_workspace != null)
        {
            var label = _envName is { } envName ? $"env: {envName}" : "no environment";
            var chip = Flex.Row(gap: 4).AlignItems(AlignItems.Center)
                .Padding(new Thickness(3, 7, 3, 7))
                .CornerRadius(Azimuth.Radius1)
                .BorderColor(Azimuth.Border).BorderWidth(1f)
                .Cursor(CursorType.Pointer)
                .Tooltip("Switch environment (Ctrl E)")
                .AccessibleRole(AccessibilityRole.Button)
                .AccessibleName($"Environment: {_envName ?? "none"}")
                .AutomationId("env-name");
            chip.AddChild(Ui.Mono(label, 11f, Azimuth.TextMuted));
            chip.AddChild(Text.New("▾").FontSize(8f).TextColor(Azimuth.TextFaint));
            chip.OnClick(_ => OpenEnvSwitcher());
            chip.HoverBg(Azimuth.Raised, Color.Transparent);
            right.AddChild(chip);
        }

        right.AddChild(Box.New().Width(14));

        var controls = Flex.Row(gap: 0).AlignItems(AlignItems.Stretch);
        controls.AddChild(WinButton(Icons.WinMinimize, "Minimize", () => _window.Minimize(), Azimuth.Raised));
        // Flagged so the Windows custom frame reports HTMAXBUTTON and Windows 11
        // shows its Snap Layouts flyout on hover.
        controls.AddChild(WinButton(Icons.WinMaximize, "Maximize", () => _window.ToggleMaximize(), Azimuth.Raised)
            .WindowMaximizeButton());
        controls.AddChild(WinButton(Icons.WinClose, "Close", () => _window.Close(), new Color(232, 17, 35)));
        right.AddChild(controls);

        bar.AddChild(right);
        return bar;
    }

    /// <summary>"MERIDIAN" at the letterform's 0.35em tracking.</summary>
    internal static TextElement Wordmark(float size, Color color) =>
        Text.New("MERIDIAN").FontSize(size).FontWeight(FontWeight.Medium)
            .TextColor(color).LetterSpacing(Azimuth.WordmarkTracking(size));

    private BoxElement WinButton(string glyph, string tooltip, Action onClick, Color hoverBg)
    {
        // No pointer cursor — Windows caption buttons keep the arrow (see Junction's
        // chrome for the Snap Layouts rationale).
        var b = Flex.Row(gap: 0).Width(46).Height(40)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Tooltip(tooltip)
            .AccessibleRole(AccessibilityRole.Button).AccessibleName(tooltip);
        b.AddChild(Ui.Icon(glyph, 13f, Azimuth.Alpha(Azimuth.Text, 0.75)));
        b.OnClick(_ => onClick());
        return b.HoverBg(hoverBg, Color.Transparent);
    }

    // ── View rail ────────────────────────────────────────────────────────

    private Element BuildRail()
    {
        var rail = Flex.Column(gap: Azimuth.S1).FlexShrink(0)
            .Width(Azimuth.RailWidth)
            .Padding(new Thickness(Azimuth.S2, 0, Azimuth.S2, 0))
            .AlignItems(AlignItems.Center)
            .Background(Azimuth.Surface)
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 1, 0, 0));

        rail.AddChild(RailButton(MeridianView.Requests, "Requests", Icons.Requests, "1"));
        rail.AddChild(RailButton(MeridianView.Runner, "Runner", Icons.Runner, "2"));
        rail.AddChild(RailButton(MeridianView.Discovery, "Discovery", Icons.Discovery, "3"));
        rail.AddChild(RailButton(MeridianView.History, "History", Icons.History, "4"));
        rail.AddChild(RailButton(MeridianView.Environments, "Environments", Icons.Environments, "5"));
        return rail;
    }

    private Element RailButton(MeridianView view, string label, string glyph, string num)
    {
        bool active = View == view;
        var fg = active ? Azimuth.Accent : Azimuth.TextMuted;

        // Active: 16% accent tint + 2dip left edge. The edge border is reserved
        // (transparent) on inactive buttons so activation never shifts the glyph.
        var b = Flex.Column(gap: 2)
            .Width(40).Height(44).FlexShrink(0)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .CornerRadius(Azimuth.Radius1)
            .BorderColor(active ? Azimuth.Accent : Color.Transparent)
            .BorderWidth(new Thickness(2, 0, 0, 0))
            .Cursor(CursorType.Pointer)
            .Tooltip($"{label} ({num})")
            .AccessibleRole(AccessibilityRole.Button).AccessibleName($"{label} ({num})")
            .AutomationId("rail-" + label.ToLowerInvariant());
        if (active)
            b.Background(Azimuth.Tint(Azimuth.Accent));

        b.AddChild(Ui.Icon(glyph, 16f, fg));
        b.AddChild(Ui.Mono(num, 9f, active ? Azimuth.Alpha(Azimuth.Accent, 0.85) : Azimuth.TextFaint));
        b.OnClick(_ => SetView(view));
        if (!active)
            b.HoverBg(Azimuth.Raised, Color.Transparent);
        return b;
    }
}
