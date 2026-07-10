using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HultUI.Platform.Accessibility.Windows;

// ── Managed-facing provider interfaces ─────────────────────────────────────
// These mirror the native UIA COM vtables (HRESULT-returning, out-params). The
// provider logic classes implement them; NativeProvider wraps a logic object in
// a real native COM object whose vtable thunks dispatch here. Methods that
// return another provider hand back a raw native IUnknown* (AddRef'd), created
// via NativeProvider.Create.

[SupportedOSPlatform("windows")]
internal interface INativeSimple
{
    int GetProviderOptions(out int pRetVal);
    int GetPatternProvider(int patternId, out IntPtr pPatternProvider);
    int GetPropertyValue(int propertyId, out UiaVariant pRetVal);
    int GetHostRawElementProvider(out IntPtr pRetVal);
}

[SupportedOSPlatform("windows")]
internal interface INativeFragment
{
    int Navigate(int direction, out IntPtr pRetVal);
    int GetRuntimeId(out IntPtr pRetVal);
    int GetBoundingRectangle(out UiaRect pRetVal);
    int GetEmbeddedFragmentRoots(out IntPtr pRetVal);
    int SetFocus();
    int GetFragmentRoot(out IntPtr pRetVal);
}

[SupportedOSPlatform("windows")]
internal interface INativeFragmentRoot
{
    int ElementProviderFromPoint(double x, double y, out IntPtr pRetVal);
    int GetFocus(out IntPtr pRetVal);
}

[SupportedOSPlatform("windows")]
internal interface INativeInvoke { int Invoke(); }

[SupportedOSPlatform("windows")]
internal interface INativeToggle { int Toggle(); int GetToggleState(out int pRetVal); }

[SupportedOSPlatform("windows")]
internal interface INativeValue { int SetValue(IntPtr pwsz); int GetValue(out IntPtr pRetVal); int GetIsReadOnly(out int pRetVal); }

[SupportedOSPlatform("windows")]
internal interface INativeSelectionItem
{
    int Select(); int AddToSelection(); int RemoveFromSelection();
    int GetIsSelected(out int pRetVal); int GetSelectionContainer(out IntPtr pRetVal);
}

[SupportedOSPlatform("windows")]
internal interface INativeExpandCollapse { int Expand(); int Collapse(); int GetExpandCollapseState(out int pRetVal); }

[SupportedOSPlatform("windows")]
internal interface INativeScroll
{
    int Scroll(int horizontalAmount, int verticalAmount);
    int SetScrollPercent(double horizontalPercent, double verticalPercent);
    int GetHorizontalScrollPercent(out double pRetVal);
    int GetVerticalScrollPercent(out double pRetVal);
    int GetHorizontalViewSize(out double pRetVal);
    int GetVerticalViewSize(out double pRetVal);
    int GetHorizontallyScrollable(out int pRetVal);
    int GetVerticallyScrollable(out int pRetVal);
}

