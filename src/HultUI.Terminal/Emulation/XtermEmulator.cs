using System.Text;
using HultUI.Core.Primitives;
using XTerm.Buffer;
using XTerm.Common;
using XTerm.Input;
using XTerm.Options;
using XCursorStyle = XTerm.Common.CursorStyle;
using XTerminal = XTerm.Terminal;
using XOptions = XTerm.Options.TerminalOptions;

namespace HultUI.Terminal.Emulation;

/// <summary>
/// The emulator seam over XTerm.NET, HultUI's patched build (external/XTerm.NET). Reflows the
/// cursor's own line (patch 0004), which a terminal behind ConPTY needs.
/// </summary>
internal sealed class XtermEmulator : ITerminalEmulator
{
    private readonly XTerminal _term;
    private readonly TerminalBuffer _normal;
    private long _linesDropped;
    private long _generation;
    private int? _pendingScrollback;
    private double _cellPixelWidth, _cellPixelHeight;

    public XtermEmulator(int columns, int rows, int scrollback, TerminalTheme theme, string termName,
        bool images = true, long maxImageBytes = TerminalOptions.DefaultMaxImageBytes)
    {
        if (theme.Ansi.Count != 16)
            throw new ArgumentException($"A theme has 16 ANSI colours; this one has {theme.Ansi.Count}.", nameof(theme));

        _term = new XTerminal(new XOptions
        {
            Cols = Math.Max(1, columns),
            Rows = Math.Max(1, rows),
            Scrollback = scrollback,
            ReflowCursorLine = true,
            TermName = termName,
            Theme = ThemeOf(theme),
            // The size reports a program reads to fit images or a layout to the grid. Answered
            // from the grid, not the element: the strip below the last row belongs to no row.
            WindowOptions = new WindowOptions { GetWinSizePixels = true, GetCellSizePixels = true, GetWinSizeChars = true },
            // Pictures: off, the terminal says it has none (no Sixel in DA1), and drops them.
            SixelEnabled = images,
            KittyGraphicsEnabled = images,
            ITerm2ImagesEnabled = images,
            MaxImageBytes = maxImageBytes,
        });
        // The buffers are made once, in the constructor; RIS clears them in place. Held here so
        // the scrollback can be changed while the alternate screen is up, and its trims counted.
        _normal = _term.Buffer;
        _normal.Trimmed += n => _linesDropped += n;

        _term.DataReceived += (_, e) => Raise(new TerminalEvent.Reply(e.Data));
        _term.TitleChanged += (_, e) => Raise(new TerminalEvent.Title(e.Title));
        _term.BellRang += (_, _) => Raise(new TerminalEvent.Bell());
        _term.DirectoryChanged += (_, e) => Raise(new TerminalEvent.Directory(e.Directory));
        _term.ClipboardWriteRequested += (_, e) =>
        {
            if (e.MimeType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase))
                Raise(new TerminalEvent.ClipboardWrite(Encoding.UTF8.GetString(e.Data)));
        };
        _term.WindowInfoRequested += (_, e) =>
        {
            switch (e.Request)
            {
                case WindowInfoRequest.SizePixels:
                    e.WidthPixels = (int)Math.Round(_term.Cols * _cellPixelWidth);
                    e.HeightPixels = (int)Math.Round(_term.Rows * _cellPixelHeight);
                    e.Handled = true;
                    break;
                case WindowInfoRequest.CellSizePixels:
                    e.CellWidth = (int)Math.Round(_cellPixelWidth);
                    e.CellHeight = (int)Math.Round(_cellPixelHeight);
                    e.Handled = true;
                    break;
            }
        };
        _term.BufferChanged += (_, _) =>
        {
            bool alternate = _term.IsAlternateBufferActive;
            if (!alternate && _pendingScrollback is { } lines)
            {
                _pendingScrollback = null;
                _normal.SetScrollback(lines);
            }
            Raise(new TerminalEvent.ScreenSwitched(alternate));
        };
    }

    public event Action<TerminalEvent>? Event;

    public int Columns => _term.Cols;
    public int Rows => _term.Rows;

    private void Raise(TerminalEvent e) => Event?.Invoke(e);

    public void Feed(ReadOnlySpan<byte> output)
    {
        _term.Write(output);
        _generation++;
    }

    public void Resize(int columns, int rows)
    {
        _term.Resize(Math.Max(1, columns), Math.Max(1, rows));
        _generation++;
    }

    public void SetScrollback(int lines)
    {
        if (_term.IsAlternateBufferActive) { _pendingScrollback = lines; return; }
        _pendingScrollback = null;
        _normal.SetScrollback(lines);
        _generation++;
    }

    public void SetCellPixels(double width, double height, double scale)
    {
        _cellPixelWidth = width;
        _cellPixelHeight = height;
        // What pictures are laid out against: how many cells an image of a given size covers.
        // Whole pixels, as the engine counts them; left at its default it was 10 x 20.
        var options = _term.Options;
        options.CellWidthPixels = Math.Max(1, (int)Math.Round(width));
        options.CellHeightPixels = Math.Max(1, (int)Math.Round(height));
        options.DisplayScale = scale > 0 ? scale : 1;
    }

    public bool AdvanceAnimations(TimeSpan delta)
    {
        if (!_term.AdvanceAnimations(delta)) return false;
        _generation++;
        return true;
    }

    public void ScrollViewport(int lines)
    {
        if (lines == 0) return;
        _term.ScrollLines(lines);
        _generation++;
    }

    public void ScrollToBottom()
    {
        _term.ScrollToBottom();
        _generation++;
    }

    public void Paste(string text) => _term.Paste(text);

    public string? EncodeFocus(bool focused) => NullIfEmpty(_term.GenerateFocusEvent(focused));

    public string? EncodeKey(in TerminalKey key, bool win32Input) => TerminalKeys.Encode(_term, key, win32Input);

    public string? EncodeMouse(in TerminalMouse mouse)
    {
        if (_term.MouseTrackingMode == MouseTrackingMode.None) return null;
        var (button, type) = mouse.Action switch
        {
            TerminalMouseAction.WheelUp => (MouseButton.WheelUp, MouseEventType.WheelUp),
            TerminalMouseAction.WheelDown => (MouseButton.WheelDown, MouseEventType.WheelDown),
            TerminalMouseAction.Down => (ButtonOf(mouse.Button), MouseEventType.Down),
            TerminalMouseAction.Up => (ButtonOf(mouse.Button), MouseEventType.Up),
            _ => (ButtonOf(mouse.Button), mouse.Button == TerminalMouseButton.None ? MouseEventType.Move : MouseEventType.Drag),
        };
        return NullIfEmpty(_term.GenerateMouseEvent(button, mouse.Column, mouse.Row, type, TerminalKeys.ModifiersOf(mouse.Modifiers)));
    }

    private static MouseButton ButtonOf(TerminalMouseButton b) => b switch
    {
        TerminalMouseButton.Left => MouseButton.Left,
        TerminalMouseButton.Middle => MouseButton.Middle,
        TerminalMouseButton.Right => MouseButton.Right,
        _ => MouseButton.None,
    };

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    // ──── The snapshot ────

    public void CopySnapshot(TerminalSnapshot s)
    {
        var buffer = _term.Buffer;
        int cols = _term.Cols, rows = _term.Rows;
        s.Resize(cols, rows);

        s.Links.Clear();
        s.Pictures.Clear();
        var colors = _term.Colors.Take();
        int defaultFg = colors.Foreground, defaultBg = colors.Background;
        s.DefaultForeground = defaultFg;
        s.DefaultBackground = defaultBg;
        s.CursorColor = colors.Cursor;

        for (int r = 0; r < rows; r++)
        {
            var line = buffer.Lines[buffer.YDisp + r];
            var row = s.Cells.AsSpan(r * cols, cols);
            if (line is null)
            {
                row.Clear();
                for (int c = 0; c < cols; c++) { row[c].Width = 1; row[c].Flags = CellFlags.DefaultBackground; row[c].Foreground = defaultFg; row[c].UnderlineColor = -1; }
                continue;
            }

            int n = Math.Min(cols, line.Length);
            bool hasLinks = line.HasLinks;
            if (hasLinks)
                foreach (var link in line.Links)
                    if (link.Column < cols)
                        s.Links.Add(new LinkSpan(r, link.Column, Math.Min(link.EndColumn, cols), link.Url, link.Id));
            if (line.HasImages) CopyPictures(line, r, s.Pictures);
            for (int c = 0; c < n; c++)
            {
                var cell = line[c];
                ref var o = ref row[c];
                Resolve(cell, colors, defaultFg, defaultBg, ref o);
                if (hasLinks && line.TryGetLinkAt(c, out _)) o.Flags |= CellFlags.Link;
            }
            for (int c = n; c < cols; c++)
            {
                row[c] = default;
                row[c].Width = 1;
                row[c].Flags = CellFlags.DefaultBackground;
                row[c].Foreground = defaultFg;
                row[c].UnderlineColor = -1;
            }
        }

        // The cursor: the engine's deferred wrap puts X one past the last column; it is drawn on it.
        int cursorAbs = buffer.YBase + buffer.Y;
        int cursorRow = cursorAbs - buffer.YDisp;
        s.CursorColumn = Math.Clamp(buffer.X, 0, Math.Max(0, cols - 1));
        s.CursorRow = cursorRow;
        s.CursorVisible = _term.CursorVisible && cursorRow >= 0 && cursorRow < rows;
        s.CursorShape = _term.Options.CursorStyle switch
        {
            XCursorStyle.Bar => CursorShape.Bar,
            XCursorStyle.Underline => CursorShape.Underline,
            _ => CursorShape.Block,
        };
        s.CursorBlink = _term.Options.CursorBlink;

        bool alternate = _term.IsAlternateBufferActive;
        s.ScrollbackLines = buffer.YBase;
        s.NormalLines = _normal.Lines.Length;
        s.ViewportTop = buffer.YDisp;
        s.AtBottom = buffer.IsAtBottom;
        s.LinesDropped = alternate ? 0 : _linesDropped;

        var modes = TerminalModes.None;
        if (_term.BracketedPasteMode) modes |= TerminalModes.BracketedPaste;
        if (_term.SendFocusEvents) modes |= TerminalModes.FocusEvents;
        if (alternate) modes |= TerminalModes.AlternateScreen;
        if (_term.SynchronizedOutput) modes |= TerminalModes.SynchronizedOutput;
        if (_term.Win32InputMode) modes |= TerminalModes.Win32Input;
        if (_term.ApplicationCursorKeys) modes |= TerminalModes.ApplicationCursorKeys;
        if (_term.MouseTrackingMode != MouseTrackingMode.None) modes |= TerminalModes.MouseTracking;
        s.Modes = modes;
        s.Generation = _generation;
        s.PicturesAnimating = AnyAnimating(s.Pictures);
    }

    /// <summary>Whether a picture on screen animates. Not the engine's HasRunningAnimations, which
    /// counts every animation it holds: one transmitted and never shown, or cleared off the screen
    /// while still running, kept the element drawing four times a second for nothing. One off
    /// screen waits, and goes on when it is shown again.</summary>
    private static bool AnyAnimating(List<PictureRun> pictures)
    {
        foreach (var run in pictures)
            if (run.Image is XTerm.Graphics.TerminalImage { Animation: { FrameCount: > 1, State: not XTerm.Graphics.AnimationState.Stopped } })
                return true;
        return false;
    }

    private static void CopyPictures(BufferLine line, int row, List<PictureRun> into)
    {
        var images = line.Images;
        foreach (var p in line.Placements)
        {
            XTerm.Graphics.TerminalImage? image = null;
            for (int i = 0; i < images.Count && image is null; i++)
                if (images[i].Id == p.ImageId) image = images[i];
            if (image is null || p.Cols <= 0 || p.SrcWidth <= 0 || p.SrcHeight <= 0) continue;
            into.Add(new PictureRun(row, p.Column, p.Cols,
                image, image.PixelWidth, image.PixelHeight, image.CellWidth, image.CellHeight, image.FrameSerial, image.CurrentPixels,
                p.SrcX, p.SrcY, p.SrcWidth, p.SrcHeight, p.PxPerCellX, p.PxPerCellY, p.OffsetX, p.OffsetY, p.ZIndex));
        }
    }

    /// <summary>
    /// One cell, resolved. XTerm.NET's encoding (its README has it wrong): colour mode 0 is a
    /// palette index where 256 and 257 are the default foreground and background; mode 1 is RGB.
    /// </summary>
    private static void Resolve(BufferCell cell, ColorSnapshot colors, int defaultFg, int defaultBg, ref TerminalCell o)
    {
        var a = cell.Attributes;
        bool bold = a.IsBold();

        int fgMode = a.GetFgColorMode(), fg = a.GetFgColor();
        int fgRgb = fgMode == 1 ? fg
            : fg == 256 ? defaultFg
            : colors[bold && fg < 8 ? fg + 8 : fg];   // bold as bright, as xterm and most terminals do

        int bgMode = a.GetBgColorMode(), bg = a.GetBgColor();
        bool defaultBackground = bgMode == 0 && bg == 257;
        int bgRgb = bgMode == 1 ? bg : bg is 257 or 256 ? defaultBg : colors[bg];

        var flags = CellFlags.None;
        if (bold) flags |= CellFlags.Bold;
        if (a.IsDim()) flags |= CellFlags.Dim;
        if (a.IsItalic()) flags |= CellFlags.Italic;
        if (a.IsStrikethrough()) flags |= CellFlags.Strikethrough;
        if (a.IsOverline()) flags |= CellFlags.Overline;
        if (a.IsBlink()) flags |= CellFlags.Blink;

        if (a.IsInverse())
        {
            (fgRgb, bgRgb) = (bgRgb, fgRgb);
            defaultBackground = false;
        }
        if (defaultBackground) flags |= CellFlags.DefaultBackground;

        o.Width = (byte)Math.Clamp(cell.Width, 0, 255);
        o.Cluster = cell.ClusterId != 0 ? cell.Content : null;
        o.CodePoint = a.IsInvisible() ? 0 : cell.CodePoint;
        if (a.IsInvisible()) o.Cluster = null;
        o.Foreground = fgRgb;
        o.Background = bgRgb;
        o.Flags = flags;
        o.Underline = a.GetUnderlineStyle() switch
        {
            UnderlineStyle.Single => TerminalUnderline.Single,
            UnderlineStyle.Double => TerminalUnderline.Double,
            UnderlineStyle.Curly => TerminalUnderline.Curly,
            UnderlineStyle.Dotted => TerminalUnderline.Dotted,
            UnderlineStyle.Dashed => TerminalUnderline.Dashed,
            _ => TerminalUnderline.None,
        };
        o.UnderlineColor = a.TryGetUnderlineColor(out int uc, out int um)
            ? um == 1 ? uc : colors[Math.Clamp(uc, 0, 255)]
            : -1;
    }

    // ──── Text ────

    private long Dropped => _term.IsAlternateBufferActive ? 0 : _linesDropped;

    private BufferLine? LineAt(long line)
    {
        long index = line - Dropped;
        var lines = _term.Buffer.Lines;
        return index >= 0 && index < lines.Length ? lines[(int)index] : null;
    }

    public string GetText(TerminalPosition start, TerminalPosition end)
    {
        if (end <= start) return "";
        var lines = _term.Buffer.Lines;
        long dropped = Dropped;
        long first = Math.Max(start.Line, dropped), last = Math.Min(end.Line, dropped + lines.Length - 1);
        var sb = new StringBuilder();
        for (long l = first; l <= last; l++)
        {
            var line = lines[(int)(l - dropped)];
            if (line is null) continue;
            int from = l == start.Line ? Math.Clamp(start.Column, 0, line.Length) : 0;
            int to = l == end.Line ? Math.Clamp(end.Column, 0, line.Length) : line.Length;
            bool continues = l < last && lines[(int)(l - dropped + 1)]?.IsWrapped == true;
            if (to > from) sb.Append(line.TranslateToString(trimRight: !continues, from, to));
            if (l < last && !continues) sb.Append('\n');
        }
        return sb.ToString();
    }

    public (TerminalPosition Start, TerminalPosition End) WordAt(TerminalPosition at)
    {
        var line = LineAt(at.Line);
        if (line is null) return (at, at);
        int col = Math.Clamp(at.Column, 0, Math.Max(0, line.Length - 1));
        bool word = IsWordCell(line, col);
        int start = col, end = col + 1;
        while (start > 0 && IsWordCell(line, start - 1) == word) start--;
        while (end < line.Length && IsWordCell(line, end) == word) end++;
        return (at with { Column = start }, at with { Column = end });
    }

    public (TerminalPosition Start, TerminalPosition End) LineAt(TerminalPosition at)
    {
        var lines = _term.Buffer.Lines;
        long dropped = Dropped;
        long first = at.Line, last = at.Line;
        while (first > dropped && lines[(int)(first - dropped)]?.IsWrapped == true) first--;
        while (last + 1 - dropped < lines.Length && lines[(int)(last + 1 - dropped)]?.IsWrapped == true) last++;
        int width = LineAt(last)?.Length ?? 0;
        return (new TerminalPosition(first, 0), new TerminalPosition(last, width));
    }

    public long Generation => _generation;

    public List<LogicalLine> ReadLogicalLines(long fromLine = 0)
    {
        var result = new List<LogicalLine>();
        var lines = _term.Buffer.Lines;
        int cols = _term.Cols;
        long dropped = Dropped, first = 0;
        var text = new StringBuilder();
        var starts = new List<int>();
        Span<char> pair = stackalloc char[2];

        // From the start of the logical line holding fromLine.
        int from = (int)Math.Clamp(fromLine - dropped, 0, lines.Length);
        while (from > 0 && from < lines.Length && lines[from]?.IsWrapped == true) from--;

        for (int i = from; i < lines.Length; i++)
        {
            var line = lines[i];
            if (starts.Count > 0 && line?.IsWrapped != true) Flush();
            if (starts.Count == 0) first = dropped + i;
            starts.Add(text.Length);
            if (line is null) continue;
            for (int c = 0, n = Math.Min(cols, line.Length); c < n; c++)
            {
                var cell = line[c];
                if (cell.Width == 0) continue;
                int cp = cell.CodePoint;
                if (cell.ClusterId == 0 && cp is > 0 and < 0xD800 or (> 0xDFFF and < 0x10000))
                {
                    // The common case first, and without an allocation: one char.
                    text.Append(cell.Attributes.IsInvisible() ? ' ' : (char)cp);
                    continue;
                }
                if (cell.Attributes.IsInvisible() || (cell.ClusterId == 0 && cp == 0)) text.Append(' ');
                else if (cell.ClusterId != 0) text.Append(cell.Content);
                else if (Rune.IsValid(cp)) text.Append(pair[..new Rune(cp).EncodeToUtf16(pair)]);
                else text.Append('\uFFFD');
            }
        }
        if (starts.Count > 0) Flush();
        return result;

        void Flush()
        {
            int end = text.Length;
            while (end > 0 && text[end - 1] == ' ') end--;
            result.Add(new LogicalLine(first, [.. starts], text.ToString(0, end)));
            text.Clear();
            starts.Clear();
        }
    }

    // Windows Terminal's default delimiters, plus box drawing: what a double click should stop at.
    private static bool IsWordCell(BufferLine line, int col)
    {
        var cell = line[col];
        if (cell.Width == 0 && col > 0) return IsWordCell(line, col - 1);
        int cp = cell.CodePoint;
        if (cp is 0 or ' ' or '\t') return false;
        if (cp is '(' or ')' or '[' or ']' or '{' or '}' or '<' or '>' or '\'' or '"' or '`' or ',' or ';' or '|' or '│') return false;
        return !(cp >= 0x2500 && cp <= 0x259F);
    }

    // ──── The theme ────

    private static ThemeOptions ThemeOf(TerminalTheme t) => new()
    {
        Foreground = Hex(t.Foreground),
        Background = Hex(t.Background),
        Cursor = Hex(t.Cursor),
        Black = Hex(t.Ansi[0]), Red = Hex(t.Ansi[1]), Green = Hex(t.Ansi[2]), Yellow = Hex(t.Ansi[3]),
        Blue = Hex(t.Ansi[4]), Magenta = Hex(t.Ansi[5]), Cyan = Hex(t.Ansi[6]), White = Hex(t.Ansi[7]),
        BrightBlack = Hex(t.Ansi[8]), BrightRed = Hex(t.Ansi[9]), BrightGreen = Hex(t.Ansi[10]), BrightYellow = Hex(t.Ansi[11]),
        BrightBlue = Hex(t.Ansi[12]), BrightMagenta = Hex(t.Ansi[13]), BrightCyan = Hex(t.Ansi[14]), BrightWhite = Hex(t.Ansi[15]),
    };

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public void Dispose() => _term.Dispose();
}
