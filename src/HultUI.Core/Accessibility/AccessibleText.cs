using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Core.Accessibility;

/// <summary>
/// The text an element offers a screen reader to read and move through: what UIA's text
/// pattern and AT-SPI's Text interface serve. An immutable snapshot, so a bridge can read it on
/// its own thread while the element changes; the element hands out a new one when its text,
/// caret or selection change (<see cref="IAccessibleTextSource"/>).
/// </summary>
/// <remarks>
/// <para>Paragraphs are the text's <c>'\n'</c>-separated runs. Lines are what a reader moves
/// through by line and what <see cref="LineAt"/> answers: the paragraphs, unless the element
/// says where its text wraps (<c>lineStarts</c>), and then the lines it draws — as a word
/// processor's reader means a line. Offsets count UTF-16 code units, as both platforms do;
/// moving by character steps over a surrogate pair whole.</para>
/// <para>Words are the framework's own (the same as a double-click selects): letters, digits
/// and underscores, blanks, and everything else are three kinds, and a word runs to the start of
/// the next one, so it includes the blanks after it — what a reader means by "next word".</para>
/// </remarks>
public sealed class AccessibleText
{
    private readonly int[] _lineStarts;
    private readonly int[] _paragraphStarts;

    /// <param name="text">The text, paragraphs separated by <c>'\n'</c>.</param>
    /// <param name="caret">Where the caret is, or -1 when there is none.</param>
    /// <param name="selectionStart">Where the selection starts; equal to
    /// <paramref name="selectionEnd"/> (or -1) for none.</param>
    /// <param name="selectionEnd">Where the selection ends, past its last character.</param>
    /// <param name="geometry">Where the characters are on screen; null when the element cannot
    /// say, and then only the element's own box is known.</param>
    /// <param name="visibleStart">The first character on screen; 0 for all of it.</param>
    /// <param name="visibleEnd">Past the last character on screen; -1 for the end.</param>
    /// <param name="isReadOnly">Whether nothing the reader does may change the text.</param>
    /// <param name="lineStarts">Where each drawn line starts, for text that wraps; null when the
    /// lines are the paragraphs. Every paragraph's start is a line's start whether listed or not.</param>
    public AccessibleText(string text, int caret = -1, int selectionStart = -1, int selectionEnd = -1,
        AccessibleTextGeometry? geometry = null, int visibleStart = 0, int visibleEnd = -1, bool isReadOnly = true,
        IReadOnlyList<int>? lineStarts = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        Caret = caret < 0 ? -1 : Math.Min(caret, text.Length);
        if (selectionStart >= 0 && selectionEnd >= 0 && selectionStart != selectionEnd)
        {
            SelectionStart = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length);
            SelectionEnd = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, text.Length);
        }
        Geometry = geometry;
        VisibleStart = Math.Clamp(visibleStart, 0, text.Length);
        VisibleEnd = visibleEnd < 0 ? text.Length : Math.Clamp(visibleEnd, VisibleStart, text.Length);
        IsReadOnly = isReadOnly;

        // Vectorized searches: a long document's text is hundreds of thousands of characters,
        // and an editor makes one of these a keystroke while a reader listens.
        var span = text.AsSpan();
        _paragraphStarts = new int[span.Count('\n') + 1];
        for (int at = 0, p = 1, i; (i = span[at..].IndexOf('\n')) >= 0; at += i + 1)
            _paragraphStarts[p++] = at + i + 1;
        _lineStarts = lineStarts is { Count: > 0 } ? Merge(_paragraphStarts, lineStarts, text.Length) : _paragraphStarts;
    }

    // The paragraph starts and the given line starts, in order, each once, inside the text.
    private static int[] Merge(int[] paragraphs, IReadOnlyList<int> lines, int length)
    {
        var all = new List<int>(paragraphs.Length + lines.Count);
        int a = 0, b = 0, last = -1;
        while (a < paragraphs.Length || b < lines.Count)
        {
            int next = b >= lines.Count || (a < paragraphs.Length && paragraphs[a] <= lines[b]) ? paragraphs[a++] : lines[b++];
            if (next <= last || next > length) continue;
            all.Add(next);
            last = next;
        }
        return [.. all];
    }

    public string Text { get; }
    public int Length => Text.Length;

    /// <summary>The caret's offset, or -1 when there is none.</summary>
    public int Caret { get; }

    /// <summary>The selection; <see cref="SelectionStart"/> equals <see cref="SelectionEnd"/>
    /// when there is none.</summary>
    public int SelectionStart { get; }
    public int SelectionEnd { get; }
    public bool HasSelection => SelectionEnd > SelectionStart;

    /// <summary>The characters on screen, [<see cref="VisibleStart"/>,
    /// <see cref="VisibleEnd"/>).</summary>
    public int VisibleStart { get; }
    public int VisibleEnd { get; }

    public bool IsReadOnly { get; }
    public AccessibleTextGeometry? Geometry { get; }

    // ──── Lines ────

    public int LineCount => _lineStarts.Length;

    /// <summary>The line holding <paramref name="offset"/> (clamped into the text).</summary>
    public int LineAt(int offset)
    {
        offset = Math.Clamp(offset, 0, Length);
        int i = Array.BinarySearch(_lineStarts, offset);
        return i >= 0 ? i : ~i - 1;
    }

    public int LineStart(int line) => _lineStarts[Math.Clamp(line, 0, LineCount - 1)];

    /// <summary>Where a line's text ends: before its <c>'\n'</c>, where the next line starts when
    /// it wraps, or at the end of the text.</summary>
    public int LineEnd(int line)
    {
        line = Math.Clamp(line, 0, LineCount - 1);
        if (line + 1 >= LineCount) return Length;
        int next = _lineStarts[line + 1];
        return Text[next - 1] == '\n' ? next - 1 : next;
    }

    /// <summary>A line with its <c>'\n'</c>: what a reader takes as the line unit, so moving
    /// by lines covers the text with no gaps.</summary>
    public (int Start, int End) LineRange(int line)
    {
        line = Math.Clamp(line, 0, LineCount - 1);
        return (_lineStarts[line], line + 1 < LineCount ? _lineStarts[line + 1] : Length);
    }

    // ──── Paragraphs ────

    public int ParagraphCount => _paragraphStarts.Length;

    /// <summary>The paragraph holding <paramref name="offset"/>, with its <c>'\n'</c>: the
    /// paragraph unit, which covers the text with no gaps as lines do. The same as the line
    /// unit for text that does not wrap.</summary>
    public (int Start, int End) ParagraphRange(int offset)
    {
        offset = Math.Clamp(offset, 0, Length);
        int i = Array.BinarySearch(_paragraphStarts, offset);
        int p = i >= 0 ? i : ~i - 1;
        return (_paragraphStarts[p], p + 1 < _paragraphStarts.Length ? _paragraphStarts[p + 1] : Length);
    }

    // ──── Words ────

    /// <summary>The word holding <paramref name="offset"/>: a run of one kind of character
    /// with the blanks (line breaks included) after it, up to the next word's start. Blanks at
    /// the very start are a word of their own.</summary>
    public (int Start, int End) WordRange(int offset)
    {
        if (Length == 0) return (0, 0);
        int start = WordStart(Math.Clamp(offset, 0, Length - 1));
        return (start, NextWordStart(start));
    }

    /// <summary>The start of the word after the one holding <paramref name="offset"/>; the
    /// length at the end. Where Ctrl+Right lands.</summary>
    public int NextWordStart(int offset) => WordBoundary.WordRight(Text, offset);

    /// <summary>The start of the word holding the character before <paramref name="offset"/>:
    /// the current word's start from inside it, the previous word's from a word's start; 0 at
    /// the start.</summary>
    public int PreviousWordStart(int offset)
    {
        offset = Math.Clamp(offset, 0, Length);
        return offset == 0 ? 0 : WordStart(offset - 1);
    }

    /// <summary>The start of the word holding the character at <paramref name="offset"/>.</summary>
    private int WordStart(int offset)
    {
        int i = offset;
        if (WordBoundary.IsBlank(Text[i]))
        {
            // Trailing blanks belong to the run before them.
            while (i > 0 && WordBoundary.IsBlank(Text[i - 1])) i--;
            if (i == 0) return 0;
            i--;
        }
        while (i > 0 && WordBoundary.SameKind(Text[i - 1], Text[i])) i--;
        return i;
    }

    // ──── Characters ────

    /// <summary>The offset after the character at <paramref name="offset"/>, a surrogate pair
    /// counting as one.</summary>
    public int NextCharacter(int offset)
    {
        if (offset >= Length) return Length;
        if (offset < 0) return 0;
        return char.IsHighSurrogate(Text[offset]) && offset + 1 < Length && char.IsLowSurrogate(Text[offset + 1])
            ? offset + 2 : offset + 1;
    }

    /// <summary>The offset of the character before <paramref name="offset"/>.</summary>
    public int PreviousCharacter(int offset)
    {
        if (offset <= 0) return 0;
        if (offset > Length) return Length;
        return offset >= 2 && char.IsLowSurrogate(Text[offset - 1]) && char.IsHighSurrogate(Text[offset - 2])
            ? offset - 2 : offset - 1;
    }

    // ──── Geometry ────

    /// <summary>One box per line of [<paramref name="start"/>, <paramref name="end"/>), in
    /// layout units from the window's top left (as <see cref="AccessibilityNode.BoundingRect"/>);
    /// empty when there is no geometry.</summary>
    public IReadOnlyList<Rect> RangeBounds(int start, int end)
    {
        if (Geometry is null) return [];
        start = Math.Clamp(start, 0, Length);
        end = Math.Clamp(end, start, Length);
        return Geometry.RangeBounds(this, start, end);
    }

    /// <summary>The character at a point in layout units, or -1 where there is none.</summary>
    public int OffsetAt(Point point) => Geometry?.OffsetAt(this, point) ?? -1;
}