/// <summary>
/// Wraps a managed provider logic object in a genuine native COM object —
/// unmanaged memory holding one tear-off per supported interface, each with a
/// vtable of <see cref="UnmanagedCallersOnlyAttribute"/> thunks.
/// </summary>
/// <remarks>
/// Why not <c>ComWrappers</c>: on released .NET 10, handing a
/// StrategyBasedComWrappers CCW to <c>UiaReturnRawElementProvider</c>
/// access-violates inside UIAutomationCore before it even calls the provider.
/// A hand-built native object (what native UIA providers are) is marshalled and
/// called correctly. This keeps the assembly AOT-friendly and free of a
/// Windows-Desktop/WPF dependency.
///
/// Memory layout of one object:
/// <code>
///   [ 0] long   refCount
///   [ 8] IntPtr gcHandle          -> the managed logic object
///   [16] int    tearoffCount
///   [24] tearoff[i] { vtbl, back } (16 bytes each; back -> object base)
///   [..] Guid    iid[i]           (16 bytes each; parallel to tearoffs)
/// </code>
/// The interface pointer handed to COM is the address of a tear-off; every
/// thunk recovers the object base from tear-off.back. All interfaces share one
/// ref count and one IUnknown identity (tear-off[0]).
/// </remarks>
[SupportedOSPlatform("windows")]
internal static unsafe class NativeProvider
{
    private const int HdrRefCount = 0;
    private const int HdrGcHandle = 8;
    private const int HdrCount = 16;
    private const int TearoffsStart = 24;
    private const int TearoffSize = 16; // { vtbl(8), back(8) }

    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    // Marker interface. Claiming it tells COM/UIA the object is free-threaded, so
    // it is marshalled with the lightweight free-threaded marshaler instead of a
    // per-object standard proxy/stub — an order of magnitude faster to navigate.
    private static readonly Guid IID_IAgileObject = new("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90");
    // IServiceProvider: UIA QIs every provider for this (to reach the MSAA
    // bridge / extra services). Implementing it — even to say "no service" —
    // lets UIA cache the answer instead of re-asking cross-process constantly.
    private static readonly Guid IID_IServiceProvider = new("6d5140c1-7436-11ce-8034-00aa006009fa");
    private static readonly Guid IID_Simple = new("d6dd68d1-86fd-4332-8666-9abedea2d24c");
    private static readonly Guid IID_Fragment = new("f7063da8-8359-439c-9297-bbc5299a7d87");
    private static readonly Guid IID_FragmentRoot = new("620ce2a5-ab8f-40a9-86cb-de3c75599b58");
    private static readonly Guid IID_Invoke = new("54fcb24b-e18e-47a2-b4d3-eccbe77599a2");
    private static readonly Guid IID_Toggle = new("56d00bd0-c4f4-433c-a836-1a52a57e0892");
    private static readonly Guid IID_Value = new("c7935180-6fb3-4201-b174-7df73adbf64a");
    private static readonly Guid IID_SelectionItem = new("2acad808-b2d4-452d-a407-91ff1ad167b2");
    private static readonly Guid IID_ExpandCollapse = new("d847d3a5-cab0-4a98-8c32-ecb45c59ad24");
    private static readonly Guid IID_Scroll = new("b38b8077-1fc3-42a5-8cae-d40c2215055a");

    // Prebuilt shared vtables (one per interface), allocated once.
    private static readonly IntPtr VtSimple;
    private static readonly IntPtr VtFragment;
    private static readonly IntPtr VtFragmentRoot;
    private static readonly IntPtr VtInvoke;
    private static readonly IntPtr VtToggle;
    private static readonly IntPtr VtValue;
    private static readonly IntPtr VtSelectionItem;
    private static readonly IntPtr VtExpandCollapse;
    private static readonly IntPtr VtScroll;
    private static readonly IntPtr VtServiceProvider;

