using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Platform;

namespace HultUI.Platform.SDL3;

/// <summary>
/// Starting an OLE file drag that other applications can accept — the outbound half of
/// desktop drag &amp; drop, which SDL does not offer.
/// </summary>
/// <remarks>
/// <para><b>Why no COM interfaces are implemented here.</b> The textbook route is to write
/// your own <c>IDataObject</c> and <c>IDropSource</c> and hand them to <c>DoDragDrop</c>.
/// Implementing a COM interface from managed code means a callable wrapper, and built-in COM
/// interop is off under NativeAOT (which these apps publish with) — so it would mean
/// hand-rolled vtables of unmanaged function pointers for two interfaces and their whole
/// lifetimes. The shell already ships both objects: <c>SHCreateDataObject</c> makes an empty
/// data object we only ever <i>call</i>, and <c>SHDoDragDrop</c> with a null drop source
/// supplies the default one — the same one Explorer uses, drag image included. That leaves
/// two outgoing vtable calls (SetData, Release) and no callbacks at all.</para>
///
/// <para><b>OLE initialisation is one-way.</b> It is done lazily, once, and never undone.
/// Balancing it per drag would be correct in isolation but risks tearing the apartment's OLE
/// state out from under whatever else in the process registered with it (SDL's own drop
/// target, a shell context menu mid-flight); the process exit does that cleanup properly.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowsFileDrag
{
    private const uint CF_HDROP = 15;
    private const uint TYMED_HGLOBAL = 1;
    private const uint DVASPECT_CONTENT = 1;
    private const uint GMEM_MOVEABLE = 0x0002;

    // DoDragDrop's two success codes. Anything else (including a cancel) means nothing landed.
    private const int DRAGDROP_S_DROP = 0x00040100;

    private static Guid _iidDataObject = new("0000010e-0000-0000-C000-000000000046");

    private static bool? _oleReady;
    private static uint _preferredEffectFormat;

    /// <summary>
    /// True when this thread can host an OLE drag at all. It cannot when the apartment is
    /// MTA — <c>OleInitialize</c> answers RPC_E_CHANGED_MODE there and <c>DoDragDrop</c> is
    /// simply unavailable, which is what a .NET process gets unless its entry point is marked
    /// <c>[STAThread]</c>. Reported rather than discovered on the first drag, so an app can
    /// leave the gesture unarmed instead of offering one that would quietly do nothing.
    /// </summary>
    public static bool IsAvailable => EnsureOle();

    /// <summary>Offer <paramref name="paths"/> to the desktop and block until the drop
    /// resolves. Returns what the target did, or <see cref="DragEffect.None"/>.</summary>
    public static DragEffect Drag(nint hwnd, IReadOnlyList<string> paths, DragEffect allowed)
    {
        if (paths.Count == 0 || allowed == DragEffect.None) return DragEffect.None;
        if (!EnsureOle()) return DragEffect.None;

        nint pdo;
        fixed (Guid* iid = &_iidDataObject)
        {
            if (SHCreateDataObject(0, 0, null, 0, iid, out pdo) < 0 || pdo == 0)
                return DragEffect.None;
        }

        try
        {
            if (!SetFileList(pdo, paths)) return DragEffect.None;
            // Advisory: which effect the target should default to when the user holds no
            // modifier. Without it a drop onto Explorer within the same volume defaults to
            // Move, which is not what a "drag a copy out of here" gesture should mean.
            SetPreferredEffect(pdo, allowed);

            uint effect = 0;
            int rc = SHDoDragDrop(hwnd, pdo, 0, (uint)allowed, &effect);
            if (rc != DRAGDROP_S_DROP) return DragEffect.None;
            // Mask to the three effect bits: targets also set DROPEFFECT_SCROLL (0x80000000).
            return (DragEffect)(effect & 0x7) & allowed;
        }
        finally
        {
            Release(pdo);
        }
    }

    private static bool EnsureOle()
    {
        // Cached either way: a failure is a property of the thread's apartment, which is fixed
        // for the life of the process, so retrying it once per frame would be pure waste.
        // S_OK (0) and S_FALSE (1) both mean the apartment is usable; only a negative HRESULT
        // — in practice RPC_E_CHANGED_MODE on an MTA thread — means it is not.
        return _oleReady ??= OleInitialize(0) >= 0;
    }

    /// <summary>Put the paths into the data object as CF_HDROP: a <c>DROPFILES</c> header
    /// followed by the names as one double-null-terminated UTF-16 block.</summary>
    private static bool SetFileList(nint pdo, IReadOnlyList<string> paths)
    {
        int chars = 1;                                   // the list's own terminating NUL
        foreach (var p in paths) chars += p.Length + 1;

        nuint bytes = (nuint)(sizeof(DROPFILES) + chars * sizeof(char));
        nint hglobal = GlobalAlloc(GMEM_MOVEABLE, bytes);
        if (hglobal == 0) return false;

        bool handedOver = false;
        try
        {
            nint locked = GlobalLock(hglobal);
            if (locked == 0) return false;
            try
            {
                var header = (DROPFILES*)locked;
                *header = default;
                header->pFiles = (uint)sizeof(DROPFILES);   // names start right after the header
                header->fWide = 1;                          // ...and are UTF-16

                var dst = (char*)((byte*)locked + sizeof(DROPFILES));
                foreach (var p in paths)
                {
                    p.AsSpan().CopyTo(new Span<char>(dst, p.Length));
                    dst += p.Length;
                    *dst++ = '\0';
                }
                *dst = '\0';
            }
            finally { GlobalUnlock(hglobal); }

            handedOver = SetData(pdo, CF_HDROP, hglobal);
            return handedOver;
        }
        finally
        {
            // SetData took ownership only if it succeeded (fRelease is honoured on success).
            if (!handedOver) GlobalFree(hglobal);
        }
    }

    /// <summary>Write the "Preferred DropEffect" format — a single DWORD the shell reads to
    /// pick the default action, and what stops a same-volume drop from meaning Move.</summary>
    private static void SetPreferredEffect(nint pdo, DragEffect allowed)
    {
        if (_preferredEffectFormat == 0)
            _preferredEffectFormat = RegisterClipboardFormatW("Preferred DropEffect");
        if (_preferredEffectFormat == 0) return;

        uint preferred = allowed.HasFlag(DragEffect.Copy) ? (uint)DragEffect.Copy : (uint)allowed;
        nint hglobal = GlobalAlloc(GMEM_MOVEABLE, sizeof(uint));
        if (hglobal == 0) return;

        bool handedOver = false;
        try
        {
            nint locked = GlobalLock(hglobal);
            if (locked == 0) return;
            try { *(uint*)locked = preferred; }
            finally { GlobalUnlock(hglobal); }
            handedOver = SetData(pdo, _preferredEffectFormat, hglobal);
        }
        finally
        {
            if (!handedOver) GlobalFree(hglobal);
        }
    }

    /// <summary><c>IDataObject::SetData</c> — vtable slot 7 (three IUnknown methods, then
    /// GetData, GetDataHere, QueryGetData, GetCanonicalFormatEtc, SetData).</summary>
    private static bool SetData(nint pdo, uint format, nint hglobal)
    {
        var fmt = new FORMATETC
        {
            cfFormat = (ushort)format,
            ptd = 0,
            dwAspect = DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED_HGLOBAL,
        };
        var medium = new STGMEDIUM { tymed = TYMED_HGLOBAL, handle = hglobal, pUnkForRelease = 0 };

        var vtable = *(void***)pdo;
        var setData = (delegate* unmanaged[Stdcall]<nint, FORMATETC*, STGMEDIUM*, int, int>)vtable[7];
        return setData(pdo, &fmt, &medium, 1) >= 0;
    }

    private static void Release(nint pUnk)
    {
        if (pUnk == 0) return;
        var vtable = *(void***)pUnk;
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)vtable[2];
        release(pUnk);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DROPFILES
    {
        public uint pFiles;      // byte offset from this header to the first name
        public int PtX, PtY;     // POINT pt — the drop point; the shell fills it in
        public int fNC;
        public int fWide;        // non-zero: the names are UTF-16
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FORMATETC
    {
        public ushort cfFormat;  // CLIPFORMAT
        public nint ptd;         // DVTARGETDEVICE*
        public uint dwAspect;
        public int lindex;
        public uint tymed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STGMEDIUM
    {
        public uint tymed;
        public nint handle;      // the union — hGlobal for TYMED_HGLOBAL
        public nint pUnkForRelease;
    }

    [LibraryImport("ole32.dll")]
    private static partial int OleInitialize(nint pvReserved);

    [LibraryImport("shell32.dll")]
    private static partial int SHCreateDataObject(nint pidlFolder, uint cidl, nint* apidl,
        nint pdtInner, Guid* riid, out nint ppv);

    [LibraryImport("shell32.dll")]
    private static partial int SHDoDragDrop(nint hwnd, nint pdata, nint pdsrc, uint dwEffect, uint* pdwEffect);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string lpszFormat);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalFree(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint hMem);
}
