using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Platform.SDL3;
using SDL;
using Xunit;
using static SDL.SDL3;

namespace HultUI.UiTests;

/// <summary>
/// What a GL window's default framebuffer holds, as the context reports it rather than as
/// it was asked for. Skia draws into it with a stencil (the surface tells it 8 bits) and
/// never with a depth buffer. SDL's default asked for 16 bits of depth, which NVIDIA
/// granted as 24 packed with the stencil: about 3 bytes a pixel nobody read.
/// </summary>
[Collection("ui")]
public sealed class GLFramebufferTests
{
    [UiFact]
    public unsafe void TheWindowFramebuffer_HasAStencil_AndNoDepthBuffer()
    {
        using var app = new Application(new SdlWindowingPlatform(), "framebuffer", 320, 200);
        app.SetRoot(Box.New().Width(200f).Height(100f).CornerRadius(12f).Background(Color.FromHex("#5b8def")));
        // Forced, as in PixelFrameGLTests: a second SDL application in one test process can
        // report its first unforced frame as not rendered.
        Assert.True(app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true));

        // The window's context is current on this thread, which built it.
        int depth = -1, stencil = -1;
        Assert.True(SDL_GL_GetAttribute(SDL_GLAttr.SDL_GL_DEPTH_SIZE, &depth), SDL_GetError());
        Assert.True(SDL_GL_GetAttribute(SDL_GLAttr.SDL_GL_STENCIL_SIZE, &stencil), SDL_GetError());

        Assert.Equal(8, stencil);
        // A driver with no stencil-only pixel format would give 24 here although 0 was asked
        // for; NVIDIA has one.
        Assert.Equal(0, depth);
    }
}
