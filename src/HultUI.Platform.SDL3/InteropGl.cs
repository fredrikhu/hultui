using System.Runtime.InteropServices;
using static SDL.SDL3;

namespace HultUI.Platform.SDL3;

/// <summary>
/// The handful of OpenGL entry points needed to import a Vulkan image, resolved against
/// whichever context is current when this is constructed.
///
/// <para>Hand-rolled rather than taken from a binding package, for the reason the rest of the
/// tree gives: fifteen functions do not justify a dependency, and the ones that matter here
/// are extensions (<c>GL_EXT_memory_object</c> and its platform halves) that a general
/// binding would hide behind a loader of its own.</para>
///
/// <para>Every extension function is optional. A driver without them is not a broken driver,
/// it is a driver this renderer cannot share an image with, and the caller answers that by
/// leaving the video on its software path.</para>
/// </summary>
internal sealed unsafe class InteropGl
{
    private const uint Texture2D = 0x0DE1;
    private const uint Framebuffer = 0x8D40;
    private const uint ColorAttachment0 = 0x8CE0;
    private const uint FramebufferComplete2 = 0x8CD5;
    private const uint Renderer2 = 0x1F01;
    private const uint Extensions = 0x1F03;
    private const uint NumExtensions = 0x821D;

    // GL_EXT_memory_object and its two platform halves.
    private const uint HandleTypeOpaqueWin32 = 0x9587;
    private const uint HandleTypeOpaqueFd = 0x9586;
    private const uint TextureTiling = 0x9580;
    private const uint DedicatedMemoryObject = 0x9581;
    private const uint OptimalTiling = 0x9584;
    private const uint DeviceUuidExt = 0x9597;

    private const uint ReadFramebuffer = 0x8CA8;
    private const uint Rgba = 0x1908;
    private const uint HalfFloat = 0x140B;

    private readonly delegate* unmanaged[Cdecl]<uint, byte*> _getString;
    private readonly delegate* unmanaged[Cdecl]<uint, uint, byte*> _getStringi;
    private readonly delegate* unmanaged[Cdecl]<uint, int*, void> _getIntegerv;
    private readonly delegate* unmanaged[Cdecl]<uint, byte*, void> _getUnsignedBytev;
    private readonly delegate* unmanaged[Cdecl]<uint> _getError;
    private readonly delegate* unmanaged[Cdecl]<void> _finish;

    private readonly delegate* unmanaged[Cdecl]<int, uint*, void> _createMemoryObjects;
    private readonly delegate* unmanaged[Cdecl]<int, uint*, void> _deleteMemoryObjects;
    private readonly delegate* unmanaged[Cdecl]<uint, uint, int*, void> _memoryObjectParameteriv;
    private readonly delegate* unmanaged[Cdecl]<uint, ulong, uint, void*, void> _importMemoryWin32Handle;
    private readonly delegate* unmanaged[Cdecl]<uint, ulong, uint, int, void> _importMemoryFd;
    private readonly delegate* unmanaged[Cdecl]<uint, int, uint, int, int, uint, ulong, void> _textureStorageMem2D;

    private readonly delegate* unmanaged[Cdecl]<uint, int, uint*, void> _createTextures;
    private readonly delegate* unmanaged[Cdecl]<uint, uint, int, void> _textureParameteri;
    private readonly delegate* unmanaged[Cdecl]<int, uint*, void> _deleteTextures;
    private readonly delegate* unmanaged[Cdecl]<int, uint*, void> _createFramebuffers;
    private readonly delegate* unmanaged[Cdecl]<int, uint*, void> _deleteFramebuffers;
    private readonly delegate* unmanaged[Cdecl]<uint, uint, uint, int, void> _namedFramebufferTexture;
    private readonly delegate* unmanaged[Cdecl]<uint, uint, uint> _checkNamedFramebufferStatus;
    private readonly delegate* unmanaged[Cdecl]<uint, uint, void> _bindFramebuffer;
    private readonly delegate* unmanaged[Cdecl]<int, int, int, int, uint, uint, void*, void> _readPixels;

