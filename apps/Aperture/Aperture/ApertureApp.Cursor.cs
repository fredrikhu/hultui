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

    internal int CursorIndex => _cursorIndex;
    internal object? CursorEntry =>
        _cursorIndex >= 0 && _cursorIndex < _cursorItems.Count ? _cursorItems[_cursorIndex] : null;

    /// <summary>The library item under the cursor, or null when it is on something else.</summary>
    internal MediaItem? CursorItem => CursorEntry as MediaItem;

    /// <summary>
    /// Called from a view's build: this is what the arrows are walking now. Changing the
    /// key resets the cursor, because "the third poster" means nothing in a different list.
    /// </summary>
    private void SetCursorList(string key, IReadOnlyList<object> items, int perRow)
    {
        _cursorItems = items;
        _cursorPerRow = Math.Max(1, perRow);
        if (_cursorKey == key)
        {
            // The list can shrink under the cursor as pages load or a filter changes.
            if (_cursorIndex >= items.Count) _cursorIndex = items.Count - 1;
            return;
        }
        _cursorKey = key;
        _cursorIndex = -1;
    }

    /// <summary>True when this card is the one the arrows are on — the ring, and the
    /// element that takes focus.</summary>
    internal bool IsCursor(int index) => index == _cursorIndex && _cursorItems.Count > 0;

    /// <summary>Attaches the cursor ref to the card at the cursor, so the next build can
    /// hand it the keyboard.</summary>
    internal T TrackCursor<T>(T card, int index) where T : BoxElement
    {
        if (IsCursor(index)) card.WithRef(_cursorCard);
        return card;
    }

    /// <summary>
    /// Moves the cursor. The first press lands on the first card rather than moving from
    /// nowhere, which is what makes "press right to start" work from a cold view.
    /// </summary>
    private bool MoveCursor(int delta)
    {
        if (_cursorItems.Count == 0) return false;
        int next = _cursorIndex < 0
            ? (delta > 0 ? 0 : _cursorItems.Count - 1)
            : Math.Clamp(_cursorIndex + delta, 0, _cursorItems.Count - 1);
        if (next == _cursorIndex) return true;   // at an end: the key is still ours
        _cursorIndex = next;
        _pendingCursorFocus = true;
        Rerender();
        return true;
    }

    private bool MoveCursorTo(int index)
    {
        if (_cursorItems.Count == 0) return false;
        _cursorIndex = Math.Clamp(index, 0, _cursorItems.Count - 1);
        _pendingCursorFocus = true;
        Rerender();
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
            case KeyCode.End: return MoveCursorTo(_cursorItems.Count - 1);
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
    /// cursor ring is still correct — so a miss is not worth retrying.</summary>
    private void FocusCursorCard()
    {
        if (!_pendingCursorFocus) return;
        _pendingCursorFocus = false;
        _cursorCard.Current?.Focus();
    }
}
