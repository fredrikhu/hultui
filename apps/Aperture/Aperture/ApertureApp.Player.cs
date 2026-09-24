using Aperture.Core;
using Aperture.Theme;
using HultUI;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Media;

namespace Aperture;

/// <summary>
/// Playback: opening a stream, keeping the server told where we are in it, and putting
/// the picture on screen as a sheet over whatever you were looking at.
///
/// <para>The session bookkeeping is the part that makes this a Jellyfin client rather
/// than a video player pointed at a URL. Three reports matter — started, progress,
/// stopped — and between them they drive the dashboard's "now playing", the Continue
/// watching row, and the resume point on every other device. They are sent on every
/// state change and every ten seconds, and the last one is sent even when the app is
/// closing, because a film that keeps its place is the whole promise.</para>
/// </summary>
public sealed partial class ApertureApp
{
    private MediaPlayer? _player;
    private VideoPlayer? _videoPlayer;

    /// <summary>The session the server opened for what is playing, or null when nothing is.</summary>
    private PlaybackTarget? _playback;
    private MediaItem? _playingItem;

    /// <summary>Where the stream should start. mpv cannot be told to open at a position
    /// (the URL streams the file from its first byte), so the seek is issued once the
    /// backend reports it has loaded — see <see cref="ApplyResumeSeek"/>.</summary>
    private TimeSpan _resumeTo;
    private bool _resumeApplied;

    /// <summary>When the last progress report went out, and what it said. Both are needed:
    /// the clock paces the heartbeat, and the state decides whether a change is worth an
    /// immediate report.</summary>
    private long _lastReportTicks;
    private bool _lastReportedPaused;

    /// <summary>The heartbeat Jellyfin's own clients keep.</summary>
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(10);

    /// <summary>The last stop report, so shutting down can wait for it.</summary>
    private Task<ApertureResult<bool>>? _stopReport;

    /// <summary>How long a closing window waits for that report. Long enough for a LAN
    /// round trip, short enough that a server which has gone away does not hold the
    /// window open.</summary>
    private static readonly TimeSpan StopReportGrace = TimeSpan.FromSeconds(2);

    /// <summary>Waits out the final stop report, if one is in flight. Called from
    /// <see cref="Dispose"/>, which runs on the UI thread as the window closes — there is
    /// no frame left to await on, so this blocks, briefly and boundedly.</summary>
    internal void WaitForStopReport()
    {
        var report = _stopReport;
        _stopReport = null;
        try { report?.Wait(StopReportGrace); }
        catch (AggregateException) { /* the report failed; the resume point is the server's problem now */ }
    }

    /// <summary>Non-null while the player sheet is up, for the tests.</summary>
    internal MediaPlayer? Player => _player;
    internal PlaybackTarget? Playback => _playback;
    internal MediaItem? PlayingItem => _playingItem;

    /// <summary>The ceiling to ask the server for, in bits per second, or null for the
    /// file as it is. Kept in kilobits where a person reads it and converted here.</summary>
    internal int? MaxStreamingBitrateBps =>
        _settings.Data.MaxStreamingKbps is > 0 ? _settings.Data.MaxStreamingKbps * 1000 : null;

    /// <summary>
    /// Changes the ceiling. What is playing was fetched under the old one, so it is
    /// re-opened at the position it had reached — a quality control that only takes effect
    /// next time is a quality control nobody trusts.
    /// </summary>
    internal void SetMaxStreamingKbps(int? kbps)
    {
        if (_settings.Data.MaxStreamingKbps == kbps) return;
        _settings.Data.MaxStreamingKbps = kbps;
        _settings.Save();

        if (_playingItem is { } item)
        {
            var at = _player?.Position ?? TimeSpan.Zero;
            Play(item, at, countsAsDeliberate: false, keepQueue: true);
        }
        Rerender();
    }

