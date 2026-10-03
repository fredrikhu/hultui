using System.Text;
using HultUI.Core.Animation;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Terminal.Emulation;
using HultUI.Terminal.Rendering;

namespace HultUI.Terminal;

/// <summary>
/// Drawing. Each frame copies the screen out of the emulator under the controller's lock (12.6 µs
/// for 200 × 60 in the spike) and draws from the copy with the lock released.
/// </summary>
/// <remarks>
/// <para><b>Cells.</b> The cell is as wide as the font's character width and as tall as its line
/// (<see cref="IRenderContext.MeasureFont"/>), the height rounded up to whole device pixels so rows
/// land on pixel edges. Columns and rows are the content box divided by the cell, truncated; the
/// strip left over belongs to no cell.</para>
/// <para><b>Text.</b> Each row is one <c>DrawText</c> with a span per change of style, so its
/// glyphs share a baseline and step by the font's own advance, which is the cell. A character
/// that would not step by it is drawn on its own at its column, with a space in its place in the
/// row: a wide one, a cluster, and one the face has no glyph for
/// (<see cref="IRenderContext.HasGlyph"/>), whose fallback glyph's advance would push the rest of
/// the row off its columns. (Its baseline is its own font's, which can sit up to about a pixel off
/// the row's: seam T2 in docs/design/TERMINAL.md.) Right-to-left letters are drawn on their own
/// too, so they are never reordered. Box drawing and block elements are geometry.</para>
/// <para><b>Synchronized output.</b> While a program holds an update open (DECSET 2026) the last
/// complete screen stays up, for at most 150 ms.</para>
/// </remarks>
public sealed partial class TerminalElement
{
    private static readonly TimeSpan SyncHoldLimit = TimeSpan.FromMilliseconds(150);

    private TerminalSnapshot _snapshot = new();
    private TerminalSnapshot _incoming = new();

    // The hold's clock is a tween, so it runs on the frame loop's time (virtual under a headless
    // session) and wakes a frame when it runs out.
    private TweenHandle _syncHold;
    private bool _syncHolding, _syncExpired;

    private float _cellWidth = 8, _cellHeight = 16;
    private (string Family, float Size, float Scale) _metricsKey;
    private FontMetrics _font;

    /// <summary>Whether the face has each non-ASCII code point met so far; cleared with the font.</summary>
    private readonly Dictionary<int, bool> _glyphs = [];
    private bool _resizeQueued;
    private string? _preedit;

    private readonly StringBuilder _rowText = new();
    private readonly List<TextSpan> _spans = [];
    private readonly List<int> _specials = [];

    /// <summary>The cell's size in layout units, as the last frame measured it.</summary>
    internal (float Width, float Height) CellSize => (_cellWidth, _cellHeight);

    public override void Render(IRenderContext ctx)
    {
        StartListening();
        var controller = _controller;
        if (controller is null) return;

        var box = Layout.AbsoluteContentBox;
        if (box.Width <= 0 || box.Height <= 0) return;
        float scale = OverlayHost is Window window ? window.DisplayScale : 1f;
        var style = BaseStyle(controller);

        UpdateMetrics(ctx, style, scale);
        RequestGrid(controller, box);
        TakeSnapshot(controller);
        var s = _snapshot;

        ctx.PushClip(box);
        ctx.DrawRect(box, Rgb(s.DefaultBackground));
        var selection = _selection?.Ordered;
        var selectionColor = controller.Options.Theme.Selection;

        for (int row = 0; row < s.Rows; row++)
        {
            float y = box.Y + row * _cellHeight;
            if (y >= box.Y + box.Height) break;
            DrawBackgrounds(ctx, s, row, box.X, y, scale);
            if (selection is { } sel) DrawSelection(ctx, s, row, sel, selectionColor, box.X, y, scale);
            DrawRowText(ctx, s, row, box.X, y, style, scale);
        }

        DrawCursor(ctx, s, box, style, scale);
        DrawPreedit(ctx, s, box, style);
        ctx.Pop();

        ApplyInputArea(s);
    }

