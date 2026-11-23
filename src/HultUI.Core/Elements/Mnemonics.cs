using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>The <c>&amp;</c> rule menus and menu titles share: <c>&amp;</c> marks the next
/// character, <c>&amp;&amp;</c> is an ampersand, and only the first mark counts.</summary>
internal static class Mnemonics
{
    /// <summary>The text as shown, without its markers, and the index in it of the marked
    /// character (-1 for none).</summary>
    internal static (string Label, int Index) Parse(string text)
    {
        var label = new System.Text.StringBuilder(text.Length);
        int index = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '&' && i + 1 < text.Length)
            {
                i++;
                if (text[i] != '&' && index < 0) index = label.Length;
            }
            label.Append(text[i]);
        }
        return (label.ToString(), index);
    }

    // One list per index, made once: a label underlined while Alt is held is drawn every frame.
    private static readonly TextSpan[]?[] s_underlines = new TextSpan[]?[64];

    /// <summary>The span that underlines the character at <paramref name="index"/>, in the text's
    /// own colour. The same list every time for an index under 64; not to be changed.</summary>
    internal static TextSpan[] Underline(int index) =>
        index < s_underlines.Length ? s_underlines[index] ??= Make(index) : Make(index);

    private static TextSpan[] Make(int index) =>
        [new TextSpan(index, 1, Color.Transparent, Decoration: TextDecoration.Underline)];
}