    /// <summary>
    /// Plays an item from a position. Asks the server how it can be played first: that
    /// call is what produces the play-session id, and it is also where a server decides
    /// to transcode — both of which the UI then shows.
    /// </summary>
    /// <param name="countsAsDeliberate">A person pressed play, so the pass-out counter
    /// starts over. False when the countdown started it.</param>
    /// <param name="keepQueue">The queue is driving this. Anything else ends it: a queue
    /// that survived someone picking a different film would follow them around.</param>
    internal void Play(MediaItem item, TimeSpan from, bool countsAsDeliberate = true, bool keepQueue = false)
    {
        if (_library is not { } lib) return;
        if (_backends is null)
        {
            Flash("No libmpv on this machine — see src/HultUI.Media.Mpv/README.md", ToastSeverity.Error);
            return;
        }
        if (!item.IsPlayable)
        {
            // A series or a season: open it instead of refusing. Clicking a shelf should
            // never do nothing.
            Open(item);
            return;
        }

        if (countsAsDeliberate) _autoPlayedInARow = 0;
        if (!keepQueue) ClearQueue();
        DismissUpNext();
        StopPlayback(report: true, keepSheet: true);
        _playingItem = item;
        _resumeTo = from;
        _resumeApplied = from <= TimeSpan.Zero;
        Rerender();

        _ = StartAsync(lib, item, from);
    }

    private async Task StartAsync(IMediaLibrary lib, MediaItem item, TimeSpan from)
    {
        var target = await lib.StartPlaybackAsync(item.Id, from, MaxStreamingBitrateBps).ConfigureAwait(false);
        Application.InvokeOnMainThread(() =>
        {
            if (!target.Ok)
            {
                _playingItem = null;
                FlashFailure(target.Failure!, item.Name);
                Rerender();
                return;
            }
            // The user may have pressed Escape while the server was thinking.
            if (_playingItem?.Id != item.Id) return;

            _playback = target.Value;
            OpenStream(target.Value);
            // The stills and the chapter marks live on the full item, which a shelf card
            // is not — fetch it behind the picture.
            EnsurePlayingDetail(lib, item);
            if (target.Value.Transcoding)
                Flash($"{lib.Server.Name} is transcoding this one", ToastSeverity.Warning);
        });
    }

    private void OpenStream(PlaybackTarget target)
    {
        _player?.Dispose();
        _player = new MediaPlayer(_backends!());
        _player.Volume = _settings.Data.Volume;
        _player.Muted = _settings.Data.Muted;

        _videoPlayer = new VideoPlayer(_player)
        {
            JumpStep = TimeSpan.FromSeconds(10),
            PreviewWidth = PreviewWidth,
            PreviewFrame = TrickplayFrame,
        };
        _videoPlayer.FullscreenRequested += ToggleFullscreen;
        // The headless driver pumps frames far faster than wall time, so the transport bar
        // would hide before any shot of it playing.
        if (Environment.GetEnvironmentVariable("HULTUI_MEDIA_NOHIDE") is "1")
            _videoPlayer.AutoHideControls = false;

        // Play() straight after Open() is a no-op with mpv — the backend ignores commands
        // until the file has loaded — so the autoPlay flag is what starts it.
        _player.Open(MediaSource.FromUri(target.StreamUrl), autoPlay: true);
        // The picture takes the keyboard as it opens, so the transport keys — seek, jump,
        // chapters, Home/End — work without clicking the picture first. Escape, Space and
        // F still reach the app's own handler, because the overlay leaves them alone.
        _videoPlayer.FocusPicture();
        ForgetPlaybackEnding();
        ForgetTrackSelection();
        _lastReportTicks = 0;
        Rerender();
    }

    /// <summary>
    /// Stops whatever is playing and tells the server where it got to. Called on Escape,
    /// on starting something else, on sign-out and on dispose — so it has to be safe when
    /// nothing is playing.
    /// </summary>
    /// <param name="keepSheet">True when another item is about to open: the sheet stays,
    /// so starting the next episode does not flash the library between the two.</param>
    internal void StopPlayback(bool report, bool keepSheet = false)
    {
        if (!keepSheet) DismissUpNext();
        var target = _playback;
        var position = _player?.Position ?? TimeSpan.Zero;

        if (_player is { } player)
        {
            _settings.Data.Volume = player.Volume;
            _settings.Data.Muted = player.Muted;
            _settings.Save();
            player.Dispose();
        }
        _player = null;
        _videoPlayer = null;
        _playback = null;
        _playingItem = null;
        _resumeApplied = false;

        if (!keepSheet) ClearQueue();

        // Full screen was for the film. Coming back to a library that fills the display
        // with no way to reach the task bar is a trap, so leaving playback leaves it.
        SetFullscreen(false);

        if (report && target is not null && _library is { } lib)
        {
            // Kept, not abandoned: this report is what fixes the resume point, and the
            // common way to stop a film is to close the window — so Dispose waits on it.
            _stopReport = lib.ReportStoppedAsync(target, position);
            // What was watched changed, so the home rows are stale.
            _dataGeneration++;
        }
        Rerender();
    }

