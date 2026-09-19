using System.Diagnostics;
using HultUI.Core.Rendering;
using HultUI.Media;

namespace HultUI.Samples.Media;

/// <summary>
/// A backend with no decoder: a worker thread renders a moving test pattern at 30 fps
/// into the sink and reports the playhead four times a second, honouring seeks, the
/// rate, and the two-minute duration. It exercises exactly the paths a real decoder
/// would — frames and events from another thread — which is the point of it.
/// </summary>
public sealed class SyntheticMediaBackend : IMediaBackend
{
    private const int Width = 640;
    private const int Height = 360;
    private const double Fps = 30;
    private static readonly TimeSpan Length = TimeSpan.FromMinutes(2);

    private readonly object _gate = new();
    private IVideoSink? _sink;
    private Thread? _thread;
    private volatile bool _running;
    private PlaybackState _state = PlaybackState.NoMedia;
    private double _position;          // seconds
    private double _rate = 1;
    private long _frame;

    public BackendCapabilities Capabilities =>
        BackendCapabilities.PixelFrames | BackendCapabilities.Rate | BackendCapabilities.Subtitles
        | BackendCapabilities.PreciseSeek;

    public event Action<BackendEvent>? Event;

    public void Attach(IVideoSink sink) => _sink = sink;

    public void Open(MediaSource source)
    {
        lock (_gate)
        {
            _position = 0;
            _frame = 0;
            _state = PlaybackState.Opening;
        }
        Raise(new BackendEvent.StateChanged(PlaybackState.Opening));
        Raise(new BackendEvent.DurationChanged(Length));
        Raise(new BackendEvent.VideoGeometry(Width, Height));
        Raise(new BackendEvent.TracksChanged(
            [new MediaTrack(1, "English", "en"), new MediaTrack(2, "Commentary", "en")],
            [new MediaTrack(1, "English", "en"), new MediaTrack(2, "Svenska", "sv")],
            SelectedAudio: 1, SelectedSubtitle: null));
        SetState(PlaybackState.Paused);
        RenderFrame(0);

        if (_thread is null)
        {
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "synthetic-video" };
            _thread.Start();
        }
    }

    public void Play()
    {
        lock (_gate)
        {
            if (_state is PlaybackState.NoMedia or PlaybackState.Opening or PlaybackState.Failed) return;
            if (_state == PlaybackState.Ended) _position = 0;
        }
        SetState(PlaybackState.Playing);
    }

    public void Pause()
    {
        lock (_gate) { if (_state != PlaybackState.Playing) return; }
        SetState(PlaybackState.Paused);
    }

    public void Stop()
    {
        lock (_gate) _position = 0;
        Raise(new BackendEvent.PositionChanged(TimeSpan.Zero));
        SetState(PlaybackState.Paused);
        RenderFrame(0);
    }

    public void Seek(TimeSpan position, bool precise)
    {
        double seconds = Math.Clamp(position.TotalSeconds, 0, Length.TotalSeconds);
        bool wasEnded;
        lock (_gate)
        {
            _position = seconds;
            wasEnded = _state == PlaybackState.Ended;
        }
        Raise(new BackendEvent.PositionChanged(TimeSpan.FromSeconds(seconds)));
        if (wasEnded && seconds < Length.TotalSeconds) SetState(PlaybackState.Paused);
        RenderFrame(seconds);
    }

    public void SetRate(double rate) { lock (_gate) _rate = rate; }
    public void SetVolume(float volume) { }
    public void SetMuted(bool muted) { }
    public void SelectAudioTrack(int? id) { }
    public void SelectSubtitleTrack(int? id) { }

    public void Dispose()
    {
        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    private void SetState(PlaybackState state)
    {
        lock (_gate) _state = state;
        Raise(new BackendEvent.StateChanged(state));
    }

    private void Raise(BackendEvent e) => Event?.Invoke(e);

    private void Loop()
    {
        var clock = Stopwatch.StartNew();
        double last = 0;
        double lastReport = 0;
        var frameTime = TimeSpan.FromSeconds(1 / Fps);

        while (_running)
        {
            Thread.Sleep(frameTime);
            double now = clock.Elapsed.TotalSeconds;
            double dt = now - last;
            last = now;

            double position;
            bool ended = false;
            lock (_gate)
            {
                if (_state != PlaybackState.Playing) continue;
                _position += dt * _rate;
                if (_position >= Length.TotalSeconds)
                {
                    _position = Length.TotalSeconds;
                    _state = PlaybackState.Ended;
                    ended = true;
                }
                position = _position;
            }

            RenderFrame(position);

            if (now - lastReport >= 0.25 || ended)
            {
                lastReport = now;
                Raise(new BackendEvent.PositionChanged(TimeSpan.FromSeconds(position)));
            }
            if (ended) Raise(new BackendEvent.EndReached());
        }
    }

    /// <summary>A gradient, a sweeping bar and a bouncing dot, all functions of the position.</summary>
    private void RenderFrame(double seconds)
    {
        if (_sink is null) return;
        using var writer = _sink.Pixels.BeginWrite(Width, Height, PixelFormat.Rgbx8888);
        var pixels = writer.Pixels;
        int stride = writer.Stride;

        float sweep = (float)(seconds % 4 / 4) * Width;
        float dotX = Width * 0.5f + MathF.Cos((float)seconds * 1.3f) * Width * 0.35f;
        float dotY = Height * 0.5f + MathF.Sin((float)seconds * 2.1f) * Height * 0.35f;
        const float dotR = 22f;

        for (int y = 0; y < Height; y++)
        {
            var row = pixels.Slice(y * stride, Width * 4);
            byte rowG = (byte)(30 + 60 * y / Height);
            byte rowB = (byte)(70 + 80 * (Height - y) / Height);
            for (int x = 0; x < Width; x++)
            {
                byte r = (byte)(20 + 90 * x / Width), g = rowG, b = rowB;
                float dx = x - dotX, dy = y - dotY;
                if (dx * dx + dy * dy < dotR * dotR) { r = 250; g = 204; b = 21; }
                else if (Math.Abs(x - sweep) < 3) { r = 255; g = 255; b = 255; }
                int i = x * 4;
                row[i] = r; row[i + 1] = g; row[i + 2] = b; row[i + 3] = 255;
            }
        }
        writer.Publish();
        Interlocked.Increment(ref _frame);
    }
}
