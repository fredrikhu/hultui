using System.Diagnostics;
using HultUI.Core.Rendering;
using HultUI.Media;

namespace HultUI.Samples.ProceduralVideo;

/// <summary>
/// An <see cref="IMediaBackend"/> whose frames come from an <see cref="IFrameAlgorithm"/>
/// instead of a decoder. A worker thread asks the algorithm for the frame at the
/// current time thirty times a second and writes it into the sink; because a frame is a
/// pure function of time, seeking is a change of clock and playback speed a change of
/// its rate. The "clip" is five minutes long and reports itself like any file would.
/// </summary>
public sealed class ProceduralBackend : IMediaBackend
{
    private const int Width = 480, Height = 270;
    public const double MinFrameRate = 1, MaxFrameRate = 250;
    private static readonly TimeSpan Length = TimeSpan.FromMinutes(5);
    private double _frameRate = 30;

    private readonly object _gate = new();
    private IVideoSink? _sink;
    private Thread? _thread;
    private volatile bool _running;
    private PlaybackState _state = PlaybackState.NoMedia;
    private double _position;
    private double _rate = 1;
    private IFrameAlgorithm _algorithm;

    public ProceduralBackend(IFrameAlgorithm algorithm) => _algorithm = algorithm;

    /// <summary>The algorithm drawing the frames. Swappable while playing; the next frame uses it.</summary>
    public IFrameAlgorithm Algorithm
    {
        get => _algorithm;
        set
        {
            lock (_gate) _algorithm = value;
            RenderNow();
        }
    }

    /// <summary>
    /// How many frames a second the worker publishes while playing, 1 to 250. Only the
    /// delivery pace: a frame is still computed from the playhead's time, so the picture
    /// moves at the same speed at 5 fps as at 200, in coarser or finer steps. Read by the
    /// worker each frame, so a change applies at once.
    /// </summary>
    public double FrameRate
    {
        get => Volatile.Read(ref _frameRate);
        set => Volatile.Write(ref _frameRate, Math.Clamp(value, MinFrameRate, MaxFrameRate));
    }

    public BackendCapabilities Capabilities =>
        BackendCapabilities.PixelFrames | BackendCapabilities.Rate | BackendCapabilities.PreciseSeek;

    public event Action<BackendEvent>? Event;

    public void Attach(IVideoSink sink) => _sink = sink;

    public void Open(MediaSource source)
    {
        lock (_gate) { _position = 0; _state = PlaybackState.Opening; }
        Raise(new BackendEvent.StateChanged(PlaybackState.Opening));
        Raise(new BackendEvent.DurationChanged(Length));
        Raise(new BackendEvent.VideoGeometry(Width, Height));
        Raise(new BackendEvent.TracksChanged([], [], null, null));
        SetState(PlaybackState.Paused);
        RenderNow();
        if (_thread is null)
        {
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "procedural-video" };
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
        RenderNow();
    }

    public void Seek(TimeSpan position, bool precise)
    {
        double seconds = Math.Clamp(position.TotalSeconds, 0, Length.TotalSeconds);
        bool wasEnded;
        lock (_gate) { _position = seconds; wasEnded = _state == PlaybackState.Ended; }
        Raise(new BackendEvent.PositionChanged(TimeSpan.FromSeconds(seconds)));
        if (wasEnded && seconds < Length.TotalSeconds) SetState(PlaybackState.Paused);
        RenderNow();
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
        double last = 0, lastReport = 0;
        while (_running)
        {
            // Sleep out what is left of this frame's interval, measured from the last
            // frame's start, so the rate holds however long a frame took to compute.
            double interval = 1 / FrameRate;
            double remaining = interval - (clock.Elapsed.TotalSeconds - last);
            if (remaining > 0) Thread.Sleep(TimeSpan.FromSeconds(remaining));
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

            RenderAt(position);
            if (now - lastReport >= 0.25 || ended)
            {
                lastReport = now;
                Raise(new BackendEvent.PositionChanged(TimeSpan.FromSeconds(position)));
            }
            if (ended) Raise(new BackendEvent.EndReached());
        }
    }

    /// <summary>Draws the frame for the current position, whatever the state — a seek while paused shows where it landed.</summary>
    private void RenderNow()
    {
        double position;
        PlaybackState state;
        lock (_gate) { position = _position; state = _state; }
        if (state is PlaybackState.NoMedia or PlaybackState.Failed) return;
        RenderAt(position);
    }

    private void RenderAt(double seconds)
    {
        if (_sink is null) return;
        IFrameAlgorithm algorithm;
        lock (_gate) algorithm = _algorithm;
        // The ring allows one writer at a time; the worker and a UI-thread seek can
        // both want to draw, so they take turns here.
        lock (_sink)
        {
            using var writer = _sink.Pixels.BeginWrite(Width, Height, PixelFormat.Rgbx8888);
            algorithm.Render(writer.Pixels, Width, Height, writer.Stride, seconds);
            writer.Publish();
        }
    }
}