/// <summary>
/// Where an <see cref="AccessibleText"/>'s characters are on screen, in layout units from the
/// window's top left. Called on the bridges' threads, so it must not touch live elements: an
/// implementation keeps what it needs from when its snapshot was made.
/// </summary>
public abstract class AccessibleTextGeometry
{
    /// <summary>The box of the character at <paramref name="offset"/>. At a line's end (its
    /// <c>'\n'</c>, or the end of the text) a box of no width where a caret there would stand.</summary>
    public abstract Rect CharacterBounds(AccessibleText text, int offset);

    /// <summary>The character at <paramref name="point"/>: the nearest one on the line under it,
    /// or -1 when the point is outside the text.</summary>
    public abstract int OffsetAt(AccessibleText text, Point point);

    /// <summary>One box per line the range covers, from its first character's left edge to its
    /// last character's right edge on that line. An empty range on a line is a box of no width.</summary>
    public virtual IReadOnlyList<Rect> RangeBounds(AccessibleText text, int start, int end)
    {
        var boxes = new List<Rect>();
        int first = text.LineAt(start), last = text.LineAt(end);
        // A range ending at the start of a line does not reach into it.
        if (last > first && end == text.LineStart(last)) last--;
        for (int line = first; line <= last; line++)
        {
            int from = Math.Max(start, text.LineStart(line));
            int to = Math.Min(end, text.LineEnd(line));
            var a = CharacterBounds(text, from);
            if (to <= from) { boxes.Add(new Rect(a.X, a.Y, 0, a.Height)); continue; }
            var b = CharacterBounds(text, text.PreviousCharacter(to));
            float left = Math.Min(a.X, b.X), right = Math.Max(a.X + a.Width, b.X + b.Width);
            boxes.Add(new Rect(left, a.Y, right - left, a.Height));
        }
        return boxes;
    }
}

