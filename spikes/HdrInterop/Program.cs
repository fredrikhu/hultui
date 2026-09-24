using SDL;
using static SDL.SDL3;

namespace HdrInterop;

/// <summary>
/// Phase 0 of docs/design/HDR.md: can OpenGL render into a Vulkan image?
///
/// <para>Everything in that design hangs on it. Skia would draw with Vulkan, because that
/// is the only API that can present HDR on both Windows and Linux; mpv can only render
/// with OpenGL, because its render API has no other type. They meet by exporting the
/// Vulkan image's memory and importing it into GL — and if that does not work on real
/// drivers there is no route, so it is worth finding out before anything is built on it.</para>
///
/// <para>Run it with no arguments for the interop test alone, or with a path to a video
/// file to also have mpv decode a frame into the shared image:</para>
/// <code>
/// dotnet run --project spikes/HdrInterop -- "D:\films\Stalker.mkv"
/// </code>
/// </summary>
internal static class Program
{
    private const int Width = 256;
    private const int Height = 144;

    private static int Main(string[] args)
    {
        Console.WriteLine("── HDR interop spike ─────────────────────────────────");
        Console.WriteLine($"platform: {(OperatingSystem.IsWindows() ? "Windows" : "Linux/other")}\n");

        try
        {
            return Run(args.FirstOrDefault(a => !a.StartsWith('-')));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nFAILED: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static unsafe int Run(string? mediaFile)
    {
        // ── 1. Vulkan: the device, and an image whose memory can leave it ────
        using var vk = VulkanImage.Create(Width, Height);
        Console.WriteLine($"[1] Vulkan device      : {vk.DeviceName}");
        Console.WriteLine($"    device UUID        : {Convert.ToHexString(vk.DeviceUuid)}");
        Console.WriteLine($"    image              : {Width}x{Height} RGBA16F, {vk.AllocationSize} bytes");

        // ── 2. GL: a context on (hopefully) the same GPU ─────────────────────
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
            throw new InvalidOperationException($"SDL_Init: {SDL_GetError()}");

        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MAJOR_VERSION, 4);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MINOR_VERSION, 5);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_PROFILE_MASK,
            (int)SDL_GLProfile.SDL_GL_CONTEXT_PROFILE_CORE);

        var window = SDL_CreateWindow("hdr-interop", 64, 64,
            SDL_WindowFlags.SDL_WINDOW_OPENGL | SDL_WindowFlags.SDL_WINDOW_HIDDEN);
        if (window is null) throw new InvalidOperationException($"SDL_CreateWindow: {SDL_GetError()}");

        var context = SDL_GL_CreateContext(window);
        if (context is null) throw new InvalidOperationException($"SDL_GL_CreateContext: {SDL_GetError()}");
        SDL_GL_MakeCurrent(window, context);

        Gl.Load();
        Console.WriteLine($"\n[2] GL renderer        : {Gl.GetString(Gl.RENDERER)}");

        var extensions = Gl.Extensions();
        bool memoryObject = extensions.Contains("GL_EXT_memory_object");
        bool platformImport = OperatingSystem.IsWindows()
            ? extensions.Contains("GL_EXT_memory_object_win32")
            : extensions.Contains("GL_EXT_memory_object_fd");

        Console.WriteLine($"    GL_EXT_memory_object      : {(memoryObject ? "yes" : "NO")}");
        Console.WriteLine($"    …_win32 / …_fd            : {(platformImport ? "yes" : "NO")}");

        if (!memoryObject || !platformImport || !Gl.HasMemoryObject)
        {
            Console.WriteLine("\nVERDICT: this driver cannot import external memory into GL.");
            Console.WriteLine("The route in docs/design/HDR.md does not work here.");
            return 2;
        }

        var glUuid = Gl.DeviceUuid();
        Console.WriteLine($"    GL device UUID            : {Gl.Describe(glUuid)}");
        bool sameGpu = glUuid is not null && glUuid.AsSpan().SequenceEqual(vk.DeviceUuid);
        Console.WriteLine($"    same GPU as Vulkan        : {(sameGpu ? "yes" : "NO — import will fail")}");

