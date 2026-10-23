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

/// <summary>IRawElementProviderHwndOverride: the provider that stands for a child window.</summary>
[SupportedOSPlatform("windows")]
internal interface INativeHwndOverride
{
    // Returns an AddRef'd IRawElementProviderSimple*, or null for "no override".
    int GetOverrideProviderForHwnd(IntPtr hwnd, out IntPtr pRetVal);
}

/// <summary>Provider logic that stands for one element by its id (a pattern, a text range, a
/// virtual list item): <see cref="Gone"/> once the element left the tree, and every call on it
/// then answers UIA_E_ELEMENTNOTAVAILABLE (<see cref="NativeProvider"/>).</summary>
internal interface IElementBound
{
    bool Gone { get; }
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
internal interface INativeRangeValue
{
    int SetValue(double value);
    int GetValue(out double pRetVal);
    int GetIsReadOnly(out int pRetVal);
    int GetMaximum(out double pRetVal);
    int GetMinimum(out double pRetVal);
    int GetLargeChange(out double pRetVal);
    int GetSmallChange(out double pRetVal);
}

[SupportedOSPlatform("windows")]
internal interface INativeSelection
{
    // GetSelection returns a SAFEARRAY of IRawElementProviderSimple* (VT_UNKNOWN).
    int GetSelection(out IntPtr pRetVal);
    int GetCanSelectMultiple(out int pRetVal);
    int GetIsSelectionRequired(out int pRetVal);
}

[SupportedOSPlatform("windows")]
internal interface INativeItemContainer
{
    // Returns the found item as IRawElementProviderSimple* (AddRef'd), or null.
    // pStartAfter is a provider we previously handed out (or null to search from
    // the start); value is a by-value VARIANT.
    int FindItemByProperty(IntPtr pStartAfter, int propertyId, UiaVariant value, out IntPtr pFound);
}

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

[SupportedOSPlatform("windows")]
internal interface INativeScrollItem { int ScrollIntoView(); }

/// <summary>ITextProvider and ITextProvider2 (whose vtable extends it), as one interface: the
/// text pattern. Ranges come back as AddRef'd ITextRangeProvider* pointers.</summary>
[SupportedOSPlatform("windows")]
internal interface INativeTextProvider
{
    int GetSelection(out IntPtr pRetVal);
    int GetVisibleRanges(out IntPtr pRetVal);
    int RangeFromChild(IntPtr childElement, out IntPtr pRetVal);
    int RangeFromPoint(UiaPoint point, out IntPtr pRetVal);
    int GetDocumentRange(out IntPtr pRetVal);
    int GetSupportedTextSelection(out int pRetVal);
    int RangeFromAnnotation(IntPtr annotationElement, out IntPtr pRetVal);
    int GetCaretRange(out int isActive, out IntPtr pRetVal);
}

/// <summary>ITextRangeProvider. A range handed back by UIA (<c>Compare</c>, the endpoint
/// moves) is one of ours; <see cref="NativeProvider.LogicOf"/> recovers it.</summary>
[SupportedOSPlatform("windows")]
internal interface INativeTextRange
{
    int Clone(out IntPtr pRetVal);
    int Compare(IntPtr range, out int pRetVal);
    int CompareEndpoints(int endpoint, IntPtr targetRange, int targetEndpoint, out int pRetVal);
    int ExpandToEnclosingUnit(int unit);
    int FindAttribute(int attributeId, UiaVariant value, int backward, out IntPtr pRetVal);
    int FindText(IntPtr text, int backward, int ignoreCase, out IntPtr pRetVal);
    int GetAttributeValue(int attributeId, out UiaVariant pRetVal);
    int GetBoundingRectangles(out IntPtr pRetVal);
    int GetEnclosingElement(out IntPtr pRetVal);
    int GetText(int maxLength, out IntPtr pRetVal);
    int Move(int unit, int count, out int pRetVal);
    int MoveEndpointByUnit(int endpoint, int unit, int count, out int pRetVal);
    int MoveEndpointByRange(int endpoint, IntPtr targetRange, int targetEndpoint);
    int Select();
    int AddToSelection();
    int RemoveFromSelection();
    int ScrollIntoView(int alignToTop);
    int GetChildren(out IntPtr pRetVal);
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
    private static readonly Guid IID_HwndOverride = new("1d5df27c-8947-4425-b8d9-79787bb460b8");
    private static readonly Guid IID_Invoke = new("54fcb24b-e18e-47a2-b4d3-eccbe77599a2");
    private static readonly Guid IID_Toggle = new("56d00bd0-c4f4-433c-a836-1a52a57e0892");
    private static readonly Guid IID_Value = new("c7935180-6fb3-4201-b174-7df73adbf64a");
    private static readonly Guid IID_SelectionItem = new("2acad808-b2d4-452d-a407-91ff1ad167b2");
    private static readonly Guid IID_ExpandCollapse = new("d847d3a5-cab0-4a98-8c32-ecb45c59ad24");
    private static readonly Guid IID_Scroll = new("b38b8077-1fc3-42a5-8cae-d40c2215055a");
    private static readonly Guid IID_ScrollItem = new("2360c714-4bf1-4b26-ba65-9b21316127eb");
    private static readonly Guid IID_RangeValue = new("36dc7aef-33e6-4691-afe1-2be7274b3d33");
    private static readonly Guid IID_Selection = new("fb8b03af-3bdf-48d4-bd36-1a65793be168");
    private static readonly Guid IID_ItemContainer = new("e747770b-39ce-4382-ab30-d8fb3f336f24");
    private static readonly Guid IID_TextProvider = new("3589c92c-63f3-4367-99bb-ada653b77cf2");
    private static readonly Guid IID_TextProvider2 = new("0dc5e6ed-3e16-4bf1-8f9a-a979878bc195");
    private static readonly Guid IID_TextRange = new("5347ad7b-c355-46f8-aff5-909033582f63");

