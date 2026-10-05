using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Core.Threading;

namespace HultUI.Core.Elements;

public abstract class Element
{
    // ──── Identity ────
    public ElementId Id { get; } = ElementId.New();
    public string? Key { get; set; }
    public string? Name { get; set; }

    /// <summary>
    /// Stable, non-localized identifier exposed to UI Automation as AutomationId.
    /// Unlike the accessible name, it's meant for test/automation selectors
    /// (e.g. FlaUI's <c>ByAutomationId</c>), so keep it constant across runs.
    /// </summary>
    public string? AutomationId { get; set; }

    // ──── Source origin (DevTools) ────
    // Storage is conditional, unlike the factory parameters that feed it: two extra
    // fields on every Element is real memory at 10k+ elements, and nothing on the
    // layout, style, render, or hit-test paths ever reads them.
#if HULTUI_ELEMENT_ORIGIN
    private string? _originFile;
    private int _originLine;
#endif

    /// <summary>
    /// Where this element was created — the source file and line of the factory call.
    /// Null when the build has origins compiled out (see <c>HultUIElementOrigin</c> in
    /// Directory.Build.targets) or when the element was constructed directly rather
    /// than through a factory.
    /// </summary>
    public ElementOrigin? Origin
    {
#if HULTUI_ELEMENT_ORIGIN
        get => _originFile is null ? null : new ElementOrigin(_originFile, _originLine);
#else
        get => null;
#endif
    }

    internal void SetOrigin(string? file, int line)
    {
#if HULTUI_ELEMENT_ORIGIN
        _originFile = file;
        _originLine = line;
#endif
    }

    /// <summary>
    /// Adopts <paramref name="source"/>'s origin during reconciliation. Usually a
    /// no-op — the same call site rebuilt — but a retained element can be reused for
    /// a different branch of a conditional build, and then its origin has moved.
    /// </summary>
    private void CopyOriginFrom(Element source)
    {
#if HULTUI_ELEMENT_ORIGIN
        _originFile = source._originFile;
        _originLine = source._originLine;
#endif
    }

    // ──── Tree structure ────
    public Element? Parent { get; internal set; }
    // Allocated on first child: roughly half of a real tree is leaves (text,
    // icons), and an empty List per leaf adds up at 10k+ elements.
    private List<Element>? _children;
    public IReadOnlyList<Element> Children => (IReadOnlyList<Element>?)_children ?? [];

    // ──── Layout input ────
    // Style objects are allocated on first write. Most elements never touch
    // Grid/GridItem/Position (and leaves often never touch Flex/Box), so eager
    // allocation dominated element-construction cost and GC churn in large
    // trees. Public getters keep the fluent API unchanged; hot read paths
    // (layout, render, hit test, style resolution) go through the *Read
    // accessors below, which fall back to a shared immutable default instead
    // of allocating. The defaults must never be mutated — reads only.
    private FlexStyle? _flex;
    private BoxStyle? _box;
    private PositionStyle? _position;
    private GridStyle? _grid;
    private GridItemStyle? _gridItem;

    public FlexStyle Flex => _flex ??= new();
    public BoxStyle Box => _box ??= new();
    public PositionStyle Position => _position ??= new();
    public GridStyle Grid => _grid ??= new();
    public GridItemStyle GridItem => _gridItem ??= new();

    internal FlexStyle FlexRead => _flex ?? FlexStyle.Default;
    internal BoxStyle BoxRead => _box ?? BoxStyle.Default;
    internal PositionStyle PositionRead => _position ?? PositionStyle.Default;
    internal GridStyle GridRead => _grid ?? GridStyle.Default;
    internal GridItemStyle GridItemRead => _gridItem ?? GridItemStyle.Default;

    // ──── Layout output ────

    private LayoutResult _layout;

    /// <summary>
    /// Where this element ended up: its box, in both local and absolute coordinates. Written
    /// by the layout pass, read by rendering, hit testing and app code.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// In a strict-lifetime build, when this element has been replaced by reconciliation —
    /// see <see cref="IsDetached"/>. Its layout was never computed and never will be, so the
    /// value would be a zero box or whatever the last pass left behind.
    /// </exception>
    public LayoutResult Layout
    {
        get
        {
#if HULTUI_STRICT_LIFETIME
            if (_detached) ThrowDetached(nameof(Layout));
#endif
            return _layout;
        }
        internal set => _layout = value;
    }

    /// <summary>
    /// What every ancestor scroll view subtracts from this element's position when painting
    /// and hit-testing.
    ///
    /// <para>A scroll view does not move its children — their layout boxes are computed once
    /// and stay where the content says they are. Scrolling is a translate applied at paint
    /// time and mirrored in the hit tester, so inside a scrolled view
    /// <see cref="LayoutResult.AbsoluteBorderBox"/> is not where the element appears.</para>
    /// </summary>
    public Point AncestorScrollOffset
    {
        get
        {
            float x = 0, y = 0;
            for (var parent = Parent; parent is not null; parent = parent.Parent)
                if (parent is ScrollViewElement scroll)
                {
                    x += scroll.ScrollOffsetX;
                    y += scroll.ScrollOffsetY;
                }
            return new Point(x, y);
        }
    }

    /// <summary>
    /// Draw the theme's two-ring focus visual around this element while it has focus.
    ///
    /// <para>Opt-in, because the visual is drawn <i>outside</i> the element: in a tight row it
    /// needs a pixel or two of clearance that only the app knows it has, and a control that
    /// already shows focus its own way (a text box lighting its underline) would end up saying
    /// it twice.</para>
    /// </summary>
    public bool ShowsFocusVisual { get; set; }

    /// <summary>
    /// This element's border box in window coordinates — where it actually is on screen, with
    /// every ancestor's scroll applied. Inside an overlay it is relative to the overlay, which is
    /// the space the overlay's pointer events arrive in; <see cref="WindowBorderBox"/> is where it
    /// is in the window.
    ///
    /// <para>This is what anything positioning itself <i>against</i> an element wants: an
    /// overlay, a tooltip, a synthesized click. Using the layout box instead is silently
    /// correct until something scrolls and then silently wrong, which is how a dropdown ended
    /// up opening a third of a page away from the control it belongs to.</para>
    /// </summary>
    public Rect ScreenBorderBox
    {
        get
        {
            var offset = AncestorScrollOffset;
            var box = Layout.AbsoluteBorderBox;
            return new Rect(box.X - offset.X, box.Y - offset.Y, box.Width, box.Height);
        }
    }

