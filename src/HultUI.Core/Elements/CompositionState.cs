namespace HultUI.Core.Elements;

/// <summary>
/// IME preedit (composition) state shared by <see cref="TextInputElement"/> and
/// <see cref="TextAreaElement"/>. While <see cref="IsActive"/> the <see cref="Text"/>
/// is rendered inline at the caret as an underlined preview; it is not part of the
/// element's underlying text until the IME commits via a <c>TextInputEvent</c>.
///
/// <para>The active segment (<see cref="SegmentStart"/>, <see cref="SegmentLength"/>)
/// is the portion of the preedit the IME is currently editing — drawn with a thicker
/// underline per the Windows/macOS convention.</para>
/// </summary>
internal struct CompositionState
{
    public string Text;
    public int SegmentStart;
    public int SegmentLength;

    public readonly bool IsActive => !string.IsNullOrEmpty(Text);

    /// <summary>
    /// Replaces composition content. An empty <paramref name="text"/> clears all fields —
    /// SDL signals commit/cancel by sending TEXT_EDITING with empty text immediately
    /// before (or instead of) the corresponding TEXT_INPUT.
    /// </summary>
    public void Set(string? text, int segmentStart, int segmentLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            Text = "";
            SegmentStart = 0;
            SegmentLength = 0;
            return;
        }
        Text = text;
        SegmentStart = Math.Clamp(segmentStart, 0, text.Length);
        SegmentLength = Math.Clamp(segmentLength, 0, text.Length - SegmentStart);
    }

    public void Clear()
    {
        Text = "";
        SegmentStart = 0;
        SegmentLength = 0;
    }
}
