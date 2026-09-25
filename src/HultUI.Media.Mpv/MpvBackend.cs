using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HultUI.Core.Rendering;
using static HultUI.Media.Mpv.Mpv;

namespace HultUI.Media.Mpv;

/// <summary>
/// An <see cref="IMediaBackend"/> over one libmpv instance. Commands go straight to
/// mpv on the caller's thread (its API is thread-safe); mpv's events are read on a
/// thread of the backend's own and forwarded as <see cref="BackendEvent"/>s; frames
/// come through the render API — into the sink's GL framebuffer on the render
/// thread, or as pixels into the sink's ring from a worker, whichever the surface
/// asked for first.
/// </summary>
public sealed unsafe class MpvBackend : IMediaBackend, IGpuFrameRenderer
{
    private const ulong ObservePause = 1, ObservePosition = 2, ObserveDuration = 3, ObserveEof = 4,
        ObservePausedForCache = 5, ObserveCacheState = 6;

    private static readonly Lazy<bool> Available = new(() => TryLoad(out _));

    /// <summary>Whether a libmpv could be found and loaded; false means every <see cref="MpvBackend"/> constructor throws.</summary>
    public static bool IsAvailable => Available.Value;

    private nint _mpv;
    private readonly Thread _eventThread;
    private volatile bool _stopping;
    private IVideoSink? _sink;
    private GCHandle _self;

    // Playback bookkeeping, touched on the event thread and read on the UI thread.
    private volatile bool _loaded;
    private volatile bool _eof;
    private bool _paused = true;
    private int _width, _height;
    private double _sampleAspect = 1;
    private long _lastPositionTicks;

    // Rendering.
    private VideoSinkMode _mode;
    private nint _render;                 // mpv_render_context, GL or sw
    private GCHandle _procAddressHandle;   // pins the resolver for mpv's callback
    private Func<string, nint>? _getProcAddress;
    private Thread? _swThread;
    private readonly AutoResetEvent _swWake = new(false);
    private volatile bool _swStopping;
    // With vo=libmpv a file cannot start until a render context exists, and the
    // context's kind is only known once the element has been drawn. A source opened
    // before that waits here and loads the moment the context is up.
    private string? _pendingTarget;
    private readonly object _loadGate = new();

    public MpvBackend()
    {
        if (!IsAvailable)
            throw new DllNotFoundException(
                "libmpv was not found. Put libmpv-2.dll / libmpv.so.2 / libmpv.2.dylib beside the application, " +
                "on the library path, or point HULTUI_MPV_PATH at it — see HultUI.Media.Mpv's README.");

        _mpv = mpv_create();
        if (_mpv == 0) throw new InvalidOperationException("mpv_create failed.");

        // The render API owns the picture; nothing else may open a window.
        Check(mpv_set_option_string(_mpv, "vo", "libmpv"));
        // A source loads paused and says what it is before anything moves.
        Check(mpv_set_option_string(_mpv, "pause", "yes"));
        // Reaching the end keeps the last frame instead of unloading the file.
        Check(mpv_set_option_string(_mpv, "keep-open", "yes"));
        Check(mpv_set_option_string(_mpv, "idle", "yes"));
        // Best effort: these options only exist in builds with the feature they
        // belong to (the OSC and ytdl hook are Lua scripts, "terminal" is the
        // command-line player's), and the trimmed LGPL build has none of them.
        mpv_set_option_string(_mpv, "input-default-bindings", "no");
        mpv_set_option_string(_mpv, "input-vo-keyboard", "no");
        mpv_set_option_string(_mpv, "osc", "no");
        mpv_set_option_string(_mpv, "terminal", "no");
        mpv_set_option_string(_mpv, "ytdl", "no");
        // Decode on the GPU where there is a decoder, copy back for mpv's GL pipeline:
        // the mode that works with a desktop GL context on every platform.
        mpv_set_option_string(_mpv, "hwdec", "auto-copy");
        // No headroom: mpv signals a frame when it is due, not early with a sleep in
        // the render call to make up the difference (see Render, which also turns
        // that sleep off). The loop then wakes and draws it.
        mpv_set_option_string(_mpv, "video-timing-offset", "0");
        // HULTUI_MPV_LOG=warn|info|v|debug forwards mpv's own log to stderr: the
        // fastest way to learn why a source would not open.
        if (Environment.GetEnvironmentVariable("HULTUI_MPV_LOG") is { Length: > 0 } logLevel)
            mpv_request_log_messages(_mpv, logLevel);
        Check(mpv_initialize(_mpv));

        Check(mpv_observe_property(_mpv, ObservePause, "pause", MPV_FORMAT_FLAG));
        Check(mpv_observe_property(_mpv, ObservePosition, "time-pos", MPV_FORMAT_DOUBLE));
        Check(mpv_observe_property(_mpv, ObserveDuration, "duration", MPV_FORMAT_DOUBLE));
        Check(mpv_observe_property(_mpv, ObserveEof, "eof-reached", MPV_FORMAT_FLAG));
        Check(mpv_observe_property(_mpv, ObservePausedForCache, "paused-for-cache", MPV_FORMAT_FLAG));
        Check(mpv_observe_property(_mpv, ObserveCacheState, "cache-buffering-state", MPV_FORMAT_INT64));

        _self = GCHandle.Alloc(this);
        _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mpv-events" };
        _eventThread.Start();
    }

