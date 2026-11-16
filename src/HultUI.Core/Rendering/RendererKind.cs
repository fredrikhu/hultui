namespace HultUI.Core.Rendering;

/// <summary>
/// Which graphics API a window draws through.
///
/// <para>This exists because HDR output does: an HDR swapchain needs Vulkan, and WGL cannot
/// give one. An app chooses here, and the <c>HULTUI_RENDERER</c> environment variable overrides
/// the choice in either direction. See <c>docs/design/HDR.md</c>. <see cref="Software"/> is the
/// renderer for machines with neither.</para>
/// </summary>
public enum RendererKind
{
    /// <summary>
    /// Whatever the framework picks, which today is OpenGL everywhere. The costs in
    /// <c>docs/design/HDR.md</c> are why Vulkan is not the default: apps without HDR video
    /// would pay them for nothing.
    /// </summary>
    Default,

    /// <summary>OpenGL, explicitly. Distinct from <see cref="Default"/> only in saying so:
    /// an app that must not be moved off GL by a future change to the default says this.</summary>
    OpenGL,

    /// <summary>
    /// Vulkan — the only renderer that can present HDR, and what Reel and Aperture want.
    ///
    /// <para>Costs more than OpenGL at start-up and through a resize, both of which are
    /// measured in <c>docs/design/HDR.md</c>; an app asking for this is trading those for HDR
    /// output and sixteen bits a channel. Falls back to OpenGL, with a line on stderr, when the
    /// platform cannot provide it, unless <c>HULTUI_RENDERER=vulkan</c> asked, in which case the
    /// failure stands.</para>
    /// </summary>
    Vulkan,

    /// <summary>
    /// Skia on the CPU, into the window surface SDL keeps in main memory: no GPU and no
    /// graphics driver at all. What a window falls back to when OpenGL cannot start, as on a
    /// Windows server or VM without a GPU, or in a remote desktop session there, where Windows
    /// offers only OpenGL 1.1. Costs CPU for every pixel drawn, so it is a fallback, not a
    /// choice for an app that has a GPU. A GPU image source (video, a web view) shows only
    /// through its pixel path, when it has one.
    /// </summary>
    Software,
}
