using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Headless;
using HultUI.Media.Testing;
using Xunit;

namespace HultUI.Media.Tests;

/// <summary>
/// The transport bar under the headless host: its buttons drive the player, its keys
/// do what the docs say, the time reads right, and the bar hides while playing and
/// comes back when the pointer moves.
/// </summary>
public class VideoPlayerTests
{
    private static readonly MediaSource Clip = MediaSource.FromFile("clip.mp4");

    private sealed class Fixture : IDisposable
    {
        public HeadlessSession Session { get; }
        public FakeMediaBackend Backend { get; } = new();
        public MediaPlayer Player { get; }
        public VideoPlayer VideoPlayer { get; }

        public Fixture(bool open = true)
        {
            Session = HeadlessSession.Create(640, 400);
            Player = new MediaPlayer(Backend);
            VideoPlayer = new VideoPlayer(Player);
            Session.Application.SetRoot(Box.New().Children(VideoPlayer.Mount().Width(640f).Height(400f)));
            if (open) Player.Open(Clip);
            Session.PumpFrames(2);
        }

        public HeadlessElement Button(string name) =>
            Session.Window.FindByRole(AccessibilityRole.Button, name)
            ?? throw new InvalidOperationException($"no button '{name}':\n{Session.Window.DumpAccessibilityTree()}");

        public void FocusPlayer()
        {
            var root = Session.Window.FindByAutomationId("video-player")
                ?? throw new InvalidOperationException("no player root");
            root.Focus();
        }

        /// <summary>Runs frames worth <paramref name="seconds"/> of animation time.</summary>
        public void Elapse(double seconds) =>
            Session.PumpFrames((int)Math.Ceiling(seconds / Session.DefaultFrameDelta.TotalSeconds));

        public void Dispose()
        {
            Player.Dispose();
            Session.Dispose();
        }
    }

    [Fact]
    public void ThePlayButton_TogglesPlayback_AndRenames()
    {
        using var f = new Fixture();

        f.Button("Play").Click();
        f.Session.PumpFrame();
        Assert.True(f.Player.IsPlaying);

        f.Button("Pause").Click();
        f.Session.PumpFrame();
        Assert.Equal(PlaybackState.Paused, f.Player.State);
    }

    [Fact]
    public void ClickingThePicture_TogglesPlayback_AndTakesFocus()
    {
        using var f = new Fixture();

        f.Session.Window.Click(320, 150);
        f.Session.PumpFrame();

        Assert.True(f.Player.IsPlaying);
        var root = f.Session.Window.FindByAutomationId("video-player")!;
        Assert.True(root.IsFocused);
    }

    [Fact]
    public void Keys_DoWhatTheDocsSay()
    {
        using var f = new Fixture();
        bool fullscreen = false;
        f.VideoPlayer.FullscreenRequested += () => fullscreen = true;
        f.FocusPlayer();
        var w = f.Session.Window;

        w.PressKey(KeyCode.Space);
        Assert.True(f.Player.IsPlaying);

        w.PressKey(KeyCode.Right);
        Assert.Equal(TimeSpan.FromSeconds(5), f.Player.Position);
        w.PressKey(KeyCode.L);
        Assert.Equal(TimeSpan.FromSeconds(10), f.Player.Position);   // +10 s, clamped to the 10 s clip
        w.PressKey(KeyCode.J);
        Assert.Equal(TimeSpan.Zero, f.Player.Position);
        w.PressKey(KeyCode.Right);
        w.PressKey(KeyCode.Left);
        Assert.Equal(TimeSpan.Zero, f.Player.Position);

        w.PressKey(KeyCode.Down);
        Assert.Equal(0.95f, f.Player.Volume, 0.001f);
        w.PressKey(KeyCode.M);
        Assert.True(f.Player.Muted);
        w.PressKey(KeyCode.F);
        Assert.True(fullscreen);

        w.PressKey(KeyCode.Digit5);
        Assert.Equal(TimeSpan.FromSeconds(5), f.Player.Position);
        w.PressKey(KeyCode.End);
        Assert.Equal(TimeSpan.FromSeconds(10), f.Player.Position);
        w.PressKey(KeyCode.Home);
        Assert.Equal(TimeSpan.Zero, f.Player.Position);

        w.PressKey(KeyCode.K);
        Assert.Equal(PlaybackState.Paused, f.Player.State);
    }

