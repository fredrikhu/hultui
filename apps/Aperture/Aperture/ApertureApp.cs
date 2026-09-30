using Aperture.Core;
using Aperture.Theme;
using HultUI;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using HultUI.Media;

namespace Aperture;

/// <summary>Where the window is: signing in, or browsing a signed-in server.</summary>
public enum ApertureStage { Connect, Browsing }

/// <summary>A place in the app. The stack of these is the back button.</summary>
/// <param name="Id">The library or item the view is about; null for Home and Search. For a
/// discovery title it is the title's key, <c>movie:603</c> or <c>tv:1399</c>.</param>
public sealed record Route(ApertureView View, string? Id = null, string? Title = null);

public enum ApertureView
{
    Home, Library, Detail, Person, Favourites, Search, Settings,
    /// <summary>The request server's shelves: what is trending, popular and coming.</summary>
    Discover,
    /// <summary>One title the request server knows, whether or not the library has it.</summary>
    DiscoverTitle,
    /// <summary>Requests made, and — for someone who may — the ones waiting for approval.</summary>
    Requests,
    /// <summary>One genre's titles on the request server; the Id is <c>movie:28</c>.</summary>
    DiscoverGenre,
    /// <summary>One person's films and series on the request server; the Id is their TMDB id.</summary>
    DiscoverPerson,
    /// <summary>A studio's films or a network's series; the Id is <c>movie:2</c> or <c>tv:213</c>.</summary>
    DiscoverCompany,
    /// <summary>A custom Discover row in full — "See all"; the Id is the row's id in the layout.</summary>
    DiscoverAll,
    /// <summary>Problem reports, for someone who may read everyone's.</summary>
    Issues,
    /// <summary>One report and its conversation; the Id is the server's issue id.</summary>
    Issue,
}

/// <summary>
/// Root of Aperture, the Jellyfin client: a Halide-themed shell — title bar, 64dip rail,
/// a navigation stack of views — over one signed-in server, with the framework's video
/// player for the thing it is all for.
///
/// <para>The app owns three things the views borrow: the <see cref="IMediaLibrary"/> (one
/// per signed-in server, replaced on sign-in and disposed on sign-out), the
/// <see cref="PosterCache"/> (artwork, shared so a poster fetched for the home row is
/// already there when the grid opens), and the player — which is deliberately <i>not</i>
/// a view. Playback is a sheet over whatever you were looking at, so stopping a film puts
/// you back where you started it with the stack intact.</para>
/// </summary>
public sealed partial class ApertureApp : Component, IDisposable
{
    private readonly Application _app;
    private readonly Window _window;
    private readonly ApertureSettings _settings;
    private readonly Func<IMediaBackend>? _backends;

    /// <summary>The signed-in server, or null while the connect screen is up.</summary>
    private IMediaLibrary? _library;

    private readonly PosterCache _posters;
    private readonly ToastHost _toasts;

    /// <summary>Bumped to force a reactive rebuild rather than the untracked MarkDirty
    /// path — every async load lands through here. Same arrangement as the other apps.</summary>
    private readonly Signal<int> _revision = new(0);

    /// <summary>Where we are. Never empty while browsing: the bottom is always Home.</summary>
    private readonly List<Route> _stack = [new Route(ApertureView.Home)];

    private ApertureStage _stage = ApertureStage.Connect;

