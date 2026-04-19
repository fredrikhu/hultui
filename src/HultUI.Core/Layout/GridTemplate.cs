using System.Globalization;

namespace HultUI.Core.Layout;

public static class GridTemplate
{
    /// <summary>
    /// Parse a CSS-grid-style template string into an array of <see cref="GridTrack"/>.
    /// Whitespace-separated tokens. Supported tokens: <c>auto</c>, <c>&lt;n&gt;px</c>,
    /// <c>&lt;n&gt;%</c>, <c>&lt;n&gt;fr</c>, bare <c>&lt;n&gt;</c> (treated as pixels),
    /// and bare <c>fr</c> (treated as <c>1fr</c>).
    /// </summary>
    public static GridTrack[] Parse(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
            throw new ArgumentException("Grid template must contain at least one track.", nameof(template));

        var span = template.AsSpan();
        var tracks = new List<GridTrack>(capacity: 4);

        int i = 0;
        while (i < span.Length)
        {
            while (i < span.Length && char.IsWhiteSpace(span[i])) i++;
            if (i >= span.Length) break;

            int start = i;
            while (i < span.Length && !char.IsWhiteSpace(span[i])) i++;
            tracks.Add(ParseToken(span[start..i], template));
        }

        if (tracks.Count == 0)
            throw new ArgumentException("Grid template must contain at least one track.", nameof(template));

        return tracks.ToArray();
    }

    private static GridTrack ParseToken(ReadOnlySpan<char> token, string original)
    {
        if (token.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return GridTrack.Auto;

        if (token.Equals("fr", StringComparison.OrdinalIgnoreCase))
            return GridTrack.Fr(1f);

        if (TryStripSuffix(token, "px", out var px) && TryParseFloat(px, out var pxVal))
            return GridTrack.Fixed(pxVal);

        if (TryStripSuffix(token, "fr", out var fr) && TryParseFloat(fr, out var frVal))
            return GridTrack.Fr(frVal);

        if (TryStripSuffix(token, "%", out var pct) && TryParseFloat(pct, out var pctVal))
            return GridTrack.Percent(pctVal);

        if (TryParseFloat(token, out var bare))
            return GridTrack.Fixed(bare);

        throw new FormatException(
            $"Unrecognized grid track token '{token.ToString()}' in template '{original}'.");
    }

    private static bool TryStripSuffix(ReadOnlySpan<char> token, string suffix, out ReadOnlySpan<char> rest)
    {
        if (token.Length > suffix.Length &&
            token[^suffix.Length..].Equals(suffix, StringComparison.OrdinalIgnoreCase))
        {
            rest = token[..^suffix.Length];
            return true;
        }
        rest = default;
        return false;
    }

    private static bool TryParseFloat(ReadOnlySpan<char> s, out float value) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
