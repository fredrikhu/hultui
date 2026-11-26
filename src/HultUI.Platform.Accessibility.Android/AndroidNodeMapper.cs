using HultUI.Core.Accessibility;

namespace HultUI.Platform.Accessibility.Android;

/// <summary>
/// One node of HultUI's tree as Android's model has it (docs/design/TALKBACK.md §3): its widget
/// class, what it says, its states and the actions it offers. Where it is and which ids it and its
/// children have are the bridge's to say (<see cref="AndroidAccessibilityBridge"/>); everything here
/// reads the node alone, and its parent for a list item's place.
/// </summary>
internal static class AndroidNodeMapper
{
    /// <summary>
    /// Whether the node is a virtual view of its own. Everything is but a scrollbar that stands on
    /// its own: on a phone the scroll view offers the scroll actions, and a bar TalkBack stopped on
    /// would read as a second slider. A scrollbar has no children, so nothing is lost under it.
    /// </summary>
    /// <remarks>
    /// Nor, as Android leaves out what is not important for accessibility:
    /// <list type="bullet">
    /// <item>an image with no name: a decorative icon, which TalkBack read as "Unlabelled. Image" beside
    /// every named button that drew one (seen in the Fluent sample);</item>
    /// <item>anything inside a control that has a name, but a control: the name covers what is drawn
    /// inside (ARIA's presentational children), and TalkBack read a named node's label children after
    /// its name ("View. Menu item. View"). A control inside, a check box in a list item, stays a stop of
    /// its own, its hidden containers' place taken by it.</item>
    /// </list>
    /// A node left out has no virtual view; its shown descendants are its parent's children.
    /// </remarks>
    public static bool IsExposed(AccessibilityNode node)
    {
        if (node.Role == AccessibilityRole.ScrollBar) return false;
        if (node.Role == AccessibilityRole.Image && string.IsNullOrEmpty(node.Name) && string.IsNullOrEmpty(node.Description))
            return false;
        if (!IsControl(node.Role))
            for (var above = node.Parent; above is not null; above = above.Parent)
                if (CoversContent(above)) return false;
        return true;
    }

    /// <summary>A control with a name, which says what is drawn inside it.</summary>
    public static bool CoversContent(AccessibilityNode node) =>
        IsControl(node.Role) && node.Role != AccessibilityRole.TextInput && !string.IsNullOrEmpty(node.Name);

