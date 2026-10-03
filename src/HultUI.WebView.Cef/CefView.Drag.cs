using System.Runtime.InteropServices;
using HultUI.Core.Events;
using HultUI.WebView.Cef.Interop;

namespace HultUI.WebView.Cef;

/// <summary>
/// Step 5.4: drags. Off-screen, CEF has no drop target and no drag source of its own: a drag
/// from outside (files, text) arrives through <see cref="SendDrag"/>, and a drag the page
/// starts (<c>start_dragging</c>) is carried here, by the pointer events that follow it, to
/// a drop on the page itself. A page's drag does not leave the page for another application:
/// that needs an operating-system drag source, which the Linux engine does not have.
/// </summary>
internal sealed unsafe partial class CefView
{
    private const cef_drag_operations_mask_t Offered =
        cef_drag_operations_mask_t.DRAG_OPERATION_COPY | cef_drag_operations_mask_t.DRAG_OPERATION_LINK
        | cef_drag_operations_mask_t.DRAG_OPERATION_GENERIC | cef_drag_operations_mask_t.DRAG_OPERATION_MOVE;

    /// <summary>What the page last said it would do with the drag over it (<c>update_drag_cursor</c>).</summary>
    private int _dragOperation;

    /// <summary>A drag the page started, carried by the pointer until the button goes up
    /// (CEF's UI thread); its operations.</summary>
    private bool _pageDrag;
    private cef_drag_operations_mask_t _pageDragAllowed;

    /// <summary>
    /// A drag from outside. CEF answers through <c>update_drag_cursor</c>, after; the answer
    /// given is the one before this step, as the window's next query reads it again (HultUI's
    /// thread never waits for CEF's).
    /// </summary>
    public bool SendDrag(in WebDragInput input)
    {
        var i = input;
        if (i.Action == WebDragAction.Enter) Volatile.Write(ref _dragOperation, 0);
        OnUi(() =>
        {
            if (_browserHost == null) return;
            var h = _browserHost;
            float scale = ScaleNow();
            var e = new cef_mouse_event_t
            {
                x = (int)MathF.Floor(i.X / scale),
                y = (int)MathF.Floor(i.Y / scale),
                modifiers = CefKeys.Flags(i.Modifiers) | (uint)cef_event_flags_t.EVENTFLAG_LEFT_MOUSE_BUTTON,
            };
            var allowed = Allowed(i.Modifiers);
            switch (i.Action)
            {
                case WebDragAction.Enter:
                    _dragCarries = (i.Files.Count, i.Text);
                    h->drag_target_drag_enter(h, DragData(i), &e, allowed);   // the call takes the reference
                    break;
                case WebDragAction.Over:
                    h->drag_target_drag_over(h, &e, allowed);
                    break;
                case WebDragAction.Leave:
                    h->drag_target_drag_leave(h);
                    break;
                case WebDragAction.Drop:
                    if (_dragCarries != (i.Files.Count, i.Text))
                    {
                        // SDL names a drag's files only at the drop: enter again with them. The
                        // page has its dragenter at the first step and its dragover at the
                        // second, as a browser gives them, before the drop.
                        h->drag_target_drag_leave(h);
                        h->drag_target_drag_enter(h, DragData(i), &e, allowed);
                        h->drag_target_drag_over(h, &e, allowed);
                        h->drag_target_drag_over(h, &e, allowed);
                    }
                    h->drag_target_drop(h, &e);
                    break;
            }
        });
        return i.Action != WebDragAction.Leave && Volatile.Read(ref _dragOperation) != 0;
    }

    /// <summary>What the drag from outside carried at its enter: its file count and text.</summary>
    private (int Files, string? Text) _dragCarries;

    private static cef_drag_data_t* DragData(in WebDragInput i)
    {
        var data = Capi.cef_drag_data_create();
        foreach (var file in i.Files)
        {
            var path = CefString.Make(file);
            data->add_file(data, &path, null);
            CefString.Clear(&path);
        }
        if (i.Text is { } text)
        {
            var t = CefString.Make(text);
            data->set_fragment_text(data, &t);
            CefString.Clear(&t);
        }
        return data;
    }

