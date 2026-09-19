using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Threading;

namespace HultUI.Media;

/// <summary>
/// One backend, one sink, and the reactive state the UI binds to. Every property is
/// signal-backed, so reading it inside a component's <c>Build</c> tracks it; every
/// command runs on the UI thread and is forwarded to the backend, whose events —
/// from whatever thread — are applied on the UI thread on the next frame.
/// </summary>
/// <remarks>
/// <para>State is the backend's word. <see cref="Play"/> does not set
/// <see cref="State"/> to <see cref="PlaybackState.Playing"/>; the backend does when it
/// is. The exceptions are the settings a backend does not echo — <see cref="Rate"/>,
/// <see cref="Volume"/>, <see cref="Muted"/> — and <see cref="Position"/> on a seek,
/// which moves at once so a scrubber lands where it was dropped.</para>
///
/// <para>Frames: the player owns a <see cref="PixelFrameSink"/>, hands it to the backend
/// and exposes it as <see cref="Frames"/> for a <see cref="VideoElement"/> to paint.
/// <see cref="FrameArrived"/> fires on the UI thread for each published frame; the
/// element uses it to mark itself dirty, so nothing else needs to.</para>
/// </remarks>
public sealed class MediaPlayer : IDisposable
{
    private readonly IMediaBackend _backend;
    private readonly PixelFrameSink _sink = new();
    private readonly VideoSink _videoSink;
    private GpuFrameSink? _gpuSink;
    private bool _disposed;

    private readonly Signal<MediaSource?> _source = new(null);
    private readonly Signal<PlaybackState> _state = new(PlaybackState.NoMedia);
    private readonly Signal<TimeSpan> _position = new(TimeSpan.Zero);
    private readonly Signal<TimeSpan?> _duration = new(null);
    private readonly Signal<double> _rate = new(1.0);
    private readonly Signal<float> _volume = new(1f);
    private readonly Signal<bool> _muted = new(false);
    private readonly Signal<Size> _videoSize = new(Size.Zero);
    private readonly Signal<IReadOnlyList<MediaTrack>> _audioTracks = new([]);
    private readonly Signal<IReadOnlyList<MediaTrack>> _subtitleTracks = new([]);
    private readonly Signal<int?> _selectedAudioTrack = new(null);
    private readonly Signal<int?> _selectedSubtitleTrack = new(null);
    private readonly Signal<float?> _buffering = new(null);
    private readonly Signal<string?> _error = new(null);

    public MediaPlayer(IMediaBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _videoSink = new VideoSink(_sink) { Owner = this };
        _backend.Event += OnBackendEvent;
        _sink.FramePublished += OnFramePublished;
        _backend.Attach(_videoSink);
    }

    public IMediaBackend Backend => _backend;
    public BackendCapabilities Capabilities => _backend.Capabilities;

    /// <summary>
    /// The frames, for an image element to draw. Never null; empty until the first frame.
    /// A backend that registered a GPU renderer is drawn through a <see cref="GpuFrameSink"/>,
    /// which is also this pixel ring on a surface without GL.
    /// </summary>
    public IPixelImageSource Frames => _gpuSink ?? (IPixelImageSource)_sink;

    /// <summary>How frames reach the screen; <see cref="VideoSinkMode.Pixels"/> for a backend with no GPU renderer.</summary>
    public VideoSinkMode SinkMode => _gpuSink?.Mode ?? VideoSinkMode.Pixels;

    /// <summary>A frame was published. UI thread. <see cref="VideoElement"/> subscribes.</summary>
    public event Action? FrameArrived;

    // ──── State ────

    public MediaSource? Source => _source.Value;
    public PlaybackState State => _state.Value;
    public TimeSpan Position => _position.Value;
    /// <summary>Null until known, and for a live stream.</summary>
    public TimeSpan? Duration => _duration.Value;
    /// <summary>The display size of the video (sample aspect applied); zero until known.</summary>
    public Size VideoSize => _videoSize.Value;
    public IReadOnlyList<MediaTrack> AudioTracks => _audioTracks.Value;
    public IReadOnlyList<MediaTrack> SubtitleTracks => _subtitleTracks.Value;
    public int? SelectedAudioTrack => _selectedAudioTrack.Value;
    public int? SelectedSubtitleTrack => _selectedSubtitleTrack.Value;
    /// <summary>Buffering fraction while the backend waits for data; null otherwise.</summary>
    public float? Buffering => _buffering.Value;
    /// <summary>Why the state is <see cref="PlaybackState.Failed"/>; null otherwise.</summary>
    public string? Error => _error.Value;

    public bool IsPlaying => State == PlaybackState.Playing;
    public bool HasMedia => State is not (PlaybackState.NoMedia or PlaybackState.Failed);
    public bool HasVideo => VideoSize.Width > 0 && VideoSize.Height > 0;

