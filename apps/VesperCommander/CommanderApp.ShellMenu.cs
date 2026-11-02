using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;

namespace VesperCommander;

/// <summary>
/// The two ways into the Windows shell for the current selection: Alt+Enter for the
/// Properties sheet, and the context menu — reached with the context-menu key or by holding
/// the right button down on a row, the way Total Commander does it.
/// </summary>
public partial class CommanderApp
{
    // The cursor row of each pane, so the context-menu key has somewhere to open under.
    private readonly Ref<BoxElement> _cursorRowL = new();
    private readonly Ref<BoxElement> _cursorRowR = new();
    private Ref<BoxElement> CursorRowRef(char side) => side == 'L' ? _cursorRowL : _cursorRowR;

    /// <summary>What a shell command should act on: the marked set when there is one, else the
    /// row under the cursor — the same rule <see cref="OperationTargets"/> gives F5, F6 and F8,
    /// so a marked selection means the same thing everywhere. Null (with a notice posted) when
    /// there is nothing the shell can act on.
    ///
    /// <para><paramref name="anchor"/> is set when a pointer opened the menu on a particular
    /// row, which the cursor rule can't express — see <see cref="ContextTargets"/>.</para></summary>
    private List<string>? ShellScope(char side, string noun, FileEntry? anchor = null)
    {
        if (!ShellContextMenu.IsSupported)
        {
            Notice($"{noun} is a Windows shell feature");
            return null;
        }
        var items = anchor is null ? OperationTargets(side) : ContextTargets(side, anchor);
        if (items.Count == 0) return null;    // an empty folder, or the cursor on ".."
        if (items.Any(e => ArchivePath.InArchive(e.FullPath)))
        {
            // Archive members live in Vesper's VFS; the shell has no item to bind to.
            Notice($"No {noun.ToLowerInvariant()} for archive members");
            return null;
        }
        return items.Select(e => e.FullPath).ToList();
    }

    /// <summary>What a menu opened *on a row* acts on. A right-hold names one row with the
    /// pointer, so that row is the target — unless it is one of the marked ones, where the
    /// gesture means the whole marked set, the way it does in Explorer.
    ///
    /// <para>Without this the hold fell back to <see cref="OperationTargets"/>, which reads the
    /// <em>cursor</em> row. The right press deliberately doesn't move the cursor (it starts a
    /// mark sweep), so the menu opened for whatever row the cursor happened to be on — and when
    /// that was "..", as it is on every freshly entered folder, <c>ShellScope</c> returned empty
    /// and no menu appeared at all.</para></summary>
    private IReadOnlyList<FileEntry> ContextTargets(char side, FileEntry anchor)
    {
        if (anchor.IsParent) return [];
        var marks = Marks(side);
        if (marks.Contains(MarkKey(anchor)))
            return Listing(side).Where(e => !e.IsParent && marks.Contains(MarkKey(e))).ToList();
        return [anchor];
    }

    private void Notice(string message)
    {
        _notice = message;
        MarkDirty();
    }

    // ── Properties (Alt+Enter) ───────────────────────────────────────────
    private void ShowProperties(char side)
    {
        if (ShellScope(side, "Properties") is not { } paths) return;

        // One file goes through ShellExecuteEx: no COM, and it is far and away the common
        // case. Several have to go through IContextMenu — ShellExecuteEx has no multi-item
        // form, and the combined sheet is the whole point of having marked them.
        bool ok = paths.Count == 1
            ? ShellProperties.Show(paths[0])
            : ShellContextMenu.InvokeVerb(paths, "properties");
        if (!ok) Notice("Couldn't open properties");
    }

    // ── Context menu ─────────────────────────────────────────────────────
    /// <summary>The context-menu key: open under the cursor row, where the eye already is.
    /// Falls back to the pane's top-left if that row isn't realized.</summary>
    private void ShowContextMenuAtCursor(char side)
    {
        float x = 40f, y = 80f;
        if (CursorRowBox(side) is { } box)
        {
            x = box.X + 24f;
            y = box.Y + box.Height;
        }
        ShowContextMenuAt(side, x, y);
    }

