using System.Globalization;
using ICU4N.Text;

namespace HultUI.Text;

/// <summary>
/// Finds line break opportunities in text using ICU4N's line break rules.
/// </summary>
internal static class LineBreaker
{
    /// <summary>
    /// Returns indices where the text may be broken for line wrapping.
    /// Each index is a position AFTER which a break may occur.
    /// </summary>
    public static List<int> FindBreakOpportunities(string text)
    {
        var breaks = new List<int>();
        if (string.IsNullOrEmpty(text))
            return breaks;

        var iterator = BreakIterator.GetLineInstance(CultureInfo.InvariantCulture);
        iterator.SetText(text);

        // Skip the first boundary (always 0)
        iterator.First();
        for (int pos = iterator.Next(); pos != BreakIterator.Done; pos = iterator.Next())
        {
            breaks.Add(pos);
        }

        return breaks;
    }
}
