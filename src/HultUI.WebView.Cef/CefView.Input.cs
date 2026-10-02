using System.Runtime.InteropServices;
using HultUI.Core.Events;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.WebView.Cef.Interop;

namespace HultUI.WebView.Cef;

/// <summary>
/// Step 5.3: input, focus, the input method, the cursor, and the <c>&lt;select&gt;</c> popup.
/// CEF has no keyboard or input method of its own off-screen, so the element forwards both
/// (no <see cref="WebEngineCapabilities.NativeKeyboard"/>), and how CEF wants them is spike
/// L0's question 5.
/// </summary>
internal sealed unsafe partial class CefView
{
    // ──── Pointer ────

    public void SendPointer(in WebPointerInput input)
    {
        var i = input;
        OnUi(() =>
        {
            if (_browserHost == null) return;
            var h = _browserHost;
            // CEF's mouse events are in view coordinates: the page's device pixels over its scale.
            float scale = ScaleNow();
            var e = new cef_mouse_event_t
            {
                x = (int)MathF.Floor(i.X / scale),
                y = (int)MathF.Floor(i.Y / scale),
                modifiers = CefKeys.Flags(i.Modifiers) | ButtonFlags(i.Held),
            };
            if (CarryPageDrag(i, &e)) return;
            switch (i.Action)
            {
                case WebPointerAction.Move:
                    h->send_mouse_move_event(h, &e, 0);
                    break;
                case WebPointerAction.Leave:
                    h->send_mouse_move_event(h, &e, 1);
                    break;
                case WebPointerAction.Down or WebPointerAction.Up when Button(i.Button) is { } button:
                    h->send_mouse_click_event(h, &e, button, i.Action == WebPointerAction.Up ? 1 : 0, Math.Max(1, i.ClickCount));
                    break;
                case WebPointerAction.Up when i.Button == WebPointerButtons.Back && _browser != null:
                    _browser->go_back(_browser);   // the side buttons are history, as in a browser
                    break;
                case WebPointerAction.Up when i.Button == WebPointerButtons.Forward && _browser != null:
                    _browser->go_forward(_browser);
                    break;
                case WebPointerAction.Wheel:
                    // A notch is 120, Windows' WHEEL_DELTA; positive y scrolls up (spike L0,
                    // question 5). HultUI's positive x scrolls toward the left, as CEF's does.
                    h->send_mouse_wheel_event(h, &e, (int)MathF.Round(i.WheelX * 120), (int)MathF.Round(i.WheelY * 120));
                    break;
            }
        });
    }

    private static cef_mouse_button_type_t? Button(WebPointerButtons b) => b switch
    {
        WebPointerButtons.Left => cef_mouse_button_type_t.MBT_LEFT,
        WebPointerButtons.Middle => cef_mouse_button_type_t.MBT_MIDDLE,
        WebPointerButtons.Right => cef_mouse_button_type_t.MBT_RIGHT,
        _ => null,
    };

    private static uint ButtonFlags(WebPointerButtons held)
    {
        cef_event_flags_t f = 0;
        if (held.HasFlag(WebPointerButtons.Left)) f |= cef_event_flags_t.EVENTFLAG_LEFT_MOUSE_BUTTON;
        if (held.HasFlag(WebPointerButtons.Middle)) f |= cef_event_flags_t.EVENTFLAG_MIDDLE_MOUSE_BUTTON;
        if (held.HasFlag(WebPointerButtons.Right)) f |= cef_event_flags_t.EVENTFLAG_RIGHT_MOUSE_BUTTON;
        return (uint)f;
    }

    // ──── Keys and text ────

    /// <summary>The key down now, whose text is sent as its CHAR (CEF's UI thread).</summary>
    private (int Wkc, int Native, uint Flags)? _keyDown;

    /// <summary>Whether an input-method composition is open (CEF's UI thread).</summary>
    private bool _composing;

