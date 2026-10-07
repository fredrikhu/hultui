using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Primitives;

namespace HultUI.Platform.Accessibility.Linux;

/// <summary>
/// AT-SPI's Text interface over an <see cref="AccessibleText"/> (seam A1 on Linux): the answers,
/// with no D-Bus in them, so they can be tested without a bus. The handler marshals them.
/// </summary>
/// <remarks>
/// Offsets are AT-SPI's character offsets, which here are the text's UTF-16 offsets, as on
/// Windows. Coordinates are device pixels from the window's top left whatever coordinate type is
/// asked for, as the Component interface answers (under Wayland a client has no screen origin).
/// A sentence is a line: the text knows no smaller structure. A paragraph is a paragraph, which
/// is a line unless the text wraps.
/// </remarks>
[SupportedOSPlatform("linux")]
internal static class AtSpiText
{
    /// <summary>Text from <paramref name="start"/> to <paramref name="end"/>; an end of -1 (or
    /// past the text) is its end.</summary>
    public static string GetText(AccessibleText t, int start, int end)
    {
        start = Math.Clamp(start, 0, t.Length);
        end = end < 0 ? t.Length : Math.Clamp(end, start, t.Length);
        return t.Text[start..end];
    }

    /// <summary>The unit of <paramref name="granularity"/> holding <paramref name="offset"/>.</summary>
    public static (string Text, int Start, int End) GetStringAtOffset(AccessibleText t, int offset, uint granularity)
    {
        var (start, end) = Unit(t, offset, granularity switch
        {
            AtSpiConstants.GranularityChar => Granularity.Char,
            AtSpiConstants.GranularityWord => Granularity.Word,
            AtSpiConstants.GranularityParagraph => Granularity.Paragraph,
            _ => Granularity.Line,
        });
        return (t.Text[start..end], start, end);
    }

    /// <summary>GetTextAtOffset, -BeforeOffset and -AfterOffset (<paramref name="relative"/> 0,
    /// -1, 1): the unit holding the offset, or the one before or after it.</summary>
    public static (string Text, int Start, int End) GetTextRelative(AccessibleText t, int offset, uint boundary, int relative)
    {
        var g = boundary switch
        {
            AtSpiConstants.BoundaryChar => Granularity.Char,
            AtSpiConstants.BoundaryWordStart or AtSpiConstants.BoundaryWordEnd => Granularity.Word,
            _ => Granularity.Line,
        };
        var (start, end) = Unit(t, offset, g);
        if (relative < 0)
            (start, end) = start == 0 ? (0, 0) : Unit(t, start - 1, g);
        else if (relative > 0)
            (start, end) = end >= t.Length ? (t.Length, t.Length) : Unit(t, end, g);
        return (t.Text[start..end], start, end);
    }

    private enum Granularity { Char, Word, Line, Paragraph }

    private static (int Start, int End) Unit(AccessibleText t, int offset, Granularity g)
    {
        offset = Math.Clamp(offset, 0, t.Length);
        return g switch
        {
            Granularity.Char => offset >= t.Length ? (t.Length, t.Length) : (offset, t.NextCharacter(offset)),
            Granularity.Word => offset >= t.Length && t.Length > 0 ? (t.PreviousWordStart(t.Length), t.Length) : t.WordRange(offset),
            Granularity.Paragraph => t.ParagraphRange(offset),
            _ => t.LineRange(t.LineAt(offset)),
        };
    }

    /// <summary>The code point at <paramref name="offset"/>, or 0 outside the text.</summary>
    public static int GetCharacterAtOffset(AccessibleText t, int offset)
    {
        if (offset < 0 || offset >= t.Length) return 0;
        return char.IsHighSurrogate(t.Text[offset]) && offset + 1 < t.Length && char.IsLowSurrogate(t.Text[offset + 1])
            ? char.ConvertToUtf32(t.Text[offset], t.Text[offset + 1])
            : t.Text[offset];
    }

    /// <summary>A character's box, in device pixels; zeros where the text has no geometry.</summary>
    public static (int X, int Y, int Width, int Height) GetCharacterExtents(AccessibleText t, int offset, float scale)
    {
        if (t.Geometry is null || offset < 0 || offset > t.Length) return default;
        return Pixels(t.Geometry.CharacterBounds(t, offset), scale);
    }

    /// <summary>The box around a range, in device pixels: its lines' boxes together.</summary>
    public static (int X, int Y, int Width, int Height) GetRangeExtents(AccessibleText t, int start, int end, float scale)
    {
        var boxes = t.RangeBounds(start, end < 0 ? t.Length : end);
        if (boxes.Count == 0) return default;
        float left = boxes.Min(b => b.X), top = boxes.Min(b => b.Y);
        float right = boxes.Max(b => b.X + b.Width), bottom = boxes.Max(b => b.Y + b.Height);
        return Pixels(new Rect(left, top, right - left, bottom - top), scale);
    }

    /// <summary>The character at a point in device pixels, or -1.</summary>
    public static int GetOffsetAtPoint(AccessibleText t, int x, int y, float scale)
    {
        if (scale <= 0) scale = 1;
        return t.OffsetAt(new Point(x / scale, y / scale));
    }

    public static int SelectionCount(AccessibleText t) => t.HasSelection ? 1 : 0;

    public static (int Start, int End) GetSelection(AccessibleText t, int index) =>
        index == 0 && t.HasSelection ? (t.SelectionStart, t.SelectionEnd) : (0, 0);

    private static (int X, int Y, int Width, int Height) Pixels(Rect r, float scale) =>
        ((int)MathF.Round(r.X * scale), (int)MathF.Round(r.Y * scale),
         (int)MathF.Round(r.Width * scale), (int)MathF.Round(r.Height * scale));

    /// <summary>
    /// What changed between two texts, as AT-SPI tells it: one deletion and one insertion at the
    /// first differing offset, each null when there is none. Common ends are left out, so typing
    /// a character is an insertion of one.
    /// </summary>
    public static ((int Offset, string Text)? Deleted, (int Offset, string Text)? Inserted) Difference(string before, string after)
    {
        int prefix = 0, max = Math.Min(before.Length, after.Length);
        while (prefix < max && before[prefix] == after[prefix]) prefix++;
        int suffix = 0;
        while (suffix < max - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix]) suffix++;
        var deleted = before.Length - prefix - suffix > 0 ? (prefix, before.Substring(prefix, before.Length - prefix - suffix)) : ((int, string)?)null;
        var inserted = after.Length - prefix - suffix > 0 ? (prefix, after.Substring(prefix, after.Length - prefix - suffix)) : ((int, string)?)null;
        return (deleted, inserted);
    }
}
