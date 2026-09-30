namespace HultUI.WebView.WebView2.Interop;

/// <summary>
/// The GL entry points the GPU path uses, resolved through the surface: a few core ones to
/// blit or upload, and <c>WGL_NV_DX_interop2</c>, which lets GL read a D3D11 texture in place.
/// Function pointers rather than delegates, so nothing needs marshalling stubs under NativeAOT.
/// On a Vulkan surface the resolver is its hidden GL context's, a WGL context on the same GPU,
/// where the interop works the same.
/// </summary>
internal sealed unsafe class GLInterop
{
    public const uint GL_TEXTURE_2D = 0x0DE1, GL_READ_FRAMEBUFFER = 0x8CA8, GL_DRAW_FRAMEBUFFER = 0x8CA9,
        GL_COLOR_ATTACHMENT0 = 0x8CE0, GL_COLOR_BUFFER_BIT = 0x4000, GL_NEAREST = 0x2600,
        GL_BGRA = 0x80E1, GL_UNSIGNED_BYTE = 0x1401, GL_UNPACK_ROW_LENGTH = 0x0CF2;
    public const uint WGL_ACCESS_READ_ONLY_NV = 0;

    public readonly delegate* unmanaged[Stdcall]<int, uint*, void> GenTextures, GenFramebuffers;
    public readonly delegate* unmanaged[Stdcall]<int, uint*, void> DeleteTextures, DeleteFramebuffers;
    public readonly delegate* unmanaged[Stdcall]<uint, uint, void> BindTexture, BindFramebuffer;
    public readonly delegate* unmanaged[Stdcall]<uint, uint, uint, uint, int, void> FramebufferTexture2D;
    public readonly delegate* unmanaged[Stdcall]<int, int, int, int, int, int, int, int, uint, uint, void> BlitFramebuffer;
    public readonly delegate* unmanaged[Stdcall]<uint, int, int, int, int, int, uint, uint, void*, void> TexSubImage2D;
    public readonly delegate* unmanaged[Stdcall]<uint, int, void> PixelStorei;

    // WGL_NV_DX_interop(2). Null when the driver does not offer it.
    public readonly delegate* unmanaged[Stdcall]<void*, nint> DXOpenDevice;
    public readonly delegate* unmanaged[Stdcall]<nint, int> DXCloseDevice;
    public readonly delegate* unmanaged[Stdcall]<nint, void*, uint, uint, uint, nint> DXRegisterObject;
    public readonly delegate* unmanaged[Stdcall]<nint, nint, int> DXUnregisterObject;
    public readonly delegate* unmanaged[Stdcall]<nint, int, nint*, int> DXLockObjects, DXUnlockObjects;

    public GLInterop(Func<string, nint> resolve)
    {
        nint Required(string name) => resolve(name) is var p and not 0 ? p : throw new InvalidOperationException($"GL entry point {name} not found.");

        GenTextures = (delegate* unmanaged[Stdcall]<int, uint*, void>)Required("glGenTextures");
        GenFramebuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)Required("glGenFramebuffers");
        DeleteTextures = (delegate* unmanaged[Stdcall]<int, uint*, void>)Required("glDeleteTextures");
        DeleteFramebuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)Required("glDeleteFramebuffers");
        BindTexture = (delegate* unmanaged[Stdcall]<uint, uint, void>)Required("glBindTexture");
        BindFramebuffer = (delegate* unmanaged[Stdcall]<uint, uint, void>)Required("glBindFramebuffer");
        FramebufferTexture2D = (delegate* unmanaged[Stdcall]<uint, uint, uint, uint, int, void>)Required("glFramebufferTexture2D");
        BlitFramebuffer = (delegate* unmanaged[Stdcall]<int, int, int, int, int, int, int, int, uint, uint, void>)Required("glBlitFramebuffer");
        TexSubImage2D = (delegate* unmanaged[Stdcall]<uint, int, int, int, int, int, uint, uint, void*, void>)Required("glTexSubImage2D");
        PixelStorei = (delegate* unmanaged[Stdcall]<uint, int, void>)Required("glPixelStorei");

        DXOpenDevice = (delegate* unmanaged[Stdcall]<void*, nint>)resolve("wglDXOpenDeviceNV");
        DXCloseDevice = (delegate* unmanaged[Stdcall]<nint, int>)resolve("wglDXCloseDeviceNV");
        DXRegisterObject = (delegate* unmanaged[Stdcall]<nint, void*, uint, uint, uint, nint>)resolve("wglDXRegisterObjectNV");
        DXUnregisterObject = (delegate* unmanaged[Stdcall]<nint, nint, int>)resolve("wglDXUnregisterObjectNV");
        DXLockObjects = (delegate* unmanaged[Stdcall]<nint, int, nint*, int>)resolve("wglDXLockObjectsNV");
        DXUnlockObjects = (delegate* unmanaged[Stdcall]<nint, int, nint*, int>)resolve("wglDXUnlockObjectsNV");
    }

    /// <summary>Whether every WGL_NV_DX_interop entry point resolved.</summary>
    public bool HasDxInterop =>
        DXOpenDevice != null && DXCloseDevice != null && DXRegisterObject != null &&
        DXUnregisterObject != null && DXLockObjects != null && DXUnlockObjects != null;
}
