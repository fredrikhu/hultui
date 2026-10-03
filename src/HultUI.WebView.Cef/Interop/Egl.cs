using System.Runtime.InteropServices;

namespace HultUI.WebView.Cef.Interop;

/// <summary>
/// The EGL calls zero-copy needs, on both sides: the engine's own context on CEF's thread
/// (surfaceless, GL ES 3), and HultUI's, whose display is whichever EGL display is current on
/// the render thread. libEGL is libglvnd's, the one SDL loaded, so both resolve to one driver.
/// </summary>
internal static unsafe partial class Egl
{
    private const string Lib = "libEGL.so.1";

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] internal static partial nint eglGetProcAddress(string name);
    [LibraryImport(Lib)] internal static partial nint eglGetPlatformDisplay(uint platform, nint nativeDisplay, nint* attribs);
    [LibraryImport(Lib)] internal static partial uint eglInitialize(nint display, int* major, int* minor);
    [LibraryImport(Lib)] internal static partial uint eglBindAPI(uint api);
    [LibraryImport(Lib)] internal static partial nint eglCreateContext(nint display, nint config, nint share, int* attribs);
    [LibraryImport(Lib)] internal static partial uint eglDestroyContext(nint display, nint context);
    [LibraryImport(Lib)] internal static partial uint eglMakeCurrent(nint display, nint draw, nint read, nint context);
    [LibraryImport(Lib)] internal static partial nint eglQueryString(nint display, int name);
    [LibraryImport(Lib)] internal static partial int eglGetError();
    [LibraryImport(Lib)] internal static partial nint eglGetCurrentDisplay();
    [LibraryImport(Lib)] internal static partial nint eglGetCurrentContext();

    internal const uint EGL_PLATFORM_SURFACELESS_MESA = 0x31DD;
    internal const uint EGL_OPENGL_ES_API = 0x30A0;
    internal const int EGL_EXTENSIONS = 0x3055, EGL_NONE = 0x3038, EGL_CONTEXT_MAJOR_VERSION = 0x3098;
    internal const uint EGL_LINUX_DMA_BUF_EXT = 0x3270, EGL_GL_TEXTURE_2D_KHR = 0x30B1;
    internal const int EGL_WIDTH = 0x3057, EGL_HEIGHT = 0x3056, EGL_LINUX_DRM_FOURCC_EXT = 0x3271;

    // The four planes' attributes: fd, offset, pitch, modifier low and high.
    private static readonly int[] PlaneFd = [0x3272, 0x3275, 0x3278, 0x3440];
    private static readonly int[] PlaneOffset = [0x3273, 0x3276, 0x3279, 0x3441];
    private static readonly int[] PlanePitch = [0x3274, 0x3277, 0x327A, 0x3442];
    private static readonly int[] PlaneModifierLo = [0x3443, 0x3445, 0x3447, 0x3449];
    private static readonly int[] PlaneModifierHi = [0x3444, 0x3446, 0x3448, 0x344A];

    /// <summary>B, G, R, A in memory: CEF's BGRA frames.</summary>
    internal const int DRM_FORMAT_ARGB8888 = 0x34325241;
    /// <summary>R, G, B, A in memory.</summary>
    internal const int DRM_FORMAT_ABGR8888 = 0x34324241;

    /// <summary>One plane of a dmabuf.</summary>
    internal readonly record struct Plane(int Fd, int Offset, int Pitch);

    /// <summary>A dmabuf as EGL describes it: format, modifier, planes.</summary>
    internal sealed record Dmabuf(int Width, int Height, int Fourcc, ulong Modifier, Plane[] Planes);

    /// <summary>The EGL extension functions; resolved once, the same for every display.</summary>
    internal static class Ext
    {
        public static readonly delegate* unmanaged<nint, nint, uint, nint, int*, nint> CreateImage =
            (delegate* unmanaged<nint, nint, uint, nint, int*, nint>)eglGetProcAddress("eglCreateImageKHR");
        public static readonly delegate* unmanaged<nint, nint, uint> DestroyImage =
            (delegate* unmanaged<nint, nint, uint>)eglGetProcAddress("eglDestroyImageKHR");
        public static readonly delegate* unmanaged<nint, nint, int*, int*, ulong*, uint> ExportQuery =
            (delegate* unmanaged<nint, nint, int*, int*, ulong*, uint>)eglGetProcAddress("eglExportDMABUFImageQueryMESA");
        public static readonly delegate* unmanaged<nint, nint, int*, int*, int*, uint> Export =
            (delegate* unmanaged<nint, nint, int*, int*, int*, uint>)eglGetProcAddress("eglExportDMABUFImageMESA");
    }

    /// <summary>An EGLImage over a dmabuf on <paramref name="display"/>; 0 on failure.</summary>
    internal static nint Import(nint display, Dmabuf buffer)
    {
        if (Ext.CreateImage == null) return 0;
        var a = stackalloc int[7 + buffer.Planes.Length * 10];
        int n = 0;
        a[n++] = EGL_WIDTH; a[n++] = buffer.Width;
        a[n++] = EGL_HEIGHT; a[n++] = buffer.Height;
        a[n++] = EGL_LINUX_DRM_FOURCC_EXT; a[n++] = buffer.Fourcc;
        for (int i = 0; i < buffer.Planes.Length; i++)
        {
            var p = buffer.Planes[i];
            a[n++] = PlaneFd[i]; a[n++] = p.Fd;
            a[n++] = PlaneOffset[i]; a[n++] = p.Offset;
            a[n++] = PlanePitch[i]; a[n++] = p.Pitch;
            a[n++] = PlaneModifierLo[i]; a[n++] = (int)(buffer.Modifier & 0xFFFFFFFF);
            a[n++] = PlaneModifierHi[i]; a[n++] = (int)(buffer.Modifier >> 32);
        }
        a[n] = EGL_NONE;
        return Ext.CreateImage(display, 0, EGL_LINUX_DMA_BUF_EXT, 0, a);
    }

    internal static void DestroyImage(nint display, nint image)
    {
        if (image != 0 && Ext.DestroyImage != null) Ext.DestroyImage(display, image);
    }

    internal static string Extensions(nint display) => Marshal.PtrToStringAnsi(eglQueryString(display, EGL_EXTENSIONS)) ?? "";

    internal static bool Has(string extensions, string name) => (" " + extensions + " ").Contains(" " + name + " ");
}

