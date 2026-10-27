using HultUI.Core.Primitives;

namespace HultUI.Core.Rendering;

/// <summary>
/// A background a <see cref="Elements.TextAreaElement"/> paints behind a range of its text, in
/// UTF-16 offsets into the whole text: a find match, a line the debugger stopped on. Its
/// <see cref="Elements.TextAreaElement.Highlights"/> are a layer of these over its
/// <see cref="Elements.TextAreaElement.Spans"/>.
///
/// <para>Only a background. A highlight never changes the colour of the text under it, its
/// shaping or its wrapping, so handing an area other highlights draws it again and lays nothing
/// out. A transparent <see cref="Background"/> draws nothing.</para>
///
/// <para>Not a <see cref="TextSpan"/>, though the area draws one as a background-only span: a
/// span's colour, decoration and face would mean nothing here, and a highlight is 12 bytes where
/// a span is 40, which is what a find of 20,000 matches hands over on every key.</para>
/// </summary>
public readonly record struct TextHighlight(int Start, int Length, Color Background)
{
    public int End => Start + Length;
}
