using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Icons;
using HultUI.Media;

namespace HultUI.Samples.VideoPlayer;

// Inside a HultUI.* namespace bare `Text` binds to the HultUI.Text namespace.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// The player window: a toolbar with Open… and the file's name, the picture with
/// its transport overlay, and a status line. Files arrive from the toolbar's dialog,
/// from a drop anywhere on the window, or from the command line. Built as a class
/// with <see cref="Mount"/> so the headless driver and the tests can host it, with
/// whichever backend they hand it.
/// </summary>
public sealed class VideoPlayerApp : IDisposable
{
    private static readonly FileDialogFilter[] VideoFilters =
    [
        new("Video files", "*.mp4;*.mkv;*.webm;*.mov;*.avi;*.m4v;*.ts;*.y4m"),
        new("All files", "*"),
    ];

    private readonly Application _app;
    private readonly Window _window;
    private readonly MediaPlayer? _player;
    private readonly HultUI.Media.VideoPlayer? _videoPlayer;
    private readonly string? _initialFile;
    private readonly TextElement _title = Text.New("No file open").FontSize(14f);
    private readonly TextElement _status = Text.New("").FontSize(12f);
    private readonly Color _muted = new(148, 163, 184);

    /// <param name="backends">Makes the decoder backend; null when none is available, in
    /// which case the window explains instead of playing.</param>
    /// <param name="initialFile">A file to open as soon as the tree is mounted.</param>
    public VideoPlayerApp(Application app, Window window, Func<IMediaBackend>? backends, string? initialFile = null)
    {
        _app = app;
        _window = window;
        _initialFile = initialFile;
        if (backends is null) return;

        _player = new MediaPlayer(backends());
        _videoPlayer = new HultUI.Media.VideoPlayer(_player);
        _videoPlayer.FullscreenRequested += ToggleFullscreen;
        // The headless driver pumps frames far faster than wall time, so the bar would
        // hide before any shot of it playing; HULTUI_MEDIA_NOHIDE=1 keeps it on screen.
        if (Environment.GetEnvironmentVariable("HULTUI_MEDIA_NOHIDE") is "1")
            _videoPlayer.AutoHideControls = false;
    }

    public MediaPlayer? Player => _player;
    public HultUI.Media.VideoPlayer? VideoPlayer => _videoPlayer;

    /// <summary>The path of the file that is open, or null.</summary>
    public string? CurrentFile { get; private set; }

    public Element Mount()
    {
        _title.TextColor(_muted);
        _status.TextColor(_muted);

        var root = Flex.Column(gap: 10).Padding(14f)
            .OnDragOver(e => e.Accepted = e.DragData.HasFiles)
            .OnDrop(e => { if (e.DragData.HasFiles) Open(e.DragData.Files[0]); });

        if (_player is null || _videoPlayer is null)
        {
            root.Children(
                Text.New("Video Player").FontSize(22f).FontWeight(FontWeight.Bold).FlexShrink(0),
                Box.New().FlexGrow(1).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center).Children(
                    Flex.Column(gap: 8).AlignItems(AlignItems.Center).Children(
                        Text.New("No libmpv found").FontSize(18f).FontWeight(FontWeight.SemiBold),
                        Text.New("Add the HultUI.Media.Mpv.Natives package, put libmpv-2.dll beside the application,")
                            .FontSize(13f).TextColor(_muted),
                        Text.New("or point HULTUI_MPV_PATH at one. See src/HultUI.Media.Mpv/README.md.")
                            .FontSize(13f).TextColor(_muted))));
            return root;
        }

        var openButton = Box.New()
            .Padding(8f).CornerRadius(6f).Gap(6f)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .AccessibleName("Open file").AutomationId("open-file").Tooltip("Open a video file")
            .OnClick(e => { _ = OpenFromDialogAsync(); })
            .Children(Flex.Row(gap: 6).AlignItems(AlignItems.Center).Children(
                Phosphor.Draw("folder-open", 16f, Color.White),
                Text.New("Open…").FontSize(13f)));
        openButton.Role = AccessibilityRole.Button;
        openButton.IsFocusable = true;
        openButton.OnPointerEnter(e => { ((BoxElement)e.CurrentTarget).Background(new Color(255, 255, 255, 30)); e.CurrentTarget.MarkPaintDirty(); })
                  .OnPointerLeave(e => { ((BoxElement)e.CurrentTarget).Background(new Color(0, 0, 0, 0)); e.CurrentTarget.MarkPaintDirty(); });

        root.Children(
            Flex.Row(gap: 14).AlignItems(AlignItems.Center).FlexShrink(0).Children(
                Text.New("Video Player").FontSize(22f).FontWeight(FontWeight.Bold),
                openButton,
                _title),
            _videoPlayer.Mount().FlexGrow(1),
            Flex.Row(gap: 14).FlexShrink(0).Children(
                _status,
                Text.New("Drop a file anywhere · Space play/pause · ←/→ 5 s · J/L 10 s · ↑/↓ volume · M mute · F full screen")
                    .FontSize(12f).TextColor(_muted)));

        if (_initialFile is not null) Open(_initialFile);
        return root;
    }

    /// <summary>Opens a file and starts playing it.</summary>
    public void Open(string path)
    {
        if (_player is null) return;
        CurrentFile = path;
        _title.Content = Path.GetFileName(path);
        _status.Content = path;
        _window.Title = $"{Path.GetFileName(path)} - HultUI Video Player";
        _player.Open(MediaSource.FromFile(path), autoPlay: true);
    }

    private async Task OpenFromDialogAsync()
    {
        var initial = CurrentFile is null ? null : Path.GetDirectoryName(CurrentFile);
        var path = await _app.FileDialogs.OpenFileAsync(initial, VideoFilters);
        if (path is not null) Open(path);
    }

    private void ToggleFullscreen()
    {
        if (_videoPlayer is null) return;
        if (_window.IsMaximized) _window.Restore(); else _window.Maximize();
        _videoPlayer.IsFullscreen = !_videoPlayer.IsFullscreen;
    }

    public void Dispose() => _player?.Dispose();
}
