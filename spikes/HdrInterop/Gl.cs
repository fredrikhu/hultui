using System.Runtime.InteropServices;
using System.Text;
using static SDL.SDL3;

namespace HdrInterop;

/// <summary>
/// The handful of GL entry points this spike needs, resolved through SDL. Hand-rolled
/// rather than pulled from a binding package for the reason the rest of the repo gives:
/// thirty functions do not justify a dependency, and the extension ones
/// (<c>GL_EXT_memory_object</c>) are the whole point here, so they are worth seeing
/// written out.
/// </summary>
internal static unsafe class Gl
{
    public const uint RGBA16F = 0x881A;
    public const uint FRAMEBUFFER = 0x8D40;
    public const uint COLOR_ATTACHMENT0 = 0x8CE0;
    public const uint FRAMEBUFFER_COMPLETE = 0x8CD5;
    public const uint COLOR_BUFFER_BIT = 0x4000;
    public const uint EXTENSIONS = 0x1F03;
    public const uint NUM_EXTENSIONS = 0x821D;
    public const uint RENDERER = 0x1F01;
    public const uint TEXTURE_2D = 0x0DE1;

    // GL_EXT_memory_object / _win32
    public const uint HANDLE_TYPE_OPAQUE_WIN32_EXT = 0x9587;
    public const uint HANDLE_TYPE_OPAQUE_FD_EXT = 0x9586;
    public const uint TEXTURE_TILING_EXT = 0x9580;
    public const uint OPTIMAL_TILING_EXT = 0x9584;
    public const uint DEVICE_UUID_EXT = 0x9597;
    public const uint UUID_SIZE_EXT = 16;

    private static delegate* unmanaged[Cdecl]<uint, byte*> _getString;
    private static delegate* unmanaged[Cdecl]<uint, uint, byte*> _getStringi;
    private static delegate* unmanaged[Cdecl]<uint, int*, void> _getIntegerv;
    private static delegate* unmanaged[Cdecl]<uint, uint, byte*, void> _getUnsignedBytei_v;
    private static delegate* unmanaged[Cdecl]<uint, byte*, void> _getUnsignedBytev;
    private static delegate* unmanaged[Cdecl]<uint> _getError;
    private static delegate* unmanaged[Cdecl]<void> _finish;

    private static delegate* unmanaged[Cdecl]<int, uint*, void> _createMemoryObjects;
    private static delegate* unmanaged[Cdecl]<uint, ulong, uint, void*, void> _importMemoryWin32Handle;
    private static delegate* unmanaged[Cdecl]<uint, ulong, uint, int, void> _importMemoryFd;
    private static delegate* unmanaged[Cdecl]<uint, int, uint, int, int, uint, ulong, void> _textureStorageMem2D;
    private static delegate* unmanaged[Cdecl]<uint, int, uint*, void> _createTextures;
    private static delegate* unmanaged[Cdecl]<uint, uint, int, void> _textureParameteri;

    private static delegate* unmanaged[Cdecl]<int, uint*, void> _createFramebuffers;
    private static delegate* unmanaged[Cdecl]<uint, uint, uint, int, void> _namedFramebufferTexture;
    private static delegate* unmanaged[Cdecl]<uint, uint, uint> _checkNamedFramebufferStatus;
    private static delegate* unmanaged[Cdecl]<uint, uint, void> _bindFramebuffer;
    private static delegate* unmanaged[Cdecl]<float, float, float, float, void> _clearColor;
    private static delegate* unmanaged[Cdecl]<uint, void> _clear;
    private static delegate* unmanaged[Cdecl]<int, int, int, int, void> _viewport;

    public static void Load()
    {
        _getString = (delegate* unmanaged[Cdecl]<uint, byte*>)Proc("glGetString");
        _getStringi = (delegate* unmanaged[Cdecl]<uint, uint, byte*>)Proc("glGetStringi");
        _getIntegerv = (delegate* unmanaged[Cdecl]<uint, int*, void>)Proc("glGetIntegerv");
        _getUnsignedBytei_v = (delegate* unmanaged[Cdecl]<uint, uint, byte*, void>)Proc("glGetUnsignedBytei_vEXT", optional: true);
        _getUnsignedBytev = (delegate* unmanaged[Cdecl]<uint, byte*, void>)Proc("glGetUnsignedBytevEXT", optional: true);
        _getError = (delegate* unmanaged[Cdecl]<uint>)Proc("glGetError");
        _finish = (delegate* unmanaged[Cdecl]<void>)Proc("glFinish");

        _createMemoryObjects = (delegate* unmanaged[Cdecl]<int, uint*, void>)Proc("glCreateMemoryObjectsEXT", optional: true);
        _importMemoryWin32Handle = (delegate* unmanaged[Cdecl]<uint, ulong, uint, void*, void>)Proc("glImportMemoryWin32HandleEXT", optional: true);
        _importMemoryFd = (delegate* unmanaged[Cdecl]<uint, ulong, uint, int, void>)Proc("glImportMemoryFdEXT", optional: true);
        _textureStorageMem2D = (delegate* unmanaged[Cdecl]<uint, int, uint, int, int, uint, ulong, void>)Proc("glTextureStorageMem2DEXT", optional: true);
        _createTextures = (delegate* unmanaged[Cdecl]<uint, int, uint*, void>)Proc("glCreateTextures");
        _textureParameteri = (delegate* unmanaged[Cdecl]<uint, uint, int, void>)Proc("glTextureParameteri");

        _createFramebuffers = (delegate* unmanaged[Cdecl]<int, uint*, void>)Proc("glCreateFramebuffers");
        _namedFramebufferTexture = (delegate* unmanaged[Cdecl]<uint, uint, uint, int, void>)Proc("glNamedFramebufferTexture");
        _checkNamedFramebufferStatus = (delegate* unmanaged[Cdecl]<uint, uint, uint>)Proc("glCheckNamedFramebufferStatus");
        _bindFramebuffer = (delegate* unmanaged[Cdecl]<uint, uint, void>)Proc("glBindFramebuffer");
        _clearColor = (delegate* unmanaged[Cdecl]<float, float, float, float, void>)Proc("glClearColor");
        _clear = (delegate* unmanaged[Cdecl]<uint, void>)Proc("glClear");
        _viewport = (delegate* unmanaged[Cdecl]<int, int, int, int, void>)Proc("glViewport");
    }

