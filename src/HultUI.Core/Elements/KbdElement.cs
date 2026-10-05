using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>The chip <see cref="Kbd.New"/> returns; it puts <see cref="KbdTheme"/> on itself
/// and its text each style pass.</summary>
public sealed class KbdElement : BoxElement
{
    protected internal override void ApplyTheme(Theme theme)
    {
        var t = theme.Get<KbdTheme>();
        BackgroundColor = t.Background ?? theme.Surface;
        BorderColor = t.Border ?? theme.Border;
        Box.BorderWidth = new Primitives.Thickness(1);
        CornerRadius = new Primitives.CornerRadius(t.CornerRadius);
        Box.Padding = t.Padding;
        foreach (var child in Children)
        {
            child.Inherited.FontSize = t.FontSize;
            child.Inherited.TextColor = t.Text ?? theme.Secondary;
            if (t.FontFamily is { } family) child.Inherited.FontFamily = family;
        }
    }
}
