using HultUI.Core.Animation;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;

namespace HultUI.Core.Builders;

/// <summary>
/// One transient notice at a time, bottom-centre: <see cref="Flash"/> shows a message
/// that fades in, stays a moment and fades out, or stays longer and offers one verb when
/// it is asking for a decision rather than reporting one. App state, like
/// <see cref="PaneSplitter"/>: the owner adds <see cref="Build"/> to its root on every
/// build, and the host calls back to rebuild when anything changes.
/// </summary>
/// <remarks>
/// Two apps had grown the same forty lines: a message field, a cancellation token, a
/// <c>Task.Delay</c>, an absolutely positioned strip with a pill in it. The clock here
/// is a tween rather than a task, so it runs on the frame loop's virtual time and a
/// headless test can pump exactly the seconds it means to; and the strip is looked
/// through by the pointer, so a toast over the bottom edge does not steal the clicks
/// meant for whatever sits there.
/// </remarks>
public sealed class ToastHost
{
    private readonly Action _onChanged;
    private TweenHandle _hold;
    private TweenHandle _fade;
    private float _opacity;
    private int _serial;

    /// <param name="onChanged">Runs whenever the toast appears, changes, fades or goes —
    /// normally the owner's <c>MarkDirty</c>.</param>
    public ToastHost(Action onChanged) => _onChanged = onChanged;

    /// <summary>The message on screen, or null.</summary>
    public string? Message { get; private set; }
    public ToastSeverity Severity { get; private set; }
    /// <summary>The verb the toast offers, if any.</summary>
    public (string Label, Action Run)? Action { get; private set; }
    public bool IsShowing => Message is not null;
    /// <summary>Where the fade is, 0..1.</summary>
    public float Opacity => _opacity;

    /// <summary>How long a plain toast stays.</summary>
    public float Seconds { get; set; } = 1.7f;
    /// <summary>How long a toast with an action stays: it is asking for a decision.</summary>
    public float ActionSeconds { get; set; } = 4.5f;
    public float FadeInSeconds { get; set; } = 0.12f;
    public float FadeOutSeconds { get; set; } = 0.18f;
    /// <summary>Distance from the window's bottom edge.</summary>
    public float BottomMargin { get; set; } = 22f;
    /// <summary>Z-order of the strip among the root's children.</summary>
    public int ZIndex { get; set; } = 50;

    /// <summary>
    /// Shows <paramref name="message"/>, replacing whatever was up. With an
    /// <paramref name="action"/> it stays <see cref="ActionSeconds"/> instead of
    /// <see cref="Seconds"/>; <paramref name="seconds"/> overrides either.
    /// </summary>
    public void Flash(string message, ToastSeverity severity = ToastSeverity.None,
        (string Label, Action Run)? action = null, float? seconds = null)
    {
        int serial = ++_serial;
        _hold.Cancel();
        _fade.Cancel();
        bool wasShowing = IsShowing;
        Message = message;
        Severity = severity;
        Action = action;

        // A replacement while one is up does not blink: it keeps the opacity it has.
        if (!wasShowing)
        {
            _opacity = 0f;
            _fade = Animate.Value(0f, 1f, v => { _opacity = v; _onChanged(); })
                .Duration(FadeInSeconds).Ease(Easing.EaseOutQuad).Start();
        }

        float hold = seconds ?? (action is null ? Seconds : ActionSeconds);
        // A timer: the loop wakes once, when it ends. A tween with this Duration and a setter
        // that changed nothing stepped every frame interval: 118 steps for a 2 s toast.
        _hold = Animate.After(hold, () => { if (serial == _serial) FadeOut(serial); });
        _onChanged();
    }

    /// <summary>Takes the toast down now, without the fade.</summary>
    public void Dismiss()
    {
        _serial++;
        _hold.Cancel();
        _fade.Cancel();
        if (!IsShowing) return;
        Clear();
        _onChanged();
    }

    private void FadeOut(int serial)
    {
        _fade = Animate.Value(_opacity, 0f, v => { _opacity = v; _onChanged(); })
            .Duration(FadeOutSeconds)
            .OnComplete(() => { if (serial == _serial) { Clear(); _onChanged(); } })
            .Start();
    }

    private void Clear()
    {
        Message = null;
        Action = null;
        Severity = ToastSeverity.None;
        _opacity = 0f;
    }

    /// <summary>
    /// The strip to add to the root on every build, or null when nothing is showing. It
    /// spans the window's bottom edge, centres the pill, and is keyed so a rebuild keeps
    /// it rather than remounting it mid-fade.
    /// </summary>
    public Element? Build()
    {
        if (Message is null) return null;

        var toast = ToastElement.New(Message, Severity);
        toast.Opacity = _opacity;
        if (Action is { } action)
        {
            toast.ActionLabel = action.Label;
            toast.OnAction = () => { Dismiss(); action.Run(); };
        }

        var strip = Flex.Row(gap: 0)
            .Width(Dimension.Percent(100))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.FlexEnd)
            .Padding(new Thickness(0, 0, BottomMargin, 0))
            .ZIndex(ZIndex)
            .Children(toast);
        strip.Position.Type = PositionType.Absolute;
        strip.Position.Left = Dimension.Px(0);
        strip.Position.Bottom = Dimension.Px(0);
        strip.IsHitTestVisible = false;
        strip.Key = "toast";
        return strip;
    }
}
