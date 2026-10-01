using System.Runtime.InteropServices;

namespace CefOsr;

/// <summary>
/// An EGL context on the surfaceless platform (no window, the render node's GPU) and the few GL
/// ES 3 calls question 8 needs: import a dmabuf as an EGLImage, blit it into a texture of our
/// own, read pixels back, upload pixels for the CPU path to compare with.
/// </summary>
internal static unsafe class Gl
{
    const string EglLib = "libEGL.so.1";
    [DllImport(EglLib)] static extern nint eglGetProcAddress(string name);
    [DllImport(EglLib)] static extern nint eglGetPlatformDisplay(uint platform, nint nativeDisplay, nint* attribs);
    [DllImport(EglLib)] static extern uint eglInitialize(nint dpy, int* major, int* minor);
    [DllImport(EglLib)] static extern uint eglBindAPI(uint api);
    [DllImport(EglLib)] static extern nint eglCreateContext(nint dpy, nint config, nint share, int* attribs);
    [DllImport(EglLib)] static extern uint eglMakeCurrent(nint dpy, nint draw, nint read, nint ctx);
    [DllImport(EglLib)] static extern nint eglQueryString(nint dpy, int name);
    [DllImport(EglLib)] static extern int eglGetError();

    const uint EGL_PLATFORM_SURFACELESS_MESA = 0x31DD;
    const uint EGL_OPENGL_ES_API = 0x30A0;
    const int EGL_EXTENSIONS = 0x3055;
    const int EGL_CONTEXT_MAJOR_VERSION = 0x3098, EGL_NONE = 0x3038;
    public const int EGL_LINUX_DMA_BUF_EXT = 0x3270, EGL_WIDTH = 0x3057, EGL_HEIGHT = 0x3056,
        EGL_LINUX_DRM_FOURCC_EXT = 0x3271, EGL_DMA_BUF_PLANE0_FD_EXT = 0x3272,
        EGL_DMA_BUF_PLANE0_OFFSET_EXT = 0x3273, EGL_DMA_BUF_PLANE0_PITCH_EXT = 0x3274,
        EGL_DMA_BUF_PLANE0_MODIFIER_LO_EXT = 0x3443, EGL_DMA_BUF_PLANE0_MODIFIER_HI_EXT = 0x3444;
    public const int DRM_FORMAT_ARGB8888 = 0x34325241;   // 'AR24': B, G, R, A in memory, CEF's BGRA

    public const uint GL_TEXTURE_2D = 0x0DE1, GL_RGBA8 = 0x8058, GL_RGBA = 0x1908, GL_BGRA = 0x80E1,
        GL_UNSIGNED_BYTE = 0x1401, GL_READ_FRAMEBUFFER = 0x8CA8, GL_DRAW_FRAMEBUFFER = 0x8CA9,
        GL_COLOR_ATTACHMENT0 = 0x8CE0, GL_COLOR_BUFFER_BIT = 0x4000, GL_NEAREST = 0x2600,
        GL_FRAMEBUFFER_COMPLETE = 0x8CD5, GL_SYNC_GPU_COMMANDS_COMPLETE = 0x9117,
        GL_SYNC_FLUSH_COMMANDS_BIT = 1, GL_FRAMEBUFFER = 0x8D40;

    public static nint Display, Context;
    public static string Extensions = "";

    public static delegate* unmanaged<nint, nint, uint, nint, int*, nint> eglCreateImageKHR;
    public static delegate* unmanaged<nint, nint, uint> eglDestroyImageKHR;
    public static delegate* unmanaged<uint, nint, void> glEGLImageTargetTexture2DOES;
    public static delegate* unmanaged<int, uint*, void> glGenTextures, glGenFramebuffers;
    public static delegate* unmanaged<int, uint*, void> glDeleteTextures, glDeleteFramebuffers;
    public static delegate* unmanaged<uint, uint, void> glBindTexture, glBindFramebuffer;
    public static delegate* unmanaged<uint, int, uint, int, int, void> glTexStorage2D;
    public static delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> glTexSubImage2D;
    public static delegate* unmanaged<uint, uint, uint, uint, int, void> glFramebufferTexture2D;
    public static delegate* unmanaged<uint, uint> glCheckFramebufferStatus;
    public static delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void> glBlitFramebuffer;
    public static delegate* unmanaged<int, int, int, int, uint, uint, void*, void> glReadPixels;
    public static delegate* unmanaged<void> glFinish, glFlush;
    public static delegate* unmanaged<uint> glGetError;
    public static delegate* unmanaged<uint, uint, nint> glFenceSync;
    public static delegate* unmanaged<nint, uint, ulong, uint> glClientWaitSync;
    public static delegate* unmanaged<nint, void> glDeleteSync;

