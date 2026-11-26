using HultUI.Core.Animation;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;

namespace HultUI.Core.Builders;

/// <summary>
/// One transient notice at a time, bottom-centre: <see cref="Flash"/> shows a message
/// that fades in, stays a moment and fades out, or stays longer and offers one verb when
/// it is asking for a decision rather than reporting one. App state, like
/// <see cref="PaneSplitter"/>: the owner adds <see cref="Build"/> to its root on every
/// build, and the host calls back to rebuild when the toast appears, changes or goes.
/// The fades in between are written to the toast on screen, a repaint, not a rebuild.
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
    private readonly Action<float> _fadeStep;
    /// <summary>The toast on screen, whose opacity a fade step writes (a Ref follows it across
    /// the owner's rebuilds).</summary>
    private readonly Ref<ToastElement> _shown = new();
    private readonly Action<PanStartEvent> _panStart;
    private readonly Action<PanUpdateEvent> _panUpdate;
    private readonly Action<PanEndEvent> _panEnd;
    private readonly Action<float> _slideStep;
    private TweenHandle _hold;
    private TweenHandle _fade;
    private TweenHandle _slide;
    private float _opacity;
    private int _serial;
    private float _holdSeconds;
    /// <summary>How far a finger has pulled the toast sideways, or where the slide back or away
    /// has it.</summary>
    private float _dragX;
    private bool _dragging;

    /// <summary>How far, as a share of its width, a toast is pulled before letting go sends it away.</summary>
    private const float SwipeFraction = 0.4f;
    /// <summary>A flick at least this fast (DIP a second) the way it is pulled sends it away from anywhere.</summary>
    private const float FlingSpeed = 600f;
    private const float SlideSeconds = 0.15f;

    /// <param name="onChanged">Runs when the toast appears, changes or goes — normally the
    /// owner's <c>MarkDirty</c>. Not on the frames of a fade: those write the opacity of the
    /// toast on screen. They called it on every step, and the apps hand it the
    /// <c>MarkDirty</c> of their whole window, so a toast rebuilt the app on every frame of
    /// its fades (Junction: 23 builds and 4.5 MB for one toast).</param>
    public ToastHost(Action onChanged)
    {
        _onChanged = onChanged;
        _fadeStep = SetOpacity;
        _panStart = OnPanStart;
        _panUpdate = OnPanUpdate;
        _panEnd = OnPanEnd;
        _slideStep = SetDrag;
    }

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
    /// <summary>The least distance from the window's side edges.</summary>
    private const float SideMargin = 16f;

    /// <summary>
    /// Shows <paramref name="message"/>, replacing whatever was up. With an
    /// <paramref name="action"/> it stays <see cref="ActionSeconds"/> instead of
    /// <see cref="Seconds"/>; <paramref name="seconds"/> overrides either.
    /// </summary>
    public void Flash(string message, ToastSeverity severity = ToastSeverity.None,
        (string Label, Action Run)? action = null, float? seconds = null)
    {
        ++_serial;
        _hold.Cancel();
        _fade.Cancel();
        bool wasShowing = IsShowing;
        Message = message;
        Severity = severity;
        Action = action;
        // A replacement comes in where the toast belongs, not where a finger had pulled the last one.
        _slide.Cancel();
        _dragX = 0;
        _dragging = false;

        // A replacement while one is up does not blink: it goes on to whole from the level it
        // has, at the fade-in's pace. It used to keep that level, so one that came while the
        // first was fading in or out stayed half drawn for its whole hold (the fade it met was
        // cancelled above and nothing raised it again).
        if (!wasShowing) _opacity = 0f;
        if (_opacity < 1f)
        {
            _fade = Animate.Value(_opacity, 1f, _fadeStep)
                .Duration(FadeInSeconds * (1f - _opacity)).Ease(Easing.EaseOutQuad).Start();
        }

        _holdSeconds = seconds ?? (action is null ? Seconds : ActionSeconds);
        StartHold();
        _onChanged();
    }

    /// <summary>The toast's time on screen, from now, after which it fades out.</summary>
    private void StartHold()
    {
        int serial = _serial;
        _hold.Cancel();
        // A timer: the loop wakes once, when it ends. A tween with this Duration and a setter
        // that changed nothing stepped every frame interval: 118 steps for a 2 s toast.
        _hold = Animate.After(_holdSeconds, () => { if (serial == _serial) FadeOut(serial); });
    }

    /// <summary>Takes the toast down now, without the fade.</summary>
    public void Dismiss()
    {
        _serial++;
        _hold.Cancel();
        _fade.Cancel();
        _slide.Cancel();
        if (!IsShowing) return;
        Clear();
        _onChanged();
    }

    private void FadeOut(int serial)
    {
        _fade = Animate.Value(_opacity, 0f, _fadeStep)
            .Duration(FadeOutSeconds)
            .OnComplete(() => { if (serial == _serial) { Clear(); _onChanged(); } })
            .Start();
    }

    /// <summary>A step of a fade: the level, and the toast on screen at it. A rebuild in the
    /// middle of a fade (the owner's, for anything) builds the toast at the level too.</summary>
    private void SetOpacity(float opacity)
    {
        _opacity = opacity;
        if (_shown.Current is { } toast) toast.Opacity = opacity * DragAlpha;
    }

    private void Clear()
    {
        Message = null;
        Action = null;
        Severity = ToastSeverity.None;
        _opacity = 0f;
        _dragX = 0;
        _dragging = false;
    }

    // ──── Swiped away ────
    //
    // A finger drags the toast sideways (OnPan: touch only, past the touch slop), and it fades as
    // it goes. Let go past SwipeFraction of its width, or flicked the way it is pulled, it slides
    // the rest of the way and is gone, as Dismiss; let go short of that, it slides back and has its
    // whole time again. While the finger holds it, it does not time out: a toast grabbed as it
    // began to fade comes back whole. The mouse cannot swipe it: on a desktop it times out.

    private float PillWidth => Math.Max(1f, _shown.Current?.Layout.AbsoluteBorderBox.Width ?? 0f);

    /// <summary>How much of the toast's fade level shows at the distance it is pulled: all of it
    /// where it belongs, none a whole width away.</summary>
    private float DragAlpha => _dragX == 0 ? 1f : Math.Max(0f, 1f - Math.Abs(_dragX) / PillWidth);

    /// <summary>The toast at <paramref name="x"/> sideways of where it belongs, drawn there: a
    /// repaint, not a build.</summary>
    private void SetDrag(float x)
    {
        _dragX = x;
        if (_shown.Current is not { } toast) return;
        toast.Transform = x == 0 ? null : Transform2D.Translate(x, 0);
        toast.Opacity = _opacity * DragAlpha;
    }

    private void OnPanStart(PanStartEvent e)
    {
        if (!IsShowing) return;
        _dragging = true;
        _hold.Cancel();
        _slide.Cancel();
        if (_opacity < 1f)
        {
            // Held as it faded out (or still fading in): whole again, at the fade-in's pace.
            _fade.Cancel();
            _fade = Animate.Value(_opacity, 1f, _fadeStep)
                .Duration(FadeInSeconds * (1f - _opacity)).Ease(Easing.EaseOutQuad).Start();
        }
    }

    private void OnPanUpdate(PanUpdateEvent e)
    {
        // A Flash while the finger is down puts the new toast where it belongs; that finger no
        // longer moves it.
        if (_dragging) SetDrag(e.Total.X);
    }

    private void OnPanEnd(PanEndEvent e)
    {
        if (!_dragging) return;
        _dragging = false;
        float width = PillWidth, vx = e.Velocity.X;
        bool away = !e.Cancelled && _dragX != 0
            && (Math.Abs(_dragX) >= width * SwipeFraction || (Math.Abs(vx) >= FlingSpeed && Math.Sign(vx) == Math.Sign(_dragX)));
        int serial = _serial;
        if (away)
        {
            _slide = Animate.Value(_dragX, Math.Sign(_dragX) * width, _slideStep)
                .Duration(SlideSeconds).Ease(Easing.EaseOutQuad)
                .OnComplete(() => { if (serial == _serial) Dismiss(); })
                .Start();
            return;
        }
        _slide = Animate.Value(_dragX, 0f, _slideStep).Duration(SlideSeconds).Ease(Easing.EaseOutQuad).Start();
        StartHold();
    }

    /// <summary>
    /// The strip to add to the root on every build, or null when nothing is showing. It
    /// spans the window's bottom edge, centres the pill, and is keyed so a rebuild keeps
    /// it rather than remounting it mid-fade.
    /// </summary>
    public Element? Build()
    {
        if (Message is null)
        {
            _shown.Current = null;
            return null;
        }

        var toast = ToastElement.New(Message, Severity);
        // A rebuild in the middle of a fade or a swipe builds the toast where they have it.
        toast.Opacity = _opacity * DragAlpha;
        if (_dragX != 0) toast.Transform = Transform2D.Translate(_dragX, 0);
        toast.OnPan(PanAxis.Horizontal, _panStart, _panUpdate, _panEnd);
        toast.WithRef(_shown);
        if (Action is { } action)
        {
            toast.ActionLabel = action.Label;
            toast.OnAction = () => { Dismiss(); action.Run(); };
        }

        // Padded at the sides too, so a message too long for the window wraps inside it with a
        // margin, rather than running to the window's edges (a phone) and past them.
        var strip = Flex.Row(gap: 0)
            .Width(Dimension.Percent(100))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.FlexEnd)
            .Padding(new Thickness(0, SideMargin, BottomMargin, SideMargin))
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
