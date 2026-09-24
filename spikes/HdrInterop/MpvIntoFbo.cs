using System.Runtime.InteropServices;
using static HdrInterop.MpvNative;
using static SDL.SDL3;

namespace HdrInterop;

/// <summary>
/// mpv, told to render one frame into a framebuffer it did not create.
///
/// <para>This is the ordinary render-API path the media backend already uses — the only
/// difference is where the framebuffer's memory came from. If mpv minds, it minds here.</para>
/// </summary>
internal static unsafe class MpvIntoFbo
{
    public static (bool Ok, string Message) Render(string path, uint fbo, int width, int height,
        bool colorspaceHint)
    {
        if (!MpvNative.IsAvailable) return (false, "no libmpv found");

        nint mpv = mpv_create();
        if (mpv == 0) return (false, "mpv_create returned null");

        try
        {
            mpv_set_option_string(mpv, "vo", "libmpv");
            mpv_set_option_string(mpv, "audio", "no");
            mpv_set_option_string(mpv, "terminal", "no");
            mpv_set_option_string(mpv, "osc", "no");
            mpv_set_option_string(mpv, "input-default-bindings", "no");
            mpv_set_option_string(mpv, "pause", "yes");

            // Off by default, and that is a finding rather than a preference. Asking for it
            // tells mpv to render for the target's colour space — but the API user is then
            // expected to say what that space is, through the render params, and this spike
            // says nothing. A 1080p SDR file came back clipped to white with it on, which is
            // what an unstated very-bright target would do. Phase 4 is where it belongs,
            // once there is a real swapchain with a real colour space to hint at.
            if (colorspaceHint) mpv_set_option_string(mpv, "target-colorspace-hint", "yes");

            // A .rawvideo file is the spike's own synthetic clip: this build of libmpv is
            // trimmed and has no lavfi, so there is no generated source to ask for, and a
            // repository is no place for a sample film. The demuxer has to be told the
            // shape by hand because raw frames carry no header saying it.
            if (path.EndsWith(".rawvideo", StringComparison.OrdinalIgnoreCase))
            {
                mpv_set_option_string(mpv, "demuxer", "rawvideo");
                mpv_set_option_string(mpv, "demuxer-rawvideo-w", width.ToString());
                mpv_set_option_string(mpv, "demuxer-rawvideo-h", height.ToString());
                mpv_set_option_string(mpv, "demuxer-rawvideo-mp-format", "bgra");
                mpv_set_option_string(mpv, "demuxer-rawvideo-fps", "25");
            }

            if (mpv_initialize(mpv) < 0) return (false, "mpv_initialize failed");

            nint render = CreateGlRenderContext(mpv);
            if (render == 0) return (false, "mpv_render_context_create failed");

            try
            {
                if (Command(mpv, "loadfile", path) < 0) return (false, "loadfile failed");
                if (!WaitFor(mpv, MPV_EVENT_FILE_LOADED, TimeSpan.FromSeconds(20)))
                    return (false, "the file never loaded");

                // A frame has to exist before there is anything to render.
                if (!WaitFor(mpv, MPV_EVENT_PLAYBACK_RESTART, TimeSpan.FromSeconds(10)))
                    return (false, "playback never started");

                // The render call wants the FBO's id and size. internal_format 0 means
                // "whatever the attachment already is", which here is the RGBA16F storage
                // the Vulkan image was created with.
                var target = new mpv_opengl_fbo
                {
                    fbo = (int)fbo,
                    w = width,
                    h = height,
                    internal_format = 0,
                };
                int flipY = 1;

                // Allocated once, outside the loop: a stackalloc inside one is not freed per
                // iteration, so thirty passes would be thirty allocations living to the end
                // of the method. Harmless at this size and wrong at any other (CA2014).
                var parameters = stackalloc mpv_render_param[3];
                parameters[0] = new mpv_render_param { type = MPV_RENDER_PARAM_OPENGL_FBO, data = &target };
                parameters[1] = new mpv_render_param { type = MPV_RENDER_PARAM_FLIP_Y, data = &flipY };
                parameters[2] = default;

                // A few passes: the first can land before the decoder has produced anything,
                // and an empty frame would read back as the clear colour.
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    mpv_render_context_update(render);

                    int error = mpv_render_context_render(render, parameters);
                    if (error < 0) return (false, $"mpv_render_context_render: {error}");

                    Gl.Finish();
                    uint glError = Gl.Error();
                    if (glError != 0) return (false, $"GL error after mpv rendered: 0x{glError:X}");
                    Thread.Sleep(30);
                }

                return (true, "rendered");
            }
            finally
            {
                mpv_render_context_free(render);
            }
        }
        finally
        {
            mpv_terminate_destroy(mpv);
        }
    }

    private static nint CreateGlRenderContext(nint mpv)
    {
        var apiType = "opengl\0"u8.ToArray();
        fixed (byte* api = apiType)
        {
            var init = new mpv_opengl_init_params
            {
                get_proc_address = &GetProcAddressThunk,
                get_proc_address_ctx = null,
            };
            var parameters = stackalloc mpv_render_param[3];
            parameters[0] = new mpv_render_param { type = MPV_RENDER_PARAM_API_TYPE, data = api };
            parameters[1] = new mpv_render_param { type = MPV_RENDER_PARAM_OPENGL_INIT_PARAMS, data = &init };
            parameters[2] = default;

            nint context;
            return mpv_render_context_create(&context, mpv, parameters) < 0 ? 0 : context;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void* GetProcAddressThunk(void* ctx, byte* name)
    {
        var managed = Marshal.PtrToStringUTF8((nint)name);
        return managed is null ? null : (void*)SDL_GL_GetProcAddress(managed);
    }

    private static bool WaitFor(nint mpv, int wanted, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var evt = mpv_wait_event(mpv, 0.05);
            if (evt is null) continue;
            if (evt->event_id == wanted) return true;
            if (evt->event_id is MPV_EVENT_END_FILE or MPV_EVENT_SHUTDOWN) return false;
        }
        return false;
    }
}