    /// <summary>
    /// A key as CEF's recipe has it (spike L0, question 5): <c>RAWKEYDOWN</c> and <c>KEYUP</c>
    /// carrying the Windows key code and the XKB keycode. The text a key types arrives apart,
    /// as SDL sends it (<see cref="SendText"/>), and goes as a <c>CHAR</c> with the same key's
    /// codes. Keys that type a control character SDL sends no text for (Enter, Tab, Backspace,
    /// Escape) get their <c>CHAR</c> here, as Windows' WM_CHAR would give it.
    /// </summary>
    public void SendKey(in WebKeyInput input)
    {
        var i = input;
        OnUi(() =>
        {
            if (_browserHost == null) return;
            int wkc = CefKeys.WindowsKeyCode(i.Key, i.ScanCode);
            int native = CefKeys.Native(i.Key, i.ScanCode);
            if (wkc == 0 && native == 0) return;
            uint flags = CefKeys.Flags(i.Modifiers, i.Key, i.ScanCode)
                         | (i.IsRepeat ? (uint)cef_event_flags_t.EVENTFLAG_IS_REPEAT : 0);
            if (i.IsDown)
            {
                Key(cef_key_event_type_t.KEYEVENT_RAWKEYDOWN, wkc, native, flags, '\0');
                _keyDown = (wkc, native, flags);
                if (CefKeys.ControlCharacter(i.Key, i.ScanCode) is var c and not '\0')
                    Key(cef_key_event_type_t.KEYEVENT_CHAR, wkc, native, flags, c);
            }
            else
            {
                Key(cef_key_event_type_t.KEYEVENT_KEYUP, wkc, native, flags, '\0');
                if (_keyDown?.Wkc == wkc) _keyDown = null;
            }
        });
    }

    private void Key(cef_key_event_type_t type, int wkc, int native, uint flags, char character)
    {
        var e = new cef_key_event_t
        {
            size = (ulong)sizeof(cef_key_event_t),
            type = type,
            windows_key_code = wkc,
            native_key_code = native,
            modifiers = flags,
            character = character,
            unmodified_character = character,
            is_system_key = (flags & (uint)cef_event_flags_t.EVENTFLAG_ALT_DOWN) != 0 ? 1 : 0,
        };
        _browserHost->send_key_event(_browserHost, &e);
    }

    /// <summary>
    /// Text from the window's input method. Composing text goes to CEF's composition; text a
    /// key typed goes as that key's <c>CHAR</c>; any other text (a committed composition, a
    /// dead key's result, an on-screen keyboard) is committed, which inserts it without a
    /// <c>keypress</c>, as a browser does.
    /// </summary>
    public void SendText(in WebTextInput input)
    {
        var t = input;
        OnUi(() =>
        {
            if (_browserHost == null) return;
            var h = _browserHost;
            var none = new cef_range_t { from = uint.MaxValue, to = uint.MaxValue };
            if (t.IsComposition)
            {
                if (t.Text.Length == 0)
                {
                    if (_composing) h->ime_cancel_composition(h);
                    _composing = false;
                    return;
                }
                var text = CefString.Make(t.Text);
                var underline = new cef_composition_underline_t
                {
                    size = (ulong)sizeof(cef_composition_underline_t),
                    range = new cef_range_t { from = 0, to = (uint)t.Text.Length },
                    color = 0xFF000000,
                };
                uint start = (uint)Math.Clamp(t.SegmentStart, 0, t.Text.Length);
                uint end = (uint)Math.Clamp(t.SegmentStart + t.SegmentLength, (int)start, t.Text.Length);
                var selection = new cef_range_t { from = start, to = end };
                h->ime_set_composition(h, &text, 1, &underline, &none, &selection);
                CefString.Clear(&text);
                _composing = true;
                return;
            }
            if (t.Text.Length == 0) return;
            if (!_composing && _keyDown is { } key && (t.Text.Length == 1 || (t.Text.Length == 2 && char.IsSurrogatePair(t.Text, 0))))
            {
                foreach (var ch in t.Text) Key(cef_key_event_type_t.KEYEVENT_CHAR, key.Wkc, key.Native, key.Flags, ch);
                return;
            }
            var commit = CefString.Make(t.Text);
            h->ime_commit_text(h, &commit, &none, 0);
            CefString.Clear(&commit);
            _composing = false;
        });
    }

    // ──── Focus ────

    /// <summary>
    /// The page takes the keyboard. Coming in by Tab starts at its first stop, by Shift+Tab at
    /// its last: CEF has no call for that, so the page's sequential-focus starting point is
    /// moved to the document's start (or end) and a Tab (or Shift+Tab) sent from there.
    /// </summary>
    public void Focus(FocusReason reason)
    {
        OnUi(() =>
        {
            if (_browserHost == null) return;
            _browserHost->set_focus(_browserHost, 1);
            if (reason is not (FocusReason.Next or FocusReason.Previous) || _devTools is not { } devTools) return;
            bool forward = reason == FocusReason.Next;
            var place = forward ? "prepend" : "append";
            var script = "(() => { document.activeElement && document.activeElement.blur && document.activeElement.blur();" +
                         " const s = document.createElement('span'); s.tabIndex = -1;" +
                         $" (document.body || document.documentElement).{place}(s); s.focus({{ preventScroll: true }}); s.remove(); }})()";
            _ = devTools.Send("Runtime.evaluate", w => w.WriteString("expression", script)).ContinueWith(_ => OnUi(() =>
            {
                if (_browserHost == null) return;
                uint flags = forward ? 0 : (uint)cef_event_flags_t.EVENTFLAG_SHIFT_DOWN;
                Key(cef_key_event_type_t.KEYEVENT_RAWKEYDOWN, 0x09, CefKeys.Native(KeyCode.Tab, 0), flags, '\0');
                Key(cef_key_event_type_t.KEYEVENT_CHAR, 0x09, CefKeys.Native(KeyCode.Tab, 0), flags, '\t');
                Key(cef_key_event_type_t.KEYEVENT_KEYUP, 0x09, CefKeys.Native(KeyCode.Tab, 0), flags, '\0');
            }), TaskScheduler.Default);
        });
    }

