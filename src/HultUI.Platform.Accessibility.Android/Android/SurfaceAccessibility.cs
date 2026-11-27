using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Views.Accessibility;
using AccessibilityAction = Android.Views.Accessibility.AccessibilityNodeInfo.AccessibilityAction;
using Log = Android.Util.Log;

namespace HultUI.Platform.Accessibility.Android;

// Obtain is how a node, an event and their infos are made on Android 12; the constructors that
// replace it start at 13 (API 33). Checked, a bool, is how 12 to 15 say checked.
#pragma warning disable CA1422, CS0618

/// <summary>
/// The thin Android layer (docs/design/TALKBACK.md §4): SDL's surface as a host of virtual views.
/// Its <see cref="AccessibilityNodeProvider"/> makes each <see cref="AccessibilityNodeInfo"/> from the
/// bridge's record, field by field, and hands TalkBack's actions back to it; its hover listener is
/// explore by touch; the bridge's events become <see cref="AccessibilityEvent"/>s sent from the
/// surface. Everything here runs on Android's main thread.
/// </summary>
internal sealed class SurfaceAccessibility : Java.Lang.Object, View.IOnHoverListener,
    AccessibilityManager.IAccessibilityStateChangeListener, IAndroidEventSink
{
    // Logcat's tag, HultUI's: the state's changes at info, TalkBack's actions and hovers at debug.
    private const string Tag = "HultUI";

    private readonly View _host;
    private readonly AndroidAccessibilityBridge _bridge;
    private readonly AccessibilityManager? _manager;
    private readonly int[] _location = new int[2];

    /// <summary>What the surface is given in place of a subclass: SDL's <c>SDLSurface</c> stays as SDL
    /// made it, and its delegate hands out the provider.</summary>
    public View.AccessibilityDelegate Delegate { get; }

    public SurfaceAccessibility(View host, AndroidAccessibilityBridge bridge, AccessibilityManager? manager)
    {
        _host = host;
        _bridge = bridge;
        _manager = manager;
        Delegate = new HostDelegate(new Provider(this));
    }

    public void OnAccessibilityStateChanged(bool enabled)
    {
        Log.Info(Tag, $"accessibility {(enabled ? "on" : "off")}");
        _bridge.SetEnabled(enabled);
    }

    /// <summary>
    /// Explore by touch: with TalkBack on, a finger's touches arrive as hover events, before SDL sees
    /// them (a view's hover listener comes first). The node under the finger is entered, and TalkBack
    /// focuses it and speaks.
    /// </summary>
    public bool OnHover(View? view, MotionEvent? e)
    {
        if (e is null || _manager is not { IsEnabled: true, IsTouchExplorationEnabled: true }) return false;
        int before = _bridge.HoveredId;
        bool taken = e.Action switch
        {
            MotionEventActions.HoverEnter or MotionEventActions.HoverMove => _bridge.HoverTo(e.GetX(), e.GetY()),
            MotionEventActions.HoverExit => _bridge.HoverTo(null, null),
            _ => false,
        };
        if (_bridge.HoveredId != before) Log.Debug(Tag, $"TalkBack: hover {e.Action} at {e.GetX():0},{e.GetY():0} on {_bridge.HoveredId}");
        return taken;
    }

    private (int X, int Y) Origin()
    {
        _host.GetLocationOnScreen(_location);
        return (_location[0], _location[1]);
    }

    // ──── Nodes ────

    private AccessibilityNodeInfo? Create(int id)
    {
        var (ox, oy) = Origin();
        var node = _bridge.Node(id, ox, oy);
        if (id == AndroidIds.Host)
        {
            // The surface's own node, as Android makes it, with the window's root under it. Not
            // focusable for TalkBack, though SDL's surface takes the keys: TalkBack makes a focusable
            // node one stop and reads every label under it as its own, so a finger on a heading read
            // the whole window (seen on the emulator). The virtual nodes hold the focus.
            var hostInfo = AccessibilityNodeInfo.Obtain(_host)!;
            _host.OnInitializeAccessibilityNodeInfo(hostInfo);
            hostInfo.Focusable = false;
            hostInfo.Focused = false;
            hostInfo.RemoveAction(AccessibilityAction.ActionFocus);
            hostInfo.RemoveAction(AccessibilityAction.ActionClearFocus);
            if (node is not null)
                foreach (int child in node.Children) hostInfo.AddChild(_host, child);
            return hostInfo;
        }
        if (node is null) return null;

        var info = AccessibilityNodeInfo.Obtain(_host, id)!;
        info.PackageName = _host.Context?.PackageName;
        info.ClassName = node.ClassName;
        if (node.ParentId == AndroidIds.Host) info.SetParent(_host);
        else info.SetParent(_host, node.ParentId);
        foreach (int child in node.Children) info.AddChild(_host, child);
        var b = node.BoundsInScreen;
        info.SetBoundsInScreen(new Rect(b.Left, b.Top, b.Right, b.Bottom));
        info.VisibleToUser = node.Has(AndroidNodeFlags.VisibleToUser);

        info.Text = node.Text;
        info.ContentDescription = node.ContentDescription;
        info.HintText = node.HintText;
        info.StateDescription = node.StateDescription;
        if (node.PaneTitle is { } pane) info.PaneTitle = pane;
        if (node.ContainerTitle is { } title && OperatingSystem.IsAndroidVersionAtLeast(34)) info.ContainerTitle = title;
        // AndroidX's setRoleDescription, which TalkBack reads: the platform has no setter for it.
        if (node.RoleDescription is { } role) info.Extras?.PutCharSequence("AccessibilityNodeInfo.roleDescription", new Java.Lang.String(role));
        if (node.Expanded is { } expanded)
        {
            // Before 16, TalkBack says it from the expand or collapse action the node offers; a state
            // description as well was spoken twice ("collapsed. Collapsed.").
            if (OperatingSystem.IsAndroidVersionAtLeast(36))
                info.ExpandedState = expanded ? ExpandedState.Full : ExpandedState.Collapsed;
        }

        info.Enabled = node.Has(AndroidNodeFlags.Enabled);
        info.Focusable = node.Has(AndroidNodeFlags.Focusable);
        info.Focused = node.Has(AndroidNodeFlags.Focused);
        info.AccessibilityFocused = node.Has(AndroidNodeFlags.AccessibilityFocused);
        info.Clickable = node.Has(AndroidNodeFlags.Clickable);
        info.Checkable = node.Has(AndroidNodeFlags.Checkable);
        info.Checked = node.Has(AndroidNodeFlags.Checked);
        info.Selected = node.Has(AndroidNodeFlags.Selected);
        info.Password = node.Has(AndroidNodeFlags.Password);
        info.Heading = node.Has(AndroidNodeFlags.Heading);
        info.Editable = node.Has(AndroidNodeFlags.Editable);
        info.MultiLine = node.Has(AndroidNodeFlags.MultiLine);
        info.Scrollable = node.Has(AndroidNodeFlags.Scrollable);
        info.LongClickable = node.Can(AndroidActions.LongClick);
        info.Dismissable = node.Can(AndroidActions.Dismiss);

        foreach (var (ours, theirs) in ActionTable)
            if (node.Can(ours)) info.AddAction(theirs);

        if (node.Range is { } range)
            info.SetRangeInfo(AccessibilityNodeInfo.RangeInfo.Obtain(
                range.Type == AndroidRangeType.Int ? RangeType.Int : RangeType.Float, range.Min, range.Max, range.Current));
        if (node.Collection is { } collection)
            info.SetCollectionInfo(AccessibilityNodeInfo.CollectionInfo.Obtain(collection.Rows, collection.Columns,
                collection.Hierarchical, collection.SelectionMode switch
                {
                    AndroidSelectionMode.Single => SelectionMode.Single,
                    AndroidSelectionMode.Multiple => SelectionMode.Multiple,
                    _ => SelectionMode.None,
                }));
        if (node.CollectionItem is { } item)
            info.SetCollectionItemInfo(AccessibilityNodeInfo.CollectionItemInfo.Obtain(item.Row, 1, item.Column, 1, item.Heading, item.Selected));
        if (node.SelectionStart >= 0) info.SetTextSelection(node.SelectionStart, node.SelectionEnd);
        if (node.Granularities != AndroidGranularity.None) info.MovementGranularities = (MovementGranularity)(int)node.Granularities;
        info.LiveRegion = node.LiveRegion switch
        {
            AndroidLiveRegion.Polite => AccessibilityLiveRegion.Polite,
            AndroidLiveRegion.Assertive => AccessibilityLiveRegion.Assertive,
            _ => AccessibilityLiveRegion.None,
        };
        return info;
    }

    // ──── Actions ────

    private static readonly (AndroidActions Ours, AccessibilityAction Theirs)[] ActionTable =
    [
        (AndroidActions.Click, AccessibilityAction.ActionClick!),
        (AndroidActions.Focus, AccessibilityAction.ActionFocus!),
        (AndroidActions.ClearFocus, AccessibilityAction.ActionClearFocus!),
        (AndroidActions.AccessibilityFocus, AccessibilityAction.ActionAccessibilityFocus!),
        (AndroidActions.ClearAccessibilityFocus, AccessibilityAction.ActionClearAccessibilityFocus!),
        (AndroidActions.Select, AccessibilityAction.ActionSelect!),
        (AndroidActions.ScrollForward, AccessibilityAction.ActionScrollForward!),
        (AndroidActions.ScrollBackward, AccessibilityAction.ActionScrollBackward!),
        (AndroidActions.ScrollUp, AccessibilityAction.ActionScrollUp!),
        (AndroidActions.ScrollDown, AccessibilityAction.ActionScrollDown!),
        (AndroidActions.ScrollLeft, AccessibilityAction.ActionScrollLeft!),
        (AndroidActions.ScrollRight, AccessibilityAction.ActionScrollRight!),
        (AndroidActions.Expand, AccessibilityAction.ActionExpand!),
        (AndroidActions.Collapse, AccessibilityAction.ActionCollapse!),
        (AndroidActions.SetProgress, AccessibilityAction.ActionSetProgress!),
        (AndroidActions.SetText, AccessibilityAction.ActionSetText!),
        (AndroidActions.ShowOnScreen, AccessibilityAction.ActionShowOnScreen!),
        (AndroidActions.NextAtGranularity, AccessibilityAction.ActionNextAtMovementGranularity!),
        (AndroidActions.PreviousAtGranularity, AccessibilityAction.ActionPreviousAtMovementGranularity!),
        (AndroidActions.SetSelection, AccessibilityAction.ActionSetSelection!),
        (AndroidActions.LongClick, AccessibilityAction.ActionLongClick!),
        (AndroidActions.Dismiss, AccessibilityAction.ActionDismiss!),
    ];

    private static readonly Dictionary<int, AndroidActions> ActionsById = ActionTable.ToDictionary(a => a.Theirs.Id, a => a.Ours);

    private bool Perform(int id, global::Android.Views.Accessibility.Action action, Bundle? arguments)
    {
        // The surface's own actions are the view's, as ExploreByTouchHelper leaves them.
        if (id == AndroidIds.Host) return _host.PerformAccessibilityAction(action, arguments);
        if (!ActionsById.TryGetValue((int)action, out var ours))
        {
            Log.Debug(Tag, $"TalkBack: action {(int)action} on {id}: not offered");
            return false;
        }
        float? progress = arguments?.ContainsKey(AccessibilityNodeInfo.ActionArgumentProgressValue) == true
            ? arguments.GetFloat(AccessibilityNodeInfo.ActionArgumentProgressValue) : null;
        string? text = arguments?.GetCharSequence(AccessibilityNodeInfo.ActionArgumentSetTextCharsequence);
        var unit = (AndroidGranularity)(arguments?.GetInt(AccessibilityNodeInfo.ActionArgumentMovementGranularityInt) ?? 0);
        bool extend = arguments?.GetBoolean(AccessibilityNodeInfo.ActionArgumentExtendSelectionBoolean) ?? false;
        int start = arguments?.ContainsKey(AccessibilityNodeInfo.ActionArgumentSelectionStartInt) == true
            ? arguments.GetInt(AccessibilityNodeInfo.ActionArgumentSelectionStartInt) : -1;
        int end = arguments?.ContainsKey(AccessibilityNodeInfo.ActionArgumentSelectionEndInt) == true
            ? arguments.GetInt(AccessibilityNodeInfo.ActionArgumentSelectionEndInt) : -1;
        bool done = _bridge.Perform(id, ours, new AndroidActionArgs(progress, text, unit, extend, start, end));
        // At a person's pace, so always (Log.isLoggable says no to debug for an app's tag by default).
        var node = _bridge.Node(id);
        string how = unit != AndroidGranularity.None ? $" by {unit}{(extend ? ", extending" : "")}"
            : start >= 0 ? $" {start}..{end}" : "";
        Log.Debug(Tag, $"TalkBack: {ours}{how} on {id} ({node?.ClassName} \"{node?.Text ?? node?.ContentDescription}\" " +
            $"{node?.BoundsInScreen} visible {node?.Has(AndroidNodeFlags.VisibleToUser)}): {(done ? "done" : "refused")}");
        return done;
    }

    private AccessibilityNodeInfo? FindFocus(NodeFocus focus)
    {
        int id = focus == NodeFocus.Accessibility ? _bridge.AccessibilityFocusedId : _bridge.InputFocusedId;
        return id == AndroidIds.None ? null : Create(id);
    }

    // ──── Events ────

    /// <summary>From HultUI's UI thread (the window's changes) or Android's main thread (an action's,
    /// a hover's): sent from the main thread, in order.</summary>
    public void Send(IReadOnlyList<AndroidEvent> events) => _host.Post(() =>
    {
        if (_manager is not { IsEnabled: true }) return;
        foreach (var e in events) SendNow(e);
    });

    private void SendNow(AndroidEvent e)
    {
        var ev = AccessibilityEvent.Obtain(TypeOf(e.Type))!;
        ev.PackageName = _host.Context?.PackageName;
        if (e.Source == AndroidIds.Host)
        {
            ev.SetSource(_host);
            ev.ClassName = _host.Class.Name;
        }
        else
        {
            ev.SetSource(_host, e.Source);
            var (ox, oy) = Origin();
            if (_bridge.Node(e.Source, ox, oy) is { } node)
            {
                // What describes the source, as ExploreByTouchHelper fills an event for a virtual view.
                ev.ClassName = node.ClassName;
                if (AndroidNodeMapper.EventWords(node, e.Type) is { } words)
                    ev.Text?.Add(new Java.Lang.String(words));
                ev.ContentDescription = node.ContentDescription;
                ev.Enabled = node.Has(AndroidNodeFlags.Enabled);
                ev.Checked = node.Has(AndroidNodeFlags.Checked);
                ev.Password = node.Has(AndroidNodeFlags.Password);
                ev.Scrollable = node.Has(AndroidNodeFlags.Scrollable);
            }
        }
        switch (e.Type)
        {
            case AndroidEventType.WindowContentChanged:
            case AndroidEventType.WindowStateChanged:
                ev.ContentChangeTypes = ChangesOf(e.Changes);
                break;
            case AndroidEventType.ViewTextChanged:
                ev.BeforeText = e.BeforeText;
                ev.FromIndex = e.FromIndex;
                ev.AddedCount = e.AddedCount;
                ev.RemovedCount = e.RemovedCount;
                break;
            case AndroidEventType.ViewTextSelectionChanged:
                ev.FromIndex = e.FromIndex;
                ev.ToIndex = e.ToIndex;
                ev.ItemCount = e.ItemCount;
                break;
            case AndroidEventType.ViewTextTraversed:
                // The text moved through, which TalkBack reads the unit from, as a TextView sends it.
                ev.Text?.Clear();
                if (e.Text is { } traversed) ev.Text?.Add(new Java.Lang.String(traversed));
                ev.FromIndex = e.FromIndex;
                ev.ToIndex = e.ToIndex;
                ev.MovementGranularity = (MovementGranularity)(int)e.Granularity;
                // The binding types the event's action as a GlobalAction; it holds the node action's id.
                ev.Action = (global::Android.AccessibilityServices.GlobalAction)(int)(e.Forward
                    ? global::Android.Views.Accessibility.Action.NextAtMovementGranularity
                    : global::Android.Views.Accessibility.Action.PreviousAtMovementGranularity);
                Log.Debug(Tag, $"TalkBack: traversed {e.Granularity} {(e.Forward ? "forward" : "back")} {e.FromIndex}..{e.ToIndex} on {e.Source}");
                break;
            case AndroidEventType.ViewScrolled:
                ev.ScrollX = e.ScrollX;
                ev.ScrollY = e.ScrollY;
                ev.MaxScrollX = e.MaxScrollX;
                ev.MaxScrollY = e.MaxScrollY;
                ev.ScrollDeltaX = e.ScrollDeltaX;
                ev.ScrollDeltaY = e.ScrollDeltaY;
                break;
        }
        _host.Parent?.RequestSendAccessibilityEvent(_host, ev);
        // TalkBack's focus box is drawn by the window (ViewRootImpl) over the view, in its own pass,
        // which a move of the focus does not ask for: with nothing else redrawing SDL's surface view,
        // the box showed only once something else did (the keyboard opening), seen on a Galaxy S25.
        // ExploreByTouchHelper invalidates its host as the focus moves, too.
        if (e.Type is AndroidEventType.ViewAccessibilityFocused or AndroidEventType.ViewAccessibilityFocusCleared)
            _host.Invalidate();
    }

    private static EventTypes TypeOf(AndroidEventType type) => type switch
    {
        AndroidEventType.ViewClicked => EventTypes.ViewClicked,
        AndroidEventType.ViewFocused => EventTypes.ViewFocused,
        AndroidEventType.ViewSelected => EventTypes.ViewSelected,
        AndroidEventType.ViewScrolled => EventTypes.ViewScrolled,
        AndroidEventType.ViewTextChanged => EventTypes.ViewTextChanged,
        AndroidEventType.ViewTextSelectionChanged => EventTypes.ViewTextSelectionChanged,
        AndroidEventType.ViewHoverEnter => EventTypes.ViewHoverEnter,
        AndroidEventType.ViewHoverExit => EventTypes.ViewHoverExit,
        AndroidEventType.ViewAccessibilityFocused => EventTypes.ViewAccessibilityFocused,
        AndroidEventType.ViewAccessibilityFocusCleared => EventTypes.ViewAccessibilityFocusCleared,
        AndroidEventType.WindowStateChanged => EventTypes.WindowStateChanged,
        AndroidEventType.ViewTextTraversed => EventTypes.ViewTextTraversedAtMovementGranularity,
        _ => EventTypes.WindowContentChanged,
    };

    /// <summary>The change's kinds, each where Android has it: enabled from 14, checked and expanded
    /// from 16; the state description's change says them before.</summary>
    private static ContentChangeTypes ChangesOf(AndroidContentChange changes)
    {
        ContentChangeTypes types = 0;
        if ((changes & AndroidContentChange.Subtree) != 0) types |= ContentChangeTypes.Subtree;
        if ((changes & AndroidContentChange.Text) != 0) types |= ContentChangeTypes.Text;
        if ((changes & AndroidContentChange.ContentDescription) != 0) types |= ContentChangeTypes.ContentDescription;
        if ((changes & AndroidContentChange.StateDescription) != 0) types |= ContentChangeTypes.StateDescription;
        if ((changes & AndroidContentChange.PaneTitle) != 0) types |= ContentChangeTypes.PaneTitle;
        if ((changes & AndroidContentChange.PaneAppeared) != 0) types |= ContentChangeTypes.PaneAppeared;
        if ((changes & AndroidContentChange.PaneDisappeared) != 0) types |= ContentChangeTypes.PaneDisappeared;
        if ((changes & AndroidContentChange.Enabled) != 0 && OperatingSystem.IsAndroidVersionAtLeast(34)) types |= ContentChangeTypes.Enabled;
        if ((changes & AndroidContentChange.Checked) != 0 && OperatingSystem.IsAndroidVersionAtLeast(36)) types |= ContentChangeTypes.Checked;
        if ((changes & AndroidContentChange.Expanded) != 0 && OperatingSystem.IsAndroidVersionAtLeast(36)) types |= ContentChangeTypes.Expanded;
        return types;
    }

    // ──── The provider, and the delegate that hands it out ────

    private sealed class Provider(SurfaceAccessibility owner) : AccessibilityNodeProvider
    {
        public override AccessibilityNodeInfo? CreateAccessibilityNodeInfo(int virtualViewId) => owner.Create(virtualViewId);

        public override bool PerformAction(int virtualViewId, global::Android.Views.Accessibility.Action action, Bundle? arguments) =>
            owner.Perform(virtualViewId, action, arguments);

        public override AccessibilityNodeInfo? FindFocus(NodeFocus focus) => owner.FindFocus(focus);
    }

    private sealed class HostDelegate(AccessibilityNodeProvider provider) : View.AccessibilityDelegate
    {
        public override AccessibilityNodeProvider? GetAccessibilityNodeProvider(View host) => provider;
    }
}
