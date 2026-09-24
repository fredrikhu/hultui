using System.Globalization;

namespace HultUI.Media.Mpv;

/// <summary>
/// Scrubber stills from a second, silent mpv.
///
/// <para>The playing backend cannot be asked for these: seeking it to make a thumbnail is
/// seeking the film the viewer is watching. So this opens its own handle with no video
/// output, no audio and no subtitles, seeks it around, and writes each frame to a PNG in a
/// temporary folder — <c>screenshot-to-file … video</c>, which takes the decoded frame
/// rather than what a window is showing, and therefore needs no window.</para>
///
/// <para>Deliberately one frame at a time and in order. A seek plus a decode is tens of
/// milliseconds of one core, and this is background work for a picture nobody has asked to
/// see yet; running it flat out would steal from the decode that is actually on screen.</para>
/// </summary>
public sealed class MpvFrameSource : IScrubFrameSource
{
    private readonly string _scratch;
    private nint _handle;
    private bool _disposed;
    private int _n;

    public MpvFrameSource()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "reel-stills", Guid.NewGuid().ToString("N"));
    }

    /// <summary>Whether a decoder is present at all — the same check the playing backend makes.</summary>
    public static bool IsAvailable => MpvBackend.IsAvailable;

    public Task<bool> OpenAsync(string path, CancellationToken cancellationToken)
    {
        if (_disposed || !IsAvailable) return Task.FromResult(false);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_scratch);

            _handle = Mpv.mpv_create();
            if (_handle == 0) return false;

            // Everything off that is not a decoded video frame. hwdec in particular: a
            // second hardware decoder context competes with the one drawing the picture,
            // and software decoding one frame at a time is cheap enough.
            Mpv.mpv_set_option_string(_handle, "vo", "null");
            Mpv.mpv_set_option_string(_handle, "audio", "no");
            Mpv.mpv_set_option_string(_handle, "sub", "no");
            Mpv.mpv_set_option_string(_handle, "hwdec", "no");
            Mpv.mpv_set_option_string(_handle, "pause", "yes");
            Mpv.mpv_set_option_string(_handle, "input-default-bindings", "no");
            Mpv.mpv_set_option_string(_handle, "osc", "no");
            Mpv.mpv_set_option_string(_handle, "load-scripts", "no");
            // A thumbnail is looked at 176 points wide; decoding a 4K frame to shrink it is
            // most of the cost of this whole exercise.
            Mpv.mpv_set_option_string(_handle, "screenshot-format", "png");
            Mpv.mpv_set_option_string(_handle, "screenshot-png-compression", "3");

            if (Mpv.mpv_initialize(_handle) < 0) { Close(); return false; }
            if (Mpv.Command(_handle, "loadfile", path) < 0) { Close(); return false; }

            return WaitForFileLoaded(cancellationToken);
        }, cancellationToken);
    }

    public Task<byte[]?> GrabAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        if (_disposed || _handle == 0) return Task.FromResult<byte[]?>(null);

        return Task.Run<byte[]?>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var seconds = position.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            // "absolute" and not "exact": a keyframe seek is far cheaper, and a still that is
            // a second out is still the right scene. Precision here would cost a full decode
            // from the previous keyframe for every thumbnail.
            if (Mpv.Command(_handle, "seek", seconds, "absolute+keyframes") < 0) return null;
            if (!WaitForSeekDone(cancellationToken)) return null;

            var file = Path.Combine(_scratch, $"{_n++}.png");
            if (Mpv.Command(_handle, "screenshot-to-file", file, "video") < 0) return null;

            try
            {
                if (!File.Exists(file)) return null;
                var bytes = File.ReadAllBytes(file);
                File.Delete(file);       // the decoded image is what is kept, not the PNG
                return bytes.Length == 0 ? null : bytes;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }, cancellationToken);
    }

    /// <summary>Pumps the handle's events until the file is open, or it gives up.</summary>
    private bool WaitForFileLoaded(CancellationToken cancellationToken) =>
        Pump(Mpv.MPV_EVENT_FILE_LOADED, TimeSpan.FromSeconds(20), cancellationToken);

    private bool WaitForSeekDone(CancellationToken cancellationToken) =>
        Pump(Mpv.MPV_EVENT_PLAYBACK_RESTART, TimeSpan.FromSeconds(10), cancellationToken);

    private unsafe bool Pump(int wanted, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evt = Mpv.mpv_wait_event(_handle, 0.05);
            if (evt is null) continue;

            int id = evt->event_id;
            if (id == wanted) return true;
            // The file could not be opened, or ended while we were seeking past its end.
            if (id is Mpv.MPV_EVENT_END_FILE or Mpv.MPV_EVENT_SHUTDOWN) return false;
        }
        return false;
    }

    private void Close()
    {
        if (_handle != 0)
        {
            Mpv.mpv_terminate_destroy(_handle);
            _handle = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        try { if (Directory.Exists(_scratch)) Directory.Delete(_scratch, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
