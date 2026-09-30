using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace HultUI.WebView.WebView2.Interop;

/// <summary>
/// A COM object with one method, <c>Invoke</c>, that calls a managed delegate: what WebView2
/// wants for every completion and event handler, and what WinRT wants for a
/// <c>TypedEventHandler</c>. Built by hand — a native block holding a vtable pointer, a
/// reference count and a GC handle to this object — the way the UIA bridge builds its
/// providers, because NativeAOT has no built-in COM to do it.
/// </summary>
/// <remarks>
/// <para>Two shapes cover every handler used: <c>Invoke(HRESULT, T*)</c> for completions
/// and <c>Invoke(sender*, args*)</c> for events. The object answers <c>QueryInterface</c>
/// for IUnknown, its handler's IID and IAgileObject — the capture's frame handler is called
/// from a thread pool, and WinRT checks for agility before it will.</para>
/// <para>The creator holds one reference and gives it up with <see cref="Release"/>; the
/// native side's own references keep the delegate alive after that for as long as it needs
/// it. A delegate that throws must not unwind through native frames, so the exception is
/// rethrown on the UI thread instead.</para>
/// </remarks>
internal sealed unsafe class ComHandler
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Block
    {
        public nint Vtbl;
        public int RefCount;
        public nint Handle;
    }

    private static readonly nint ResultVtbl = BuildVtbl(result: true);
    private static readonly nint EventVtbl = BuildVtbl(result: false);

    private readonly Guid _iid;
    private readonly Action<int, nint>? _onResult;
    private readonly Action<nint, nint>? _onEvent;
    private nint _pointer;

    private ComHandler(Guid iid, Action<int, nint>? onResult, Action<nint, nint>? onEvent)
    {
        _iid = iid;
        _onResult = onResult;
        _onEvent = onEvent;
        var block = (Block*)NativeMemory.Alloc((nuint)sizeof(Block));
        block->Vtbl = onResult is not null ? ResultVtbl : EventVtbl;
        block->RefCount = 1;
        block->Handle = GCHandle.ToIntPtr(GCHandle.Alloc(this));
        _pointer = (nint)block;
    }

    /// <summary>A completion handler: <c>Invoke(HRESULT errorCode, T* result)</c>.</summary>
    public static ComHandler ForResult(Guid iid, Action<int, nint> onResult) => new(iid, onResult, null);

    /// <summary>An event handler: <c>Invoke(sender*, args*)</c>.</summary>
    public static ComHandler ForEvent(Guid iid, Action<nint, nint> onEvent) => new(iid, null, onEvent);

    /// <summary>The COM pointer to hand over. Valid until <see cref="Release"/>.</summary>
    public nint Pointer => _pointer;

    /// <summary>Gives up the creator's reference.</summary>
    public void Release()
    {
        var p = Interlocked.Exchange(ref _pointer, 0);
        if (p != 0) ReleaseBlock(p);
    }

    private static nint BuildVtbl(bool result)
    {
        var vtbl = (nint*)NativeMemory.Alloc((nuint)(4 * sizeof(nint)));
        vtbl[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryInterface;
        vtbl[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef;
        vtbl[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&ReleaseThunk;
        vtbl[3] = result
            ? (nint)(delegate* unmanaged[Stdcall]<nint, int, nint, int>)&InvokeResult
            : (nint)(delegate* unmanaged[Stdcall]<nint, nint, nint, int>)&InvokeEvent;
        return (nint)vtbl;
    }

    private static ComHandler Target(nint self) =>
        (ComHandler)GCHandle.FromIntPtr(((Block*)self)->Handle).Target!;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(nint self, Guid* iid, nint* result)
    {
        if (*iid == Com.IID_IUnknown || *iid == Com.IID_IAgileObject || *iid == Target(self)._iid)
        {
            Interlocked.Increment(ref ((Block*)self)->RefCount);
            *result = self;
            return Com.S_OK;
        }
        *result = 0;
        return Com.E_NOINTERFACE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(nint self) => (uint)Interlocked.Increment(ref ((Block*)self)->RefCount);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint ReleaseThunk(nint self) => ReleaseBlock(self);

    private static uint ReleaseBlock(nint self)
    {
        var block = (Block*)self;
        int count = Interlocked.Decrement(ref block->RefCount);
        if (count == 0)
        {
            GCHandle.FromIntPtr(block->Handle).Free();
            NativeMemory.Free(block);
        }
        return (uint)count;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int InvokeResult(nint self, int errorCode, nint value)
    {
        try { Target(self)._onResult!(errorCode, value); }
        catch (Exception ex) { Rethrow(ex); }
        return Com.S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int InvokeEvent(nint self, nint sender, nint args)
    {
        try { Target(self)._onEvent!(sender, args); }
        catch (Exception ex) { Rethrow(ex); }
        return Com.S_OK;
    }

    /// <summary>An app handler's exception is still the app's: surface it on the UI thread,
    /// where it would have been thrown had the call not come through native code.</summary>
    private static void Rethrow(Exception ex)
    {
        var captured = ExceptionDispatchInfo.Capture(ex);
        Application.InvokeOnMainThread(captured.Throw);
    }
}
