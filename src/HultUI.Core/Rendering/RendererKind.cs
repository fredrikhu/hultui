namespace HultUI.Core.Rendering;

/// <summary>
/// Which graphics API a window draws through.
///
/// <para>This exists because HDR output does: an HDR swapchain needs Vulkan, WGL cannot give
/// one, and until now the only way to ask for Vulkan was the <c>HULTUI_RENDERER</c> environment
/// variable — which meant an app could not ship with HDR video, only be launched with it by
/// someone who knew the variable existed. See <c>docs/design/HDR.md</c>.</para>
/// </summary>
public enum RendererKind
{
    /// <summary>
    /// Whatever the framework picks, which today is OpenGL everywhere. The default, and the
    /// path every shipped build has used — see the renderer comparison in
    /// <c>docs/design/HDR.md</c> for why Vulkan is not it yet.
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
    /// platform cannot provide it.</para>
    /// </summary>
    Vulkan,
}