    public InteropGl()
    {
        _getString = (delegate* unmanaged[Cdecl]<uint, byte*>)Proc("glGetString");
        _getStringi = (delegate* unmanaged[Cdecl]<uint, uint, byte*>)Proc("glGetStringi");
        _getIntegerv = (delegate* unmanaged[Cdecl]<uint, int*, void>)Proc("glGetIntegerv");
        _getUnsignedBytev = (delegate* unmanaged[Cdecl]<uint, byte*, void>)Proc("glGetUnsignedBytevEXT");
        _getError = (delegate* unmanaged[Cdecl]<uint>)Proc("glGetError");
        _finish = (delegate* unmanaged[Cdecl]<void>)Proc("glFinish");

        _createMemoryObjects = (delegate* unmanaged[Cdecl]<int, uint*, void>)Proc("glCreateMemoryObjectsEXT");
        _deleteMemoryObjects = (delegate* unmanaged[Cdecl]<int, uint*, void>)Proc("glDeleteMemoryObjectsEXT");
        _memoryObjectParameteriv = (delegate* unmanaged[Cdecl]<uint, uint, int*, void>)Proc("glMemoryObjectParameterivEXT");
        _importMemoryWin32Handle = (delegate* unmanaged[Cdecl]<uint, ulong, uint, void*, void>)Proc("glImportMemoryWin32HandleEXT");
        _importMemoryFd = (delegate* unmanaged[Cdecl]<uint, ulong, uint, int, void>)Proc("glImportMemoryFdEXT");
        _textureStorageMem2D = (delegate* unmanaged[Cdecl]<uint, int, uint, int, int, uint, ulong, void>)Proc("glTextureStorageMem2DEXT");

        _createTextures = (delegate* unmanaged[Cdecl]<uint, int, uint*, void>)Proc("glCreateTextures");
        _textureParameteri = (delegate* unmanaged[Cdecl]<uint, uint, int, void>)Proc("glTextureParameteri");
        _deleteTextures = (delegate* unmanaged[Cdecl]<int, uint*, void>)Proc("glDeleteTextures");
        _createFramebuffers = (delegate* unmanaged[Cdecl]<int, uint*, void>)Proc("glCreateFramebuffers");
        _deleteFramebuffers = (delegate* unmanaged[Cdecl]<int, uint*, void>)Proc("glDeleteFramebuffers");
        _namedFramebufferTexture = (delegate* unmanaged[Cdecl]<uint, uint, uint, int, void>)Proc("glNamedFramebufferTexture");
        _checkNamedFramebufferStatus = (delegate* unmanaged[Cdecl]<uint, uint, uint>)Proc("glCheckNamedFramebufferStatus");
        _bindFramebuffer = (delegate* unmanaged[Cdecl]<uint, uint, void>)Proc("glBindFramebuffer");
        _readPixels = (delegate* unmanaged[Cdecl]<int, int, int, int, uint, uint, void*, void>)Proc("glReadPixels");
    }

