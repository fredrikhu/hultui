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
/// and K play/pause; Left/Right seek by <see cref="SeekStep"/>, J/L by twice that;
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
    private readonly Signal<bool> _fullscreen = new(false);
    private readonly Signal<VideoPlayerTheme> _theme = new(VideoPlayerTheme.CreateDefault(Theme.Light));
    private TweenHandle _hideTimer;
    private Effect? _playingWatch;

    public VideoPlayer(MediaPlayer player)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
    }

    public MediaPlayer Player { get; }

    /// <summary>Hide the bar while playing after <see cref="AutoHideDelay"/> without pointer movement.</summary>
    public bool AutoHideControls { get; set; } = true;
    public TimeSpan AutoHideDelay { get; set; } = TimeSpan.FromSeconds(2.5);
    /// <summary>How far Left/Right seek; J/L seek twice this.</summary>
    public TimeSpan SeekStep { get; set; } = TimeSpan.FromSeconds(5);

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
            else { _hideTimer.Cancel(); _controlsVisible.Value = true; }
        });
    }

    protected internal override void OnUnmount()
    {
        _hideTimer.Cancel();
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
        _controlsVisible.Value = true;
        ScheduleHide();
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
            .OnComplete(() => { if (Player.IsPlaying) _controlsVisible.Value = false; })
            .Start();
    }

    // ──── Build ────

    public override Element Build()
    {
        var theme = _theme.Value;
        var player = Player;
        bool playing = player.IsPlaying;
        bool controls = _controlsVisible.Value || !playing;

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

        root.Children(
            Video.New(player).Width(Dimension.Percent(100)).Height(Dimension.Percent(100)),
            Notice(theme, player),
            Bar(theme, player, playing, controls));
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

    private Element Bar(VideoPlayerTheme theme, MediaPlayer player, bool playing, bool visible)
    {
        var duration = player.Duration;
        var position = player.Position;

        var bar = Flex.Row(gap: 4)
            .Height(theme.BarHeight).Padding(6f)
            .Background(theme.Scrim)
            .AlignItems(AlignItems.Center)
            .Opacity(visible ? 1f : 0f)
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
            case KeyCode.J: player.SeekBy(-SeekStep * 2); break;
            case KeyCode.L: player.SeekBy(SeekStep * 2); break;
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
