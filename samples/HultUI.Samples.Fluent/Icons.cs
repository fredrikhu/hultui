using HultUI.Core.Builders;
using HultUI.Icons;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;

namespace HultUI.Samples.Fluent;

// Inside the namespace on purpose. This assembly sits under HultUI, so an unqualified `Text`
// here binds to the HultUI.Text namespace (the shaping engine) rather than to the label
// builder — and an alias declared above the namespace loses to that. Declared in the
// namespace body, it wins.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// One icon, named twice: once as a Segoe Fluent Icons codepoint and once as a Phosphor name.
///
/// <para>Windows 11 ships <i>Segoe Fluent Icons</i>, and it is a large part of why a WinUI
/// window looks like a WinUI window — the weight, the corner treatment and the optical sizing
/// of those glyphs are as much the design language as the colours are. So the sample uses the
/// real thing where it exists. It is a Windows font, though, and this framework runs on Linux
/// too, where a private-use codepoint has nothing to resolve to and every icon comes out as a
/// blank box. Hence the pair: the fallback is Phosphor, which HultUI already bundles as path
/// data and which draws at about the same visual weight.</para>
/// </summary>
public readonly record struct Icon(string Glyph, string Fallback);

public static class Icons
{
    // Navigation
    public static readonly Icon Home = new("", "squares-four");
    public static readonly Icon Personalize = new("", "palette");
    public static readonly Icon Gallery = new("", "sliders-horizontal");
    public static readonly Icon About = new("", "sparkle");

    // Chrome
    public static readonly Icon Back = new("", "arrow-left");
    public static readonly Icon Search = new("", "magnifying-glass");
    public static readonly Icon ChevronDown = new("", "caret-down");
    public static readonly Icon ChevronRight = new("", "caret-right");
    public static readonly Icon Minimize = new("", "minus");
    public static readonly Icon Maximize = new("", "square");
    public static readonly Icon Restore = new("", "browsers");
    public static readonly Icon Close = new("", "x");

    // Content
    public static readonly Icon Brightness = new("", "sparkle");
    public static readonly Icon Color = new("", "swatches");
    public static readonly Icon Font = new("", "text-aa");
    public static readonly Icon Layers = new("", "shapes");
    public static readonly Icon Storage = new("", "hard-drives");
    public static readonly Icon Network = new("", "cloud");
    public static readonly Icon Bluetooth = new("", "lightning");
    public static readonly Icon Update = new("", "arrows-clockwise");
    public static readonly Icon Privacy = new("", "lock-simple");
    public static readonly Icon Accessibility = new("", "eye");
    public static readonly Icon Keyboard = new("", "keyboard");
    public static readonly Icon Notifications = new("", "tray-arrow-down");
    public static readonly Icon Startup = new("", "rocket-launch");
    public static readonly Icon Info = new("", "check-circle");
    public static readonly Icon Success = new("", "check-circle");
    public static readonly Icon Warning = new("", "warning");
    public static readonly Icon Error = new("", "prohibit");
}

public static class IconRenderer
{
    /// <summary>
    /// Whether the real Windows icon font is available. Probed by file rather than by asking
    /// the font stack, because a missing family does not fail there — Skia hands back the
    /// default typeface, which renders a private-use codepoint as a blank box and looks like
    /// a layout bug rather than a missing font.
    /// </summary>
    public static readonly bool UseSegoe =
        OperatingSystem.IsWindows() &&
        File.Exists(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "SegoeIcons.ttf"));

    private const string SegoeIcons = "Segoe Fluent Icons";

    /// <summary>
    /// The icon at <paramref name="size"/>, in a box exactly that big either way. Fixing the
    /// box matters: a glyph's line height is not its em size, so a bare label would set a row
    /// a few pixels taller than the same row built from the Phosphor path, and every card in
    /// the page would be a different height depending on which machine drew it.
    /// </summary>
    public static Element Draw(Icon icon, float size, Color color)
    {
        if (!UseSegoe)
            return Phosphor.Draw(icon.Fallback, size, color);

        return Box.New()
            .Width(Dimension.Px(size)).Height(Dimension.Px(size)).FlexShrink(0)
            .Overflow(Overflow.Visible)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Children(
                Text.New(icon.Glyph)
                    .FontFamily(SegoeIcons)
                    .FontSize(size)
                    .LineHeight(size)
                    .TextColor(color));
    }
}
