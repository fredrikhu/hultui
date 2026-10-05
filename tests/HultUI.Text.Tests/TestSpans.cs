using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Text.Tests;

/// <summary>Span lists for the styled-path tests: metric spans on some words, and a
/// colour-only one that layout must not see.</summary>
internal static class TestSpans
{
    /// <summary>Two different span lists for one text, so a pipeline that kept one list's
    /// work under the other's would show it.</summary>
    public static IEnumerable<TextSpan[]> Variants(string text)
    {
        yield return OnWords(text, w => w % 3 == 1 ? new(0, 0, Color.Black, Weight: FontWeight.Bold)
            : w % 5 == 3 ? new(0, 0, Color.Black, FontSize: 20f)
            : w == 0 ? new(0, 0, new Color(200, 0, 0, 255))
            : null);
        yield return OnWords(text, w => w % 4 == 1 ? new(0, 0, Color.Black, Slant: FontSlant.Italic)
            : w % 4 == 2 ? new(0, 0, Color.Black, Weight: FontWeight.Bold, FontSize: 12f)
            : null);
    }

    // One span per space-separated word that pick gives one for, placed over that word.
    private static TextSpan[] OnWords(string text, Func<int, TextSpan?> pick)
    {
        var spans = new List<TextSpan>();
        int word = 0, start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != ' ') continue;
            if (i > start && pick(word++) is { } span)
                spans.Add(span with { Start = start, Length = i - start });
            start = i + 1;
        }
        return spans.ToArray();
    }
}
