using HultUI.Core.Elements;
using HultUI.Core.Styling;

namespace HultUI.Core.Builders;

/// <summary>
/// A key chip: <c>Ctrl+P</c> in a small outlined box, wherever a key is named — a palette row,
/// a tooltip's neighbour, an options page. Its look is the theme's <see cref="KbdTheme"/>.
/// </summary>
public static class Kbd
{
    public static KbdElement New(string keys)
    {
        var chip = new KbdElement();
        chip.AddChild(Text.New(keys));
        return chip;
    }
}
