using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Media;

namespace HultUI.Samples.Media;

// Inside a HultUI.* namespace bare `Text` binds to the HultUI.Text namespace.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// The sample's tree: a title, the player, and a line of key help. Built as a class
/// with <see cref="Mount"/> so the headless driver can host it the way the Fluent
/// sample is hosted.
/// </summary>
public sealed class MediaSampleApp : IDisposable
{
    private readonly Application _app;
    private readonly Window _window;
    private readonly MediaPlayer _player;
    private readonly VideoPlayer _videoPlayer;

    private readonly MediaSource _source;

    public MediaSampleApp(Application app, Window window)
        : this(app, window, new SyntheticMediaBackend(), MediaSource.FromFile("test-pattern.synthetic")) { }

    public MediaSampleApp(Application app, Window window, IMediaBackend backend, MediaSource source)
    {
        _app = app;
        _window = window;
        _source = source;
        _player = new MediaPlayer(backend);
        _videoPlayer = new VideoPlayer(_player);
        _videoPlayer.FullscreenRequested += ToggleFullscreen;
        // The headless driver pumps frames far faster than wall time, so the bar would
        // hide before any shot of it playing; HULTUI_MEDIA_NOHIDE=1 keeps it on screen.
        if (Environment.GetEnvironmentVariable("HULTUI_MEDIA_NOHIDE") is "1")
            _videoPlayer.AutoHideControls = false;
    }

    public MediaPlayer Player => _player;

    public Element Mount()
    {
        var muted = new Color(148, 163, 184);

        _player.Open(_source);

        return Flex.Column(gap: 12).Padding(16f).Children(
            Text.New("Video").FontSize(22f).FontWeight(FontWeight.Bold).FlexShrink(0),
            _videoPlayer.Mount().FlexGrow(1),
            Text.New("Space play/pause · ←/→ seek 5 s · J/L seek 10 s · ↑/↓ volume · M mute · F full screen · 0–9 jump")
                .FontSize(12f).TextColor(muted).FlexShrink(0));
    }

    private void ToggleFullscreen()
    {
        // The sample's idea of full screen: the window maximised. A real app would also
        // hide its chrome, which is why the component leaves this to the app.
        if (_window.IsMaximized) _window.Restore(); else _window.Maximize();
        _videoPlayer.IsFullscreen = !_videoPlayer.IsFullscreen;
    }

    public void Dispose() => _player.Dispose();
}
