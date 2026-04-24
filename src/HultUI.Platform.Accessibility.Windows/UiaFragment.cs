using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// UIA wrapper for a single <see cref="AccessibilityNode"/>. Wrappers are
/// cached in <see cref="WindowsAccessibilityBridge"/> keyed by
/// <see cref="ElementId"/> so COM identity stays stable across tree rebuilds —
/// UIA clients compare providers by reference.
/// </summary>
/// <remarks>
/// Property values are snapshot-copied here (Name, Role, Value, etc.) rather
/// than fetched live through the node reference. UIA may call provider methods
/// from worker threads; snapshots avoid cross-thread element access and also
/// keep wrappers valid briefly after a node has been removed (UIA sometimes
/// queries just-removed providers before fully disconnecting).
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class UiaFragment :
    IRawElementProviderSimple,
    IRawElementProviderFragment
{
    private readonly WindowsAccessibilityBridge _bridge;
    public ElementId ElementId { get; }
    public AccessibilityNode Node { get; private set; }

    private string? _name;
    private string? _description;
    private string? _value;
    private AccessibilityRole _role;
    private Rect _bounds;
    private bool _isFocusable;
    private bool _isFocused;
    private bool _isEnabled;
    private bool? _isToggled;

    public UiaFragment(WindowsAccessibilityBridge bridge, AccessibilityNode node)
    {
        _bridge = bridge;
        Node = node;
        ElementId = node.ElementId;
        Snapshot(node);
    }

    public void UpdateFrom(AccessibilityNode node)
    {
        Node = node;
        Snapshot(node);
    }

    private void Snapshot(AccessibilityNode node)
    {
        _name = node.Name;
        _description = node.Description;
        _value = node.Value;
        _role = node.Role;
        _bounds = node.BoundingRect;
        _isFocusable = node.IsFocusable;
        _isFocused = node.IsFocused;
        _isEnabled = node.IsEnabled;
        _isToggled = node.IsToggled;
    }

    // ──── IRawElementProviderSimple

    public int GetProviderOptions(out int pRetVal)
    {
        pRetVal = UiaInterop.ProviderOptions_ServerSideProvider | UiaInterop.ProviderOptions_UseComThreading;
        return 0;
    }

    public int GetPatternProvider(int patternId, out IntPtr pPatternProvider)
    {
        pPatternProvider = IntPtr.Zero;
        object? provider = _role switch
        {
            AccessibilityRole.Button when patternId == UiaInterop.UIA_InvokePatternId
                => new UiaInvokeProvider(_bridge, ElementId),
            AccessibilityRole.Checkbox when patternId == UiaInterop.UIA_TogglePatternId
                => new UiaToggleProvider(_bridge, ElementId),
            AccessibilityRole.RadioButton when patternId == UiaInterop.UIA_SelectionItemPatternId
                => new UiaSelectionItemProvider(_bridge, ElementId),
            AccessibilityRole.TextInput when patternId == UiaInterop.UIA_ValuePatternId
                => new UiaValueProvider(_bridge, ElementId),
            _ => null,
        };

        if (provider is null)
            return 0; // S_OK + null out-ptr means "pattern not supported"

        pPatternProvider = UiaComWrappers.GetIUnknown(provider);
        return 0;
    }

    public int GetPropertyValue(int propertyId, out UiaVariant pRetVal)
    {
        pRetVal = propertyId switch
        {
            UiaInterop.UIA_NamePropertyId when _name is not null => UiaVariant.FromBStr(_name),
            UiaInterop.UIA_HelpTextPropertyId when _description is not null => UiaVariant.FromBStr(_description),
            UiaInterop.UIA_ControlTypePropertyId => UiaVariant.FromInt(MapControlType(_role)),
            UiaInterop.UIA_IsKeyboardFocusablePropertyId => UiaVariant.FromBool(_isFocusable),
            UiaInterop.UIA_HasKeyboardFocusPropertyId => UiaVariant.FromBool(_isFocused),
            UiaInterop.UIA_IsEnabledPropertyId => UiaVariant.FromBool(_isEnabled),
            UiaInterop.UIA_ValueValuePropertyId when _value is not null => UiaVariant.FromBStr(_value),
            UiaInterop.UIA_ToggleToggleStatePropertyId when _isToggled is { } t
                => UiaVariant.FromInt(t ? UiaInterop.ToggleState_On : UiaInterop.ToggleState_Off),
            _ => UiaVariant.Empty,
        };
        return 0;
    }

    public int GetHostRawElementProvider(out IRawElementProviderSimple? pRetVal)
    {
        // Only the root fragment chains to the HWND host provider — child
        // fragments return null so UIA doesn't double-walk the tree.
        pRetVal = Node.Parent is null ? _bridge.HostProvider : null;
        return 0;
    }

    // ──── IRawElementProviderFragment

    public int Navigate(int direction, out IRawElementProviderFragment? pRetVal)
    {
        pRetVal = direction switch
        {
            UiaInterop.NavigateDirection_Parent => _bridge.GetFragment(Node.Parent?.ElementId),
            UiaInterop.NavigateDirection_FirstChild when Node.Children.Count > 0
                => _bridge.GetFragment(Node.Children[0].ElementId),
            UiaInterop.NavigateDirection_LastChild when Node.Children.Count > 0
                => _bridge.GetFragment(Node.Children[^1].ElementId),
            UiaInterop.NavigateDirection_NextSibling => Sibling(+1),
            UiaInterop.NavigateDirection_PreviousSibling => Sibling(-1),
            _ => null,
        };
        return 0;
    }

    private IRawElementProviderFragment? Sibling(int delta)
    {
        var parent = Node.Parent;
        if (parent is null) return null;
        var siblings = parent.Children;
        for (int i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].ElementId != ElementId) continue;
            int ni = i + delta;
            if (ni < 0 || ni >= siblings.Count) return null;
            return _bridge.GetFragment(siblings[ni].ElementId);
        }
        return null;
    }

    public int GetRuntimeId(out IntPtr pRetVal)
    {
        // UIA expects a SAFEARRAY of int here. Returning null is valid and
        // tells UIA to use COM-pointer identity instead, which works for us
        // because wrappers are cached by ElementId.
        pRetVal = IntPtr.Zero;
        return 0;
    }

    public int GetBoundingRectangle(out UiaRect pRetVal)
    {
        var screen = _bridge.ToScreen(_bounds);
        pRetVal = new UiaRect
        {
            Left = screen.X,
            Top = screen.Y,
            Width = screen.Width,
            Height = screen.Height,
        };
        return 0;
    }

    public int GetEmbeddedFragmentRoots(out IntPtr pRetVal)
    {
        pRetVal = IntPtr.Zero;
        return 0;
    }

    public int SetFocus()
    {
        _bridge.RequestFocus(ElementId);
        return 0;
    }

    public int GetFragmentRoot(out IRawElementProviderFragmentRoot? pRetVal)
    {
        pRetVal = _bridge.FragmentRoot;
        return 0;
    }

    // ──── Helpers

    private static int MapControlType(AccessibilityRole role) => role switch
    {
        AccessibilityRole.Button => UiaInterop.UIA_ButtonControlTypeId,
        AccessibilityRole.Checkbox => UiaInterop.UIA_CheckBoxControlTypeId,
        AccessibilityRole.RadioButton => UiaInterop.UIA_RadioButtonControlTypeId,
        AccessibilityRole.TextInput => UiaInterop.UIA_EditControlTypeId,
        AccessibilityRole.Label => UiaInterop.UIA_TextControlTypeId,
        AccessibilityRole.Image => UiaInterop.UIA_ImageControlTypeId,
        AccessibilityRole.ScrollView => UiaInterop.UIA_PaneControlTypeId,
        AccessibilityRole.Group => UiaInterop.UIA_GroupControlTypeId,
        AccessibilityRole.Tab => UiaInterop.UIA_TabControlTypeId,
        AccessibilityRole.Separator => UiaInterop.UIA_SeparatorControlTypeId,
        AccessibilityRole.Application => UiaInterop.UIA_WindowControlTypeId,
        _ => UiaInterop.UIA_PaneControlTypeId,
    };
}
