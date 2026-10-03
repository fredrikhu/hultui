using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Terminal.Emulation;

namespace HultUI.Terminal;

/// <summary>
/// OSC 8 links. Under the pointer a link is underlined, every stretch of it (a link that wrapped
/// is one link when the program gave it an id), and the pointer becomes a hand. Ctrl+click
/// raises <see cref="TerminalController.LinkActivated"/>; a plain click selects, as it does
/// anywhere else. Ctrl+click is the element's even while the program tracks the mouse.
/// </summary>
public sealed partial class TerminalElement
{
    /// <summary>The cell under the pointer, while it is over the element.</summary>
    private (int Column, int Row)? _pointerCell;
    private LinkSpan? _hoverLink;

    private LinkSpan? LinkAtCell(int column, int row)
    {
        foreach (var link in _snapshot.Links)
            if (link.Covers(column, row)) return link;
        return null;
    }

    /// <summary>Follows the pointer: the hovered link, and the hand over it.</summary>
    private void TrackHover(Point position)
    {
        var cell = CellAt(position);
        _pointerCell = cell;
        SetHoverLink(LinkAtCell(cell.Column, cell.Row));
    }

    private void OnPointerLeave(PointerLeaveEvent e)
    {
        _pointerCell = null;
        SetHoverLink(null);
    }

    private void SetHoverLink(LinkSpan? link)
    {
        if (link == _hoverLink) return;
        bool wasOver = _hoverLink is not null;
        _hoverLink = link;
        if (wasOver != link is not null)
        {
            Inherited.Cursor = link is not null ? CursorType.Pointer : CursorType.Text;
            MarkStyleDirty(); // the window puts the new cursor up without the pointer moving
        }
        MarkPaintDirty();
    }

    /// <summary>Ctrl+click on a link: tells the application. Returns whether it was one.</summary>
    private bool ActivateLink(PointerDownEvent e, int column, int row)
    {
        if ((e.Modifiers & KeyModifiers.Ctrl) == 0 || e.Button != PointerEvent.LeftButton) return false;
        if (LinkAtCell(column, row) is not { } link) return false;
        _controller!.RaiseLinkActivated(new TerminalLink(link.Url, link.Id));
        return true;
    }

    /// <summary>The hovered link's underline on one row, after the text. Recomputed from this
    /// frame's snapshot, so a link that scrolled away is not underlined where it was.</summary>
    private void DrawLinkUnderline(IRenderContext ctx, TerminalSnapshot s, int row, float left, float y,
        float underlineY, float t, float scale)
    {
        if (_pointerCell is not { } p || LinkAtCell(p.Column, p.Row) is not { } hovered) return;
        var cells = s.Row(row);
        foreach (var link in s.Links)
        {
            if (link.Row != row || !link.SameLinkAs(hovered)) continue;
            float x0 = Snap(left + link.Start * _cellWidth, scale), x1 = Snap(left + link.End * _cellWidth, scale);
            var color = Rgb(cells[Math.Clamp(link.Start, 0, cells.Length - 1)].Foreground);
            ctx.DrawRect(new Rect(x0, underlineY, x1 - x0, t), color);
        }
    }
}