    static NativeProvider()
    {
        VtServiceProvider = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, Guid*, Guid*, IntPtr*, int>)&SP_QueryService);

        VtSimple = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, int*, int>)&Simple_GetProviderOptions,
            (IntPtr)(delegate* unmanaged<IntPtr, int, IntPtr*, int>)&Simple_GetPatternProvider,
            (IntPtr)(delegate* unmanaged<IntPtr, int, void*, int>)&Simple_GetPropertyValue,
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&Simple_GetHostRawElementProvider);

        VtFragment = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, int, IntPtr*, int>)&Fragment_Navigate,
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&Fragment_GetRuntimeId,
            (IntPtr)(delegate* unmanaged<IntPtr, void*, int>)&Fragment_GetBoundingRectangle,
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&Fragment_GetEmbeddedFragmentRoots,
            (IntPtr)(delegate* unmanaged<IntPtr, int>)&Fragment_SetFocus,
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&Fragment_GetFragmentRoot);

        VtFragmentRoot = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, double, double, IntPtr*, int>)&Root_ElementProviderFromPoint,
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&Root_GetFocus);

        VtInvoke = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, int>)&Invoke_Invoke);

        VtToggle = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, int>)&Toggle_Toggle,
            (IntPtr)(delegate* unmanaged<IntPtr, int*, int>)&Toggle_GetToggleState);

        VtValue = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int>)&Value_SetValue,
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&Value_GetValue,
            (IntPtr)(delegate* unmanaged<IntPtr, int*, int>)&Value_GetIsReadOnly);

        VtSelectionItem = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, int>)&Sel_Select,
            (IntPtr)(delegate* unmanaged<IntPtr, int>)&Sel_AddToSelection,
            (IntPtr)(delegate* unmanaged<IntPtr, int>)&Sel_RemoveFromSelection,
            (IntPtr)(delegate* unmanaged<IntPtr, int*, int>)&Sel_GetIsSelected,
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&Sel_GetSelectionContainer);

        VtExpandCollapse = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, int>)&EC_Expand,
            (IntPtr)(delegate* unmanaged<IntPtr, int>)&EC_Collapse,
            (IntPtr)(delegate* unmanaged<IntPtr, int*, int>)&EC_GetState);

        VtScroll = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, int, int, int>)&Scr_Scroll,
            (IntPtr)(delegate* unmanaged<IntPtr, double, double, int>)&Scr_SetScrollPercent,
            (IntPtr)(delegate* unmanaged<IntPtr, double*, int>)&Scr_GetHPercent,
            (IntPtr)(delegate* unmanaged<IntPtr, double*, int>)&Scr_GetVPercent,
            (IntPtr)(delegate* unmanaged<IntPtr, double*, int>)&Scr_GetHViewSize,
            (IntPtr)(delegate* unmanaged<IntPtr, double*, int>)&Scr_GetVViewSize,
            (IntPtr)(delegate* unmanaged<IntPtr, int*, int>)&Scr_GetHScrollable,
            (IntPtr)(delegate* unmanaged<IntPtr, int*, int>)&Scr_GetVScrollable);
    }

    private static IntPtr BuildVtable(params IntPtr[] methods)
    {
        // slots: QueryInterface, AddRef, Release, then the interface methods.
        int n = 3 + methods.Length;
        var v = (IntPtr*)NativeMemory.Alloc((nuint)n, (nuint)sizeof(IntPtr));
        v[0] = (IntPtr)(delegate* unmanaged<IntPtr, Guid*, IntPtr*, int>)&QueryInterface;
        v[1] = (IntPtr)(delegate* unmanaged<IntPtr, uint>)&AddRef;
        v[2] = (IntPtr)(delegate* unmanaged<IntPtr, uint>)&Release;
        for (int i = 0; i < methods.Length; i++) v[3 + i] = methods[i];
        return (IntPtr)v;
    }

    /// <summary>Which interface pointer a factory call should hand back.</summary>
    /// <remarks>
    /// A COM method must return a pointer whose vtable matches its <em>declared</em>
    /// return type — e.g. <c>Navigate</c> returns <c>IRawElementProviderFragment**</c>,
    /// so it must return the Fragment tear-off, not the primary (Simple) one.
    /// Returning the wrong tear-off makes the client call the wrong vtable.
    /// </remarks>
    public enum As { Primary, Simple, Fragment, FragmentRoot }

    /// <summary>
    /// Creates a native COM object wrapping <paramref name="logic"/>, exposing a
    /// tear-off for every provider interface it implements. Returns the primary
    /// (tear-off[0]) IUnknown pointer with a reference count of 1 — the caller
    /// owns that reference. Use <see cref="Tearoff"/> to obtain the pointer for a
    /// specific interface (all tear-offs share one ref count and IUnknown
    /// identity). Callers that cache the object hold this reference for the
    /// object's lifetime and hand out AddRef'd tear-offs.
    /// </summary>
    public static IntPtr Create(object logic)
    {
        Span<IntPtr> vt = stackalloc IntPtr[10];
        Span<Guid> iids = stackalloc Guid[10];
        int n = 0;
        // Order matters: tear-off[0] is the IUnknown identity. Simple first for
        // fragments; the single pattern interface first for pattern providers.
        if (logic is INativeSimple) { vt[n] = VtSimple; iids[n] = IID_Simple; n++; }
        if (logic is INativeFragment) { vt[n] = VtFragment; iids[n] = IID_Fragment; n++; }
        if (logic is INativeFragmentRoot) { vt[n] = VtFragmentRoot; iids[n] = IID_FragmentRoot; n++; }
        if (logic is INativeInvoke) { vt[n] = VtInvoke; iids[n] = IID_Invoke; n++; }
        if (logic is INativeToggle) { vt[n] = VtToggle; iids[n] = IID_Toggle; n++; }
        if (logic is INativeValue) { vt[n] = VtValue; iids[n] = IID_Value; n++; }
        if (logic is INativeSelectionItem) { vt[n] = VtSelectionItem; iids[n] = IID_SelectionItem; n++; }
        if (logic is INativeExpandCollapse) { vt[n] = VtExpandCollapse; iids[n] = IID_ExpandCollapse; n++; }
        if (logic is INativeScroll) { vt[n] = VtScroll; iids[n] = IID_Scroll; n++; }
        if (n == 0) return IntPtr.Zero;
        // Every provider also exposes IServiceProvider (see IID note) so UIA
        // caches the "no extra service" answer instead of re-QIing constantly.
        vt[n] = VtServiceProvider; iids[n] = IID_IServiceProvider; n++;

        nuint size = (nuint)(TearoffsStart + n * TearoffSize + n * sizeof(Guid));
        byte* obj = (byte*)NativeMemory.AllocZeroed(size, 1);
        *(long*)(obj + HdrRefCount) = 1;
        *(IntPtr*)(obj + HdrGcHandle) = GCHandle.ToIntPtr(GCHandle.Alloc(logic));
        *(int*)(obj + HdrCount) = n;

        Guid* iidBase = (Guid*)(obj + TearoffsStart + n * TearoffSize);
        for (int i = 0; i < n; i++)
        {
            IntPtr* tear = (IntPtr*)(obj + TearoffsStart + i * TearoffSize);
            tear[0] = vt[i];          // vtable
            tear[1] = (IntPtr)obj;    // back-pointer to object base
            iidBase[i] = iids[i];
        }
        return (IntPtr)(obj + TearoffsStart); // primary = tear-off[0]
    }

    /// <summary>
    /// Given any tear-off pointer of an object, returns the tear-off pointer for
    /// interface <paramref name="which"/> (falling back to the primary), and
    /// AddRefs the shared object so the returned pointer carries a reference the
    /// caller must Release. A COM method must return the pointer whose vtable
    /// matches its declared return type.
    /// </summary>
    public static IntPtr Tearoff(IntPtr anyTearoff, As which)
    {
        if (anyTearoff == IntPtr.Zero) return IntPtr.Zero;
        byte* b = Base(anyTearoff);
        AddRefBase(b);
        if (which == As.Primary) return (IntPtr)(b + TearoffsStart);

        Guid want = which switch
        {
            As.Simple => IID_Simple,
            As.Fragment => IID_Fragment,
            As.FragmentRoot => IID_FragmentRoot,
            _ => IID_IUnknown,
        };
        int count = *(int*)(b + HdrCount);
        Guid* iidBase = (Guid*)(b + TearoffsStart + count * TearoffSize);
        for (int i = 0; i < count; i++)
            if (iidBase[i] == want) return (IntPtr)(b + TearoffsStart + i * TearoffSize);
        return (IntPtr)(b + TearoffsStart); // primary fallback
    }

    // ── helpers to recover state inside a thunk ──
    private static byte* Base(IntPtr pThis) => (byte*)*((IntPtr*)pThis + 1);

    private static object? Target(IntPtr pThis)
    {
        IntPtr gch = *(IntPtr*)(Base(pThis) + HdrGcHandle);
        return gch == IntPtr.Zero ? null : GCHandle.FromIntPtr(gch).Target;
    }

    // ── IUnknown thunks (shared by every tear-off) ──

    [UnmanagedCallersOnly]
    private static int QueryInterface(IntPtr pThis, Guid* riid, IntPtr* ppv)
    {
        try
        {
            byte* b = Base(pThis);
            Guid want = *riid;
            // IUnknown and IAgileObject both resolve to the primary tear-off:
            // IAgileObject is a marker (IUnknown-shaped), and claiming it makes
            // UIA marshal us as free-threaded (much cheaper to navigate).
            if (want == IID_IUnknown || want == IID_IAgileObject)
            {
                *ppv = (IntPtr)(b + TearoffsStart); AddRefBase(b); return 0;
            }

            int count = *(int*)(b + HdrCount);
            Guid* iidBase = (Guid*)(b + TearoffsStart + count * TearoffSize);
            for (int i = 0; i < count; i++)
            {
                if (iidBase[i] == want)
                {
                    *ppv = (IntPtr)(b + TearoffsStart + i * TearoffSize);
                    AddRefBase(b);
                    return 0;
                }
            }
            *ppv = IntPtr.Zero;
            return unchecked((int)0x80004002); // E_NOINTERFACE
        }
        catch { *ppv = IntPtr.Zero; return unchecked((int)0x80004002); }
    }

    private static void AddRefBase(byte* b) => Interlocked.Increment(ref *(long*)(b + HdrRefCount));

    [UnmanagedCallersOnly]
    private static uint AddRef(IntPtr pThis)
    {
        byte* b = Base(pThis);
        return (uint)Interlocked.Increment(ref *(long*)(b + HdrRefCount));
    }

    [UnmanagedCallersOnly]
    private static uint Release(IntPtr pThis)
    {
        byte* b = Base(pThis);
        long c = Interlocked.Decrement(ref *(long*)(b + HdrRefCount));
        if (c == 0)
        {
            IntPtr gch = *(IntPtr*)(b + HdrGcHandle);
            if (gch != IntPtr.Zero) GCHandle.FromIntPtr(gch).Free();
            NativeMemory.Free(b);
        }
        return (uint)c;
    }

    // ── IRawElementProviderSimple ──
    [UnmanagedCallersOnly] private static int Simple_GetProviderOptions(IntPtr t, int* p) { try { int hr = ((INativeSimple)Target(t)!).GetProviderOptions(out int v); *p = v; return hr; } catch { *p = 0; return 0; } }
    [UnmanagedCallersOnly] private static int Simple_GetPatternProvider(IntPtr t, int id, IntPtr* p) { try { return ((INativeSimple)Target(t)!).GetPatternProvider(id, out *p); } catch { *p = IntPtr.Zero; return 0; } }
    [UnmanagedCallersOnly] private static int Simple_GetPropertyValue(IntPtr t, int id, void* p) { try { int hr = ((INativeSimple)Target(t)!).GetPropertyValue(id, out var v); *(UiaVariant*)p = v; return hr; } catch { *(UiaVariant*)p = UiaVariant.Empty; return 0; } }
    [UnmanagedCallersOnly] private static int Simple_GetHostRawElementProvider(IntPtr t, IntPtr* p) { try { return ((INativeSimple)Target(t)!).GetHostRawElementProvider(out *p); } catch { *p = IntPtr.Zero; return 0; } }

    // ── IRawElementProviderFragment ──
    [UnmanagedCallersOnly] private static int Fragment_Navigate(IntPtr t, int dir, IntPtr* p) { try { return ((INativeFragment)Target(t)!).Navigate(dir, out *p); } catch { *p = IntPtr.Zero; return 0; } }
    [UnmanagedCallersOnly] private static int Fragment_GetRuntimeId(IntPtr t, IntPtr* p) { try { return ((INativeFragment)Target(t)!).GetRuntimeId(out *p); } catch { *p = IntPtr.Zero; return 0; } }
    [UnmanagedCallersOnly] private static int Fragment_GetBoundingRectangle(IntPtr t, void* p) { try { int hr = ((INativeFragment)Target(t)!).GetBoundingRectangle(out var r); *(UiaRect*)p = r; return hr; } catch { *(UiaRect*)p = default; return 0; } }
    [UnmanagedCallersOnly] private static int Fragment_GetEmbeddedFragmentRoots(IntPtr t, IntPtr* p) { try { return ((INativeFragment)Target(t)!).GetEmbeddedFragmentRoots(out *p); } catch { *p = IntPtr.Zero; return 0; } }
    [UnmanagedCallersOnly] private static int Fragment_SetFocus(IntPtr t) { try { return ((INativeFragment)Target(t)!).SetFocus(); } catch { return 0; } }
    [UnmanagedCallersOnly] private static int Fragment_GetFragmentRoot(IntPtr t, IntPtr* p) { try { return ((INativeFragment)Target(t)!).GetFragmentRoot(out *p); } catch { *p = IntPtr.Zero; return 0; } }

    // ── IRawElementProviderFragmentRoot ──
    [UnmanagedCallersOnly] private static int Root_ElementProviderFromPoint(IntPtr t, double x, double y, IntPtr* p) { try { return ((INativeFragmentRoot)Target(t)!).ElementProviderFromPoint(x, y, out *p); } catch { *p = IntPtr.Zero; return 0; } }
    [UnmanagedCallersOnly] private static int Root_GetFocus(IntPtr t, IntPtr* p) { try { return ((INativeFragmentRoot)Target(t)!).GetFocus(out *p); } catch { *p = IntPtr.Zero; return 0; } }

    // ── IServiceProvider ──
    [UnmanagedCallersOnly] private static int SP_QueryService(IntPtr t, Guid* service, Guid* riid, IntPtr* ppv)
    {
        if (ppv != null) *ppv = IntPtr.Zero;
        return unchecked((int)0x80004002); // E_NOINTERFACE — we expose no extra services
    }

    // ── pattern providers ──
    [UnmanagedCallersOnly] private static int Invoke_Invoke(IntPtr t) { try { return ((INativeInvoke)Target(t)!).Invoke(); } catch { return 0; } }
    [UnmanagedCallersOnly] private static int Toggle_Toggle(IntPtr t) { try { return ((INativeToggle)Target(t)!).Toggle(); } catch { return 0; } }
    [UnmanagedCallersOnly] private static int Toggle_GetToggleState(IntPtr t, int* p) { try { int hr = ((INativeToggle)Target(t)!).GetToggleState(out int v); *p = v; return hr; } catch { *p = 0; return 0; } }
    [UnmanagedCallersOnly] private static int Value_SetValue(IntPtr t, IntPtr s) { try { return ((INativeValue)Target(t)!).SetValue(s); } catch { return 0; } }
    [UnmanagedCallersOnly] private static int Value_GetValue(IntPtr t, IntPtr* p) { try { return ((INativeValue)Target(t)!).GetValue(out *p); } catch { *p = IntPtr.Zero; return 0; } }
    [UnmanagedCallersOnly] private static int Value_GetIsReadOnly(IntPtr t, int* p) { try { int hr = ((INativeValue)Target(t)!).GetIsReadOnly(out int v); *p = v; return hr; } catch { *p = 0; return 0; } }
    [UnmanagedCallersOnly] private static int Sel_Select(IntPtr t) { try { return ((INativeSelectionItem)Target(t)!).Select(); } catch { return 0; } }
    [UnmanagedCallersOnly] private static int Sel_AddToSelection(IntPtr t) { try { return ((INativeSelectionItem)Target(t)!).AddToSelection(); } catch { return 0; } }
    [UnmanagedCallersOnly] private static int Sel_RemoveFromSelection(IntPtr t) { try { return ((INativeSelectionItem)Target(t)!).RemoveFromSelection(); } catch { return 0; } }
    [UnmanagedCallersOnly] private static int Sel_GetIsSelected(IntPtr t, int* p) { try { int hr = ((INativeSelectionItem)Target(t)!).GetIsSelected(out int v); *p = v; return hr; } catch { *p = 0; return 0; } }
    [UnmanagedCallersOnly] private static int Sel_GetSelectionContainer(IntPtr t, IntPtr* p) { try { return ((INativeSelectionItem)Target(t)!).GetSelectionContainer(out *p); } catch { *p = IntPtr.Zero; return 0; } }
    [UnmanagedCallersOnly] private static int EC_Expand(IntPtr t) { try { return ((INativeExpandCollapse)Target(t)!).Expand(); } catch { return 0; } }
    [UnmanagedCallersOnly] private static int EC_Collapse(IntPtr t) { try { return ((INativeExpandCollapse)Target(t)!).Collapse(); } catch { return 0; } }
    [UnmanagedCallersOnly] private static int EC_GetState(IntPtr t, int* p) { try { int hr = ((INativeExpandCollapse)Target(t)!).GetExpandCollapseState(out int v); *p = v; return hr; } catch { *p = 3; return 0; } }
    [UnmanagedCallersOnly] private static int Scr_Scroll(IntPtr t, int h, int v) { try { return ((INativeScroll)Target(t)!).Scroll(h, v); } catch { return 0; } }
    [UnmanagedCallersOnly] private static int Scr_SetScrollPercent(IntPtr t, double h, double v) { try { return ((INativeScroll)Target(t)!).SetScrollPercent(h, v); } catch { return 0; } }
    [UnmanagedCallersOnly] private static int Scr_GetHPercent(IntPtr t, double* p) { try { int hr = ((INativeScroll)Target(t)!).GetHorizontalScrollPercent(out double v); *p = v; return hr; } catch { *p = -1; return 0; } }
    [UnmanagedCallersOnly] private static int Scr_GetVPercent(IntPtr t, double* p) { try { int hr = ((INativeScroll)Target(t)!).GetVerticalScrollPercent(out double v); *p = v; return hr; } catch { *p = -1; return 0; } }
    [UnmanagedCallersOnly] private static int Scr_GetHViewSize(IntPtr t, double* p) { try { int hr = ((INativeScroll)Target(t)!).GetHorizontalViewSize(out double v); *p = v; return hr; } catch { *p = 100; return 0; } }
    [UnmanagedCallersOnly] private static int Scr_GetVViewSize(IntPtr t, double* p) { try { int hr = ((INativeScroll)Target(t)!).GetVerticalViewSize(out double v); *p = v; return hr; } catch { *p = 100; return 0; } }
    [UnmanagedCallersOnly] private static int Scr_GetHScrollable(IntPtr t, int* p) { try { int hr = ((INativeScroll)Target(t)!).GetHorizontallyScrollable(out int v); *p = v; return hr; } catch { *p = 0; return 0; } }
    [UnmanagedCallersOnly] private static int Scr_GetVScrollable(IntPtr t, int* p) { try { int hr = ((INativeScroll)Target(t)!).GetVerticallyScrollable(out int v); *p = v; return hr; } catch { *p = 0; return 0; } }
}
