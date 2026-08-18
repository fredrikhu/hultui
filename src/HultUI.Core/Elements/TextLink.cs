namespace HultUI.Core.Elements;

/// <summary>
/// A stretch of a label's text that can be clicked, and what clicking it means. The target
/// is opaque to the framework — a URL, an id, whatever the handler understands.
/// </summary>
/// <remarks>
/// A range rather than a nested element, because the text around it has to keep flowing,
/// wrapping and — where the label is selectable — taking part in one continuous selection.
/// Splitting a line into a row of little labels would break all three. Appearance is left to
/// the caller: give the same range a <see cref="Rendering.TextSpan"/> to colour or underline
/// it, so a link can look like whatever the surface's design says a link looks like.
/// </remarks>
public readonly record struct TextLink(int Start, int Length, string Target)
{
    public int End => Start + Length;

    /// <summary>Whether <paramref name="offset"/> falls inside this link.</summary>
    public bool Contains(int offset) => offset >= Start && offset < End;
}
