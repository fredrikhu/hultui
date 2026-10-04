using HultUI.Core.Accessibility;
using HultUI.Core.Animation;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// An on/off switch: a pill-shaped track with a knob that slides to the on side. The
/// same contract as <see cref="CheckboxElement"/> — click or Space toggles,
/// <see cref="OnChanged"/> reports the new state — with the knob animated across the
/// track rather than snapping, and the <see cref="AccessibilityRole.Switch"/> role so a
/// reader says "switch, on" rather than "check box, checked".
/// </summary>
/// <remarks>
/// Every app had grown its own: one moved the knob with padding, one with
/// justify-content, one with a transform transition. The knob position here is a
/// single 0..1 number the element owns, tweened on change while mounted and set
/// directly otherwise, so a rebuild that patches <see cref="IsOn"/> onto the live
/// element animates from wherever the knob currently is.
/// </remarks>
public class ToggleSwitchElement : Element
{
    private bool _isOn;
    private bool _focused;
    /// <summary>Where the knob is drawn, 0 = off side, 1 = on side.</summary>
    private float _knob;
    private TweenHandle _knobTween;
    private Color _focusRingColor;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            _isOn = value;
            MoveKnob(value ? 1f : 0f);
            MarkPaintDirty();
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.IsToggled);
        }
    }

    /// <summary>Text drawn to the right of the track; part of the element so one click
    /// target covers both, like a checkbox's label.</summary>
    public string? Label { get; set; }
    public float LabelGap { get; set; } = 8f;

    public float TrackWidth { get; set; } = 40f;
    public float TrackHeight { get; set; } = 20f;
    public float KnobSize { get; set; } = 14f;
    public Color OnTrackColor { get; set; } = new(79, 70, 229);
    public Color OffTrackColor { get; set; } = new(241, 245, 249);
    public Color OffBorderColor { get; set; } = new(100, 116, 139);
    public float OffBorderWidth { get; set; } = 1f;
    public Color OnKnobColor { get; set; } = Color.White;
    public Color OffKnobColor { get; set; } = new(100, 116, 139);
    /// <summary>How long the knob takes to cross the track. Zero snaps.</summary>
    public float TransitionSeconds { get; set; } = 0.15f;

    public Action<bool>? OnChanged { get; set; }

    /// <summary>Where the knob is drawn right now, 0 (off) to 1 (on). Exposed so a test
    /// can check the slide finished rather than trusting that it started.</summary>
    public float KnobPosition => _knob;

    protected internal override void ApplyTheme(Theme theme)
    {
        var t = theme.ToggleSwitch;
        TrackWidth = t.TrackWidth;
        TrackHeight = t.TrackHeight;
        KnobSize = t.KnobSize;
        OnTrackColor = t.OnTrack ?? theme.Primary;
        OffTrackColor = t.OffTrack ?? theme.SurfaceVariant;
        OffBorderColor = t.OffBorder ?? theme.Secondary;
        OffBorderWidth = t.OffBorderWidth;
        OnKnobColor = t.OnKnob ?? theme.PrimaryText;
        OffKnobColor = t.OffKnob ?? theme.Secondary;
        TransitionSeconds = t.TransitionSeconds;
        _focusRingColor = theme.FocusRing;
    }

    public ToggleSwitchElement()
    {
        IsFocusable = true;
        Inherited.Cursor = CursorType.Pointer;
        OnOwn<ClickEvent>(_ => Toggle());
        OnOwn<FocusEvent>(_ => { _focused = true; MarkPaintDirty(); });
        OnOwn<BlurEvent>(_ => { _focused = false; MarkPaintDirty(); });
        OnOwn<KeyDownEvent>(evt =>
        {
            if (!_focused) return;
            if (evt.Key == KeyCode.Space)
            {
                Toggle();
                evt.StopPropagation = true;
            }
        });
    }

    // One factory rather than overloads, for the same reason as CheckboxElement: a
    // trailing optional string would let New(true, "Label") bind the label to the
    // captured file path.
    public static ToggleSwitchElement New(bool isOn = false, string? label = null, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new ToggleSwitchElement { _isOn = isOn, _knob = isOn ? 1f : 0f, Label = label }.WithOrigin(file, line);

    private void Toggle()
    {
        IsOn = !IsOn;
        OnChanged?.Invoke(IsOn);
    }

    private void MoveKnob(float to)
    {
        _knobTween.Cancel();
        // Only an element on screen has a knob to slide; the incoming twin of a rebuild
        // and an element built but not yet shown just take the position.
        if (!IsHosted || TransitionSeconds <= 0f)
        {
            _knob = to;
            return;
        }
        _knobTween = Animate.Value(_knob, to, v => { _knob = v; MarkPaintDirty(); })
            .Duration(TransitionSeconds)
            .Ease(Easing.EaseOutCubic)
            .Start();
    }

    protected override void OnUnmounted()
    {
        base.OnUnmounted();
        _knobTween.Cancel();
        _knob = _isOn ? 1f : 0f;
    }

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
    {
        if (Label is null)
            return new Size(TrackWidth, TrackHeight);

        float textMaxWidth = Math.Max(0f, availableWidth - TrackWidth - LabelGap);
        var textSize = ctx.MeasureText(Label, ResolvedInherited, textMaxWidth, null);
        return new Size(
            TrackWidth + LabelGap + textSize.Width,
            Math.Max(TrackHeight, textSize.Height));
    }

    // The label, drawn from the top of the content box beside the track. No label, no text.
    internal override float? MeasureBaseline(Size borderBox, IMeasureContext ctx) =>
        string.IsNullOrEmpty(Label)
            ? null
            : ContentTop + ctx.MeasureFirstBaseline(Label, ResolvedInherited, float.PositiveInfinity, null, TextOverflow.Ellipsis);

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteContentBox;
        float x = Label is null ? box.X + (box.Width - TrackWidth) / 2 : box.X;
        float y = box.Y + (box.Height - TrackHeight) / 2;
        var track = new Rect(x, y, TrackWidth, TrackHeight);
        var pill = new CornerRadius(TrackHeight / 2);

        // The track cross-fades between its two fills as the knob travels, so the
        // colour and the position tell the same story mid-slide.
        var fill = Lerp.Color(OffTrackColor, OnTrackColor, _knob);
        var border = Lerp.Color(OffBorderColor, new Color(OffBorderColor.R, OffBorderColor.G, OffBorderColor.B, 0), _knob);
        ctx.DrawRoundedRect(track, pill, fill, border, new Thickness(OffBorderWidth));

        float inset = (TrackHeight - KnobSize) / 2;
        float travel = TrackWidth - 2 * inset - KnobSize;
        var knob = new Rect(x + inset + travel * _knob, y + inset, KnobSize, KnobSize);
        ctx.DrawRoundedRect(knob, new CornerRadius(KnobSize / 2),
            Lerp.Color(OffKnobColor, OnKnobColor, _knob), Color.Transparent, default);

        if (_focused)
        {
            const float offset = 3f;
            var ring = new Rect(x - offset, y - offset, TrackWidth + offset * 2, TrackHeight + offset * 2);
            ctx.DrawRoundedRect(ring, new CornerRadius(TrackHeight / 2 + offset), Color.Transparent, _focusRingColor, new Thickness(2f));
        }

        if (Label is not null)
        {
            float textX = box.X + TrackWidth + LabelGap;
            var textBounds = new Rect(textX, box.Y, box.Width - TrackWidth - LabelGap, box.Height);
            ctx.DrawText(Label, textBounds, ResolvedInherited, TextOverflow.Ellipsis, null);
        }
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is ToggleSwitchElement ts)
        {
            Label = ts.Label;
            LabelGap = ts.LabelGap;
            TrackWidth = ts.TrackWidth;
            TrackHeight = ts.TrackHeight;
            KnobSize = ts.KnobSize;
            OnTrackColor = ts.OnTrackColor;
            OffTrackColor = ts.OffTrackColor;
            OffBorderColor = ts.OffBorderColor;
            OffBorderWidth = ts.OffBorderWidth;
            OnKnobColor = ts.OnKnobColor;
            OffKnobColor = ts.OffKnobColor;
            TransitionSeconds = ts.TransitionSeconds;
            OnChanged = ts.OnChanged;
            // Through the property, so a state change from a rebuild slides the knob
            // from where it is instead of copying the twin's snapped position.
            IsOn = ts.IsOn;
        }
    }
}
