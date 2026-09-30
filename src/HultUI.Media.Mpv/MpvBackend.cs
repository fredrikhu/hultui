using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HultUI.Core.DevTools;
using HultUI.Core.Rendering;
using static HultUI.Media.Mpv.Mpv;

namespace HultUI.Media.Mpv;

/// <summary>
/// An <see cref="IMediaBackend"/> over one libmpv instance. Commands are queued to mpv
/// without waiting, because the UI thread is also mpv's render thread; mpv's events and the
/// commands' replies are read on a thread of the backend's own and forwarded as
/// <see cref="BackendEvent"/>s; frames
/// come through the render API — into the sink's GL framebuffer on the render
/// thread, or as pixels into the sink's ring from a worker, whichever the surface
/// asked for first.
/// </summary>
public sealed unsafe class MpvBackend : IMediaBackend, IGpuFrameRenderer
{
    private const ulong ObservePause = 1, ObservePosition = 2, ObserveDuration = 3, ObserveEof = 4,
        ObservePausedForCache = 5, ObserveCacheState = 6;

    private static readonly Lazy<(bool Loaded, string? Error)> Available =
        new(() => (TryLoad(out _, out var error), error));

    /// <summary>Whether a libmpv could be found and loaded; false means every <see cref="MpvBackend"/> constructor throws.</summary>
    public static bool IsAvailable => Available.Value.Loaded;

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
    private GpuSinkMode _mode;
    private nint _render;                 // mpv_render_context, GL or sw
    private GCHandle _procAddressHandle;   // pins the resolver for mpv's callback
    private Func<string, nint>? _getProcAddress;
    private Thread? _swThread;
    private readonly AutoResetEvent _swWake = new(false);
    private volatile bool _swStopping;
    // Set by mpv's update callback, cleared when the render thread asks. Lets a frame with no
    // size to draw at skip only when mpv may be waiting, since entering GL costs a flush.
    private volatile bool _updateWaiting;
    // With vo=libmpv a file cannot start until a render context exists, and the
    // context's kind is only known once the element has been drawn. A source opened
    // before that waits here and loads the moment the context is up.
    private string? _pendingTarget;
    private readonly object _loadGate = new();

