using static HdrInterop.MpvNative;

namespace HdrInterop;

/// <summary>
/// Is this file HDR, as the thing that will decode it sees it?
///
/// <para>Not the same question as what a media server's badge says. A library can label a
/// film HDR because its container claims so while the stream is BT.709, and it can carry
/// Dolby Vision that this build of libmpv will not read. What decides whether phase 5 has
/// anything to carry is the transfer function and the primaries <b>this</b> libmpv reports
/// after it has opened the file — so that is what this asks, with no window and no
/// rendering.</para>
///
/// <para><c>--probe &lt;file-or-url&gt;</c>. A path, an SMB path, or an HTTP stream URL all
/// work, since mpv opens all three.</para>
/// </summary>
internal static unsafe class Probe
{
    public static int Run(string[] args)
    {
        string? file = args.FirstOrDefault(a => !a.StartsWith('-'));
        if (file is null) { Console.WriteLine("  usage: --probe <file-or-url>"); return 1; }
        if (!MpvNative.IsAvailable) { Console.WriteLine("  no libmpv found (HULTUI_MPV_PATH)"); return 1; }

        Console.WriteLine("── what does libmpv think this file is? ──");
        Console.WriteLine();

        nint mpv = mpv_create();
        if (mpv == 0) { Console.WriteLine("  mpv_create failed"); return 1; }
        try
        {
            mpv_set_option_string(mpv, "vo", "null");
            mpv_set_option_string(mpv, "audio", "no");
            mpv_set_option_string(mpv, "terminal", "no");
            mpv_set_option_string(mpv, "pause", "yes");
            // Decoding on the CPU: this asks what the stream says, and a hardware decoder can
            // hand back a surface whose parameters have already been converted.
            mpv_set_option_string(mpv, "hwdec", "no");
            if (mpv_initialize(mpv) < 0) { Console.WriteLine("  mpv_initialize failed"); return 1; }

            if (Command(mpv, "loadfile", file) < 0) { Console.WriteLine("  loadfile failed"); return 2; }
            if (!WaitFor(mpv, MPV_EVENT_FILE_LOADED, TimeSpan.FromSeconds(30)))
            { Console.WriteLine("  the file never loaded (wrong path, or no codec for it)"); return 2; }

            // The parameters are not filled in until a frame has been decoded.
            WaitFor(mpv, MPV_EVENT_PLAYBACK_RESTART, TimeSpan.FromSeconds(30));

            Console.WriteLine($"  file       : {Property(mpv, "filename") ?? file}");
            Console.WriteLine($"  codec      : {Property(mpv, "video-codec") ?? "?"}");
            Console.WriteLine($"  size       : {Property(mpv, "video-params/w")}x{Property(mpv, "video-params/h")}");
            Console.WriteLine($"  pixel fmt  : {Property(mpv, "video-params/pixelformat") ?? "?"}");
            Console.WriteLine($"  bit depth  : {Property(mpv, "video-params/average-bpp") ?? "?"}");
            Console.WriteLine();

            string? gamma = Property(mpv, "video-params/gamma");
            string? primaries = Property(mpv, "video-params/primaries");
            Console.WriteLine($"  transfer   : {gamma ?? "?"}        <- the one that matters");
            Console.WriteLine($"  primaries  : {primaries ?? "?"}");
            Console.WriteLine($"  colormatrix: {Property(mpv, "video-params/colormatrix") ?? "?"}");
            Console.WriteLine($"  levels     : {Property(mpv, "video-params/colorlevels") ?? "?"}");
            Console.WriteLine($"  max luma   : {Property(mpv, "video-params/max-luma") ?? "(not signalled)"} nits");
            Console.WriteLine($"  min luma   : {Property(mpv, "video-params/min-luma") ?? "(not signalled)"}");
            Console.WriteLine($"  max CLL    : {Property(mpv, "video-params/max-cll") ?? "(not signalled)"}");
            Console.WriteLine();

            // pq is HDR10 and most of what a library holds; hlg is broadcast HDR. Everything
            // else — bt.1886, srgb, gamma2.2 — is SDR whatever the container is labelled.
            bool hdr = gamma is "pq" or "hlg";
            Console.WriteLine(hdr
                ? $"  → HDR ({gamma}). This is what phase 5 needs: it has range above reference white."
                : $"  → SDR (transfer '{gamma}'). Useful as a control, not as an HDR test.");
            if (hdr && primaries != "bt.2020")
                Console.WriteLine($"    (unusual: HDR transfer with {primaries} primaries)");
            if (Property(mpv, "video-params/max-luma") is null && hdr)
                Console.WriteLine("    No mastering luminance in the stream, so target-peak has to be assumed.");
            return hdr ? 0 : 3;
        }
        finally
        {
            mpv_terminate_destroy(mpv);
        }
    }

    private static bool WaitFor(nint mpv, int wanted, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var evt = mpv_wait_event(mpv, 0.05);
            if (evt is null) continue;
            if (evt->event_id == wanted) return true;
            if (evt->event_id is MPV_EVENT_END_FILE or MPV_EVENT_SHUTDOWN) return false;
        }
        return false;
    }
}