    [Fact]
    public void AnAppsChord_IsNotThePlayersLetter()
    {
        // The keys ignored modifiers, so a focused player took Ctrl+F (an app's find), Ctrl+M
        // and Ctrl+L as its own F, M and L — and swallowed them.
        using var f = new Fixture();
        bool fullscreen = false;
        f.VideoPlayer.FullscreenRequested += () => fullscreen = true;
        f.FocusPlayer();
        var w = f.Session.Window;

        w.PressKey(KeyCode.F, KeyModifiers.Ctrl);
        w.PressKey(KeyCode.M, KeyModifiers.Ctrl);
        w.PressKey(KeyCode.L, KeyModifiers.Ctrl);
        w.PressKey(KeyCode.Digit5, KeyModifiers.Alt);
        Assert.False(fullscreen);
        Assert.False(f.Player.Muted);
        Assert.Equal(TimeSpan.Zero, f.Player.Position);

        w.PressKey(KeyCode.F);
        Assert.True(fullscreen);
        Assert.Equal("Space · k", VideoPlayer.Shortcuts.Keys("play"));
    }

    [Fact]
    public void TheJumpButtons_SeekByTheJumpStep()
    {
        using var f = new Fixture();
        f.VideoPlayer.JumpStep = TimeSpan.FromSeconds(3);
        f.Session.PumpFrame();

        f.Button("Forward 3 s").Click();
        Assert.Equal(TimeSpan.FromSeconds(3), f.Player.Position);
        f.Button("Forward 3 s").Click();
        Assert.Equal(TimeSpan.FromSeconds(6), f.Player.Position);
        f.Button("Back 3 s").Click();
        Assert.Equal(TimeSpan.FromSeconds(3), f.Player.Position);
    }

    [Fact]
    public void TheBar_FadesOut_RatherThanVanishing()
    {
        using var f = new Fixture();
        f.VideoPlayer.AutoHideDelay = TimeSpan.FromSeconds(1);
        f.VideoPlayer.FadeDuration = TimeSpan.FromSeconds(1);
        f.Player.Play();
        f.Elapse(1.4);   // hide fired at 1 s; the fade is ~40% through

        var bar = f.Session.Window.FindByAutomationId("video-controls")!.Element;
        Assert.True(f.VideoPlayer.ControlsVisible);
        Assert.InRange(bar.Opacity, 0.2f, 0.85f);

        f.Elapse(0.8);   // the fade has ended
        Assert.False(f.VideoPlayer.ControlsVisible);

        f.Session.Window.MoveMouse(300, 150);   // comes back, quickly
        f.Elapse(0.6);
        Assert.True(f.VideoPlayer.ControlsVisible);
        Assert.Equal(1f, f.Session.Window.FindByAutomationId("video-controls")!.Element.Opacity);
    }