    public BackendCapabilities Capabilities =>
        BackendCapabilities.PixelFrames | BackendCapabilities.GpuFrames | BackendCapabilities.Uris
        | BackendCapabilities.Rate | BackendCapabilities.Subtitles | BackendCapabilities.PreciseSeek;

    public event Action<BackendEvent>? Event;

    /// <summary>How frames are being delivered; undecided until the first draw.</summary>
    public VideoSinkMode Mode => _mode;

    // ──── Commands (UI thread) ────

    public void Attach(IVideoSink sink)
    {
        _sink = sink;
        sink.UseGpuRenderer(this);
    }

    public void Open(MediaSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _loaded = false;
        _eof = false;
        Raise(new BackendEvent.StateChanged(PlaybackState.Opening));

        string? target = source.Kind switch
        {
            MediaSourceKind.File => source.Path,
            MediaSourceKind.Uri => source.Url,
            _ => null,
        };
        if (target is null)
        {
            Raise(new BackendEvent.Failed("The mpv backend plays files and URLs, not streams."));
            return;
        }

        lock (_loadGate)
        {
            if (_render == 0)
            {
                _pendingTarget = target;
                return;
            }
        }
        Load(target);
    }

    private void Load(string target)
    {
        SetFlag(_mpv, "pause", true);
        int error = Command(_mpv, "loadfile", target, "replace");
        if (error < 0) Raise(new BackendEvent.Failed(ErrorString(error)));
    }

    /// <summary>Loads the source that was waiting for a render context, if any.</summary>
    private void LoadPending()
    {
        string? target;
        lock (_loadGate)
        {
            target = _pendingTarget;
            _pendingTarget = null;
        }
        if (target is not null) Load(target);
    }

    public void Play()
    {
        if (!_loaded) return;
        if (_eof)
        {
            // keep-open holds the last frame; unpausing there does nothing.
            Command(_mpv, "seek", "0", "absolute");
            _eof = false;
        }
        SetFlag(_mpv, "pause", false);
    }

    public void Pause()
    {
        if (!_loaded) return;
        SetFlag(_mpv, "pause", true);
    }

    public void Stop()
    {
        if (!_loaded) return;
        SetFlag(_mpv, "pause", true);
        Command(_mpv, "seek", "0", "absolute");
    }

