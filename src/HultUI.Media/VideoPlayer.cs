using HultUI.Core.Accessibility;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Icons;

namespace HultUI.Media;

// Inside a HultUI.* namespace bare `Text` binds to the HultUI.Text namespace.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// A <see cref="VideoElement"/> with a transport bar: play/pause, a scrubber with the
/// elapsed and total time, mute and volume, playback speed, subtitle and audio track
/// menus, and a full-screen button. The bar hides while playing once the pointer has
/// been still for <see cref="AutoHideDelay"/> and comes back on any movement or key.
/// </summary>
/// <remarks>
/// <para>Keys, when the player has focus (clicking the picture gives it focus): Space
/// and K play/pause; Left/Right seek by <see cref="SeekStep"/>, J/L by <see cref="JumpStep"/>;
/// Up/Down change the volume; M mutes; F asks for full screen; Home/End go to the
/// start/end; 0–9 jump to that tenth of the media.</para>
///
/// <para>Full screen is the application's to grant — the component knows no window —
/// so <see cref="FullscreenRequested"/> is raised and the app sets
/// <see cref="IsFullscreen"/> once it has done whatever that means for it.</para>
///
/// <para>Colours come from <see cref="VideoPlayerTheme"/> through the app's theme.</para>
/// </remarks>
public sealed class VideoPlayer : Component
{
    private static readonly double[] Rates = [0.5, 0.75, 1, 1.25, 1.5, 2];

    private readonly Signal<bool> _controlsVisible = new(true);
    private readonly Signal<TimeSpan> _jumpStep = new(TimeSpan.FromSeconds(10));   // the buttons carry it in their names
    // The bar's opacity while it fades; driven by a tween, read by Build.
    private readonly Signal<float> _barOpacity = new(1f);
    private readonly Signal<bool> _fullscreen = new(false);
    private readonly Signal<VideoPlayerTheme> _theme = new(VideoPlayerTheme.CreateDefault(Theme.Light));
    private TweenHandle _hideTimer;
    private TweenHandle _fade;
    private Effect? _playingWatch;

