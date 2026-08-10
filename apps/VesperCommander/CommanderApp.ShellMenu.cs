using HultUI.Core.Elements;
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
    /// there is nothing the shell can act on.</summary>
    private List<string>? ShellScope(char side, string noun)
    {
        if (!ShellContextMenu.IsSupported)
        {
            Notice($"{noun} is a Windows shell feature");
            return null;
        }
        var items = OperationTargets(side);
        if (items.Count == 0) return null;    // an empty folder, or the cursor on ".."
        if (items.Any(e => ArchivePath.InArchive(e.FullPath)))
        {
            // Archive members live in Vesper's VFS; the shell has no item to bind to.
            Notice($"No {noun.ToLowerInvariant()} for archive members");
            return null;
        }
        return items.Select(e => e.FullPath).ToList();
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
        if (CursorRowRef(side).Current is { } row)
        {
            var box = row.Layout.AbsoluteBorderBox;
            x = box.X + 24f;
            y = box.Y + box.Height;
        }
        ShowContextMenuAt(side, x, y);
    }

    /// <summary>Open the shell menu at a point in window (layout) coordinates.</summary>
    private void ShowContextMenuAt(char side, float winX, float winY)
    {
        if (ShellScope(side, "Context menu") is not { } paths) return;

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
    private float _holdX, _holdY;

    private void ArmContextHold(char side, float x, float y)
    {
        if (!ShellContextMenu.IsSupported) return;
        _holdArmed = true; _holdSide = side; _holdX = x; _holdY = y;
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
        if (!_holdArmed) return;
        _holdArmed = false;
        RevertRightMark();
        ShowContextMenuAt(_holdSide, _holdX, _holdY);
    }

    /// <summary>Undo the marks this right-drag applied and end it. Movement cancels the hold,
    /// so in practice that is the single row the press landed on.</summary>
    private void RevertRightMark()
    {
        if (!_rmarkActive) return;
        var marks = Marks(_rmarkSide);
        foreach (var name in _rmarkSeen)
        {
            if (_rmarkAdd) marks.Remove(name); else marks.Add(name);
        }
        EndRightMark();
        MarkDirty();
    }
}
