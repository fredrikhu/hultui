using Vortice.Vulkan;

namespace HdrInterop;

/// <summary>
/// What range does mpv actually write into the shared half-float image?
///
/// <para>Phase 4 removed the copy and gave the image sixteen bits a channel, and it was
/// declared finished on that basis. It is not enough for HDR. mpv has no idea the target is
/// an scRGB swapchain — `mpv_opengl_fbo` carries a framebuffer, a size and an internal
/// format, and no `MPV_RENDER_PARAM` names a colour space — so it renders for an SDR sRGB
/// display and tone-maps anything brighter down before it ever reaches the image. Sixteen
/// bits of a range that stops at 1.0 is precision, not headroom.</para>
///
/// <para>The way out is not a render param but mpv's own <c>target-prim</c>,
/// <c>target-trc</c> and <c>target-peak</c>, which describe the display to the renderer
/// directly. Whether they reach libplacebo through the render API, and what numbers come out
/// when they do, is the question here — asked in the spike because it costs a readback, and
/// because the answer decides what the framework seam has to carry.</para>
///
/// <para><c>--range &lt;file&gt; [key=value ...]</c>; every extra argument is passed to mpv
/// as an option, so a combination can be tried without a rebuild.</para>
/// </summary>
internal static unsafe class RangeBench
{
    private static int Width = 320;
    private static int Height = 180;

    public static int Run(string[] args)
    {
        string? file = args.FirstOrDefault(a => !a.StartsWith('-') && !a.Contains('='));
        if (file is null)
        {
            Console.WriteLine("  usage: --range <file> [mpv-option=value ...]");
            return 1;
        }

        // "late.key=value" is applied after mpv_initialize and after the render context
        // exists — where the real backend is forced to set it, and the only difference left
        // between this spike and an app that gets no range out of the same options.
        var all = args
            .Where(a => a.Contains('=') && !a.StartsWith('-'))
            .Select(a => a.Split('=', 2))
            .Select(parts => (Key: parts[0], Value: parts[1]))
            .ToArray();
        var options = all.Where(o => !o.Key.StartsWith("late.") && !o.Key.StartsWith("post.")).ToArray();
        var lateOptions = all.Where(o => o.Key.StartsWith("late."))
            .Select(o => (o.Key["late.".Length..], o.Value)).ToArray();
        var postOptions = all.Where(o => o.Key.StartsWith("post."))
            .Select(o => (o.Key["post.".Length..], o.Value)).ToArray();

        // size=WxH renders at the clip's own size, so nothing is resampled — scaling smooths
        // a gradient and would hide exactly the noise this is looking for.
        if (all.FirstOrDefault(o => o.Key == "size").Value is { } wh && wh.Contains('x'))
        {
            var parts = wh.Split('x', 2);
            if (int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
            { Width = w; Height = h; }
        }
        int depth = int.TryParse(all.FirstOrDefault(o => o.Key == "depth").Value, out int d) ? d : 0;
        options = options.Where(o => o.Key is not ("size" or "depth")).ToArray();

        Console.WriteLine("── what range does mpv write into the shared image? ──");
        Console.WriteLine();
        Console.WriteLine($"  file   : {Path.GetFileName(file)}");
        Console.WriteLine(options.Length == 0
            ? "  options: (none — this is today's behaviour)"
            : "  options: " + string.Join("  ", options.Select(o => $"{o.Key}={o.Value}")));
        if (lateOptions.Length > 0)
            Console.WriteLine("  late   : " + string.Join("  ", lateOptions.Select(o => $"{o.Item1}={o.Item2}")));
        Console.WriteLine();

        if (!SyncBench.MakeGlContext(out string renderer)) return 1;
        Console.WriteLine($"  GL     : {renderer}");

        using var vk = VulkanImage.Create(Width, Height, VkFormat.R16G16B16A16Sfloat);
        uint fbo = ImportIntoGl(vk);
        if (fbo == 0) { Console.WriteLine("  could not import the image into GL"); return 1; }

        // Cleared to something no decoder would produce, so "mpv wrote nothing" cannot be
        // mistaken for "mpv wrote black".
        Gl.BindFramebuffer(fbo);
        Gl.Viewport(Width, Height);
        Gl.ClearTo(-1f, -1f, -1f, 1f);
        Gl.Finish();

        var result = MpvIntoFbo.Render(file, fbo, Width, Height, colorspaceHint: false, options,
            lateOptions.Length == 0 ? null : lateOptions,
            postOptions.Length == 0 ? null : postOptions, depth);
        if (!result.Ok) { Console.WriteLine($"  mpv failed: {result.Message}"); return 2; }

        var pixels = vk.ReadBack();
        Report(pixels);
        return 0;
    }

    private static void Report(Half[] pixels)
    {
        float min = float.MaxValue, max = float.MinValue, total = 0;
        int count = 0, over1 = 0, negative = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            for (int c = 0; c < 3; c++)
            {
                float v = (float)pixels[i + c];
                if (float.IsNaN(v)) continue;
                if (v < -0.5f) { negative++; continue; }   // the clear colour, never written
                min = Math.Min(min, v);
                max = Math.Max(max, v);
                if (v > 1.0001f) over1++;
                total += v;
                count++;
            }
        }

        if (count == 0) { Console.WriteLine("  mpv wrote nothing at all."); return; }

        Console.WriteLine();
        Console.WriteLine($"  written : {count / 3} of {Width * Height} pixels" +
            (negative > 0 ? $"  ({negative / 3} still hold the clear colour)" : ""));
        Console.WriteLine($"  range   : min {min:0.0000}  max {max:0.0000}  mean {total / count:0.0000}");
        Console.WriteLine($"  over 1.0: {over1} channel samples");
        Console.WriteLine();

        // The clip this is meant to be run against has a bright band, a mid grey and a dark
        // band, which is what makes encoded and linear tell apart at a glance.
        Console.WriteLine("  rows (r,g,b):");
        foreach (var frac in new[] { 0.15, 0.5, 0.85 })
        {
            int y = (int)(Height * frac);
            int i = (y * Width + Width / 2) * 4;
            Console.WriteLine($"    y={y,3}  ({(float)pixels[i]:0.0000}, {(float)pixels[i + 1]:0.0000}, " +
                $"{(float)pixels[i + 2]:0.0000})");
        }
        Console.WriteLine();
        ReportSmoothness(pixels);

        Console.WriteLine(over1 > 0
            ? "  → values exceed 1.0: the target options carried range through."
            : "  → nothing exceeds 1.0: this is still an SDR-referred picture.");
    }

