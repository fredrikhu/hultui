using System.Text;
using HultUI.Core.Accessibility;
using HultUI.Core.Animation;
using HultUI.Core.Primitives;
using HultUI.Terminal.Emulation;

namespace HultUI.Terminal;

/// <summary>
/// The terminal for a screen reader: a document named after the program's title, whose value is
/// the visible screen (<see cref="TerminalController.ReadScreen"/>), whose text a reader moves
/// through by character, word and line with the cursor as its caret (seam A1), and whose new
/// output is announced (seam A2).
/// </summary>
/// <remarks>
/// <para><b>When.</b> A change is told once the output has paused for <see cref="ScreenSettle"/>,
/// or after <see cref="ScreenSettleLimit"/> of unbroken output: one update per burst, not one a
/// frame. Nothing is timed while no assistive tool listens.</para>
/// <para><b>The text</b> is the screen's rows, trailing blanks dropped, down to the last row with
/// text or the cursor's. Built off the UI thread when a reader asks, from a copy of the screen,
/// and kept until the screen, the selection or the cell grid changes.</para>
/// <para><b>Announcements</b> are the output since the last one, on the normal screen: not a
/// full-screen program's redraws, which a reader reviews instead. Once the user has typed, what
/// comes back up to the first line break is the echo of it, which the reader has spoken as it was
/// typed, and is left out; a line's editing that never reaches a line break is not announced.
/// At most a screenful: a flood is announced by its end.</para>
/// </remarks>
public sealed partial class TerminalElement
{
    internal static readonly TimeSpan ScreenSettle = TimeSpan.FromMilliseconds(150);
    internal static readonly TimeSpan ScreenSettleLimit = TimeSpan.FromSeconds(2);

    // Two clocks: one restarted by every change, which runs out once the output pauses, and one
    // started by a burst's first change, which runs out after the limit however it continues.
    private TweenHandle _settleTimer, _limitTimer;
    private bool _settlePending, _limitPending;

    internal override string? AccessibleValue => _controller?.ReadScreen();

    // ──── The text (seam A1) ────

    /// <summary>Where the cell grid is in window coordinates, set as the element renders, for the
    /// text's geometry. Replaced, never changed, so a bridge thread reads it whole.</summary>
    private volatile TextFrame? _textFrame;

    private sealed record TextFrame(float X, float Y, float CellWidth, float CellHeight);

    private ScreenTextSource? _textSource;

    internal override IAccessibleTextSource? AccessibleTextSource => _textSource ??= new ScreenTextSource(this);

    /// <summary>Records where the grid is; called from <see cref="Render"/>. A new record only when
    /// something in it moved: one a frame was the element's last render-pass allocation.</summary>
    private void NoteTextFrame()
    {
        var box = ScreenContentBox;
        if (_textFrame is { } frame && frame.X == box.X && frame.Y == box.Y
            && frame.CellWidth == _cellWidth && frame.CellHeight == _cellHeight)
            return;
        _textFrame = new TextFrame(box.X, box.Y, _cellWidth, _cellHeight);
    }

    /// <summary>The element's text for a bridge, on whichever thread it asks: rebuilt when the
    /// screen (the emulator's generation), the selection or the grid has changed.</summary>
    private sealed class ScreenTextSource(TerminalElement owner) : IAccessibleTextSource
    {
        private readonly object _gate = new();
        private (long Generation, Selection? Selection, TextFrame? Frame, TerminalController? Controller) _key;
        private AccessibleText? _text;

        public AccessibleText? AccessibleText
        {
            get
            {
                var controller = owner._controller;
                if (controller is null) return null;
                var key = (controller.Generation, owner._selection, owner._textFrame, controller);
                lock (_gate)
                {
                    if (_text is null || key != _key)
                    {
                        _text = Build(controller, key.Item2, key.Item3);
                        _key = key;
                    }
                    return _text;
                }
            }
        }
    }

