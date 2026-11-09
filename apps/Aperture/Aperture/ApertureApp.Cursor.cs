using Aperture.Core;
using Aperture.Core.Requests;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Reactivity;

namespace Aperture;

/// <summary>
/// Moving through the shelves without a pointer.
///
/// <para>Every view that shows cards registers what it is showing during its build — the
/// items, and how many fit across — and the arrow keys walk that. Enter opens what the
/// cursor is on, Space plays it. The cursor is the app's, not the framework's focus: a
/// grid of thousands realizes a screenful at a time, and a focus ring that only exists
/// while its row happens to be realized is not a cursor. The card under the cursor does
/// take real focus when it is on screen, so a screen reader follows along and Tab still
/// walks the cards the ordinary way.</para>
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>What the current view is showing, as its build last described it: library
    /// items, and on the discovery pages titles the library does not have yet
    /// (<see cref="Aperture.Core.Requests.DiscoverItem"/>).</summary>
    private IReadOnlyList<object> _cursorItems = [];

    /// <summary>How many cards fit across, for the up/down arrows. One for a list.</summary>
    private int _cursorPerRow = 1;

    /// <summary>Identifies the list under the cursor — view plus whatever it is about — so
    /// arriving somewhere new starts at the beginning instead of wherever the last list
    /// happened to be.</summary>
    private string _cursorKey = "";

    /// <summary>Where the cursor is, or -1 when nothing has moved it yet.</summary>
    private int _cursorIndex = -1;

    /// <summary>The card the cursor is on, once it is built. Only that one card attaches
    /// itself, so focusing it is a matter of asking the ref.</summary>
    private readonly Ref<BoxElement> _cursorCard = new();
    private bool _pendingCursorFocus;

    /// <summary>The place the card in <see cref="_cursorCard"/> was made for.</summary>
    private int _cursorCardIndex = -1;

    internal int CursorIndex => _cursorIndex;
    internal object? CursorEntry =>
        _cursorIndex >= 0 && _cursorIndex < _cursorItems.Count ? _cursorItems[_cursorIndex] : null;

    /// <summary>The library item under the cursor, or null when it is on something else.</summary>
    internal MediaItem? CursorItem => CursorEntry as MediaItem;

    /// <summary>How many the list under the cursor holds in all, when only the first of them
    /// have loaded (the library grid pages them): at least the items it has.</summary>
    private int _cursorTotal;

    /// <summary>End was pressed in a list that is still loading the rest: each page that lands
    /// moves the cursor on to the new last item, until the last of the list.</summary>
    private bool _cursorToEnd;

    /// <summary>Where the cursor is in the list <paramref name="key"/> names, or -1 when it is on
    /// another list (a view asks before it registers its own).</summary>
    private int CursorIndexIn(string key) => _cursorKey == key ? _cursorIndex : -1;

    /// <summary>Whether End is on its way to the end of the list <paramref name="key"/> names,
    /// whose rest is still loading: the view asks for all of it then.</summary>
    private bool CursorGoingToEndIn(string key) => _cursorKey == key && _cursorToEnd;

    /// <summary>
    /// Called from a view's build: this is what the arrows are walking now. Changing the
    /// key resets the cursor, because "the third poster" means nothing in a different list.
    /// <paramref name="total"/> is how many the list holds in all when only part has loaded.
    /// </summary>
    private void SetCursorList(string key, IReadOnlyList<object> items, int perRow, int total = 0)
    {
        _cursorItems = items;
        _cursorPerRow = Math.Max(1, perRow);
        _cursorTotal = Math.Max(total, items.Count);
        if (_cursorKey == key)
        {
            if (_cursorToEnd && items.Count > 0)
            {
                // A page landed on the way to the end: on to its last item.
                if (_cursorIndex != items.Count - 1) _pendingCursorFocus = true;
                _cursorIndex = items.Count - 1;
                if (items.Count >= _cursorTotal) _cursorToEnd = false;
            }
            // The list can shrink under the cursor as pages load or a filter changes.
            else if (_cursorIndex >= items.Count) _cursorIndex = items.Count - 1;
            return;
        }
        _cursorKey = key;
        _cursorIndex = -1;
        _cursorToEnd = false;
    }

    /// <summary>True when this card is the one the arrows are on — the ring, and the
    /// element that takes focus.</summary>
    internal bool IsCursor(int index) => index == _cursorIndex && _cursorItems.Count > 0;

    /// <summary>Attaches the cursor ref to the card at the cursor, so the next build can
    /// hand it the keyboard.</summary>
    internal T TrackCursor<T>(T card, int index) where T : BoxElement
    {
        if (IsCursor(index))
        {
            card.WithRef(_cursorCard);
            _cursorCardIndex = index;
        }
        return card;
    }

    /// <summary>
    /// Moves the cursor. The first press lands on the first card rather than moving from
    /// nowhere, which is what makes "press right to start" work from a cold view.
    /// </summary>
    private bool MoveCursor(int delta)
    {
        if (_cursorItems.Count == 0) return false;
        _cursorToEnd = false;
        int next = _cursorIndex < 0
            ? (delta > 0 ? 0 : _cursorItems.Count - 1)
            : Math.Clamp(_cursorIndex + delta, 0, _cursorItems.Count - 1);
        if (next == _cursorIndex) return true;   // at an end: the key is still ours
        int was = _cursorIndex;
        _cursorIndex = next;
        _pendingCursorFocus = true;
        if (!MoveRingWithoutBuild(was)) RerenderView();
        return true;
    }

    private bool MoveCursorTo(int index)
    {
        if (_cursorItems.Count == 0) return false;
        _cursorToEnd = false;
        int was = _cursorIndex;
        _cursorIndex = Math.Clamp(index, 0, _cursorItems.Count - 1);
        _pendingCursorFocus = true;
        if (!MoveRingWithoutBuild(was)) RerenderView();
        return true;
    }

    /// <summary>End: the last item of the list, not of what has loaded. While the rest is still
    /// to come the cursor goes to the last loaded item and on with every page that lands, and
    /// the view asks for the rest (it used to stop at the last loaded one, the 100th of a
    /// 250-film library, for good).</summary>
    private bool MoveCursorToEnd()
    {
        if (_cursorItems.Count == 0) return false;
        int was = _cursorIndex;
        _cursorIndex = _cursorItems.Count - 1;
        _cursorToEnd = _cursorTotal > _cursorItems.Count;   // before the build, which asks for the rest
        _pendingCursorFocus = true;
        // The grid moves its live ring first, whether or not the move builds (MoveRingWithoutBuild).
        if (!MoveRingWithoutBuild(was) || _cursorToEnd) RerenderView();
        return true;
    }

    /// <summary>
    /// A cursor move done on the live cards with no build: the ring off the card at
    /// <paramref name="from"/> and onto the cursor's, which takes the cursor's ref and the keyboard —
    /// what a build would make of the move, as nothing else on these views shows the cursor. The next
    /// build compares each card with what it was made with, so the two moved are made again then.
    /// <list type="bullet">
    /// <item>Home, while it is on screen as its latest build left it. A key used to build all of Home,
    /// every shelf's cards with it. A move onto a watchlist title card builds, as that card is made
    /// by every build; so does one off it, whose ring only a build takes off.</item>
    /// <item>Discover the same, while it is on screen as its latest build left it: a key used to build
    /// every shelf, the LINQ that finds them and the walk's key with them.</item>
    /// <item>On both, a card on another page of its shelf turns the shelf to that page, and a build
    /// draws it, the ring and the keyboard on the card (one build a page turned). The shelf used to
    /// stay where it was, and the cursor walked on with no card ringed.</item>
    /// <item>The library grid, while its latest build drew it. The grid is scrolled to the cursor as a
    /// build does it, and the rows that brings in are made in the frame, the ring on the cursor's. A
    /// key used to build the view and make the two rows it moved between, and never handed over the
    /// keyboard, as the card it reached was made in the frame after the build. It still builds when
    /// the card the cursor reaches is not in a realized row, and within a page of the loaded end,
    /// where the build asks for the next page.</item>
    /// </list>
    /// A move that builds moves the ring on the live cards first: a card the ring reached with no build
    /// was made without it, so a build at another cursor would hand it back ringed, and a grid row with
    /// it would not even be made again (the list keeps a row while its item is equal, and its items are
    /// its last build's, which know nothing of the moves made since).
    /// </summary>
    private bool MoveRingWithoutBuild(int from)
    {
        Element? left, reached;
        bool build = false;
        switch (Current.View)
        {
            case ApertureView.Home:
                if (_homeBuild != Builds || !HomeCardAt(from, out left)) return false;
                if (!HomeCardAt(_cursorIndex, out reached)) build = true;
                build |= TurnHomeShelfToCursor();
                break;
            case ApertureView.Discover:
                if (_discoverBuild != Builds || !DiscoverCardAt(from, out left) || !DiscoverCardAt(_cursorIndex, out reached)) return false;
                build = TurnDiscoverShelfToCursor();
                break;
            case ApertureView.Library:
                if (!GridCardAt(from, out left) || !GridCardAt(_cursorIndex, out reached)) return false;
                build = reached is null || (GridHasMore && _cursorIndex + 1 + GridPageSize > GridItems.Count);
                if (!build) RevealGridCursor(_cursorPerRow, _fitGridStride);
                break;
            default:
                return false;
        }
        if (left is BoxElement l) l.Outline = default;
        if (reached is BoxElement r)
        {
            r.Outline = CursorRing;
            _cursorCard.Current = r;
            _cursorCardIndex = _cursorIndex;
        }
        if (build) return false;
        FocusCursorCard();
        return true;
    }

    /// <summary>
    /// The arrows, Enter and Space over whatever the current view registered. Returns true
    /// when the key was used, so the caller can stop it going anywhere else.
    /// </summary>
    private bool HandleCursorKey(KeyDownEvent e)
    {
        if (_cursorItems.Count == 0) return false;

        switch (e.Key)
        {
            case KeyCode.Right: return MoveCursor(1);
            case KeyCode.Left: return MoveCursor(-1);
            case KeyCode.Down: return MoveCursor(_cursorPerRow);
            case KeyCode.Up: return MoveCursor(-_cursorPerRow);
            case KeyCode.Home: return MoveCursorTo(0);
            case KeyCode.End: return MoveCursorToEnd();
            case KeyCode.PageDown: return MoveCursor(_cursorPerRow * 3);
            case KeyCode.PageUp: return MoveCursor(-_cursorPerRow * 3);

            case KeyCode.Enter when CursorEntry is { } entry:
                OpenEntry(entry);
                return true;

            // The card's menu, from the keyboard: the dedicated menu key where a keyboard
            // has one, and Shift+F10 where it does not.
            case KeyCode.Menu:
            case KeyCode.F10 when e.Modifiers.HasFlag(KeyModifiers.Shift):
                return OpenCursorMenu();

            // Space plays what is under the cursor — a series opens instead, because
            // "play a series" is a question the detail page answers better than a guess.
            case KeyCode.Space when CursorEntry is { } entry:
                if (entry is MediaItem { IsPlayable: true } playable)
                    Play(playable, playable.ResumePosition ?? TimeSpan.Zero);
                else OpenEntry(entry);
                return true;
        }
        return false;
    }

    /// <summary>Opens the cursor card's menu over the card itself, so it appears where
    /// the ring is rather than under the pointer, which may be somewhere else entirely.</summary>
    private bool OpenCursorMenu()
    {
        if (_cursorCard.Current is not { } card || CursorEntry is null) return false;

        // The window rather than the element's OverlayHost: a card realized inside a
        // virtual list has not been through a style pass yet, and that is where the host
        // is handed down. The app has known its window since it was constructed.
        var box = card.Layout.AbsoluteBorderBox;
        _window.OpenContextMenu(card, new HultUI.Core.Primitives.Point(box.X + 24f, box.Y + box.Height - 24f));
        return true;
    }

    /// <summary>Opens whatever a cursor entry is: a library item's page, or a title's
    /// discovery page (the library's own page, when the library has it).</summary>
    private void OpenEntry(object entry)
    {
        switch (entry)
        {
            case MediaItem item: Open(item); break;
            case DiscoverItem title: OpenDiscover(title); break;
            case DiscoverGenre genre: OpenGenre(genre); break;
            case DiscoverCompany company: OpenCompany(company); break;
            case MediaIssue issue: OpenIssue(issue); break;
        }
    }

    /// <summary>Hands the keyboard to the card the cursor moved to, once it is built.
    /// A card that is scrolled out of the realized window has no element to focus, and the
    /// cursor ring is still correct — so a miss is not worth retrying. The library's list makes
    /// its rows in the frame, after this: the ref still holds the card the cursor left, so the
    /// card the cursor reached is asked again once the frame is drawn (it used to stay with the
    /// card left, which kept the keyboard, or with nothing once that one scrolled away).</summary>
    private void FocusCursorCard()
    {
        if (!_pendingCursorFocus) return;
        _pendingCursorFocus = false;
        if (_cursorCardIndex == _cursorIndex) _cursorCard.Current?.Focus();
        else if (_fitGridStride > 0f) _window.RequestPostFrameCallback(FocusCursorCardMadeInTheFrame);
    }

    /// <summary>The card the cursor is on, once the frame that made it is drawn: still on screen, as
    /// a card that left keeps the focus manager it had.</summary>
    private void FocusCursorCardMadeInTheFrame()
    {
        if (Current.View == ApertureView.Library && _cursorCardIndex == _cursorIndex
            && _cursorCard.Current is { IsHosted: true } card)
            card.Focus();
    }
}