    /// <summary>The operations a drag offers: what the modifiers pick (Ctrl copies, Shift
    /// moves, both link, as on the desktop), or any.</summary>
    private static cef_drag_operations_mask_t Allowed(KeyModifiers m) =>
        (m.HasFlag(KeyModifiers.Ctrl), m.HasFlag(KeyModifiers.Shift)) switch
        {
            (true, true) => cef_drag_operations_mask_t.DRAG_OPERATION_LINK,
            (true, false) => cef_drag_operations_mask_t.DRAG_OPERATION_COPY,
            (false, true) => cef_drag_operations_mask_t.DRAG_OPERATION_MOVE,
            _ => Offered,
        };

    /// <summary>
    /// A pointer event while the page drags (CEF's UI thread): a drag over the page instead,
    /// and the button going up drops it. True when it was taken.
    /// </summary>
    private bool CarryPageDrag(in WebPointerInput i, cef_mouse_event_t* e)
    {
        if (!_pageDrag || _browserHost == null) return false;
        var h = _browserHost;
        switch (i.Action)
        {
            case WebPointerAction.Move:
                h->drag_target_drag_over(h, e, _pageDragAllowed);
                return true;
            case WebPointerAction.Up when i.Button == WebPointerButtons.Left:
                EndPageDrag(e, drop: true);
                return true;
            case WebPointerAction.Leave:
                h->drag_target_drag_leave(h);
                return true;
            case WebPointerAction.Down:
                EndPageDrag(e, drop: false);   // a button went down mid-drag: give up
                return false;
            default:
                return true;   // the wheel waits for the drop
        }
    }

    private void EndPageDrag(cef_mouse_event_t* e, bool drop)
    {
        var h = _browserHost;
        _pageDrag = false;
        if (h == null) return;
        var op = drop ? (cef_drag_operations_mask_t)Volatile.Read(ref _dragOperation) : 0;
        if (drop && op != 0) h->drag_target_drop(h, e);
        else h->drag_target_drag_leave(h);
        h->drag_source_ended_at(h, e->x, e->y, op);
        h->drag_source_system_drag_ended(h);
    }

    /// <summary>The page started a drag: carry it (see <see cref="CarryPageDrag"/>).</summary>
    private void StartPageDrag(cef_drag_data_t* data, cef_drag_operations_mask_t allowed, int x, int y)
    {
        if (_browserHost == null) return;
        var h = _browserHost;
        _pageDrag = true;
        _pageDragAllowed = allowed;
        Volatile.Write(ref _dragOperation, 0);
        var e = new cef_mouse_event_t { x = x, y = y, modifiers = (uint)cef_event_flags_t.EVENTFLAG_LEFT_MOUSE_BUTTON };
        h->drag_target_drag_enter(h, CefObject.AddRef(data), &e, allowed);
    }

    [UnmanagedCallersOnly]
    private static int StartDragging(cef_render_handler_t* s, cef_browser_t* b, cef_drag_data_t* data,
        cef_drag_operations_mask_t allowed, int x, int y)
    {
        try
        {
            if (Of(s) is not { } v) return 0;
            // After this returns: CEF has not finished starting the drag inside the call.
            var kept = CefObject.AddRef(data);
            v.OnUi(() =>
            {
                v.StartPageDrag(kept, allowed, x, y);
                CefObject.Release(kept);
            });
            return 1;
        }
        catch (Exception ex) { CefLog.Error("start_dragging", ex); return 0; }
        finally
        {
            CefObject.Release(b);
            CefObject.Release(data);
        }
    }

    [UnmanagedCallersOnly]
    private static void UpdateDragCursor(cef_render_handler_t* s, cef_browser_t* b, cef_drag_operations_mask_t operation)
    {
        try { if (Of(s) is { } v) Volatile.Write(ref v._dragOperation, (int)operation); }
        catch (Exception ex) { CefLog.Error("update_drag_cursor", ex); }
        finally { CefObject.Release(b); }
    }
}