    public void Seek(TimeSpan position, bool precise)
    {
        if (!_loaded) return;
        Command(_mpv, "seek", position.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            precise ? "absolute+exact" : "absolute+keyframes");
    }

    public void SetRate(double rate) => SetDouble(_mpv, "speed", rate);
    public void SetVolume(float volume) => SetDouble(_mpv, "volume", volume * 100);
    public void SetMuted(bool muted) => SetFlag(_mpv, "mute", muted);
    public void SelectAudioTrack(int? id) => mpv_set_property_string(_mpv, "aid", id?.ToString() ?? "no");
    public void SelectSubtitleTrack(int? id) => mpv_set_property_string(_mpv, "sid", id?.ToString() ?? "no");

    // ──── Events (mpv thread) ────

    private void EventLoop()
    {
        while (!_stopping)
        {
            var e = mpv_wait_event(_mpv, 0.5);
            if (e is null || e->event_id == MPV_EVENT_NONE) continue;
            try { Handle(e); }
            catch (Exception ex) { Console.Error.WriteLine($"[HultUI.Media.Mpv] event handler failed: {ex}"); }
        }
    }

    private void Handle(mpv_event* e)
    {
        switch (e->event_id)
        {
            case MPV_EVENT_LOG_MESSAGE:
                var log = (mpv_event_log_message*)e->data;
                Console.Error.Write($"[mpv {Marshal.PtrToStringUTF8((nint)log->prefix)}] {Marshal.PtrToStringUTF8((nint)log->text)}");
                break;

            case MPV_EVENT_FILE_LOADED:
                ReadGeometry();
                Raise(new BackendEvent.DurationChanged(ReadDuration()));
                RaiseGeometry();
                RaiseTracks();
                RaiseChapters();
                _loaded = true;
                _eof = false;
                Raise(new BackendEvent.StateChanged(_paused ? PlaybackState.Paused : PlaybackState.Playing));
                break;

            case MPV_EVENT_VIDEO_RECONFIG:
                if (_loaded)
                {
                    ReadGeometry();
                    RaiseGeometry();
                    if (_mode == VideoSinkMode.Pixels) _swWake.Set();
                }
                break;

            case MPV_EVENT_END_FILE:
                var end = (mpv_event_end_file*)e->data;
                if (end->reason == MPV_END_FILE_REASON_ERROR)
                {
                    _loaded = false;
                    Raise(new BackendEvent.Failed(ErrorString(end->error)));
                }
                // EOF never arrives with keep-open; a stop or replace is our own doing.
                break;

            case MPV_EVENT_PROPERTY_CHANGE:
                var prop = (mpv_event_property*)e->data;
                if (prop->data is null) break;
                switch (e->reply_userdata)
                {
                    case ObservePause:
                        _paused = *(int*)prop->data != 0;
                        if (_loaded && !_eof)
                            Raise(new BackendEvent.StateChanged(_paused ? PlaybackState.Paused : PlaybackState.Playing));
                        break;
                    case ObservePosition:
                        // mpv reports every frame; a scrubber needs a few a second.
                        long now = Stopwatch.GetTimestamp();
                        if (now - _lastPositionTicks < Stopwatch.Frequency / 10) break;
                        _lastPositionTicks = now;
                        Raise(new BackendEvent.PositionChanged(TimeSpan.FromSeconds(*(double*)prop->data)));
                        break;
                    case ObserveDuration:
                        if (_loaded) Raise(new BackendEvent.DurationChanged(TimeSpan.FromSeconds(*(double*)prop->data)));
                        break;
                    case ObserveEof:
                        bool eof = *(int*)prop->data != 0;
                        if (eof && _loaded && !_eof)
                        {
                            _eof = true;
                            Raise(new BackendEvent.EndReached());
                        }
                        else if (!eof && _eof && _loaded)
                        {
                            _eof = false;
                            Raise(new BackendEvent.StateChanged(_paused ? PlaybackState.Paused : PlaybackState.Playing));
                        }
                        break;
                    case ObservePausedForCache:
                        if (*(int*)prop->data == 0) Raise(new BackendEvent.BufferingChanged(null));
                        else Raise(new BackendEvent.BufferingChanged((GetInt64(_mpv, "cache-buffering-state") ?? 0) / 100f));
                        break;
                    case ObserveCacheState:
                        if (GetFlag(_mpv, "paused-for-cache") == true)
                            Raise(new BackendEvent.BufferingChanged(*(long*)prop->data / 100f));
                        break;
                }
                break;
        }
    }

