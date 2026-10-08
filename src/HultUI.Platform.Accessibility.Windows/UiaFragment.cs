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

    /// <summary>
    /// This element's node in the bridge's current tree. The UI thread swaps it with one
    /// reference write after every diff (<see cref="UpdateFrom"/>, <see cref="Rebase"/>); a UIA
    /// thread reads it once per call and walks that tree, which never changes once applied.
    /// </summary>
    public AccessibilityNode Node => Volatile.Read(ref _node);
    private AccessibilityNode _node;

    /// <summary>The bridge's last apply pass that added this element or found it in its tree.
    /// UI thread only.</summary>
    internal int Generation;

    private string? _name;
    private string? _description;
    private string? _value;
    private string? _automationId;
    private AccessibilityRole _role;
    private bool _isFocusable;
    private bool _isFocused;
    private bool _isEnabled;
    private bool? _isToggled;
    private bool? _expanded;
    private bool _isPassword;
    private bool? _isSelected;
    private bool _hasText;

    /// <summary>Snapshot expand state (null = not expandable), read by the ExpandCollapse provider.</summary>
    public bool? Expanded => _expanded;

    /// <summary>Snapshot selected state (null = not selectable that way), read by the
    /// SelectionItem provider for anything that is not a list item.</summary>
    public bool? Selected => _isSelected;

    public UiaFragment(WindowsAccessibilityBridge bridge, AccessibilityNode node)
    {
        _bridge = bridge;
        _node = node;
        ElementId = node.ElementId;
        Snapshot(node);
    }

    /// <summary>Takes the node's properties and points at it: for a node that is new or changed.</summary>
    public void UpdateFrom(AccessibilityNode node)
    {
        Volatile.Write(ref _node, node);
        Snapshot(node);
    }

    /// <summary>
    /// Points at the node's copy in a newer tree whose properties did not change. Every tree is
    /// built afresh, and a node reaches its whole tree through Parent and Children, so a wrapper
    /// left on an old node keeps that tree alive and navigates in it.
    /// </summary>
    public void Rebase(AccessibilityNode node, int generation)
    {
        Volatile.Write(ref _node, node);
        Generation = generation;
    }

    /// <summary>Takes a focus move, which rebuilds no tree (UI thread). True when the state changed.</summary>
    public bool SetFocused(bool focused)
    {
        if (_isFocused == focused) return false;
        _isFocused = focused;
        return true;
    }

    private void Snapshot(AccessibilityNode node)
    {
        _name = node.Name;
        _description = node.Description;
        _value = node.Value;
        _automationId = node.AutomationId;
        _role = node.Role;
        _isFocusable = node.IsFocusable;
        _isFocused = node.IsFocused;
        _isEnabled = node.IsEnabled;
        _isToggled = node.IsToggled;
        _expanded = node.Expanded;
        _isPassword = node.IsPassword;
        _isSelected = node.IsSelected;
        _hasText = node.TextSource is not null || node.Text is not null;
    }

    /// <summary>The text now, for the text pattern: the node's source, read on this thread.</summary>
    public AccessibleText? Text => Node.CurrentText;

    /// <summary>The node's box in layout units, scroll applied. Read from the node, not kept: a
    /// scroll moves every node under the view and names none of them in the diff, and the
    /// bridge points every wrapper at its node in the current tree after each diff.</summary>
    public Rect Bounds => Node.BoundingRect;

    // ── IRawElementProviderSimple ──

    public int GetProviderOptions(out int pRetVal)
    {
        // A node that stands for a child window is that window's override provider, and must
        // say so: without the flag UIA takes it for the window's main provider, asks the root
        // for the override again, and never stops.
        pRetVal = Node.NativeContent.IsEmpty
            ? UiaInterop.ProviderOptions_ServerSideProvider
            : UiaInterop.ProviderOptions_ServerSideProvider | UiaInterop.ProviderOptions_OverrideProvider;
        return 0;
    }

    public int GetPatternProvider(int patternId, out IntPtr pPatternProvider)
    {
        object? provider = _role switch
        {
            // Text a reader moves through (seam A1), whatever the role: a terminal's document,
            // and later edit fields.
            _ when patternId is UiaInterop.UIA_TextPatternId or UiaInterop.UIA_TextPattern2Id && _hasText
                => new UiaTextProvider(_bridge, ElementId),
            AccessibilityRole.Button or AccessibilityRole.ColumnHeader when patternId == UiaInterop.UIA_InvokePatternId
                => new UiaInvokeProvider(_bridge, ElementId),
            // List items are invokable so assistive tech can activate any row —
            // including virtualized ones whose id routes back to the owning list —
            // and expose selection so it can report/move to the selected row.
            AccessibilityRole.ListItem when patternId == UiaInterop.UIA_InvokePatternId
                => new UiaInvokeProvider(_bridge, ElementId),
            AccessibilityRole.ListItem when patternId == UiaInterop.UIA_SelectionItemPatternId
                => new UiaSelectionItemProvider(_bridge, ElementId),
            // Any node carrying a toggle state offers the pattern, whatever its role — a
            // menu item marking the current choice is a ListItem with a state, not a
            // checkbox, and a client that asks by pattern would otherwise be told no.
            _ when patternId == UiaInterop.UIA_TogglePatternId && _isToggled is not null
                => new UiaToggleProvider(_bridge, ElementId),
            AccessibilityRole.RadioButton when patternId == UiaInterop.UIA_SelectionItemPatternId
                => new UiaSelectionItemProvider(_bridge, ElementId),
            // Anything else that is one of a set — a tab — says so with a selected state.
            _ when patternId == UiaInterop.UIA_SelectionItemPatternId && _isSelected is not null
                => new UiaSelectionItemProvider(_bridge, ElementId),
            AccessibilityRole.TextInput when patternId == UiaInterop.UIA_ValuePatternId
                => new UiaValueProvider(_bridge, ElementId),
            // A document with a value (a terminal's screen) offers it read-only: the reader
            // reads it, and nothing writes it.
            AccessibilityRole.Document when patternId == UiaInterop.UIA_ValuePatternId && _value is not null
                => new UiaValueProvider(_bridge, ElementId, readOnly: true),
            // A progress bar exposes the same pattern read-only; the bridge refuses SetValue.
            (AccessibilityRole.Slider or AccessibilityRole.ProgressBar or AccessibilityRole.SpinButton) when patternId == UiaInterop.UIA_RangeValuePatternId
                => new UiaRangeValueProvider(_bridge, ElementId),
            // Scroll views and (scrollable) lists expose the Scroll pattern.
            (AccessibilityRole.ScrollView or AccessibilityRole.List) when patternId == UiaInterop.UIA_ScrollPatternId
                => new UiaScrollProvider(_bridge, ElementId),
            // A list is a selection container and supports finding items by property
            // (both valuable for large virtualized lists — no per-item walk needed).
            AccessibilityRole.List when patternId == UiaInterop.UIA_SelectionPatternId
                => new UiaSelectionProvider(_bridge, ElementId),
            AccessibilityRole.List when patternId == UiaInterop.UIA_ItemContainerPatternId
                => new UiaItemContainerProvider(_bridge, ElementId),
            // Any expandable element (regardless of role) exposes ExpandCollapse.
            _ when patternId == UiaInterop.UIA_ExpandCollapsePatternId && _expanded is not null
                => new UiaExpandCollapseProvider(_bridge, ElementId),
            _ => null,
        };

        pPatternProvider = provider is null ? IntPtr.Zero : NativeProvider.Create(provider);
        return 0;
    }

    public int GetPropertyValue(int propertyId, out UiaVariant pRetVal)
    {
        // A node that stands for a child window answers together with that window's own
        // providers. Left to them, a web view's node was not a control: the control view
        // dropped it, and a hit test on it came back as its parent. So the node says it is.
        if (propertyId is UiaInterop.UIA_IsControlElementPropertyId or UiaInterop.UIA_IsContentElementPropertyId
            && !Node.NativeContent.IsEmpty)
        {
            pRetVal = UiaVariant.FromBool(true);
            return 0;
        }

        pRetVal = propertyId switch
        {
            UiaInterop.UIA_NamePropertyId when _name is not null => UiaVariant.FromBStr(_name),
            UiaInterop.UIA_AutomationIdPropertyId when _automationId is not null => UiaVariant.FromBStr(_automationId),
            UiaInterop.UIA_HelpTextPropertyId when _description is not null => UiaVariant.FromBStr(_description),
            UiaInterop.UIA_ControlTypePropertyId => UiaVariant.FromInt(MapControlType(_role)),
            UiaInterop.UIA_IsKeyboardFocusablePropertyId => UiaVariant.FromBool(_isFocusable),
            UiaInterop.UIA_HasKeyboardFocusPropertyId => UiaVariant.FromBool(_isFocused),
            UiaInterop.UIA_IsEnabledPropertyId => UiaVariant.FromBool(_isEnabled),
            UiaInterop.UIA_IsOffscreenPropertyId => UiaVariant.FromBool(Node.IsOffscreen),
            UiaInterop.UIA_IsPasswordPropertyId => UiaVariant.FromBool(_isPassword),
            UiaInterop.UIA_SelectionItemIsSelectedPropertyId when _isSelected is { } selected
                => UiaVariant.FromBool(selected),
            UiaInterop.UIA_ValueValuePropertyId when _value is not null => UiaVariant.FromBStr(_value),
            UiaInterop.UIA_ToggleToggleStatePropertyId when _isToggled is { } t
                => UiaVariant.FromInt(t ? UiaInterop.ToggleState_On : UiaInterop.ToggleState_Off),
            UiaInterop.UIA_ExpandCollapseExpandCollapseStatePropertyId when _expanded is { } e
                => UiaVariant.FromInt(e ? UiaInterop.ExpandCollapseState_Expanded : UiaInterop.ExpandCollapseState_Collapsed),
            _ => UiaVariant.Empty,
        };
        return 0;
    }

    public int GetHostRawElementProvider(out IntPtr pRetVal)
    {
        // Host chaining lives on the fragment root only; element fragments must
        // return null or UIA double-walks to the HWND host. The exception is a node that
        // stands for a child window (provider repositioning): naming that window's host
        // provider couples this node with it, so the window's own tree becomes this node's
        // children, and the root's HWND override keeps the window from appearing twice.
        var window = Node.NativeContent.Window;
        pRetVal = IntPtr.Zero;
        if (window != IntPtr.Zero && UiaInterop.UiaHostProviderFromHwnd(window, out var host) >= 0)
            pRetVal = host;
        return 0;
    }

    // ── IRawElementProviderFragment ──

    public int Navigate(int direction, out IntPtr pRetVal)
    {
        // Snapshot Node once: the UI thread may swap it after a diff while this
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
        var screen = _bridge.ToScreen(Bounds);
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
        AccessibilityRole.Tree => UiaInterop.UIA_TreeControlTypeId,
        AccessibilityRole.TreeItem => UiaInterop.UIA_TreeItemControlTypeId,
        AccessibilityRole.ScrollView => UiaInterop.UIA_PaneControlTypeId,
        AccessibilityRole.Group => UiaInterop.UIA_GroupControlTypeId,
        // One tab. UIA's Tab control type is the strip of them (W3C Core-AAM, as Chromium maps it).
        AccessibilityRole.Tab => UiaInterop.UIA_TabItemControlTypeId,
        AccessibilityRole.TabList => UiaInterop.UIA_TabControlTypeId,
        AccessibilityRole.Separator => UiaInterop.UIA_SeparatorControlTypeId,
        AccessibilityRole.Application => UiaInterop.UIA_WindowControlTypeId,
        AccessibilityRole.Link => UiaInterop.UIA_HyperlinkControlTypeId,
        AccessibilityRole.Slider => UiaInterop.UIA_SliderControlTypeId,
        AccessibilityRole.ProgressBar => UiaInterop.UIA_ProgressBarControlTypeId,
        AccessibilityRole.SpinButton => UiaInterop.UIA_SpinnerControlTypeId,
        AccessibilityRole.ComboBox => UiaInterop.UIA_ComboBoxControlTypeId,
        AccessibilityRole.MenuBar => UiaInterop.UIA_MenuBarControlTypeId,
        AccessibilityRole.MenuItem => UiaInterop.UIA_MenuItemControlTypeId,
        AccessibilityRole.ColumnHeader => UiaInterop.UIA_HeaderItemControlTypeId,
        // Toggle switches read as check boxes (with toggle state) to UIA.
        AccessibilityRole.Switch => UiaInterop.UIA_CheckBoxControlTypeId,
        // No dedicated UIA control type: headings/alerts are text, dialogs are
        // windows, and a tab panel is a plain container.
        AccessibilityRole.Heading => UiaInterop.UIA_TextControlTypeId,
        AccessibilityRole.Alert => UiaInterop.UIA_TextControlTypeId,
        AccessibilityRole.Dialog => UiaInterop.UIA_WindowControlTypeId,
        AccessibilityRole.TabPanel => UiaInterop.UIA_PaneControlTypeId,
        AccessibilityRole.Document => UiaInterop.UIA_DocumentControlTypeId,
        _ => UiaInterop.UIA_PaneControlTypeId,
    };
}
