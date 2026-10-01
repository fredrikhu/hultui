using System.Collections.Concurrent;
using CefOsr.Capi;
using static CefOsr.Native;

namespace CefOsr;

/// <summary>
/// Question 5: input and focus. Mouse, wheel, keys (what cef_key_event_t needs for a page's
/// keydown and for a text field), focus in and out, the &lt;select&gt; popup, and IME. The
/// page reports every event it sees through console.log ("EV ..."); this prints them in order
/// after each step. Run with --loop=mt, so every call is made from a thread that is not CEF's.
/// </summary>
internal static unsafe class Q5
{
    const string Page = """
        <html><head><title>Input</title><style>
        body { margin:0; font: 16px sans-serif; }
        #t { position:absolute; left:20px; top:20px; width:200px; height:30px; }
        #b { position:absolute; left:20px; top:70px; width:80px; height:30px; }
        #s { position:absolute; left:20px; top:120px; width:200px; height:30px; }
        #tall { position:absolute; left:0; top:0; width:10px; height:3000px; }
        </style></head><body>
        <div id="tall"></div>
        <input id="t"><button id="b">Button</button>
        <select id="s"><option>one</option><option>two</option><option>three</option><option>four</option></select>
        <script>
        const L = (...a) => console.log('EV ' + a.join(' '));
        let moves = 0;
        addEventListener('mousemove', e => { if (moves++ < 2) L('mousemove', e.clientX, e.clientY, 'buttons', e.buttons); });
        for (const n of ['mousedown', 'mouseup', 'click', 'dblclick', 'contextmenu', 'auxclick'])
          addEventListener(n, e => L(n, 'button', e.button, 'at', e.clientX, e.clientY, 'detail', e.detail, 'on', e.target.id || e.target.tagName));
        addEventListener('wheel', e => L('wheel', 'dx', e.deltaX, 'dy', e.deltaY, 'mode', e.deltaMode));
        addEventListener('scroll', () => L('scroll', scrollX, scrollY));
        for (const n of ['keydown', 'keypress', 'keyup'])
          addEventListener(n, e => L(n, 'key', JSON.stringify(e.key), 'code', e.code || '""', 'keyCode', e.keyCode,
            'which', e.which, 'repeat', e.repeat, 'on', e.target.id || e.target.tagName), true);
        t.addEventListener('input', e => L('input', JSON.stringify(t.value), 'inputType', e.inputType, 'composing', e.isComposing));
        s.addEventListener('change', () => L('change', 'select', s.value));
        addEventListener('focus', e => L('focus', e.target.id || 'window'), true);
        addEventListener('blur', e => L('blur', e.target.id || 'window'), true);
        for (const n of ['compositionstart', 'compositionupdate', 'compositionend'])
          t.addEventListener(n, e => L(n, JSON.stringify(e.data)));
        L('ready', 'dpr', devicePixelRatio, 'inner', innerWidth, innerHeight, 'hasFocus', document.hasFocus());
        </script></body></html>
        """;

    static readonly ConcurrentQueue<string> Events = new();
    static cef_browser_host_t* H;