    // Prebuilt shared vtables (one per interface), allocated once.
    private static readonly IntPtr VtSimple;
    private static readonly IntPtr VtFragment;
    private static readonly IntPtr VtFragmentRoot;
    private static readonly IntPtr VtHwndOverride;
    private static readonly IntPtr VtInvoke;
    private static readonly IntPtr VtToggle;
    private static readonly IntPtr VtValue;
    private static readonly IntPtr VtSelectionItem;
    private static readonly IntPtr VtExpandCollapse;
    private static readonly IntPtr VtScroll;
    private static readonly IntPtr VtScrollItem;
    private static readonly IntPtr VtRangeValue;
    private static readonly IntPtr VtSelection;
    private static readonly IntPtr VtItemContainer;
    private static readonly IntPtr VtServiceProvider;
    private static readonly IntPtr VtText;
    private static readonly IntPtr VtTextRange;

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

        VtHwndOverride = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)&Override_GetProvider);

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

        VtScrollItem = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, int>)&SI_ScrollIntoView);

        VtRangeValue = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, double, int>)&RV_SetValue,
            (IntPtr)(delegate* unmanaged<IntPtr, double*, int>)&RV_GetValue,
            (IntPtr)(delegate* unmanaged<IntPtr, int*, int>)&RV_GetIsReadOnly,
            (IntPtr)(delegate* unmanaged<IntPtr, double*, int>)&RV_GetMaximum,
            (IntPtr)(delegate* unmanaged<IntPtr, double*, int>)&RV_GetMinimum,
            (IntPtr)(delegate* unmanaged<IntPtr, double*, int>)&RV_GetLargeChange,
            (IntPtr)(delegate* unmanaged<IntPtr, double*, int>)&RV_GetSmallChange);

        VtSelection = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&Sel2_GetSelection,
            (IntPtr)(delegate* unmanaged<IntPtr, int*, int>)&Sel2_GetCanSelectMultiple,
            (IntPtr)(delegate* unmanaged<IntPtr, int*, int>)&Sel2_GetIsSelectionRequired);

        VtItemContainer = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int, UiaVariant, IntPtr*, int>)&IC_FindItemByProperty);

        // ITextProvider's six methods, then ITextProvider2's two: one table for both.
        VtText = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&Text_GetSelection,
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&Text_GetVisibleRanges,
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)&Text_RangeFromChild,
            (IntPtr)(delegate* unmanaged<IntPtr, UiaPoint, IntPtr*, int>)&Text_RangeFromPoint,
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&Text_GetDocumentRange,
            (IntPtr)(delegate* unmanaged<IntPtr, int*, int>)&Text_GetSupportedTextSelection,
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)&Text_RangeFromAnnotation,
            (IntPtr)(delegate* unmanaged<IntPtr, int*, IntPtr*, int>)&Text_GetCaretRange);

        VtTextRange = BuildVtable(
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&TR_Clone,
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int*, int>)&TR_Compare,
            (IntPtr)(delegate* unmanaged<IntPtr, int, IntPtr, int, int*, int>)&TR_CompareEndpoints,
            (IntPtr)(delegate* unmanaged<IntPtr, int, int>)&TR_ExpandToEnclosingUnit,
            (IntPtr)(delegate* unmanaged<IntPtr, int, UiaVariant, int, IntPtr*, int>)&TR_FindAttribute,
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int, int, IntPtr*, int>)&TR_FindText,
            (IntPtr)(delegate* unmanaged<IntPtr, int, void*, int>)&TR_GetAttributeValue,
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&TR_GetBoundingRectangles,
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&TR_GetEnclosingElement,
            (IntPtr)(delegate* unmanaged<IntPtr, int, IntPtr*, int>)&TR_GetText,
            (IntPtr)(delegate* unmanaged<IntPtr, int, int, int*, int>)&TR_Move,
            (IntPtr)(delegate* unmanaged<IntPtr, int, int, int, int*, int>)&TR_MoveEndpointByUnit,
            (IntPtr)(delegate* unmanaged<IntPtr, int, IntPtr, int, int>)&TR_MoveEndpointByRange,
            (IntPtr)(delegate* unmanaged<IntPtr, int>)&TR_Select,
            (IntPtr)(delegate* unmanaged<IntPtr, int>)&TR_AddToSelection,
            (IntPtr)(delegate* unmanaged<IntPtr, int>)&TR_RemoveFromSelection,
            (IntPtr)(delegate* unmanaged<IntPtr, int, int>)&TR_ScrollIntoView,
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int>)&TR_GetChildren);
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
        if (logic is INativeHwndOverride) { vt[n] = VtHwndOverride; iids[n] = IID_HwndOverride; n++; }
        if (logic is INativeInvoke) { vt[n] = VtInvoke; iids[n] = IID_Invoke; n++; }
        if (logic is INativeToggle) { vt[n] = VtToggle; iids[n] = IID_Toggle; n++; }
        if (logic is INativeValue) { vt[n] = VtValue; iids[n] = IID_Value; n++; }
        if (logic is INativeSelectionItem) { vt[n] = VtSelectionItem; iids[n] = IID_SelectionItem; n++; }
        if (logic is INativeExpandCollapse) { vt[n] = VtExpandCollapse; iids[n] = IID_ExpandCollapse; n++; }
        if (logic is INativeScroll) { vt[n] = VtScroll; iids[n] = IID_Scroll; n++; }
        if (logic is INativeScrollItem) { vt[n] = VtScrollItem; iids[n] = IID_ScrollItem; n++; }
        if (logic is INativeRangeValue) { vt[n] = VtRangeValue; iids[n] = IID_RangeValue; n++; }
        if (logic is INativeSelection) { vt[n] = VtSelection; iids[n] = IID_Selection; n++; }
        if (logic is INativeItemContainer) { vt[n] = VtItemContainer; iids[n] = IID_ItemContainer; n++; }
        if (logic is INativeTextProvider)
        {
            vt[n] = VtText; iids[n] = IID_TextProvider; n++;
            vt[n] = VtText; iids[n] = IID_TextProvider2; n++;
        }
        if (logic is INativeTextRange) { vt[n] = VtTextRange; iids[n] = IID_TextRange; n++; }
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

    /// <summary>
    /// Recovers the managed logic object behind one of <em>our</em> tear-off
    /// pointers — used when UIA hands a provider we created back to us (e.g.
    /// <c>ItemContainer.FindItemByProperty</c>'s <c>pStartAfter</c>). Only valid
    /// for pointers this class produced; passing a foreign COM pointer is
    /// undefined. Returns null for <see cref="IntPtr.Zero"/>.
    /// </summary>
    public static object? LogicOf(IntPtr tearoff) => tearoff == IntPtr.Zero ? null : RawTarget(tearoff);

    // ── helpers to recover state inside a thunk ──
    private static byte* Base(IntPtr pThis) => (byte*)*((IntPtr*)pThis + 1);

    private static object? Target(IntPtr pThis)
    {
        var logic = LogicOf(pThis);
        if (logic == Detached || logic is IElementBound { Gone: true }) throw NotAvailable.Instance;
        return logic;
    }

    private static object? RawTarget(IntPtr pThis)
    {
        IntPtr gch = *(IntPtr*)(Base(pThis) + HdrGcHandle);
        return gch == IntPtr.Zero ? null : GCHandle.FromIntPtr(gch).Target;
    }

    /// <summary>What a detached object holds in place of its logic.</summary>
    private static readonly object Detached = new();

    /// <summary>
    /// Cuts the object from its logic: every call on it answers UIA_E_ELEMENTNOTAVAILABLE from
    /// now on, and what the logic held (a fragment's node, through it its whole tree, and the
    /// text it showed) is no longer kept alive by a client that still holds the object. The
    /// handle is repointed, not freed, so a call on another thread meanwhile reads the logic or
    /// the stand-in, never a freed handle.
    /// </summary>
    public static void Detach(IntPtr anyTearoff)
    {
        IntPtr gch = *(IntPtr*)(Base(anyTearoff) + HdrGcHandle);
        if (gch == IntPtr.Zero) return;
        var handle = GCHandle.FromIntPtr(gch);
        handle.Target = Detached;
    }

    /// <summary>The HRESULT a thunk answers for an exception: UIA_E_ELEMENTNOTAVAILABLE for an
    /// element that left the tree, else what it answered before.</summary>
    private static int Hr(Exception e, int otherwise) =>
        e is NotAvailable ? UiaInterop.UIA_E_ELEMENTNOTAVAILABLE : otherwise;

    /// <summary>Thrown by <see cref="Target"/> for a detached object or a gone element; one
    /// instance, as only the thunks see it.</summary>
    private sealed class NotAvailable : Exception
    {
        public static readonly NotAvailable Instance = new();
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
    [UnmanagedCallersOnly] private static int Simple_GetProviderOptions(IntPtr t, int* p) { try { int hr = ((INativeSimple)Target(t)!).GetProviderOptions(out int v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Simple_GetPatternProvider(IntPtr t, int id, IntPtr* p) { try { return ((INativeSimple)Target(t)!).GetPatternProvider(id, out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Simple_GetPropertyValue(IntPtr t, int id, void* p) { try { int hr = ((INativeSimple)Target(t)!).GetPropertyValue(id, out var v); *(UiaVariant*)p = v; return hr; } catch (Exception ex) { *(UiaVariant*)p = UiaVariant.Empty; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Simple_GetHostRawElementProvider(IntPtr t, IntPtr* p) { try { return ((INativeSimple)Target(t)!).GetHostRawElementProvider(out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, 0); } }

    // ── IRawElementProviderFragment ──
    [UnmanagedCallersOnly] private static int Fragment_Navigate(IntPtr t, int dir, IntPtr* p) { try { return ((INativeFragment)Target(t)!).Navigate(dir, out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Fragment_GetRuntimeId(IntPtr t, IntPtr* p) { try { return ((INativeFragment)Target(t)!).GetRuntimeId(out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Fragment_GetBoundingRectangle(IntPtr t, void* p) { try { int hr = ((INativeFragment)Target(t)!).GetBoundingRectangle(out var r); *(UiaRect*)p = r; return hr; } catch (Exception ex) { *(UiaRect*)p = default; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Fragment_GetEmbeddedFragmentRoots(IntPtr t, IntPtr* p) { try { return ((INativeFragment)Target(t)!).GetEmbeddedFragmentRoots(out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Fragment_SetFocus(IntPtr t) { try { return ((INativeFragment)Target(t)!).SetFocus(); } catch (Exception ex) { return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Fragment_GetFragmentRoot(IntPtr t, IntPtr* p) { try { return ((INativeFragment)Target(t)!).GetFragmentRoot(out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, 0); } }

    // ── IRawElementProviderFragmentRoot ──
    [UnmanagedCallersOnly] private static int Root_ElementProviderFromPoint(IntPtr t, double x, double y, IntPtr* p) { try { return ((INativeFragmentRoot)Target(t)!).ElementProviderFromPoint(x, y, out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Root_GetFocus(IntPtr t, IntPtr* p) { try { return ((INativeFragmentRoot)Target(t)!).GetFocus(out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, 0); } }

    // ── IRawElementProviderHwndOverride ──
    [UnmanagedCallersOnly] private static int Override_GetProvider(IntPtr t, IntPtr hwnd, IntPtr* p) { try { return ((INativeHwndOverride)Target(t)!).GetOverrideProviderForHwnd(hwnd, out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, 0); } }

    // ── IServiceProvider ──
    [UnmanagedCallersOnly] private static int SP_QueryService(IntPtr t, Guid* service, Guid* riid, IntPtr* ppv)
    {
        if (ppv != null) *ppv = IntPtr.Zero;
        return unchecked((int)0x80004002); // E_NOINTERFACE — we expose no extra services
    }

    // ── pattern providers ──
    [UnmanagedCallersOnly] private static int Invoke_Invoke(IntPtr t) { try { return ((INativeInvoke)Target(t)!).Invoke(); } catch (Exception ex) { return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Toggle_Toggle(IntPtr t) { try { return ((INativeToggle)Target(t)!).Toggle(); } catch (Exception ex) { return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Toggle_GetToggleState(IntPtr t, int* p) { try { int hr = ((INativeToggle)Target(t)!).GetToggleState(out int v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Value_SetValue(IntPtr t, IntPtr s) { try { return ((INativeValue)Target(t)!).SetValue(s); } catch (Exception ex) { return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Value_GetValue(IntPtr t, IntPtr* p) { try { return ((INativeValue)Target(t)!).GetValue(out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Value_GetIsReadOnly(IntPtr t, int* p) { try { int hr = ((INativeValue)Target(t)!).GetIsReadOnly(out int v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Sel_Select(IntPtr t) { try { return ((INativeSelectionItem)Target(t)!).Select(); } catch (Exception ex) { return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Sel_AddToSelection(IntPtr t) { try { return ((INativeSelectionItem)Target(t)!).AddToSelection(); } catch (Exception ex) { return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Sel_RemoveFromSelection(IntPtr t) { try { return ((INativeSelectionItem)Target(t)!).RemoveFromSelection(); } catch (Exception ex) { return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Sel_GetIsSelected(IntPtr t, int* p) { try { int hr = ((INativeSelectionItem)Target(t)!).GetIsSelected(out int v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Sel_GetSelectionContainer(IntPtr t, IntPtr* p) { try { return ((INativeSelectionItem)Target(t)!).GetSelectionContainer(out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int EC_Expand(IntPtr t) { try { return ((INativeExpandCollapse)Target(t)!).Expand(); } catch (Exception ex) { return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int EC_Collapse(IntPtr t) { try { return ((INativeExpandCollapse)Target(t)!).Collapse(); } catch (Exception ex) { return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int EC_GetState(IntPtr t, int* p) { try { int hr = ((INativeExpandCollapse)Target(t)!).GetExpandCollapseState(out int v); *p = v; return hr; } catch (Exception ex) { *p = 3; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Scr_Scroll(IntPtr t, int h, int v) { try { return ((INativeScroll)Target(t)!).Scroll(h, v); } catch (Exception ex) { return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Scr_SetScrollPercent(IntPtr t, double h, double v) { try { return ((INativeScroll)Target(t)!).SetScrollPercent(h, v); } catch (Exception ex) { return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Scr_GetHPercent(IntPtr t, double* p) { try { int hr = ((INativeScroll)Target(t)!).GetHorizontalScrollPercent(out double v); *p = v; return hr; } catch (Exception ex) { *p = -1; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Scr_GetVPercent(IntPtr t, double* p) { try { int hr = ((INativeScroll)Target(t)!).GetVerticalScrollPercent(out double v); *p = v; return hr; } catch (Exception ex) { *p = -1; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Scr_GetHViewSize(IntPtr t, double* p) { try { int hr = ((INativeScroll)Target(t)!).GetHorizontalViewSize(out double v); *p = v; return hr; } catch (Exception ex) { *p = 100; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Scr_GetVViewSize(IntPtr t, double* p) { try { int hr = ((INativeScroll)Target(t)!).GetVerticalViewSize(out double v); *p = v; return hr; } catch (Exception ex) { *p = 100; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Scr_GetHScrollable(IntPtr t, int* p) { try { int hr = ((INativeScroll)Target(t)!).GetHorizontallyScrollable(out int v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Scr_GetVScrollable(IntPtr t, int* p) { try { int hr = ((INativeScroll)Target(t)!).GetVerticallyScrollable(out int v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int SI_ScrollIntoView(IntPtr t) { try { return ((INativeScrollItem)Target(t)!).ScrollIntoView(); } catch (Exception ex) { return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int RV_SetValue(IntPtr t, double val) { try { return ((INativeRangeValue)Target(t)!).SetValue(val); } catch (Exception ex) { return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int RV_GetValue(IntPtr t, double* p) { try { int hr = ((INativeRangeValue)Target(t)!).GetValue(out double v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int RV_GetIsReadOnly(IntPtr t, int* p) { try { int hr = ((INativeRangeValue)Target(t)!).GetIsReadOnly(out int v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int RV_GetMaximum(IntPtr t, double* p) { try { int hr = ((INativeRangeValue)Target(t)!).GetMaximum(out double v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int RV_GetMinimum(IntPtr t, double* p) { try { int hr = ((INativeRangeValue)Target(t)!).GetMinimum(out double v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int RV_GetLargeChange(IntPtr t, double* p) { try { int hr = ((INativeRangeValue)Target(t)!).GetLargeChange(out double v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int RV_GetSmallChange(IntPtr t, double* p) { try { int hr = ((INativeRangeValue)Target(t)!).GetSmallChange(out double v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Sel2_GetSelection(IntPtr t, IntPtr* p) { try { return ((INativeSelection)Target(t)!).GetSelection(out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Sel2_GetCanSelectMultiple(IntPtr t, int* p) { try { int hr = ((INativeSelection)Target(t)!).GetCanSelectMultiple(out int v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int Sel2_GetIsSelectionRequired(IntPtr t, int* p) { try { int hr = ((INativeSelection)Target(t)!).GetIsSelectionRequired(out int v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, 0); } }
    [UnmanagedCallersOnly] private static int IC_FindItemByProperty(IntPtr t, IntPtr startAfter, int propertyId, UiaVariant value, IntPtr* p) { try { return ((INativeItemContainer)Target(t)!).FindItemByProperty(startAfter, propertyId, value, out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, 0); } }

    // ── ITextProvider / ITextProvider2 ──
    [UnmanagedCallersOnly] private static int Text_GetSelection(IntPtr t, IntPtr* p) { try { return ((INativeTextProvider)Target(t)!).GetSelection(out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int Text_GetVisibleRanges(IntPtr t, IntPtr* p) { try { return ((INativeTextProvider)Target(t)!).GetVisibleRanges(out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int Text_RangeFromChild(IntPtr t, IntPtr c, IntPtr* p) { try { return ((INativeTextProvider)Target(t)!).RangeFromChild(c, out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int Text_RangeFromPoint(IntPtr t, UiaPoint pt, IntPtr* p) { try { return ((INativeTextProvider)Target(t)!).RangeFromPoint(pt, out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int Text_GetDocumentRange(IntPtr t, IntPtr* p) { try { return ((INativeTextProvider)Target(t)!).GetDocumentRange(out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int Text_GetSupportedTextSelection(IntPtr t, int* p) { try { int hr = ((INativeTextProvider)Target(t)!).GetSupportedTextSelection(out int v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int Text_RangeFromAnnotation(IntPtr t, IntPtr a, IntPtr* p) { try { return ((INativeTextProvider)Target(t)!).RangeFromAnnotation(a, out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int Text_GetCaretRange(IntPtr t, int* active, IntPtr* p) { try { int hr = ((INativeTextProvider)Target(t)!).GetCaretRange(out int a, out *p); *active = a; return hr; } catch (Exception ex) { *active = 0; *p = IntPtr.Zero; return Hr(ex, Failed); } }

    // ── ITextRangeProvider ──
    [UnmanagedCallersOnly] private static int TR_Clone(IntPtr t, IntPtr* p) { try { return ((INativeTextRange)Target(t)!).Clone(out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_Compare(IntPtr t, IntPtr r, int* p) { try { int hr = ((INativeTextRange)Target(t)!).Compare(r, out int v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_CompareEndpoints(IntPtr t, int e, IntPtr r, int te, int* p) { try { int hr = ((INativeTextRange)Target(t)!).CompareEndpoints(e, r, te, out int v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_ExpandToEnclosingUnit(IntPtr t, int u) { try { return ((INativeTextRange)Target(t)!).ExpandToEnclosingUnit(u); } catch (Exception ex) { return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_FindAttribute(IntPtr t, int id, UiaVariant v, int back, IntPtr* p) { try { return ((INativeTextRange)Target(t)!).FindAttribute(id, v, back, out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_FindText(IntPtr t, IntPtr s, int back, int ic, IntPtr* p) { try { return ((INativeTextRange)Target(t)!).FindText(s, back, ic, out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_GetAttributeValue(IntPtr t, int id, void* p) { try { int hr = ((INativeTextRange)Target(t)!).GetAttributeValue(id, out var v); *(UiaVariant*)p = v; return hr; } catch (Exception ex) { *(UiaVariant*)p = UiaVariant.Empty; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_GetBoundingRectangles(IntPtr t, IntPtr* p) { try { return ((INativeTextRange)Target(t)!).GetBoundingRectangles(out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_GetEnclosingElement(IntPtr t, IntPtr* p) { try { return ((INativeTextRange)Target(t)!).GetEnclosingElement(out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_GetText(IntPtr t, int max, IntPtr* p) { try { return ((INativeTextRange)Target(t)!).GetText(max, out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_Move(IntPtr t, int u, int c, int* p) { try { int hr = ((INativeTextRange)Target(t)!).Move(u, c, out int v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_MoveEndpointByUnit(IntPtr t, int e, int u, int c, int* p) { try { int hr = ((INativeTextRange)Target(t)!).MoveEndpointByUnit(e, u, c, out int v); *p = v; return hr; } catch (Exception ex) { *p = 0; return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_MoveEndpointByRange(IntPtr t, int e, IntPtr r, int te) { try { return ((INativeTextRange)Target(t)!).MoveEndpointByRange(e, r, te); } catch (Exception ex) { return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_Select(IntPtr t) { try { return ((INativeTextRange)Target(t)!).Select(); } catch (Exception ex) { return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_AddToSelection(IntPtr t) { try { return ((INativeTextRange)Target(t)!).AddToSelection(); } catch (Exception ex) { return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_RemoveFromSelection(IntPtr t) { try { return ((INativeTextRange)Target(t)!).RemoveFromSelection(); } catch (Exception ex) { return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_ScrollIntoView(IntPtr t, int top) { try { return ((INativeTextRange)Target(t)!).ScrollIntoView(top); } catch (Exception ex) { return Hr(ex, Failed); } }
    [UnmanagedCallersOnly] private static int TR_GetChildren(IntPtr t, IntPtr* p) { try { return ((INativeTextRange)Target(t)!).GetChildren(out *p); } catch (Exception ex) { *p = IntPtr.Zero; return Hr(ex, Failed); } }

    /// <summary>E_FAIL. The text pattern's thunks answer it for an exception, where the older
    /// ones answer S_OK with an empty result: a reader told a text call failed asks again or
    /// gives up, where one told "nothing" reads nothing.</summary>
    private const int Failed = unchecked((int)0x80004005);
}
