namespace HultUI.Platform.Accessibility.Android;

/// <summary>Virtual view ids with a meaning of their own, as Android's
/// <c>AccessibilityNodeProvider</c> and ExploreByTouchHelper use them.</summary>
internal static class AndroidIds
{
    /// <summary>The host view itself, SDL's surface: <c>AccessibilityNodeProvider.HOST_VIEW_ID</c>.
    /// The window's root node stands for it.</summary>
    public const int Host = -1;

    /// <summary>No virtual view: ExploreByTouchHelper's <c>INVALID_ID</c>.</summary>
    public const int None = int.MinValue;

    /// <summary>The node that carries announcements as a live region (<see cref="AndroidAccessibilityBridge.Announce"/>):
    /// no element's, and in no node's children, so swiping never lands on it.</summary>
    public const int Announcer = 0;
}

/// <summary>What a virtual view is and says: one <c>AccessibilityNodeInfo</c>, made platform-neutral
/// so the desktop tests can read it. The Android layer copies it field by field.</summary>
internal sealed class AndroidNode
{
    public required int Id { get; init; }

    /// <summary>The parent's virtual id: <see cref="AndroidIds.Host"/> for the root's children,
    /// <see cref="AndroidIds.None"/> for the host's own record.</summary>
    public int ParentId { get; init; } = AndroidIds.Host;

    /// <summary>The children's virtual ids, in reading order.</summary>
    public int[] Children { get; init; } = [];

    /// <summary>The Android widget class TalkBack reads the role from (<c>android.widget.Button</c>).</summary>
    public string ClassName { get; init; } = AndroidClasses.View;

    /// <summary>The role, spoken, where no widget class says it (a link, a tab).</summary>
    public string? RoleDescription { get; init; }

    public string? Text { get; init; }
    public string? ContentDescription { get; init; }
    public string? HintText { get; init; }
    public string? StateDescription { get; init; }

    /// <summary>A dialog's title: <c>setPaneTitle</c>, spoken as it opens.</summary>
    public string? PaneTitle { get; init; }

    /// <summary>A container's name (a list's, a group's): <c>setContainerTitle</c>, Android 14 and later.</summary>
    public string? ContainerTitle { get; init; }

    /// <summary>Expanded or collapsed, for what opens (an expander, a tree item, a submenu); null
    /// for anything else. Android 16's <c>setExpandedState</c>; before it, TalkBack says it from the
    /// expand or collapse action.</summary>
    public bool? Expanded { get; init; }

    public AndroidNodeFlags Flags { get; init; }

    /// <summary>Where the node is on screen, in device pixels, clipped to the scroll views above it
    /// and the window; empty when none of it shows.</summary>
    public PixelBox BoundsInScreen { get; init; }

    public AndroidActions Actions { get; init; }
    public AndroidRange? Range { get; init; }
    public AndroidCollection? Collection { get; init; }
    public AndroidCollectionItem? CollectionItem { get; init; }

    /// <summary>The units TalkBack may move through the node's text by
    /// (<see cref="AndroidActions.NextAtGranularity"/>): <c>setMovementGranularities</c>.</summary>
    public AndroidGranularity Granularities { get; init; }

    /// <summary>The text's selection, or the caret at both ends; -1 for none.</summary>
    public int SelectionStart { get; init; } = -1;
    public int SelectionEnd { get; init; } = -1;

    public AndroidLiveRegion LiveRegion { get; init; }

    public bool Has(AndroidNodeFlags flag) => (Flags & flag) == flag;
    public bool Can(AndroidActions action) => (Actions & action) == action;
}