    public MpvBackend()
    {
        if (!IsAvailable)
            throw new DllNotFoundException(Available.Value.Error is { } error
                // Found, and would not load: the reason is what a person needs, not a hunt for
                // a file they already have. Usually a dependency of libmpv's own.
                ? $"libmpv was found but could not be loaded — {error}"
                : "libmpv was not found. Put libmpv-2.dll / libmpv.so.2 / libmpv.2.dylib beside the application, " +
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
        // "auto-safe" tries the zero-copy methods first — mpv imports the decoder's surface
        // into its own GL textures and renders from there, no trip through main memory — and
        // then the -copy methods "auto-copy" would have used, so it is never worse. On NVIDIA
        // the bundled libmpv lands on nvdec with CUDA interop, on both platforms; elsewhere on
        // Windows it lands on d3d11va-copy, because the DirectX interops would bridge at eight
        // bits and stay disabled (docs/design/HDR.md, Decode).
        //
        // "auto-safe" rather than "auto": the same on mpv 0.40 and later, but on an older
        // distribution libmpv loaded through HULTUI_MPV_PATH, "auto" skips the whitelist and
        // may pick methods known to be broken.
        //
        // HULTUI_MPV_HWDEC overrides it with any mode mpv understands. mpv falls back to
        // software on its own if the mode it is given cannot initialise.
        mpv_set_option_string(_mpv, "hwdec",
            Environment.GetEnvironmentVariable("HULTUI_MPV_HWDEC") is { Length: > 0 } mode
                ? mode : "auto-safe");
        // No headroom: mpv signals a frame when it is due, not early with a sleep in
        // the render call to make up the difference (see Render, which also turns
        // that sleep off). The loop then wakes and draws it.
        mpv_set_option_string(_mpv, "video-timing-offset", "0");
        // HULTUI_MPV_LOG=warn|info|v|debug forwards mpv's own log to stderr: the
        // fastest way to learn why a source would not open.
        if (Environment.GetEnvironmentVariable("HULTUI_MPV_LOG") is { Length: > 0 } logLevel)
            mpv_request_log_messages(_mpv, logLevel);
        ApplyExtraOptions();
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
    public GpuSinkMode Mode => _mode;

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

    // ──── Commands (UI thread) ────
    //
    // The UI thread is also mpv's render thread, and mpv's render API forbids a render thread
    // from waiting on its core: after every seek and load the core waits for the render thread
    // to draw the first frame. A synchronous seek arriving in that window waited out mpv's
    // 200 ms timeout. Scrubbing at the rate a mouse drag sends seeks froze the UI for 200 ms on
    // nearly every one. So every command here is queued and answered on the event thread. mpv
    // runs them in the order they were queued.

    /// <summary>The reply tag of a loadfile, whose refusal is reported; other replies are ignored,
    /// as the synchronous calls' results were.</summary>
    private const ulong LoadReply = 0x100;

    private void Load(string target)
    {
        SetFlagAsync(_mpv, 0, "pause", true);
        int error = CommandAsync(_mpv, LoadReply, "loadfile", target, "replace");
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
            CommandAsync(_mpv, 0, "seek", "0", "absolute");
            _eof = false;
        }
        SetFlagAsync(_mpv, 0, "pause", false);
    }

    public void Pause()
    {
        if (!_loaded) return;
        SetFlagAsync(_mpv, 0, "pause", true);
    }

    public void Stop()
    {
        if (!_loaded) return;
        SetFlagAsync(_mpv, 0, "pause", true);
        CommandAsync(_mpv, 0, "seek", "0", "absolute");
    }

    public void Seek(TimeSpan position, bool precise)
    {
        if (!_loaded) return;
        CommandAsync(_mpv, 0, "seek",
            position.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            precise ? "absolute+exact" : "absolute+keyframes");
    }

    public void SetRate(double rate) => SetDoubleAsync(_mpv, 0, "speed", rate);
    public void SetVolume(float volume) => SetDoubleAsync(_mpv, 0, "volume", volume * 100);
    public void SetMuted(bool muted) => SetFlagAsync(_mpv, 0, "mute", muted);
    public void SelectAudioTrack(int? id) => SetStringAsync(_mpv, 0, "aid", id?.ToString() ?? "no");
    public void SelectSubtitleTrack(int? id) => SetStringAsync(_mpv, 0, "sid", id?.ToString() ?? "no");

    // ──── Events (mpv thread) ────

    private void EventLoop()
    {
        while (!_stopping)
        {
            var e = mpv_wait_event(_mpv, 0.5);
            if (_traceHwdec)
            {
                _traceHwdec = false;
                StartupTrace.Mark($"mpv hwdec: asked {GetString(_mpv, "hwdec")}, " +
                    $"using {GetString(_mpv, "hwdec-current") ?? "(none)"}");
            }
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

            case MPV_EVENT_SET_PROPERTY_REPLY:
                HandlePropertyReply(e);
                break;

            case MPV_EVENT_COMMAND_REPLY:
                if (e->reply_userdata == LoadReply && e->error < 0)
                    Raise(new BackendEvent.Failed(ErrorString(e->error)));
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
                    if (_mode == GpuSinkMode.Pixels) _swWake.Set();
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

    public void OnModeDecided(GpuSinkMode mode)
    {
        _mode = mode;
        if (mode != GpuSinkMode.Pixels) return;   // GL waits for PrepareGL, inside the frame
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
        _updateWaiting = false;

        if (_colour.Update(target.WantsLinearLight, target.PeakNits, target.WideGamut)) ApplyTargetColour();

        // Nothing new, so the texture still holds the last frame — unless mpv has since
        // confirmed a change of target colour, in which case that frame was drawn for the old
        // options and is drawn again. Confirmed, not merely asked for: the options are set
        // asynchronously, and a redraw before mpv has them would draw the old picture again.
        if ((mpv_render_context_update(_render) & MPV_RENDER_UPDATE_FRAME) == 0 && _lastGLFrameDrawn
            && !_colour.AppliedSinceLastFrame)
            return false;

        // Passed for completeness: mpv 0.41 ignores it (ra_create_wrapped_fb hard-codes zero).
        // What stops it dithering a half-float target down to eight bits is DEPTH, below.
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
        int depth = DepthOf(target.InternalFormat);
        var parameters = stackalloc mpv_render_param[5];
        parameters[0] = new mpv_render_param { type = MPV_RENDER_PARAM_OPENGL_FBO, data = &fbo };
        parameters[1] = new mpv_render_param { type = MPV_RENDER_PARAM_FLIP_Y, data = &flip };
        parameters[2] = new mpv_render_param { type = MPV_RENDER_PARAM_BLOCK_FOR_TARGET_TIME, data = &block };
        parameters[3] = new mpv_render_param { type = MPV_RENDER_PARAM_DEPTH, data = &depth };
        parameters[4] = default;
        _lastGLFrameDrawn = mpv_render_context_render(_render, parameters) >= 0;
        if (_lastGLFrameDrawn) _colour.FrameDrawn();

        // What mpv actually chose, not what it was asked for. "auto-safe" falls back on its own
        // when a decoder or its interop will not initialise, and a silent fallback looks
        // exactly like the mode working — the CPU moves either way, because the fallback is
        // also hardware decoding. Read on the event thread, because this one must not wait on
        // mpv's core.
        if (_lastGLFrameDrawn && !_tracedHwdec && StartupTrace.Enabled)
        {
            _tracedHwdec = true;
            _traceHwdec = true;
            mpv_wakeup(_mpv);
        }
        return _lastGLFrameDrawn;
    }

    private bool _lastGLFrameDrawn;
    private bool _tracedHwdec;
    private volatile bool _traceHwdec;

    /// <inheritdoc />
    public bool FrameMayBeWaiting => _updateWaiting;

    /// <summary>
    /// Takes mpv's waiting frame without drawing it, so mpv stops waiting and the size can be
    /// read. After every load, and before its first frame is shown, mpv's core waits for that
    /// frame to be rendered, for up to 200 ms. The size comes from the core, so it cannot arrive
    /// while the core waits, and the app cannot render without it. Each side waited for the
    /// other until mpv timed out, logged "mpv_render_context_render() not being called or
    /// stuck", and counted the frame as dropped. Reopening a file took three of those timeouts,
    /// about 600 ms, before the picture came up.
    /// </summary>
    public void SkipFrame(Func<string, nint> getProcAddress)
    {
        _updateWaiting = false;
        if (_render == 0 || (mpv_render_context_update(_render) & MPV_RENDER_UPDATE_FRAME) == 0) return;
        if (TakeFrameWithoutDrawing())
            _lastGLFrameDrawn = false;   // so the first Render with a target draws it
    }

    private bool TakeFrameWithoutDrawing()
    {
        int skip = 1, block = 0;
        var parameters = stackalloc mpv_render_param[3];
        parameters[0] = new mpv_render_param { type = MPV_RENDER_PARAM_SKIP_RENDERING, data = &skip };
        parameters[1] = new mpv_render_param { type = MPV_RENDER_PARAM_BLOCK_FOR_TARGET_TIME, data = &block };
        parameters[2] = default;
        return mpv_render_context_render(_render, parameters) >= 0;
    }

    /// <summary>
    /// How many bits a channel the target holds, for <c>MPV_RENDER_PARAM_DEPTH</c>.
    ///
    /// <para><b>Omitting this is not neutral.</b> The render API says an absent or zero depth
    /// means the renderer assumes 8 and dithers to them, and it does not infer anything from
    /// <c>internal_format</c>: measured on a 10-bit ramp into an RGBA16F target, omitting the
    /// parameter and forcing it to 8 give byte-identical output — 256 distinct levels across
    /// 1024 pixels with 250 direction reversals, which is quantisation plus dither noise.
    /// Saying 16 gives 951 levels and 62 reversals. So the half-float image phase 4 exists to
    /// provide was being filled with a dithered 8-bit picture.</para>
    /// </summary>
    /// <remarks>Capped at 16. mpv computes <c>(1 &lt;&lt; depth) - 1</c> for its dither, which
    /// is undefined at 32 and zero on x86 — a division by zero in the shader. Dithering to 16
    /// bits inside a float target is invisible anyway.</remarks>
    internal static int DepthOf(uint internalFormat) => internalFormat switch
    {
        GlRgba16f or GlRgba32f => 16,
        _ => 8,          // GL_RGBA8, and anything unrecognised: the safe assumption
    };

    private const uint GlRgba16f = 0x881A;
    private const uint GlRgba32f = 0x8814;

    private readonly MpvTargetColour _colour = new();

    /// <inheritdoc />
    /// <remarks>What the frame in the target was drawn with, which is not always what mpv has
    /// just been told — see <see cref="MpvTargetColour"/>.</remarks>
    public float ReferenceWhiteNits => _colour.ReferenceWhiteNits;

    /// <summary>
    /// <c>HULTUI_MPV_OPTIONS</c>: extra mpv options, <c>name=value</c> pairs separated by
    /// <c>;</c>, set before initialisation. Diagnostic: it is how a test clip is told what it is
    /// when its container cannot say — a Y4M of BT.2020 colour bars carries no primaries, and
    /// <c>vf=format=colormatrix=bt.2020-ncl:primaries=bt.2020:gamma=pq</c> supplies them. Only
    /// the first <c>=</c> of a pair splits, so values may contain their own. A pair mpv refuses
    /// is reported and skipped.
    /// </summary>
    private void ApplyExtraOptions()
    {
        if (Environment.GetEnvironmentVariable("HULTUI_MPV_OPTIONS") is not { Length: > 0 } extra) return;
        foreach (var pair in extra.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) { Console.Error.WriteLine($"[HultUI.Media.Mpv] HULTUI_MPV_OPTIONS: '{pair}' is not name=value"); continue; }
            int result = mpv_set_option_string(_mpv, pair[..eq], pair[(eq + 1)..]);
            if (result < 0)
                Console.Error.WriteLine($"[HultUI.Media.Mpv] HULTUI_MPV_OPTIONS: {pair[..eq]} refused: {ErrorString(result)}");
        }
    }

    /// <summary>
    /// Tells mpv what it is drawing into, after <see cref="MpvTargetColour.Update"/> said it
    /// changed.
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
    private void ApplyTargetColour()
    {
        // Asynchronously, and this is not a detail. These run on the render thread inside a
        // frame, and a synchronous mpv_set_property waits for mpv's core — which, with a frame
        // due, is waiting on its video output, which is waiting for the render this thread is
        // holding up. Measured on the first HDR frame: 360 ms, released only by mpv's own
        // timeout, one "render() not being called or stuck" per property. Replies arrive on
        // the event thread; see HandlePropertyReply.
        long start = Stopwatch.GetTimestamp();
        int generation = _colour.Generation;
        bool linear = _colour.Linear;
        string prim = "auto", trc = "auto", peak = "auto";
        if (linear)
        {
            // scRGB's primaries are BT.709; the transfer is linear light; the peak is what
            // the display can show, which decides where tone mapping lands.
            prim = "bt.709";
            trc = "linear";
            peak = _colour.PeakNits.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        }
        // "clip" is not a clamp in mpv's GL renderer: it skips the step that pulls a colour
        // outside the target's gamut back inside by desaturating it toward grey, so BT.2020 and
        // P3 colours arrive as negative BT.709 values, which scRGB shows as the colours they
        // are. "auto" is that desaturation — what an encoded target needs, since it has no
        // negative values to put them in.
        string gamut = _colour.WideGamut ? "clip" : "auto";
        int a = SetStringAsync(_mpv, MpvTargetColour.ReplyTag(generation, linear, 0), "target-prim", prim);
        int b = SetStringAsync(_mpv, MpvTargetColour.ReplyTag(generation, linear, 1), "target-trc", trc);
        int c = SetStringAsync(_mpv, MpvTargetColour.ReplyTag(generation, linear, 2), "target-peak", peak);
        int d = SetStringAsync(_mpv, MpvTargetColour.ReplyTag(generation, linear, 3), "gamut-mapping-mode", gamut);

        if (StartupTrace.Enabled)
            StartupTrace.Mark($"mpv {(linear ? "linear" : "encoded")} target {generation}: asked {prim}/{trc}/{peak}/{gamut}, " +
                $"queued {a}/{b}/{c}/{d} in {Stopwatch.GetElapsedTime(start).TotalMilliseconds:0.00} ms");
    }

    // Event thread only: how many of a change's options mpv has answered for.
    private int _replyGeneration;
    private int _repliesReceived;

    /// <summary>
    /// An answer to one of <see cref="ApplyTargetColour"/>'s options. When all of a change's
    /// have arrived the change is confirmed, and the frame is drawn again under it — the element
    /// may be sitting on a paused picture that nothing else would redraw.
    /// </summary>
    private void HandlePropertyReply(mpv_event* e)
    {
        if (!MpvTargetColour.TryReadReplyTag(e->reply_userdata, out int generation, out bool linear)) return;
        if (e->error < 0)
            Console.Error.WriteLine($"[HultUI.Media.Mpv] setting the target colour failed: {ErrorString(e->error)}");

        if (generation != _replyGeneration)
        {
            _replyGeneration = generation;
            _repliesReceived = 0;
        }
        if (++_repliesReceived < MpvTargetColour.OptionsPerChange) return;

        _colour.Applied(generation, linear);
        if (StartupTrace.Enabled)
            StartupTrace.Mark($"mpv {(linear ? "linear" : "encoded")} target {generation}: confirmed, " +
                $"reads back prim={GetString(_mpv, "target-prim")} trc={GetString(_mpv, "target-trc")} " +
                $"peak={GetString(_mpv, "target-peak")} gamut={GetString(_mpv, "gamut-mapping-mode")}");
        _sink?.RequestRepaint();
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
        // A frame taken without a size (see SkipFrame) and not yet drawn.
        bool owed = false;
        fixed (byte* fmt = format)
        {
            while (!_swStopping)
            {
                _swWake.WaitOne(500);
                if (_swStopping || _render == 0 || _sink is null) continue;
                bool newFrame = (mpv_render_context_update(_render) & MPV_RENDER_UPDATE_FRAME) != 0;
                if (!newFrame && !owed) continue;
                if (!TryGetFrameSize(out int width, out int height))
                {
                    if (newFrame && TakeFrameWithoutDrawing()) owed = true;
                    continue;
                }
                owed = false;

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
                if (self._mode == GpuSinkMode.Pixels) self._swWake.Set();
                else
                {
                    self._updateWaiting = true;
                    self._sink?.RequestRepaint();
                }
            }
        }
        catch { /* never let an exception cross into mpv */ }
    }

    public void ReleaseGL()
    {
        if (_mode != GpuSinkMode.Gpu || _render == 0) return;
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
