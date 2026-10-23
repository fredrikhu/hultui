using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// Provider logic for a single item of a large virtualized list, provisioned on
/// demand rather than materialized as an <see cref="AccessibilityNode"/>. It
/// carries only its owning list and index; name, bounds, sibling count and
/// activation are resolved live through the bridge/host each time UIA asks — so a
/// list of any size costs nothing until assistive tech actually walks into it.
/// </summary>
/// <remarks>
/// A fresh native object is created per navigation result (see
/// <see cref="WindowsAccessibilityBridge.NativeVirtualItem"/>); UIA tracks
/// identity by the stable runtime id from <see cref="VirtualItemId"/>, so these
/// need no caching.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class UiaVirtualItem(WindowsAccessibilityBridge bridge, ElementId listId, int index)
    : INativeSimple, INativeFragment, IElementBound
{
    /// <summary>An item of a list that left the tree is gone with it.</summary>
    public bool Gone => bridge.IsGone(listId);

    /// <summary>The owning list, so ItemContainer can decode a startAfter item.</summary>
    public ElementId ListId => listId;

    /// <summary>This item's index within the list.</summary>
    public int Index => index;

    // ── IRawElementProviderSimple ──

    public int GetProviderOptions(out int pRetVal)
    {
        pRetVal = UiaInterop.ProviderOptions_ServerSideProvider;
        return 0;
    }

    public int GetPatternProvider(int patternId, out IntPtr pPatternProvider)
    {
        // Invoke and SelectionItem both route through the synthetic id back to the
        // list — the same path materialized list items use.
        var id = VirtualItemId.For(listId, index);
        object? provider = patternId switch
        {
            UiaInterop.UIA_InvokePatternId => new UiaInvokeProvider(bridge, id),
            UiaInterop.UIA_SelectionItemPatternId => new UiaSelectionItemProvider(bridge, id),
            UiaInterop.UIA_ScrollItemPatternId => new UiaScrollItemProvider(bridge, id),
            _ => null,
        };
        pPatternProvider = provider is null ? IntPtr.Zero : NativeProvider.Create(provider);
        return 0;
    }

    public int GetPropertyValue(int propertyId, out UiaVariant pRetVal)
    {
        pRetVal = propertyId switch
        {
            UiaInterop.UIA_NamePropertyId when bridge.GetVirtualItemLabel(listId, index) is { } name
                => UiaVariant.FromBStr(name),
            UiaInterop.UIA_ControlTypePropertyId => UiaVariant.FromInt(UiaInterop.UIA_ListItemControlTypeId),
            UiaInterop.UIA_IsEnabledPropertyId => UiaVariant.FromBool(true),
            UiaInterop.UIA_IsOffscreenPropertyId => UiaVariant.FromBool(bridge.GetVirtualItemOffscreen(listId, index)),
            _ => UiaVariant.Empty,
        };
        return 0;
    }

    public int GetHostRawElementProvider(out IntPtr pRetVal)
    {
        pRetVal = IntPtr.Zero;
        return 0;
    }

    // ── IRawElementProviderFragment ──

    public int Navigate(int direction, out IntPtr pRetVal)
    {
        pRetVal = direction switch
        {
            UiaInterop.NavigateDirection_Parent => bridge.NativeFragment(listId),
            UiaInterop.NavigateDirection_NextSibling when index + 1 < bridge.GetVirtualItemCount(listId)
                => bridge.NativeVirtualItem(listId, index + 1),
            UiaInterop.NavigateDirection_PreviousSibling when index > 0
                => bridge.NativeVirtualItem(listId, index - 1),
            _ => IntPtr.Zero, // items have no children
        };
        return 0;
    }

    public int GetRuntimeId(out IntPtr pRetVal)
    {
        pRetVal = UiaInterop.BuildRuntimeId(VirtualItemId.For(listId, index).Value);
        return 0;
    }

    public int GetBoundingRectangle(out UiaRect pRetVal)
    {
        if (bridge.TryGetVirtualItemScreenRect(listId, index, out Rect screen))
            pRetVal = new UiaRect { Left = screen.X, Top = screen.Y, Width = screen.Width, Height = screen.Height };
        else
            pRetVal = default;
        return 0;
    }

    public int GetEmbeddedFragmentRoots(out IntPtr pRetVal)
    {
        pRetVal = IntPtr.Zero;
        return 0;
    }

    public int SetFocus() => 0;

    public int GetFragmentRoot(out IntPtr pRetVal)
    {
        pRetVal = bridge.NativeRootFragmentRoot();
        return 0;
    }
}
