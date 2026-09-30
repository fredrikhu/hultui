using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Events;

namespace HultUI.Platform.SDL3;

/// <summary>
/// The window's OLE drop target on Windows, in place of SDL's.
/// </summary>
/// <remarks>
/// <para><b>Why not SDL's.</b> SDL's drop target reads what is being dragged only when it is
/// dropped; while the pointer moves it reports a position and nothing else. So no element
/// could know during a hover whether it was files, text or neither, and nothing could hand the
/// drag on to something that reads it itself — a web page, whose engine wants the real
/// <c>IDataObject</c> on every step of the drag, including drags that start inside the page.
/// This one reads the files and text as the drag enters, raises the same input events SDL's
/// did (<see cref="InputEvent.DropBeginInput"/> with the data object, then files, text and
/// positions), and answers each step with what the window's elements said
/// (<see cref="DropFeedback"/>), so the pointer shows whether the drop would be taken.</para>
/// <para><b>How it replaces SDL's.</b> A window has one drop target. SDL registers its own
/// when it creates the window; this revokes it and registers itself, per window, on the
/// window's thread. SDL's object stays SDL's: it releases it when the window goes, and its
/// own revoke then finds nothing to revoke. A thread that cannot host OLE keeps SDL's.</para>
/// <para><b>COM by hand.</b> Built-in COM interop is off under NativeAOT, so the object is a
/// block of native memory with a vtable of unmanaged function pointers, the way
/// <see cref="WindowsFileDrag"/> avoids implementing anything at all. OLE calls it on the
/// window's own thread, from inside its message loop.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WindowsDropTarget : IDisposable
{
    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-c000-000000000046");
    private static readonly Guid IID_IDropTarget = new("00000122-0000-0000-c000-000000000046");

    private const uint CF_UNICODETEXT = 13, CF_HDROP = 15;
    private const uint TYMED_HGLOBAL = 1, DVASPECT_CONTENT = 1;
    private const uint MK_SHIFT = 0x4, MK_CONTROL = 0x8, MK_ALT = 0x20;
    private const uint DROPEFFECT_NONE = 0, DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2, DROPEFFECT_LINK = 4;
    private const int S_OK = 0, E_NOINTERFACE = unchecked((int)0x80004002);

    private static readonly nint* Vtable = CreateVtable();
    [ThreadStatic] private static bool? _available;

    private readonly nint _hwnd;
    private readonly Action<InputEvent> _raise;
    private readonly Func<float> _pixelsPerUnit;
    private GCHandle _self;
    private Instance* _native;
    private nint _data;

    [StructLayout(LayoutKind.Sequential)]
    private struct Instance
    {
        public nint Vtable;
        public int RefCount;
        public nint Handle;
    }

    /// <summary>
    /// True when this thread can host an OLE drop target — an STA thread, as a .NET app gets
    /// with <c>[STAThread]</c> on its entry point. Otherwise SDL's target stays. Cached per
    /// thread: the apartment is fixed for the life of a thread, and OLE stays initialised on it.
    /// </summary>
    public static bool IsAvailable => _available ??= OleInitialize(0) >= 0;

    private WindowsDropTarget(nint hwnd, Action<InputEvent> raise, Func<float> pixelsPerUnit)
    {
        _hwnd = hwnd;
        _raise = raise;
        _pixelsPerUnit = pixelsPerUnit;
        _self = GCHandle.Alloc(this);
        _native = (Instance*)NativeMemory.AllocZeroed((nuint)sizeof(Instance));
        _native->Vtable = (nint)Vtable;
        _native->RefCount = 1; // ours, given back in Dispose
        _native->Handle = GCHandle.ToIntPtr(_self);
    }

    /// <summary>Registers a drop target for <paramref name="hwnd"/>; null when OLE cannot be
    /// used on this thread or the window already has one.</summary>
    /// <param name="hwnd">The window to take drops for.</param>
    /// <param name="raise">Where the drag's input events go, synchronously.</param>
    /// <param name="pixelsPerUnit">Device pixels per layout unit, read on each event.</param>
    public static WindowsDropTarget? Install(nint hwnd, Action<InputEvent> raise, Func<float> pixelsPerUnit)
    {
        if (hwnd == 0 || !IsAvailable) return null;
        var target = new WindowsDropTarget(hwnd, raise, pixelsPerUnit);
        RevokeDragDrop(hwnd); // SDL's
        if (RegisterDragDrop(hwnd, (nint)target._native) >= 0) return target;
        target.Dispose();
        return null;
    }

    /// <summary>The registered object, as OLE sees it: an <c>IDropTarget*</c>. For tests that
    /// play a drag without a mouse.</summary>
    internal nint Pointer => (nint)_native;

    public void Dispose()
    {
        if (_native == null) return;
        RevokeDragDrop(_hwnd);
        ReleaseData();
        var native = _native;
        _native = null;
        Release(native);
    }

    // ──── The drag ────

    private int DragEnter(nint data, uint keys, POINTL at, uint* effect)
    {
        ReleaseData();
        _data = data;
        if (data != 0) Marshal.AddRef(data);

        _raise(new InputEvent.DropBeginInput(data));
        foreach (var path in Files(data)) _raise(new InputEvent.DropFileInput(path));
        if (Text(data) is { } text) _raise(new InputEvent.DropTextInput(text));
        *effect = Position(keys, at, *effect);
        return S_OK;
    }

    private int DragOver(uint keys, POINTL at, uint* effect)
    {
        *effect = Position(keys, at, *effect);
        return S_OK;
    }

    private int DragLeave()
    {
        _raise(new InputEvent.DropCancelInput());
        ReleaseData();
        return S_OK;
    }

    private int Drop(nint data, uint keys, POINTL at, uint* effect)
    {
        uint allowed = *effect;
        Position(keys, at, allowed);
        var feedback = new DropFeedback();
        _raise(new InputEvent.DropCompleteInput(Modifiers(keys), feedback));
        *effect = feedback.Accepted ? Choose(keys, allowed) : DROPEFFECT_NONE;
        ReleaseData();
        return S_OK;
    }

    /// <summary>Raises the position and answers with what the element under it said.</summary>
    private uint Position(uint keys, POINTL at, uint allowed)
    {
        var point = new POINT { X = at.X, Y = at.Y };
        ScreenToClient(_hwnd, ref point);
        float scale = _pixelsPerUnit();
        if (scale <= 0) scale = 1;
        var feedback = new DropFeedback();
        _raise(new InputEvent.DropPositionInput(point.X / scale, point.Y / scale, Modifiers(keys), feedback));
        return feedback.Accepted ? Choose(keys, allowed) : DROPEFFECT_NONE;
    }

    /// <summary>Explorer's convention: Ctrl copies, Shift moves, both link; otherwise copy.
    /// Whatever the source does not allow falls back to what it does.</summary>
    private static uint Choose(uint keys, uint allowed)
    {
        bool ctrl = (keys & MK_CONTROL) != 0, shift = (keys & MK_SHIFT) != 0;
        uint wanted = ctrl && shift ? DROPEFFECT_LINK : shift ? DROPEFFECT_MOVE : DROPEFFECT_COPY;
        if ((allowed & wanted) != 0) return wanted;
        foreach (var effect in new[] { DROPEFFECT_COPY, DROPEFFECT_MOVE, DROPEFFECT_LINK })
            if ((allowed & effect) != 0) return effect;
        return DROPEFFECT_NONE;
    }

    private static KeyModifiers Modifiers(uint keys) =>
        ((keys & MK_SHIFT) != 0 ? KeyModifiers.Shift : 0)
        | ((keys & MK_CONTROL) != 0 ? KeyModifiers.Ctrl : 0)
        | ((keys & MK_ALT) != 0 ? KeyModifiers.Alt : 0);

    private void ReleaseData()
    {
        if (_data == 0) return;
        Marshal.Release(_data);
        _data = 0;
    }

    // ──── Reading the data object ────

    private static List<string> Files(nint data)
    {
        var files = new List<string>();
        if (!TryGet(data, CF_HDROP, out var medium)) return files;
        try
        {
            uint count = DragQueryFileW(medium.Handle, 0xFFFFFFFF, null, 0);
            for (uint i = 0; i < count; i++)
            {
                uint length = DragQueryFileW(medium.Handle, i, null, 0);
                if (length == 0) continue;
                var buffer = new char[length + 1];
                fixed (char* p = buffer)
                    if (DragQueryFileW(medium.Handle, i, p, length + 1) > 0)
                        files.Add(new string(p, 0, (int)length));
            }
        }
        finally { ReleaseStgMedium(&medium); }
        return files;
    }

    private static string? Text(nint data)
    {
        if (!TryGet(data, CF_UNICODETEXT, out var medium)) return null;
        try
        {
            var locked = GlobalLock(medium.Handle);
            if (locked == 0) return null;
            try { return new string((char*)locked); }
            finally { GlobalUnlock(medium.Handle); }
        }
        finally { ReleaseStgMedium(&medium); }
    }

    /// <summary><c>IDataObject::GetData</c>, vtable slot 3, for an HGLOBAL of <paramref name="format"/>.</summary>
    private static bool TryGet(nint data, uint format, out STGMEDIUM medium)
    {
        medium = default;
        if (data == 0) return false;
        var request = new FORMATETC { Format = (ushort)format, Aspect = DVASPECT_CONTENT, Index = -1, Tymed = TYMED_HGLOBAL };
        STGMEDIUM result;
        int hr = ((delegate* unmanaged[Stdcall]<nint, FORMATETC*, STGMEDIUM*, int>)(*(nint**)data)[3])(data, &request, &result);
        if (hr < 0 || result.Tymed != TYMED_HGLOBAL || result.Handle == 0)
        {
            if (hr >= 0) ReleaseStgMedium(&result);
            return false;
        }
        medium = result;
        return true;
    }

    // ──── COM plumbing ────

    private static nint* CreateVtable()
    {
        var vtable = (nint*)NativeMemory.Alloc((nuint)(7 * sizeof(nint)));
        vtable[0] = (nint)(delegate* unmanaged[Stdcall]<Instance*, Guid*, nint*, int>)&QueryInterfaceThunk;
        vtable[1] = (nint)(delegate* unmanaged[Stdcall]<Instance*, uint>)&AddRefThunk;
        vtable[2] = (nint)(delegate* unmanaged[Stdcall]<Instance*, uint>)&ReleaseThunk;
        vtable[3] = (nint)(delegate* unmanaged[Stdcall]<Instance*, nint, uint, POINTL, uint*, int>)&DragEnterThunk;
        vtable[4] = (nint)(delegate* unmanaged[Stdcall]<Instance*, uint, POINTL, uint*, int>)&DragOverThunk;
        vtable[5] = (nint)(delegate* unmanaged[Stdcall]<Instance*, int>)&DragLeaveThunk;
        vtable[6] = (nint)(delegate* unmanaged[Stdcall]<Instance*, nint, uint, POINTL, uint*, int>)&DropThunk;
        return vtable;
    }

    private static WindowsDropTarget? Target(Instance* self) =>
        self->Handle == 0 ? null : GCHandle.FromIntPtr(self->Handle).Target as WindowsDropTarget;

    private static uint Release(Instance* self)
    {
        int count = Interlocked.Decrement(ref self->RefCount);
        if (count == 0)
        {
            if (self->Handle != 0) GCHandle.FromIntPtr(self->Handle).Free();
            self->Handle = 0;
            NativeMemory.Free(self);
        }
        return (uint)Math.Max(count, 0);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterfaceThunk(Instance* self, Guid* iid, nint* result)
    {
        if (*iid == IID_IUnknown || *iid == IID_IDropTarget)
        {
            Interlocked.Increment(ref self->RefCount);
            *result = (nint)self;
            return S_OK;
        }
        *result = 0;
        return E_NOINTERFACE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRefThunk(Instance* self) => (uint)Interlocked.Increment(ref self->RefCount);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint ReleaseThunk(Instance* self) => Release(self);

    // Nothing may escape into OLE's drag loop: a failure is a drop nobody takes.

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DragEnterThunk(Instance* self, nint data, uint keys, POINTL at, uint* effect)
    {
        try { return Target(self)?.DragEnter(data, keys, at, effect) ?? S_OK; }
        catch { *effect = DROPEFFECT_NONE; return S_OK; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DragOverThunk(Instance* self, uint keys, POINTL at, uint* effect)
    {
        try { return Target(self)?.DragOver(keys, at, effect) ?? S_OK; }
        catch { *effect = DROPEFFECT_NONE; return S_OK; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DragLeaveThunk(Instance* self)
    {
        try { return Target(self)?.DragLeave() ?? S_OK; }
        catch { return S_OK; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int DropThunk(Instance* self, nint data, uint keys, POINTL at, uint* effect)
    {
        try { return Target(self)?.Drop(data, keys, at, effect) ?? S_OK; }
        catch { *effect = DROPEFFECT_NONE; return S_OK; }
    }

    // ──── Win32 ────

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FORMATETC
    {
        public ushort Format;
        public nint TargetDevice;
        public uint Aspect;
        public int Index;
        public uint Tymed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STGMEDIUM
    {
        public uint Tymed;
        public nint Handle;
        public nint UnkForRelease;
    }

    [LibraryImport("ole32.dll")]
    private static partial int OleInitialize(nint reserved);

    [LibraryImport("ole32.dll")]
    private static partial int RegisterDragDrop(nint hwnd, nint target);

    [LibraryImport("ole32.dll")]
    private static partial int RevokeDragDrop(nint hwnd);

    [LibraryImport("ole32.dll")]
    private static partial void ReleaseStgMedium(STGMEDIUM* medium);

    [LibraryImport("shell32.dll")]
    private static partial uint DragQueryFileW(nint drop, uint index, char* file, uint length);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ScreenToClient(nint hwnd, ref POINT point);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);
}