    private TimeSpan? ReadDuration()
    {
        var seconds = GetDouble(_mpv, "duration");
        return seconds is { } s && s > 0 ? TimeSpan.FromSeconds(s) : null;
    }

    private void ReadGeometry()
    {
        _width = (int)(GetInt64(_mpv, "video-params/w") ?? 0);
        _height = (int)(GetInt64(_mpv, "video-params/h") ?? 0);
        _sampleAspect = GetDouble(_mpv, "video-params/par") is { } par && par > 0 ? par : 1;
    }

    private void RaiseGeometry()
    {
        if (_width > 0 && _height > 0)
            Raise(new BackendEvent.VideoGeometry(_width, _height, _sampleAspect));
    }

    /// <summary>
    /// The file's own chapter marks. mpv exposes them as a list property, the same shape
    /// as the track list; a container with none simply reports a count of zero.
    /// </summary>
    private void RaiseChapters()
    {
        long count = GetInt64(_mpv, "chapter-list/count") ?? 0;
        if (count <= 0)
        {
            Raise(new BackendEvent.ChaptersChanged([]));
            return;
        }

        var chapters = new List<MediaChapterMark>((int)count);
        for (int i = 0; i < count; i++)
        {
            string prefix = $"chapter-list/{i}/";
            double start = GetDouble(_mpv, prefix + "time") ?? 0;
            if (start < 0) continue;
            chapters.Add(new MediaChapterMark(TimeSpan.FromSeconds(start), GetString(_mpv, prefix + "title")));
        }
        Raise(new BackendEvent.ChaptersChanged(chapters));
    }

    private void RaiseTracks()
    {
        var audio = new List<MediaTrack>();
        var subtitles = new List<MediaTrack>();
        long count = GetInt64(_mpv, "track-list/count") ?? 0;
        for (int i = 0; i < count; i++)
        {
            string prefix = $"track-list/{i}/";
            string? type = GetString(_mpv, prefix + "type");
            if (type is not ("audio" or "sub")) continue;
            int id = (int)(GetInt64(_mpv, prefix + "id") ?? 0);
            string? lang = GetString(_mpv, prefix + "lang");
            // Both halves, not whichever exists: a release with an English and a Japanese
            // track both titled "Stereo" is otherwise a menu of two identical entries.
            string title = MediaTrack.Describe(GetString(_mpv, prefix + "title"), lang, id);
            (type == "audio" ? audio : subtitles).Add(new MediaTrack(id, title, lang));
        }
        Raise(new BackendEvent.TracksChanged(audio, subtitles, ReadSelected("aid"), ReadSelected("sid")));
    }

    private int? ReadSelected(string property)
        => int.TryParse(GetString(_mpv, property), out int id) ? id : null;

    private void Raise(BackendEvent e) => Event?.Invoke(e);

    /// <summary>Reads a numeric mpv property, for tests.</summary>
    internal double Peek(string property) => GetDouble(_mpv, property) ?? double.NaN;

    /// <summary>Reads an mpv property as mpv prints it, for tests.</summary>
    internal string? PeekString(string property) => GetString(_mpv, property);

    private static void Check(int error)
    {
        if (error < 0) throw new InvalidOperationException($"libmpv: {ErrorString(error)}");
    }

    // ──── Frames: IGpuFrameRenderer ────