    private static ResolvedInheritedStyle BaseStyle(TerminalController c) => new(
        FontFamily: c.Options.ResolvedFontFamily,
        FontSize: c.Options.FontSize,
        FontWeight: FontWeight.Regular,
        TextColor: Color.White,
        TextDirection: TextDirection.Ltr,
        Cursor: CursorType.Text,
        TextAlign: TextAlign.Left);

    private void UpdateMetrics(IRenderContext ctx, ResolvedInheritedStyle style, float scale)
    {
        var key = (style.FontFamily, style.FontSize, scale);
        if (_metricsKey == key) return;
        _metricsKey = key;
        _glyphs.Clear();

        _font = ctx.MeasureFont(style);
        _cellWidth = _font.CharacterWidth > 0 ? _font.CharacterWidth : style.FontSize * 0.6f;
        float height = _font.LineHeight > 0 ? _font.LineHeight : style.FontSize * 1.2f;
        _cellHeight = MathF.Ceiling(height * scale) / scale;
    }

    /// <summary>The face the terminal draws with, as the last frame measured it.</summary>
    internal FontMetrics Font => _font;

    /// <summary>The grid the content box holds. A change is applied after the frame, never inside
    /// it: the resize reaches the program, and ConPTY answers it synchronously.</summary>
    private void RequestGrid(TerminalController controller, Rect box)
    {
        int columns = Math.Max(1, (int)(box.Width / _cellWidth));
        int rows = Math.Max(1, (int)(box.Height / _cellHeight));
        if ((columns, rows) == controller.GridSize || _resizeQueued) return;
        _resizeQueued = true;
        Application.InvokeOnMainThread(FlushGrid);
    }

    private void FlushGrid()
    {
        _resizeQueued = false;
        if (_controller is null || !IsHosted) return;
        var box = Layout.AbsoluteContentBox;
        if (box.Width <= 0 || box.Height <= 0) return;
        _controller.Resize(Math.Max(1, (int)(box.Width / _cellWidth)), Math.Max(1, (int)(box.Height / _cellHeight)));
    }

    private void TakeSnapshot(TerminalController controller)
    {
        // A disposed controller has nothing new: its last screen stays up.
        if (!controller.CopySnapshot(_incoming)) return;

        bool syncing = (_incoming.Modes & TerminalModes.SynchronizedOutput) != 0;
        if (syncing && !_syncExpired && _snapshot.Rows > 0)
        {
            if (!_syncHolding)
            {
                _syncHolding = true;
                _syncHold = Animate.Value(0f, 1f, static _ => { })
                    .Duration((float)SyncHoldLimit.TotalSeconds)
                    .OnComplete(OnSyncLimit)
                    .Start();
            }
            return;
        }

        if (!syncing)
        {
            // The update is complete; the next one gets a hold of its own.
            _syncExpired = false;
            StopSyncHold();
        }
        (_snapshot, _incoming) = (_incoming, _snapshot);
    }

    /// <summary>The program held an update open too long: show the screen as it is, until it
    /// closes the update.</summary>
    private void OnSyncLimit()
    {
        _syncHolding = false;
        _syncExpired = true;
        if (IsHosted) MarkPaintDirty();
    }

    private void StopSyncHold()
    {
        if (!_syncHolding) return;
        _syncHold.Cancel();
        _syncHolding = false;
    }

    // ──── Backgrounds and selection ────

    private void DrawBackgrounds(IRenderContext ctx, TerminalSnapshot s, int row, float left, float y, float scale)
    {
        var cells = s.Row(row);
        int start = -1, color = 0;
        for (int col = 0; col <= cells.Length; col++)
        {
            bool has = col < cells.Length && (cells[col].Flags & CellFlags.DefaultBackground) == 0;
            int c = has ? cells[col].Background : 0;
            if (start >= 0 && (!has || c != color))
            {
                ctx.DrawRect(Span(left, y, start, col, scale), Rgb(color));
                start = -1;
            }
            if (has && start < 0) { start = col; color = c; }
        }
    }

