namespace Meridian.Core;

/// <summary>
/// One physical line of a document: the text and the exact terminator that
/// followed it in the source ("\n", "\r\n", or "" for a final line with no
/// trailing newline). Keeping the terminator per line is what makes
/// serialization byte-for-byte exact on files with mixed endings.
/// </summary>
public readonly record struct SourceLine(string Raw, string Terminator);

internal static class LineBuffer
{
    /// <summary>Splits <paramref name="text"/> into lines preserving each line's
    /// own terminator. Empty input yields no lines.</summary>
    public static List<SourceLine> Split(string text)
    {
        var lines = new List<SourceLine>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            bool crlf = i > start && text[i - 1] == '\r';
            int end = crlf ? i - 1 : i;
            lines.Add(new SourceLine(text[start..end], crlf ? "\r\n" : "\n"));
            start = i + 1;
        }
        if (start < text.Length)
            lines.Add(new SourceLine(text[start..], ""));
        return lines;
    }

    public static string Join(IEnumerable<SourceLine> lines)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            sb.Append(line.Raw);
            sb.Append(line.Terminator);
        }
        return sb.ToString();
    }
}
