namespace HultUI.Media.Mpv;

/// <summary>
/// The format mpv keeps its intermediate pictures in, on GL ES, where the one it picks may not
/// copy into the sink's texture, or may not draw.
///
/// <para>A still picture (a paused video, a seek while paused, a file opened paused) is drawn
/// once into a cache texture of mpv's <c>fbo-format</c>, and then copied into the target with
/// <c>glBlitFramebuffer</c>, so a redraw costs a copy. Through the render API the target is a
/// wrapped framebuffer whose format mpv does not know, so the cache takes mpv's own choice,
/// rgba16f, and the copy goes from half-float into the sink's RGBA8 texture. GL ES 3.0 allows
/// that, but the Android emulator's GL ES refuses it (GL_INVALID_OPERATION): it judges a blit by
/// the type each texture was made with, and mpv makes rgba16f with GL_FLOAT. The target stayed
/// black, so every paused picture was black while playback, which draws straight into the
/// target, showed.</para>
///
/// <para>So on GL ES the copy is tried once, before mpv's render context exists, with textures
/// made as mpv makes them. If it fails, mpv is given the first of its other formats that does
/// copy: rgba16, rgb10_a2, then rgba8, which copies into RGBA8 on any driver. The emulator lands
/// on rgb10_a2. The cost is the precision of mpv's intermediate passes, and only on a driver
/// that needs it.</para>
///
/// <para>An Adreno 5xx is given another format without the probe. A Galaxy Tab S4's Adreno 540
/// (Android 10, driver V@415) drew mpv's rgba16f pictures in part: a film showed only a strip
/// of each frame, or with a hardware decoder's frames, bands of older frames beside the new one.
/// rgba16, rgb10_a2 and rgba8 drew whole there, mpv's own test of rgba16f passed, and so did
/// the probe's copy, at 4×4 and at a 1080p frame's size, cleared and read back: the fault is in
/// what mpv draws, which no small probe here reproduces.</para>
/// </summary>
internal static unsafe class GlesFboFormat
{
    private const uint GL_RENDERER = 0x1F01, GL_VERSION = 0x1F02, GL_TEXTURE_2D = 0x0DE1,
        GL_FRAMEBUFFER = 0x8D40, GL_READ_FRAMEBUFFER = 0x8CA8, GL_DRAW_FRAMEBUFFER = 0x8CA9,
        GL_COLOR_ATTACHMENT0 = 0x8CE0, GL_FRAMEBUFFER_COMPLETE = 0x8CD5, GL_COLOR_BUFFER_BIT = 0x4000,
        GL_NEAREST = 0x2600,
        GL_RGBA = 0x1908, GL_UNSIGNED_BYTE = 0x1401, GL_UNSIGNED_SHORT = 0x1403, GL_FLOAT = 0x1406,
        GL_UNSIGNED_INT_2_10_10_10_REV = 0x8368, GL_RGBA8 = 0x8058, GL_RGBA16F = 0x881A,
        GL_RGBA16_EXT = 0x805B, GL_RGB10_A2 = 0x8059;

    /// <summary>An mpv format name, with the internal format and type mpv's GL table makes it
    /// with (video/out/opengl/formats.c).</summary>
    private readonly record struct Format(string Name, uint Internal, uint Type);

    /// <summary>mpv's own first choice: "auto" starts with it.</summary>
    private static readonly Format MpvsChoice = new("rgba16f", GL_RGBA16F, GL_FLOAT);

    /// <summary>What mpv falls back to after its float formats, in its own order. rgba16 needs
    /// EXT_texture_norm16, and is skipped where the texture cannot be made.</summary>
    private static readonly Format[] Fallbacks =
    [
        new("rgba16", GL_RGBA16_EXT, GL_UNSIGNED_SHORT),
        new("rgb10_a2", GL_RGB10_A2, GL_UNSIGNED_INT_2_10_10_10_REV),
    ];

    /// <summary>Why the last <see cref="Choose"/> passed over mpv's choice, for the log.</summary>
    public static string? Why { get; private set; }

    /// <summary>
    /// The <c>fbo-format</c> mpv should be given, or null to leave its own choice: on desktop GL,
    /// where any copy is allowed; on GL ES when its choice copies; and when the probe cannot tell
    /// (a missing entry point, or rgba16f not renderable, in which case mpv picks another format
    /// too). GL thread, with the context current.
    /// </summary>
    public static string? Choose(Func<string, nint> getProcAddress)
    {
        Why = null;
        if (!Gl.TryLoad(getProcAddress, out var gl)) return null;
        var version = (sbyte*)gl.GetString(GL_VERSION);
        if (version is null || !new string(version).StartsWith("OpenGL ES", StringComparison.Ordinal))
            return null;

        var renderer = (sbyte*)gl.GetString(GL_RENDERER);
        if (renderer is not null && DrawsHalfFloatInPart(new string(renderer)))
            Why = $"{new string(renderer)} draws rgba16f in part";
        else if (CopiesInto8Bit(gl, MpvsChoice) is not false)
            return null;
        else
            Why = "the copy of rgba16f into RGBA8 was refused";

        foreach (var format in Fallbacks)
            if (CopiesInto8Bit(gl, format) is true) return format.Name;
        return "rgba8";
    }