/// <summary>The widget classes the mapper names (TalkBack reads a role from them).</summary>
internal static class AndroidClasses
{
    public const string View = "android.view.View";
    public const string Button = "android.widget.Button";
    public const string ToggleButton = "android.widget.ToggleButton";
    public const string CheckBox = "android.widget.CheckBox";
    public const string RadioButton = "android.widget.RadioButton";
    public const string Switch = "android.widget.Switch";
    public const string EditText = "android.widget.EditText";
    public const string TextView = "android.widget.TextView";
    public const string ImageView = "android.widget.ImageView";
    public const string ListView = "android.widget.ListView";
    public const string ScrollView = "android.widget.ScrollView";
    public const string HorizontalScrollView = "android.widget.HorizontalScrollView";
    public const string SeekBar = "android.widget.SeekBar";
    public const string ProgressBar = "android.widget.ProgressBar";
    public const string Spinner = "android.widget.Spinner";
    public const string TabWidget = "android.widget.TabWidget";
}

[Flags]
internal enum AndroidNodeFlags
{
    None = 0,
    Enabled = 1 << 0,
    Focusable = 1 << 1,
    Focused = 1 << 2,
    AccessibilityFocused = 1 << 3,
    Clickable = 1 << 4,
    Checkable = 1 << 5,
    Checked = 1 << 6,
    Selected = 1 << 7,
    Password = 1 << 8,
    Heading = 1 << 9,
    Editable = 1 << 10,
    MultiLine = 1 << 11,
    Scrollable = 1 << 12,
    VisibleToUser = 1 << 13,
}

/// <summary>The actions a virtual view offers, by Android's names (<c>AccessibilityAction</c>).</summary>
[Flags]
internal enum AndroidActions : long
{
    None = 0,
    Click = 1L << 0,
    Focus = 1L << 1,
    ClearFocus = 1L << 2,
    AccessibilityFocus = 1L << 3,
    ClearAccessibilityFocus = 1L << 4,
    Select = 1L << 5,
    ScrollForward = 1L << 6,
    ScrollBackward = 1L << 7,
    ScrollUp = 1L << 8,
    ScrollDown = 1L << 9,
    ScrollLeft = 1L << 10,
    ScrollRight = 1L << 11,
    Expand = 1L << 12,
    Collapse = 1L << 13,
    SetProgress = 1L << 14,
    SetText = 1L << 15,
    ShowOnScreen = 1L << 16,
    /// <summary>The next character, word, line or paragraph (<see cref="AndroidActionArgs.Granularity"/>):
    /// what TalkBack reads by when its reading control is set to one.</summary>
    NextAtGranularity = 1L << 17,
    PreviousAtGranularity = 1L << 18,
    /// <summary>A field's selection, or its caret (<see cref="AndroidActionArgs.SelectionStart"/>).</summary>
    SetSelection = 1L << 19,
    /// <summary>A long press: TalkBack's double tap held.</summary>
    LongClick = 1L << 20,
    /// <summary>Closes a dialog or a menu (<c>setDismissable</c>).</summary>
    Dismiss = 1L << 21,
}

/// <summary>The units TalkBack moves through a node's text by: Android's
/// <c>MOVEMENT_GRANULARITY_*</c>, whose values these are.</summary>
[Flags]
internal enum AndroidGranularity
{
    None = 0,
    Character = 1,
    Word = 2,
    Line = 4,
    Paragraph = 8,
}

/// <summary>What an action carries (<c>ACTION_ARGUMENT_*</c>): a slider's value, a field's text, the
/// unit to move by and whether the move extends the selection, a selection's ends.</summary>
internal readonly record struct AndroidActionArgs(float? Progress = null, string? Text = null,
    AndroidGranularity Granularity = AndroidGranularity.None, bool ExtendSelection = false,
    int SelectionStart = -1, int SelectionEnd = -1);

internal enum AndroidLiveRegion { None, Polite, Assertive }

internal enum AndroidRangeType { Int, Float }

internal readonly record struct AndroidRange(AndroidRangeType Type, float Min, float Max, float Current);

internal enum AndroidSelectionMode { None, Single, Multiple }

internal readonly record struct AndroidCollection(int Rows, int Columns, bool Hierarchical, AndroidSelectionMode SelectionMode);

internal readonly record struct AndroidCollectionItem(int Row, int Column, bool Heading, bool Selected);

