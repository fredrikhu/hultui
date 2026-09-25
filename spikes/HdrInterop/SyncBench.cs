using System.Diagnostics;
using SDL;
using Vortice.Vulkan;
using static SDL.SDL3;

namespace HdrInterop;

/// <summary>
/// What the interop costs per frame, before anything is built on it.
///
/// <para>Phase 4 would have mpv render into a Vulkan image through GL and Skia read it back
/// out, every frame. The saving is the software path's copy through main memory — measured
/// at 1.75 CPU-seconds per ten wall seconds for 1080p. The cost is that GL and Vulkan have
/// to be ordered against each other, and the only ordering this spike has is the blunt one:
/// <c>glFinish</c> on one side and a queue wait on the other. Both stall.</para>
///
/// <para>If the stall is larger than the copy it replaces, the whole arrangement is worse
/// than what already works, and that is worth knowing before writing a hidden GL context and
/// a new lifecycle. Semaphores would fix it — <c>VK_KHR_external_semaphore</c> with
/// <c>GL_EXT_semaphore</c> — and are a further piece of interop; this says whether they are
/// required or merely nice.</para>
/// </summary>
internal static unsafe class SyncBench
{
    private const int Warmup = 20;
    private const int Iterations = 200;

    public static int Run()
    {
        Console.WriteLine("── what one interop frame costs ──");
        Console.WriteLine();

        // This mode is dispatched before Program.Run makes a window, so it makes its own.
        // GL entry points cannot be resolved without a current context, and calling one
        // that was never resolved is an access violation rather than a null check.
        if (!MakeGlContext(out string renderer)) return 1;
        Console.WriteLine($"  GL renderer: {renderer}");
        Console.WriteLine();

        foreach (var (w, h, label) in new[] { (1920, 1080, "1080p"), (3840, 2160, "4K   ") })
        {
            using var vk = VulkanImage.Create(w, h, VkFormat.R16G16B16A16Sfloat);
            var fbo = ImportIntoGl(vk);
            if (fbo == 0) { Console.WriteLine($"  {label}: could not import"); continue; }

            // Each stage on its own, then the whole thing, so a bad number says which part.
            double draw = Time(() => { GlDraw(fbo, w, h); });
            double drawFinish = Time(() => { GlDraw(fbo, w, h); Gl.Finish(); });
            double whole = Time(() => { GlDraw(fbo, w, h); Gl.Finish(); vk.TouchFromVulkan(); });

            double sync = whole - draw;
            Console.WriteLine($"  {label}  gl draw {draw:0.00} ms   + glFinish {drawFinish - draw:0.00} ms" +
                $"   + vulkan wait {whole - drawFinish:0.00} ms   = {whole:0.00} ms/frame");
            Console.WriteLine($"         synchronisation alone: {sync:0.00} ms/frame" +
                $"   ({sync * 24:0} ms per second at 24 fps, {sync * 60:0} at 60)");
            Console.WriteLine();
        }

        Console.WriteLine("  The software path it would replace costs about 175 ms of CPU per second");
        Console.WriteLine("  at 1080p (1.75 CPU-seconds per ten, measured on the video sample).");
        return 0;
    }

    /// <summary>A hidden GL window, purely so Gl.Load has something to resolve against.</summary>
    private static bool MakeGlContext(out string renderer)
    {
        renderer = "";
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
        {
            Console.WriteLine($"  SDL_Init: {SDL_GetError()}");
            return false;
        }

        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MAJOR_VERSION, 4);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MINOR_VERSION, 5);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_PROFILE_MASK,
            (int)SDL_GLProfile.SDL_GL_CONTEXT_PROFILE_CORE);

        var window = SDL_CreateWindow("syncbench", 64, 64,
            SDL_WindowFlags.SDL_WINDOW_OPENGL | SDL_WindowFlags.SDL_WINDOW_HIDDEN);
        if (window is null) { Console.WriteLine($"  SDL_CreateWindow: {SDL_GetError()}"); return false; }

        var context = SDL_GL_CreateContext(window);
        if (context is null) { Console.WriteLine($"  SDL_GL_CreateContext: {SDL_GetError()}"); return false; }
        SDL_GL_MakeCurrent(window, context);

        Gl.Load();
        if (!Gl.HasMemoryObject)
        {
            Console.WriteLine("  this driver cannot import external memory into GL — nothing to measure");
            return false;
        }
        renderer = Gl.GetString(Gl.RENDERER) ?? "unknown";
        return true;
    }

    private static double Time(Action frame)
    {
        for (int i = 0; i < Warmup; i++) frame();
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < Iterations; i++) frame();
        clock.Stop();
        return clock.Elapsed.TotalMilliseconds / Iterations;
    }

    /// <summary>A frame's worth of GL work: what mpv would do, standing in as a clear.</summary>
    private static void GlDraw(uint fbo, int width, int height)
    {
        Gl.BindFramebuffer(fbo);
        Gl.Viewport(width, height);
        Gl.ClearTo(0.2f, 0.4f, 0.6f, 1f);
    }

    /// <summary>Imports the Vulkan image into the GL context and returns an FBO over it.</summary>
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