    public static bool HasMemoryObject => _createMemoryObjects is not null && _textureStorageMem2D is not null;
    public static bool HasWin32Import => _importMemoryWin32Handle is not null;
    public static bool HasFdImport => _importMemoryFd is not null;

    private static nint Proc(string name, bool optional = false)
    {
        var address = SDL_GL_GetProcAddress(name);
        if (address == 0 && !optional)
            throw new InvalidOperationException($"GL entry point missing: {name}");
        return address;
    }

    public static string GetString(uint name)
    {
        var p = _getString(name);
        return p is null ? "" : Marshal.PtrToStringUTF8((nint)p) ?? "";
    }

    public static IReadOnlyList<string> Extensions()
    {
        int count = 0;
        _getIntegerv(NUM_EXTENSIONS, &count);
        var list = new List<string>(count);
        for (uint i = 0; i < count; i++)
        {
            var p = _getStringi(EXTENSIONS, i);
            if (p is not null) list.Add(Marshal.PtrToStringUTF8((nint)p) ?? "");
        }
        return list;
    }

    /// <summary>The GPU this GL context is on, as the same 16 bytes Vulkan reports in
    /// VkPhysicalDeviceIDProperties. On a laptop with two GPUs these differ, and importing
    /// across them fails in ways that look like driver bugs.</summary>
    public static byte[]? DeviceUuid()
    {
        if (_getUnsignedBytev is null) return null;
        var uuid = new byte[UUID_SIZE_EXT];
        fixed (byte* p = uuid) _getUnsignedBytev(DEVICE_UUID_EXT, p);
        return uuid;
    }

    public static uint CreateMemoryObject()
    {
        uint memory = 0;
        _createMemoryObjects(1, &memory);
        return memory;
    }

    public static void ImportWin32(uint memory, ulong size, nint handle) =>
        _importMemoryWin32Handle(memory, size, HANDLE_TYPE_OPAQUE_WIN32_EXT, (void*)handle);

    public static void ImportFd(uint memory, ulong size, int fd) =>
        _importMemoryFd(memory, size, HANDLE_TYPE_OPAQUE_FD_EXT, fd);

    public static uint CreateTexture()
    {
        uint texture = 0;
        _createTextures(TEXTURE_2D, 1, &texture);
        return texture;
    }

    public static void SetOptimalTiling(uint texture) =>
        _textureParameteri(texture, TEXTURE_TILING_EXT, (int)OPTIMAL_TILING_EXT);

    public static void TextureStorageMem(uint texture, int width, int height, uint memory, ulong offset) =>
        _textureStorageMem2D(texture, 1, RGBA16F, width, height, memory, offset);

    public static uint CreateFramebuffer()
    {
        uint fbo = 0;
        _createFramebuffers(1, &fbo);
        return fbo;
    }

    public static void AttachColour(uint fbo, uint texture) =>
        _namedFramebufferTexture(fbo, COLOR_ATTACHMENT0, texture, 0);

    public static bool FramebufferComplete(uint fbo) =>
        _checkNamedFramebufferStatus(fbo, FRAMEBUFFER) == FRAMEBUFFER_COMPLETE;

    public static void BindFramebuffer(uint fbo) => _bindFramebuffer(FRAMEBUFFER, fbo);

    public static void ClearTo(float r, float g, float b, float a)
    {
        _clearColor(r, g, b, a);
        _clear(COLOR_BUFFER_BIT);
    }

    public static void Viewport(int w, int h) => _viewport(0, 0, w, h);

    public static void Finish() => _finish();

    public static uint Error() => _getError();

    public static string Describe(byte[]? uuid) =>
        uuid is null ? "(unavailable)" : Convert.ToHexString(uuid);
}