    /// <summary><see cref="ScreenBorderBox"/> less this element's own border and padding.</summary>
    public Rect ScreenContentBox
    {
        get
        {
            var offset = AncestorScrollOffset;
            var box = Layout.AbsoluteContentBox;
            return new Rect(box.X - offset.X, box.Y - offset.Y, box.Width, box.Height);
        }
    }

    /// <summary>
    /// Where this element is in the window, overlay or not: <see cref="ScreenBorderBox"/> plus,
    /// inside an overlay, the overlay's place. What anything positioned in the window against an
    /// element wants — another overlay opened from it, a focus ring, a synthesized click. Using
    /// <see cref="ScreenBorderBox"/> for those is right in the main tree and in a dialog (whose
    /// overlay covers the window from its corner), and off by the overlay's offset in a popover:
    /// a dropdown in one opened that far away.
    /// </summary>
    public Rect WindowBorderBox
    {
        get
        {
            var box = ScreenBorderBox;
            var origin = TreeOrigin;
            return new Rect(box.X + origin.X, box.Y + origin.Y, box.Width, box.Height);
        }
    }

    /// <summary><see cref="WindowBorderBox"/> less this element's own border and padding.</summary>
    public Rect WindowContentBox
    {
        get
        {
            var box = ScreenContentBox;
            var origin = TreeOrigin;
            return new Rect(box.X + origin.X, box.Y + origin.Y, box.Width, box.Height);
        }
    }

    /// <summary>Where the overlay this element is the root of sits in the window, kept by the
    /// host; zero for the main tree.</summary>
    internal Point OverlayOrigin { get; set; }

    private Point TreeOrigin
    {
        get
        {
            var top = this;
            while (top.Parent is { } parent) top = parent;
            return top.OverlayOrigin;
        }
    }

    // ──── Lifetime ────

    private bool _detached;

    /// <summary>
    /// True once reconciliation has replaced this element with a live counterpart and
    /// discarded it.
    ///
    /// <para>Every rebuild constructs a fresh element tree. The reconciler patches that tree's
    /// values into the live one and throws the fresh elements away, so a reference to one is
    /// live for exactly one frame. Reading such an element's <see cref="Layout"/> gives
    /// geometry that was never computed, and writing its state — a scroll offset, a selection —
    /// changes nothing anyone will see. Neither fails on its own, which is what makes it an
    /// expensive mistake to find.</para>
    ///
    /// <para>Hold a <see cref="Reactivity.Ref{T}"/> instead: the reconciler repoints it at the
    /// live element on every patch. Inside an event handler, <c>e.CurrentTarget</c> is already
    /// the live element.</para>
    /// </summary>
    public bool IsDetached => _detached;

    /// <summary>
    /// Whether this element hangs off a root a window is showing. <see cref="OnMounted"/>
    /// means "got a parent", which the freshly built twin of a rebuild also does, so an
    /// element that drives its own animation asks this instead: a twin's root has no host,
    /// a discarded root and a removed subtree's top have had their host cleared, and only
    /// what is actually on screen says yes. A walk to the root, cheap enough for a
    /// per-frame check.
    ///
    /// <para>Removal unmounts the whole removed subtree, so <see cref="OnUnmounted"/> covers
    /// that exit. This check is for the two that never unmount: the twin, and a root
    /// dropped by <c>Window.SetRoot</c>.</para>
    /// </summary>
    public bool IsHosted
    {
        get
        {
            if (_detached) return false;
            var e = this;
            while (e.Parent is not null) e = e.Parent;
            return e.OverlayHost is not null;
        }
    }

    /// <summary>
    /// Whether a detached-element access throws. Compiled in for strict-lifetime builds only
    /// (Debug by default — see Directory.Build.targets), and switchable at runtime for a test
    /// that means to inspect a discarded element.
    /// </summary>
    public static bool StrictLifetimeChecks { get; set; } = true;

    /// <summary>
    /// Marks this element as replaced. Called by the reconciler on the freshly built element
    /// once its values have been patched into the live one.
    /// </summary>
    internal void MarkDetached() => _detached = true;

    /// <summary>
    /// Throws if this element has been replaced by reconciliation. For live-only state whose
    /// setter would otherwise accept the write and drop it.
    /// </summary>
    internal void ThrowIfDetached(string member)
    {
#if HULTUI_STRICT_LIFETIME
        if (_detached) ThrowDetached(member);
#endif
    }

#if HULTUI_STRICT_LIFETIME
    private void ThrowDetached(string member)
    {
        if (!StrictLifetimeChecks) return;

        string where = Origin is { } origin ? $" created at {origin}" : "";
        string? label = Name ?? AutomationId;
        string what = label is null ? "" : $" '{label}'";
        throw new InvalidOperationException(
            $"{GetType().Name}{what}{where} was replaced by reconciliation, so its "
            + $"{member} is meaningless — reads are stale and writes are dropped. "
            + "This element came from a build that has since been patched into the live tree "
            + "and discarded. Hold a Ref<T> and use its Current, or reach the live element "
            + "through an event's CurrentTarget. "
            + $"Set {nameof(Element)}.{nameof(StrictLifetimeChecks)} = false to allow it.");
    }
#endif

    // ──── Style / Inheritance ────
    private InheritedStyle? _inherited;
    public InheritedStyle Inherited => _inherited ??= new();
    internal InheritedStyle InheritedRead => _inherited ?? InheritedStyle.Default;
    public ResolvedInheritedStyle ResolvedInherited { get; internal set; }

    /// <summary>
    /// Called during style resolution to apply theme-dependent visual properties.
    /// Override in themed elements (Button, TextInput, etc.) to read from the theme.
    /// </summary>
    protected internal virtual void ApplyTheme(Theme theme) { }

    // ──── Visual ────
    private float _opacity = 1f;
    public float Opacity
    {
        get => _opacity;
        set
        {
            var clamped = Math.Clamp(value, 0f, 1f);
            if (_opacity == clamped) return;
            _opacity = clamped;
            MarkPaintDirty();
        }
    }

