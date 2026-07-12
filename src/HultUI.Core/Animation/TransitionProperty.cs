namespace HultUI.Core.Animation;

/// <summary>
/// A visual property that can be smoothly animated when it changes (e.g. via an
/// interaction-state override). Register with <c>.Transition(...)</c>.
/// </summary>
public enum TransitionProperty : byte
{
    Background,
    BorderColor,
    Opacity,
    Transform
}