    /// <summary>The screen as an <see cref="AccessibleText"/>: rows joined by line breaks, each
    /// character's column kept for the geometry.</summary>
    private static AccessibleText? Build(TerminalController controller, Selection? selection, TextFrame? frame)
    {
        var s = new TerminalSnapshot();
        if (!controller.CopySnapshot(s)) return null;

        int lastRow = -1;
        for (int row = s.Rows - 1; row >= 0 && lastRow < 0; row--)
            foreach (var cell in s.Row(row))
                if (!cell.IsBlank) { lastRow = row; break; }
        if (s.CursorVisible) lastRow = Math.Max(lastRow, s.CursorRow);

        var text = new StringBuilder();
        var columns = new List<int>();
        var widths = new List<byte>();
        var rowStarts = new int[Math.Max(0, lastRow + 1)];
        int caret = -1;
        Span<char> pair = stackalloc char[2];

        for (int row = 0; row <= lastRow; row++)
        {
            if (row > 0) Add('\n', RowEndColumn(rowStarts[row - 1]), 0);
            int start = rowStarts[row] = text.Length;
            int column = 0;
            foreach (var cell in s.Row(row))
            {
                int col = column++;
                if (cell.Width == 0) continue; // the second half of a wide character
                if (cell.Cluster is { } cluster) foreach (var c in cluster) Add(c, col, cell.Width);
                else if (cell.CodePoint == 0) Add(' ', col, cell.Width);
                else foreach (var c in pair[..(System.Text.Rune.IsValid(cell.CodePoint) ? new System.Text.Rune(cell.CodePoint) : System.Text.Rune.ReplacementChar).EncodeToUtf16(pair)]) Add(c, col, cell.Width);
            }
            // Trailing blanks are not text.
            int end = text.Length;
            while (end > start && text[end - 1] == ' ') end--;
            text.Length = end;
            columns.RemoveRange(end, columns.Count - end);
            widths.RemoveRange(end, widths.Count - end);

            if (s.CursorVisible && row == s.CursorRow) caret = OffsetOfColumn(start, end, s.CursorColumn);
        }
        // Where the text ends: after the last row's last character.
        columns.Add(lastRow >= 0 ? RowEndColumn(rowStarts[lastRow]) : 0);
        widths.Add(0);

        int selectionStart = -1, selectionEnd = -1;
        if (selection is { Block: false } sel && lastRow >= 0)
        {
            var (a, b) = sel.Ordered;
            long top = s.LinesDropped + s.ViewportTop;
            selectionStart = OffsetOf(a);
            selectionEnd = OffsetOf(b);

            int OffsetOf(TerminalPosition p)
            {
                long row = p.Line - top;
                if (row < 0) return 0;
                if (row > lastRow) return text.Length;
                int start = rowStarts[row], end = row < lastRow ? rowStarts[row + 1] - 1 : text.Length;
                return OffsetOfColumn(start, end, p.Column);
            }
        }

        var geometry = frame is null ? null : new GridGeometry(frame, [.. columns], [.. widths]);
        return new AccessibleText(text.ToString(), caret, selectionStart, selectionEnd, geometry, isReadOnly: true);

        void Add(char c, int column, byte width)
        {
            text.Append(c);
            columns.Add(column);
            widths.Add(width);
        }

        int RowEndColumn(int rowStart) =>
            columns.Count > rowStart && text.Length > rowStart ? columns[^1] + widths[^1] : 0;

        // The offset of the character covering a column, or the row's end past its text.
        int OffsetOfColumn(int start, int end, int column)
        {
            for (int i = start; i < end; i++)
                if (columns[i] + Math.Max((int)widths[i], 1) > column) return i;
            return end;
        }
    }

    /// <summary>Character boxes from the cell grid: a character's columns times the cell width,
    /// its row times the cell height, from where the grid was when the text was built.</summary>
    private sealed class GridGeometry(TextFrame frame, int[] columns, byte[] widths) : AccessibleTextGeometry
    {
        public override Rect CharacterBounds(AccessibleText text, int offset)
        {
            offset = Math.Clamp(offset, 0, columns.Length - 1);
            int line = text.LineAt(offset);
            return new Rect(frame.X + columns[offset] * frame.CellWidth, frame.Y + line * frame.CellHeight,
                widths[offset] * frame.CellWidth, frame.CellHeight);
        }

