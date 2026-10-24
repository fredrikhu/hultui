using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>The chip <see cref="Builders.Kbd.New"/> returns; it puts <see cref="KbdTheme"/> on itself
/// and its text each style pass.</summary>
public sealed class KbdElement : BoxElement
{
    // Its padding and border are the theme's (compared on a themed twin); it copies nothing else.
    private protected override bool PatchMarksOwnLayout => true;
    private protected override bool PatchThemesTwin => true;

    protected internal override void ApplyTheme(Theme theme)
    {
        var t = theme.Get<KbdTheme>();
        BackgroundColor = t.Background ?? theme.Surface;
        BorderColor = t.Border ?? theme.Border;
        CornerRadius = new Primitives.CornerRadius(t.CornerRadius);
        // A theme with another padding lays the chip out again; the label's font goes through
        // the style pass's own check.
        var box = BoxRead;
        var border = new Primitives.Thickness(1);
        if (box.BorderWidth != border || box.Padding != t.Padding)
        {
            Box.BorderWidth = border;
            Box.Padding = t.Padding;
            MarkLayoutDirty();
        }
        // By index: foreach over the Children interface boxes an enumerator, now on twins too.
        var children = Children;
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            child.Inherited.FontSize = t.FontSize;
            child.Inherited.TextColor = t.Text ?? theme.Secondary;
            if (t.FontFamily is { } family) child.Inherited.FontFamily = family;
        }
    }
}