    public ApertureApp(Application app, Window window, ApertureSettings? settings = null,
        IMediaLibrary? library = null, Func<IMediaBackend>? backends = null, HttpClient? posterHttp = null,
        Aperture.Core.Requests.IRequestServer? requests = null)
    {
        _app = app;
        _window = window;
        _backends = backends;
        // The built-in widgets read the Application theme rather than the colours this app
        // paints with, so an unthemed Application leaves every text box flat white in an
        // otherwise Halide window. Program.cs sets it too, before the first frame; doing it
        // here as well means anything hosting this directly (tests, the screenshot driver)
        // gets the same window the user sees. Mirrors the other three apps.
        _app.Theme = Halide.ToHultTheme();

        _settings = settings ?? new ApertureSettings(CredentialStore.Default);
        _toasts = new ToastHost(MarkDirty) { Seconds = 2.6f };
        _posters = new PosterCache(() => Application.InvokeOnMainThread(Rerender), http: posterHttp);

        if (library is not null)
        {
            // Handed a signed-in library (tests, the screenshot driver, an API-key run):
            // skip the connect screen entirely. The poster cache still needs the session's
            // header — scrubber tiles are behind it — and this path does not go through
            // Adopt, which is where a normal sign-in sets it.
            _library = library;
            _posters.Authorize(library.Server.Address, _settings.Device.AuthorizationHeader(library.User.AccessToken));
            _stage = ApertureStage.Browsing;
            // A request server handed in the same way (tests, the driver) is simply in use.
            _requestServer = requests;
            if (requests is not null) StartArrivalWatch();
        }
        else
        {
            _address = _settings.Data.Active?.Address ?? "";
            _username = _settings.Data.Active?.UserName ?? "";
        }

        Ui.DisplayScale = _window.DisplayScale;
        _window.Resized += OnWindowResize;

        // Global keys land on the host when nothing is focused.
        Mount().On<KeyDownEvent>(HandleKeyDown);
    }

    /// <summary>The signed-in server, for tests and the views.</summary>
    internal IMediaLibrary? Library => _library;
    internal PosterCache Posters => _posters;
    internal ApertureSettings Settings => _settings;
    internal ApertureStage Stage => _stage;
    internal Route Current => _stack[^1];
    internal IReadOnlyList<Route> Stack => _stack;

    private void Rerender() => _revision.Value++;

    // A monitor change arrives as a resize: retire the icon rasters cached for the old
    // scale, or every icon disappears until the next build.
    private void OnWindowResize()
    {
        float scale = _window.DisplayScale;
        if (MathF.Abs(Ui.DisplayScale - scale) >= 0.001f)
        {
            Ui.DisplayScale = scale;
            Rerender();
        }
        else
        {
            // Column counts in the poster grid are read off the window width.
            MarkDirty();
        }
    }

    protected override void OnAfterBuild()
    {
        FocusCursorCard();
        FocusPendingSearchField();
    }

    public override Element Build()
    {
        // Track the revision signal so Rerender() re-runs Build through the Effect.
        _ = _revision.Value;

        var root = Flex.Column(gap: 0)
            .FlexGrow(1)
            .Background(Halide.Bg)
            .TextColor(Halide.Text)
            .FontSize(13f);

        // Full screen means the window has no chrome — so neither do we. The app's own
        // title bar is the only chrome there is here, and leaving it up would put a strip
        // of app furniture across the top of a film.
        if (!_window.IsFullscreen)
        {
            var titleBar = BuildTitleBar(); titleBar.Key = "titlebar";
            root.AddChild(titleBar);
        }

        var body = _stage == ApertureStage.Connect ? BuildConnectView() : BuildBrowsing();
        body.Key = "body";
        root.AddChild(body);

        // The player is a sheet over everything, so the view underneath keeps its state
        // (and its scroll position) while a film plays. It goes up the moment Play is
        // pressed — before the server has answered — because the wait is part of playing
        // and belongs on the sheet, not as a frozen library behind a spinner-less pause.
        if (_playingItem is not null)
        {
            var sheet = BuildPlayerSheet();
            sheet.Key = "player";
            root.AddChild(sheet);
        }

        if (_toasts.Build() is { } toast)
        {
            toast.Key = "toast";
            root.AddChild(toast);
        }
        return root;
    }