    public bool RendersBottomUp => false;   // without FLIP_Y mpv fills an FBO top row first

    public bool TryGetFrameSize(out int width, out int height)
    {
        width = _width;
        height = _height;
        return _loaded && width > 0 && height > 0;
    }

    public void OnModeDecided(VideoSinkMode mode)
    {
        _mode = mode;
        if (mode != VideoSinkMode.Pixels) return;   // GL waits for PrepareGL, inside the frame
        // Software rendering: mpv's sw renderer writes into the ring from a thread of
        // its own, woken by mpv's update callback, so the UI thread never waits on it.
        CreateSoftwareContext();
        _swThread = new Thread(SoftwareLoop) { IsBackground = true, Name = "mpv-sw-render" };
        _swThread.Start();
        _swWake.Set();
        LoadPending();
    }

    public void PrepareGL(Func<string, nint> getProcAddress)
    {
        if (_render == 0) CreateGLContext(getProcAddress);
        LoadPending();
    }

    public bool Render(Func<string, nint> getProcAddress, in GpuFrameTarget target, int width, int height)
    {
        if (_render == 0)
            CreateGLContext(getProcAddress);
        if (_render == 0) return false;

        ApplyTargetColour(target);

        if ((mpv_render_context_update(_render) & MPV_RENDER_UPDATE_FRAME) == 0 && _lastGLFrameDrawn)
            return false;   // nothing new; the texture still holds the last frame

        // Zero means "whatever the FBO already has", which is right for the sink's own
        // RGBA8 texture and wrong for a renderer-lent half-float one: told nothing, mpv
        // assumes eight bits and dithers to them inside a target that holds sixteen.
        var fbo = new mpv_opengl_fbo
        {
            fbo = (int)target.Framebuffer,
            w = width,
            h = height,
            internal_format = (int)target.InternalFormat,
        };
        int flip = 0;
        // By default mpv hands a frame over early and then sleeps inside this call
        // until the frame's display time — on the UI thread, inside the frame, so the
        // whole window ran at the video's rate (a resize at 24 fps). With blocking off
        // and video-timing-offset at 0 (set at start-up), mpv only signals when a frame
        // is due, and the loop draws it on the frame it wakes for.
        int block = 0;
        var parameters = stackalloc mpv_render_param[4];
        parameters[0] = new mpv_render_param { type = MPV_RENDER_PARAM_OPENGL_FBO, data = &fbo };
        parameters[1] = new mpv_render_param { type = MPV_RENDER_PARAM_FLIP_Y, data = &flip };
        parameters[2] = new mpv_render_param { type = MPV_RENDER_PARAM_BLOCK_FOR_TARGET_TIME, data = &block };
        parameters[3] = default;
        _lastGLFrameDrawn = mpv_render_context_render(_render, parameters) >= 0;
        return _lastGLFrameDrawn;
    }

    private bool _lastGLFrameDrawn;

    /// <summary>
    /// libplacebo's linear output puts 1.0 at the BT.2408 reference white of 203 nits.
    ///
    /// <para>Measured rather than assumed: <c>spikes/HdrInterop --range</c> reads back a peak
    /// of exactly 1.0000 at <c>target-peak=203</c>, 2.0000 at 406 and 5.0000 at 1015. It is
    /// also the number the Wayland colour-management protocol names as the reference white of
    /// Windows-scRGB, which is why the two ends of this pipeline need no fudge between
    /// them.</para>
    /// </summary>
    private const float LibplaceboReferenceWhiteNits = 203f;

    /// <summary>What <c>target-peak</c> is told when the renderer does not know the display's
    /// peak. A common mastering peak, and wrong on a given monitor in a way nothing here can
    /// detect — neither platform exposes a peak query through this SDL binding.</summary>
    private const float AssumedPeakNits = 1000f;

    private bool _linearTarget;
    private float _appliedPeak;

