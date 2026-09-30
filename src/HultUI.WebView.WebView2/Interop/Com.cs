using System.Runtime.InteropServices;

namespace HultUI.WebView.WebView2.Interop;

/// <summary>
/// Raw vtable calls. Every COM and WinRT method used here returns an HRESULT and takes
/// its outputs by pointer, so a handful of shapes cover them; slots come from
/// <c>ComSlots.g.cs</c>, generated from the headers. On x64 and ARM64 there is one calling
/// convention, which these function pointers name for the x86 case.
/// </summary>
internal static unsafe class Com
{
    public const int S_OK = 0;
    public const int E_NOINTERFACE = unchecked((int)0x80004002);
    public const int E_FAIL = unchecked((int)0x80004005);

    public static readonly Guid IID_IUnknown = new("00000000-0000-0000-c000-000000000046");
    public static readonly Guid IID_IAgileObject = new("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90");
    /// <summary>windows.graphics.directx.direct3d11.interop.h: one method, GetInterface, at slot 3.</summary>
    public static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");

    private static nint Fn(nint obj, int slot) => (*(nint**)obj)[slot];

    public static void Check(int hr, string what)
    {
        if (hr < 0) throw new COMException($"{what} failed (0x{hr:X8}).", hr);
    }

    public static nint QueryInterface(nint obj, Guid iid)
    {
        Check(Marshal.QueryInterface(obj, in iid, out var result), "QueryInterface " + iid);
        return result;
    }

    public static nint TryQueryInterface(nint obj, Guid iid) =>
        obj != 0 && Marshal.QueryInterface(obj, in iid, out var result) >= 0 ? result : 0;

    public static void Release(ref nint obj)
    {
        if (obj == 0) return;
        Marshal.Release(obj);
        obj = 0;
    }

    public static void AddRef(nint obj)
    {
        if (obj != 0) Marshal.AddRef(obj);
    }

    // ──── Shapes ────

    public static int Call(nint obj, int slot) =>
        ((delegate* unmanaged[Stdcall]<nint, int>)Fn(obj, slot))(obj);

    public static int Call(nint obj, int slot, nint a) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, int>)Fn(obj, slot))(obj, a);

    public static int Call(nint obj, int slot, int a) =>
        ((delegate* unmanaged[Stdcall]<nint, int, int>)Fn(obj, slot))(obj, a);

    public static int Call(nint obj, int slot, double a) =>
        ((delegate* unmanaged[Stdcall]<nint, double, int>)Fn(obj, slot))(obj, a);

    public static int Call(nint obj, int slot, nint a, nint b) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Fn(obj, slot))(obj, a, b);

    public static int Call<T>(nint obj, int slot, T a) where T : unmanaged =>
        ((delegate* unmanaged[Stdcall]<nint, T, int>)Fn(obj, slot))(obj, a);

    public static nint GetPtr(nint obj, int slot, string what)
    {
        nint result;
        Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Fn(obj, slot))(obj, &result), what);
        return result;
    }

    public static nint GetPtr(nint obj, int slot, nint argument, string what)
    {
        nint result;
        Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Fn(obj, slot))(obj, argument, &result), what);
        return result;
    }

    public static T Get<T>(nint obj, int slot, string what) where T : unmanaged
    {
        T result;
        Check(((delegate* unmanaged[Stdcall]<nint, T*, int>)Fn(obj, slot))(obj, &result), what);
        return result;
    }

    public static bool GetBool(nint obj, int slot, string what) => Get<int>(obj, slot, what) != 0;

    /// <summary>A <c>LPWSTR*</c> output: the callee allocated it with CoTaskMemAlloc.</summary>
    public static string? GetString(nint obj, int slot, string what)
    {
        char* value;
        Check(((delegate* unmanaged[Stdcall]<nint, char**, int>)Fn(obj, slot))(obj, &value), what);
        if (value is null) return null;
        try { return new string(value); }
        finally { Marshal.FreeCoTaskMem((nint)value); }
    }

    public static int CallString(nint obj, int slot, string value)
    {
        fixed (char* p = value)
            return ((delegate* unmanaged[Stdcall]<nint, char*, int>)Fn(obj, slot))(obj, p);
    }

    public static int CallString(nint obj, int slot, string value, nint handler)
    {
        fixed (char* p = value)
            return ((delegate* unmanaged[Stdcall]<nint, char*, nint, int>)Fn(obj, slot))(obj, p, handler);
    }

    /// <summary><c>add_X(handler, EventRegistrationToken*)</c>.</summary>
    public static long AddHandler(nint obj, int slot, nint handler, string what)
    {
        long token;
        Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Fn(obj, slot))(obj, handler, &token), what);
        return token;
    }

    /// <summary><c>remove_X(EventRegistrationToken)</c>. Best effort: it runs on teardown.</summary>
    public static void RemoveHandler(nint obj, int slot, long token)
    {
        if (obj != 0) ((delegate* unmanaged[Stdcall]<nint, long, int>)Fn(obj, slot))(obj, token);
    }

    // ──── D3D11: the few methods that return void ────

    public static void CallVoid(nint obj, int slot, nint a) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, void>)Fn(obj, slot))(obj, a);

    public static void CallVoid(nint obj, int slot, nint a, uint b) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, void>)Fn(obj, slot))(obj, a, b);

    public static void CopySubresourceRegion(nint context, nint dst, nint src, Native.D3D11_BOX box) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, uint, nint, uint, Native.D3D11_BOX*, void>)
            Fn(context, ID3D11DeviceContext.CopySubresourceRegion))(context, dst, 0, 0, 0, 0, src, 0, &box);

    public static int Map(nint context, nint resource, out Native.D3D11_MAPPED_SUBRESOURCE mapped)
    {
        Native.D3D11_MAPPED_SUBRESOURCE m;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint, uint, int, uint, Native.D3D11_MAPPED_SUBRESOURCE*, int>)
            Fn(context, ID3D11DeviceContext.Map))(context, resource, 0, Native.D3D11_MAP_READ, 0, &m);
        mapped = m;
        return hr;
    }

    // ──── WinRT strings ────

    /// <summary>An HSTRING for the length of a call.</summary>
    public readonly ref struct HString
    {
        public readonly nint Handle;

        public HString(string value)
        {
            nint h;
            fixed (char* p = value)
                Check(Native.WindowsCreateString(p, (uint)value.Length, &h), "WindowsCreateString");
            Handle = h;
        }

        public void Dispose() => Native.WindowsDeleteString(Handle);
    }

    public static nint ActivateInstance(string runtimeClass)
    {
        using var name = new HString(runtimeClass);
        nint instance;
        Check(Native.RoActivateInstance(name.Handle, &instance), "RoActivateInstance " + runtimeClass);
        return instance;
    }

    public static nint GetActivationFactory(string runtimeClass, Guid iid)
    {
        using var name = new HString(runtimeClass);
        nint factory;
        Check(Native.RoGetActivationFactory(name.Handle, &iid, &factory), "RoGetActivationFactory " + runtimeClass);
        return factory;
    }
}
