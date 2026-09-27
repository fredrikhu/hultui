using System.Runtime.InteropServices;

namespace HdrInterop;

/// <summary>
/// Will CUDA register a texture from <i>our</i> GL context? — the one question that decides
/// whether zero-copy decode is possible on Windows.
///
/// <para><c>docs/design/HDR.md</c> ruled Windows out because the only zero-copy bridge that
/// works with a desktop GL context is 8-bit and the ten-bit one needs ANGLE, which cannot
/// import Vulkan memory. Both are still true. Both missed CUDA, which is neither: mpv's
/// <c>hwdec_cuda_gl.c</c> includes no EGL, no GLX and no WGL, and its meson gate is
/// <c>cuda-hwaccel and (gl or vulkan)</c> with no platform condition at all.</para>
///
/// <para><b>And the question is smaller than it first looked.</b> mpv does not register the
/// image we lend it. It creates its <i>own</i> plane textures, registers those with
/// <c>cuGraphicsGLRegisterImage</c>, copies the decoded surface into them on the GPU, and
/// renders from them into our framebuffer. So nothing here has to survive being Vulkan-owned
/// memory imported into GL — which is what the Linux brief wrongly assumed was the crux. What
/// has to work is plain CUDA-GL interop in a plain WGL context.</para>
///
/// <para>This mirrors mpv's sequence exactly (register, map, get the mapped array, unmap) on
/// the plane formats a P010 frame actually uses, so a pass here means mpv's path has nothing
/// left to fail on except mpv.</para>
/// </summary>
internal static unsafe class CudaGlBench
{
    // CUDA driver API. Loaded by name, never linked — the same reason mpv loads it through
    // ffnvcodec's dynamic loader: a binary that links nvcuda.dll will not start on a machine
    // without an NVIDIA driver, which is worse than having no zero-copy.
    private const string Library = "nvcuda";

    private const uint CU_GL_DEVICE_LIST_ALL = 0x01;
    private const uint CU_GRAPHICS_REGISTER_FLAGS_WRITE_DISCARD = 0x02;
    private const uint GL_TEXTURE_2D = 0x0DE1;

    // The planes a P010 frame is made of, which is what a 10-bit HDR film decodes to: one
    // 16-bit luma plane and one two-channel 16-bit chroma plane. NV12's 8-bit pair is here
    // as the control — if only the 8-bit ones register, this is the dxva2 story again.
    private static readonly (uint Format, string Name)[] Planes =
    [
        (0x8229, "GL_R8    (NV12 luma)"),
        (0x822B, "GL_RG8   (NV12 chroma)"),
        (0x822A, "GL_R16   (P010 luma)"),
        (0x822C, "GL_RG16  (P010 chroma)"),
    ];

    public static int Run()
    {
        Console.WriteLine("── will CUDA register a texture from our GL context? ──");
        Console.WriteLine();

        if (!SyncBench.MakeGlContext(out string renderer)) return 1;
        Console.WriteLine($"  GL renderer : {renderer}");

        if (!NativeLibrary.TryLoad(Library, out nint cuda))
        {
            Console.WriteLine("  nvcuda not present — no NVIDIA driver here, so no CUDA route.");
            return 2;
        }

        var cu = new Cu(cuda);
        if (!cu.Complete)
        {
            Console.WriteLine("  nvcuda loaded but is missing entry points this needs.");
            return 2;
        }

        Check(cu.Init(0), "cuInit", cu);

        // Which CUDA device drives the GL context that is current. mpv asks exactly this, and
        // it is the check that fails on a laptop whose GL landed on the integrated GPU.
        uint count = 0;
        int device = 0;
        int rc = cu.GLGetDevices(&count, &device, 1, CU_GL_DEVICE_LIST_ALL);
        if (rc != 0 || count == 0)
        {
            Console.WriteLine($"  cuGLGetDevices found no CUDA device for this GL context " +
                $"(rc {rc}, count {count}).");
            Console.WriteLine("  → zero-copy through CUDA cannot work here. This is the answer.");
            return 3;
        }
        Console.WriteLine($"  CUDA device : {device} (for this GL context)");
        Console.WriteLine();

        nint context;
        Check(cu.CtxCreate(&context, 0, device), "cuCtxCreate", cu);

        bool allOk = true;
        try
        {
            foreach (var (format, name) in Planes)
            {
                uint texture = Gl.CreateTexture();
                Gl.TextureStorage(texture, format, 1920, 1080);
                if (Gl.Error() is var glError and not 0)
                {
                    Console.WriteLine($"  {name}  GL refused the storage (0x{glError:X})");
                    allOk = false;
                    continue;
                }

                // mpv's sequence, in mpv's order.
                nint resource;
                int r = cu.GraphicsGLRegisterImage(&resource, texture, GL_TEXTURE_2D,
                    CU_GRAPHICS_REGISTER_FLAGS_WRITE_DISCARD);
                if (r != 0)
                {
                    Console.WriteLine($"  {name}  cuGraphicsGLRegisterImage failed: {cu.Describe(r)}");
                    allOk = false;
                    continue;
                }

                int mapped = cu.GraphicsMapResources(1, &resource, 0);
                nint array = 0;
                int got = mapped == 0 ? cu.GraphicsSubResourceGetMappedArray(&array, resource, 0, 0) : -1;
                if (mapped == 0) cu.GraphicsUnmapResources(1, &resource, 0);
                cu.GraphicsUnregisterResource(resource);

                if (mapped != 0 || got != 0 || array == 0)
                {
                    Console.WriteLine($"  {name}  registered but would not map " +
                        $"(map {cu.Describe(mapped)}, array {cu.Describe(got)})");
                    allOk = false;
                    continue;
                }
                Console.WriteLine($"  {name}  registered and mapped");
            }
        }
        finally
        {
            cu.CtxDestroy(context);
        }

        Console.WriteLine();
        Console.WriteLine(allOk
            ? "  → CUDA-GL interop works in this context, on the planes a P010 frame uses.\n" +
              "    Nothing in mpv's path is left to fail on except mpv."
            : "  → Some plane formats did not register. Read which: an 8-bit-only result is the\n" +
              "    dxva2 story again and would not carry HDR.");
        return allOk ? 0 : 4;
    }