    private static nint Proc(string name)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name);
        Array.Resize(ref bytes, bytes.Length + 1);
        fixed (byte* p = bytes) return SDL_GL_GetProcAddress(p);
    }

    /// <summary>
    /// Whether this driver can import external memory at all.
    ///
    /// <para>Both the entry points and the advertised extension are checked. A driver may
    /// resolve a name it does not implement — the address comes from the ICD rather than
    /// from a capability table — so the extension string is what makes the answer
    /// trustworthy.</para>
    /// </summary>
    public bool HasMemoryObject =>
        _createMemoryObjects is not null && _textureStorageMem2D is not null
        && _createTextures is not null && _createFramebuffers is not null
        && HasExtension("GL_EXT_memory_object");

    public bool HasPlatformImport => OperatingSystem.IsWindows()
        ? _importMemoryWin32Handle is not null && HasExtension("GL_EXT_memory_object_win32")
        : _importMemoryFd is not null && HasExtension("GL_EXT_memory_object_fd");

    public string Renderer => ReadString(Renderer2);

    private bool HasExtension(string name)
    {
        if (_getIntegerv is null || _getStringi is null) return false;
        int count = 0;
        _getIntegerv(NumExtensions, &count);
        for (uint i = 0; i < count; i++)
        {
            var p = _getStringi(Extensions, i);
            if (p is null) continue;
            if (string.Equals(Marshal.PtrToStringUTF8((nint)p), name, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private string ReadString(uint name)
    {
        if (_getString is null) return "";
        var p = _getString(name);
        return p is null ? "" : Marshal.PtrToStringUTF8((nint)p) ?? "";
    }

    /// <summary>The GPU this context is on, as the same sixteen bytes Vulkan reports in
    /// <c>VkPhysicalDeviceIDProperties</c>. Null when the driver will not say.</summary>
    public byte[]? DeviceUuid()
    {
        if (_getUnsignedBytev is null) return null;
        var uuid = new byte[16];
        fixed (byte* p = uuid) _getUnsignedBytev(DeviceUuidExt, p);
        return uuid;
    }

    public uint CreateMemoryObject()
    {
        uint memory = 0;
        _createMemoryObjects(1, &memory);
        return memory;
    }

    public void DeleteMemoryObject(uint memory)
    {
        if (memory == 0 || _deleteMemoryObjects is null) return;
        _deleteMemoryObjects(1, &memory);
    }

    /// <summary>
    /// Tells GL the memory about to be imported is a dedicated allocation, which the Vulkan
    /// side always makes. Must come after the object is created and before the import.
    ///
    /// <para>The spec requires it and leaves the result undefined without it, and on
    /// NVIDIA-Linux undefined meant a texture that read as black once the image passed a
    /// size — 2560x1440 drew, 2560x1600 and a 3840x1746 film did not, with no GL error and a
    /// complete framebuffer. Small images and Windows happened to work without it.</para>
    /// </summary>
    public void MarkDedicated(uint memory)
    {
        if (_memoryObjectParameteriv is null) return;
        int yes = 1;
        _memoryObjectParameteriv(memory, DedicatedMemoryObject, &yes);
    }

    public void ImportWin32(uint memory, ulong size, nint handle) =>
        _importMemoryWin32Handle(memory, size, HandleTypeOpaqueWin32, (void*)handle);

    public void ImportFd(uint memory, ulong size, int fd) =>
        _importMemoryFd(memory, size, HandleTypeOpaqueFd, fd);

    public uint CreateTexture()
    {
        uint texture = 0;
        _createTextures(Texture2D, 1, &texture);
        return texture;
    }

    public void DeleteTexture(uint texture)
    {
        if (texture == 0) return;
        _deleteTextures(1, &texture);
    }

    /// <summary>Must be called before the storage is declared, and must say OPTIMAL to match
    /// the tiling the Vulkan image was created with.</summary>
    public void SetOptimalTiling(uint texture) =>
        _textureParameteri(texture, TextureTiling, (int)OptimalTiling);

    public void TextureStorageMem(uint texture, uint internalFormat, int width, int height, uint memory) =>
        _textureStorageMem2D(texture, 1, internalFormat, width, height, memory, 0);

    public uint CreateFramebuffer()
    {
        uint fbo = 0;
        _createFramebuffers(1, &fbo);
        return fbo;
    }

    public void DeleteFramebuffer(uint fbo)
    {
        if (fbo == 0) return;
        _deleteFramebuffers(1, &fbo);
    }

    public void AttachColour(uint fbo, uint texture) =>
        _namedFramebufferTexture(fbo, ColorAttachment0, texture, 0);

    public bool FramebufferComplete(uint fbo) =>
        _checkNamedFramebufferStatus(fbo, Framebuffer) == FramebufferComplete2;

    /// <summary>
    /// The brightest thing in the framebuffer, read from the GL side.
    ///
    /// <para>Deliberately GL and not Vulkan. The same question asked through Vulkan needs the
    /// image's current layout, which Skia owns and moves, and an attempt to guess it read a
    /// uniform zero out of an image that plainly had a picture in it. GL has no layouts: it
    /// reads what the framebuffer holds.</para>
    ///
    /// <para>This is what mpv wrote, before the renderer has touched it — which is the only
    /// measurement that separates "the producer never made HDR" from "the compositor lost
    /// it".</para>
    /// </summary>
    public (float Max, long Over1, int Levels, int Reversals) ReadRange(uint fbo, int width, int height)
    {
        if (_readPixels is null || _bindFramebuffer is null) return (0f, 0, 0, 0);

        var pixels = new Half[(long)width * height * 4];
        _bindFramebuffer(ReadFramebuffer, fbo);
        fixed (Half* p = pixels) _readPixels(0, 0, width, height, Rgba, HalfFloat, p);
        _bindFramebuffer(ReadFramebuffer, 0);

        float max = 0f;
        long over1 = 0;
        for (long i = 0; i < pixels.LongLength; i += 4)
            for (int c = 0; c < 3; c++)
            {
                float v = (float)pixels[i + c];
                if (float.IsNaN(v)) continue;
                if (v > max) max = v;
                if (v > 1.0001f) over1++;
            }

        // How many distinct levels one row holds, and how often it changes direction. On a
        // smooth ramp those two separate a clean path from a dithered one: dithering to eight
        // bits caps the levels near 256 and reverses direction constantly.
        var seen = new HashSet<float>();
        int reversals = 0, lastSign = 0;
        long rowStart = (long)(height / 2) * width * 4;
        for (int x = 0; x < width; x++)
        {
            float v = (float)pixels[rowStart + (long)x * 4];
            seen.Add(v);
            if (x == 0) continue;
            float delta = v - (float)pixels[rowStart + (long)(x - 1) * 4];
            int sign = delta > 1e-6f ? 1 : delta < -1e-6f ? -1 : 0;
            if (sign != 0 && lastSign != 0 && sign != lastSign) reversals++;
            if (sign != 0) lastSign = sign;
        }

        return (max, over1, seen.Count, reversals);
    }

    public void Finish() => _finish();

    public uint Error() => _getError();
}