    /// <param name="node">The node, of the tree the bridge last took.</param>
    /// <param name="id">Its virtual id.</param>
    /// <param name="parentId">Its parent's, or <see cref="AndroidIds.Host"/>.</param>
    /// <param name="children">Its exposed children's ids, in order.</param>
    /// <param name="bounds">Its box on screen in device pixels, clipped; empty when none of it shows.</param>
    /// <param name="accessibilityFocused">Whether TalkBack's focus is on it.</param>
    public static AndroidNode Map(AccessibilityNode node, int id, int parentId, int[] children, PixelBox bounds,
        bool accessibilityFocused)
    {
        var role = node.Role;
        bool enabled = node.IsEnabled;
        bool visible = !bounds.IsEmpty && !node.IsOffscreen;
        var (text, contentDescription, hint, paneTitle, containerTitle) = Words(node);

        // Focusable only for a control: TalkBack makes a focusable node one stop and reads the labels
        // inside it as part of it, so a container that takes the keys (a scroll view, a list) would
        // swallow every label in it.
        bool focusable = node.IsFocusable && IsControl(role);
        var flags = AndroidNodeFlags.None;
        if (enabled) flags |= AndroidNodeFlags.Enabled;
        if (focusable) flags |= AndroidNodeFlags.Focusable;
        if (focusable && node.IsFocused) flags |= AndroidNodeFlags.Focused;
        if (accessibilityFocused) flags |= AndroidNodeFlags.AccessibilityFocused;
        if (visible) flags |= AndroidNodeFlags.VisibleToUser;
        if (node.IsPassword) flags |= AndroidNodeFlags.Password;
        if (role == AccessibilityRole.Heading) flags |= AndroidNodeFlags.Heading;
        if (node.IsSelected == true) flags |= AndroidNodeFlags.Selected;
        bool clickable = enabled && IsClickable(node);
        if (clickable) flags |= AndroidNodeFlags.Clickable;
        if (IsCheckable(node))
        {
            flags |= AndroidNodeFlags.Checkable;
            if (IsChecked(node)) flags |= AndroidNodeFlags.Checked;
        }

        var actions = (accessibilityFocused ? AndroidActions.ClearAccessibilityFocus : AndroidActions.AccessibilityFocus)
            | AndroidActions.ShowOnScreen;
        if (clickable) actions |= AndroidActions.Click;
        if (enabled && focusable) actions |= node.IsFocused ? AndroidActions.ClearFocus : AndroidActions.Focus;
        if (enabled && node.Expanded is { } expanded) actions |= expanded ? AndroidActions.Collapse : AndroidActions.Expand;
        if (enabled && node.IsSelected == false && role is AccessibilityRole.ListItem or AccessibilityRole.Tab
                or AccessibilityRole.TreeItem or AccessibilityRole.RadioButton)
            actions |= AndroidActions.Select;

        AndroidRange? range = null;
        string? state = null;
        if (node.Range is { } r)
        {
            range = new AndroidRange(IsWhole(r) ? AndroidRangeType.Int : AndroidRangeType.Float,
                (float)r.Minimum, (float)r.Maximum, (float)r.Value);
            // The element's own words for its value ("50 %", "3 of 10"); TalkBack speaks the range else.
            if (!string.IsNullOrEmpty(node.Value)) state = node.Value;
            if (enabled && !r.IsReadOnly)
            {
                actions |= AndroidActions.SetProgress;
                if (r.Value < r.Maximum) actions |= AndroidActions.ScrollForward;
                if (r.Value > r.Minimum) actions |= AndroidActions.ScrollBackward;
            }
        }

        if (node.ScrollExtent is { } extent)
        {
            var (up, down, left, right) = ScrollRoom(node, extent);
            if (up || down || left || right) flags |= AndroidNodeFlags.Scrollable;
            if (down || right) actions |= AndroidActions.ScrollForward;
            if (up || left) actions |= AndroidActions.ScrollBackward;
            if (up) actions |= AndroidActions.ScrollUp;
            if (down) actions |= AndroidActions.ScrollDown;
            if (left) actions |= AndroidActions.ScrollLeft;
            if (right) actions |= AndroidActions.ScrollRight;
        }

        int selectionStart = -1, selectionEnd = -1;
        if (role == AccessibilityRole.TextInput)
        {
            var current = node.CurrentText;
            if (enabled && !(current?.IsReadOnly ?? false))
            {
                flags |= AndroidNodeFlags.Editable;
                actions |= AndroidActions.SetText;
            }
            if (current is not null)
            {
                if (current.LineCount > 1 || current.Text.Contains('\n')) flags |= AndroidNodeFlags.MultiLine;
                (selectionStart, selectionEnd) = current.HasSelection
                    ? (current.SelectionStart, current.SelectionEnd)
                    : (current.Caret, current.Caret);
            }
        }

        return new AndroidNode
        {
            Id = id,
            ParentId = parentId,
            Children = children,
            ClassName = ClassOf(node),
            RoleDescription = RoleDescriptionOf(role),
            Text = text,
            ContentDescription = contentDescription,
            HintText = hint,
            StateDescription = state,
            PaneTitle = paneTitle,
            ContainerTitle = containerTitle,
            Expanded = role == AccessibilityRole.ComboBox ? null : node.Expanded,
            Flags = flags,
            BoundsInScreen = bounds,
            Actions = actions,
            Range = range,
            Collection = CollectionOf(node),
            CollectionItem = CollectionItemOf(node),
            SelectionStart = selectionStart,
            SelectionEnd = selectionEnd,
            LiveRegion = role == AccessibilityRole.Alert ? AndroidLiveRegion.Assertive : AndroidLiveRegion.None,
        };
    }