    /// <summary>Whether the GPU is one that draws mpv's rgba16f pictures in part: an Adreno 5xx
    /// (the Adreno 540 of a Galaxy Tab S4, and the rest of its generation's drivers).</summary>
    internal static bool DrawsHalfFloatInPart(string renderer)
    {
        const string Adreno = "Adreno (TM) 5";
        return renderer.StartsWith(Adreno, StringComparison.Ordinal)
            && renderer.Length >= Adreno.Length + 2
            && char.IsAsciiDigit(renderer[Adreno.Length]) && char.IsAsciiDigit(renderer[Adreno.Length + 1])
            && (renderer.Length == Adreno.Length + 2 || !char.IsAsciiDigit(renderer[Adreno.Length + 2]));
    }

    /// <summary>Whether a blit from a texture of <paramref name="format"/> into an RGBA8 one
    /// succeeds; null if the format cannot be made or rendered to.</summary>
    private static bool? CopiesInto8Bit(in Gl gl, Format format)
    {
        // Errors left by others would read as this probe's.
        for (int i = 0; i < 16 && gl.GetError() != 0; i++) { }

        uint* textures = stackalloc uint[2];
        uint* framebuffers = stackalloc uint[2];
        gl.GenTextures(2, textures);
        gl.GenFramebuffers(2, framebuffers);
        try
        {
            if (!Attach(gl, textures[0], framebuffers[0], format.Internal, format.Type)) return null;
            if (!Attach(gl, textures[1], framebuffers[1], GL_RGBA8, GL_UNSIGNED_BYTE)) return null;

            gl.BindFramebuffer(GL_READ_FRAMEBUFFER, framebuffers[0]);
            gl.BindFramebuffer(GL_DRAW_FRAMEBUFFER, framebuffers[1]);
            gl.BlitFramebuffer(0, 0, 4, 4, 0, 0, 4, 4, GL_COLOR_BUFFER_BIT, GL_NEAREST);
            return gl.GetError() == 0;
        }
        finally
        {
            gl.BindFramebuffer(GL_FRAMEBUFFER, 0);
            gl.BindTexture(GL_TEXTURE_2D, 0);
            gl.DeleteFramebuffers(2, framebuffers);
            gl.DeleteTextures(2, textures);
        }
    }

    /// <summary>A 4×4 texture of the format, attached to the framebuffer; false if either
    /// cannot be made.</summary>
    private static bool Attach(in Gl gl, uint texture, uint framebuffer, uint internalFormat, uint type)
    {
        gl.BindTexture(GL_TEXTURE_2D, texture);
        gl.TexImage2D(GL_TEXTURE_2D, 0, (int)internalFormat, 4, 4, 0, GL_RGBA, type, null);
        gl.BindFramebuffer(GL_FRAMEBUFFER, framebuffer);
        gl.FramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, texture, 0);
        return gl.CheckFramebufferStatus(GL_FRAMEBUFFER) == GL_FRAMEBUFFER_COMPLETE && gl.GetError() == 0;
    }

    private struct Gl
    {
        public delegate* unmanaged<uint, nint> GetString;
        public delegate* unmanaged<uint> GetError;
        public delegate* unmanaged<int, uint*, void> GenTextures;
        public delegate* unmanaged<int, uint*, void> DeleteTextures;
        public delegate* unmanaged<uint, uint, void> BindTexture;
        public delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> TexImage2D;
        public delegate* unmanaged<int, uint*, void> GenFramebuffers;
        public delegate* unmanaged<int, uint*, void> DeleteFramebuffers;
        public delegate* unmanaged<uint, uint, void> BindFramebuffer;
        public delegate* unmanaged<uint, uint, uint, uint, int, void> FramebufferTexture2D;
        public delegate* unmanaged<uint, uint> CheckFramebufferStatus;
        public delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void> BlitFramebuffer;

        public static bool TryLoad(Func<string, nint> resolve, out Gl gl)
        {
            gl = default;
            nint getString = resolve("glGetString"), getError = resolve("glGetError"),
                genTextures = resolve("glGenTextures"), deleteTextures = resolve("glDeleteTextures"),
                bindTexture = resolve("glBindTexture"), texImage2D = resolve("glTexImage2D"),
                genFramebuffers = resolve("glGenFramebuffers"), deleteFramebuffers = resolve("glDeleteFramebuffers"),
                bindFramebuffer = resolve("glBindFramebuffer"), framebufferTexture2D = resolve("glFramebufferTexture2D"),
                checkStatus = resolve("glCheckFramebufferStatus"), blit = resolve("glBlitFramebuffer");
            if (getString == 0 || getError == 0 || genTextures == 0 || deleteTextures == 0 || bindTexture == 0
                || texImage2D == 0 || genFramebuffers == 0 || deleteFramebuffers == 0 || bindFramebuffer == 0
                || framebufferTexture2D == 0 || checkStatus == 0 || blit == 0)
                return false;
            gl = new Gl
            {
                GetString = (delegate* unmanaged<uint, nint>)getString,
                GetError = (delegate* unmanaged<uint>)getError,
                GenTextures = (delegate* unmanaged<int, uint*, void>)genTextures,
                DeleteTextures = (delegate* unmanaged<int, uint*, void>)deleteTextures,
                BindTexture = (delegate* unmanaged<uint, uint, void>)bindTexture,
                TexImage2D = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void>)texImage2D,
                GenFramebuffers = (delegate* unmanaged<int, uint*, void>)genFramebuffers,
                DeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)deleteFramebuffers,
                BindFramebuffer = (delegate* unmanaged<uint, uint, void>)bindFramebuffer,
                FramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)framebufferTexture2D,
                CheckFramebufferStatus = (delegate* unmanaged<uint, uint>)checkStatus,
                BlitFramebuffer = (delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void>)blit,
            };
            return true;
        }
    }
}
