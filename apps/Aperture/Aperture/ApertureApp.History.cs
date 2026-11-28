using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Reactivity;

namespace Aperture;

/// <summary>
/// Where you have been, as a browser keeps it: a list of pages with a place in it. Opening a
/// page drops whatever was ahead of you and adds the page; Back and Forward move along the list
/// without changing it. Escape, Backspace, Alt+← and the mouse's back button go back; Alt+→ and
/// the forward button go forward.
///
/// <para>Each page remembers where the cursor was when you left it, and a library how far its grid
/// was scrolled, so Back lands where you were scrolled, as a browser puts you, on the card you
/// opened, ringed and with the keyboard. A page opened again from the rail starts where its last
/// visit left it; one never visited starts at the top, its first card with the cursor and the
/// keyboard.</para>
///
/// <para>The last Home or library left is kept as it was, and comes back on Back, Forward or the rail
/// rather than being made again (ApertureApp.KeptPage.cs): Home also keeps how far it was scrolled.</para>
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>One page in the history, and where the cursor was on it when it was left.</summary>
    private sealed class Visit(Route route)
    {
        public Route Route { get; } = route;
        public string CursorKey = "";
        public int CursorIndex = -1;

        /// <summary>How far a library's grid was scrolled when the page was left, or -1: another
        /// page, or a grid not on screen then (still loading). The wheel's scroll counts as the
        /// keys' does: a page opened with a click is returned to where it was clicked.</summary>
        public float GridScrollY = -1;
    }

    /// <summary>How many pages the history keeps, oldest dropped first. A browser keeps about
    /// fifty; a hundred is an afternoon of browsing, and a page is three strings.</summary>
    internal const int HistoryKept = 100;

    /// <summary>Every page visited, oldest first. Never empty while browsing: it starts at Home.</summary>
    private readonly List<Visit> _history = [new Visit(new Route(ApertureView.Home))];

    /// <summary>The page shown, as a place in <see cref="_history"/>.</summary>
    private int _at;

    internal Route Current => _history[_at].Route;

    /// <summary>The pages Back walks, oldest first, ending with the one shown.</summary>
    internal IReadOnlyList<Route> Stack => _history.Take(_at + 1).Select(v => v.Route).ToList();

    /// <summary>The pages Forward walks, nearest first.</summary>
    internal IReadOnlyList<Route> Ahead => _history.Skip(_at + 1).Select(v => v.Route).ToList();

    internal bool CanGoBack => _at > 0;
    internal bool CanGoForward => _at < _history.Count - 1;

    /// <summary>Opens a page: what was ahead of you is dropped, as in a browser. Opening the
    /// page you are on is a no-op rather than a second entry, so Back never walks through
    /// repeats.</summary>
    internal void Navigate(Route route)
    {
        // The page already open: nothing to open. On a D-pad its door on the rail hands the keyboard
        // to the page's content, as a door to another page does by opening it; it stayed on the door.
        if (Current == route)
        {
            if (_window.ArrowKeysMoveFocus) FocusPageContent();
            return;
        }
        Leave();
        if (_at < _history.Count - 1) _history.RemoveRange(_at + 1, _history.Count - _at - 1);
        var visit = new Visit(route);
        // A page opened again starts where its last visit left the cursor and the grid: Home from
        // the rail after a film's page has the ring where it was, as it did before there was a
        // history, and a library is scrolled as it was.
        for (int i = _history.Count - 1; i >= 0; i--)
        {
            if (_history[i].Route != route) continue;
            visit.CursorKey = _history[i].CursorKey;
            visit.CursorIndex = _history[i].CursorIndex;
            visit.GridScrollY = _history[i].GridScrollY;
            break;
        }
        _history.Add(visit);
        _at = _history.Count - 1;
        if (_history.Count > HistoryKept)
        {
            _history.RemoveRange(0, _history.Count - HistoryKept);
            _at = _history.Count - 1;
        }
        Arrive(_history[_at]);
        ShowKeptPage(_history[_at]);
        Rerender();
    }

    /// <summary>Home, as a page like any other: Back returns to where you were.</summary>
    internal void GoHome() => Navigate(new Route(ApertureView.Home));

    /// <summary>Back one page. False at the first page, where there is nowhere further back —
    /// quitting on Escape is a surprise, not a shortcut.</summary>
    internal bool GoBack()
    {
        if (!CanGoBack) return false;
        Leave();
        _at--;
        Arrive(_history[_at]);
        ShowKeptPage(_history[_at]);
        Rerender();
        return true;
    }

    /// <summary>Forward one page, undoing a Back. False when nothing is ahead.</summary>
    internal bool GoForward()
    {
        if (!CanGoForward) return false;
        Leave();
        _at++;
        Arrive(_history[_at]);
        ShowKeptPage(_history[_at]);
        Rerender();
        return true;
    }

    /// <summary>A new session starts a new history, at Home: the pages of the last one belong
    /// to another server or another user.</summary>
    private void ResetHistory()
    {
        DropKeptPage();
        _history.Clear();
        _history.Add(new Visit(new Route(ApertureView.Home)));
        _at = 0;
        Arrive(_history[0]);
        Rerender();
    }

    /// <summary>
    /// Takes out every page <paramref name="gone"/> names — the request server's, when it is
    /// disconnected: their rail buttons have gone, and a page with no way back to it is a dead
    /// end. Pages left side by side that are the same page become one, and an emptied history
    /// is Home.
    /// </summary>
    private void ForgetPages(Func<Route, bool> gone)
    {
        var shown = _history[_at];
        var kept = new List<Visit>(_history.Count);
        // The page shown if it stays; else the nearest kept before it; else the first kept.
        int at = 0;
        for (int i = 0; i < _history.Count; i++)
        {
            var visit = _history[i];
            if (gone(visit.Route)) continue;
            if (kept.Count > 0 && kept[^1].Route == visit.Route)
            {
                // Two of the same page side by side are one; the shown one stands for both.
                if (ReferenceEquals(visit, shown)) kept[^1] = visit;
            }
            else kept.Add(visit);
            if (i <= _at) at = kept.Count - 1;
        }
        if (kept.Count == 0) kept.Add(new Visit(new Route(ApertureView.Home)));
        _history.Clear();
        _history.AddRange(kept);
        _at = Math.Clamp(at, 0, _history.Count - 1);
        if (!ReferenceEquals(_history[_at], shown)) Arrive(_history[_at]);
    }

    /// <summary>Remembers where the cursor is on the page being left, and how far its grid is
    /// scrolled: the grid on screen is a library's, as nothing else shows one. Home or a library is
    /// kept as it is for the way back (ApertureApp.KeptPage.cs).</summary>
    private void Leave()
    {
        var visit = _history[_at];
        visit.CursorKey = _cursorKey;
        visit.CursorIndex = _cursorKey.Length > 0 ? _cursorIndex : -1;
        visit.GridScrollY = visit.Route.View == ApertureView.Library && _gridList.Current is { IsHosted: true } grid
            ? grid.ScrollOffsetY : -1;
        KeepPage(visit);
    }

    /// <summary>
    /// Puts the cursor back where it was on a page returned to, for the page's build to keep
    /// (its list has the same key, unless what it shows changed) and hand the keyboard to, and a
    /// library's grid where it was scrolled (<see cref="OpenGridWhereItWasLeft"/>); a page with no
    /// cursor of its own, or one arrived at fresh, starts with none. A page whose verbs lead (an
    /// item's) hands its first verb the keyboard once it has loaded.
    /// <para>Every way into a page hands it the keyboard: Back and Forward, a number key, a rail
    /// button, a card's link. With the cursor nowhere it starts on the page's first card on screen,
    /// ringed, as Tab onto that card starts it (<see cref="SetCursorList"/>): Escape from a library
    /// opened from the rail went Back to Home, whose cursor was never set, and the keyboard went
    /// nowhere, so no focus event went out and a screen reader stayed on an element that was gone;
    /// a number key left it on nothing in the same way, and a rail button on itself (plan 17 round
    /// 18, questions 76 and 77). A page that leads with verbs hands them the keyboard instead, and
    /// Search its field. Whatever takes the keyboard before the first card is handed it keeps it (a
    /// click or a Tab while a slow page loads); the cursor is on that card all the same.</para>
    /// </summary>
    private void Arrive(Visit visit)
    {
        _cursorKey = visit.CursorKey;
        _cursorIndex = visit.CursorIndex;
        _cursorToEnd = false;
        _gridArrivalScrollY = visit.GridScrollY;
        // The ref still holds a card of the page left; the build sets it again.
        _cursorCard.Current = null;
        _cursorCardIndex = -1;
        _pendingCursorFocus = _cursorIndex >= 0;
        _pendingArrivalFocus = _cursorIndex < 0
            && visit.Route.View is ApertureView.Detail or ApertureView.DiscoverTitle;
        // Search is a view whose whole content is one field: arriving there means typing —
        // unless coming back to a result the cursor was on, which takes the keyboard instead.
        if (visit.Route.View == ApertureView.Search && _cursorIndex < 0) _pendingSearchFocus = true;
        _pendingFirstCard = _cursorIndex < 0 && !_pendingArrivalFocus && visit.Route.View != ApertureView.Search;
        _firstCardFocus = false;
        _cursorFromArrival = false;
        _arrivalFocusHeld.SetTarget(_pendingArrivalFocus || _pendingFirstCard ? _window.FocusedElement : null);
        _arrivalInputs = _inputs;
    }

    /// <summary>Key and pointer presses that reached the app's root, and how many there had been when
    /// the page was opened. Only presses: a key's release (the remote's OK lifted after it opened the
    /// page) moves nothing, and counted, it kept the focus where the window had put it while the page
    /// loaded (a Shield TV: the rail's Search, not the series' first season).</summary>
    private int _inputs, _arrivalInputs;

    /// <summary>Whether the keyboard went somewhere since the page was opened: an element other than
    /// the one that had it then, still in the window. It stays there. On a D-pad
    /// (<see cref="Window.ArrowKeysMoveFocus"/>) only where the user moved it: the window gives the
    /// focus to something while the page is still loading, and the page's verb or first card takes
    /// it from that when they come.</summary>
    private bool KeyboardMovedSinceArrival()
    {
        if (_window.ArrowKeysMoveFocus && _inputs == _arrivalInputs) return false;
        _arrivalFocusHeld.TryGetTarget(out var before);
        return _window.FocusedElement is { } held && !ReferenceEquals(held, before) && _window.IsInTree(held);
    }

    // ── The page's first verb ────────────────────────────────────────────

    /// <summary>The first verb of an item's page — Play, Resume, Request — which takes the
    /// keyboard as the page opens, so Enter on a card and Enter again plays it.</summary>
    private readonly Ref<BoxElement> _arrivalFocus = new();

    /// <summary>Set on arriving at a page that leads with verbs; cleared once one has the
    /// keyboard, or once something else has taken it.</summary>
    private bool _pendingArrivalFocus;

    /// <summary>What had the keyboard when the page was opened: anything else holding it by the
    /// time the verbs are in was given it since, and keeps it. Weak: a card of the page left holds
    /// that whole page through its parents (REACTIVITY.md, "A ref into a view keeps the whole view"),
    /// and a page that never loads would keep it for good.</summary>
    private readonly WeakReference<Element?> _arrivalFocusHeld = new(null);

    /// <summary>Called by a page that leads with verbs, on its row of them: the first that takes
    /// the keyboard is the one handed it on arrival. Only the first <paramref name="verbs"/> of the
    /// row's children are verbs (the rest are toggles); with none among them, once
    /// <paramref name="known"/> says the page knows it has none, the page's first card takes it
    /// instead (<see cref="MarkArrivalCard"/>): a season, an episode.</summary>
    private void MarkArrivalFocus(BoxElement row, int verbs = int.MaxValue, bool known = true)
    {
        _arrivalToFirstCard = false;
        var children = row.Children;
        for (int i = 0; i < Math.Min(verbs, children.Count); i++)
            if (children[i] is BoxElement { IsFocusable: true } first)
            {
                first.WithRef(_arrivalFocus);
                return;
            }
        _arrivalToFirstCard = known && _pendingArrivalFocus;
    }

    /// <summary>A page whose verbs are known to be none: its first card is handed the keyboard on
    /// arrival in their place (<see cref="MarkArrivalCard"/>).</summary>
    private bool _arrivalToFirstCard;

    /// <summary>Called with each card a page builds after its verbs (a series' seasons, a season's
    /// episodes, in order): with no verb to take the keyboard, the first is handed it.</summary>
    private T MarkArrivalCard<T>(T card) where T : Element
    {
        if (_arrivalToFirstCard && card is BoxElement { IsFocusable: true } box)
        {
            _arrivalToFirstCard = false;
            box.WithRef(_arrivalFocus);
        }
        return card;
    }

    /// <summary>From OnAfterBuild: the page's first verb, once it is on screen. A page still
    /// loading has none yet, and the next build asks again.</summary>
    private void FocusArrival()
    {
        if (!_pendingArrivalFocus) return;
        if (KeyboardMovedSinceArrival())
        {
            Settled();   // the keyboard went somewhere since; it stays there
            return;
        }
        if (_arrivalFocus.Current is not { IsFocusable: true } verb || !_window.IsInTree(verb)) return;
        Settled();
        verb.Focus();

        void Settled()
        {
            _pendingArrivalFocus = false;
            _arrivalFocusHeld.SetTarget(null);
        }
    }

    // ── Keys and buttons ─────────────────────────────────────────────────

    /// <summary>
    /// The mouse's side buttons, as a browser takes them: back (X1) and forward (X2). Heard on
    /// the root, passively, so a press anywhere in the window counts and the many presses that are
    /// not side buttons draw nothing for it. Not over a film: a thumb resting on the button
    /// would end it.
    /// </summary>
    private void OnRootPointerDown(PointerDownEvent e)
    {
        _inputs++;   // KeyboardMovedSinceArrival
        if (e.Button is not (PointerEvent.BackButton or PointerEvent.ForwardButton)) return;
        if (_stage != ApertureStage.Browsing || _playingItem is not null) return;
        if (e.Button == PointerEvent.BackButton ? GoBack() : GoForward())
            e.StopPropagation = true;
    }
}