    private static void Check(int result, string what, Cu cu)
    {
        if (result != 0) throw new InvalidOperationException($"{what}: {cu.Describe(result)}");
    }

    /// <summary>The handful of CUDA driver entry points this needs, resolved by name.</summary>
    private sealed class Cu
    {
        public readonly delegate* unmanaged[Cdecl]<uint, int> Init;
        public readonly delegate* unmanaged[Cdecl]<uint*, int*, uint, uint, int> GLGetDevices;
        public readonly delegate* unmanaged[Cdecl]<nint*, uint, int, int> CtxCreate;
        public readonly delegate* unmanaged[Cdecl]<nint, int> CtxDestroy;
        public readonly delegate* unmanaged[Cdecl]<nint*, uint, uint, uint, int> GraphicsGLRegisterImage;
        public readonly delegate* unmanaged[Cdecl]<uint, nint*, nint, int> GraphicsMapResources;
        public readonly delegate* unmanaged[Cdecl]<uint, nint*, nint, int> GraphicsUnmapResources;
        public readonly delegate* unmanaged[Cdecl]<nint*, nint, uint, uint, int> GraphicsSubResourceGetMappedArray;
        public readonly delegate* unmanaged[Cdecl]<nint, int> GraphicsUnregisterResource;
        private readonly delegate* unmanaged[Cdecl]<int, byte**, int> _getErrorString;

        public Cu(nint library)
        {
            nint P(string name) =>
                NativeLibrary.TryGetExport(library, name, out nint address) ? address : 0;

            // The _v2 names are the current ABI for these; the unsuffixed ones are the 3.x
            // originals and are not what a modern driver exports for all of them.
            Init = (delegate* unmanaged[Cdecl]<uint, int>)P("cuInit");
            GLGetDevices = (delegate* unmanaged[Cdecl]<uint*, int*, uint, uint, int>)P("cuGLGetDevices_v2");
            CtxCreate = (delegate* unmanaged[Cdecl]<nint*, uint, int, int>)P("cuCtxCreate_v2");
            CtxDestroy = (delegate* unmanaged[Cdecl]<nint, int>)P("cuCtxDestroy_v2");
            GraphicsGLRegisterImage = (delegate* unmanaged[Cdecl]<nint*, uint, uint, uint, int>)P("cuGraphicsGLRegisterImage");
            GraphicsMapResources = (delegate* unmanaged[Cdecl]<uint, nint*, nint, int>)P("cuGraphicsMapResources");
            GraphicsUnmapResources = (delegate* unmanaged[Cdecl]<uint, nint*, nint, int>)P("cuGraphicsUnmapResources");
            GraphicsSubResourceGetMappedArray = (delegate* unmanaged[Cdecl]<nint*, nint, uint, uint, int>)P("cuGraphicsSubResourceGetMappedArray");
            GraphicsUnregisterResource = (delegate* unmanaged[Cdecl]<nint, int>)P("cuGraphicsUnregisterResource");
            _getErrorString = (delegate* unmanaged[Cdecl]<int, byte**, int>)P("cuGetErrorString");
        }

        public bool Complete =>
            Init is not null && GLGetDevices is not null && CtxCreate is not null
            && GraphicsGLRegisterImage is not null && GraphicsMapResources is not null
            && GraphicsSubResourceGetMappedArray is not null;

        public string Describe(int code)
        {
            if (code == 0) return "ok";
            if (_getErrorString is null) return $"CUDA error {code}";
            byte* text;
            return _getErrorString(code, &text) == 0 && text is not null
                ? $"{Marshal.PtrToStringUTF8((nint)text)} ({code})"
                : $"CUDA error {code}";
        }
    }
}