    private void DrawSelection(IRenderContext ctx, TerminalSnapshot s, int row,
        (TerminalPosition Start, TerminalPosition End) sel, Color color, float left, float y, float scale)
    {
        long line = s.LinesDropped + s.ViewportTop + row;
        if (line < sel.Start.Line || line > sel.End.Line) return;
        int from = line == sel.Start.Line ? sel.Start.Column : 0;
        int to = line == sel.End.Line ? sel.End.Column : s.Columns;
        from = Math.Clamp(from, 0, s.Columns);
        to = Math.Clamp(to, 0, s.Columns);
        if (to > from) ctx.DrawRect(Span(left, y, from, to, scale), color);
    }

    /// <summary>Columns [from, to) of a row, its edges snapped to device pixels so neighbouring
    /// rectangles meet without a seam.</summary>
    private Rect Span(float left, float y, int from, int to, float scale)
    {
        float x0 = Snap(left + from * _cellWidth, scale), x1 = Snap(left + to * _cellWidth, scale);
        return new Rect(x0, y, x1 - x0, _cellHeight);
    }

    private static float Snap(float v, float scale) => MathF.Round(v * scale) / scale;

    // ──── Text ────

    private void DrawRowText(IRenderContext ctx, TerminalSnapshot s, int row, float left, float y,
        ResolvedInheritedStyle style, float scale)
    {
        var cells = s.Row(row);
        _rowText.Clear();
        _spans.Clear();
        _specials.Clear();

        int spanStart = 0;
        (int Fg, CellFlags Flags, bool Underline)? current = null;
        int lastInk = -1;

        for (int col = 0; col < cells.Length; col++)
        {
            ref readonly var cell = ref cells[col];
            if (cell.Width == 0) continue; // covered by the wide character before it

            var key = (cell.Foreground, cell.Flags & (CellFlags.Bold | CellFlags.Dim | CellFlags.Italic | CellFlags.Strikethrough),
                cell.Underline != TerminalUnderline.None);
            if (current != key)
            {
                if (current is { } prev && _rowText.Length > spanStart)
                    _spans.Add(SpanOf(prev, spanStart, _rowText.Length - spanStart));
                spanStart = _rowText.Length;
                current = key;
            }

            if (IsSpecial(ctx, style, cell))
            {
                _specials.Add(col);
                _rowText.Append(' ', Math.Max(1, (int)cell.Width));
                continue;
            }

            if (cell.IsBlank) _rowText.Append(' ');
            else
            {
                AppendCodePoint(_rowText, cell.CodePoint);
                lastInk = _rowText.Length;
            }
            if (cell.Underline != TerminalUnderline.None || (cell.Flags & CellFlags.Strikethrough) != 0)
                lastInk = _rowText.Length;
        }
        if (current is { } last && _rowText.Length > spanStart)
            _spans.Add(SpanOf(last, spanStart, _rowText.Length - spanStart));

        if (lastInk > 0)
        {
            var text = _rowText.ToString(0, lastInk);
            var bounds = new Rect(left, y, (s.Columns + 1) * _cellWidth, _cellHeight);
            ctx.DrawText(text, bounds, style, TextOverflow.Clip, 1, TrimSpans(lastInk));
        }

        foreach (int col in _specials)
            DrawSpecial(ctx, cells[col], left + col * _cellWidth, y, style, scale);

        // Overline has no span decoration: a line along the top of each cell that asks for one.
        for (int col = 0; col < cells.Length; col++)
            if ((cells[col].Flags & CellFlags.Overline) != 0)
            {
                var r = Span(left, y, col, col + Math.Max(1, (int)cells[col].Width), scale);
                float thickness = MathF.Max(1f, MathF.Round(_font.UnderlineThickness * scale)) / scale;
                ctx.DrawRect(new Rect(r.X, y, r.Width, thickness), Rgb(cells[col].Foreground));
            }
    }

    /// <summary>A code point as UTF-16: one char, or a surrogate pair above the BMP; U+FFFD for a
    /// value that is not a scalar (a lone surrogate would throw, or reach the shaper broken).</summary>
    private static void AppendCodePoint(StringBuilder sb, int cp)
    {
        if (cp is < 0xD800 or (> 0xDFFF and < 0x10000)) { sb.Append((char)cp); return; }
        var rune = Rune.IsValid(cp) ? new Rune(cp) : Rune.ReplacementChar;
        Span<char> utf16 = stackalloc char[2];
        sb.Append(utf16[..rune.EncodeToUtf16(utf16)]);
    }