    /// <summary>Creates the display and a GL ES 3 context, and makes it current on the calling thread.</summary>
    public static string Init()
    {
        Display = eglGetPlatformDisplay(EGL_PLATFORM_SURFACELESS_MESA, 0, null);
        int maj, min;
        if (Display == 0 || eglInitialize(Display, &maj, &min) == 0) return $"eglInitialize failed 0x{eglGetError():X}";
        Extensions = Marshal.PtrToStringAnsi(eglQueryString(Display, EGL_EXTENSIONS)) ?? "";
        eglBindAPI(EGL_OPENGL_ES_API);
        int* attribs = stackalloc int[] { EGL_CONTEXT_MAJOR_VERSION, 3, EGL_NONE };
        Context = eglCreateContext(Display, 0, 0, attribs);   // EGL_KHR_no_config_context
        if (Context == 0) return $"eglCreateContext failed 0x{eglGetError():X}";
        MakeCurrent();
        static nint P(string n) { var p = eglGetProcAddress(n); return p != 0 ? p : throw new Exception(n); }
        eglCreateImageKHR = (delegate* unmanaged<nint, nint, uint, nint, int*, nint>)P("eglCreateImageKHR");
        eglDestroyImageKHR = (delegate* unmanaged<nint, nint, uint>)P("eglDestroyImageKHR");
        glEGLImageTargetTexture2DOES = (delegate* unmanaged<uint, nint, void>)P("glEGLImageTargetTexture2DOES");
        glGenTextures = (delegate* unmanaged<int, uint*, void>)P("glGenTextures");
        glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)P("glGenFramebuffers");
        glDeleteTextures = (delegate* unmanaged<int, uint*, void>)P("glDeleteTextures");
        glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)P("glDeleteFramebuffers");
        glBindTexture = (delegate* unmanaged<uint, uint, void>)P("glBindTexture");
        glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)P("glBindFramebuffer");
        glTexStorage2D = (delegate* unmanaged<uint, int, uint, int, int, void>)P("glTexStorage2D");
        glTexSubImage2D = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void>)P("glTexSubImage2D");
        glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)P("glFramebufferTexture2D");
        glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)P("glCheckFramebufferStatus");
        glBlitFramebuffer = (delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void>)P("glBlitFramebuffer");
        glReadPixels = (delegate* unmanaged<int, int, int, int, uint, uint, void*, void>)P("glReadPixels");
        glFinish = (delegate* unmanaged<void>)P("glFinish");
        glFlush = (delegate* unmanaged<void>)P("glFlush");
        glGetError = (delegate* unmanaged<uint>)P("glGetError");
        glFenceSync = (delegate* unmanaged<uint, uint, nint>)P("glFenceSync");
        glClientWaitSync = (delegate* unmanaged<nint, uint, ulong, uint>)P("glClientWaitSync");
        glDeleteSync = (delegate* unmanaged<nint, void>)P("glDeleteSync");
        return $"EGL {maj}.{min} surfaceless, dma_buf_import {Extensions.Contains("EGL_EXT_image_dma_buf_import ")}, " +
               $"modifiers {Extensions.Contains("EGL_EXT_image_dma_buf_import_modifiers")}";
    }

    public static void MakeCurrent() => eglMakeCurrent(Display, 0, 0, Context);

    /// <summary>A texture of ours with a framebuffer on it: where each frame is copied to.</summary>
    public static (uint tex, uint fbo) Target(int w, int h)
    {
        uint tex, fbo;
        glGenTextures(1, &tex);
        glBindTexture(GL_TEXTURE_2D, tex);
        glTexStorage2D(GL_TEXTURE_2D, 1, GL_RGBA8, w, h);
        glGenFramebuffers(1, &fbo);
        glBindFramebuffer(GL_FRAMEBUFFER, fbo);
        glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, tex, 0);
        return (tex, fbo);
    }

    /// <summary>EGLImage from a single-plane dmabuf; 0 on failure.</summary>
    public static nint Import(int fd, int w, int h, uint stride, ulong offset, ulong modifier)
    {
        int* a = stackalloc int[]
        {
            EGL_WIDTH, w, EGL_HEIGHT, h, EGL_LINUX_DRM_FOURCC_EXT, DRM_FORMAT_ARGB8888,
            EGL_DMA_BUF_PLANE0_FD_EXT, fd, EGL_DMA_BUF_PLANE0_OFFSET_EXT, (int)offset,
            EGL_DMA_BUF_PLANE0_PITCH_EXT, (int)stride,
            EGL_DMA_BUF_PLANE0_MODIFIER_LO_EXT, (int)(modifier & 0xFFFFFFFF),
            EGL_DMA_BUF_PLANE0_MODIFIER_HI_EXT, (int)(modifier >> 32),
            EGL_NONE,
        };
        return eglCreateImageKHR(Display, 0, EGL_LINUX_DMA_BUF_EXT, 0, a);
    }

    public static int LastEglError => eglGetError();
}
