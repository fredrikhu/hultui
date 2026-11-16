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
/// Property values the diff reports changes of are snapshot-copied here (Name, Value, the
/// states) and refreshed when it names the node. The role and the automation id are read
/// from <see cref="Node"/>: the differ compares neither, so a copy taken when the wrapper was
/// made answered for good — a group that became a dialog stayed a group. The node is the
/// current tree's (the bridge moves every wrapper there after each diff), never changes once
/// applied but for what a layout refresh writes in place (its box, its offscreen state, a scroll
/// view's fields, each read whole), and is no element, so reading it from a UIA worker thread is
/// safe.
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
    private bool _isFocusable;
    private bool _isEnabled;
    private bool? _isToggled;
    private bool? _expanded;
    private bool _isPassword;
    private bool? _isSelected;
    private bool _hasText;

    /// <summary>Snapshot expand state (null = not expandable), read by the ExpandCollapse provider.</summary>
    public bool? Expanded => _expanded;

    /// <summary>Snapshot selected state (null = not selectable that way), read by the
    /// SelectionItem provider.</summary>
    public bool? Selected => _isSelected;

    /// <summary>Snapshot toggle state (null = not a toggle), read by the Toggle provider, as the
    /// ToggleState property is.</summary>
    public bool? Toggled => _isToggled;

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

    private void Snapshot(AccessibilityNode node)
    {
        _name = node.Name;
        _description = node.Description;
        // A node that offers text has its value read on request (GetPropertyValue): a long
        // document's is made only when asked, and its changes are told as text changes.
        _value = node.TextSource is null ? node.Value : null;
        _isFocusable = node.IsFocusable;
        _isEnabled = node.IsEnabled;
        _isToggled = node.IsToggled;
        _expanded = node.Expanded;
        _isPassword = node.IsPassword;
        _isSelected = node.IsSelected;
        _hasText = node.TextSource is not null || node.Text is not null;
    }

    /// <summary>Whether the node offers text (UIA's text pattern): its value is then the node's,
    /// read on request (<see cref="GetPropertyValue"/>).</summary>
    public bool OffersText => _hasText;

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
        object? provider = Node.Role switch
        {
            // Text a reader moves through (seam A1), whatever the role: a terminal's document,
            // and later edit fields.
            _ when patternId is UiaInterop.UIA_TextPatternId or UiaInterop.UIA_TextPattern2Id && _hasText
                => new UiaTextProvider(_bridge, ElementId),
            AccessibilityRole.Button or AccessibilityRole.ColumnHeader or AccessibilityRole.Link
                when patternId == UiaInterop.UIA_InvokePatternId
                => new UiaInvokeProvider(_bridge, ElementId),
            // List items are invokable so assistive tech can activate any row —
            // including virtualized ones whose id routes back to the owning list —
            // and expose selection so it can report/move to the selected row.
            AccessibilityRole.ListItem when patternId == UiaInterop.UIA_InvokePatternId
                => new UiaInvokeProvider(_bridge, ElementId),
            // A menu's entry runs its command; one that opens a menu of its own (a menu bar's
            // title) expands instead.
            AccessibilityRole.MenuItem when patternId == UiaInterop.UIA_InvokePatternId && _expanded is null
                => new UiaInvokeProvider(_bridge, ElementId),
            AccessibilityRole.ListItem when patternId == UiaInterop.UIA_SelectionItemPatternId
                => new UiaSelectionItemProvider(_bridge, ElementId),
            // A menu's entry is no SelectionItem, as a Win32 or WinUI menu's is not: a reader said
            // "selected" on every entry it walked. The entry the keyboard is on has the focus instead.
            AccessibilityRole.MenuItem when patternId == UiaInterop.UIA_SelectionItemPatternId => null,
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
            AccessibilityRole.Document when patternId == UiaInterop.UIA_ValuePatternId && (_value is not null || _hasText)
                => new UiaValueProvider(_bridge, ElementId, readOnly: true),
            // A combo box whose name says what it is for (a dropdown the app named) gives its choice
            // as its value, read-only as a Win32 drop-down list's is: the reader reads the name, the
            // role, then the choice. One named by its choice (a dropdown the app did not name, a date
            // picker) offers none, or the reader would read the choice twice.
            AccessibilityRole.ComboBox when patternId == UiaInterop.UIA_ValuePatternId
                && _value is not null && _value != Node.Name
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
            // A menu that publishes its items (a context menu, a menu bar's menu) is their
            // selection container as a list is, with the same three patterns.
            AccessibilityRole.Menu when patternId == UiaInterop.UIA_ScrollPatternId && Node.ItemSource is not null
                => new UiaScrollProvider(_bridge, ElementId),
            AccessibilityRole.Menu when patternId == UiaInterop.UIA_ItemContainerPatternId && Node.ItemSource is not null
                => new UiaItemContainerProvider(_bridge, ElementId),
            // Any expandable element (regardless of role) exposes ExpandCollapse.
            _ when patternId == UiaInterop.UIA_ExpandCollapsePatternId && _expanded is not null
                => new UiaExpandCollapseProvider(_bridge, ElementId),
            // Anything a scroll view or a list holds can be scrolled into view.
            _ when patternId == UiaInterop.UIA_ScrollItemPatternId && InScrollable(Node)
                => new UiaScrollItemProvider(_bridge, ElementId),
            _ => null,
        };

        pPatternProvider = provider is null ? IntPtr.Zero : NativeProvider.Create(provider);
        return 0;
    }

    public int GetPropertyValue(int propertyId, out UiaVariant pRetVal)
    {
        // One read of the node per call: the UI thread may move the wrapper to a newer tree meanwhile.
        var node = Node;

        // A node that stands for a child window answers together with that window's own
        // providers. Left to them, a web view's node was not a control: the control view
        // dropped it, and a hit test on it came back as its parent. So the node says it is.
        if (propertyId is UiaInterop.UIA_IsControlElementPropertyId or UiaInterop.UIA_IsContentElementPropertyId
            && !node.NativeContent.IsEmpty)
        {
            pRetVal = UiaVariant.FromBool(true);
            return 0;
        }

        // The value of a node that offers text, read on request: the node's, as the tree was built
        // with it — the version of the text its text events describe, which a long document makes
        // on the first ask, on any thread (AccessibilityNode.Value). It was read from the element
        // through the window: a walk of the live tree on this thread while the UI thread may be
        // changing it, then the document as it is now rather than as last told.
        if (propertyId == UiaInterop.UIA_ValueValuePropertyId && _value is null && _hasText)
        {
            pRetVal = node.Value is { } value ? UiaVariant.FromBStr(value) : UiaVariant.Empty;
            return 0;
        }

        pRetVal = propertyId switch
        {
            UiaInterop.UIA_NamePropertyId when _name is not null => UiaVariant.FromBStr(_name),
            UiaInterop.UIA_AutomationIdPropertyId when node.AutomationId is { } automationId => UiaVariant.FromBStr(automationId),
            UiaInterop.UIA_HelpTextPropertyId when _description is not null => UiaVariant.FromBStr(_description),
            UiaInterop.UIA_ControlTypePropertyId => UiaVariant.FromInt(MapControlType(node.Role)),
            // A list's or a menu's item can have the reader's focus (its container's current item), as
            // a WPF list box's item can; and so can whatever has it now (a menu bar's title).
            // Not the node that holds the focus while an item of its own stands for it: the items' container
            // is then no focus stop of its own, as Vesper's file list and a context menu are not, which a
            // reader walks item by item. A focusable list or menu bar holding the focus had Narrator say
            // its name again on every move ("History list", "Menu bar menu bar").
            UiaInterop.UIA_IsKeyboardFocusablePropertyId
                => UiaVariant.FromBool(_isFocusable && !_bridge.StandsForItem(ElementId)
                    || VirtualItemId.IsItem(ElementId) || node.Parent?.TracksCurrentItem == true || _bridge.HasReaderFocus(ElementId)),
            // The reader's focus, as the bridge holds it: the node that holds the focus, or its current
            // item while it has one, and then not the holder (AccessibilityFocus). A focus move rebuilds
            // no tree, so a node's own state was the last rebuild's until the window marked it.
            UiaInterop.UIA_HasKeyboardFocusPropertyId => UiaVariant.FromBool(_bridge.HasReaderFocus(ElementId)),
            UiaInterop.UIA_IsEnabledPropertyId => UiaVariant.FromBool(_isEnabled),
            UiaInterop.UIA_IsOffscreenPropertyId => UiaVariant.FromBool(Node.IsOffscreen),
            UiaInterop.UIA_IsPasswordPropertyId => UiaVariant.FromBool(_isPassword),
            UiaInterop.UIA_PositionInSetPropertyId when PlaceInSet(node) is var (at, _) && at > 0 => UiaVariant.FromInt(at),
            UiaInterop.UIA_SizeOfSetPropertyId when PlaceInSet(node) is var (at, of) && at > 0 => UiaVariant.FromInt(of),
            UiaInterop.UIA_SelectionItemIsSelectedPropertyId when _isSelected is { } selected && OffersSelection(node)
                => UiaVariant.FromBool(selected),
            UiaInterop.UIA_ValueValuePropertyId when _value is not null => UiaVariant.FromBStr(_value),
            // A range as the tree was built with it, which is what its change events carry.
            UiaInterop.UIA_RangeValueValuePropertyId when node.Range is { } r => UiaVariant.FromDouble(r.Value),
            UiaInterop.UIA_RangeValueMinimumPropertyId when node.Range is { } r => UiaVariant.FromDouble(r.Minimum),
            UiaInterop.UIA_RangeValueMaximumPropertyId when node.Range is { } r => UiaVariant.FromDouble(r.Maximum),
            UiaInterop.UIA_RangeValueSmallChangePropertyId when node.Range is { } r => UiaVariant.FromDouble(r.SmallChange),
            UiaInterop.UIA_RangeValueLargeChangePropertyId when node.Range is { } r => UiaVariant.FromDouble(r.LargeChange),
            UiaInterop.UIA_RangeValueIsReadOnlyPropertyId when node.Range is { } r => UiaVariant.FromBool(r.IsReadOnly),
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

    /// <summary>
    /// Where an item is in its set, from 1, and how many the set has; (0, 0) for a node that is in none.
    /// A list's, a menu's or a menu bar's items are its children (its choices: a menu's separators are
    /// none of them), an item node's place read from its id. Any other list item, menu item, tree item,
    /// tab or radio button is one of its parent's children with its role: a set of elements that has no
    /// container of its own (the Fluent sample's navigation, five list items among the window's other
    /// children), whose size UI Automation worked out otherwise, and not always the same (6, 5, then 7
    /// for those five). On the client's thread, from the node; a scan of the parent's children for the
    /// second kind.
    /// </summary>
    internal static (int At, int Of) PlaceInSet(AccessibilityNode node)
    {
        if (node.Parent is not { } parent) return default;
        var siblings = parent.Children;
        if (parent.TracksCurrentItem)
        {
            int at = VirtualItemId.IsItem(node.ElementId) ? (int)(uint)(node.ElementId.Value & 0xFFFF_FFFFUL) : IndexOf(siblings, node);
            return at >= 0 && at < siblings.Count ? (at + 1, siblings.Count) : default;
        }
        if (node.Role is not (AccessibilityRole.ListItem or AccessibilityRole.MenuItem or AccessibilityRole.TreeItem
            or AccessibilityRole.Tab or AccessibilityRole.RadioButton))
            return default;
        int position = 0, count = 0;
        for (int i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].Role != node.Role) continue;
            count++;
            if (siblings[i].ElementId == node.ElementId) position = count;
        }
        return position > 0 ? (position, count) : default;
    }

    private static int IndexOf(IReadOnlyList<AccessibilityNode> nodes, AccessibilityNode node)
    {
        for (int i = 0; i < nodes.Count; i++)
            if (nodes[i].ElementId == node.ElementId) return i;
        return -1;
    }

    /// <summary>Whether a node with a selected state offers it to UIA (SelectionItem, IsSelected): not a
    /// menu's entry, whose highlight is the focus moving to it.</summary>
    internal static bool OffersSelection(AccessibilityNode node) => node.Role != AccessibilityRole.MenuItem;

    private static bool InScrollable(AccessibilityNode node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            if (parent.Role is AccessibilityRole.ScrollView or AccessibilityRole.List) return true;
        return false;
    }

    internal static int MapControlType(AccessibilityRole role) => role switch
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
        AccessibilityRole.Menu => UiaInterop.UIA_MenuControlTypeId,
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
