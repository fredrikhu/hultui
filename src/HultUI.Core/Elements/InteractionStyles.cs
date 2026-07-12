using HultUI.Core.Animation;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;

namespace HultUI.Core.Elements;

/// <summary>
/// Backs an element's declarative interaction styling (<c>.OnHover</c>/<c>.OnPressed</c>/
/// <c>.OnFocus</c>) and property transitions. Created lazily, so elements that don't
/// use it cost nothing.
///
/// <para>Model: a base snapshot of the element's visual properties is captured, and
/// on every state change the element is reset to that base and the active state
/// overrides are re-layered (pressed on top of focus on top of hover). Overrides are
/// pure functions of the element passed in, so they survive reconciliation.</para>
/// </summary>
internal sealed class InteractionStyles
{
    public bool Hovered;
    public bool Pressed;
    public bool Focused;

    public Action<Element>? OnHover;
    public Action<Element>? OnPressed;
    public Action<Element>? OnFocus;

    private List<TransitionSpec>? _transitions;
    private VisualState? _base;
    private Dictionary<TransitionProperty, TweenHandle>? _tweens;

    public void AddTransition(TransitionProperty property, float seconds, Func<float, float> easing)
    {
        _transitions ??= [];
        _transitions.RemoveAll(s => s.Property == property);
        _transitions.Add(new TransitionSpec(property, seconds, easing));
    }

    public void CopyConfigFrom(InteractionStyles source)
    {
        OnHover = source.OnHover;
        OnPressed = source.OnPressed;
        OnFocus = source.OnFocus;
        _transitions = source._transitions;
    }

    /// <summary>Forces the base snapshot to be recaptured on the next recompute.</summary>
    public void InvalidateBase() => _base = null;

    /// <summary>
    /// Resets the element to its base visual state and re-applies the active state
    /// overrides. When <paramref name="animate"/> is true, properties with a
    /// registered transition tween from their current value to the new target.
    /// </summary>
    public void Recompute(Element owner, bool animate)
    {
        _base ??= VisualState.Capture(owner);

        bool animating = animate && _transitions is { Count: > 0 };
        VisualState before = animating ? VisualState.Capture(owner) : default;

        VisualState.Restore(owner, _base.Value);
        if (Hovered) OnHover?.Invoke(owner);
        if (Focused) OnFocus?.Invoke(owner);
        if (Pressed) OnPressed?.Invoke(owner);

        if (animating)
        {
            var after = VisualState.Capture(owner);
            foreach (var spec in _transitions!)
                AnimateTransition(owner, spec, before, after);
        }
        else
        {
            CancelAllTweens();
        }

        owner.MarkPaintDirty();
    }

    private void AnimateTransition(Element owner, TransitionSpec spec, VisualState before, VisualState after)
    {
        CancelTween(spec.Property);
        TweenHandle handle;

        switch (spec.Property)
        {
            case TransitionProperty.Background:
                if (owner is not BoxElement bg || before.Background == after.Background) return;
                bg.BackgroundColor = before.Background;
                handle = new TweenBuilder<Color>(before.Background, after.Background, Lerp.Color, v => bg.BackgroundColor = v)
                    .MarkDirty(bg.MarkPaintDirty).Duration(spec.DurationSeconds).Ease(spec.Easing).Start();
                break;

            case TransitionProperty.BorderColor:
                if (owner is not BoxElement bd || before.BorderColor == after.BorderColor) return;
                bd.BorderColor = before.BorderColor;
                handle = new TweenBuilder<Color>(before.BorderColor, after.BorderColor, Lerp.Color, v => bd.BorderColor = v)
                    .MarkDirty(bd.MarkPaintDirty).Duration(spec.DurationSeconds).Ease(spec.Easing).Start();
                break;

            case TransitionProperty.Opacity:
                if (before.Opacity == after.Opacity) return;
                owner.Opacity = before.Opacity;
                handle = new TweenBuilder<float>(before.Opacity, after.Opacity, Lerp.Float, v => owner.Opacity = v)
                    .MarkDirty(owner.MarkPaintDirty).Duration(spec.DurationSeconds).Ease(spec.Easing).Start();
                break;

            case TransitionProperty.Transform:
                var b = before.Transform ?? Transform2D.Identity;
                var a = after.Transform ?? Transform2D.Identity;
                if (b.Equals(a)) return;
                owner.Transform = b;
                handle = new TweenBuilder<Transform2D>(b, a, Lerp.Transform, v => owner.Transform = v)
                    .MarkDirty(owner.MarkPaintDirty).Duration(spec.DurationSeconds).Ease(spec.Easing).Start();
                break;

            default:
                return;
        }

        (_tweens ??= [])[spec.Property] = handle;
    }

    private void CancelTween(TransitionProperty property)
    {
        if (_tweens is not null && _tweens.TryGetValue(property, out var handle))
        {
            handle.Cancel();
            _tweens.Remove(property);
        }
    }

    private void CancelAllTweens()
    {
        if (_tweens is null) return;
        foreach (var handle in _tweens.Values)
            handle.Cancel();
        _tweens.Clear();
    }

    private readonly record struct TransitionSpec(TransitionProperty Property, float DurationSeconds, Func<float, float> Easing);

    /// <summary>A snapshot of the transitionable/overridable visual properties.</summary>
    private readonly struct VisualState
    {
        private readonly float _opacity;
        private readonly Transform2D? _transform;
        private readonly bool _isBox;
        private readonly Color _background;
        private readonly Gradient? _gradient;
        private readonly Color _borderColor;
        private readonly BorderStyle _borderStyle;
        private readonly CornerRadius _cornerRadius;
        private readonly Thickness _borderWidth;
        private readonly List<BoxShadow>? _shadows;

        public float Opacity => _opacity;
        public Transform2D? Transform => _transform;
        public Color Background => _background;
        public Color BorderColor => _borderColor;

        private VisualState(float opacity, Transform2D? transform, bool isBox, Color background, Gradient? gradient,
            Color borderColor, BorderStyle borderStyle, CornerRadius cornerRadius, Thickness borderWidth, List<BoxShadow>? shadows)
        {
            _opacity = opacity;
            _transform = transform;
            _isBox = isBox;
            _background = background;
            _gradient = gradient;
            _borderColor = borderColor;
            _borderStyle = borderStyle;
            _cornerRadius = cornerRadius;
            _borderWidth = borderWidth;
            _shadows = shadows;
        }

        public static VisualState Capture(Element e)
        {
            if (e is BoxElement b)
                return new VisualState(e.Opacity, e.Transform, true, b.BackgroundColor, b.BackgroundGradient,
                    b.BorderColor, b.BorderStyle, b.CornerRadius, b.Box.BorderWidth,
                    b.BoxShadows is null ? null : [.. b.BoxShadows]);

            return new VisualState(e.Opacity, e.Transform, false, default, null, default, default, default, default, null);
        }

        public static void Restore(Element e, VisualState s)
        {
            e.Opacity = s._opacity;
            e.Transform = s._transform;
            if (e is BoxElement b && s._isBox)
            {
                b.BackgroundColor = s._background;
                b.BackgroundGradient = s._gradient;
                b.BorderColor = s._borderColor;
                b.BorderStyle = s._borderStyle;
                b.CornerRadius = s._cornerRadius;
                b.Box.BorderWidth = s._borderWidth;
                b.BoxShadows = s._shadows is null ? null : [.. s._shadows];
            }
        }
    }
}
