using System.Runtime.InteropServices;
using HultUI.Core.Rendering;
using HultUI.Rendering.Skia;
using SDL;
using static SDL.SDL3;

namespace HultUI.Platform.SDL3;

public sealed unsafe class SDL3Window : IDisposable
{
    private SDL_Window* _window;
    private SDL_GLContextState* _glContext;
    private SkiaGLRenderSurface? _surface;
    private bool _disposed;

    public int PixelWidth { get; private set; }
    public int PixelHeight { get; private set; }
    public int LogicalWidth { get; private set; }
    public int LogicalHeight { get; private set; }
    public float ScaleFactor { get; private set; } = 1f;

    public IRenderSurface Surface => _surface ?? throw new InvalidOperationException("Window not initialized");
    public bool IsOpen { get; private set; }

    public EventTranslator EventTranslator { get; } = new();

    public event Action? Resized;
    public event Action? CloseRequested;

    public SDL3Window(string title, int width, int height)
    {
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
            throw new InvalidOperationException($"SDL_Init failed: {GetError()}");

        // Set OpenGL attributes for SkiaSharp compatibility
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MAJOR_VERSION, 3);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MINOR_VERSION, 3);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_PROFILE_MASK, (int)SDL_GLProfile.SDL_GL_CONTEXT_PROFILE_CORE);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_DOUBLEBUFFER, 1);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_STENCIL_SIZE, 8);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_RED_SIZE, 8);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_GREEN_SIZE, 8);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_BLUE_SIZE, 8);
        SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_ALPHA_SIZE, 8);

        var flags = SDL_WindowFlags.SDL_WINDOW_OPENGL
            | SDL_WindowFlags.SDL_WINDOW_RESIZABLE
            | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY;

        var titleBytes = MarshalUtf8(title);
        fixed (byte* titlePtr = titleBytes)
        {
            _window = SDL_CreateWindow(titlePtr, width, height, flags);
        }

        if (_window == null)
            throw new InvalidOperationException($"SDL_CreateWindow failed: {GetError()}");

        _glContext = SDL_GL_CreateContext(_window);
        if (_glContext == null)
            throw new InvalidOperationException($"SDL_GL_CreateContext failed: {GetError()}");

        SDL_GL_MakeCurrent(_window, _glContext);

        // Create SkiaSharp surface with SDL3 GL proc resolver
        _surface = new SkiaGLRenderSurface(GetGLProcAddress);

        UpdateSize();
        IsOpen = true;
    }

    public void PollEvents()
    {
        SDL_Event evt;
        while (SDL_PollEvent(&evt))
        {
            switch (evt.type)
            {
                case (uint)SDL_EventType.SDL_EVENT_QUIT:
                    IsOpen = false;
                    CloseRequested?.Invoke();
                    break;

                case (uint)SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
                case (uint)SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
                    UpdateSize();
                    Resized?.Invoke();
                    break;

                default:
                    EventTranslator.ProcessEvent(&evt);
                    break;
            }
        }
    }

    public void SwapBuffers()
    {
        SDL_GL_SwapWindow(_window);
    }

    private void UpdateSize()
    {
        int pw, ph, lw, lh;
        SDL_GetWindowSizeInPixels(_window, &pw, &ph);
        SDL_GetWindowSize(_window, &lw, &lh);

        PixelWidth = pw;
        PixelHeight = ph;
        LogicalWidth = lw;
        LogicalHeight = lh;
        ScaleFactor = lw > 0 ? (float)pw / lw : 1f;

        _surface?.Resize(pw, ph, ScaleFactor);
    }

    private static IntPtr GetGLProcAddress(string name)
    {
        var nameBytes = MarshalUtf8(name);
        fixed (byte* namePtr = nameBytes)
        {
            return SDL_GL_GetProcAddress(namePtr);
        }
    }

    private static string GetError()
    {
        return SDL_GetError() ?? "Unknown error";
    }

    private static byte[] MarshalUtf8(string text)
    {
        var bytes = new byte[System.Text.Encoding.UTF8.GetByteCount(text) + 1];
        System.Text.Encoding.UTF8.GetBytes(text, bytes);
        bytes[^1] = 0; // null terminator
        return bytes;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _surface?.Dispose();
        _surface = null;

        if (_glContext != null)
        {
            SDL_GL_DestroyContext(_glContext);
            _glContext = null;
        }

        if (_window != null)
        {
            SDL_DestroyWindow(_window);
            _window = null;
        }

        SDL_Quit();
    }
}