    /// <summary>
    /// What the renderer paints this subtree at: <see cref="Opacity"/>, times whatever a
    /// control adds for a state of its own — a disabled button dims through here rather
    /// than by writing <see cref="Opacity"/>, which is the app's to set and animate.
    /// </summary>
    internal virtual float RenderOpacity => _opacity;

    /// <summary>
    /// Whether assistive technology should offer this element as enabled. A control with a
    /// disabled state overrides it; everything else is enabled.
    /// </summary>
    internal virtual bool IsEnabledForAccessibility => true;

    /// <summary>
    /// A 2D transform (rotate/scale/translate) applied to this element and its
    /// subtree at render and hit-test time. Null = identity (no cost).
    /// </summary>
    private Transform2D? _transform;
    public Transform2D? Transform
    {
        get => _transform;
        set { if (Nullable.Equals(_transform, value)) return; _transform = value; MarkPaintDirty(); }
    }

    // ──── Interaction state (hover / press / focus) ────
    private InteractionStyles? _interaction;
    private InteractionStyles Interaction => _interaction ??= new InteractionStyles();

    // Hover, press and focus are recorded for every element, not only those with interaction
    // styles: they used to live in the interaction state, which exists only once such a style
    // is registered, so an unstyled element read false while the pointer was over it or it
    // had focus. The state is not created on demand either — its first recompute snapshots
    // the element's visuals, and a later one would revert an app's own changes.
    private bool _hovered, _pressed, _focused;

    /// <summary>True while the pointer is over this element or a descendant.</summary>
    public bool IsHovered => _hovered;
    /// <summary>True while the pointer is pressed on this element or a descendant.</summary>
    public bool IsPressed => _pressed;
    /// <summary>True while this element holds keyboard focus.</summary>
    public bool IsFocused => _focused;

    internal void AddHoverStyle(Action<Element> style) => Interaction.OnHover += style;
    internal void AddPressedStyle(Action<Element> style) => Interaction.OnPressed += style;
    internal void AddFocusStyle(Action<Element> style) => Interaction.OnFocus += style;
    internal void AddTransition(Animation.TransitionProperty property, float seconds, Func<float, float> easing) =>
        Interaction.AddTransition(property, seconds, easing);

    // Called by the event pipeline (PointerState / FocusManager) when interaction
    // state changes; no-ops when the element has no interaction styling registered.
    internal void SetInteractionHovered(bool value)
    {
        _hovered = value;
        if (_interaction is { } s && s.Hovered != value) { s.Hovered = value; s.Recompute(this, animate: true); }
    }
    internal void SetInteractionPressed(bool value)
    {
        _pressed = value;
        if (_interaction is { } s && s.Pressed != value) { s.Pressed = value; s.Recompute(this, animate: true); }
    }
    internal void SetInteractionFocused(bool value)
    {
        _focused = value;
        if (_interaction is { } s && s.Focused != value) { s.Focused = value; s.Recompute(this, animate: true); }
    }

    // Called by the reconciler after a full patch: the visual props now hold the new
    // base, so re-snapshot it and re-apply whatever states are currently active
    // (without animating — a rebuild shouldn't trigger transitions).
    internal void RefreshInteractionAfterPatch()
    {
        if (_interaction is { } s) { s.InvalidateBase(); s.Recompute(this, animate: false); }
    }

    /// <summary>
    /// Called by the reconciler after this element has been patched and its children
    /// reconciled. Self-managing elements (e.g. Tabs) override this to re-assert
    /// their internally-owned subtree, which the child reconciliation may have
    /// altered. Default is a no-op.
    /// </summary>
    internal virtual void OnReconciled() { }

    /// <summary>
    /// True for an element that creates its children itself rather than receiving them from
    /// a build — a virtualised list, whose rows are realized from a template during layout.
    /// The reconciler then leaves the live children alone when it patches the element: the
    /// incoming twin carries none, and clearing the live ones would unmount rows the element
    /// is about to put back, taking the keyboard focus inside one of them with it.
    /// </summary>
    internal virtual bool ManagesOwnChildren => false;

    /// <summary>
    /// Whether the platform's text input (the IME, and on SDL the text-input events at
    /// all) should be armed while this element has focus. Text boxes say yes; so does any
    /// other element that types, such as a number field. Without it a real window sends
    /// key downs only, and typed characters never arrive.
    /// </summary>
    internal virtual bool WantsTextInput => false;

    /// <summary>
    /// Content of this element that keeps its own accessibility tree — a web page's engine.
    /// The accessibility tree gives it to the node as
    /// <see cref="Accessibility.AccessibilityNode.NativeContent"/>. Empty by default.
    /// </summary>
    internal virtual Accessibility.NativeAccessibleContent NativeAccessibleContent => default;

    /// <summary>
    /// The value a screen reader reads for an element the framework's own types do not cover:
    /// a terminal's visible screen. Read on the UI thread when the accessibility tree is
    /// rebuilt, which happens only while an assistive tool listens, and by the Windows bridge on
    /// its own thread when a reader asks; null (the default) is none. An element whose value
    /// changes calls <see cref="NotifyAccessibilityPropertyChanged"/> with
    /// <see cref="Accessibility.AccessibilityProperties.Value"/>.
    /// </summary>
    internal virtual string? AccessibleValue => null;

    /// <summary>
    /// The text a screen reader can move through by character, word and line, with a caret and
    /// a selection: UIA's text pattern and AT-SPI's Text interface. Read when the accessibility
    /// tree is built; the source itself is then read by the bridges on their own threads. An
    /// element whose text, caret or selection change calls
    /// <see cref="NotifyAccessibilityPropertyChanged"/> (any property) so the tree is rebuilt
    /// and the difference told. Null (the default) is none.
    /// </summary>
    internal virtual Accessibility.IAccessibleTextSource? AccessibleTextSource => null;

