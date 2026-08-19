namespace HultUI.Core.Elements;

/// <summary>
/// Where a word starts and ends. Shared by every text surface so a double-click and a
/// Ctrl+arrow mean the same thing in a label, an input and an editor.
/// </summary>
/// <remarks>
/// Characters fall into three classes — word (letters, digits, underscore), whitespace, and
/// everything else (punctuation and symbols) — and a double-click takes the run of whichever
/// class it landed in. That is what makes the gesture useful over code: splitting on
/// whitespace alone hands back <c>_diffGroup.Bind(key,</c> where the user asked for
/// <c>Bind</c>, and treating punctuation as a break but not symbols draws the line in a
/// different place for <c>a.b</c> than for <c>a+b</c>. Landing in the indentation selects the
/// indentation rather than nothing at all, which is what a browser does too.
/// </remarks>
internal static class WordBoundary
{
    private enum CharClass { Word, Space, Newline, Other }

    // A line break is its own class, so a run of blanks stops at the end of the line
    // instead of swallowing the next line's indentation along with it.
    private static CharClass ClassOf(char c) =>
        char.IsLetterOrDigit(c) || c == '_' ? CharClass.Word
        : c is '\n' or '\r' ? CharClass.Newline
        : char.IsWhiteSpace(c) ? CharClass.Space
        : CharClass.Other;

    /// <summary>
    /// The run of same-class characters around <paramref name="pos"/>. A caret sitting on a
    /// boundary takes the run to its left when there is one, so double-clicking just past the
    /// end of a word still selects that word.
    /// </summary>
    public static (int Start, int End) RangeAt(string text, int pos)
    {
        if (text.Length == 0) return (0, 0);
        pos = Math.Clamp(pos, 0, text.Length);

        // At the very end, or where the class changes, look left first — the pointer lands
        // between characters, and the one before it is the one that was clicked on.
        int probe = pos < text.Length ? pos : pos - 1;
        if (pos > 0 && pos < text.Length && ClassOf(text[pos - 1]) != ClassOf(text[pos]))
            probe = pos - 1;

        var cls = ClassOf(text[probe]);
        int start = probe, end = probe + 1;
        while (start > 0 && ClassOf(text[start - 1]) == cls) start--;
        while (end < text.Length && ClassOf(text[end]) == cls) end++;
        return (start, end);
    }

    // Ctrl+arrow crosses a line break the way it crosses a space: the caret keeps moving
    // rather than parking at the end of the line, which is what the whitespace-only rule
    // this replaced did too.
    private static bool IsBlank(CharClass c) => c is CharClass.Space or CharClass.Newline;

    /// <summary>
    /// Where Ctrl+Left lands, starting from <paramref name="from"/>: back over any blanks
    /// immediately to the left, then back over the whole run of whichever class precedes
    /// them. Also where Ctrl+Backspace deletes to.
    /// </summary>
    public static int WordLeft(string text, int from)
    {
        int i = Math.Clamp(from, 0, text.Length);
        while (i > 0 && IsBlank(ClassOf(text[i - 1]))) i--;
        if (i == 0) return 0;

        var cls = ClassOf(text[i - 1]);
        while (i > 0 && ClassOf(text[i - 1]) == cls) i--;
        return i;
    }

    /// <summary>
    /// Where Ctrl+Right lands, starting from <paramref name="from"/>: forward over the run
    /// the caret is in, then over the blanks that follow it. Also where Ctrl+Delete deletes to.
    /// </summary>
    public static int WordRight(string text, int from)
    {
        int i = Math.Clamp(from, 0, text.Length);
        if (i >= text.Length) return text.Length;

        var cls = ClassOf(text[i]);
        if (!IsBlank(cls))
            while (i < text.Length && ClassOf(text[i]) == cls) i++;
        while (i < text.Length && IsBlank(ClassOf(text[i]))) i++;
        return i;
    }
}