    public void Blur() => OnUi(() =>
    {
        if (_browserHost == null) return;
        if (_composing) _browserHost->ime_cancel_composition(_browserHost);
        _composing = false;
        _browserHost->set_focus(_browserHost, 0);
    });

    // ──── The <select> popup, composited into the same sink ────

    private bool _popupShown;
    private cef_rect_t _popupRect;
    private byte[]? _viewCopy, _popup;
    private int _viewW, _viewH, _popupW, _popupH;

    /// <summary>
    /// A paint (CEF's UI thread). The view goes straight into the sink; while a popup (a
    /// <c>&lt;select&gt;</c>'s list, a date picker) is open, CEF paints it as a buffer of its own
    /// and both are kept, so either's change makes one frame with the popup over the page at its
    /// rectangle.
    /// </summary>
    private void Painted(cef_paint_element_type_t type, ReadOnlySpan<byte> pixels, int width, int height)
    {
        if (_host is not { } host) return;
        if (type == cef_paint_element_type_t.PET_VIEW)
        {
            if (!_popupShown)
            {
                host.Pixels.Write(width, height, PixelFormat.Bgra8888, pixels, width * 4);
                return;
            }
            Keep(ref _viewCopy, pixels);
            (_viewW, _viewH) = (width, height);
        }
        else
        {
            Keep(ref _popup, pixels);
            (_popupW, _popupH) = (width, height);
        }
        Compose(host.Pixels);
    }

    private static void Keep(ref byte[]? copy, ReadOnlySpan<byte> pixels)
    {
        if (copy is null || copy.Length != pixels.Length) copy = new byte[pixels.Length];
        pixels.CopyTo(copy);
    }

    private void Compose(PixelFrameSink sink)
    {
        if (_viewCopy is null) return;
        using var frame = sink.BeginWrite(_viewW, _viewH, PixelFormat.Bgra8888);
        var dst = frame.Pixels;
        int rowBytes = _viewW * 4;
        for (int y = 0; y < _viewH; y++)
            _viewCopy.AsSpan(y * rowBytes, rowBytes).CopyTo(dst.Slice(y * frame.Stride, rowBytes));
        if (_popupShown && _popup is { } popup)
        {
            float scale = ScaleNow();
            int px = (int)MathF.Round(_popupRect.x * scale), py = (int)MathF.Round(_popupRect.y * scale);
            int x0 = Math.Max(0, px), y0 = Math.Max(0, py);
            int x1 = Math.Min(_viewW, px + _popupW), y1 = Math.Min(_viewH, py + _popupH);
            for (int y = y0; y < y1; y++)
                popup.AsSpan(((y - py) * _popupW + (x0 - px)) * 4, (x1 - x0) * 4)
                    .CopyTo(dst.Slice(y * frame.Stride + x0 * 4, (x1 - x0) * 4));
        }
        frame.Publish();
    }

    private void PopupShown(bool show)
    {
        _popupShown = show;
        _popup = null;
        if (!show) _viewCopy = null;
        // A fresh view paint: kept while the popup is open, drawn alone once it closes.
        if (_browserHost != null) _browserHost->invalidate(_browserHost, cef_paint_element_type_t.PET_VIEW);
    }

    // ──── Callbacks ────

    [UnmanagedCallersOnly]
    private static cef_focus_handler_t* GetFocusHandler(cef_client_t* s)
    {
        try { return Of(s) is { } v ? CefObject.Give(v._focus) : null; }
        catch (Exception ex) { CefLog.Error("get_focus_handler", ex); return null; }
    }

    /// <summary>Tab went past the page's last stop (or Shift+Tab its first).</summary>
    [UnmanagedCallersOnly]
    private static void OnTakeFocus(cef_focus_handler_t* s, cef_browser_t* b, int next)
    {
        try { Of(s)?.Raise(new WebEngineEvent.FocusLeaving(next != 0)); }
        catch (Exception ex) { CefLog.Error("on_take_focus", ex); }
        finally { CefObject.Release(b); }
    }

