using System.Text;
using XTerm;
using XTerm.Buffer;
using XTerm.Options;
using static XtermNet.Probe;

namespace XtermNet;

/// <summary>
/// Question 5: what a renderer pays to read the grid. XTerm.NET has no damage tracking (the
/// <c>RenderEventArgs</c> type exists, nothing raises it), so the element either walks the whole
/// viewport every frame or works out for itself which rows changed. Three costs, per frame, for a
/// full 200x60 screen of coloured text: copy the viewport out under the lock, walk it into styled
/// runs the way a Skia text renderer wants them, and hash each row to find the dirty ones.
/// </summary>
internal static class Q5
{
    public static int Run()
    {
        const int cols = 200, rows = 60;
        var t = new Terminal(new TerminalOptions { Cols = cols, Rows = rows, Scrollback = 1000 });
        var screen = Q4.Corpora().First(c => c.Name == "sgr-churn").Text;
        t.Write(screen[..Math.Min(screen.Length, 400_000)]);
        t.Write("\x1b[H東京 👍🏽 é á ─┼─");

        var snapshot = new BufferCell[rows * cols];
        var copy = Time(() => Copy(t, snapshot), 1.0);
        Log($"copy viewport out ({cols}x{rows} cells, indexer per cell): {copy.NsPerOp / 1000,8:0.0} us/frame  {copy.BytesPerOp,8:0} B alloc/frame");

        var chars = new char[cols * 4];
        var runs = 0;
        var walkCp = Time(() => runs = WalkByCodePoint(t, chars), 1.0);
        Log($"walk into styled runs, CodePoint + cluster table:  {walkCp.NsPerOp / 1000,8:0.0} us/frame  {walkCp.BytesPerOp,8:0} B alloc/frame  ({runs} runs)");
        var walkContent = Time(() => runs = WalkByContent(t, chars), 1.0);
        Log($"walk into styled runs, cell.Content strings:       {walkContent.NsPerOp / 1000,8:0.0} us/frame  {walkContent.BytesPerOp,8:0} B alloc/frame  ({runs} runs)");

        var hashes = new int[rows];
        var hash = Time(() => HashRows(t, hashes), 1.0);
        Log($"hash every row (dirty detection):                  {hash.NsPerOp / 1000,8:0.0} us/frame  {hash.BytesPerOp,8:0} B alloc/frame");

        // Does the hash see the changes a renderer must repaint? One cell's character, one cell's
        // colour, a scroll.
        HashRows(t, hashes);
        var before = (int[])hashes.Clone();
        t.Write("\x1b[10;5HZ");
        HashRows(t, hashes);
        var changedChar = Enumerable.Range(0, rows).Where(r => hashes[r] != before[r]).ToArray();
        before = (int[])hashes.Clone();
        t.Write("\x1b[20;5H\x1b[41mZ\x1b[0m");
        HashRows(t, hashes);
        var changedColour = Enumerable.Range(0, rows).Where(r => hashes[r] != before[r]).ToArray();
        before = (int[])hashes.Clone();
        t.Write("\x1b[60;1H\r\n");
        HashRows(t, hashes);
        var changedScroll = Enumerable.Range(0, rows).Count(r => hashes[r] != before[r]);
        var previous = before.ToHashSet();
        var reusable = hashes.Count(previous.Contains);
        Log($"hash sees: a character -> rows [{string.Join(",", changedChar)}]; a colour -> rows [{string.Join(",", changedColour)}];"
            + $" a scroll -> {changedScroll} of {rows} rows moved, {reusable} of them found in a cache keyed by content");
        return 0;
    }

    private static void Copy(Terminal t, BufferCell[] into)
    {
        var b = t.Buffer;
        for (var r = 0; r < t.Rows; r++)
        {
            var line = b.Lines[b.YDisp + r]!;
            var n = Math.Min(t.Cols, line.Length);
            for (var c = 0; c < n; c++) into[r * t.Cols + c] = line[c];
        }
    }

    /// <summary>What a Skia renderer builds: per row, maximal runs of one style and one cell width,
    /// as UTF-16, with wide cells as runs of their own so each lands on its own column.</summary>
    private static int WalkByCodePoint(Terminal t, char[] chars)
    {
        var b = t.Buffer;
        var runs = 0;
        for (var r = 0; r < t.Rows; r++)
        {
            var line = b.Lines[b.YDisp + r]!;
            var len = 0;
            var style = default(AttributeData);
            for (var c = 0; c < line.Length; c++)
            {
                var cell = line[c];
                if (cell.Width == 0) continue;
                if (len > 0 && (cell.Width != 1 || !cell.Attributes.Equals(style))) { runs++; len = 0; }
                style = cell.Attributes;
                if (cell.ClusterId != 0) { var s = cell.Content; s.CopyTo(chars.AsSpan(len)); len += s.Length; }
                else if (cell.CodePoint == 0) chars[len++] = ' ';
                else len += new Rune(cell.CodePoint).EncodeToUtf16(chars.AsSpan(len));
                if (cell.Width != 1) { runs++; len = 0; }
            }
            if (len > 0) runs++;
        }
        return runs;
    }

    private static int WalkByContent(Terminal t, char[] chars)
    {
        var b = t.Buffer;
        var runs = 0;
        for (var r = 0; r < t.Rows; r++)
        {
            var line = b.Lines[b.YDisp + r]!;
            var len = 0;
            var style = default(AttributeData);
            for (var c = 0; c < line.Length; c++)
            {
                var cell = line[c];
                if (cell.Width == 0) continue;
                if (len > 0 && (cell.Width != 1 || !cell.Attributes.Equals(style))) { runs++; len = 0; }
                style = cell.Attributes;
                var s = cell.Content;
                if (s.Length == 0) chars[len++] = ' ';
                else { s.CopyTo(chars.AsSpan(len)); len += s.Length; }
                if (cell.Width != 1) { runs++; len = 0; }
            }
            if (len > 0) runs++;
        }
        return runs;
    }

    private static void HashRows(Terminal t, int[] hashes)
    {
        var b = t.Buffer;
        for (var r = 0; r < t.Rows; r++)
        {
            var line = b.Lines[b.YDisp + r]!;
            var h = new HashCode();
            for (var c = 0; c < line.Length; c++)
            {
                var cell = line[c];
                h.Add(cell.CodePoint);
                h.Add(cell.ClusterId);
                h.Add(cell.Width);
                h.Add(cell.Attributes.GetHashCode());
            }
            // Content only, not position: a scroll moves rows, and a renderer that keys its cached
            // row pictures by content redraws only the row that is new.
            hashes[r] = h.ToHashCode();
        }
    }
}
