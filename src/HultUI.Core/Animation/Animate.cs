using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Core.Animation;

public static class Animate
{
    // ── Generic (no element, user provides setter) ──

    public static TweenBuilder<float> Value(float from, float to, Action<float> setter) =>
        new(from, to, Lerp.Float, setter);

    public static TweenBuilder<Color> Value(Color from, Color to, Action<Color> setter) =>
        new(from, to, Lerp.Color, setter);

    public static TweenBuilder<T> Value<T>(T from, T to, Func<T, T, float, T> lerp, Action<T> setter) =>
        new(from, to, lerp, setter);

    // ── Layout properties (mark layout dirty) ──

    public static TweenBuilder<Dimension> Width(Element el, Dimension from, Dimension to) =>
        new TweenBuilder<Dimension>(from, to, Lerp.Dimension, v => el.Box.Width = v)
            .MarkDirty(el.MarkLayoutDirty);

    public static TweenBuilder<Dimension> Height(Element el, Dimension from, Dimension to) =>
        new TweenBuilder<Dimension>(from, to, Lerp.Dimension, v => el.Box.Height = v)
            .MarkDirty(el.MarkLayoutDirty);

    public static TweenBuilder<Thickness> Padding(Element el, Thickness from, Thickness to) =>
        new TweenBuilder<Thickness>(from, to, Lerp.Thickness, v => el.Box.Padding = v)
            .MarkDirty(el.MarkLayoutDirty);

    public static TweenBuilder<Thickness> Margin(Element el, Thickness from, Thickness to) =>
        new TweenBuilder<Thickness>(from, to, Lerp.Thickness, v => el.Box.Margin = v)
            .MarkDirty(el.MarkLayoutDirty);

    public static TweenBuilder<float> Gap(Element el, float from, float to) =>
        new TweenBuilder<float>(from, to, Lerp.Float, v => el.Flex.Gap = v)
            .MarkDirty(el.MarkLayoutDirty);

    // ── Visual properties (mark paint dirty) ──

    public static TweenBuilder<Color> Background(BoxElement el, Color from, Color to) =>
        new TweenBuilder<Color>(from, to, Lerp.Color, v => el.BackgroundColor = v)
            .MarkDirty(el.MarkPaintDirty);

    public static TweenBuilder<Color> BorderColor(BoxElement el, Color from, Color to) =>
        new TweenBuilder<Color>(from, to, Lerp.Color, v => el.BorderColor = v)
            .MarkDirty(el.MarkPaintDirty);

    public static TweenBuilder<CornerRadius> CornerRadius(BoxElement el, CornerRadius from, CornerRadius to) =>
        new TweenBuilder<CornerRadius>(from, to, Lerp.CornerRadius, v => el.CornerRadius = v)
            .MarkDirty(el.MarkPaintDirty);

    public static TweenBuilder<float> Opacity(Element el, float from, float to) =>
        new TweenBuilder<float>(from, to, Lerp.Float, v => el.Opacity = v)
            .MarkDirty(el.MarkPaintDirty);

    // ── Timers ──

    /// <summary>
    /// Runs <paramref name="then"/> after <paramref name="seconds"/> on the frame loop's clock: a
    /// tween that waits out a delay and ends at once. The loop wakes once for it. A tween with a
    /// <see cref="TweenBuilder{T}.Duration"/> and a setter that changes nothing does the same
    /// job stepping every frame interval until it ends: a 2 s toast hold was 118 steps.
    /// </summary>
    internal static TweenHandle After(float seconds, Action then) =>
        Value(0f, 1f, static _ => { }).Delay(seconds).Duration(0f).OnComplete(then).Start();

    /// <summary>
    /// Cancels all active animations targeting the given element.
    /// Note: only works for animations started via the element-aware methods.
    /// </summary>
    public static void CancelAll(Element el)
    {
        // Elements don't track their animations — cancel is done via TweenHandle.
        // This is a no-op placeholder for future enhancement.
    }
}