    public static int Run(Options o)
    {
        if (Host.Initialize([], o) != 1) return 1;
        Host.Pump(() => Host.ContextReady, 15000);

        nint browser = 0;
        Host.AfterCreated += b => { AddRef((void*)b); browser = b; };
        Host.ConsoleMessage += (b, m, src, line) => { if (m.StartsWith("EV ")) Events.Enqueue(m[3..]); };
        Host.TakeFocus += (b, next) => Events.Enqueue($"[host] on_take_focus next={next} on {Native.Thread()}");
        Host.Cursor += (b, c) => Events.Enqueue($"[host] cursor {c}");
        int popupPaints = 0;
        cef_rect_t popupRect = default;
        Host.PopupShow += (b, show) => Events.Enqueue($"[host] on_popup_show {show} on {Native.Thread()}");
        Host.PopupSize += (b, r) => { popupRect = r; Events.Enqueue($"[host] on_popup_size ({r.x},{r.y} {r.width}x{r.height})"); };
        Host.Paint += (b, type, w, h, buf, n, d) =>
        {
            if (type != cef_paint_element_type_t.PET_POPUP) return;
            if (popupPaints++ == 0)
            {
                Events.Enqueue($"[host] first PET_POPUP paint {w}x{h}");
                Png.SaveBgra("results/q5-select-popup.png", buf, w, h);
            }
        };
        Host.ImeRange += (b, range, rects) => Events.Enqueue(
            $"[host] on_ime_composition_range_changed {range.from}-{range.to}: " +
            string.Join(" ", rects.Select(r => $"({r.x},{r.y} {r.width}x{r.height})")));
        Host.KeyEvent += (b, e) =>
        {
            if (e.type != cef_key_event_type_t.KEYEVENT_CHAR)
                Events.Enqueue($"[host] on_key_event (unhandled by page) {e.type} wkc 0x{e.windows_key_code:X2} native {e.native_key_code}");
        };

        Host.ViewWidth = 800; Host.ViewHeight = 600; Host.Scale = 1.5f;
        Host.CreateBrowser(Host.DataUrl(Page), sync: false);
        Host.Pump(() => browser != 0 && Events.Any(e => e.StartsWith("ready")), 15000);
        var b0 = (cef_browser_t*)browser;
        H = b0->get_host(b0);
        Step("loaded");

        H->set_focus(H, 1);
        Step("set_focus(1)");

        // Mouse: CEF's coordinates are view coordinates (DIP); the button's centre is (60, 85).
        Move(60, 85);
        Click(60, 85, cef_mouse_button_type_t.MBT_LEFT);
        Step("move + left click at (60, 85) in view coordinates");
        Click(60, 85, cef_mouse_button_type_t.MBT_RIGHT);
        Step("right click at (60, 85)");
        Click(60, 85, cef_mouse_button_type_t.MBT_LEFT, count: 1);
        Click(60, 85, cef_mouse_button_type_t.MBT_LEFT, count: 2);
        Step("double click: click_count 1 then 2");
        var me = new cef_mouse_event_t { x = 400, y = 300 };
        H->send_mouse_wheel_event(H, &me, 0, -120);
        Step("wheel delta_y -120 (one notch down)");
        H->send_mouse_wheel_event(H, &me, 0, 120);
        Step("wheel delta_y +120");

        // Keys into the text field.
        Click(120, 35, cef_mouse_button_type_t.MBT_LEFT);
        Step("click into the text field");
        Key(cef_key_event_type_t.KEYEVENT_RAWKEYDOWN, 0x41, 38, 'a');
        Key(cef_key_event_type_t.KEYEVENT_CHAR, 0x41, 38, 'a');
        Key(cef_key_event_type_t.KEYEVENT_KEYUP, 0x41, 38, 'a');
        Step("'a' as cefclient sends it: RAWKEYDOWN, CHAR, KEYUP; wkc 0x41, native 38 (XKB), character 'a'");
        Key(cef_key_event_type_t.KEYEVENT_RAWKEYDOWN, 0x42, 0, 'b');
        Key(cef_key_event_type_t.KEYEVENT_CHAR, 0x42, 0, 'b');
        Key(cef_key_event_type_t.KEYEVENT_KEYUP, 0x42, 0, 'b');
        Step("'b' with native_key_code 0");
        Key(cef_key_event_type_t.KEYEVENT_RAWKEYDOWN, 0x43, 54, '\0');
        Key(cef_key_event_type_t.KEYEVENT_CHAR, 'c', 0, 'c');
        Key(cef_key_event_type_t.KEYEVENT_KEYUP, 0x43, 54, '\0');
        Step("'c' as HultUI has it: keydown from the key code (no character), the text as a CHAR on its own, keyup");
        Key(cef_key_event_type_t.KEYEVENT_CHAR, 'é', 0, 'é');
        Key(cef_key_event_type_t.KEYEVENT_CHAR, '日', 0, '日');
        Step("CHAR alone: 'é' and '日' (text input with no key)");
        Key(cef_key_event_type_t.KEYEVENT_RAWKEYDOWN, 0x25, 113, '\0');
        Key(cef_key_event_type_t.KEYEVENT_KEYUP, 0x25, 113, '\0');
        Key(cef_key_event_type_t.KEYEVENT_CHAR, 'X', 0, 'X');
        Step("ArrowLeft (wkc 0x25, native 113), then 'X' as text: the caret moved if X lands before the last character");
        Key(cef_key_event_type_t.KEYEVENT_RAWKEYDOWN, 0x0D, 36, '\r');
        Key(cef_key_event_type_t.KEYEVENT_CHAR, 0x0D, 36, '\r');
        Key(cef_key_event_type_t.KEYEVENT_KEYUP, 0x0D, 36, '\r');
        Step("Enter (wkc 0x0D, native 36, character \\r)");
        Key(cef_key_event_type_t.KEYEVENT_RAWKEYDOWN, 0x09, 23, '\t');
        Key(cef_key_event_type_t.KEYEVENT_CHAR, 0x09, 23, '\t');
        Key(cef_key_event_type_t.KEYEVENT_KEYUP, 0x09, 23, '\t');
        Step("Tab (wkc 0x09, native 23): focus to the button");
        Tab(); Step("Tab: to the select");
        Tab(); Step("Tab past the last element");
        Tab(shift: true); Step("Shift+Tab");

        // The <select>: its list is a popup widget, painted as a second buffer.
        Click(120, 135, cef_mouse_button_type_t.MBT_LEFT);
        Host.Wait(500);
        Step("click on the select");
        if (popupRect.height > 0)
        {
            // The third option: the list's rows are equal; pick the middle of row 3 of 4.
            int y = popupRect.y + popupRect.height * 5 / 8;
            Move(popupRect.x + 20, y);
            Click(popupRect.x + 20, y, cef_mouse_button_type_t.MBT_LEFT);
            Host.Wait(300);
            Step($"click in the popup at ({popupRect.x + 20}, {y}), view coordinates");
        }

        // The CHAR recipe: what the keypress's key comes from.
        Click(120, 35, cef_mouse_button_type_t.MBT_LEFT);
        Key(cef_key_event_type_t.KEYEVENT_RAWKEYDOWN, 0x44, 40, '\0');
        Key(cef_key_event_type_t.KEYEVENT_CHAR, 0x44, 40, 'd');
        Key(cef_key_event_type_t.KEYEVENT_KEYUP, 0x44, 40, '\0');
        Step("'d': CHAR carries the key's own wkc 0x44 and native 40 with the character");
        Key(cef_key_event_type_t.KEYEVENT_RAWKEYDOWN, 0x45, 26, '\0', (uint)cef_event_flags_t.EVENTFLAG_SHIFT_DOWN);
        Key(cef_key_event_type_t.KEYEVENT_CHAR, 0x45, 26, 'E', (uint)cef_event_flags_t.EVENTFLAG_SHIFT_DOWN);
        Key(cef_key_event_type_t.KEYEVENT_KEYUP, 0x45, 26, '\0', (uint)cef_event_flags_t.EVENTFLAG_SHIFT_DOWN);
        Step("Shift+E the same way, with EVENTFLAG_SHIFT_DOWN");
        Key(cef_key_event_type_t.KEYEVENT_CHAR, 0, 0, 'é');
        Step("CHAR 'é' with wkc 0 and native 0 (text with no key)");
        var txt = Make("ü");
        var none = new cef_range_t { from = uint.MaxValue, to = uint.MaxValue };
        H->ime_commit_text(H, &txt, &none, 0);
        Step("ime_commit_text(\"ü\") with no composition (text with no key)");

        // IME: compose in the text field, then commit.
        Click(120, 35, cef_mouse_button_type_t.MBT_LEFT);
        Step("click into the text field again");
        var comp = Make("にほん");
        var under = new cef_composition_underline_t
        {
            size = (ulong)sizeof(cef_composition_underline_t),
            range = new cef_range_t { from = 0, to = 3 }, color = 0xFF000000, thick = 0,
        };
        var invalid = new cef_range_t { from = uint.MaxValue, to = uint.MaxValue };
        var sel = new cef_range_t { from = 3, to = 3 };
        H->ime_set_composition(H, &comp, 1, &under, &invalid, &sel);
        Step("ime_set_composition(\"にほん\", caret at 3)");
        var commit = Make("日本");
        H->ime_commit_text(H, &commit, &invalid, 0);
        Step("ime_commit_text(\"日本\")");

        H->set_focus(H, 0);
        Step("set_focus(0)");

        Release(H);
        Host.Close(b0);
        Q1.Shutdown();
        return 0;
    }