    public double Rate
    {
        get => _rate.Value;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), value, "Rate must be positive.");
            UIThread.VerifyAccess("MediaPlayer.Rate");
            _rate.Value = value;
            _backend.SetRate(value);
        }
    }

    public float Volume
    {
        get => _volume.Value;
        set
        {
            UIThread.VerifyAccess("MediaPlayer.Volume");
            value = Math.Clamp(value, 0f, 1f);
            _volume.Value = value;
            _backend.SetVolume(value);
        }
    }

    public bool Muted
    {
        get => _muted.Value;
        set
        {
            UIThread.VerifyAccess("MediaPlayer.Muted");
            _muted.Value = value;
            _backend.SetMuted(value);
        }
    }

    // ──── Commands ────

    public void Open(MediaSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        UIThread.VerifyAccess("MediaPlayer.Open");
        ReactiveRuntime.Batch(() =>
        {
            _source.Value = source;
            _state.Value = PlaybackState.Opening;
            _error.Value = null;
            _position.Value = TimeSpan.Zero;
            _duration.Value = null;
            _videoSize.Value = Size.Zero;
            _audioTracks.Value = [];
            _subtitleTracks.Value = [];
            _selectedAudioTrack.Value = null;
            _selectedSubtitleTrack.Value = null;
            _buffering.Value = null;
        });
        _backend.Open(source);
    }

    public void Play()
    {
        UIThread.VerifyAccess("MediaPlayer.Play");
        if (!HasMedia) return;
        _backend.Play();
    }

    public void Pause()
    {
        UIThread.VerifyAccess("MediaPlayer.Pause");
        if (!HasMedia) return;
        _backend.Pause();
    }

    public void TogglePlay()
    {
        if (IsPlaying) Pause(); else Play();
    }

    public void Stop()
    {
        UIThread.VerifyAccess("MediaPlayer.Stop");
        if (!HasMedia) return;
        _position.Value = TimeSpan.Zero;
        _backend.Stop();
    }

    /// <summary>Moves the playhead; <see cref="Position"/> changes at once. Clamped to the media.</summary>
    public void Seek(TimeSpan position, bool precise = true)
    {
        UIThread.VerifyAccess("MediaPlayer.Seek");
        if (!HasMedia) return;
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (Duration is { } duration && position > duration) position = duration;
        _position.Value = position;
        _backend.Seek(position, precise);
    }

    public void SeekBy(TimeSpan delta, bool precise = false) => Seek(Position + delta, precise);

    public void SelectAudioTrack(int? id)
    {
        UIThread.VerifyAccess("MediaPlayer.SelectAudioTrack");
        _selectedAudioTrack.Value = id;
        _backend.SelectAudioTrack(id);
    }

    public void SelectSubtitleTrack(int? id)
    {
        UIThread.VerifyAccess("MediaPlayer.SelectSubtitleTrack");
        _selectedSubtitleTrack.Value = id;
        _backend.SelectSubtitleTrack(id);
    }

    // ──── Backend → UI thread ────

    private void OnBackendEvent(BackendEvent e) => Dispatch(() => Apply(e));

    private void OnFramePublished(PixelFrameSink _) => Dispatch(() => FrameArrived?.Invoke());

    private void OnRepaintRequested() => Dispatch(() => FrameArrived?.Invoke());

    /// <summary>
    /// Runs on the UI thread: now when this is it (a backend that reports synchronously,
    /// or a unit test with no application), otherwise on the next frame — which the post
    /// wakes, so a decoded frame does not wait out the idle interval.
    /// </summary>
    private void Dispatch(Action apply)
    {
        if (_disposed) return;
        if (UIThread.IsCurrent) apply();
        else Application.InvokeOnMainThread(() => { if (!_disposed) apply(); });
    }

    private void Apply(BackendEvent e)
    {
        ReactiveRuntime.Batch(() =>
        {
            switch (e)
            {
                case BackendEvent.StateChanged s:
                    _state.Value = s.State;
                    break;
                case BackendEvent.PositionChanged p:
                    _position.Value = p.Position;
                    break;
                case BackendEvent.DurationChanged d:
                    _duration.Value = d.Duration;
                    break;
                case BackendEvent.VideoGeometry g:
                    _videoSize.Value = new Size((float)Math.Round(g.Width * g.SampleAspect), g.Height);
                    break;
                case BackendEvent.TracksChanged t:
                    _audioTracks.Value = t.Audio;
                    _subtitleTracks.Value = t.Subtitles;
                    _selectedAudioTrack.Value = t.SelectedAudio;
                    _selectedSubtitleTrack.Value = t.SelectedSubtitle;
                    break;
                case BackendEvent.BufferingChanged b:
                    _buffering.Value = b.Fraction;
                    break;
                case BackendEvent.EndReached:
                    _state.Value = PlaybackState.Ended;
                    if (_duration.Value is { } end) _position.Value = end;
                    break;
                case BackendEvent.Failed f:
                    _error.Value = f.Message;
                    _state.Value = PlaybackState.Failed;
                    _buffering.Value = null;
                    break;
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backend.Event -= OnBackendEvent;
        _sink.FramePublished -= OnFramePublished;
        // The backend first: a GPU renderer frees its GL objects before the sink
        // deletes the framebuffer they drew into.
        _backend.Dispose();
        _gpuSink?.Dispose();
        _sink.Dispose();
    }

    private sealed class VideoSink(PixelFrameSink pixels) : IVideoSink
    {
        internal MediaPlayer? Owner { get; set; }
        public PixelFrameSink Pixels => pixels;

        public void UseGpuRenderer(IGpuFrameRenderer renderer)
        {
            ArgumentNullException.ThrowIfNull(renderer);
            var owner = Owner ?? throw new InvalidOperationException("The sink has no player.");
            if (owner._gpuSink is not null)
                throw new InvalidOperationException("A GPU renderer is already registered.");
            owner._gpuSink = new GpuFrameSink(pixels, renderer);
        }

        public void RequestRepaint() => Owner?.OnRepaintRequested();
    }
}