    /// <summary>
    /// How smooth a smooth thing came out — the dithering question.
    ///
    /// <para>mpv is told nothing about the target's depth, and the render API says an omitted
    /// <c>MPV_RENDER_PARAM_DEPTH</c> means it assumes 8 bits and dithers to them. Into a
    /// half-float target that would be noise added for no reason, and precision phase 4
    /// existed to keep thrown away. Whether <c>internal_format</c> already tells it otherwise
    /// is not documented, so it is measured.</para>
    ///
    /// <para>Run against a smooth ramp. Dither shows as neighbouring pixels that disagree in
    /// both directions — a monotonic ramp reversing thousands of times across a row — and as
    /// values sitting on a coarse grid. A clean 16-bit path gives a nearly monotonic row with
    /// far more distinct levels than 256.</para>
    /// </summary>
    private static void ReportSmoothness(Half[] pixels)
    {
        int y = Height / 2;
        var row = new float[Width];
        for (int x = 0; x < Width; x++) row[x] = (float)pixels[(y * Width + x) * 4];

        var distinct = new HashSet<float>(row);
        double totalStep = 0;
        int reversals = 0;
        int lastSign = 0;
        for (int x = 1; x < Width; x++)
        {
            float delta = row[x] - row[x - 1];
            totalStep += Math.Abs(delta);
            int sign = delta > 1e-6f ? 1 : delta < -1e-6f ? -1 : 0;
            if (sign != 0 && lastSign != 0 && sign != lastSign) reversals++;
            if (sign != 0) lastSign = sign;
        }

        Console.WriteLine($"  middle row ({Width} px): {distinct.Count} distinct levels, " +
            $"{reversals} direction reversals, mean step {totalStep / (Width - 1):0.00000}");
    }

    private static uint ImportIntoGl(VulkanImage vk)
    {
        uint memory = Gl.CreateMemoryObject();
        Gl.MarkDedicated(memory);
        if (OperatingSystem.IsWindows()) Gl.ImportWin32(memory, vk.AllocationSize, vk.ExportWin32Handle());
        else Gl.ImportFd(memory, vk.AllocationSize, vk.ExportFd());
        if (Gl.Error() != 0) return 0;

        uint texture = Gl.CreateTexture();
        Gl.SetOptimalTiling(texture);
        Gl.TextureStorageMem(texture, vk.Width, vk.Height, memory, 0);
        if (Gl.Error() != 0) return 0;

        uint fbo = Gl.CreateFramebuffer();
        Gl.AttachColour(fbo, texture);
        return Gl.FramebufferComplete(fbo) ? fbo : 0;
    }
}