    /// <summary>
    /// Has a screen reader speak <paramref name="text"/>, without moving its focus: a status, a
    /// toast, a program's new output. Spoken only while one listens (nothing is queued for a
    /// reader that starts later), on Windows as a UI Automation notification and on Linux as an
    /// AT-SPI announcement. An element not in a window yet (one announcing as it mounts, in a
    /// tree still being built) says it once a window takes it; one removed first never does.
    /// </summary>
    /// <param name="text">What to say.</param>
    /// <param name="politeness">Whether to wait for the reader to finish what it is saying
    /// (the default), or to cut in.</param>
    public void Announce(string text, Accessibility.AnnouncementPoliteness politeness = Accessibility.AnnouncementPoliteness.Polite)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (OverlayHost is Accessibility.IAccessibilityAnnouncer announcer) announcer.Announce(this, text, politeness);
        else (_pendingAnnouncements ??= []).Add((text, politeness));
    }

    // ──── Drag & Drop ────
    public bool Draggable { get; set; }

    // ──── Window Chrome ────
    public bool IsWindowDragRegion { get; set; }

    /// <summary>Marks the element as the window's maximize/restore button. On
    /// Windows this lets the custom frame report <c>HTMAXBUTTON</c> for the
    /// element's region so Windows 11 shows its Snap Layouts flyout on hover.</summary>
    public bool IsWindowMaximizeButton { get; set; }

    // ──── Overlay ────
    public IOverlayHost? OverlayHost
    {
        get => _overlayHost;
        internal set
        {
            _overlayHost = value;
            // Said before the element was in a window: say it now that it is.
            if (_pendingAnnouncements is { } pending && value is Accessibility.IAccessibilityAnnouncer announcer)
            {
                _pendingAnnouncements = null;
                foreach (var (text, politeness) in pending) announcer.Announce(this, text, politeness);
            }
        }
    }

    private IOverlayHost? _overlayHost;
    private List<(string Text, Accessibility.AnnouncementPoliteness Politeness)>? _pendingAnnouncements;

    // ──── Tooltip ────
    public string? TooltipText { get; set; }

    // ──── Context Menu ────
    public Func<IReadOnlyList<ContextMenuItem>>? ContextMenuItems { get; set; }

    // ──── Focus ────
    public bool IsFocusable { get; set; }

    /// <summary>
    /// Controls Tab traversal order, following HTML <c>tabindex</c> semantics:
    ///   <list type="bullet">
    ///     <item><c>&gt; 0</c>: visited first, ascending (ties broken by document order).</item>
    ///     <item><c>== 0</c> (default): visited after all positives, in document order.</item>
    ///     <item><c>&lt; 0</c>: focusable programmatically via <see cref="Focus"/> but skipped by Tab.</item>
    ///   </list>
    /// Has no effect unless <see cref="IsFocusable"/> is true.
    /// </summary>
    public int TabIndex { get; set; }

    internal FocusManager? FocusManager { get; set; }

    /// <summary>
    /// Programmatically focuses this element. The element must be focusable
    /// and attached to a tree that has a <see cref="Events.FocusManager"/>.
    /// </summary>
    public void Focus()
    {
        if (!IsFocusable) return;

        // FocusManager may not have been propagated yet (e.g. element was
        // just added to the tree and style resolution hasn't run). Walk up
        // the parent chain to find it.
        var fm = FocusManager;
        if (fm is null)
        {
            for (var p = Parent; p is not null; p = p.Parent)
            {
                if (p.FocusManager is not null) { fm = p.FocusManager; break; }
            }
        }

        fm?.SetFocus(this);
    }

    // ──── Accessibility ────
    public AccessibilityRole Role { get; set; } = AccessibilityRole.None;
    public string? AccessibleName { get; set; }

    /// <summary>
    /// Expanded state for an expandable node (e.g. a tree item), exposed as the
    /// UIA ExpandCollapse pattern: <c>null</c> = not expandable, <c>true</c> =
    /// expanded, <c>false</c> = collapsed. Expand/Collapse from assistive tech
    /// reuse the element's click handler, so wire the node to toggle on click.
    /// </summary>
    public bool? AccessibleExpanded { get; set; }
    public string? AccessibleDescription { get; set; }

    /// <summary>
    /// Set by <c>Window</c> on the root when a platform accessibility bridge is
    /// active — named rather than linked because Window lives in the HultUI
    /// assembly above this one, which Core does not reference.
    /// Descendants discover it by walking up <see cref="Parent"/>
    /// in <see cref="NotifyAccessibilityPropertyChanged"/>.
    /// </summary>
    internal IElementPropertyObserver? AccessibilityObserver { get; set; }

    /// <summary>
    /// Announces that an accessibility-visible property on this element changed
    /// (e.g. a checkbox was toggled, a text input's value was replaced). Walks
    /// up the parent chain to deliver to the root-scoped observer, if any.
    /// Use constants from <see cref="AccessibilityProperties"/> for the name.
    /// </summary>
    protected internal void NotifyAccessibilityPropertyChanged(string propertyName)
    {
        for (Element? current = this; current is not null; current = current.Parent)
        {
            if (current.AccessibilityObserver is { } observer)
            {
                observer.OnElementPropertyChanged(this, propertyName);
                return;
            }
        }
    }

    // ──── Refs ────
    // Refs registered via WithRef(). During PatchFrom the source element's
    // refs are repointed to the live (target) element so that handler
    // closures which captured the Ref always reach the in-tree element.
    internal List<IRef>? _refs;

    /// <summary>
    /// Register a <see cref="Ref{T}"/> on this element. The ref's
    /// <c>Current</c> is set to this element immediately, and will be
    /// updated to the live element when PatchFrom runs during reconciliation.
    /// </summary>
    public Element WithRef<T>(Ref<T> r) where T : Element
    {
        _refs ??= [];
        _refs.Add(r);
        r.Current = (T)this;
        return this;
    }

    // ──── Events ────
    private Dictionary<Type, List<EventHandlerEntry>>? _handlers;

    /// <summary>
    /// How many dispatches are currently walking this element's handler lists. While it is
    /// non-zero, anything that changes a list replaces the instance instead of mutating it,
    /// so the walk in progress keeps seeing the handlers that were attached when it started.
    /// Nested because one dispatch can synchronously trigger another (a click handler that
    /// moves focus fires blur).
    /// </summary>
    private int _dispatchDepth;

    /// <summary>
    /// The list to mutate for <paramref name="type"/>, copied first if a dispatch is walking
    /// it. Copy-on-write is what lets <see cref="RunHandlers"/> iterate the live list with
    /// no per-event snapshot: pointer storms and drags dispatch continuously, and that
    /// snapshot was the highest-frequency allocation in the framework.
    /// </summary>
    private List<EventHandlerEntry> ListForMutation(Type type)
    {
        _handlers ??= [];
        if (!_handlers.TryGetValue(type, out var list))
            return _handlers[type] = [];

        if (_dispatchDepth > 0)
            _handlers[type] = list = [.. list];

        return list;
    }

    public void On<TEvent>(Action<TEvent> handler) where TEvent : ElementEvent
    {
        On(EventPhase.Bubble, handler);
    }

    public void On<TEvent>(EventPhase phase, Action<TEvent> handler) where TEvent : ElementEvent =>
        AddHandler(phase, handler, owned: false);

    /// <summary>
    /// Register an element-owned handler: part of this element's own machinery
    /// (constructor wiring, handlers on internally-built children). Owned handlers
    /// survive reconciliation; app handlers registered via <see cref="On{TEvent}(Action{TEvent})"/>
    /// are replaced by the incoming build's handlers in <see cref="PatchFrom"/>.
    /// </summary>
    internal void OnOwn<TEvent>(Action<TEvent> handler) where TEvent : ElementEvent =>
        AddHandler(EventPhase.Bubble, handler, owned: true);

    /// <summary>
    /// Registers a handler that promises to mark dirty itself whenever it changes
    /// anything visible. The input pipeline renders a frame whenever an ordinary
    /// handler ran (a handler may mutate without marking — the imperative-mutation
    /// safety net); a passive one is exempt, so a high-frequency listener that
    /// usually does nothing — a drag tracker on a surface root, gated on
    /// "is a drag active" — does not turn every pointer move over an idle window
    /// into a frame. Same reconciliation lifecycle as <see cref="On{TEvent}(Action{TEvent})"/>.
    /// </summary>
    public void OnPassive<TEvent>(Action<TEvent> handler) where TEvent : ElementEvent =>
        AddHandler(EventPhase.Bubble, handler, owned: false, passive: true);

    private void AddHandler<TEvent>(EventPhase phase, Action<TEvent> handler, bool owned, bool passive = false) where TEvent : ElementEvent
    {
        // A handler can register another handler on the same element mid-dispatch, so this
        // goes through the copy-on-write path like every other mutation.
        ListForMutation(typeof(TEvent))
            .Add(new EventHandlerEntry(phase, evt => handler((TEvent)evt), owned, passive));
    }

    internal bool HasEventHandler<TEvent>() where TEvent : ElementEvent =>
        _handlers is not null && _handlers.ContainsKey(typeof(TEvent));

    internal bool HasAnyEventHandlers => _handlers is not null && _handlers.Count > 0;

    /// <summary>
    /// Names of the event types this element has handlers for, for DevTools
    /// introspection ("why doesn't my click fire" is usually "nothing is listening").
    /// </summary>
    internal IEnumerable<string> HandlerEventNames =>
        _handlers is null ? [] : _handlers.Keys.Select(static t => t.Name);

    /// <summary>
    /// Hold this element's handler lists still, and hand back the one for
    /// <paramref name="eventType"/> — null when nothing listens for it. While pinned,
    /// anything that changes a list replaces the instance instead of mutating it, so the
    /// returned list keeps exactly the handlers that were attached at this moment, whatever
    /// a rebuild does to the element in the meantime.
    ///
    /// <para><see cref="EventDispatcher.Dispatch"/> pins the whole path up front, which is
    /// what makes "the handlers that were live when the event was dispatched" true for the
    /// ancestors as well as for the element in flight. Always paired with
    /// <see cref="UnpinHandlers"/> in a finally.</para>
    /// </summary>
    internal List<EventHandlerEntry>? PinHandlers(Type eventType)
    {
        _dispatchDepth++;
        if (_handlers is null) return null;
        return _handlers.TryGetValue(eventType, out var list) ? list : null;
    }

    /// <summary>Releases one <see cref="PinHandlers"/>.</summary>
    internal void UnpinHandlers() => _dispatchDepth--;

    internal void InvokeHandlers(ElementEvent evt, EventPhase phase)
    {
        if (_handlers is null) return;
        if (!_handlers.TryGetValue(evt.GetType(), out var list)) return;

        // No snapshot. A handler can MarkDirty → synchronous rebuild → PatchFrom, which
        // swaps this element's app handlers mid-dispatch; _dispatchDepth makes every such
        // mutation replace the list instance rather than touch this one, so `list` still
        // holds exactly the handlers that were attached when dispatch started.
        _dispatchDepth++;
        try
        {
            RunHandlers(evt, phase, list);
        }
        finally
        {
            _dispatchDepth--;
        }
    }

    /// <summary>
    /// Runs the handlers a caller already pinned (see <see cref="PinHandlers"/>), which is
    /// how a bubbling dispatch reaches an ancestor: the list is the one that ancestor had
    /// when the event started, not whatever an earlier handler in the same walk rebuilt it
    /// into.
    /// </summary>
    internal void InvokeHandlers(ElementEvent evt, EventPhase phase, List<EventHandlerEntry>? pinned)
    {
        if (pinned is not null)
            RunHandlers(evt, phase, pinned);
    }

    private void RunHandlers(ElementEvent evt, EventPhase phase, List<EventHandlerEntry> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            if (entry.Phase != phase && phase != EventPhase.Target)
                continue;

            // Only handlers that may mutate without marking count toward the input
            // pipeline's "a handler ran, render a frame" safety net. Owned handlers
            // are the framework's own machinery and passive ones have promised to
            // mark dirty themselves — counting either turns a high-frequency no-op
            // listener (a drag tracker on a surface root) into a full frame per
            // pointer move over an idle window.
            if (!entry.Owned && !entry.Passive)
                EventDispatcher.HandlerInvocationCount++;
            entry.Handler(evt);
            if (evt.StopPropagation) break;
        }
    }

    // ──── Dirty tracking ────
    internal DirtyFlags DirtyFlags { get; set; } = DirtyFlags.All;

    // ──── Layout cache (used by LayoutEngine for subtree skipping) ────
    internal float CacheConstraintW;
    internal float CacheConstraintH;
    internal float? CacheForcedW;
    internal float? CacheForcedH;
    internal Size CacheResultSize;

    // ──── Measure cache (Phase 1 sizing without writing Layout) ────
    // Each ancestor's flex Phase 1 calls MeasureSize on this element. Without
    // this cache, that's O(depth) measure passes per frame — see LayoutEngine
    // and MeasureCache for the full rationale.
    internal MeasureCache MeasureCache;

    /// <summary>
    /// Marks this element (and ancestors up to root) as needing layout on the next frame.
    /// Call this after directly mutating <see cref="Flex"/>, <see cref="Box"/>, or
    /// <see cref="Position"/> style properties at runtime — those property setters do not
    /// auto-invalidate, so the layout pass would otherwise see no reason to re-run.
    /// Most code does not need this: builder fluent calls happen before the first layout
    /// pass, and component rebuilds invalidate via PatchFrom. This is the escape hatch
    /// for imperative drag/animation/resize code that mutates styles directly.
    /// </summary>
    public void MarkLayoutDirty()
    {
        // The four Mark*Dirty methods are where every element mutation ends up — the
        // fluent setters, the imperative escape hatch, and the reconciler all funnel
        // here. Guarding them covers direct tree mutation the way the Signal setter
        // covers reactive state. Each walks to the root, so one thread-id comparison
        // is not measurable against the work already being done.
        UIThread.VerifyAccess("Element.MarkLayoutDirty");

        var current = (Element?)this;
        while (current is not null)
        {
            if ((current.DirtyFlags & DirtyFlags.Layout) != 0)
                break;
            current.DirtyFlags |= DirtyFlags.Layout;
            // Invalidate the cached MeasureSize result for this element and
            // every ancestor: the changed subtree may produce a different
            // outer size at the next Phase 1 cascade. The layout cache
            // (CacheConstraintW/H/...) is gated by DirtyFlags.Layout, so it's
            // already invalidated implicitly.
            current.MeasureCache.Invalidate();
            current = current.Parent;
        }
    }

    /// <summary>
    /// Marks this element (and ancestors up to root) as needing repaint on the next frame
    /// without re-running layout. Use after direct mutations that only affect appearance
    /// (background color, opacity, etc.) so the change is visible without a full layout pass.
    /// </summary>
    public void MarkPaintDirty()
    {
        UIThread.VerifyAccess("Element.MarkPaintDirty");

        var current = (Element?)this;
        while (current is not null)
        {
            if ((current.DirtyFlags & DirtyFlags.Paint) != 0)
                break;
            current.DirtyFlags |= DirtyFlags.Paint;
            current = current.Parent;
        }
    }

    /// <summary>
    /// Marks this element (and ancestors up to root) as having had a structural
    /// change to its children. Distinct from <see cref="MarkLayoutDirty"/>: only
    /// child add/remove/replace sets this, so the accessibility tree can rebuild
    /// on real structure changes without rebuilding on every resize or animation.
    /// </summary>
    internal void MarkChildrenDirty()
    {
        UIThread.VerifyAccess("Element.MarkChildrenDirty");

        var current = (Element?)this;
        while (current is not null)
        {
            if ((current.DirtyFlags & DirtyFlags.Children) != 0)
                break;
            current.DirtyFlags |= DirtyFlags.Children;
            current = current.Parent;
        }
    }

    /// <summary>
    /// Marks this element and its descendants as needing inherited style re-resolved on
    /// the next frame. Call this after directly mutating <see cref="Inherited"/> — those
    /// setters do not auto-invalidate, so the style pass would otherwise skip the subtree
    /// and every descendant would keep the value it resolved earlier.
    ///
    /// <para>This re-resolves; it does not re-measure. Anything that changes text metrics
    /// — <c>FontFamily</c>, <c>FontSize</c>, <c>FontWeight</c>, <c>FontSlant</c>,
    /// <c>LineHeight</c>, <c>LetterSpacing</c>, <c>TextAlign</c>, <c>TextDirection</c> —
    /// needs <see cref="MarkLayoutDirty"/> as well, or the text redraws at its old
    /// measured size. <c>TextColor</c>, <c>Decoration</c> and <c>Cursor</c> need only
    /// this call.</para>
    ///
    /// <para>Like <see cref="MarkLayoutDirty"/>, this is the escape hatch for imperative
    /// code. Builder chains run before the first style pass and component rebuilds
    /// invalidate through PatchFrom, so neither needs it.</para>
    /// </summary>
    public void MarkStyleDirty()
    {
        UIThread.VerifyAccess("Element.MarkStyleDirty");

        // Propagate down (inherited style may change for descendants)
        MarkStyleDirtyDown();
        // Propagate up so root knows style work is needed
        var current = Parent;
        while (current is not null)
        {
            if ((current.DirtyFlags & DirtyFlags.Style) != 0)
                break;
            current.DirtyFlags |= DirtyFlags.Style;
            current = current.Parent;
        }
    }

    private void MarkStyleDirtyDown()
    {
        DirtyFlags |= DirtyFlags.Style;
        if (_children is null) return;
        foreach (var child in _children)
            child.MarkStyleDirtyDown();
    }

    // ──── Lifecycle ────
    protected virtual void OnMounted() { }
    protected virtual void OnUnmounted() { }

    // Whether OnMounted has fired more recently than OnUnmounted. Not the same as having a
    // parent: an element deep in a removed subtree keeps its parent and is unmounted.
    private bool _mounted;

    /// <summary>
    /// Mounts this element and anything under it that is not mounted, children first: the
    /// order a freshly built tree mounts in, since a builder adds each child to its parent
    /// before that parent has one of its own. A fresh subtree's children are all mounted
    /// already, so this reads only the direct children; it goes deeper only into a subtree
    /// coming back after a removal unmounted it.
    /// </summary>
    private void Mount()
    {
        if (_children is not null)
        {
            // By index: a component mounting can write signals that rebuild other parts of
            // the tree, and a foreach would throw on the change.
            for (int i = 0; i < _children.Count; i++)
                if (!_children[i]._mounted) _children[i].Mount();
        }
        _mounted = true;
        OnMounted();
        // A freshly mounted subtree has no resolved inherited style yet, so it would
        // measure with a zero font size (zero-height text) until some later restyle. Flag
        // it so the next frame's style pass resolves it before layout. Structural ops
        // otherwise mark only layout/children.
        MarkStyleDirty();
    }

    /// <summary>
    /// Unmounts this element and every mounted element under it, top down, so a component
    /// host stops rebuilding before the elements it built hear about it. The walk covers
    /// the whole subtree: the removal already cost a build of at least that size.
    /// </summary>
    private void Unmount()
    {
        if (_mounted)
        {
            _mounted = false;
            _pendingAnnouncements = null; // gone before a window took it: a rebuild's twin
            OnUnmounted();
            // Its synthetic accessibility nodes (list items, described content) go with it:
            // their ids stop resolving rather than ever naming a newer element.
            Accessibility.AccessibleOwnerSlots.Release(Id);
        }
        if (_children is null) return;
        for (int i = 0; i < _children.Count; i++) // by index: see Mount
            _children[i].Unmount();
    }

    // ──── Tree mutation ────
    //
    // Lifecycle contract:
    //   OnMounted  fires when an element gets a parent while unmounted: the first time it
    //              enters a tree, or when it comes back after a removal. Everything under
    //              it that the removal unmounted mounts with it.
    //   OnUnmounted fires when an element permanently leaves a tree, and on every element
    //              under it: a component hosted three levels down has left too.
    //
    // When an element is *moved* (it is mounted and is added to a different parent),
    // neither lifecycle event fires — the element was never truly removed. This
    // prevents ComponentHost from tearing down its reactive Effect every time a parent
    // component rebuilds and passes the same child through a helper like
    // SplitPane.Build(). The builder moves the host into the new tree before the
    // reconciler removes the old one, so the old one no longer contains it.

    public void AddChild(Element child)
    {
        if (child.Parent is not null)
            DetachSilently(child);

        child.Parent = this;
        (_children ??= []).Add(child);
        MarkLayoutDirty();
        MarkChildrenDirty();

        if (!child._mounted)
            child.Mount();
    }

    public void InsertChild(int index, Element child)
    {
        if (child.Parent is not null)
            DetachSilently(child);

        child.Parent = this;
        (_children ??= []).Insert(index, child);
        MarkLayoutDirty();
        MarkChildrenDirty();

        if (!child._mounted)
            child.Mount();
    }

    public void RemoveChild(Element child)
    {
        if (_children?.Remove(child) == true)
        {
            ClearFocusIfRemoved(child);
            child.Unmount();
            child.Parent = null;
            // The style pass copied the window onto every element it resolved; left in
            // place, it tells IsHosted this subtree is still on screen.
            child.OverlayHost = null;
            MarkLayoutDirty();
            MarkChildrenDirty();
        }
    }

    public void ReplaceChildren(params Element[] children)
    {
        // Take incoming children out of their current parents first. One that sits inside
        // a child about to leave is being moved, and must not be unmounted with it.
        foreach (var child in children)
        {
            if (child.Parent is not null && !ReferenceEquals(child.Parent, this))
                DetachSilently(child);
        }

        // Remove old children. Only unmount elements that are genuinely
        // leaving — not those that reappear in the new list.
        var newSet = new HashSet<Element>(children);
        if (_children is not null)
        {
            foreach (var old in _children)
            {
                if (!newSet.Contains(old))
                {
                    ClearFocusIfRemoved(old);
                    old.Unmount();
                    old.OverlayHost = null; // see RemoveChild
                }
                old.Parent = null;
            }
            _children.Clear();
        }

        // Add new children.
        var list = _children ??= new List<Element>(children.Length);
        foreach (var child in children)
        {
            if (child.Parent is not null) // only an element listed twice
                DetachSilently(child);

            child.Parent = this;
            list.Add(child);

            if (!child._mounted)
                child.Mount();
        }
        MarkLayoutDirty();
        MarkChildrenDirty();
    }

    /// <summary>
    /// Removes <paramref name="child"/> from its current parent without
    /// firing lifecycle events.  Used for element moves — the child is not
    /// leaving the tree, just changing parents.
    /// </summary>
    private static void DetachSilently(Element child)
    {
        child.Parent?._children?.Remove(child);
        child.Parent?.MarkLayoutDirty();
        child.Parent?.MarkChildrenDirty();
        child.Parent = null;
    }

    private void ClearFocusIfRemoved(Element removed)
    {
        var fm = FocusManager ?? removed.FocusManager;
        if (fm?.FocusedElement is { } focused && ContainsElement(removed, focused))
            fm.ClearFocus();
    }

    private static bool ContainsElement(Element root, Element candidate)
    {
        for (Element? current = candidate; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
                return true;
        }
        return false;
    }

    // ──── Patching (used by TreeDiffer) ────
    internal virtual void PatchFrom(Element source)
    {
        Key = source.Key;
        Name = source.Name;
        CopyOriginFrom(source);
        // Copy styles without forcing allocation on either side: when the source
        // never touched a style and the target did, reset the target to defaults
        // (copying from the shared immutable default); when neither side has one,
        // there is nothing to do.
        if (source._flex is not null) Flex.CopyFrom(source._flex);
        else _flex?.CopyFrom(FlexStyle.Default);
        if (source._box is not null) Box.CopyFrom(source._box);
        else _box?.CopyFrom(BoxStyle.Default);
        if (source._position is not null) Position.CopyFrom(source._position);
        else _position?.CopyFrom(PositionStyle.Default);
        if (source._grid is not null) Grid.CopyFrom(source._grid);
        else _grid?.CopyFrom(GridStyle.Default);
        if (source._gridItem is not null) GridItem.CopyFrom(source._gridItem);
        else _gridItem?.CopyFrom(GridItemStyle.Default);
        if (source._inherited is not null) Inherited.CopyFrom(source._inherited);
        else _inherited?.CopyFrom(InheritedStyle.Default);
        IsFocusable = source.IsFocusable;
        TabIndex = source.TabIndex;
        Draggable = source.Draggable;
        IsWindowDragRegion = source.IsWindowDragRegion;
        IsWindowMaximizeButton = source.IsWindowMaximizeButton;
        TooltipText = source.TooltipText;
        ContextMenuItems = source.ContextMenuItems;
        _opacity = source._opacity;
        Transform = source.Transform;
        // Interaction config follows the latest build, like event handlers: overrides
        // are pure functions of the element, so the incoming build's config carries
        // across reconciliation — and an incoming element WITHOUT interaction clears
        // any stale config (a conditionally-attached hover style must disappear when
        // its condition stops holding). The base snapshot is refreshed and the active
        // states re-applied in RefreshInteractionAfterPatch, once all visual props
        // have been copied.
        if (source._interaction is { } sourceInteraction)
            Interaction.CopyConfigFrom(sourceInteraction);
        else
            _interaction?.ClearConfig();
        Role = source.Role;
        AccessibleName = source.AccessibleName;
        AccessibleDescription = source.AccessibleDescription;
        AutomationId = source.AutomationId;
        AccessibleExpanded = source.AccessibleExpanded;
        // App event handlers ARE copied: the incoming build is the source of truth,
        // so a retained element always runs the latest build's handlers (matching how
        // callback props like ButtonElement.Clicked already behave). Element-OWNED
        // handlers (constructor wiring registered via OnOwn) are kept instead — the
        // incoming element's owned closures capture the incoming (detached) instance.
        // Caveat for app code: a handler that captures a *sibling element* from its own
        // build now points at a detached element after the next patch — reach across
        // elements via Ref<T> (repointed below), never via captured element locals.
        CopyAppHandlersFrom(source);
        if (source._refs != null)
        {
            foreach (var r in source._refs)
                r.SetTarget(this);
        }

        MarkLayoutDirty();
        MarkPaintDirty();
        MarkStyleDirty();
    }

    /// <summary>
    /// Drop this element's app handlers and adopt the incoming build's, preserving
    /// element-owned (constructor-wired) handlers and their registration order:
    /// owned handlers always precede app handlers, both at creation and after a patch.
    /// </summary>
    private void CopyAppHandlersFrom(Element source)
    {
        if (_handlers is not null)
        {
            List<Type>? empty = null;
            // Collect first, apply after: this runs mid-dispatch often enough (a handler
            // marks dirty and the rebuild lands here) that mutating while enumerating the
            // dictionary is not worth relying on.
            List<(Type Type, List<EventHandlerEntry> List)>? replacements = null;

            foreach (var (type, list) in _handlers)
            {
                if (_dispatchDepth > 0)
                {
                    var kept = list.FindAll(static e => e.Owned);
                    if (kept.Count == 0) (empty ??= []).Add(type);
                    else (replacements ??= []).Add((type, kept));
                    continue;
                }

                list.RemoveAll(static e => !e.Owned);
                if (list.Count == 0)
                    (empty ??= []).Add(type);
            }

            if (replacements is not null)
                foreach (var (type, kept) in replacements)
                    _handlers[type] = kept;
            if (empty is not null)
                foreach (var type in empty)
                    _handlers.Remove(type);
        }

        if (source._handlers is null)
            return;
        foreach (var (type, list) in source._handlers)
        {
            foreach (var entry in list)
            {
                if (entry.Owned)
                    continue;
                ListForMutation(type).Add(entry);
            }
        }
    }

    // ──── Rendering ────
    public virtual void Render(IRenderContext ctx) { }

    public virtual bool HitTest(Point point) =>
        Layout.AbsoluteBorderBox.Contains(point);

    /// <summary>
    /// Whether this element can itself be the target of a pointer. Its children still
    /// can: a full-width strip that centres a toast over the window's bottom edge must
    /// not steal the clicks meant for whatever sits under it, while the toast inside it
    /// stays clickable. CSS calls the same thing <c>pointer-events: none</c>.
    /// </summary>
    public bool IsHitTestVisible { get; set; } = true;

    // ──── Measurement (overridden by leaf elements) ────
    /// <summary>The shape this element's content has of its own — a picture's width over its
    /// height — or 0 for none. The layout keeps it when the app gives one side and leaves the
    /// other auto (see <see cref="BoxStyle.AspectRatio"/>, which takes precedence).</summary>
    internal virtual float NaturalAspectRatio => 0f;

    internal virtual Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx) =>
        Size.Zero;

    /// <summary>
    /// The narrowest content width below which this element's own content genuinely
    /// breaks, used by the automatic minimum size (docs/plans/13-min-content.md).
    /// Measuring at zero available width answers that for most leaves; text
    /// overrides it, because zero width wraps text at every character and reports
    /// the widest character rather than the widest word.
    /// </summary>
    internal virtual float MeasureMinContentWidth(IMeasureContext ctx) =>
        MeasureContent(0f, LayoutSpace.Unbounded, ctx).Width;

    /// <summary>
    /// Where this element's own first line of text sits: the distance from the top of its
    /// border box to the baseline, at the border-box size given — what
    /// <see cref="AlignItems.Baseline"/> lines items up by. Null for an element that draws no
    /// text of its own (a box, an image, a slider). Layout then takes a container's baseline
    /// from its first child, and gives a leaf without one a baseline at the bottom of its
    /// border box, as CSS does. An element that draws text must answer with where it really
    /// draws it — the same font, the same offsets — or the text it lines up with will not line up.
    /// </summary>
    internal virtual float? MeasureBaseline(Size borderBox, IMeasureContext ctx) => null;

    /// <summary>Distance from the top of the border box to the top of the content box.</summary>
    private protected float ContentTop => BoxRead.BorderWidth.Top + BoxRead.Padding.Top;

    /// <summary>The content box a border box of <paramref name="borderBox"/> leaves.</summary>
    private protected Size ContentSizeOf(Size borderBox)
    {
        var spacing = BoxRead.BorderWidth + BoxRead.Padding;
        return new Size(Math.Max(0, borderBox.Width - spacing.Horizontal), Math.Max(0, borderBox.Height - spacing.Vertical));
    }
}

internal readonly record struct EventHandlerEntry(
    EventPhase Phase, Action<ElementEvent> Handler, bool Owned = false, bool Passive = false);