/// <summary>
/// The GL calls zero-copy makes, resolved against one context: the engine's GL ES, or HultUI's
/// desktop GL (or GL ES) through the surface's resolver. Every one exists in both.
/// </summary>
internal sealed unsafe class GlCalls
{
    public const uint GL_TEXTURE_2D = 0x0DE1, GL_RGBA8 = 0x8058, GL_RGBA = 0x1908, GL_BGRA = 0x80E1,
        GL_UNSIGNED_BYTE = 0x1401, GL_READ_FRAMEBUFFER = 0x8CA8, GL_DRAW_FRAMEBUFFER = 0x8CA9,
        GL_FRAMEBUFFER = 0x8D40, GL_COLOR_ATTACHMENT0 = 0x8CE0, GL_COLOR_BUFFER_BIT = 0x4000,
        GL_NEAREST = 0x2600, GL_LINEAR = 0x2601, GL_TEXTURE_MIN_FILTER = 0x2801, GL_TEXTURE_MAG_FILTER = 0x2800,
        GL_SYNC_GPU_COMMANDS_COMPLETE = 0x9117, GL_SYNC_FLUSH_COMMANDS_BIT = 1, GL_TIMEOUT_EXPIRED = 0x911B,
        GL_FRAMEBUFFER_COMPLETE = 0x8CD5, GL_UNPACK_ROW_LENGTH = 0x0CF2;

    public readonly delegate* unmanaged<int, uint*, void> GenTextures, DeleteTextures, GenFramebuffers, DeleteFramebuffers;
    public readonly delegate* unmanaged<uint, uint, void> BindTexture, BindFramebuffer;
    public readonly delegate* unmanaged<uint, uint, int, void> TexParameteri;
    public readonly delegate* unmanaged<uint, int, uint, int, int, void> TexStorage2D;
    public readonly delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> TexImage2D;
    public readonly delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> TexSubImage2D;
    public readonly delegate* unmanaged<uint, uint, uint, uint, int, void> FramebufferTexture2D;
    public readonly delegate* unmanaged<uint, uint> CheckFramebufferStatus;
    public readonly delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void> BlitFramebuffer;
    public readonly delegate* unmanaged<int, int, int, int, uint, uint, void*, void> ReadPixels;
    public readonly delegate* unmanaged<uint, nint, void> EGLImageTargetTexture2DOES;
    public readonly delegate* unmanaged<uint, uint, nint> FenceSync;
    public readonly delegate* unmanaged<nint, uint, ulong, uint> ClientWaitSync;
    public readonly delegate* unmanaged<nint, void> DeleteSync;
    public readonly delegate* unmanaged<uint, int, void> PixelStorei;
    public readonly delegate* unmanaged<void> Flush;
    public readonly delegate* unmanaged<uint> GetError;