    private Element BuildBrowsing()
    {
        // The request server rides along with the library: reopened from its stored
        // session once the library is up, and asked for its waiting count for the rail.
        EnsureRequestServer();
        EnsurePendingCount();
        EnsureIssueCounts();
        EnsureMyIssues();

        var row = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        var rail = BuildRail(); rail.Key = "rail";
        row.AddChild(rail);

        var view = Current.View switch
        {
            ApertureView.Library => BuildLibraryView(Current),
            ApertureView.Detail => BuildDetailView(Current),
            ApertureView.Person => BuildPersonView(Current),
            ApertureView.Favourites => BuildFavouritesView(),
            ApertureView.Search => BuildSearchView(),
            ApertureView.Settings => BuildSettingsView(),
            ApertureView.Discover => BuildDiscoverView(),
            ApertureView.DiscoverTitle => BuildDiscoverTitleView(Current),
            ApertureView.DiscoverGenre => BuildDiscoverGenreView(Current),
            ApertureView.DiscoverCompany => BuildDiscoverCompanyView(Current),
            ApertureView.DiscoverAll => BuildDiscoverAllView(Current),
            ApertureView.DiscoverPerson => BuildDiscoverPersonView(Current),
            ApertureView.Requests => BuildRequestsView(),
            ApertureView.Issues => BuildIssuesView(),
            ApertureView.Issue => BuildIssueView(Current),
            _ => BuildHomeView(),
        };
        view.Key = "view-" + Current.View;
        row.AddChild(view.FlexGrow(1));
        return row;
    }

    // ── Title bar ────────────────────────────────────────────────────────

