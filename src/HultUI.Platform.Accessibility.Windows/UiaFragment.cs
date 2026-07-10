using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// Provider logic for a single <see cref="AccessibilityNode"/>. Logic objects
/// are cached in <see cref="WindowsAccessibilityBridge"/> keyed by
/// <see cref="ElementId"/>; UIA identity is tracked by the stable runtime id
/// (see <see cref="GetRuntimeId"/>), so the native COM wrappers can be created
/// on demand. Exposed to UIA as IRawElementProviderSimple / Fragment.
/// </summary>
/// <remarks>
/// Property values are snapshot-copied here (Name, Role, Value, etc.) rather
/// than read live through the node reference — UIA may call provider methods
/// from worker threads, so snapshots avoid cross-thread element access.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class UiaFragment : INativeSimple, INativeFragment
{
    private readonly WindowsAccessibilityBridge _bridge;
    public ElementId ElementId { get; }
    public AccessibilityNode Node { get; private set; }

    private string? _name;
    private string? _description;
    private string? _value;
    private string? _automationId;
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
        _automationId = node.AutomationId;
        _role = node.Role;
        _bounds = node.BoundingRect;
        _isFocusable = node.IsFocusable;
        _isFocused = node.IsFocused;
        _isEnabled = node.IsEnabled;
        _isToggled = node.IsToggled;
    }

    // ── IRawElementProviderSimple ──

    public int GetProviderOptions(out int pRetVal)
    {
        pRetVal = UiaInterop.ProviderOptions_ServerSideProvider;
        return 0;
    }

    public int GetPatternProvider(int patternId, out IntPtr pPatternProvider)
    {
        object? provider = _role switch
        {
            AccessibilityRole.Button when patternId == UiaInterop.UIA_InvokePatternId
                => new UiaInvokeProvider(_bridge, ElementId),
            // List items are invokable so assistive tech can activate any row —
            // including virtualized ones whose id routes back to the owning list.
            AccessibilityRole.ListItem when patternId == UiaInterop.UIA_InvokePatternId
                => new UiaInvokeProvider(_bridge, ElementId),
            AccessibilityRole.Checkbox when patternId == UiaInterop.UIA_TogglePatternId
                => new UiaToggleProvider(_bridge, ElementId),
            AccessibilityRole.RadioButton when patternId == UiaInterop.UIA_SelectionItemPatternId
                => new UiaSelectionItemProvider(_bridge, ElementId),
            AccessibilityRole.TextInput when patternId == UiaInterop.UIA_ValuePatternId
                => new UiaValueProvider(_bridge, ElementId),
            _ => null,
        };

        pPatternProvider = provider is null ? IntPtr.Zero : NativeProvider.Create(provider);
        return 0;
    }

    public int GetPropertyValue(int propertyId, out UiaVariant pRetVal)
    {
        pRetVal = propertyId switch
        {
            UiaInterop.UIA_NamePropertyId when _name is not null => UiaVariant.FromBStr(_name),
            UiaInterop.UIA_AutomationIdPropertyId when _automationId is not null => UiaVariant.FromBStr(_automationId),
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

    public int GetHostRawElementProvider(out IntPtr pRetVal)
    {
        // Host chaining lives on the fragment root only; element fragments must
        // return null or UIA double-walks to the HWND host.
        pRetVal = IntPtr.Zero;
        return 0;
    }

    // ── IRawElementProviderFragment ──

    public int Navigate(int direction, out IntPtr pRetVal)
    {
        // Snapshot Node once: the UI thread may swap it via UpdateFrom while this
        // runs on a UIA worker thread. Each Node is an immutable per-frame tree.
        var node = Node;

        if (direction == UiaInterop.NavigateDirection_Parent)
        {
            // Navigate returns IRawElementProviderFragment*: the root's Fragment
            // tear-off when this is the app-root element.
            pRetVal = node.Parent is null ? _bridge.NativeRootFragment() : _bridge.NativeFragment(node.Parent.ElementId);
            return 0;
        }

        // A large virtualized list has no child nodes; provide its items on demand.
        // (Its own siblings fall through to the normal logic below.)
        if (node.IsVirtualItemContainer &&
            (direction == UiaInterop.NavigateDirection_FirstChild || direction == UiaInterop.NavigateDirection_LastChild))
        {
            int count = _bridge.GetVirtualItemCount(ElementId);
            pRetVal = count <= 0 ? IntPtr.Zero
                : _bridge.NativeVirtualItem(ElementId,
                    direction == UiaInterop.NavigateDirection_FirstChild ? 0 : count - 1);
            return 0;
        }

        ElementId? target = direction switch
        {
            UiaInterop.NavigateDirection_FirstChild when node.Children.Count > 0 => node.Children[0].ElementId,
            UiaInterop.NavigateDirection_LastChild when node.Children.Count > 0 => node.Children[^1].ElementId,
            UiaInterop.NavigateDirection_NextSibling => Sibling(node, +1),
            UiaInterop.NavigateDirection_PreviousSibling => Sibling(node, -1),
            _ => null,
        };
        pRetVal = target is null ? IntPtr.Zero : _bridge.NativeFragment(target.Value);
        return 0;
    }

    private ElementId? Sibling(AccessibilityNode node, int delta)
    {
        var parent = node.Parent;
        if (parent is null) return null;
        var siblings = parent.Children;
        for (int i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].ElementId != ElementId) continue;
            int ni = i + delta;
            if (ni < 0 || ni >= siblings.Count) return null;
            return siblings[ni].ElementId;
        }
        return null;
    }

    public int GetRuntimeId(out IntPtr pRetVal)
    {
        // A stable runtime id (ElementId survives tree rebuilds) lets UIA track
        // element identity even though the native wrappers are created on demand.
        pRetVal = UiaInterop.BuildRuntimeId(ElementId.Value);
        return 0;
    }

    public int GetBoundingRectangle(out UiaRect pRetVal)
    {
        var screen = _bridge.ToScreen(_bounds);
        pRetVal = new UiaRect { Left = screen.X, Top = screen.Y, Width = screen.Width, Height = screen.Height };
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

    public int GetFragmentRoot(out IntPtr pRetVal)
    {
        pRetVal = _bridge.NativeRootFragmentRoot();
        return 0;
    }

    // ── Helpers ──

    private static int MapControlType(AccessibilityRole role) => role switch
    {
        AccessibilityRole.Button => UiaInterop.UIA_ButtonControlTypeId,
        AccessibilityRole.Checkbox => UiaInterop.UIA_CheckBoxControlTypeId,
        AccessibilityRole.RadioButton => UiaInterop.UIA_RadioButtonControlTypeId,
        AccessibilityRole.TextInput => UiaInterop.UIA_EditControlTypeId,
        AccessibilityRole.Label => UiaInterop.UIA_TextControlTypeId,
        AccessibilityRole.Image => UiaInterop.UIA_ImageControlTypeId,
        AccessibilityRole.List => UiaInterop.UIA_ListControlTypeId,
        AccessibilityRole.ListItem => UiaInterop.UIA_ListItemControlTypeId,
        AccessibilityRole.ScrollView => UiaInterop.UIA_PaneControlTypeId,
        AccessibilityRole.Group => UiaInterop.UIA_GroupControlTypeId,
        AccessibilityRole.Tab => UiaInterop.UIA_TabControlTypeId,
        AccessibilityRole.Separator => UiaInterop.UIA_SeparatorControlTypeId,
        AccessibilityRole.Application => UiaInterop.UIA_WindowControlTypeId,
        AccessibilityRole.Link => UiaInterop.UIA_HyperlinkControlTypeId,
        AccessibilityRole.Slider => UiaInterop.UIA_SliderControlTypeId,
        // Toggle switches read as check boxes (with toggle state) to UIA.
        AccessibilityRole.Switch => UiaInterop.UIA_CheckBoxControlTypeId,
        // No dedicated UIA control type: headings/alerts are text, dialogs are
        // windows, and a tab panel is a plain container.
        AccessibilityRole.Heading => UiaInterop.UIA_TextControlTypeId,
        AccessibilityRole.Alert => UiaInterop.UIA_TextControlTypeId,
        AccessibilityRole.Dialog => UiaInterop.UIA_WindowControlTypeId,
        AccessibilityRole.TabPanel => UiaInterop.UIA_PaneControlTypeId,
        _ => UiaInterop.UIA_PaneControlTypeId,
    };
}