    /// <summary>
    /// Borderless fullscreen — the window covers the display, the app stops drawing its
    /// title bar, and the screen mode is untouched. Maximizing is not the same thing: it
    /// leaves the task bar over the picture and the title bar above it, which is what this
    /// used to do and what made full screen feel like a bigger window.
    /// </summary>
    private void ToggleFullscreen() => SetFullscreen(!_window.IsFullscreen);

    private void SetFullscreen(bool fullscreen)
    {
        if (_window.IsFullscreen == fullscreen) return;
        _window.SetFullscreen(fullscreen);
        // The overlay draws the enter/exit glyph off its own flag, so it has to be told.
        if (_videoPlayer is not null) _videoPlayer.IsFullscreen = fullscreen;
        Rerender();
    }

    // ── Reporting ────────────────────────────────────────────────────────

    /// <summary>
    /// Called from the sheet's build — which runs on every frame while a film plays,
    /// because the transport bar reads the position signal. Two things come out of it: the
    /// resume seek once the backend is ready, and the progress heartbeat.
    /// </summary>
    private void PumpPlayback()
    {
        if (_player is not { } player || _playback is not { } target || _library is not { } lib) return;

        ApplyResumeSeek(player);
        PumpTrackPreference(player);

        // The file ran out: report it as a stop at the end (which is what marks it
        // watched), then ask what comes next.
        if (player.State == PlaybackState.Ended && _upNext is null)
        {
            OnPlaybackEnded();
        }

        bool paused = !player.IsPlaying;
        long now = Environment.TickCount64;
        bool first = _lastReportTicks == 0;
        bool stateChanged = !first && paused != _lastReportedPaused;
        bool due = !first && now - _lastReportTicks >= (long)ReportInterval.TotalMilliseconds;

        // Nothing is reported until the backend has actually started: a report at
        // position zero from a file that has not loaded would move the resume point to
        // the beginning of a film someone is half-way through.
        if (first && player.State is PlaybackState.NoMedia or PlaybackState.Opening) return;
        if (!first && !stateChanged && !due) return;

        _lastReportTicks = now;
        _lastReportedPaused = paused;
        var position = player.Position;

        if (first) _ = lib.ReportStartedAsync(target, position, paused);
        else _ = lib.ReportProgressAsync(target, position, paused);
    }

    /// <summary>
    /// Seeks to the resume point once there is something to seek in. The backend ignores
    /// a seek before the file is loaded, and a loaded file is exactly what a known
    /// duration means.
    /// </summary>
    private void ApplyResumeSeek(MediaPlayer player)
    {
        if (_resumeApplied) return;
        if (player.State is PlaybackState.NoMedia or PlaybackState.Opening) return;
        if (player.Duration is not { Ticks: > 0 }) return;

        _resumeApplied = true;
        if (_resumeTo > TimeSpan.Zero) player.Seek(_resumeTo);
    }

    // ── The sheet ────────────────────────────────────────────────────────

    private Element BuildPlayerSheet()
    {
        PumpPlayback();

        var sheet = Box.New()
            .Size(Dimension.Percent(100), Dimension.Percent(100))
            .Background(Halide.Bg)
            .AlignItems(AlignItems.Stretch)
            .AutomationId("player");
        sheet.Position.Type = PositionType.Absolute;
        sheet.Position.Left = Dimension.Px(0);
        sheet.Position.Top = Dimension.Px(0);
        sheet.ZIndex(40);

        var column = Flex.Column(gap: 0).Size(Dimension.Percent(100), Dimension.Percent(100));

        if (_videoPlayer is null)
        {
            column.AddChild(PlayerWaiting());
        }
        else
        {
            // The marks arrive with the full item, a moment after playback starts, so this
            // is set per build rather than once when the stream opens.
            if (_videoPlayer.ChapterMarks.Count != PlayingChapters.Count)
                _videoPlayer.ChapterMarks = [.. PlayingChapters.Select(c => c.Start)];

            var picture = _videoPlayer.Mount().FlexGrow(1);
            picture.Key = "picture";
            column.AddChild(picture);
        }

        // The title strip rides over the picture's top edge. It follows the transport
        // bar's own visibility so a still frame is never permanently captioned.
        if (_videoPlayer is null || _videoPlayer.ControlsVisible)
            column.AddChild(PlayerTitleStrip());

        if (_upNext is { } next)
        {
            var card = BuildUpNextCard(next);
            card.Key = "up-next";
            column.AddChild(card);
        }

        // One panel at a time: they occupy the same side, and stacking them would hide
        // the picture entirely.
        if (_chaptersOpen && PlayingChapters.Count > 0)
        {
            var panel = BuildChapterPanel();
            panel.Key = "chapters";
            column.AddChild(panel);
        }
        else if (_queueOpen && QueueIsRunning)
        {
            var panel = BuildQueuePanel();
            panel.Key = "queue";
            column.AddChild(panel);
        }

        sheet.AddChild(column);
        sheet.On<KeyDownEvent>(e =>
        {
            if (e.Key == KeyCode.Escape)
            {
                StopPlayback(report: true);
                e.Handled = true;
            }
        });
        return sheet;
    }