    /// <summary>The widget class TalkBack reads the role from.</summary>
    public static string ClassOf(AccessibilityNode node) => node.Role switch
    {
        AccessibilityRole.Button => node.IsToggled is null ? AndroidClasses.Button : AndroidClasses.ToggleButton,
        AccessibilityRole.TextInput => AndroidClasses.EditText,
        AccessibilityRole.Label or AccessibilityRole.Heading => AndroidClasses.TextView,
        AccessibilityRole.Image => AndroidClasses.ImageView,
        AccessibilityRole.List or AccessibilityRole.Tree or AccessibilityRole.Menu => AndroidClasses.ListView,
        AccessibilityRole.ScrollView => IsSideways(node) ? AndroidClasses.HorizontalScrollView : AndroidClasses.ScrollView,
        AccessibilityRole.Slider => AndroidClasses.SeekBar,
        AccessibilityRole.Checkbox => AndroidClasses.CheckBox,
        AccessibilityRole.RadioButton => AndroidClasses.RadioButton,
        AccessibilityRole.Switch => AndroidClasses.Switch,
        AccessibilityRole.TabList => AndroidClasses.TabWidget,
        AccessibilityRole.ComboBox => AndroidClasses.Spinner,
        AccessibilityRole.ProgressBar => AndroidClasses.ProgressBar,
        _ => AndroidClasses.View,
    };

    /// <summary>
    /// The role, spoken, for the roles no widget class names: as Compose names a tab. English, as
    /// HultUI's other spoken words are for now; TalkBack's own role words (button, check box) are
    /// its own and follow the phone's language.
    /// </summary>
    public static string? RoleDescriptionOf(AccessibilityRole role) => role switch
    {
        AccessibilityRole.Link => "Link",
        AccessibilityRole.Tab => "Tab",
        AccessibilityRole.MenuItem => "Menu item",
        AccessibilityRole.Menu => "Menu",
        AccessibilityRole.MenuBar => "Menu bar",
        AccessibilityRole.TreeItem => "Tree item",
        AccessibilityRole.ColumnHeader => "Column header",
        AccessibilityRole.SpinButton => "Spin button",
        _ => null,
    };

    /// <summary>
    /// What the node says, and where. A control's name is its text, as a button's label is; an
    /// image's is its content description; a field's text is what is typed and its name the hint; a
    /// slider's name is its content description, its value TalkBack's to speak; a container's name
    /// is its title (a dialog's pane title), never text, which would make the container a stop of
    /// its own in front of its children.
    /// </summary>
    private static (string? Text, string? ContentDescription, string? Hint, string? PaneTitle, string? ContainerTitle) Words(AccessibilityNode node)
    {
        string? name = Nonempty(node.Name), value = Nonempty(node.Value), description = Nonempty(node.Description);
        switch (node.Role)
        {
            case AccessibilityRole.Image:
                return (null, name, description, null, null);
            case AccessibilityRole.TextInput:
            {
                string? text = node.IsPassword ? value : Nonempty(node.CurrentText?.Text) ?? value;
                return (text, null, Join(name, description), null, null);
            }
            case AccessibilityRole.Slider or AccessibilityRole.ProgressBar or AccessibilityRole.SpinButton
                or AccessibilityRole.ScrollBar:
                return (null, name, description, null, null);
            case AccessibilityRole.ComboBox:
                // The choice is what it shows; its name says what it is for.
                return value is null || value == name
                    ? (name, null, description, null, null)
                    : (value, null, Join(name, description), null, null);
            case AccessibilityRole.Dialog:
                return (null, null, description, name, null);
            case AccessibilityRole.List or AccessibilityRole.Tree or AccessibilityRole.Menu or AccessibilityRole.MenuBar
                or AccessibilityRole.TabList or AccessibilityRole.TabPanel or AccessibilityRole.Group
                or AccessibilityRole.ScrollView or AccessibilityRole.Document or AccessibilityRole.Application:
                return (null, null, description, null, name);
            default:
                return (name ?? value, null, description, null, null);
        }
    }

    private static string? Nonempty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static string? Join(string? a, string? b) =>
        a is null ? b : b is null || b == a ? a : a + ", " + b;

    /// <summary>The roles a double tap activates.</summary>
    public static bool IsClickable(AccessibilityNode node) => IsClickableRole(node.Role);

    /// <summary>The roles that are one stop for TalkBack, with what they say: a double tap's, a field,
    /// a slider.</summary>
    public static bool IsControl(AccessibilityRole role) => role is AccessibilityRole.TextInput
        or AccessibilityRole.Slider or AccessibilityRole.SpinButton || IsClickableRole(role);