        public override int OffsetAt(AccessibleText text, Point point)
        {
            if (point.Y < frame.Y || point.X < frame.X || frame.CellHeight <= 0 || frame.CellWidth <= 0) return -1;
            int line = (int)((point.Y - frame.Y) / frame.CellHeight);
            if (line >= text.LineCount) return -1;
            int column = (int)((point.X - frame.X) / frame.CellWidth);
            int start = text.LineStart(line), end = text.LineEnd(line);
            for (int i = start; i < end; i++)
                if (columns[i] + Math.Max((int)widths[i], 1) > column) return i;
            return end;
        }
    }

    // ──── When to tell ────

    /// <summary>The screen changed: tell the reader once it settles.</summary>
    private void NoteScreenChanged()
    {
        if (!AccessibilityClients.Active) return;
        if (_settlePending) _settleTimer.Cancel();
        _settlePending = true;
        _settleTimer = Animate.After((float)ScreenSettle.TotalSeconds, ScreenSettled);

        if (_limitPending) return;
        _limitPending = true;
        _limitTimer = Animate.After((float)ScreenSettleLimit.TotalSeconds, ScreenSettled);
    }

    private void ScreenSettled()
    {
        StopScreenWait();
        if (!IsHosted) return;
        // One rebuild tells the text and the caret, whichever changed. The value (the screen) is
        // read on request: a node that offers text tells a change as its text, not its value.
        NotifyAccessibilityPropertyChanged(AccessibilityProperties.Value);
        AnnounceNewOutput();
    }

    private void StopScreenWait()
    {
        if (_settlePending) _settleTimer.Cancel();
        if (_limitPending) _limitTimer.Cancel();
        _settlePending = _limitPending = false;
    }

    // ──── Announcements (seam A2) ────

    /// <summary>Where the announced output ends: a logical line (its first line) and the
    /// characters of it already announced. Null until the first settle on the normal screen,
    /// which only takes the place.</summary>
    private TerminalPosition? _announcedTo;

    /// <summary>The user sent keys or text since the last announcement: what comes back up to
    /// the first line break is their echo.</summary>
    private bool _typedSinceAnnounce;

    private void AnnounceNewOutput()
    {
        var controller = _controller!;
        if ((_snapshot.Modes & TerminalModes.AlternateScreen) != 0)
        {
            // A full-screen program's screen is reviewed, not read out; take the place again
            // on the way back.
            _announcedTo = null;
            _typedSinceAnnounce = false;
            return;
        }

        var lines = controller.ReadLogicalLines(_announcedTo?.Line ?? Math.Max(0, _snapshot.LinesDropped + _snapshot.ScrollbackLines));
        int last = lines.Count - 1;
        while (last >= 0 && lines[last].Text.Length == 0) last--;
        if (last < 0) return;
        var end = new TerminalPosition(lines[last].FirstLine, lines[last].Text.Length);
        if (_announcedTo is not { } from)
        {
            _announcedTo = end;
            _typedSinceAnnounce = false;
            return;
        }

        // From the announced place to the end, a screenful at most.
        int first = 0;
        while (first < last && lines[first].FirstLine < from.Line) first++;
        if (lines[first].FirstLine != from.Line) from = new TerminalPosition(lines[first].FirstLine, 0);
        first = Math.Max(first, last + 1 - Math.Max(1, _snapshot.Rows));
        var sb = new StringBuilder();
        for (int i = first; i <= last; i++)
        {
            string line = lines[i].Text;
            int skip = lines[i].FirstLine == from.Line ? Math.Min(from.Column, line.Length) : 0;
            if (i > first) sb.Append('\n');
            sb.Append(line, skip, line.Length - skip);
        }
        _announcedTo = end;

        string text = sb.ToString();
        if (_typedSinceAnnounce)
        {
            // The echo of what was typed; with no line break yet, only the line being edited.
            int newline = text.IndexOf('\n');
            text = newline < 0 ? "" : text[(newline + 1)..];
        }
        _typedSinceAnnounce = false;
        // Trimmed: the place was taken where a line's text ended, before the blanks a prompt
        // leaves, so output continuing that line starts with them.
        if (!string.IsNullOrWhiteSpace(text)) Announce(text.Trim());
    }
}