    /// <summary>Between pressing Play and the first frame: the server is being asked how,
    /// then mpv is opening the stream. Both can take a second over a LAN.</summary>
    private Element PlayerWaiting()
    {
        var column = Flex.Column(gap: Halide.S3).FlexGrow(1)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .AutomationId("player-waiting");
        column.AddChild(Ui.Icon(Icons.Loading, 24f, Halide.Accent));
        column.AddChild(Text.New(_playingItem is { } item ? $"Opening {item.Name}…" : "Opening…")
            .FontSize(14f).TextColor(Halide.TextDim));
        column.AddChild(Text.New("Escape stops").FontSize(11.5f).TextColor(Halide.TextFaint));
        return column;
    }

    private Element PlayerTitleStrip()
    {
        var strip = Flex.Row(gap: Halide.S3).AlignItems(AlignItems.Center)
            .Height(56).FlexShrink(0)
            .Padding(new Thickness(0, Halide.S4, 0, Halide.S3))
            .Background(Halide.Alpha(Halide.Bg, 0.82));
        strip.Position.Type = PositionType.Absolute;
        strip.Position.Left = Dimension.Px(0);
        strip.Position.Top = Dimension.Px(0);
        strip.Box.Width = Dimension.Percent(100);

        strip.AddChild(Ui.IconButton(Icons.Back, "Stop (Esc)", () => StopPlayback(report: true),
            16f, Halide.Text, "player-back"));

        var titles = Flex.Column(gap: 1).FlexGrow(1).FlexShrink(1);
        if (_playingItem is { } item)
        {
            titles.AddChild(Ui.Line(item.Name, 14f, Halide.Text));
            var sub = item.Kind == MediaKind.Episode && item.SeriesName is { Length: > 0 } series
                ? $"{series} · {item.SubLine}"
                : item.SubLine;
            if (sub.Length > 0) titles.AddChild(Ui.Line(sub, 11.5f, Halide.TextMuted));
        }
        strip.AddChild(titles);

        if (QueueIsRunning)
            strip.AddChild(Ui.IconButton(Icons.Episodes, $"Queue — {_queueIndex + 1} of {_queue.Count} (Q)",
                ToggleQueue, 15f, _queueOpen ? Halide.Accent : Halide.TextDim, "player-queue"));

        if (PlayingChapters.Count > 0)
            strip.AddChild(Ui.IconButton(Icons.Episodes, $"Chapters ({PlayingChapters.Count}) — C",
                ToggleChapters, 15f, _chaptersOpen ? Halide.Accent : Halide.TextDim, "player-chapters"));

        // What the server is actually doing with the stream: a direct play and a transcode
        // feel identical until something stutters, and then it is the first question.
        if (_playback is { } target)
            strip.AddChild(Ui.Chip(target.Transcoding ? "Transcoding" : "Direct play",
                target.Transcoding ? Halide.StatusWarn : Halide.StatusOk));

        if (_player?.Error is { Length: > 0 } error)
            strip.AddChild(Ui.Chip(error, Halide.StatusFail));

        strip.AccessibleRole(AccessibilityRole.Group);
        strip.AccessibleName(_playingItem is { } playing ? "Playing " + playing.Name : "Player");
        return strip;
    }
}
