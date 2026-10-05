using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// P/Invokes for UIAutomationCore.dll plus the small set of User32 calls we
/// need, along with UIA constant tables. Everything here is Windows-only —
/// callers must be gated with <see cref="OperatingSystem.IsWindows"/> or an
/// analogous host-side check.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class UiaInterop
{
    /// <summary>lParam value on <c>WM_GETOBJECT</c> for UIA root queries.</summary>
    public const int UiaRootObjectId = -25;

    // Returns an LRESULT (pointer-sized); the window procedure must return it
    // verbatim so UIA connects the provider.
    [LibraryImport("UIAutomationCore.dll")]
    public static partial nint UiaReturnRawElementProvider(
        IntPtr hwnd, IntPtr wParam, IntPtr lParam, IntPtr provider);

    // ── Window-procedure subclassing (serve WM_GETOBJECT from the real WndProc,
    //    not SDL's message-pump hook, which never sees the sent message). ──
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
    public static partial IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public const int GWLP_WNDPROC = -4;
    public const uint WM_GETOBJECT = 0x003D;

    [LibraryImport("UIAutomationCore.dll")]
    public static partial int UiaHostProviderFromHwnd(IntPtr hwnd, out IntPtr provider);

    [LibraryImport("UIAutomationCore.dll")]
    public static partial int UiaRaiseAutomationEvent(IntPtr provider, int eventId);

    // oldValue/newValue are VARIANTs passed BY VALUE. Declaring them as
    // by-value UiaVariant lets the marshaller honor the per-architecture ABI
    // (x64 passes a pointer to a caller-allocated copy; x86 pushes the 16
    // bytes). Passing IntPtr.Zero here made UIA dereference a null VARIANT.
    [LibraryImport("UIAutomationCore.dll")]
    public static partial int UiaRaiseAutomationPropertyChangedEvent(
        IntPtr provider, int propertyId, UiaVariant oldValue, UiaVariant newValue);

    [LibraryImport("UIAutomationCore.dll")]
    public static partial int UiaRaiseStructureChangedEvent(
        IntPtr provider, int structureChangeType, IntPtr runtimeId, int runtimeIdLen);

    [LibraryImport("UIAutomationCore.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UiaClientsAreListening();

    [LibraryImport("UIAutomationCore.dll")]
    public static partial int UiaDisconnectProvider(IntPtr provider);

    /// <summary>Has assistive technology speak <paramref name="displayString"/> (Windows 10
    /// 1709 and later). <paramref name="activityId"/> names the kind of announcement, so a
    /// reader can treat a run of them alike.</summary>
    [LibraryImport("UIAutomationCore.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int UiaRaiseNotificationEvent(IntPtr provider, int notificationKind,
        int notificationProcessing, IntPtr displayString, IntPtr activityId);

    /// <summary><see cref="UiaRaiseNotificationEvent(IntPtr, int, int, IntPtr, IntPtr)"/>
    /// with the strings as BSTRs, freed after; false where Windows predates notifications.</summary>
    public static bool RaiseNotification(IntPtr provider, int kind, int processing, string text, string activityId)
    {
        IntPtr display = Marshal.StringToBSTR(text), activity = Marshal.StringToBSTR(activityId);
        try { return UiaRaiseNotificationEvent(provider, kind, processing, display, activity) >= 0; }
        catch (EntryPointNotFoundException) { return false; }
        finally { Marshal.FreeBSTR(display); Marshal.FreeBSTR(activity); }
    }

    /// <summary>The value a text range's attribute answers when it is not supported: a COM
    /// object of UIA's own, handed back in a VT_UNKNOWN VARIANT.</summary>
    [LibraryImport("UIAutomationCore.dll")]
    public static partial int UiaGetReservedNotSupportedValue(out IntPtr notSupportedValue);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(IntPtr hwnd, ref PointNative point);

    // ──── Runtime-id SAFEARRAY construction (oleaut32)

    [LibraryImport("oleaut32.dll")]
    private static partial IntPtr SafeArrayCreateVector(ushort vt, int lLbound, uint cElements);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayAccessData(IntPtr psa, out IntPtr ppvData);

    [LibraryImport("oleaut32.dll")]
    private static partial int SafeArrayUnaccessData(IntPtr psa);

    /// <summary>
    /// Builds a UIA runtime id as a SAFEARRAY of int:
    /// <c>[UiaAppendRuntimeId, hi32, lo32]</c>. The <see cref="UiaAppendRuntimeId"/>
    /// sentinel tells UIA to prepend the window-unique prefix, so our suffix only
    /// needs to be unique within the window — the element's stable
    /// <see cref="HultUI.Core.Elements.ElementId"/> provides that. UIA takes ownership of the
    /// returned array and frees it. Returns <see cref="IntPtr.Zero"/> on failure
    /// (a valid "no runtime id" result).
    /// </summary>
    public static IntPtr BuildRuntimeId(ulong uniqueId)
    {
        const ushort VT_I4 = 3;
        IntPtr psa = SafeArrayCreateVector(VT_I4, 0, 3);
        if (psa == IntPtr.Zero)
            return IntPtr.Zero;
        if (SafeArrayAccessData(psa, out IntPtr data) != 0)
            return psa; // array exists but couldn't be filled — still valid to return
        Marshal.WriteInt32(data, 0, UiaAppendRuntimeId);
        Marshal.WriteInt32(data, 4, unchecked((int)(uniqueId >> 32)));
        Marshal.WriteInt32(data, 8, unchecked((int)uniqueId));
        SafeArrayUnaccessData(psa);
        return psa;
    }

    /// <summary>Sentinel first element of a runtime id — UIA prepends the HWND prefix.</summary>
    public const int UiaAppendRuntimeId = 3;

    /// <summary>
    /// Builds a SAFEARRAY of <c>IRawElementProviderSimple*</c> (VT_UNKNOWN) from
    /// the given provider pointers — used by <c>ISelectionProvider.GetSelection</c>.
    /// Each pointer's reference is transferred into the array (we write the slots
    /// directly rather than via SafeArrayPutElement, which would AddRef); UIA
    /// Releases every element when it frees the array. Pass an empty span for an
    /// empty selection — that returns a valid zero-length array, not null.
    /// </summary>
    public static IntPtr BuildProviderArray(ReadOnlySpan<IntPtr> providers)
    {
        const ushort VT_UNKNOWN = 13;
        IntPtr psa = SafeArrayCreateVector(VT_UNKNOWN, 0, (uint)providers.Length);
        if (psa == IntPtr.Zero || providers.Length == 0)
            return psa;
        if (SafeArrayAccessData(psa, out IntPtr data) != 0)
            return psa; // array exists but couldn't be filled — still valid to return
        for (int i = 0; i < providers.Length; i++)
            Marshal.WriteIntPtr(data, i * IntPtr.Size, providers[i]);
        SafeArrayUnaccessData(psa);
        return psa;
    }

    /// <summary>
    /// A SAFEARRAY of doubles (VT_R8): what <c>ITextRangeProvider::GetBoundingRectangles</c>
    /// returns, four per rectangle (left, top, width, height). UIA takes ownership.
    /// </summary>
    public static IntPtr BuildDoubleArray(ReadOnlySpan<double> values)
    {
        const ushort VT_R8 = 5;
        IntPtr psa = SafeArrayCreateVector(VT_R8, 0, (uint)values.Length);
        if (psa == IntPtr.Zero || values.Length == 0)
            return psa;
        if (SafeArrayAccessData(psa, out IntPtr data) != 0)
            return psa;
        Marshal.Copy(values.ToArray(), 0, data, values.Length);
        SafeArrayUnaccessData(psa);
        return psa;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PointNative
    {
        public int X;
        public int Y;
    }

    // ──── UIA property IDs (a small, curated subset — add as we wire more
    //      patterns). Reference: Microsoft Docs / UIAutomationCore.h.
    public const int UIA_NamePropertyId = 30005;
    public const int UIA_AutomationIdPropertyId = 30011;
    public const int UIA_HelpTextPropertyId = 30013;
    public const int UIA_BoundingRectanglePropertyId = 30001;
    public const int UIA_IsKeyboardFocusablePropertyId = 30009;
    public const int UIA_HasKeyboardFocusPropertyId = 30008;
    public const int UIA_IsEnabledPropertyId = 30010;
    public const int UIA_IsControlElementPropertyId = 30016;
    public const int UIA_IsContentElementPropertyId = 30017;
    public const int UIA_IsOffscreenPropertyId = 30022;
    public const int UIA_IsPasswordPropertyId = 30019;
    public const int UIA_ControlTypePropertyId = 30003;
    public const int UIA_ValueValuePropertyId = 30045;
    public const int UIA_ToggleToggleStatePropertyId = 30086;
    public const int UIA_ExpandCollapseExpandCollapseStatePropertyId = 30070;
    public const int UIA_SelectionItemIsSelectedPropertyId = 30079;

    // ──── UIA control-type IDs
    public const int UIA_ButtonControlTypeId = 50000;
    public const int UIA_CheckBoxControlTypeId = 50002;
    public const int UIA_ComboBoxControlTypeId = 50003;
    public const int UIA_MenuBarControlTypeId = 50010;
    public const int UIA_MenuItemControlTypeId = 50011;
    public const int UIA_HeaderItemControlTypeId = 50035;
    public const int UIA_EditControlTypeId = 50004;
    public const int UIA_GroupControlTypeId = 50026;
    public const int UIA_DocumentControlTypeId = 50030;
    public const int UIA_HyperlinkControlTypeId = 50005;
    public const int UIA_ImageControlTypeId = 50006;
    public const int UIA_ListControlTypeId = 50008;
    public const int UIA_ListItemControlTypeId = 50007;
    public const int UIA_PaneControlTypeId = 50033;
    public const int UIA_SliderControlTypeId = 50015;
    public const int UIA_ProgressBarControlTypeId = 50012;
    public const int UIA_SpinnerControlTypeId = 50016;
    public const int UIA_RadioButtonControlTypeId = 50013;
    public const int UIA_ScrollBarControlTypeId = 50014;
    public const int UIA_SeparatorControlTypeId = 50038;
    /// <summary>The strip that holds the tabs — not one tab, which is <see cref="UIA_TabItemControlTypeId"/>.</summary>
    public const int UIA_TabControlTypeId = 50018;
    public const int UIA_TabItemControlTypeId = 50019;
    public const int UIA_TextControlTypeId = 50020;
    public const int UIA_TreeControlTypeId = 50023;
    public const int UIA_TreeItemControlTypeId = 50024;
    public const int UIA_WindowControlTypeId = 50032;

    // ──── UIA pattern IDs
    public const int UIA_InvokePatternId = 10000;
    public const int UIA_SelectionPatternId = 10001;
    public const int UIA_SelectionItemPatternId = 10010;
    public const int UIA_TogglePatternId = 10015;
    public const int UIA_ValuePatternId = 10002;
    public const int UIA_ExpandCollapsePatternId = 10005;
    public const int UIA_ScrollPatternId = 10004;
    public const int UIA_RangeValuePatternId = 10003;
    public const int UIA_ItemContainerPatternId = 10019;
    public const int UIA_TextPatternId = 10014;
    public const int UIA_TextPattern2Id = 10024;

    // ──── Text pattern: TextUnit, TextPatternRangeEndpoint, SupportedTextSelection, attributes
    public const int TextUnit_Character = 0;
    public const int TextUnit_Format = 1;
    public const int TextUnit_Word = 2;
    public const int TextUnit_Line = 3;
    public const int TextUnit_Paragraph = 4;
    public const int TextUnit_Page = 5;
    public const int TextUnit_Document = 6;
    public const int TextPatternRangeEndpoint_Start = 0;
    public const int TextPatternRangeEndpoint_End = 1;
    public const int SupportedTextSelection_None = 0;
    public const int SupportedTextSelection_Single = 1;
    public const int UIA_IsReadOnlyAttributeId = 40015;

    // ──── Notifications
    public const int NotificationKind_Other = 4;
    public const int NotificationProcessing_ImportantAll = 0;
    public const int NotificationProcessing_All = 2;

    // ──── HRESULTs a provider answers
    public const int E_INVALIDARG = unchecked((int)0x80070057);
    /// <summary>UIA_E_INVALIDOPERATION: what selecting read-only text answers.</summary>
    public const int UIA_E_INVALIDOPERATION = unchecked((int)0x80131509);

    // ──── ScrollAmount + "no scroll" sentinel
    public const int ScrollAmount_LargeDecrement = 0;
    public const int ScrollAmount_SmallDecrement = 1;
    public const int ScrollAmount_NoAmount = 2;
    public const int ScrollAmount_LargeIncrement = 3;
    public const int ScrollAmount_SmallIncrement = 4;
    public const double UIA_ScrollPatternNoScroll = -1.0;

    // ──── ExpandCollapseState
    public const int ExpandCollapseState_Collapsed = 0;
    public const int ExpandCollapseState_Expanded = 1;
    public const int ExpandCollapseState_PartiallyExpanded = 2;
    public const int ExpandCollapseState_LeafNode = 3;

    // ──── UIA event IDs
    public const int UIA_AutomationFocusChangedEventId = 20005;
    public const int UIA_Invoke_InvokedEventId = 20009;
    public const int UIA_Text_TextSelectionChangedEventId = 20014;
    public const int UIA_Text_TextChangedEventId = 20015;

    // ──── StructureChangeType
    public const int StructureChangeType_ChildAdded = 0;
    public const int StructureChangeType_ChildRemoved = 1;
    public const int StructureChangeType_ChildrenInvalidated = 2;
    public const int StructureChangeType_ChildrenBulkAdded = 3;
    public const int StructureChangeType_ChildrenBulkRemoved = 4;
    public const int StructureChangeType_ChildrenReordered = 5;

    // ──── NavigateDirection
    public const int NavigateDirection_Parent = 0;
    public const int NavigateDirection_NextSibling = 1;
    public const int NavigateDirection_PreviousSibling = 2;
    public const int NavigateDirection_FirstChild = 3;
    public const int NavigateDirection_LastChild = 4;

    // ──── ProviderOptions flags
    // (The first of these read 0x1 until the web view needed the override flag: that is
    // ClientSideProvider, which the providers here are not. UIA was lenient about it.)
    public const int ProviderOptions_ServerSideProvider = 0x2;
    public const int ProviderOptions_OverrideProvider = 0x8;
    public const int ProviderOptions_UseComThreading = 0x20;

    // ──── ToggleState
    public const int ToggleState_Off = 0;
    public const int ToggleState_On = 1;
    public const int ToggleState_Indeterminate = 2;
}