    [Fact]
    public void TheTime_ReadsElapsedOverTotal()
    {
        using var f = new Fixture();
        f.Player.Seek(TimeSpan.FromSeconds(7));
        f.Session.PumpFrame();

        Assert.NotNull(f.Session.Window.FindByText("0:07"));
        Assert.NotNull(f.Session.Window.FindByText("0:10"));
        Assert.Equal("1:02:03", VideoPlayer.FormatTime(new TimeSpan(1, 2, 3), TimeSpan.FromHours(2)));
        Assert.Equal("0:05:00", VideoPlayer.FormatTime(TimeSpan.FromMinutes(5), TimeSpan.FromHours(2)));
        Assert.Equal("5:00", VideoPlayer.FormatTime(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void TheScrubber_Seeks()
    {
        using var f = new Fixture();
        var seek = f.Session.Window.FindByRole(AccessibilityRole.Slider, "Seek")!;
        var bounds = seek.Bounds;

        // Drag the thumb from the start to the middle of the track.
        f.Session.Window.Drag(
            new Point(bounds.X + 4, bounds.Y + bounds.Height / 2),
            new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
        f.Session.PumpFrame();

        Assert.Contains(f.Backend.Calls, c => c.StartsWith("Seek("));
        Assert.InRange(f.Player.Position.TotalSeconds, 3, 7);
    }

    [Fact]
    public void ALiveStream_HasNoScrubber()
    {
        using var f = new Fixture(open: false);
        f.Backend.Duration = null;
        f.Player.Open(Clip);
        f.Session.PumpFrame();

        Assert.Null(f.Session.Window.FindByRole(AccessibilityRole.Slider, "Seek"));
        Assert.NotNull(f.Session.Window.FindByText("LIVE"));
    }

    [Fact]
    public void TrackMenus_AppearOnlyWhenThereIsAChoice()
    {
        using var f = new Fixture(open: false);
        Assert.Null(f.Session.Window.FindByRole(AccessibilityRole.Button, "Subtitles"));
        Assert.Null(f.Session.Window.FindByRole(AccessibilityRole.Button, "Audio track"));

        f.Backend.SubtitleTracks.Add(new MediaTrack(1, "English"));
        f.Backend.AudioTracks.Add(new MediaTrack(1, "Stereo"));
        f.Backend.AudioTracks.Add(new MediaTrack(2, "Surround"));
        f.Player.Open(Clip);
        f.Session.PumpFrame();

        Assert.NotNull(f.Session.Window.FindByRole(AccessibilityRole.Button, "Subtitles"));
        Assert.NotNull(f.Session.Window.FindByRole(AccessibilityRole.Button, "Audio track"));
    }

    [Fact]
    public void TheBar_HidesWhilePlaying_AndReturnsOnPointerMovement()
    {
        using var f = new Fixture();
        f.VideoPlayer.AutoHideDelay = TimeSpan.FromSeconds(1);

        f.Elapse(2);
        Assert.True(f.VideoPlayer.ControlsVisible);        // paused: never hides

        f.Player.Play();
        f.Elapse(0.5);
        Assert.True(f.VideoPlayer.ControlsVisible);
        f.Elapse(1);
        Assert.False(f.VideoPlayer.ControlsVisible);

        f.Session.Window.MoveMouse(300, 150);
        f.Session.PumpFrame();
        Assert.True(f.VideoPlayer.ControlsVisible);

        f.Elapse(1.5);
        Assert.False(f.VideoPlayer.ControlsVisible);

        f.Player.Pause();
        f.Session.PumpFrame();
        Assert.True(f.VideoPlayer.ControlsVisible);
    }

    [Fact]
    public void AFailure_IsShownOverThePicture()
    {
        using var f = new Fixture(open: false);
        f.Backend.FailNextOpenWith = "unsupported codec";
        f.Player.Open(Clip);
        f.Session.PumpFrame();

        Assert.NotNull(f.Session.Window.FindByText("Cannot play: unsupported codec"));
    }

    [Fact]
    public void APlayingScrubber_TellsAReaderWhereItIs_OnASchedule_NotEveryFrame()
    {
        // The Seek slider moved with the playback and told a reader nothing: its node carried no
        // value. It now does, and the window tells a range's change at most every 250 ms (and its
        // last value once it stops), so a playing video costs no whole-tree rebuild per frame.
        using var f = new Fixture();
        f.Backend.Duration = TimeSpan.FromSeconds(30);
        var bridge = f.Session.Window.InstallAccessibilityBridge();
        f.Player.Open(Clip);
        f.Session.PumpFrames(3);
        f.Player.Play();
        f.Session.Window.MoveMouse(320, 380);
        f.Session.PumpFrames(5);
        bridge.ClearDiffs();

        // Ten seconds at 60 frames a second, the pointer keeping the bar up.
        for (int i = 0; i < 600; i++)
        {
            if (i % 60 == 30) f.Session.Window.MoveMouse(320 + i / 60 % 2, 380);
            f.Backend.Advance(f.Session.DefaultFrameDelta);
            f.Session.PumpFrame();
        }
        f.Player.Pause();
        f.Session.PumpFrames(30);

        var seek = f.Session.Window.FindByAutomationId("video-seek")!;
        var told = bridge.Diffs.SelectMany(d => d.PropertyChanged)
            .Where(c => c.PropertyName == AccessibilityProperties.RangeValue && c.Node.ElementId == seek.Element.Id)
            .Select(c => c.Node.Range!.Value.Value).ToList();
        // The slider steps every half second: twenty moves in ten seconds, each told.
        Assert.InRange(told.Count, 15, 21);
        Assert.Equal(((HultUI.Core.Elements.SliderElement)seek.Element).Value, told[^1]);
        Assert.Equal(f.Player.Position.TotalSeconds, told[^1], 0.5);
        Assert.True(bridge.Diffs.Count <= 60, $"{bridge.Diffs.Count} applied diffs in ten seconds");
    }

    [Fact]
    public void APositionReport_RebuildsNothing_ButMovesTheTimeAndTheThumb()
    {
        // mpv reports the position ten times a second while playing. Each report rebuilt the
        // whole player (about 22 elements, 41 KB); it now rewrites the elapsed time and the
        // scrubber's value on the elements already on screen.
        using var f = new Fixture();
        f.Player.Play();
        f.Session.PumpFrame();
        int builds = f.VideoPlayer.Builds;

        for (int i = 1; i <= 20; i++)
        {
            f.Backend.Raise(new BackendEvent.PositionChanged(TimeSpan.FromSeconds(i * 0.4)));
            f.Session.PumpFrame();
        }

        Assert.Equal(builds, f.VideoPlayer.Builds);
        Assert.NotNull(f.Session.Window.FindByText("0:08"));
        var seek = (HultUI.Core.Elements.SliderElement)f.Session.Window.FindByAutomationId("video-seek")!.Element;
        Assert.Equal(8.0, seek.Value);
    }

    [Fact]
    public void ASeek_IsToldToAReader()
    {
        // The scrubber's value is written in place now rather than by a rebuild; a reader is
        // still told where a seek landed, once the 250 ms a range waits have passed.
        using var f = new Fixture();
        var bridge = f.Session.Window.InstallAccessibilityBridge();
        f.Session.PumpFrames(3);
        f.FocusPlayer();
        f.Session.PumpFrames(30);
        bridge.ClearDiffs();

        f.Session.Window.PressKey(KeyCode.Right);   // +5 s, paused
        f.Session.PumpFrames(20);

        var seek = f.Session.Window.FindByAutomationId("video-seek")!;
        var told = bridge.Diffs.SelectMany(d => d.PropertyChanged)
            .Where(c => c.PropertyName == AccessibilityProperties.RangeValue && c.Node.ElementId == seek.Element.Id)
            .Select(c => c.Node.Range!.Value.Value).ToList();
        Assert.Equal(5.0, Assert.Single(told));
    }

    [Fact]
    public void FocusPicture_BeforeMounting_LandsWhilePlaying()
    {
        // An app opens a file, asks for the keyboard and mounts the player in its next build. The
        // focus waits for a build of the mounted player; a playing film's position reports no
        // longer supply one, and the build the root asks for with the theme still does.
        using var session = HeadlessSession.Create(640, 400);
        var backend = new FakeMediaBackend();
        using var player = new MediaPlayer(backend);
        var video = new VideoPlayer(player);
        player.Open(Clip, autoPlay: true);
        video.FocusPicture();
        session.Application.SetRoot(Box.New().Children(video.Mount().Width(640f).Height(400f)));

        for (int i = 0; i < 6; i++)
        {
            backend.Advance(session.DefaultFrameDelta);
            session.PumpFrame();
        }

        Assert.True(player.IsPlaying);
        Assert.True(session.Window.FindByAutomationId("video-player")!.IsFocused);
    }

    [Fact]
    public void TheMuteButton_TogglesMute_AndTheVolumeSliderShowsIt()
    {
        using var f = new Fixture();

        f.Button("Mute").Click();
        f.Session.PumpFrame();
        Assert.True(f.Player.Muted);
        Assert.NotNull(f.Button("Unmute"));
        Assert.NotNull(f.Session.Window.FindByRole(AccessibilityRole.Slider, "Volume"));
    }
}
