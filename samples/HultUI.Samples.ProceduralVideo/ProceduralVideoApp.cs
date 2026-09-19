using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Media;

namespace HultUI.Samples.ProceduralVideo;

// Inside a HultUI.* namespace bare `Text` binds to the HultUI.Text namespace.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// A picker of algorithms over the player. Built as a class with <see cref="Mount"/> so
/// the headless driver and the tests can host it.
/// </summary>
public sealed class ProceduralVideoApp : IDisposable
{
    private readonly Window _window;
    private readonly IFrameAlgorithm[] _algorithms = [new Plasma(), new JuliaSet(), new GameOfLife()];
    private readonly ProceduralBackend _backend;
    private readonly MediaPlayer _player;
    private readonly HultUI.Media.VideoPlayer _videoPlayer;

    public ProceduralVideoApp(Application app, Window window)
    {
        _window = window;
        _backend = new ProceduralBackend(_algorithms[0]);
        _player = new MediaPlayer(_backend);
        _videoPlayer = new HultUI.Media.VideoPlayer(_player);
        _videoPlayer.FullscreenRequested += ToggleFullscreen;
        if (Environment.GetEnvironmentVariable("HULTUI_MEDIA_NOHIDE") is "1")
            _videoPlayer.AutoHideControls = false;
    }

    public MediaPlayer Player => _player;
    public ProceduralBackend Backend => _backend;
    public IReadOnlyList<IFrameAlgorithm> Algorithms => _algorithms;

    public Element Mount()
    {
        var muted = new Color(148, 163, 184);
        _player.Open(MediaSource.FromFile("procedural.synthetic"));

        return Flex.Column(gap: 10).Padding(14f).Children(
            Flex.Row(gap: 14).AlignItems(AlignItems.Center).FlexShrink(0).Children(
                Text.New("Procedural Video").FontSize(22f).FontWeight(FontWeight.Bold),
                Text.New("Algorithm").FontSize(13f).TextColor(muted),
                Dropdown.New(_algorithms.Select(a => a.Name).ToArray(), 0)
                    .Changed(i => _backend.Algorithm = _algorithms[i])),
            _videoPlayer.Mount().FlexGrow(1),
            Text.New("Every frame is computed from the playhead's time, so seeking, speed and pause all just work · Space play/pause · ←/→ 5 s · J/L 10 s")
                .FontSize(12f).TextColor(muted).FlexShrink(0));
    }

    private void ToggleFullscreen()
    {
        if (_window.IsMaximized) _window.Restore(); else _window.Maximize();
        _videoPlayer.IsFullscreen = !_videoPlayer.IsFullscreen;
    }

    public void Dispose() => _player.Dispose();
}
