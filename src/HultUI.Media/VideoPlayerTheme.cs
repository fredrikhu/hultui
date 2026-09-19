using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Media;

/// <summary>
/// Colours and metrics of the <see cref="VideoPlayer"/>'s transport bar. Video chrome
/// is dark whatever the app's palette — a light bar over a film reads wrong — so only
/// the accent follows the theme by default. Override it as any theme extension:
/// <c>app.Theme = Theme.Dark.With(new VideoPlayerTheme { Accent = brand })</c>.
/// </summary>
public sealed class VideoPlayerTheme : IThemeExtension<VideoPlayerTheme>
{
    /// <summary>Behind the transport bar, over the picture.</summary>
    public Color Scrim { get; init; } = new(0, 0, 0, 170);
    /// <summary>Icons, labels and the slider thumbs.</summary>
    public Color Foreground { get; init; } = Color.White;
    /// <summary>Icons and labels that are secondary: the duration, the rate.</summary>
    public Color MutedForeground { get; init; } = new(255, 255, 255, 160);
    /// <summary>The played part of the scrubber and the volume level.</summary>
    public Color Accent { get; init; } = new(79, 70, 229);
    /// <summary>Hover fill behind a transport button.</summary>
    public Color ButtonHover { get; init; } = new(255, 255, 255, 40);
    /// <summary>Behind the picture, where it does not fill the element.</summary>
    public Color Backdrop { get; init; } = Color.Black;
    public float BarHeight { get; init; } = 52f;
    public float FontSize { get; init; } = 13f;

    public static VideoPlayerTheme CreateDefault(Theme theme) => new() { Accent = theme.Primary };
}
