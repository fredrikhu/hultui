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