    private List<TextSpan> TrimSpans(int length)
    {
        for (int i = _spans.Count - 1; i >= 0; i--)
        {
            var span = _spans[i];
            if (span.Start >= length) _spans.RemoveAt(i);
            else if (span.End > length) _spans[i] = span with { Length = length - span.Start };
        }
        return _spans;
    }

    private static TextSpan SpanOf((int Fg, CellFlags Flags, bool Underline) k, int start, int length)
    {
        var color = Rgb(k.Fg, (k.Flags & CellFlags.Dim) != 0 ? (byte)128 : (byte)255);
        var decoration = (k.Underline ? TextDecoration.Underline : TextDecoration.None)
            | ((k.Flags & CellFlags.Strikethrough) != 0 ? TextDecoration.LineThrough : TextDecoration.None);
        return new TextSpan(start, length, color, Decoration: decoration,
            Weight: (k.Flags & CellFlags.Bold) != 0 ? FontWeight.Bold : null,
            Slant: (k.Flags & CellFlags.Italic) != 0 ? FontSlant.Italic : null);
    }

    /// <summary>
    /// A cell drawn on its own rather than in its row's text: a wide character, a cluster, box
    /// drawing (geometry), a right-to-left letter (never reordered), and a character the face has
    /// no glyph for. Text would draw that from a fallback font, whose advance need not be the
    /// cell's: Consolas and Cascadia Mono lack some arrows and maths operators, and a row of them
    /// drifted half a cell before this asked the font.
    /// </summary>
    private bool IsSpecial(IRenderContext ctx, ResolvedInheritedStyle style, in TerminalCell cell)
    {
        if (cell.Cluster is not null || cell.Width > 1) return true;
        int cp = cell.CodePoint;
        if (cp < 0x80) return false;
        if (BoxDrawing.Handles(cp) || IsRightToLeft(cp)) return true;
        if (!_glyphs.TryGetValue(cp, out bool has))
            _glyphs[cp] = has = ctx.HasGlyph(style, cp);
        return !has;
    }

    /// <summary>The blocks of the strongly right-to-left scripts: Hebrew, Arabic, Syriac, Thaana,
    /// N'Ko and their presentation forms, and the historic and Arabic blocks above the BMP.</summary>
    private static bool IsRightToLeft(int cp) =>
        cp is >= 0x0590 and <= 0x08FF
            or >= 0xFB1D and <= 0xFDFF
            or >= 0xFE70 and <= 0xFEFF
            or >= 0x10800 and <= 0x10FFF
            or >= 0x1E800 and <= 0x1EFFF;

    private void DrawSpecial(IRenderContext ctx, in TerminalCell cell, float x, float y,
        ResolvedInheritedStyle style, float scale)
    {
        int width = Math.Max(1, (int)cell.Width);
        var rect = new Rect(x, y, width * _cellWidth, _cellHeight);
        var color = Rgb(cell.Foreground, (cell.Flags & CellFlags.Dim) != 0 ? (byte)128 : (byte)255);

        if (cell.Cluster is null && BoxDrawing.TryDraw(ctx, cell.CodePoint, SnapRect(rect, scale), color, scale))
            return;

        var text = cell.Cluster ?? (Rune.IsValid(cell.CodePoint) ? new Rune(cell.CodePoint) : Rune.ReplacementChar).ToString();
        var glyphStyle = style with
        {
            TextColor = color,
            FontWeight = (cell.Flags & CellFlags.Bold) != 0 ? FontWeight.Bold : FontWeight.Regular,
            FontSlant = (cell.Flags & CellFlags.Italic) != 0 ? FontSlant.Italic : FontSlant.Normal,
        };
        ctx.PushClip(rect);
        ctx.DrawText(text, rect with { Width = rect.Width + 2 * _cellWidth }, glyphStyle, TextOverflow.Clip, 1);
        ctx.Pop();
    }

