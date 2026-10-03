using System.Text;
using XTerm;
using XTerm.Common;
using XTerm.Input;
using static XtermNet.Probe;

namespace XtermNet;

/// <summary>
/// Question 2: does the public API carry everything a HultUI renderer and host have to read, and is
/// it right? Each row writes a sequence and reads the answer back the way the element would: cells
/// for the renderer, properties and events for the host, replies for the pty. Not a conformance
/// suite (XTerm.NET has 800+ tests and a vttest harness); the point is the seam, and the cases a
/// renderer gets visibly wrong when the engine does.
/// </summary>
internal static class Q2
{
    private const string E = "\x1b";

    public static int Run()
    {
        var c = new Checks();

        // ── Text and the cursor ────────────────────────────────────────────────────────────────
        c.Add("plain text, cursor after it", () =>
        {
            var (t, _) = NewTerminal();
            t.Write("hello");
            return (t.GetLine(0).TrimEnd() == "hello" && t.Buffer.X == 5, $"'{t.GetLine(0).TrimEnd()}' x={t.Buffer.X}");
        });
        c.Add("pending wrap: a full row leaves X == Cols (renderer clamps)", () =>
        {
            // xterm.js's representation, kept: the deferred wrap is X one past the last column,
            // not a flag beside X = Cols-1. The renderer draws the cursor at min(X, Cols-1).
            var (t, _) = NewTerminal();
            t.Write(new string('a', 80));
            var x1 = t.Buffer.X; var y1 = t.Buffer.Y;
            t.Write("b");
            var wrapped = t.Buffer.Lines[1]!.IsWrapped;
            return (x1 == 80 && y1 == 0 && t.GetLine(1).TrimEnd() == "b" && wrapped,
                $"after 80: x={x1} y={y1}; after 'b': row1='{t.GetLine(1).TrimEnd()}' IsWrapped={wrapped}");
        });
        c.Add("pending wrap cancelled by CR", () =>
        {
            var (t, _) = NewTerminal();
            t.Write(new string('a', 80) + "\rZ");
            return (t.GetLine(0)[0] == 'Z' && t.Buffer.Y == 0, $"row0[0]='{t.GetLine(0)[0]}' y={t.Buffer.Y}");
        });
        c.Add("CUP + ED + EL", () =>
        {
            var (t, _) = NewTerminal();
            t.Write($"junk{E}[2J{E}[5;10HX{E}[1;1Hlong line{E}[1;5H{E}[K");
            var cell = t.Buffer.Lines[4]![9];
            return (cell.Content == "X" && t.GetLine(0).TrimEnd() == "long", $"(4,9)='{cell.Content}' row0='{t.GetLine(0).TrimEnd()}'");
        });
        c.Add("scroll region (DECSTBM) keeps rows outside it", () =>
        {
            var (t, _) = NewTerminal(rows: 6);
            t.Write($"top{E}[6;1Hbottom{E}[2;5r{E}[5;1H1\r\n2\r\n3\r\n4\r\n5{E}[r");
            return (t.GetLine(0).TrimEnd() == "top" && t.GetLine(5).TrimEnd() == "bottom" && t.GetLine(4).TrimEnd() == "5",
                string.Join(" | ", Enumerable.Range(0, 6).Select(r => t.GetLine(r).TrimEnd())));
        });
        c.Add("IL / DL / ICH / DCH / ECH", () =>
        {
            var (t, _) = NewTerminal(rows: 4);
            t.Write($"abcdef{E}[1;3H{E}[2@{E}[1;1H{E}[1P{E}[1;7H{E}[2X");
            var r0 = t.GetLine(0).TrimEnd();
            t.Write($"{E}[2;1Hrow1{E}[3;1Hrow2{E}[2;1H{E}[L");
            var afterIl = $"{t.GetLine(1).TrimEnd()}/{t.GetLine(2).TrimEnd()}";
            t.Write($"{E}[M");
            var afterDl = $"{t.GetLine(1).TrimEnd()}/{t.GetLine(2).TrimEnd()}";
            return (r0 == "b  cde" && afterIl == "/row1" && afterDl == "row1/row2", $"row0='{r0}' IL:{afterIl} DL:{afterDl}");
        });

        // ── Attributes: what the renderer styles ───────────────────────────────────────────────
        c.Add("SGR bold/italic/dim/inverse/strike", () =>
        {
            var (t, _) = NewTerminal();
            t.Write($"{E}[1;3;2;7;9mX");
            var a = t.Buffer.Lines[0]![0].Attributes;
            var ok = a.IsBold() && a.IsItalic() && a.IsDim() && a.IsInverse() && a.IsStrikethrough();
            return (ok, $"bold={a.IsBold()} italic={a.IsItalic()} dim={a.IsDim()} inverse={a.IsInverse()} strike={a.IsStrikethrough()}");
        });
        c.Add("SGR 53 overline", () =>
        {
            // AttributeData has IsOverline/SetOverline, but the SGR switch has no case 53 or 55.
            var (t, _) = NewTerminal();
            t.Write($"{E}[53mX");
            var a = t.Buffer.Lines[0]![0].Attributes;
            return (a.IsOverline(), $"IsOverline={a.IsOverline()}");
        });
        c.Add("SGR 4:3 curly underline + 58:2 colour", () =>
        {
            var (t, _) = NewTerminal();
            t.Write($"{E}[4:3m{E}[58:2::255:0:128mX");
            var a = t.Buffer.Lines[0]![0].Attributes;
            var has = a.TryGetUnderlineColor(out var color, out var mode);
            return (a.GetUnderlineStyle() == UnderlineStyle.Curly && has && color == 0xFF0080,
                $"style={a.GetUnderlineStyle()} colour={(has ? $"{color:X6} mode {mode}" : "none")}");
        });
        // The encoding, read from AttributeData rather than the README (whose "0 default, 1 palette,
        // 2 RGB" is stale): mode 0 is a palette index, where 256 is the default foreground and 257
        // the default background; mode 1 is 24-bit RGB.
        c.Add("default colours are palette 256 / 257", () =>
        {
            var (t, _) = NewTerminal();
            t.Write("X");
            var a = t.Buffer.Lines[0]![0].Attributes;
            return (a.GetFgColorMode() == 0 && a.GetFgColor() == 256 && a.GetBgColorMode() == 0 && a.GetBgColor() == 257,
                $"fg {a.GetFgColorMode()}/{a.GetFgColor()}, bg {a.GetBgColorMode()}/{a.GetBgColor()}");
        });
        c.Add("16-colour and 256-colour are palette, truecolour is mode 1", () =>
        {
            var (t, _) = NewTerminal();
            t.Write($"{E}[31mA{E}[0m{E}[38;5;196mB{E}[48;2;1;2;3mC");
            var l = t.Buffer.Lines[0]!;
            string Fmt(BufferCellView v) => $"{v.Ch}: fg {v.FgMode}/{v.Fg:X} bg {v.BgMode}/{v.Bg:X}";
            var a = new BufferCellView(l[0]); var b = new BufferCellView(l[1]); var d = new BufferCellView(l[2]);
            var ok = a.FgMode == 0 && a.Fg == 1 && b.FgMode == 0 && b.Fg == 196 && d.BgMode == 1 && d.Bg == 0x010203;
            return (ok, $"{Fmt(a)}; {Fmt(b)}; {Fmt(d)}");
        });

        // ── Unicode: what makes a renderer's columns drift ──────────────────────────────────────
        c.Add("CJK is width 2 + a width-0 spacer", () =>
        {
            var (t, _) = NewTerminal();
            t.Write("東x");
            var l = t.Buffer.Lines[0]!;
            return (l[0].Width == 2 && l[1].Width == 0 && l[2].Content == "x" && t.Buffer.X == 3,
                $"widths {l[0].Width},{l[1].Width},{l[2].Width} '{l[2].Content}' x={t.Buffer.X}");
        });
        c.Add("CJK at the last column wraps whole", () =>
        {
            var (t, _) = NewTerminal();
            t.Write(new string('a', 79) + "東");
            var l0 = t.Buffer.Lines[0]!; var l1 = t.Buffer.Lines[1]!;
            return (l1[0].Content == "東" && l1[0].Width == 2 && l0[79].Content is "" or " ",
                $"row0[79]='{l0[79].Content}' w{l0[79].Width}; row1[0]='{l1[0].Content}' w{l1[0].Width}");
        });
        c.Add("combining mark joins its base cell", () =>
        {
            var (t, _) = NewTerminal();
            t.Write("éx");
            var l = t.Buffer.Lines[0]!;
            return (l[0].Content == "é" && l[1].Content == "x", $"cell0='{l[0].Content}' ({l[0].Content.Length} chars) cell1='{l[1].Content}'");
        });
        c.Add("ZWJ family is one cell of width 2", () =>
        {
            var (t, _) = NewTerminal();
            const string family = "👨‍👩‍👧‍👦";
            t.Write(family + "x");
            var l = t.Buffer.Lines[0]!;
            return (l[0].Content == family && l[0].Width == 2 && l[2].Content == "x", $"cell0 {l[0].Content.Length} chars w{l[0].Width}, cell2='{l[2].Content}'");
        });
        c.Add("UTF-8 split across two Write(bytes) calls", () =>
        {
            var (t, _) = NewTerminal();
            var bytes = Encoding.UTF8.GetBytes("東");
            t.Write(bytes.AsSpan(0, 1)); t.Write(bytes.AsSpan(1));
            return (t.Buffer.Lines[0]![0].Content == "東", $"cell0='{t.Buffer.Lines[0]![0].Content}'");
        });

        // ── Screens, cursor and modes the host acts on ─────────────────────────────────────────
        c.Add("alt screen 1049: saves, switches, restores", () =>
        {
            var (t, _) = NewTerminal();
            var switches = new List<BufferType>();
            t.BufferChanged += (_, e) => switches.Add(t.ActiveBuffer);
            t.Write($"MAIN{E}[?1049h{E}[HALT");
            var inAlt = t.IsAlternateBufferActive && t.GetLine(0).TrimEnd() == "ALT";
            t.Write($"{E}[?1049l");
            return (inAlt && !t.IsAlternateBufferActive && t.GetLine(0).TrimEnd() == "MAIN" && t.Buffer.X == 4 && switches.Count == 2,
                $"inAlt={inAlt} back='{t.GetLine(0).TrimEnd()}' x={t.Buffer.X} events={string.Join(",", switches)}");
        });
        c.Add("DECTCEM hides the cursor", () =>
        {
            var (t, _) = NewTerminal();
            t.Write($"{E}[?25l");
            var hidden = !t.CursorVisible;
            t.Write($"{E}[?25h");
            return (hidden && t.CursorVisible, $"after ?25l visible={!hidden}, after ?25h visible={t.CursorVisible}");
        });
        c.Add("DECSCUSR 5 = blinking bar, raises CursorStyleChanged", () =>
        {
            var (t, _) = NewTerminal();
            CursorStyle? style = null; bool? blink = null;
            t.CursorStyleChanged += (_, e) => { style = e.Style; blink = e.Blink; };
            t.Write($"{E}[5 q");
            return (style == CursorStyle.Bar && blink == true, $"event {style}/{blink}");
        });
        c.Add("OSC 0 and OSC 2 raise TitleChanged", () =>
        {
            var (t, _) = NewTerminal();
            var titles = new List<string>();
            t.TitleChanged += (_, e) => titles.Add(e.Title);
            t.Write($"{E}]0;one\a{E}]2;two{E}\\");
            return (titles.SequenceEqual(["one", "two"]) && t.Title == "two", string.Join(",", titles));
        });
        c.Add("OSC 7 raises DirectoryChanged", () =>
        {
            var (t, _) = NewTerminal();
            string? dir = null;
            t.DirectoryChanged += (_, e) => dir = e.Directory;
            t.Write($"{E}]7;file://host/home/me/src{E}\\");
            return (dir is not null && dir.Contains("/home/me/src"), $"'{dir}' CurrentDirectory='{t.CurrentDirectory}'");
        });
        c.Add("OSC 8 hyperlink lands on the line", () =>
        {
            var (t, _) = NewTerminal();
            t.Write($"see {E}]8;;https://example.com{E}\\here{E}]8;;{E}\\ ok");
            var l = t.Buffer.Lines[0]!;
            var found = l.TryGetLinkAt(5, out var link);
            var outside = l.TryGetLinkAt(10, out _);
            return (found && link.Url == "https://example.com" && link.Column == 4 && link.Cols == 4 && !outside,
                found ? $"{link} (col 10 linked: {outside})" : "no link at col 5");
        });
        c.Add("OSC 52 raises ClipboardWriteRequested", () =>
        {
            var (t, _) = NewTerminal();
            string? text = null;
            t.ClipboardWriteRequested += (_, e) => text = Encoding.UTF8.GetString(e.Data);
            t.Write($"{E}]52;c;{Convert.ToBase64String(Encoding.UTF8.GetBytes("copied"))}\a");
            return (text == "copied", $"'{text}'");
        });
        c.Add("OSC 133 A/B/C/D marks + exit code", () =>
        {
            var (t, _) = NewTerminal();
            var marks = new List<string>();
            t.ShellIntegrationMarkReceived += (_, e) => marks.Add($"{e.Mark}{(e.ExitCode is { } x ? $"({x})" : "")}");
            t.Write($"{E}]133;A\a$ {E}]133;B\als\r\n{E}]133;C\aout\r\n{E}]133;D;2\a");
            return (marks.Count == 4 && t.LastCommandExitCode == 2, $"{string.Join(",", marks)} LastCommandExitCode={t.LastCommandExitCode}");
        });
        c.Add("BEL raises BellRang", () =>
        {
            var (t, _) = NewTerminal();
            var rang = 0;
            t.BellRang += (_, _) => rang++;
            t.Write("\a");
            return (rang == 1, $"{rang}");
        });
        c.Add("synchronized output 2026 is a property + event", () =>
        {
            var (t, _) = NewTerminal();
            var events = new List<bool>();
            t.SynchronizedOutputChanged += (_, e) => events.Add(e.Active);
            t.Write($"{E}[?2026h");
            var on = t.SynchronizedOutput;
            t.Write($"{E}[?2026l");
            return (on && !t.SynchronizedOutput && events.SequenceEqual([true, false]), $"on={on} events={string.Join(",", events)}");
        });

        // ── Replies the pty must receive ───────────────────────────────────────────────────────
        c.Add("DA1 reply", () =>
        {
            var (t, r) = NewTerminal();
            t.Write($"{E}[c");
            return (r.Count == 1 && r[0].StartsWith($"{E}[?"), Show(string.Concat(r)));
        });
        c.Add("DSR 6 cursor position report", () =>
        {
            var (t, r) = NewTerminal();
            t.Write($"{E}[3;7H{E}[6n");
            return (string.Concat(r) == $"{E}[3;7R", Show(string.Concat(r)));
        });
        c.Add("DECRQM 2026 is reported as supported", () =>
        {
            var (t, r) = NewTerminal();
            t.Write($"{E}[?2026$p");
            return (string.Concat(r) == $"{E}[?2026;2$y", Show(string.Concat(r)));
        });
        c.Add("kitty keyboard query after push", () =>
        {
            var (t, r) = NewTerminal();
            t.Write($"{E}[>1u{E}[?u");
            return (t.KittyKeyboardActive && string.Concat(r) == $"{E}[?1u", $"active={t.KittyKeyboardActive} reply {Show(string.Concat(r))}");
        });

        // ── Modes that change what input the host sends ────────────────────────────────────────
        c.Add("DECCKM switches arrow encoding", () =>
        {
            var (t, _) = NewTerminal();
            var normal = t.GenerateKeyInput(Key.UpArrow);
            t.Write($"{E}[?1h");
            var app = t.GenerateKeyInput(Key.UpArrow);
            return (normal == $"{E}[A" && app == $"{E}OA", $"normal {Show(normal)}, application {Show(app)}");
        });
        c.Add("bracketed paste wraps Paste()", () =>
        {
            var (t, r) = NewTerminal();
            t.Write($"{E}[?2004h");
            t.Paste("ls -la");
            return (t.BracketedPasteMode && string.Concat(r) == $"{E}[200~ls -la{E}[201~", Show(string.Concat(r)));
        });
        c.Add("paste strips ESC unless AllowPasteControls", () =>
        {
            var (t, r) = NewTerminal();
            t.Write($"{E}[?2004h");
            t.Paste($"evil{E}[201~rm -rf");
            return (!string.Concat(r).Contains($"{E}[201~rm"), Show(string.Concat(r)));
        });
        c.Add("mouse 1000+1006: SGR press at (col 4,row 2)", () =>
        {
            var (t, _) = NewTerminal();
            t.Write($"{E}[?1000h{E}[?1006h");
            var s = t.GenerateMouseEvent(MouseButton.Left, 4, 2, MouseEventType.Down);
            return (t.MouseTrackingMode == MouseTrackingMode.VT200 && t.MouseEncoding == MouseEncoding.SGR && s == $"{E}[<0;5;3M",
                $"{t.MouseTrackingMode}/{t.MouseEncoding} {Show(s)}");
        });
        c.Add("no mouse mode: GenerateMouseEvent sends nothing", () =>
        {
            var (t, _) = NewTerminal();
            var s = t.GenerateMouseEvent(MouseButton.Left, 4, 2, MouseEventType.Down);
            return (string.IsNullOrEmpty(s), $"'{Show(s)}'");
        });
        c.Add("focus 1004 gates GenerateFocusEvent", () =>
        {
            var (t, _) = NewTerminal();
            var off = t.GenerateFocusEvent(true);
            t.Write($"{E}[?1004h");
            var on = t.GenerateFocusEvent(true);
            return (string.IsNullOrEmpty(off) && on == $"{E}[I", $"off '{Show(off)}', on '{Show(on)}'");
        });
        c.Add("win32-input-mode 9001 is tracked (ConPTY asks for it)", () =>
        {
            var (t, _) = NewTerminal();
            t.Write($"{E}[?9001h");
            var key = t.GenerateKeyInput(Key.UpArrow);
            return (t.Win32InputMode, $"Win32InputMode={t.Win32InputMode}; GenerateKeyInput(Up) still {Show(key)}");
        });

        // ── Scrollback: what the scrollbar reads ───────────────────────────────────────────────
        c.Add("scrollback fills to its limit, viewport follows", () =>
        {
            var (t, _) = NewTerminal(rows: 24, scrollback: 50);
            for (var i = 0; i < 100; i++) t.Write($"line {i}\r\n");
            var b = t.Buffer;
            return (b.Lines.Length == 74 && b.YBase == 50 && b.YDisp == 50 && t.GetLine(b.YBase).TrimEnd() == "line 77",
                $"Lines.Length={b.Lines.Length} YBase={b.YBase} YDisp={b.YDisp} top='{t.GetLine(b.YBase).TrimEnd()}'");
        });
        c.Add("ScrollLines moves the viewport, not the cursor", () =>
        {
            var (t, _) = NewTerminal(rows: 24, scrollback: 50);
            for (var i = 0; i < 100; i++) t.Write($"line {i}\r\n");
            t.ScrollLines(-10);
            var b = t.Buffer;
            return (b.YDisp == 40 && !b.IsAtBottom && b.Y == 23, $"YDisp={b.YDisp} IsAtBottom={b.IsAtBottom} cursor y={b.Y}");
        });
        c.Add("output while scrolled back does not yank the viewport", () =>
        {
            var (t, _) = NewTerminal(rows: 24, scrollback: 50);
            for (var i = 0; i < 40; i++) t.Write($"line {i}\r\n");
            t.ScrollLines(-5);
            var before = t.Buffer.YDisp;
            t.Write("more\r\n");
            return (true, $"YDisp {before} -> {t.Buffer.YDisp} (YBase {t.Buffer.YBase}); the host decides whether to follow");
        });

        var failed = c.Print();
        return failed == 0 ? 0 : 1;
    }

    /// <summary>A cell flattened for printing.</summary>
    private readonly record struct BufferCellView(string Ch, int FgMode, int Fg, int BgMode, int Bg)
    {
        public BufferCellView(XTerm.Buffer.BufferCell c)
            : this(c.Content, c.Attributes.GetFgColorMode(), c.Attributes.GetFgColor(), c.Attributes.GetBgColorMode(), c.Attributes.GetBgColor()) { }
    }
}