    public GlCalls(Func<string, nint> resolve)
    {
        GenTextures = (delegate* unmanaged<int, uint*, void>)resolve("glGenTextures");
        DeleteTextures = (delegate* unmanaged<int, uint*, void>)resolve("glDeleteTextures");
        GenFramebuffers = (delegate* unmanaged<int, uint*, void>)resolve("glGenFramebuffers");
        DeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)resolve("glDeleteFramebuffers");
        BindTexture = (delegate* unmanaged<uint, uint, void>)resolve("glBindTexture");
        BindFramebuffer = (delegate* unmanaged<uint, uint, void>)resolve("glBindFramebuffer");
        TexParameteri = (delegate* unmanaged<uint, uint, int, void>)resolve("glTexParameteri");
        TexStorage2D = (delegate* unmanaged<uint, int, uint, int, int, void>)resolve("glTexStorage2D");
        TexImage2D = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void>)resolve("glTexImage2D");
        TexSubImage2D = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void>)resolve("glTexSubImage2D");
        FramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)resolve("glFramebufferTexture2D");
        CheckFramebufferStatus = (delegate* unmanaged<uint, uint>)resolve("glCheckFramebufferStatus");
        BlitFramebuffer = (delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void>)resolve("glBlitFramebuffer");
        ReadPixels = (delegate* unmanaged<int, int, int, int, uint, uint, void*, void>)resolve("glReadPixels");
        EGLImageTargetTexture2DOES = (delegate* unmanaged<uint, nint, void>)resolve("glEGLImageTargetTexture2DOES");
        FenceSync = (delegate* unmanaged<uint, uint, nint>)resolve("glFenceSync");
        ClientWaitSync = (delegate* unmanaged<nint, uint, ulong, uint>)resolve("glClientWaitSync");
        DeleteSync = (delegate* unmanaged<nint, void>)resolve("glDeleteSync");
        PixelStorei = (delegate* unmanaged<uint, int, void>)resolve("glPixelStorei");
        Flush = (delegate* unmanaged<void>)resolve("glFlush");
        GetError = (delegate* unmanaged<uint>)resolve("glGetError");
    }

    /// <summary>Whether every call resolved.</summary>
    public bool Complete =>
        GenTextures != null && DeleteTextures != null && GenFramebuffers != null && DeleteFramebuffers != null
        && BindTexture != null && BindFramebuffer != null && TexParameteri != null && TexImage2D != null
        && TexSubImage2D != null && FramebufferTexture2D != null && BlitFramebuffer != null && ReadPixels != null
        && EGLImageTargetTexture2DOES != null && FenceSync != null && ClientWaitSync != null && DeleteSync != null
        && PixelStorei != null && Flush != null && GetError != null;

    /// <summary>A texture with a framebuffer on it.</summary>
    public (uint Texture, uint Framebuffer) Target(int width, int height)
    {
        uint t, f;
        GenTextures(1, &t);
        BindTexture(GL_TEXTURE_2D, t);
        TexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA8, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);
        TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
        TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        GenFramebuffers(1, &f);
        BindFramebuffer(GL_FRAMEBUFFER, f);
        FramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, t, 0);
        return (t, f);
    }

    /// <summary>A texture over an EGLImage, with a framebuffer to read it through.</summary>
    public (uint Texture, uint Framebuffer) Over(nint image)
    {
        uint t, f;
        GenTextures(1, &t);
        BindTexture(GL_TEXTURE_2D, t);
        EGLImageTargetTexture2DOES(GL_TEXTURE_2D, image);
        GenFramebuffers(1, &f);
        BindFramebuffer(GL_FRAMEBUFFER, f);
        FramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, t, 0);
        return (t, f);
    }

    public void Delete(ref uint texture, ref uint framebuffer)
    {
        if (framebuffer != 0) { uint f = framebuffer; DeleteFramebuffers(1, &f); framebuffer = 0; }
        if (texture != 0) { uint t = texture; DeleteTextures(1, &t); texture = 0; }
    }

    public void Blit(uint from, uint to, int sx, int sy, int width, int height, int dx, int dy)
    {
        BindFramebuffer(GL_READ_FRAMEBUFFER, from);
        BindFramebuffer(GL_DRAW_FRAMEBUFFER, to);
        BlitFramebuffer(sx, sy, sx + width, sy + height, dx, dy, dx + width, dy + height, GL_COLOR_BUFFER_BIT, GL_NEAREST);
    }

    /// <summary>Waits until the GPU has done everything asked so far.</summary>
    public void Fence()
    {
        var sync = FenceSync(GL_SYNC_GPU_COMMANDS_COMPLETE, 0);
        ClientWaitSync(sync, GL_SYNC_FLUSH_COMMANDS_BIT, 1_000_000_000);
        DeleteSync(sync);
    }
}
