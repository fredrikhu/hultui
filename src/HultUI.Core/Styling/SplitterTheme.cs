using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// The grip of a <see cref="Elements.SplitterHandleElement"/>: how wide it is to the
/// pointer and how it lights under it. It draws no line of its own — the pane beside it
/// already borders that edge — so what the theme sets is the target and the feedback.
/// </summary>
public sealed class SplitterTheme : IThemeExtension<SplitterTheme>
{
    /// <summary>How thick the handle is to the pointer. Wider than any line, because a
    /// 1px target is a game of skill rather than a control.</summary>
    public float Thickness { get; init; } = 6f;
    /// <summary>Tint under the pointer; null takes the primary colour at 18 % alpha.</summary>
    public Color? Hover { get; init; }
    /// <summary>Tint while grabbed; null takes the primary colour at 40 % alpha.</summary>
    public Color? Grabbed { get; init; }
    /// <summary>How far one arrow-key press moves the split.</summary>
    public float KeyStep { get; init; } = 10f;

    public static SplitterTheme CreateDefault(Theme theme) => new();
}