    private Element BuildTitleBar()
    {
        var bar = Flex.Row(gap: 0)
            .Height(Halide.TitleBarHeight).FlexShrink(0)
            .AlignItems(AlignItems.Stretch)
            .Background(Halide.Surface)
            .BorderColor(Halide.Border).BorderWidth(new Thickness(0, 0, 1, 0));

        var left = Flex.Row(gap: Halide.S3).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 0, 0, Halide.S4));
        left.AddChild(Ui.Wordmark(12f, Halide.TextMuted));
        if (_library is { } lib)
        {
            left.AddChild(Text.New("·").FontSize(12f).TextColor(Halide.TextFaint));
            left.AddChild(Ui.Line(lib.Server.Name, 12f, Halide.TextDim));
        }
        left.WindowDrag();
        bar.AddChild(left);

        var right = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Center);
        right.AddChild(Flex.Row(gap: 0).FlexGrow(1).AlignSelf(AlignSelf.Stretch).WindowDrag());

        if (_library is { } signedIn)
        {
            var who = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
                .Padding(new Thickness(3, Halide.S2, 3, Halide.S2))
                .CornerRadius(Halide.Radius1)
                .Cursor(CursorType.Pointer)
                .Tooltip($"Signed in as {signedIn.User.Name} — settings")
                .AccessibleRole(AccessibilityRole.Button)
                .AccessibleName($"Signed in as {signedIn.User.Name}")
                .AutomationId("titlebar-user");
            who.AddChild(Ui.Icon(Icons.User, 14f, Halide.TextMuted));
            who.AddChild(Ui.Line(signedIn.User.Name, 12f, Halide.TextDim));
            who.OnClick(_ => Navigate(new Route(ApertureView.Settings)));
            who.HoverBg(Halide.Raised);
            right.AddChild(who);
            right.AddChild(Box.New().Width(Halide.S3));
        }

        var controls = Flex.Row(gap: 0).AlignItems(AlignItems.Stretch);
        controls.AddChild(WinButton(Icons.WinMinimize, "Minimize", () => _window.Minimize(), Halide.Raised));
        // Flagged so the Windows custom frame reports HTMAXBUTTON and Windows 11 shows its
        // Snap Layouts flyout on hover.
        controls.AddChild(WinButton(Icons.WinMaximize, "Maximize", () => _window.ToggleMaximize(), Halide.Raised)
            .WindowMaximizeButton());
        controls.AddChild(WinButton(Icons.WinClose, "Close", () => _window.Close(), new Color(232, 17, 35)));
        right.AddChild(controls);

        bar.AddChild(right);
        return bar;
    }

    private BoxElement WinButton(string glyph, string tooltip, Action onClick, Color hoverBg)
    {
        // No pointer cursor — Windows caption buttons keep the arrow.
        var b = Flex.Row(gap: 0).Width(46).Height(Halide.TitleBarHeight)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Tooltip(tooltip)
            .AccessibleRole(AccessibilityRole.Button).AccessibleName(tooltip);
        b.AddChild(Ui.Icon(glyph, 13f, Halide.Alpha(Halide.Text, 0.75)));
        b.OnClick(_ => onClick());
        return b.HoverBg(hoverBg);
    }

    // ── Rail ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Home, one button per library, then search and settings at the foot. The libraries
    /// are on the rail rather than behind a menu because a media server has two or three
    /// of them and they are the app's top-level nouns — keys 2… land on them in the order
    /// the server lists them.
    /// </summary>
    private Element BuildRail()
    {
        var rail = Flex.Column(gap: Halide.S1).FlexShrink(0)
            .Width(Halide.RailWidth)
            .Padding(new Thickness(Halide.S2, 0, Halide.S2, 0))
            .AlignItems(AlignItems.Center)
            .Background(Halide.Surface)
            .BorderColor(Halide.Border).BorderWidth(new Thickness(0, 1, 0, 0));

        rail.AddChild(RailButton(Icons.Home, "Home", "1",
            active: Current.View == ApertureView.Home,
            onClick: () => GoHome()));

        int key = 2;
        foreach (var lib in Libraries)
        {
            var id = lib.Id;
            string num = key <= 9 ? key.ToString() : "";
            rail.AddChild(RailButton(Icons.ForLibrary(lib.Kind), lib.Name, num,
                active: Current.View == ApertureView.Library && Current.Id == id,
                onClick: () => Navigate(new Route(ApertureView.Library, id, lib.Name))));
            key++;
        }

        // The request server's two doors, under the libraries they fill — and only when one
        // is connected: a rail button that leads to "connect something first" is furniture.
        if (_requestServer is { } requests)
        {
            rail.AddChild(Box.New().Height(Halide.S2));
            rail.AddChild(RailButton(Icons.Discover, "Discover", "",
                active: Current.View is ApertureView.Discover or ApertureView.DiscoverTitle
                    or ApertureView.DiscoverGenre or ApertureView.DiscoverPerson or ApertureView.DiscoverCompany
                    or ApertureView.DiscoverAll,
                onClick: () => Navigate(new Route(ApertureView.Discover))));
            rail.AddChild(RailButton(Icons.Requests, "Requests", "",
                active: Current.View == ApertureView.Requests,
                onClick: () => Navigate(new Route(ApertureView.Requests)),
                badge: requests.User.CanManageRequests && PendingRequestCount > 0 ? PendingRequestCount : 0));
            // Everyone's reports are for whoever looks after the library; the badge only for
            // someone who can do something about them.
            if (requests.User.CanSeeIssues)
                rail.AddChild(RailButton(Icons.Report, "Issues", "",
                    active: Current.View is ApertureView.Issues or ApertureView.Issue,
                    onClick: () => Navigate(new Route(ApertureView.Issues)),
                    badge: requests.User.CanManageIssues ? OpenIssueCount : 0, badgeWord: "open"));
            // Someone who files reports but may not read everyone's: their own, and a
            // badge for the ones answered or resolved since they last looked.
            else if (CanReportProblems)
                rail.AddChild(RailButton(Icons.Report, "Your reports", "",
                    active: Current.View is ApertureView.Issues or ApertureView.Issue,
                    onClick: () => Navigate(new Route(ApertureView.Issues)),
                    badge: MyIssueNewsCount, badgeWord: "new"));
        }

        rail.AddChild(Box.New().FlexGrow(1));
        rail.AddChild(RailButton(Icons.FavoriteOn, "Favourites", "0",
            active: Current.View == ApertureView.Favourites,
            onClick: () => Navigate(new Route(ApertureView.Favourites))));
        rail.AddChild(RailButton(Icons.Search, "Search", "/",
            active: Current.View == ApertureView.Search,
            onClick: () => Navigate(new Route(ApertureView.Search))));
        rail.AddChild(RailButton(Icons.Settings, "Settings", "",
            active: Current.View == ApertureView.Settings,
            onClick: () => Navigate(new Route(ApertureView.Settings))));
        rail.AddChild(Box.New().Height(Halide.S2));
        return rail;
    }

    private Element RailButton(string glyph, string label, string key, bool active, Action onClick, int badge = 0,
        string badgeWord = "waiting")
    {
        var fg = active ? Halide.Accent : Halide.TextMuted;
        var name = key.Length > 0 ? $"{label} ({key})" : label;
        if (badge > 0) name += $", {badge} {badgeWord}";
        var b = Flex.Column(gap: 2)
            .Width(48).Height(48).FlexShrink(0)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .CornerRadius(Halide.Radius1)
            // The edge is reserved (transparent) when inactive so activation never shifts
            // the glyph by two pixels.
            .BorderColor(active ? Halide.Accent : Color.Transparent)
            .BorderWidth(new Thickness(2, 0, 0, 0))
            .Cursor(CursorType.Pointer)
            .Tooltip(name)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(name)
            .AutomationId("rail-" + label.ToLowerInvariant().Replace(' ', '-'));
        if (active) b.Background(Halide.Tint(Halide.Accent));
        b.AddChild(Ui.Icon(glyph, 18f, fg));
        if (key.Length > 0)
            b.AddChild(Ui.Mono(key, 9f, active ? Halide.Alpha(Halide.Accent, 0.85) : Halide.TextFaint));
        if (badge > 0)
        {
            // A count, top-right: requests waiting on someone who can approve them.
            var dot = Box.New().Height(15).Padding(new Thickness(0, 4, 0, 4))
                .CornerRadius(7.5f).Background(Halide.Accent)
                .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
                .Children(Ui.Mono(badge > 99 ? "99+" : badge.ToString(), 9f, new Color(35, 23, 4)));
            dot.IsHitTestVisible = false;
            dot.Position.Type = PositionType.Absolute;
            dot.Position.Right = Dimension.Px(3);
            dot.Position.Top = Dimension.Px(4);
            b.AddChild(dot);
        }
        b.OnClick(_ => onClick());
        if (!active) b.HoverBg(Halide.Raised);
        return b;
    }

    // ── Navigation ───────────────────────────────────────────────────────

    /// <summary>Pushes a view. Re-entering the view you are already on is a no-op rather
    /// than a second stack entry, so Back never walks through repeats.</summary>
    internal void Navigate(Route route)
    {
        if (Current == route) return;
        // Search is a view whose whole content is one field: arriving there means typing.
        if (route.View == ApertureView.Search) _pendingSearchFocus = true;
        // Home is the floor: navigating to it clears rather than stacks.
        if (route.View == ApertureView.Home) { GoHome(); return; }
        _stack.Add(route);
        Rerender();
    }

    internal void GoHome()
    {
        _stack.Clear();
        _stack.Add(new Route(ApertureView.Home));
        Rerender();
    }

    /// <summary>Back. At the bottom of the stack it does nothing — there is nowhere
    /// further back than Home, and quitting on Escape is a surprise, not a shortcut.</summary>
    internal bool GoBack()
    {
        if (_stack.Count <= 1) return false;
        _stack.RemoveAt(_stack.Count - 1);
        Rerender();
        return true;
    }

    // ── Keys ─────────────────────────────────────────────────────────────

    private void HandleKeyDown(KeyDownEvent e)
    {
        // While the sheet is up the film has the keyboard. The transport overlay owns the
        // full key map — but only once it has focus, which it takes on a click; these three
        // are the ones someone reaches for without clicking the picture first. Escape works
        // during the opening wait too, which is when someone is most likely to press it.
        if (_playingItem is not null)
        {
            // Typing a problem report over the film: the text field has the keys, and a
            // space in "out of sync" must not pause it. Escape closes the report, not the film.
            if (e.Target is TextInputElement or TextAreaElement)
            {
                if (e.Key == KeyCode.Escape && _reportOpen)
                {
                    CloseReport();
                    e.Handled = true;
                }
                return;
            }
            switch (e.Key)
            {
                case KeyCode.Escape when _reportOpen:
                    CloseReport();
                    e.Handled = true;
                    break;
                case KeyCode.Escape:
                    StopPlayback(report: true);
                    e.Handled = true;
                    break;
                case KeyCode.Space or KeyCode.K:
                    _player?.TogglePlay();
                    e.Handled = true;
                    break;
                case KeyCode.F:
                    ToggleFullscreen();
                    e.Handled = true;
                    break;
                case KeyCode.C:
                    ToggleChapters();
                    e.Handled = true;
                    break;
                case KeyCode.Q:
                    ToggleQueue();
                    e.Handled = true;
                    break;
                // Page keys step chapters without opening anything — the transport keys
                // the overlay owns are all taken, and these two are idle in a player.
                case KeyCode.PageDown:
                    StepChapter(1);
                    e.Handled = true;
                    break;
                case KeyCode.PageUp:
                    StepChapter(-1);
                    e.Handled = true;
                    break;
            }
            return;
        }
        if (_stage == ApertureStage.Connect) return;

        // The shelves take the arrows, Enter and Space — but only when the keyboard is not
        // in a text field, where they mean what they always mean.
        if (e.Target is not TextInputElement && HandleCursorKey(e))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case KeyCode.Escape or KeyCode.Backspace:
                if (GoBack()) e.Handled = true;
                return;
            case KeyCode.Slash:
                Navigate(new Route(ApertureView.Search));
                e.Handled = true;
                return;
            case KeyCode.Digit0:
                Navigate(new Route(ApertureView.Favourites));
                e.Handled = true;
                return;
            case KeyCode.F5:
                Refresh();
                e.Handled = true;
                return;
        }

        // 1 is Home; 2… are the libraries in the order the rail lists them.
        int digit = e.Key - KeyCode.Digit1;
        if (digit is >= 0 and <= 8)
        {
            if (digit == 0) { GoHome(); e.Handled = true; return; }
            var libs = Libraries;
            int index = digit - 1;
            if (index < libs.Count)
            {
                Navigate(new Route(ApertureView.Library, libs[index].Id, libs[index].Name));
                e.Handled = true;
            }
        }
    }

    /// <summary>Drops every cached list and re-asks the server — F5, and what the views'
    /// own refresh buttons call.</summary>
    internal void Refresh()
    {
        _dataGeneration++;
        _gridItems.Clear();
        _gridKey = null;
        Rerender();
    }

    /// <summary>Says something, briefly, at the bottom of the window.</summary>
    internal void Flash(string message, ToastSeverity severity = ToastSeverity.None,
        (string Label, Action Run)? action = null) =>
        _toasts.Flash(message, severity, action, action is null ? null : 6f);

    /// <summary>The failure copy for a call that did not work, as a toast.</summary>
    internal void FlashFailure(ApertureFailure failure, string what = "That")
    {
        var host = _library?.Server.Host ?? "the server";
        Flash(failure.UserMessage(host, what), ToastSeverity.Error);
        // A token that stopped working mid-session is the one failure that has to change
        // what the window shows: everything else is a retry, this one is a sign-in.
        if (failure.Kind == ApertureFailureKind.Unauthorized) SignOut(keepAccount: true);
    }

    public void Dispose()
    {
        StopPlayback(report: true);
        // Closing the window is the ordinary way to stop watching, so the resume point has
        // to survive it: give the report its moment before the connection goes.
        WaitForStopReport();
        StopQuickConnectTimer();
        StopProgressTimer();
        StopArrivalWatch();
        _quickConnectCts?.Cancel();
        _quickConnectCts?.Dispose();
        _quickConnectCts = null;
        _posters.Dispose();
        _requestServer?.Dispose();
        _requestServer = null;
        _library?.Dispose();
        _library = null;
    }
}
