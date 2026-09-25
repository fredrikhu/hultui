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
    private const int Width = 320;
    private const int Height = 180;

    public static int Run(string[] args)
    {
        string? file = args.FirstOrDefault(a => !a.StartsWith('-') && !a.Contains('='));
        if (file is null)
        {
            Console.WriteLine("  usage: --range <file> [mpv-option=value ...]");
            return 1;
        }

        var options = args
            .Where(a => a.Contains('=') && !a.StartsWith('-'))
            .Select(a => a.Split('=', 2))
            .Select(parts => (parts[0], parts[1]))
            .ToArray();

        Console.WriteLine("── what range does mpv write into the shared image? ──");
        Console.WriteLine();
        Console.WriteLine($"  file   : {Path.GetFileName(file)}");
        Console.WriteLine(options.Length == 0
            ? "  options: (none — this is today's behaviour)"
            : "  options: " + string.Join("  ", options.Select(o => $"{o.Item1}={o.Item2}")));
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

        var result = MpvIntoFbo.Render(file, fbo, Width, Height, colorspaceHint: false, options);
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
        Console.WriteLine(over1 > 0
            ? "  → values exceed 1.0: the target options carried range through."
            : "  → nothing exceeds 1.0: this is still an SDR-referred picture.");
    }

    private static uint ImportIntoGl(VulkanImage vk)
    {
        uint memory = Gl.CreateMemoryObject();
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