    [UnmanagedCallersOnly]
    private static int OnCursorChange(cef_display_handler_t* s, cef_browser_t* b, ulong cursor, cef_cursor_type_t type, cef_cursor_info_t* info)
    {
        try
        {
            Of(s)?.Raise(new WebEngineEvent.CursorChanged(CursorFor(type)));
            return 1;   // handled: there is no native window for CEF to set it on
        }
        catch (Exception ex) { CefLog.Error("on_cursor_change", ex); return 1; }
        finally { CefObject.Release(b); }
    }

    /// <summary>CEF's cursor as the nearest of HultUI's.</summary>
    internal static CursorType CursorFor(cef_cursor_type_t type) => type switch
    {
        cef_cursor_type_t.CT_HAND => CursorType.Pointer,
        cef_cursor_type_t.CT_IBEAM or cef_cursor_type_t.CT_VERTICALTEXT => CursorType.Text,
        cef_cursor_type_t.CT_CROSS or cef_cursor_type_t.CT_CELL => CursorType.Crosshair,
        cef_cursor_type_t.CT_MOVE or cef_cursor_type_t.CT_GRAB or cef_cursor_type_t.CT_GRABBING => CursorType.Move,
        cef_cursor_type_t.CT_EASTRESIZE or cef_cursor_type_t.CT_WESTRESIZE or cef_cursor_type_t.CT_EASTWESTRESIZE => CursorType.ResizeEW,
        cef_cursor_type_t.CT_NORTHRESIZE or cef_cursor_type_t.CT_SOUTHRESIZE or cef_cursor_type_t.CT_NORTHSOUTHRESIZE => CursorType.ResizeNS,
        cef_cursor_type_t.CT_NORTHEASTRESIZE or cef_cursor_type_t.CT_SOUTHWESTRESIZE or cef_cursor_type_t.CT_NORTHEASTSOUTHWESTRESIZE => CursorType.ResizeNESW,
        cef_cursor_type_t.CT_NORTHWESTRESIZE or cef_cursor_type_t.CT_SOUTHEASTRESIZE or cef_cursor_type_t.CT_NORTHWESTSOUTHEASTRESIZE => CursorType.ResizeNWSE,
        cef_cursor_type_t.CT_COLUMNRESIZE => CursorType.ColResize,
        cef_cursor_type_t.CT_ROWRESIZE => CursorType.RowResize,
        cef_cursor_type_t.CT_NOTALLOWED or cef_cursor_type_t.CT_NODROP => CursorType.NotAllowed,
        cef_cursor_type_t.CT_WAIT => CursorType.Wait,
        cef_cursor_type_t.CT_PROGRESS => CursorType.Progress,
        cef_cursor_type_t.CT_NONE => CursorType.None,
        _ => CursorType.Default,
    };

    [UnmanagedCallersOnly]
    private static void OnPopupShow(cef_render_handler_t* s, cef_browser_t* b, int show)
    {
        try { Of(s)?.PopupShown(show != 0); }
        catch (Exception ex) { CefLog.Error("on_popup_show", ex); }
        finally { CefObject.Release(b); }
    }

    [UnmanagedCallersOnly]
    private static void OnPopupSize(cef_render_handler_t* s, cef_browser_t* b, cef_rect_t* rect)
    {
        try { if (Of(s) is { } v) v._popupRect = *rect; }
        catch (Exception ex) { CefLog.Error("on_popup_size", ex); }
        finally { CefObject.Release(b); }
    }

    /// <summary>The composition's characters, in view coordinates: their union, in device
    /// pixels, is where the input method's candidate window goes.</summary>
    [UnmanagedCallersOnly]
    private static void OnImeCompositionRangeChanged(cef_render_handler_t* s, cef_browser_t* b, cef_range_t* range,
        ulong count, cef_rect_t* bounds)
    {
        try
        {
            if (Of(s) is not { } v || count == 0) return;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            for (ulong i = 0; i < count; i++)
            {
                var r = bounds[i];
                x0 = Math.Min(x0, r.x); y0 = Math.Min(y0, r.y);
                x1 = Math.Max(x1, r.x + r.width); y1 = Math.Max(y1, r.y + r.height);
            }
            float scale = v.ScaleNow();
            int px = (int)MathF.Floor(x0 * scale), py = (int)MathF.Floor(y0 * scale);
            v.Raise(new WebEngineEvent.InputAreaChanged(new PixelRect(px, py,
                Math.Max(1, (int)MathF.Ceiling(x1 * scale) - px), Math.Max(1, (int)MathF.Ceiling(y1 * scale) - py))));
        }
        catch (Exception ex) { CefLog.Error("on_ime_composition_range_changed", ex); }
        finally { CefObject.Release(b); }
    }
}