    /// <inheritdoc />
    public float ReferenceWhiteNits => _linearTarget ? LibplaceboReferenceWhiteNits : 0f;

    /// <summary>
    /// Tells mpv what it is drawing into, when that has changed.
    ///
    /// <para>There is no render parameter for this — <c>mpv_opengl_fbo</c> carries a
    /// framebuffer, a size and an internal format, and no <c>MPV_RENDER_PARAM</c> names a
    /// colour space, which is why <c>target-colorspace-hint</c> has never done anything
    /// through this API. The options are the way in, and they do reach libplacebo: an encoded
    /// mid-grey of 0.502 comes back at 0.192 under <c>target-trc=linear</c>, which is that
    /// value decoded through gamma 2.4.</para>
    ///
    /// <para>Without <c>target-peak</c> nothing exceeds 1.0 however the transfer is set, so
    /// this is the option that decides whether HDR content keeps its range or is tone-mapped
    /// away before it reaches the target.</para>
    /// </summary>
    private void ApplyTargetColour(in GpuFrameTarget target)
    {
        float peak = target.PeakNits > 0f ? target.PeakNits : AssumedPeakNits;
        if (target.WantsLinearLight == _linearTarget && (!_linearTarget || peak == _appliedPeak))
            return;

        _linearTarget = target.WantsLinearLight;
        _appliedPeak = peak;

        if (_linearTarget)
        {
            // scRGB's primaries are BT.709; the transfer is linear light; the peak is what
            // the display can show, which decides where tone mapping lands.
            int a = mpv_set_property_string(_mpv, "target-prim", "bt.709");
            int b = mpv_set_property_string(_mpv, "target-trc", "linear");
            int c = mpv_set_property_string(_mpv, "target-peak",
                peak.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            // Read them back rather than trusting the return code: a property that reports
            // success and then reads as something else is the difference between "mpv ignored
            // us" and "mpv agreed and something later undid it".
            Console.Error.WriteLine($"[mpv] linear target set: prim={a} trc={b} peak={c}; " +
                $"reads back prim={Read("target-prim")} trc={Read("target-trc")} " +
                $"peak={Read("target-peak")} tone-mapping={Read("tone-mapping")} " +
                $"inverse={Read("inverse-tone-mapping")} target-contrast={Read("target-contrast")}");

            string Read(string name) => GetString(_mpv, name) ?? "(unset)";
        }
        else
        {
            mpv_set_property_string(_mpv, "target-prim", "auto");
            mpv_set_property_string(_mpv, "target-trc", "auto");
            mpv_set_property_string(_mpv, "target-peak", "auto");
        }
    }

    private void CreateGLContext(Func<string, nint> getProcAddress)
    {
        _getProcAddress = getProcAddress;
        _procAddressHandle = GCHandle.Alloc(getProcAddress);
        var apiType = "opengl\0"u8.ToArray();
        fixed (byte* api = apiType)
        {
            var init = new mpv_opengl_init_params
            {
                get_proc_address = &GetProcAddressThunk,
                get_proc_address_ctx = (void*)GCHandle.ToIntPtr(_procAddressHandle),
            };
            var parameters = stackalloc mpv_render_param[3];
            parameters[0] = new mpv_render_param { type = MPV_RENDER_PARAM_API_TYPE, data = api };
            parameters[1] = new mpv_render_param { type = MPV_RENDER_PARAM_OPENGL_INIT_PARAMS, data = &init };
            parameters[2] = default;
            nint context;
            int error = mpv_render_context_create(&context, _mpv, parameters);
            if (error < 0)
            {
                Raise(new BackendEvent.Failed($"mpv could not start GL rendering: {ErrorString(error)}"));
                return;
            }
            _render = context;
        }
        mpv_render_context_set_update_callback(_render, &UpdateThunk, (void*)GCHandle.ToIntPtr(_self));
    }

    private void CreateSoftwareContext()
    {
        var apiType = "sw\0"u8.ToArray();
        fixed (byte* api = apiType)
        {
            var parameters = stackalloc mpv_render_param[2];
            parameters[0] = new mpv_render_param { type = MPV_RENDER_PARAM_API_TYPE, data = api };
            parameters[1] = default;
            nint context;
            int error = mpv_render_context_create(&context, _mpv, parameters);
            if (error < 0)
            {
                Raise(new BackendEvent.Failed($"mpv could not start software rendering: {ErrorString(error)}"));
                return;
            }
            _render = context;
        }
        mpv_render_context_set_update_callback(_render, &UpdateThunk, (void*)GCHandle.ToIntPtr(_self));
    }

    private void SoftwareLoop()
    {
        var format = "rgb0\0"u8.ToArray();
        var size = stackalloc int[2];
        var parameters = stackalloc mpv_render_param[5];
        nuint stride = 0;
        fixed (byte* fmt = format)
        {
            while (!_swStopping)
            {
                _swWake.WaitOne(500);
                if (_swStopping || _render == 0 || _sink is null) continue;
                if ((mpv_render_context_update(_render) & MPV_RENDER_UPDATE_FRAME) == 0) continue;
                if (!TryGetFrameSize(out int width, out int height)) continue;

                using var writer = _sink.Pixels.BeginWrite(width, height, PixelFormat.Rgbx8888);
                size[0] = width;
                size[1] = height;
                stride = (nuint)writer.Stride;
                parameters[0] = new mpv_render_param { type = MPV_RENDER_PARAM_SW_SIZE, data = size };
                parameters[1] = new mpv_render_param { type = MPV_RENDER_PARAM_SW_FORMAT, data = fmt };
                parameters[2] = new mpv_render_param { type = MPV_RENDER_PARAM_SW_STRIDE, data = &stride };
                parameters[3] = new mpv_render_param { type = MPV_RENDER_PARAM_SW_POINTER, data = (void*)writer.Address };
                parameters[4] = default;
                if (mpv_render_context_render(_render, parameters) >= 0)
                    writer.Publish();
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void* GetProcAddressThunk(void* ctx, byte* name)
    {
        try
        {
            var resolve = (Func<string, nint>)GCHandle.FromIntPtr((nint)ctx).Target!;
            return (void*)resolve(Marshal.PtrToStringUTF8((nint)name) ?? "");
        }
        catch { return null; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void UpdateThunk(void* userdata)
    {
        try
        {
            if (GCHandle.FromIntPtr((nint)userdata).Target is MpvBackend self)
            {
                if (self._mode == VideoSinkMode.Pixels) self._swWake.Set();
                else self._sink?.RequestRepaint();
            }
        }
        catch { /* never let an exception cross into mpv */ }
    }

    public void ReleaseGL()
    {
        if (_mode != VideoSinkMode.Gpu || _render == 0) return;
        mpv_render_context_free(_render);
        _render = 0;
        if (_procAddressHandle.IsAllocated) _procAddressHandle.Free();
    }

    // ──── Teardown ────

    public void Dispose()
    {
        if (_mpv == 0) return;
        _stopping = true;
        _swStopping = true;
        _swWake.Set();
        mpv_wakeup(_mpv);
        _eventThread.Join(TimeSpan.FromSeconds(5));
        _swThread?.Join(TimeSpan.FromSeconds(5));

        // The render context goes before the core — mpv requires the order — and a GL
        // one needs the context current, which is why the sink calls ReleaseGL from the
        // UI thread first; this is the software one, or a GL one nobody released.
        if (_render != 0)
        {
            mpv_render_context_free(_render);
            _render = 0;
        }
        if (_procAddressHandle.IsAllocated) _procAddressHandle.Free();
        mpv_terminate_destroy(_mpv);
        _mpv = 0;
        if (_self.IsAllocated) _self.Free();
        _swWake.Dispose();
    }
}