    /// <summary>Where the cursor's row is on screen, or null when no row has it. In Detail the
    /// list draws the cursor (<see cref="ListDrawsCursor"/>) and its rows are made without it, so
    /// the box is worked out from the list: its rows are one height, stacked from the top of its
    /// content, scrolled by its offset. Elsewhere it is the row (cell, tile) that drew the cursor
    /// — on screen, with the scroll applied: its layout box is where it would be unscrolled, and
    /// a menu opened there once the list had scrolled opened below the window.</summary>
    internal Rect? CursorRowBox(char side)
    {
        if (ViewOf(side) == ViewMode.Detail && VList(side).Current is { SelectionHighlight: not null } list)
        {
            if (list.SelectedIndex < 0) return null;
            var content = list.ScreenContentBox;
            float h = list.ItemHeight;
            return new Rect(content.X, content.Y + list.SelectedIndex * h - list.ScrollOffsetY, content.Width, h);
        }
        return CursorRowRef(side).Current is { IsDetached: false } row ? row.ScreenBorderBox : null;
    }

    /// <summary>Open the shell menu at a point in window (layout) coordinates.
    /// <paramref name="anchor"/> is the row the pointer named, when a pointer opened it.</summary>
    private void ShowContextMenuAt(char side, float winX, float winY, FileEntry? anchor = null)
    {
        if (ShellScope(side, "Context menu", anchor) is not { } paths) return;

        // Layout space is logical; the window's client area is in physical pixels.
        float scale = _window.DisplayScale > 0 ? _window.DisplayScale : 1f;
        var (sx, sy) = ShellContextMenu.ClientToScreen((int)(winX * scale), (int)(winY * scale));

        // Modal: this returns once the menu has closed and any picked verb has run. That verb
        // may well have renamed, deleted or created something, so re-read the folder.
        if (ShellContextMenu.Show(paths, sx, sy)) RefreshPane(side);
        else Notice("Couldn't open the context menu");
    }

    // ── Right-button hold ────────────────────────────────────────────────
    // A right press marks (Total Commander's right-drag selection); a right press *held
    // still* is the context menu instead. The two can't be told apart at press time, so the
    // press marks optimistically and a hold takes it back — that keeps a quick right-click's
    // mark instant, which is the gesture people actually use all day.
    private const int HoldMs = 400;
    private const float HoldSlop = 5f;     // movement past this is a drag, not a hold

    private System.Threading.Timer? _holdTimer;
    private bool _holdArmed;
    private char _holdSide;
    private FileEntry? _holdEntry;         // the row the press landed on — what the menu acts on
    private float _holdX, _holdY;

    private void ArmContextHold(char side, FileEntry entry, float x, float y)
    {
        if (!ShellContextMenu.IsSupported) return;
        _holdArmed = true; _holdSide = side; _holdEntry = entry; _holdX = x; _holdY = y;
        _holdTimer ??= new System.Threading.Timer(
            _ => HultUI.Application.InvokeOnMainThread(FireContextHold));
        _holdTimer.Change(HoldMs, System.Threading.Timeout.Infinite);
    }

    /// <summary>Called on every pointer move and on release. Small movements are the hand
    /// shaking, not a drag, so the hold survives them.</summary>
    private void UpdateContextHold(float x, float y)
    {
        if (!_holdArmed) return;
        if (Math.Abs(x - _holdX) > HoldSlop || Math.Abs(y - _holdY) > HoldSlop) CancelContextHold();
    }

    private void CancelContextHold()
    {
        if (!_holdArmed) return;
        _holdArmed = false;
        _holdTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }

    private void FireContextHold()
    {
        if (!_holdArmed || _holdEntry is not { } entry) return;
        _holdArmed = false;
        // Revert first: it decides whether this row counts as marked, and a row that *was*
        // marked before the press had its mark taken off by the optimistic toggle.
        RevertRightMark();
        // Move the cursor onto the row the menu is about, so the highlight and the menu agree.
        int idx = IndexOfPath(Rows(_holdSide), entry.FullPath);
        if (idx >= 0) SetCursor(_holdSide, idx);
        ShowContextMenuAt(_holdSide, _holdX, _holdY, entry);
    }

    /// <summary>Undo the marks this right-drag applied and end it. Movement cancels the hold,
    /// so in practice that is the single row the press landed on.</summary>
    private void RevertRightMark()
    {
        if (!_rmarkActive) return;
        var marks = Marks(_rmarkSide);
        foreach (var path in _rmarkSeen)
        {
            if (_rmarkAdd) marks.Remove(path); else marks.Add(path);
        }
        EndRightMark();
        MarkDirty();
    }
}
