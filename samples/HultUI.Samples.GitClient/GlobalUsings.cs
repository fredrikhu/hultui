// Resolve ambiguity: the class HultUI.Core.Builders.Text conflicts with
// the namespace HultUI.Text when our code lives under the HultUI.* namespace tree.
global using Text = HultUI.Core.Builders.Text;
global using TextArea = HultUI.Core.Builders.TextArea;

using HultUI.Core.Elements;
using HultUI.Core.Layout;

namespace GitClient;

/// <summary>
/// Extension methods for TextElement to support fluent overflow and max-lines configuration.
/// </summary>
public static class TextElementExtensions
{
    public static TextElement OverflowMode(this TextElement el, TextOverflow overflow)
    {
        el.Overflow = overflow;
        return el;
    }

    public static TextElement MaxLines(this TextElement el, int maxLines)
    {
        el.MaxLines = maxLines;
        return el;
    }
}