    private static bool IsClickableRole(AccessibilityRole role) => role is AccessibilityRole.Button or AccessibilityRole.Link
        or AccessibilityRole.Checkbox or AccessibilityRole.RadioButton or AccessibilityRole.Switch or AccessibilityRole.Tab
        or AccessibilityRole.ListItem or AccessibilityRole.TreeItem or AccessibilityRole.MenuItem
        or AccessibilityRole.ColumnHeader or AccessibilityRole.ComboBox;

    /// <summary>A check box, a switch, a radio button, a button that toggles.</summary>
    public static bool IsCheckable(AccessibilityNode node) => node.Role switch
    {
        AccessibilityRole.Checkbox or AccessibilityRole.Switch or AccessibilityRole.RadioButton => true,
        AccessibilityRole.Button => node.IsToggled is not null,
        _ => false,
    };

    /// <summary>Checked: toggled on, or a radio button that is the selected one of its group.</summary>
    public static bool IsChecked(AccessibilityNode node) =>
        node.Role == AccessibilityRole.RadioButton ? node.IsSelected == true || node.IsToggled == true : node.IsToggled == true;

    private static bool IsWhole(AccessibleRange r) =>
        IsWhole(r.Minimum) && IsWhole(r.Maximum) && IsWhole(r.Value) && (r.SmallChange <= 0 || IsWhole(r.SmallChange));

    private static bool IsWhole(double v) => Math.Abs(v - Math.Round(v)) < 1e-9;

    private static bool IsSideways(AccessibilityNode node) =>
        node.ScrollExtent is { } e && e.Content.Width > e.Viewport.Width + 0.5f && e.Content.Height <= e.Viewport.Height + 0.5f;

    /// <summary>Which ways the scroll view can still move: room above, below, left, right.</summary>
    public static (bool Up, bool Down, bool Left, bool Right) ScrollRoom(AccessibilityNode node, AccessibleScrollExtent extent)
    {
        var offset = node.ScrollOffset;
        float maxY = extent.Content.Height - extent.Viewport.Height;
        float maxX = extent.Content.Width - extent.Viewport.Width;
        return (offset.Y > 0.5f, offset.Y < maxY - 0.5f, offset.X > 0.5f, offset.X < maxX - 0.5f);
    }

    private static bool IsItemRole(AccessibilityRole role) => role is AccessibilityRole.ListItem or AccessibilityRole.TreeItem
        or AccessibilityRole.MenuItem or AccessibilityRole.Tab;

    /// <summary>A list's, a tree's, a menu's or a tab strip's items, counted: TalkBack says "in list,
    /// 12 items" as it enters one.</summary>
    public static AndroidCollection? CollectionOf(AccessibilityNode node)
    {
        if (node.Role is not (AccessibilityRole.List or AccessibilityRole.Tree or AccessibilityRole.Menu or AccessibilityRole.TabList))
            return null;
        int count = 0;
        bool selectable = false;
        if (node.IsVirtualItemContainer) count = node.Served.Count;
        else
        {
            foreach (var child in node.Children)
            {
                if (!IsItemRole(child.Role)) continue;
                count++;
                selectable |= child.IsSelected is not null;
            }
        }
        bool multiple = false;
        try { multiple = node.ItemSource?.CanSelectMultiple ?? false; }
        catch { /* the live list, read on Android's thread */ }
        var mode = multiple ? AndroidSelectionMode.Multiple
            : selectable || node.IsVirtualItemContainer ? AndroidSelectionMode.Single : AndroidSelectionMode.None;
        return node.Role == AccessibilityRole.TabList
            ? new AndroidCollection(1, count, false, mode)
            : new AndroidCollection(count, 1, node.Role == AccessibilityRole.Tree, mode);
    }

    /// <summary>An item's place in its list (row) or its tab strip (column).</summary>
    public static AndroidCollectionItem? CollectionItemOf(AccessibilityNode node)
    {
        bool header = node.Role == AccessibilityRole.ColumnHeader;
        if (!IsItemRole(node.Role) && !header) return null;
        if (node.Parent is not { } parent) return null;
        int index = 0;
        foreach (var sibling in parent.Children)
        {
            if (ReferenceEquals(sibling, node)) break;
            if (sibling.Role == node.Role) index++;
        }
        bool across = parent.Role == AccessibilityRole.TabList || header;
        return new AndroidCollectionItem(across ? 0 : index, across ? index : 0, header, node.IsSelected == true);
    }
}
