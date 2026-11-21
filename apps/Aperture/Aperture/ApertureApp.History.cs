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
/// <para>Each page remembers where the cursor was when you left it, so Back lands on the card you
/// opened, ringed and with the keyboard, as a browser puts you back where you were scrolled. A
/// page opened again from the rail starts where its last visit left the cursor; one never visited
/// starts with none.</para>
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>One page in the history, and where the cursor was on it when it was left.</summary>
    private sealed class Visit(Route route)
    {
        public Route Route { get; } = route;
        public string CursorKey = "";
        public int CursorIndex = -1;
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
        if (Current == route) return;
        Leave();
        if (_at < _history.Count - 1) _history.RemoveRange(_at + 1, _history.Count - _at - 1);
        var visit = new Visit(route);
        // A page opened again starts where its last visit left the cursor: Home from the rail
        // after a film's page has the ring where it was, as it did before there was a history.
        for (int i = _history.Count - 1; i >= 0; i--)
        {
            if (_history[i].Route != route) continue;
            visit.CursorKey = _history[i].CursorKey;
            visit.CursorIndex = _history[i].CursorIndex;
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
        Rerender();
        return true;
    }

    /// <summary>A new session starts a new history, at Home: the pages of the last one belong
    /// to another server or another user.</summary>
    private void ResetHistory()
    {
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

    /// <summary>Remembers where the cursor is on the page being left.</summary>
    private void Leave()
    {
        var visit = _history[_at];
        visit.CursorKey = _cursorKey;
        visit.CursorIndex = _cursorKey.Length > 0 ? _cursorIndex : -1;
    }

    /// <summary>
    /// Puts the cursor back where it was on a page returned to, for the page's build to keep
    /// (its list has the same key, unless what it shows changed) and hand the keyboard to; a page
    /// with no cursor of its own, or one arrived at fresh, starts with none. A page whose verbs
    /// lead (an item's) hands its first verb the keyboard once it has loaded.
    /// </summary>
    private void Arrive(Visit visit)
    {
        _cursorKey = visit.CursorKey;
        _cursorIndex = visit.CursorIndex;
        _cursorToEnd = false;
        // The ref still holds a card of the page left; the build sets it again.
        _cursorCard.Current = null;
        _cursorCardIndex = -1;
        _pendingCursorFocus = _cursorIndex >= 0;
        _pendingArrivalFocus = _cursorIndex < 0
            && visit.Route.View is ApertureView.Detail or ApertureView.DiscoverTitle;
        _arrivalFocusHeld.SetTarget(_pendingArrivalFocus ? _window.FocusedElement : null);
        // Search is a view whose whole content is one field: arriving there means typing —
        // unless coming back to a result the cursor was on, which takes the keyboard instead.
        if (visit.Route.View == ApertureView.Search && _cursorIndex < 0) _pendingSearchFocus = true;
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
    /// the keyboard is the one handed it on arrival.</summary>
    private void MarkArrivalFocus(BoxElement row)
    {
        foreach (var child in row.Children)
            if (child is BoxElement { IsFocusable: true } first)
            {
                first.WithRef(_arrivalFocus);
                return;
            }
    }

    /// <summary>From OnAfterBuild: the page's first verb, once it is on screen. A page still
    /// loading has none yet, and the next build asks again.</summary>
    private void FocusArrival()
    {
        if (!_pendingArrivalFocus) return;
        _arrivalFocusHeld.TryGetTarget(out var before);
        if (_window.FocusedElement is { } held && !ReferenceEquals(held, before) && _window.IsInTree(held))
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
        if (e.Button is not (PointerEvent.BackButton or PointerEvent.ForwardButton)) return;
        if (_stage != ApertureStage.Browsing || _playingItem is not null) return;
        if (e.Button == PointerEvent.BackButton ? GoBack() : GoForward())
            e.StopPropagation = true;
    }
}
