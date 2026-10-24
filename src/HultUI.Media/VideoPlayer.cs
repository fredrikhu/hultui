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
/// start/end; 0–9 jump to that tenth of the media. All without modifiers — an app's Ctrl
/// chords pass through — and listed in <see cref="Shortcuts"/>.</para>
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
    // The bar's level while it fades. A tween writes it and the bar on screen (SetBarOpacity);
    // Build reads it as a plain field, so a fade frame rebuilds nothing.
    private float _barOpacity = 1f;
    private readonly Signal<bool> _fullscreen = new(false);
    private readonly Signal<VideoPlayerTheme> _theme = new(VideoPlayerTheme.CreateDefault(Theme.Light));
    /// <summary>Where the pointer is on the scrubber, in seconds, while it is over it. A plain
    /// field: a move along the track writes the preview card in place (<see cref="ShowPreview"/>).</summary>
    private double? _previewAt;
    /// <summary>
    /// Bumped when the preview card is to come or go — the pointer reaching or leaving the
    /// track, a still arriving for a point that had none — which a build does. The one thing
    /// about the preview <see cref="Build"/> reads. It read the hovered position itself, so the
    /// player was built again on every frame the pointer moved along the track (about 42 KB a
    /// frame, with or without a card).
    /// </summary>
    private readonly Signal<int> _previewComesOrGoes = new(0);
    /// <summary>The card on screen and the two things in it a move changes, or null while no
    /// card shows: <see cref="Build"/> points them at the card it makes and clears them when it
    /// makes none.</summary>
    private readonly Ref<BoxElement> _card = new();
    private readonly Ref<ImageElement> _cardPicture = new();
    private readonly Ref<TextElement> _cardTime = new();
    /// <summary>The time the card reads, so a move that stays on one still formats nothing.</summary>
    private TimeSpan _cardTimeShown;
    /// <summary>Made once: the scrubber is handed the same delegate by every build.</summary>
    private readonly Action<double?> _hoverAt;
    /// <summary>The scrubber itself, so the preview can be placed over the point being
    /// hovered rather than over the middle of the bar.</summary>
    private readonly Ref<SliderElement> _scrubber = new();
    /// <summary>The elapsed time, which a position report rewrites in place.</summary>
    private readonly Ref<TextElement> _elapsed = new();
    /// <summary>The transport bar, whose opacity a fade writes in place.</summary>
    private readonly Ref<BoxElement> _bar = new();
    private TweenHandle _hideTimer;
    private TweenHandle _fade;
    private Effect? _playingWatch;
    private Effect? _positionWatch;

    public VideoPlayer(MediaPlayer player)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        _hoverAt = HoverAt;
    }

    public MediaPlayer Player { get; }

    /// <summary>Hide the bar while playing after <see cref="AutoHideDelay"/> without pointer movement.</summary>
    public bool AutoHideControls { get; set; } = true;
    public TimeSpan AutoHideDelay { get; set; } = TimeSpan.FromSeconds(2.5);
    /// <summary>How far Left/Right seek.</summary>
    public TimeSpan SeekStep { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>How far the jump buttons, and J/L, seek.</summary>
    public TimeSpan JumpStep { get => _jumpStep.Value; set => _jumpStep.Value = value; }

    /// <summary>
    /// The player's keys, for an app's help or legend to name. Matched exactly: they used to
    /// ignore modifiers, so the focused player took Ctrl+F, Ctrl+L and the rest of an app's
    /// chords as its own letters.
    /// </summary>
    public static ShortcutMap Shortcuts { get; } = BuildShortcuts();

    private static ShortcutMap BuildShortcuts()
    {
        static KeyChord K(KeyCode key) => new(key);
        var map = new ShortcutMap();
        const string group = "Player";
        map.Add("play", group, "Play or pause", K(KeyCode.Space), K(KeyCode.K));
        map.Add("back", group, "Back a little", K(KeyCode.Left));
        map.Add("forward", group, "Forward a little", K(KeyCode.Right));
        map.Add("jumpBack", group, "Jump back", K(KeyCode.J));
        map.Add("jumpForward", group, "Jump forward", K(KeyCode.L));
        map.Add("louder", group, "Louder", K(KeyCode.Up));
        map.Add("quieter", group, "Quieter", K(KeyCode.Down));
        map.Add("mute", group, "Mute", K(KeyCode.M));
        map.Add("fullscreen", group, "Full screen", K(KeyCode.F));
        map.Add("start", group, "To the start", K(KeyCode.Home));
        map.Add("end", group, "To the end", K(KeyCode.End));
        map.Add("tenth", group, "To that tenth of the media",
            Enumerable.Range(0, 10).Select(n => new KeyChord(KeyCode.Digit0 + (ushort)n)).ToArray()).Shown("0 – 9");
        return map;
    }
    /// <summary>How long the bar takes to fade out once the pointer has been still.</summary>
    public TimeSpan FadeDuration { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Asked for a still to show while the scrubber is hovered or dragged, at the position
    /// under the pointer. Null — the default, or a null answer — means no preview: the
    /// player shows the time alone, which is what it always did.
    ///
    /// <para>Called once per frame drawn while the pointer moves along the track, and during
    /// a build, so it must be cheap and must not block. An app backed by a server answers from
    /// a cache and starts the fetch on a miss, so the picture appears a frame or two later
    /// rather than holding the frame.</para>
    /// </summary>
    public Func<TimeSpan, VideoPreviewFrame?>? PreviewFrame { get; set; }

    /// <summary>How wide the preview still is drawn. Its height follows the frame.</summary>
    public float PreviewWidth { get; set; } = 176f;

    /// <summary>
    /// Where the chapters start, which cuts the scrubber into one piece per chapter. Empty
    /// — the default — leaves it one continuous track. The player does not know what a
    /// chapter is beyond this: the app hands over the marks it has.
    /// </summary>
    public IReadOnlyList<TimeSpan> ChapterMarks
    {
        get => _chapterMarks;
        set
        {
            _chapterMarks = value ?? [];
            // Converted once per change rather than once per build: the player rebuilds on
            // every show, hide and hover of the bar, and this list only moves when the film does.
            _chapterSeconds = _chapterMarks.Count > 0
                ? _chapterMarks.Select(c => c.TotalSeconds).ToArray()
                : [];
            MarkDirty();
        }
    }
    private IReadOnlyList<TimeSpan> _chapterMarks = [];
    private double[] _chapterSeconds = [];

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
        // A position report, ten a second from mpv while playing, moves the elapsed time and the
        // scrubber's thumb and nothing else, so it writes those two on the mounted elements and
        // Build reads the position untracked. It used to rebuild the whole player, about 22
        // elements and 41 KB a report, with the bar shown or hidden.
        _positionWatch = new Effect(ShowPosition);
    }

    /// <summary>
    /// The position, on the elements that show it: the elapsed time and the scrubber. Reads
    /// <see cref="MediaPlayer.Position"/> and <see cref="MediaPlayer.Duration"/>, so it runs
    /// again when either moves. The scrubber's value tells a reader where it is, at most every
    /// 250 ms (<see cref="AccessibilityProperties.RangeValue"/>), as the rebuilt one did.
    /// </summary>
    private void ShowPosition()
    {
        var position = Player.Position;
        var duration = Player.Duration;
        if (_elapsed.Current is { } elapsed) elapsed.Content = FormatTime(position, duration);
        if (_scrubber.Current is { } scrubber) scrubber.Value = position.TotalSeconds;
    }

    protected internal override void OnUnmount()
    {
        _hideTimer.Cancel();
        _fade.Cancel();
        _playingWatch?.Dispose();
        _playingWatch = null;
        _positionWatch?.Dispose();
        _positionWatch = null;
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
        float from = _barOpacity;
        if (from >= 1f) return;
        // Coming back is quick — half the fade-out — so the pointer never waits on it.
        _fade = Animate.Value(from, 1f, SetBarOpacity)
            .Duration((float)FadeDuration.TotalSeconds * 0.5f)
            .Start();
    }

    private void ScheduleHide()
    {
        _hideTimer.Cancel();
        if (!AutoHideControls) return;
        // A delayed tween is the framework's timer: it runs on the frame loop's clock, which
        // wakes once for it when the delay is out, and cancels cleanly.
        _hideTimer = Animate.After((float)AutoHideDelay.TotalSeconds, FadeOut);
    }

    private void FadeOut()
    {
        if (!Player.IsPlaying) return;
        _fade.Cancel();
        _fade = Animate.Value(_barOpacity, 0f, SetBarOpacity)
            .Duration((float)FadeDuration.TotalSeconds)
            .OnComplete(() => _controlsVisible.Value = false)
            .Start();
    }

    /// <summary>
    /// A step of a fade: the level, and the bar on screen at it — a repaint, nothing more. The
    /// level was a signal Build read, so every frame of a fade rebuilt the player (Reel: 166
    /// rebuilds in 12 s, 38 KB each, three quarters of what its UI thread allocated). Where the
    /// fade starts and ends, <see cref="_controlsVisible"/> still rebuilds the player, which
    /// takes the level from here.
    /// </summary>
    private void SetBarOpacity(float level)
    {
        _barOpacity = level;
        // Read outside any effect: the tween steps from the frame loop.
        if (_bar.Current is { } bar)
            bar.Opacity = BarOpacity(_controlsVisible.Peek(), Player.IsPlaying);
    }

    /// <summary>The bar's opacity: gone while hidden, whole while paused (the fade runs only
    /// while playing), otherwise the fade's level.</summary>
    private float BarOpacity(bool controlsVisible, bool playing) =>
        controlsVisible || !playing ? (playing ? _barOpacity : 1f) : 0f;

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
    /// these are builds, not seconds: the player rebuilds for its theme once mounted, as its
    /// file opens, plays and pauses, and as the bar shows and hides.
    /// </summary>
    private const int FocusAttempts = 60;
    private int _focusAttemptsLeft;

    /// <summary>
    /// Focus is taken after a build, and not necessarily the first one.
    ///
    /// <para>A component's first build runs eagerly in its constructor, before anything has
    /// mounted it — and an element that is not in a window yet has no focus manager to ask,
    /// so focusing it there does nothing at all. Retrying on each later build costs nothing
    /// (the first comes a frame after mounting, when the root hands the player its theme) and
    /// lands the moment the tree is real. It is not retried by marking the component dirty,
    /// which would just run the same unmounted build again in the same breath.</para>
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

    /// <summary>How many times <see cref="Build"/> has run: what the tests and the perf suite
    /// count when they check what a position report, a fade frame or a scrubber hover costs.</summary>
    internal int Builds { get; private set; }

    /// <summary>The position the scrubber is hovered at, for the tests and the perf suite.</summary>
    internal double? PreviewAt => _previewAt;

    public override Element Build()
    {
        Builds++;
        var theme = _theme.Value;
        var player = Player;
        bool playing = player.IsPlaying;
        bool controls = _controlsVisible.Value || !playing;
        float opacity = BarOpacity(controls, playing);

        // Read before building: WithRef points a ref at the element just created, whose
        // layout has not been computed yet, so the scrubber's geometry has to be taken
        // from the frame that has actually been laid out — this one's predecessor.
        var trackBox = _scrubber.Current?.Layout.AbsoluteContentBox;
        var rootBox = _root.Current?.Layout.AbsoluteContentBox;

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
            Bar(theme, player, playing, opacity),
            Preview(theme, controls, trackBox, rootBox, player.Duration));
        return root;
    }

    /// <summary>
    /// The still for the point being hovered on the scrubber, floating just above the bar
    /// and centred on that point. Empty when nothing is hovered, when the bar is hidden,
    /// or when the app has no frame for that second yet — a preview that popped up blank
    /// while its tile downloaded would flicker on every pass along the track.
    ///
    /// <para>Built when the card comes or goes. While the pointer moves along the track the
    /// card on screen is moved and given its new still and time in place
    /// (<see cref="ShowPreview"/>).</para>
    /// </summary>
    private Element Preview(VideoPlayerTheme theme, bool controls, Rect? trackBox, Rect? rootBox,
        TimeSpan? duration)
    {
        // Full width: the card is placed inside it by its own Left, and a host with no
        // width would offer its children none — the label then wraps one character per
        // line and the card cannot be positioned along the track.
        var host = Box.New().Width(Dimension.Percent(100)).AutomationId("video-preview");
        host.Position.Type = PositionType.Absolute;
        host.Position.Left = Dimension.Px(0);
        host.Position.Bottom = Dimension.Px(theme.BarHeight + 8f);
        host.IsHitTestVisible = false;

        // Read so that a card coming or going rebuilds the player; the hovered position is not.
        _ = _previewComesOrGoes.Value;
        if (PreviewFor(_previewAt, controls) is not { } shown)
        {
            _card.Current = null;
            _cardPicture.Current = null;
            _cardTime.Current = null;
            return host;
        }
        var (frame, seconds) = shown;

        float width = PreviewWidth;
        // The frame's own shape at the preview's width; 16:9 for a frame that has not said.
        var picture = HultUI.Core.Builders.Image.New(frame.Image).Width(width).AspectRatio(PreviewShape(frame));
        picture.SourceRect = frame.Source;
        picture.WithRef(_cardPicture);

        var card = Flex.Column(gap: 2)
            .Width(width + 8f)
            .Padding(4f).CornerRadius(6f).Background(theme.Scrim)
            .AlignItems(AlignItems.Center)
            // A group with a name, not a bare box: a reader scrubbing by keyboard is told
            // the preview is there, and the time inside it is the one node worth reading.
            .AccessibleRole(AccessibilityRole.Group)
            .AccessibleName("Preview")
            .AutomationId("video-preview-card");
        card.AddChild(picture);
        _cardTimeShown = frame.Time ?? TimeSpan.FromSeconds(seconds);
        card.AddChild(Text.New(FormatTime(_cardTimeShown, _cardTimeShown))
            .FontSize(theme.FontSize).TextColor(theme.Foreground).MaxLines(1).WithRef(_cardTime));

        card.Position.Type = PositionType.Absolute;
        card.Position.Left = Dimension.Px(PreviewLeft(seconds, width + 8f, trackBox, rootBox, duration));
        card.Position.Bottom = Dimension.Px(0);
        card.WithRef(_card);
        host.AddChild(card);
        return host;
    }

    /// <summary>The still to show for a hovered position, or null when there is none to show:
    /// nothing hovered, the bar hidden, no <see cref="PreviewFrame"/>, or no loaded frame from it.</summary>
    private (VideoPreviewFrame Frame, double Seconds)? PreviewFor(double? hovered, bool controls)
    {
        if (!controls || hovered is not { } seconds || PreviewFrame is not { } ask) return null;
        if (ask(TimeSpan.FromSeconds(seconds)) is not { } frame || !frame.Image.IsLoaded) return null;
        return (frame, seconds);
    }

    /// <summary>The still's own width to height, its crop's under a sprite sheet; 16:9 for a
    /// frame that has not said.</summary>
    private static float PreviewShape(VideoPreviewFrame frame)
    {
        var size = frame.Source is { } crop ? new Size(crop.Width, crop.Height) : frame.Image.IntrinsicSize;
        return size.Width > 0 && size.Height > 0 ? size.Width / size.Height : 16f / 9f;
    }

    /// <summary>
    /// Where the card's left edge goes: centred on the hovered point, kept inside the picture —
    /// a preview half off the left edge is the one place a scrubber preview is always looked at
    /// (the start). The scrubber's and the root's boxes are the last laid-out frame's.
    /// </summary>
    private static float PreviewLeft(double seconds, float cardWidth, Rect? trackBox, Rect? rootBox, TimeSpan? duration)
    {
        float track = trackBox?.Width ?? 0f;
        float rootWidth = rootBox?.Width ?? 0f;
        double total = duration?.TotalSeconds ?? 0;
        double t = total > 0 ? Math.Clamp(seconds / total, 0, 1) : 0;
        // Before the first frame has laid the track out there is nothing to follow, so the
        // card starts in the middle rather than in the corner.
        float centre = track > 0
            ? (trackBox!.Value.X - (rootBox?.X ?? 0f)) + (float)(t * track)
            : rootWidth / 2f;
        float left = centre - cardWidth / 2f;
        if (rootWidth > cardWidth) left = Math.Clamp(left, 4f, rootWidth - cardWidth - 4f);
        return MathF.Max(0f, left);
    }

    /// <summary>The scrubber's report of the position under the pointer: once per frame drawn
    /// while it moves along the track (or drags), and null when it leaves.</summary>
    private void HoverAt(double? seconds)
    {
        _previewAt = seconds;
        ShowPreview();
    }

    /// <summary>
    /// The card for the hovered position, without building the player: a card already on
    /// screen is moved and given its new still and time in place. Only a card that has to come
    /// or go goes through a build, which reads the same position. A hidden bar shows no card,
    /// and its return is a build of its own.
    /// </summary>
    private void ShowPreview()
    {
        // Untracked: the provider and the player's state are read here outside any build, and
        // an effect that ends up calling this must not come to depend on them.
        var previous = ReactiveScope.SetSubscriber(null);
        try
        {
            bool controls = _controlsVisible.Peek() || !Player.IsPlaying;
            var shown = PreviewFor(_previewAt, controls);
            if (shown.HasValue != _card.Current is not null)
            {
                _previewComesOrGoes.Value++;
                return;
            }
            if (shown is { } card) WriteCard(card.Frame, card.Seconds);
        }
        finally
        {
            ReactiveScope.SetSubscriber(previous);
        }
    }

    /// <summary>What a build would have made of the card, written on the one on screen: each
    /// setter marks only what it changes, and nothing changes while the pointer stays on one
    /// still but the card's place.</summary>
    private void WriteCard(VideoPreviewFrame frame, double seconds)
    {
        if (_card.Current is not { } card || _cardPicture.Current is not { } picture || _cardTime.Current is not { } time)
            return;

        float width = PreviewWidth;
        var pictureWidth = Dimension.Px(width);
        var cardWidth = Dimension.Px(width + 8f);
        if (picture.Box.Width != pictureWidth || card.Box.Width != cardWidth)
        {
            picture.Box.Width = pictureWidth;
            card.Box.Width = cardWidth;
            picture.MarkLayoutDirty();
            card.MarkLayoutDirty();
        }
        picture.Source = frame.Image;
        picture.SourceRect = frame.Source;
        float shape = PreviewShape(frame);
        if (picture.Box.AspectRatio != shape)
        {
            picture.Box.AspectRatio = shape;
            picture.MarkLayoutDirty();
        }

        var at = frame.Time ?? TimeSpan.FromSeconds(seconds);
        if (at != _cardTimeShown)
        {
            _cardTimeShown = at;
            time.Content = FormatTime(at, at);
        }

        var left = Dimension.Px(PreviewLeft(seconds, width + 8f, _scrubber.Current?.Layout.AbsoluteContentBox,
            _root.Current?.Layout.AbsoluteContentBox, Player.Duration));
        if (card.Position.Left != left)
        {
            card.Position.Left = left;
            card.MarkLayoutDirty();
        }
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
        // Untracked: a position report rewrites the two elements that show it (ShowPosition)
        // instead of rebuilding the player.
        var position = ReactiveScope.Untracked(() => player.Position);

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
        bar.WithRef(_bar);

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

        items.Add(Text.New(FormatTime(position, duration)).AutomationId("video-elapsed").WithRef(_elapsed));

        if (duration is { } total && total > TimeSpan.Zero)
        {
            var scrubber = Slider.New(position.TotalSeconds, 0, total.TotalSeconds)
                .FlexGrow(1)
                .AccessibleName("Seek").AutomationId("video-seek")
                .OnValueChanged(seconds => player.Seek(TimeSpan.FromSeconds(seconds)));
            scrubber.OnHoverValue = _hoverAt;
            scrubber.Step = 0.5;
            // Pointing at a scrubber is asking "what is there" — so the track fills to the
            // pointer, and the chapters cut it into the pieces being aimed at.
            scrubber.ShowHoverFill = true;
            if (_chapterSeconds.Length > 0) scrubber.Segments = _chapterSeconds;
            scrubber.WithRef(_scrubber);
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
                    Label = RateLabel(r),
                    IsChecked = Math.Abs(r - player.Rate) < 0.001,
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
        Label = title,
        IsChecked = id == selected,
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
        switch (Shortcuts.Find(e)?.Id)
        {
            case "play": player.TogglePlay(); break;
            case "back": player.SeekBy(-SeekStep); break;
            case "forward": player.SeekBy(SeekStep); break;
            case "jumpBack": player.SeekBy(-JumpStep); break;
            case "jumpForward": player.SeekBy(JumpStep); break;
            case "louder": player.Volume += 0.05f; break;
            case "quieter": player.Volume -= 0.05f; break;
            case "mute": player.Muted = !player.Muted; break;
            case "fullscreen": FullscreenRequested?.Invoke(); break;
            case "start": player.Seek(TimeSpan.Zero); break;
            case "end" when player.Duration is { } end: player.Seek(end); break;
            case "tenth" when e.Digit is { } digit && player.Duration is { } total:
                player.Seek(total * (digit / 10.0));
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

    // A box that copies nothing more than a box does, so a patch that moves nothing leaves its
    // layout standing, as a box's.
    private protected override bool PatchMarksOwnLayout => GetType() == typeof(VideoPlayerRootElement);

    protected internal override void ApplyTheme(Theme theme)
    {
        base.ApplyTheme(theme);
        _owner.OnTheme(theme.Get<VideoPlayerTheme>());
    }
}