    private static Rect SnapRect(Rect r, float scale)
    {
        float x0 = Snap(r.X, scale), y0 = Snap(r.Y, scale);
        float x1 = Snap(r.X + r.Width, scale), y1 = Snap(r.Y + r.Height, scale);
        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    // ──── Cursor and input method ────

    private void DrawCursor(IRenderContext ctx, TerminalSnapshot s, Rect box, ResolvedInheritedStyle style, float scale)
    {
        if (!s.CursorVisible || _preedit is not null) return;
        if (s.CursorRow < 0 || s.CursorRow >= s.Rows) return;

        ref readonly var cell = ref s.At(s.CursorColumn, s.CursorRow);
        int width = Math.Max(1, (int)cell.Width);
        float x = box.X + s.CursorColumn * _cellWidth, y = box.Y + s.CursorRow * _cellHeight;
        var rect = SnapRect(new Rect(x, y, width * _cellWidth, _cellHeight), scale);
        var color = Rgb(s.CursorColor);
        float px = 1f / scale;

        if (!IsFocused)
        {
            // Hollow while the element is not focused, whatever shape the program asked for.
            ctx.DrawRoundedRect(rect, default, Color.Transparent, color, new Thickness(px));
            return;
        }

        switch (s.CursorShape)
        {
            case CursorShape.Bar:
                ctx.DrawRect(new Rect(rect.X, rect.Y, MathF.Max(2 * px, MathF.Round(_cellWidth * scale / 8) * px), rect.Height), color);
                break;
            case CursorShape.Underline:
                float t = MathF.Max(2 * px, MathF.Round(_cellHeight * scale / 12) * px);
                ctx.DrawRect(new Rect(rect.X, rect.Y + rect.Height - t, rect.Width, t), color);
                break;
            default:
                ctx.DrawRect(rect, color);
                // The character under a block cursor, in the background colour.
                if (!cell.IsBlank && !IsSpecial(ctx, style, cell))
                {
                    var under = style with
                    {
                        TextColor = (cell.Flags & CellFlags.DefaultBackground) != 0 ? Rgb(s.DefaultBackground) : Rgb(cell.Background),
                        FontWeight = (cell.Flags & CellFlags.Bold) != 0 ? FontWeight.Bold : FontWeight.Regular,
                    };
                    var glyph = new StringBuilder(2);
                    AppendCodePoint(glyph, cell.CodePoint);
                    ctx.DrawText(glyph.ToString(), rect with { Width = rect.Width + _cellWidth }, under, TextOverflow.Clip, 1);
                }
                break;
        }
    }

    /// <summary>An input method's composition, drawn at the cursor until it commits.</summary>
    private void DrawPreedit(IRenderContext ctx, TerminalSnapshot s, Rect box, ResolvedInheritedStyle style)
    {
        if (_preedit is not { Length: > 0 } text || s.CursorRow < 0 || s.CursorRow >= s.Rows) return;
        float x = box.X + s.CursorColumn * _cellWidth, y = box.Y + s.CursorRow * _cellHeight;
        float width = MathF.Max(ctx.MeasureText(text, style).Width, _cellWidth);
        ctx.DrawRect(new Rect(x, y, width, _cellHeight), Rgb(s.DefaultBackground));
        ctx.DrawText(text, new Rect(x, y, width + _cellWidth, _cellHeight),
            style with { TextColor = Rgb(s.DefaultForeground), Decoration = TextDecoration.Underline }, TextOverflow.Clip, 1);
    }

    /// <summary>While focused, the input method's candidate window goes at the cursor.</summary>
    private void ApplyInputArea(TerminalSnapshot s)
    {
        if (!IsFocused || OverlayHost is not Window window) return;
        var box = window.ContentBoxInWindow(this);
        window.TextInputHost.SetInputArea(
            new Rect(box.X + s.CursorColumn * _cellWidth, box.Y + Math.Max(0, s.CursorRow) * _cellHeight, _cellWidth, _cellHeight), 0);
    }

    private static Color Rgb(int rgb, byte alpha = 255) =>
        new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, alpha);
}
