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
}