/// <summary>
/// Hands a bridge an element's <see cref="AccessibleText"/> as it is now. Read on the bridges'
/// threads (UIA's workers, the AT-SPI bus thread), so it must be safe off the UI thread: return a
/// snapshot made earlier, or make one under the element's own lock.
/// </summary>
public interface IAccessibleTextSource
{
    /// <summary>The text now; null while the element has none to offer.</summary>
    AccessibleText? AccessibleText { get; }
}

/// <summary>
/// An element's text as one tree was built with it, kept cheap: enough for the differ to tell
/// whether the text, or its caret and selection, changed since another version, and to make the
/// <see cref="AccessibleText"/> when something asks for it. For an element whose text is costly to
/// make on every rebuild and that a reader may never read: a long document's
/// (<see cref="IVersionedAccessibleTextSource"/>).
/// </summary>
internal abstract class AccessibleTextVersion
{
    /// <summary>Whether <paramref name="other"/>, a version of the same element's text, has the same
    /// text: what comparing the two snapshots' <see cref="AccessibleText.Text"/> would answer.</summary>
    public abstract bool SameText(AccessibleTextVersion other);

    /// <summary>The caret's offset, or -1 when there is none: what <see cref="Text"/>'s
    /// <see cref="AccessibleText.Caret"/> is, without making the text. Any thread.</summary>
    public abstract int Caret { get; }

    /// <summary>The selection, as <see cref="Text"/>'s <see cref="AccessibleText.SelectionStart"/>
    /// and <see cref="AccessibleText.SelectionEnd"/> are (both 0 for none), without making the
    /// text. Any thread.</summary>
    public abstract int SelectionStart { get; }
    public abstract int SelectionEnd { get; }

    /// <summary>Whether <paramref name="other"/> has the caret and the selection at the same offsets.</summary>
    public bool SameSelection(AccessibleTextVersion other) =>
        Caret == other.Caret && SelectionStart == other.SelectionStart && SelectionEnd == other.SelectionEnd;

    /// <summary>The text of this version, made on first ask; any thread.</summary>
    public abstract AccessibleText Text { get; }

    /// <summary>The element's value with this text (UIA's ValueValue), made on first ask; any thread.</summary>
    public abstract string? Value { get; }
}

/// <summary>
/// A text source whose text is made only when something asks for it: the tree's builder takes a
/// <see cref="AccessibleTextVersion"/> from it in place of the text, and the node's
/// <see cref="AccessibilityNode.Value"/> comes from the version too.
/// </summary>
internal interface IVersionedAccessibleTextSource : IAccessibleTextSource
{
    /// <summary>The version of the text as it is now. On the UI thread, when a tree is built.</summary>
    AccessibleTextVersion CurrentVersion { get; }
}

/// <summary>How urgently a screen reader should speak an announcement
/// (<see cref="Element.Announce"/>): ARIA's live-region politeness.</summary>
public enum AnnouncementPoliteness
{
    /// <summary>When the reader is done with what it is saying: new output, a status.</summary>
    Polite,

    /// <summary>At once, cutting in: an error the user must hear now.</summary>
    Assertive,
}