    static void Move(int x, int y)
    {
        var e = new cef_mouse_event_t { x = x, y = y };
        H->send_mouse_move_event(H, &e, 0);
    }

    static void Click(int x, int y, cef_mouse_button_type_t button, int count = 1)
    {
        var flag = button switch
        {
            cef_mouse_button_type_t.MBT_LEFT => cef_event_flags_t.EVENTFLAG_LEFT_MOUSE_BUTTON,
            cef_mouse_button_type_t.MBT_RIGHT => cef_event_flags_t.EVENTFLAG_RIGHT_MOUSE_BUTTON,
            _ => cef_event_flags_t.EVENTFLAG_MIDDLE_MOUSE_BUTTON,
        };
        var down = new cef_mouse_event_t { x = x, y = y, modifiers = (uint)flag };
        H->send_mouse_click_event(H, &down, button, 0, count);
        var up = new cef_mouse_event_t { x = x, y = y };
        H->send_mouse_click_event(H, &up, button, 1, count);
    }

    static void Key(cef_key_event_type_t type, int wkc, int native, char ch, uint modifiers = 0)
    {
        var e = new cef_key_event_t
        {
            size = (ulong)sizeof(cef_key_event_t), type = type, windows_key_code = wkc,
            native_key_code = native, character = ch, unmodified_character = ch, modifiers = modifiers,
        };
        H->send_key_event(H, &e);
    }

    static void Tab(bool shift = false)
    {
        uint m = shift ? (uint)cef_event_flags_t.EVENTFLAG_SHIFT_DOWN : 0;
        Key(cef_key_event_type_t.KEYEVENT_RAWKEYDOWN, 0x09, 23, '\t', m);
        Key(cef_key_event_type_t.KEYEVENT_CHAR, 0x09, 23, '\t', m);
        Key(cef_key_event_type_t.KEYEVENT_KEYUP, 0x09, 23, '\t', m);
    }

    static void Step(string what)
    {
        Host.Wait(250);
        Console.WriteLine($"--- {what}");
        while (Events.TryDequeue(out var e)) Console.WriteLine("      " + e);
    }
}