/// <summary>A box in device pixels, as Android's <c>Rect</c>: right and bottom are past the box.</summary>
internal readonly record struct PixelBox(int Left, int Top, int Right, int Bottom)
{
    public bool IsEmpty => Right <= Left || Bottom <= Top;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>An event's type, by Android's names (<c>AccessibilityEvent.TYPE_*</c>).</summary>
internal enum AndroidEventType
{
    /// <summary>A view was clicked. Sent for a checkable node TalkBack is on, once its new state is
    /// in the tree, as a native one sends it after its click.</summary>
    ViewClicked,
    ViewFocused,
    ViewSelected,
    ViewScrolled,
    ViewTextChanged,
    ViewTextSelectionChanged,
    ViewHoverEnter,
    ViewHoverExit,
    ViewAccessibilityFocused,
    ViewAccessibilityFocusCleared,
    WindowContentChanged,
    WindowStateChanged,
    /// <summary>A move through the node's text by a unit (<c>TYPE_VIEW_TEXT_TRAVERSED_AT_MOVEMENT_GRANULARITY</c>):
    /// TalkBack speaks <see cref="AndroidEvent.Text"/> from <see cref="AndroidEvent.FromIndex"/> to
    /// <see cref="AndroidEvent.ToIndex"/>.</summary>
    ViewTextTraversed,
}

/// <summary>What changed, for a content or a window-state change (<c>CONTENT_CHANGE_TYPE_*</c>).</summary>
[Flags]
internal enum AndroidContentChange
{
    Undefined = 0,
    Subtree = 1 << 0,
    Text = 1 << 1,
    ContentDescription = 1 << 2,
    StateDescription = 1 << 3,
    PaneTitle = 1 << 4,
    PaneAppeared = 1 << 5,
    PaneDisappeared = 1 << 6,
    /// <summary>Android 14's <c>CONTENT_CHANGE_TYPE_ENABLED</c>; undefined before it.</summary>
    Enabled = 1 << 7,
    /// <summary>Android 16's <c>CONTENT_CHANGE_TYPE_CHECKED</c>; a state description's change before it.</summary>
    Checked = 1 << 8,
    /// <summary>Android 16's <c>CONTENT_CHANGE_TYPE_EXPANDED</c>; a state description's change before it.</summary>
    Expanded = 1 << 9,
}

/// <summary>
/// An event to send for a virtual view. The Android layer makes the <c>AccessibilityEvent</c> on
/// Android's main thread and fills what describes the source (its class, text, states) from the
/// node as it is then; what only the event knows is here.
/// </summary>
internal readonly record struct AndroidEvent(AndroidEventType Type, int Source)
{
    public AndroidContentChange Changes { get; init; }

    /// <summary>A text change's text before it, and where it changed.</summary>
    public string? BeforeText { get; init; }
    public int FromIndex { get; init; } = -1;
    public int ToIndex { get; init; } = -1;
    public int AddedCount { get; init; } = -1;
    public int RemovedCount { get; init; } = -1;
    public int ItemCount { get; init; } = -1;

    /// <summary>A move through text: the text moved through (the event's text, which TalkBack reads
    /// from), the unit, and whether it went forward (<c>setAction</c>: the next or the previous).</summary>
    public string? Text { get; init; }
    public AndroidGranularity Granularity { get; init; }
    public bool Forward { get; init; }

    /// <summary>A scroll's position, its end, and how far it moved, in device pixels.</summary>
    public int ScrollX { get; init; }
    public int ScrollY { get; init; }
    public int MaxScrollX { get; init; }
    public int MaxScrollY { get; init; }
    public int ScrollDeltaX { get; init; }
    public int ScrollDeltaY { get; init; }
}

/// <summary>Where the bridge sends its events: the Android layer posts them to Android's main
/// thread; a test records them.</summary>
internal interface IAndroidEventSink
{
    /// <summary>Called on HultUI's UI thread, or on Android's main thread for an action's own
    /// events (accessibility focus, hover). The events are in the order to send them.</summary>
    void Send(IReadOnlyList<AndroidEvent> events);
}
