using System.Runtime.InteropServices;
using HultUI.Core.Rendering;
using HultUI.Rendering.Skia;
using SDL;
using static SDL.SDL3;

namespace HultUI.Platform.SDL3;

/// <summary>
/// HDR10 on an Android TV, the window's half (docs/design/TV-VIDEO.md §4). The window's framebuffer
/// is half float (a Tegra X1 has no 10-bit surface), and this tags its buffers for SurfaceFlinger
/// and the television: sRGB while the frame is drawn in sRGB, BT.2100 PQ with the film's metadata
/// while an HDR video is on screen. Android switches the display to HDR10 while a PQ layer shows,
/// and back after.
///
/// <para>The tag is the buffers' dataspace (<c>ANativeWindow_setBuffersDataSpace</c>), set after a
/// swap so the frame drawn in the old space goes out with the old tag. EGL sets it only as it makes
/// the surface, which is made in its default space; the GL driver writes PQ and sRGB values alike.
/// The metadata goes through EGL's surface attributes, which Android's EGL hands the buffers at the
/// next swap.</para>
/// </summary>
internal sealed unsafe class AndroidHdrOutput(SDL_Window* window) : IGlHdrOutput
{
    private bool _pq;
    private HdrMetadata? _metadata;
    // The first swap tags the buffers sRGB; Android gives the surface a new window after the app
    // comes back, which starts untagged.
    private bool _pending = true;
    private nint _taggedWindow;

    /// <inheritdoc />
    public void SetPq(bool pq, HdrMetadata? metadata)
    {
        _pq = pq;
        _metadata = metadata;
        _pending = true;
    }

    /// <summary>After each swap: tags the next buffers if asked to, or if the surface's window is
    /// a new one.</summary>
    public void AfterSwap()
    {
        nint native;
        fixed (byte* name = SDL_PROP_WINDOW_ANDROID_WINDOW_POINTER)
            native = SDL_GetPointerProperty(SDL_GetWindowProperties(window), name, IntPtr.Zero);
        if (native == 0 || (!_pending && native == _taggedWindow)) return;
        _pending = false;
        _taggedWindow = native;

        int tagged = ANativeWindow_setBuffersDataSpace(native, _pq ? DataspaceBt2020Pq : DataspaceSrgb);
        int metadata = 0, refused = 0;
        if (_pq && _metadata is { } m && SDL_EGL_GetCurrentDisplay() is var display && display != 0
            && SDL_EGL_GetWindowSurface(window) is var surface && surface != 0)
            foreach (var (attribute, value) in EglAttributes(m))
                if (eglSurfaceAttrib(display, surface, attribute, value) != 0) metadata++;
                else refused++;
        Console.Error.WriteLine(_pq
            ? $"[HultUI] HDR10 output: buffers BT.2100 PQ ({(tagged == 0 ? "tagged" : $"tag refused: {tagged}")}), " +
              $"metadata {(_metadata is null ? "none" : $"{metadata} set{(refused > 0 ? $", {refused} refused" : "")}")}"
            : $"[HultUI] HDR10 output: buffers sRGB ({(tagged == 0 ? "tagged" : $"tag refused: {tagged}")})");
    }

    /// <summary>
    /// The metadata as EGL surface attributes: SMPTE ST 2086 (<c>EGL_EXT_surface_SMPTE2086_metadata</c>)
    /// when the mastering display is stated, CTA-861.3 (<c>EGL_EXT_surface_CTA861_3_metadata</c>) when
    /// the light levels are. Each value in units of 1/50,000 (<c>EGL_METADATA_SCALING_EXT</c>): an xy
    /// coordinate, or nits.
    /// </summary>
    internal static List<(int Attribute, int Value)> EglAttributes(HdrMetadata m)
    {
        // At most 40,000: an EGLint holds 42,949 nits at this scale.
        static int Scaled(float v) => (int)Math.Round(Math.Clamp((double)v, 0d, 40_000d) * MetadataScaling);
        var attributes = new List<(int, int)>(12);
        if (m.HasMasteringDisplay)
        {
            attributes.Add((EGL_SMPTE2086_DISPLAY_PRIMARY_RX_EXT, Scaled(m.RedX)));
            attributes.Add((EGL_SMPTE2086_DISPLAY_PRIMARY_RY_EXT, Scaled(m.RedY)));
            attributes.Add((EGL_SMPTE2086_DISPLAY_PRIMARY_GX_EXT, Scaled(m.GreenX)));
            attributes.Add((EGL_SMPTE2086_DISPLAY_PRIMARY_GY_EXT, Scaled(m.GreenY)));
            attributes.Add((EGL_SMPTE2086_DISPLAY_PRIMARY_BX_EXT, Scaled(m.BlueX)));
            attributes.Add((EGL_SMPTE2086_DISPLAY_PRIMARY_BY_EXT, Scaled(m.BlueY)));
            attributes.Add((EGL_SMPTE2086_WHITE_POINT_X_EXT, Scaled(m.WhiteX)));
            attributes.Add((EGL_SMPTE2086_WHITE_POINT_Y_EXT, Scaled(m.WhiteY)));
            attributes.Add((EGL_SMPTE2086_MAX_LUMINANCE_EXT, Scaled(m.MaxLuminance)));
            attributes.Add((EGL_SMPTE2086_MIN_LUMINANCE_EXT, Scaled(m.MinLuminance)));
        }
        if (m.HasLightLevels)
        {
            attributes.Add((EGL_CTA861_3_MAX_CONTENT_LIGHT_LEVEL_EXT, Scaled(m.MaxContentLightLevel)));
            attributes.Add((EGL_CTA861_3_MAX_FRAME_AVERAGE_LEVEL_EXT, Scaled(m.MaxFrameAverageLightLevel)));
        }
        return attributes;
    }

    internal const int MetadataScaling = 50_000;
    internal const int EGL_SMPTE2086_DISPLAY_PRIMARY_RX_EXT = 0x3341, EGL_SMPTE2086_DISPLAY_PRIMARY_RY_EXT = 0x3342,
        EGL_SMPTE2086_DISPLAY_PRIMARY_GX_EXT = 0x3343, EGL_SMPTE2086_DISPLAY_PRIMARY_GY_EXT = 0x3344,
        EGL_SMPTE2086_DISPLAY_PRIMARY_BX_EXT = 0x3345, EGL_SMPTE2086_DISPLAY_PRIMARY_BY_EXT = 0x3346,
        EGL_SMPTE2086_WHITE_POINT_X_EXT = 0x3347, EGL_SMPTE2086_WHITE_POINT_Y_EXT = 0x3348,
        EGL_SMPTE2086_MAX_LUMINANCE_EXT = 0x3349, EGL_SMPTE2086_MIN_LUMINANCE_EXT = 0x334A,
        EGL_CTA861_3_MAX_CONTENT_LIGHT_LEVEL_EXT = 0x3360, EGL_CTA861_3_MAX_FRAME_AVERAGE_LEVEL_EXT = 0x3361;

    /// <summary><c>ADATASPACE_SRGB</c> and <c>ADATASPACE_BT2020_PQ</c> (android/data_space.h).</summary>
    internal const int DataspaceSrgb = 142671872, DataspaceBt2020Pq = 163971072;

    [DllImport("android")]
    private static extern int ANativeWindow_setBuffersDataSpace(nint window, int dataSpace);

    [DllImport("EGL")]
    private static extern int eglSurfaceAttrib(nint display, nint surface, int attribute, int value);
}