        // ── 3. Export from Vulkan, import into GL ────────────────────────────
        uint memory = Gl.CreateMemoryObject();
        if (OperatingSystem.IsWindows())
        {
            var handle = vk.ExportWin32Handle();
            Console.WriteLine($"\n[3] exported Win32 handle : 0x{handle:X}");
            Gl.ImportWin32(memory, vk.AllocationSize, handle);
        }
        else
        {
            int fd = vk.ExportFd();
            Console.WriteLine($"\n[3] exported fd           : {fd}");
            Gl.ImportFd(memory, vk.AllocationSize, fd);
        }
        uint error = Gl.Error();
        if (error != 0)
        {
            Console.WriteLine($"    import failed, GL error 0x{error:X}");
            return 3;
        }
        Console.WriteLine("    imported into GL          : ok");

        uint texture = Gl.CreateTexture();
        Gl.SetOptimalTiling(texture);
        Gl.TextureStorageMem(texture, Width, Height, memory, 0);
        error = Gl.Error();
        if (error != 0)
        {
            Console.WriteLine($"    glTextureStorageMem2DEXT failed, GL error 0x{error:X}");
            return 3;
        }

        uint fbo = Gl.CreateFramebuffer();
        Gl.AttachColour(fbo, texture);
        if (!Gl.FramebufferComplete(fbo))
        {
            Console.WriteLine("    the framebuffer over the imported texture is incomplete");
            return 3;
        }
        Console.WriteLine("    texture + FBO over it     : ok");

        // ── 4. GL writes, Vulkan reads. This is the whole experiment. ────────
        const float R = 0.25f, G = 0.5f, B = 0.75f;
        Gl.BindFramebuffer(fbo);
        Gl.Viewport(Width, Height);
        Gl.ClearTo(R, G, B, 1f);
        Gl.Finish();

        var pixels = vk.ReadBack();
        float gotR = (float)pixels[0], gotG = (float)pixels[1], gotB = (float)pixels[2];
        Console.WriteLine($"\n[4] GL cleared to          : {R}, {G}, {B}");
        Console.WriteLine($"    Vulkan read back         : {gotR:0.###}, {gotG:0.###}, {gotB:0.###}");

        bool matches = Near(gotR, R) && Near(gotG, G) && Near(gotB, B);
        if (!matches)
        {
            Console.WriteLine("\nVERDICT: the image was shared but the pixels did not survive.");
            return 4;
        }
        Console.WriteLine("    → the two APIs share one image.");

        // ── 5. And now the one that matters: mpv decoding into it ────────────
        if (mediaFile is null)
        {
            Console.WriteLine("\nVERDICT: interop works. Pass a video file to test mpv into the same image.");
            return 0;
        }

        Console.WriteLine($"\n[5] mpv → the shared image : {Path.GetFileName(mediaFile)}");
        var result = MpvIntoFbo.Render(mediaFile, fbo, Width, Height);
        if (!result.Ok)
        {
            Console.WriteLine($"    mpv failed: {result.Message}");
            return 5;
        }

        pixels = vk.ReadBack();
        var (min, max, mean) = Stats(pixels);
        Console.WriteLine($"    read back                : min {min:0.###}, max {max:0.###}, mean {mean:0.###}");

        // The clear colour is gone and something with range took its place.
        bool looksLikeVideo = max > min + 0.02f && !(Near(mean, (R + G + B) / 3f) && max - min < 0.6f);
        Console.WriteLine(looksLikeVideo
            ? "\nVERDICT: mpv decoded into a Vulkan image through GL. The route works."
            : "\nVERDICT: interop works, but mpv did not visibly write to the shared image.");
        return looksLikeVideo ? 0 : 6;
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.02f;

    private static (float Min, float Max, float Mean) Stats(Half[] pixels)
    {
        float min = float.MaxValue, max = float.MinValue, total = 0;
        int count = 0;
        // Colour channels only; alpha is 1 everywhere and would flatten the range.
        for (int i = 0; i < pixels.Length; i += 4)
        {
            for (int c = 0; c < 3; c++)
            {
                float v = (float)pixels[i + c];
                if (float.IsNaN(v)) continue;
                min = Math.Min(min, v);
                max = Math.Max(max, v);
                total += v;
                count++;
            }
        }
        return count == 0 ? (0, 0, 0) : (min, max, total / count);
    }
}