    public VideoPlayer(MediaPlayer player)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
    }

    public MediaPlayer Player { get; }

    /// <summary>Hide the bar while playing after <see cref="AutoHideDelay"/> without pointer movement.</summary>
    public bool AutoHideControls { get; set; } = true;
    public TimeSpan AutoHideDelay { get; set; } = TimeSpan.FromSeconds(2.5);
    /// <summary>How far Left/Right seek.</summary>
    public TimeSpan SeekStep { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>How far the jump buttons, and J/L, seek.</summary>
    public TimeSpan JumpStep { get => _jumpStep.Value; set => _jumpStep.Value = value; }
    /// <summary>How long the bar takes to fade out once the pointer has been still.</summary>
    public TimeSpan FadeDuration { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>The user asked for full screen (the F key or the button). The app decides what that means.</summary>
    public event Action? FullscreenRequested;

    /// <summary>Set by the app; only changes which glyph the full-screen button shows.</summary>
    public bool IsFullscreen
    {
        get => _fullscreen.Value;
        set => _fullscreen.Value = value;
    }

    /// <summary>Whether the transport bar is showing. Always true while paused.</summary>
    public bool ControlsVisible => _controlsVisible.Value || !Player.IsPlaying;

    // ──── Lifecycle ────

    protected internal override void OnMount()
    {
        base.OnMount();
        // Playing starts the hide countdown; anything else shows the bar and keeps it.
        _playingWatch = new Effect(() =>
        {
            if (Player.IsPlaying) ScheduleHide();
            else Show();
        });
    }

    protected internal override void OnUnmount()
    {
        _hideTimer.Cancel();
        _fade.Cancel();
        _playingWatch?.Dispose();
        _playingWatch = null;
        base.OnUnmount();
    }

    /// <summary>Called by the root element's theme pass; deferred a frame so a rebuild never runs inside the style walk.</summary>
    internal void OnTheme(VideoPlayerTheme theme)
    {
        if (ReferenceEquals(theme, _theme.Peek())) return;
        Application.InvokeOnMainThread(() => _theme.Value = theme);
    }

    private void Reveal()
    {
        Show();
        ScheduleHide();
    }

    /// <summary>The bar, at once: cancels any fade and a pending hide.</summary>
    private void Show()
    {
        _hideTimer.Cancel();
        _fade.Cancel();
        _controlsVisible.Value = true;
        float from = _barOpacity.Peek();
        if (from >= 1f) return;
        // Coming back is quick — half the fade-out — so the pointer never waits on it.
        _fade = Animate.Value(from, 1f, v => _barOpacity.Value = v)
            .Duration((float)FadeDuration.TotalSeconds * 0.5f)
            .Start();
    }

    private void ScheduleHide()
    {
        _hideTimer.Cancel();
        if (!AutoHideControls) return;
        // A delayed tween is the framework's timer: it ticks with the frame loop, which
        // keeps ticking through a pure delay, and cancels cleanly.
        _hideTimer = Animate.Value(0f, 1f, _ => { })
            .Delay((float)AutoHideDelay.TotalSeconds)
            .Duration(0.01f)
            .OnComplete(FadeOut)
            .Start();
    }

    private void FadeOut()
    {
        if (!Player.IsPlaying) return;
        _fade.Cancel();
        _fade = Animate.Value(_barOpacity.Peek(), 0f, v => _barOpacity.Value = v)
            .Duration((float)FadeDuration.TotalSeconds)
            .OnComplete(() => _controlsVisible.Value = false)
            .Start();
    }

    // ──── Build ────

    /// <summary>
    /// The picture element that is actually mounted — what <see cref="FocusPicture"/> hands
    /// the keyboard to. A <see cref="Ref{T}"/> rather than the instance this component's
    /// Build returned: the reconciler patches a rebuilt element into the mounted one and
    /// throws the new instance away, so focusing what Build made would focus an element
    /// that is no longer in the tree.
    /// </summary>
    private readonly Ref<VideoPlayerRootElement> _root = new();
    private bool _pendingFocus;

    /// <summary>
    /// Gives the picture the keyboard, so the transport keys work without clicking it
    /// first. An app that opens the player as its own screen calls this when it appears;
    /// one that embeds the player in a page usually should not, since focus belongs to
    /// whatever the person was doing.
    /// </summary>
    public void FocusPicture()
    {
        _pendingFocus = true;
        _focusAttemptsLeft = FocusAttempts;
        MarkDirty();
    }

    /// <summary>
    /// How many builds a pending focus waits for the tree to be ready. Generous because
    /// these are frames, not seconds: the player rebuilds on every position change while
    /// it plays, so the wait is over in one or two.
    /// </summary>
    private const int FocusAttempts = 60;
    private int _focusAttemptsLeft;

    /// <summary>
    /// Focus is taken after a build, and not necessarily the first one.
    ///
    /// <para>A component's first build runs eagerly in its constructor, before anything has
    /// mounted it — and an element that is not in a window yet has no focus manager to ask,
    /// so focusing it there does nothing at all. Retrying on each later build costs nothing
    /// (the player rebuilds as it plays) and lands the moment the tree is real. It is not
    /// retried by marking the component dirty, which would just run the same unmounted
    /// build again in the same breath.</para>
    /// </summary>
    protected internal override void OnAfterBuild()
    {
        if (!_pendingFocus) return;
        if (_root.Current is { } picture && CanTakeFocus(picture))
        {
            _pendingFocus = false;
            picture.Focus();
            return;
        }
        if (--_focusAttemptsLeft <= 0) _pendingFocus = false;
    }

    /// <summary>Whether the element is in a tree that can hold focus — the same walk
    /// <see cref="Element.Focus"/> does, asked before rather than after.</summary>
    private static bool CanTakeFocus(Element element)
    {
        for (var e = element; e is not null; e = e.Parent)
            if (e.FocusManager is not null) return true;
        return false;
    }

    public override Element Build()
    {
        var theme = _theme.Value;
        var player = Player;
        bool playing = player.IsPlaying;
        bool controls = _controlsVisible.Value || !playing;
        float opacity = controls ? (playing ? _barOpacity.Value : 1f) : 0f;

        var root = new VideoPlayerRootElement(this)
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(theme.Backdrop)
            .AccessibleName("Video player").AutomationId("video-player")
            .OnKeyDown(OnKey)
            .OnClick(e =>
            {
                if (e.Handled) return;
                ((Element)e.CurrentTarget).Focus();
                player.TogglePlay();
                Reveal();
            })
            .OnPointerEnter(_ => Reveal())
            .OnPointerLeave(_ => ScheduleHide());
        root.IsFocusable = true;
        // Explicit: a box with a click handler would otherwise be announced as a button.
        root.Role = AccessibilityRole.Group;
        root.On<PointerMoveEvent>(_ => Reveal());
        root.WithRef(_root);

        root.Children(
            Video.New(player).Width(Dimension.Percent(100)).Height(Dimension.Percent(100)),
            Notice(theme, player),
            Bar(theme, player, playing, opacity));
        return root;
    }

    /// <summary>Buffering or failure, over the picture.</summary>
    private static Element Notice(VideoPlayerTheme theme, MediaPlayer player)
    {
        string? text = player.Error is { } error ? $"Cannot play: {error}"
            : player.Buffering is { } fraction ? $"Buffering… {(int)(fraction * 100)}%"
            : player.State == PlaybackState.Opening ? "Opening…"
            : null;

        var overlay = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .AutomationId("video-notice");
        overlay.Position.Type = PositionType.Absolute;
        overlay.Position.Left = Dimension.Px(0);
        overlay.Position.Top = Dimension.Px(0);
        if (text is not null)
        {
            overlay.Children(Box.New().Padding(10f).CornerRadius(6f).Background(theme.Scrim).Children(
                Text.New(text).TextColor(theme.Foreground).FontSize(theme.FontSize + 1)));
        }
        return overlay;
    }

    private Element Bar(VideoPlayerTheme theme, MediaPlayer player, bool playing, float opacity)
    {
        var duration = player.Duration;
        var position = player.Position;

        var bar = Flex.Row(gap: 4)
            .Height(theme.BarHeight).Padding(6f)
            .Background(theme.Scrim)
            .AlignItems(AlignItems.Center)
            .Opacity(opacity)
            .AutomationId("video-controls")
            .FontSize(theme.FontSize).TextColor(theme.Foreground)
            // Clicks on the bar are the bar's; the picture behind it must not toggle play.
            .OnClick(e => { e.Handled = true; e.StopPropagation = true; });
        bar.Position.Type = PositionType.Absolute;
        bar.Position.Left = Dimension.Px(0);
        bar.Position.Bottom = Dimension.Px(0);
        bar.Width(Dimension.Percent(100));
        bar.Role = AccessibilityRole.Group;

        // Children(...) replaces, so the bar is assembled first and set once.
        var items = new List<Element>();

        items.Add(IconButton(theme, playing ? "Pause" : "Play", "video-play",
            playing ? PauseGlyph(theme.Foreground) : Phosphor.Draw("play", 16f, theme.Foreground),
            () => player.TogglePlay()));

        string jump = $"{JumpStep.TotalSeconds:0} s";
        items.Add(IconButton(theme, $"Back {jump}", "video-jump-back", ChevronsGlyph(theme.Foreground, back: true),
            () => player.SeekBy(-JumpStep)));
        items.Add(IconButton(theme, $"Forward {jump}", "video-jump-forward", ChevronsGlyph(theme.Foreground, back: false),
            () => player.SeekBy(JumpStep)));

        items.Add(Text.New(FormatTime(position, duration)).AutomationId("video-elapsed"));

        if (duration is { } total && total > TimeSpan.Zero)
        {
            var scrubber = Slider.New(position.TotalSeconds, 0, total.TotalSeconds)
                .FlexGrow(1)
                .AccessibleName("Seek").AutomationId("video-seek")
                .OnValueChanged(seconds => player.Seek(TimeSpan.FromSeconds(seconds)));
            scrubber.Step = 0.5;
            items.Add(scrubber);
            items.Add(Text.New(FormatTime(total, duration)).TextColor(theme.MutedForeground).AutomationId("video-duration"));
        }
        else
        {
            items.Add(Box.New().FlexGrow(1));
            if (player.HasMedia)
                items.Add(Text.New("LIVE").TextColor(theme.MutedForeground).AutomationId("video-live"));
        }

        items.Add(IconButton(theme, player.Muted ? "Unmute" : "Mute", "video-mute",
            SpeakerGlyph(theme.Foreground, player.Muted), () => player.Muted = !player.Muted));

        var volume = Slider.New(player.Muted ? 0 : player.Volume * 100, 0, 100)
            .Width(80f)
            .AccessibleName("Volume").AutomationId("video-volume")
            .OnValueChanged(v =>
            {
                player.Volume = (float)(v / 100);
                if (player.Muted && v > 0) player.Muted = false;
            });
        items.Add(volume);

        if ((player.Capabilities & BackendCapabilities.Rate) != 0)
        {
            items.Add(TextButton(theme, RateLabel(player.Rate), "Playback speed", "video-rate",
                () => Rates.Select(r => new ContextMenuItem
                {
                    Label = (Math.Abs(r - player.Rate) < 0.001 ? "• " : "   ") + RateLabel(r),
                    Action = () => player.Rate = r,
                }).ToArray()));
        }

        if (player.SubtitleTracks.Count > 0)
        {
            items.Add(TextButton(theme, "CC", "Subtitles", "video-subtitles", () =>
                new[] { TrackItem("Off", null, player.SelectedSubtitleTrack, player.SelectSubtitleTrack) }
                    .Concat(player.SubtitleTracks.Select(t =>
                        TrackItem(t.Title, t.Id, player.SelectedSubtitleTrack, player.SelectSubtitleTrack)))
                    .ToArray()));
        }

        if (player.AudioTracks.Count > 1)
        {
            items.Add(TextButton(theme, "Audio", "Audio track", "video-audio", () =>
                player.AudioTracks.Select(t =>
                    TrackItem(t.Title, t.Id, player.SelectedAudioTrack, player.SelectAudioTrack)).ToArray()));
        }

        bool full = _fullscreen.Value;
        items.Add(IconButton(theme, full ? "Exit full screen" : "Full screen", "video-fullscreen",
            Phosphor.Draw(full ? "arrows-in" : "arrows-out", 16f, theme.Foreground),
            () => FullscreenRequested?.Invoke()));

        bar.Children(items.ToArray());
        return bar;
    }

    private static ContextMenuItem TrackItem(string title, int? id, int? selected, Action<int?> select) => new()
    {
        Label = (id == selected ? "• " : "   ") + title,
        Action = () => select(id),
    };

    // Invariant: a menu reading "0,5×" in one locale and "0.5×" in another is noise, not localisation.
    private static string RateLabel(double rate) => rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "×";

    // ──── Keys ────

    private void OnKey(KeyDownEvent e)
    {
        if (e.Handled) return;
        // A focused slider owns its own arrows; the player only gets what it lets through.
        if (e.Target is SliderElement && e.Key is KeyCode.Left or KeyCode.Right or KeyCode.Up or KeyCode.Down
                or KeyCode.Home or KeyCode.End)
            return;

        var player = Player;
        switch (e.Key)
        {
            case KeyCode.Space or KeyCode.K: player.TogglePlay(); break;
            case KeyCode.Left: player.SeekBy(-SeekStep); break;
            case KeyCode.Right: player.SeekBy(SeekStep); break;
            case KeyCode.J: player.SeekBy(-JumpStep); break;
            case KeyCode.L: player.SeekBy(JumpStep); break;
            case KeyCode.Up: player.Volume += 0.05f; break;
            case KeyCode.Down: player.Volume -= 0.05f; break;
            case KeyCode.M: player.Muted = !player.Muted; break;
            case KeyCode.F: FullscreenRequested?.Invoke(); break;
            case KeyCode.Home: player.Seek(TimeSpan.Zero); break;
            case KeyCode.End when player.Duration is { } end: player.Seek(end); break;
            case >= KeyCode.Digit0 and <= KeyCode.Digit9 when player.Duration is { } total:
                player.Seek(total * ((e.Key - KeyCode.Digit0) / 10.0));
                break;
            default: return;
        }
        e.Handled = true;
        e.StopPropagation = true;
        Reveal();
    }

    // ──── Pieces ────

    private static BoxElement IconButton(VideoPlayerTheme theme, string name, string automationId, Element glyph, Action action)
    {
        var button = Box.New()
            .Width(36f).Height(36f).CornerRadius(6f)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Cursor(CursorType.Pointer)
            .AccessibleName(name).AutomationId(automationId).Tooltip(name)
            .Children(glyph);
        Wire(button, theme, action);
        return button;
    }

    private static BoxElement TextButton(VideoPlayerTheme theme, string label, string name, string automationId,
        Func<IReadOnlyList<ContextMenuItem>> menu)
    {
        var button = Box.New()
            .Height(36f).Padding(8f).CornerRadius(6f)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Cursor(CursorType.Pointer)
            .AccessibleName(name).AutomationId(automationId).Tooltip(name)
            .ContextMenu(menu)
            .Children(Text.New(label).TextColor(theme.MutedForeground));
        // A left click opens the same menu a right click does. The window is the
        // overlay host; a host that is not one (a test double) just has no menu.
        Wire(button, theme, null);
        button.OnClick(e =>
        {
            if (e.CurrentTarget.OverlayHost is Window window)
                window.OpenContextMenu(e.CurrentTarget, e.Position);
        });
        return button;
    }

    private static void Wire(BoxElement button, VideoPlayerTheme theme, Action? action)
    {
        button.Role = AccessibilityRole.Button;
        button.IsFocusable = true;
        button.OnPointerEnter(e => { ((BoxElement)e.CurrentTarget).Background(theme.ButtonHover); e.CurrentTarget.MarkPaintDirty(); });
        button.OnPointerLeave(e => { ((BoxElement)e.CurrentTarget).Background(new Color(0, 0, 0, 0)); e.CurrentTarget.MarkPaintDirty(); });
        if (action is null) return;
        button.OnClick(e =>
        {
            action();
            e.Handled = true;
            e.StopPropagation = true;
        });
        button.OnKeyDown(e =>
        {
            if (e.Key is not (KeyCode.Enter or KeyCode.Space)) return;
            action();
            e.Handled = true;
            e.StopPropagation = true;
        });
    }

    // Canvas coordinates are relative to the canvas: the context adds its own origin.

    private static Element PauseGlyph(Color color) => Canvas.New(ctx =>
    {
        float w = ctx.Bounds.Width, h = ctx.Bounds.Height;
        float bar = w * 0.3f;
        var radius = new CornerRadius(1.5f);
        ctx.DrawRoundedRect(new Rect(w * 0.12f, 0, bar, h), radius, color);
        ctx.DrawRoundedRect(new Rect(w * 0.58f, 0, bar, h), radius, color);
    }).Width(16f).Height(16f);

    /// <summary>Two chevrons, pointing back or forward.</summary>
    private static Element ChevronsGlyph(Color color, bool back) => Canvas.New(ctx =>
    {
        float w = ctx.Bounds.Width, h = ctx.Bounds.Height;
        float span = w * 0.42f;
        for (int i = 0; i < 2; i++)
        {
            float x0 = i * (w - span);
            float tip = back ? x0 : x0 + span;
            float tail = back ? x0 + span : x0;
            ctx.DrawLine(new Point(tail, 0), new Point(tip, h / 2), color, 2f);
            ctx.DrawLine(new Point(tip, h / 2), new Point(tail, h), color, 2f);
        }
    }).Width(18f).Height(14f);

    private static Element SpeakerGlyph(Color color, bool muted) => Canvas.New(ctx =>
    {
        float x = 0, y = 0, w = ctx.Bounds.Width, h = ctx.Bounds.Height;
        // Body and cone.
        ctx.DrawRect(new Rect(x, y + h * 0.33f, w * 0.22f, h * 0.34f), color);
        ctx.DrawLine(new Point(x + w * 0.22f, y + h * 0.33f), new Point(x + w * 0.5f, y + h * 0.1f), color, 2f);
        ctx.DrawLine(new Point(x + w * 0.22f, y + h * 0.67f), new Point(x + w * 0.5f, y + h * 0.9f), color, 2f);
        ctx.DrawLine(new Point(x + w * 0.5f, y + h * 0.1f), new Point(x + w * 0.5f, y + h * 0.9f), color, 2f);
        if (muted)
        {
            ctx.DrawLine(new Point(x + w * 0.62f, y + h * 0.35f), new Point(x + w * 0.95f, y + h * 0.65f), color, 2f);
            ctx.DrawLine(new Point(x + w * 0.62f, y + h * 0.65f), new Point(x + w * 0.95f, y + h * 0.35f), color, 2f);
        }
        else
        {
            ctx.DrawLine(new Point(x + w * 0.66f, y + h * 0.36f), new Point(x + w * 0.66f, y + h * 0.64f), color, 2f);
            ctx.DrawLine(new Point(x + w * 0.84f, y + h * 0.22f), new Point(x + w * 0.84f, y + h * 0.78f), color, 2f);
        }
    }).Width(18f).Height(16f);

    /// <summary>
    /// <c>m:ss</c>, or <c>h:mm:ss</c> when the media is an hour or longer — the whole
    /// bar switches at once, so the elapsed and total columns line up.
    /// </summary>
    public static string FormatTime(TimeSpan time, TimeSpan? total)
    {
        bool hours = (total ?? time) >= TimeSpan.FromHours(1);
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return hours
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }
}

/// <summary>
/// The player's root box. Its only job is to hear the theme pass and hand the
/// <see cref="VideoPlayerTheme"/> to the component, which rebuilds with it.
/// </summary>
internal sealed class VideoPlayerRootElement : BoxElement
{
    private readonly VideoPlayer _owner;

    public VideoPlayerRootElement(VideoPlayer owner) => _owner = owner;

    protected internal override void ApplyTheme(Theme theme)
    {
        base.ApplyTheme(theme);
        _owner.OnTheme(theme.Get<VideoPlayerTheme>());
    }
}
