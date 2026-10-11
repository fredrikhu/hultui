using HultUI.Core.Accessibility;
using HultUI.Core.Animation;
using HultUI.Core.Components;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// A header that shows and hides a body below it: a chevron, a title, optionally actions at
/// the end of the header, and the content. Click the header, or Enter or Space on it, and the
/// body slides open while the chevron turns; the same again closes it.
/// </summary>
/// <remarks>
/// <para><b>State.</b> Left alone it keeps its own: a rebuild does not close an expander the
/// user opened. <see cref="Expanded"/> hands the state to the app instead — every build then
/// says open or closed, as with <see cref="TabsElement.Selected"/> — and
/// <see cref="OnExpandedChanged"/> reports the user's toggles either way.</para>
///
/// <para><b>Body.</b> A closed body is out of the tree: nothing in it is laid out, reached by
/// Tab or read by a screen reader. Given as an element it is kept, so a text box in it keeps
/// what was typed while it is closed, and a rebuild still patches it; given as a function it
/// is built only while open and dropped on closing.</para>
///
/// <para><b>Actions.</b> Elements at the end of the header — a count, an "Add" button — that
/// are part of the header's row but not of its toggle: clicking one does not open or close
/// the body.</para>
///
/// <para><b>Accessibility.</b> The header is a button named by the title, with the expanded
/// state, so UIA's ExpandCollapse and AT-SPI's expandable/expanded states come with it.</para>
///
/// <para>The element owns its children (<see cref="ManagesOwnChildren"/>): the body comes and
/// goes with the state, which a rebuild that only says "here is the content" cannot know, so
/// the app's header, actions and content are patched into the live parts here.</para>
/// </remarks>
public class ExpanderElement : BoxElement
{
    private readonly HeaderRowElement _header;
    private readonly ChevronElement _chevron = new();
    private readonly BoxElement _titleSlot = new();
    private readonly TextElement _titleText = TextElement.New("");
    private readonly BoxElement _actions = new();
    private readonly BoxElement _body = new();
    private readonly BoxElement _inner = new();

    private string _title = "";
    private Element? _headerContent;
    private List<Element> _actionItems = [];
    private Element? _content;
    private Func<Element>? _factory;

    private bool _expanded;
    private bool _controlled;
    /// <summary>How open the body is drawn, 0 closed to 1 open; the chevron turns with it.</summary>
    private float _progress;
    private TweenHandle _tween;
    private Action<bool>? _onExpandedChanged;

    private ExpanderTheme _theme = new();
    private Color _chevronColor = new(100, 116, 139);
    private Color _hover = new(241, 245, 249);

    internal override bool ManagesOwnChildren => true;

    public ExpanderElement()
    {
        Flex.Direction = FlexDirection.Column;
        // Its height is its content, as a button's is: squeezed in a column, the body would
        // spill over whatever comes next.
        Flex.FlexShrink = 0;
        _header = new HeaderRowElement(this);
        _titleSlot.Flex.FlexGrow = 1;
        _titleSlot.Flex.FlexShrink = 1;
        // No JustifyContent: the slot is as tall as its title, so it has no space to centre
        // in. The header's AlignItems centres the slot, which is what is wanted.
        _titleSlot.Flex.Direction = FlexDirection.Column;
        _actions.Flex.Direction = FlexDirection.Row;
        _actions.Flex.AlignItems = AlignItems.Center;
        _actions.Flex.FlexShrink = 0;
        _body.Flex.Direction = FlexDirection.Column;
        _body.Flex.FlexShrink = 0;
        // The content keeps its own height inside a body that is shorter while it opens;
        // that height is what the body grows to.
        _inner.Flex.Direction = FlexDirection.Column;
        _inner.Flex.FlexShrink = 0;
        _body.AddChild(_inner);
        _titleSlot.AddChild(_titleText);
        StyleParts();
        ArrangeHeader();
        Arrange();
    }

    /// <summary>The header's text, and its accessible name whatever <see cref="Header"/> shows.</summary>
    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            _titleText.Content = value;
            NameHeader();
        }
    }

    /// <summary>Whether the body is open, or opening. Setting it opens or closes the body
    /// without reporting to <see cref="OnExpandedChanged"/>.</summary>
    public bool IsExpanded
    {
        get => _expanded;
        set => SetExpanded(value, notify: false);
    }

    /// <summary>How far open the body is drawn, 0 to 1. Exposed so a test can tell the motion
    /// finished rather than trusting it started.</summary>
    public float Progress => _progress;

    /// <summary>The header, the toggle: a button named by <see cref="Title"/>.</summary>
    public Element HeaderRow => _header;

    /// <summary>The content, while it exists: always for one given as an element, only while
    /// open for one given as a function.</summary>
    public Element? Content => _content;

    // ──── Fluent configuration ────

    /// <summary>Hands the state to the app: open or closed is whatever the latest build says.
    /// Pair it with <see cref="OnExpandedChanged"/>, or a click is undone by the next build.</summary>
    public ExpanderElement Expanded(bool open)
    {
        _controlled = true;
        SnapTo(open);
        return this;
    }

    /// <summary>Starts open (or not) and keeps its own state from then on.</summary>
    public ExpanderElement DefaultExpanded(bool open = true)
    {
        SnapTo(open);
        return this;
    }

    /// <summary>Runs with the new state when the user opens or closes it.</summary>
    public ExpanderElement OnExpandedChanged(Action<bool> handler)
    {
        _onExpandedChanged = handler;
        return this;
    }

    /// <summary>Shows <paramref name="header"/> in place of the title text — an icon and a
    /// second line, say. The title still names the toggle.</summary>
    public ExpanderElement Header(Element header)
    {
        _headerContent = header;
        _titleSlot.ReplaceChildren(header);
        NameHeader();
        return this;
    }

    /// <summary>Elements at the end of the header that do not toggle it.</summary>
    public ExpanderElement Actions(params Element[] actions)
    {
        _actionItems = [.. actions];
        _actions.ReplaceChildren(actions);
        ArrangeHeader();
        NameHeader();
        return this;
    }

    internal void SetContent(Element? content)
    {
        _factory = null;
        _content = content;
        _inner.ReplaceChildren(content is null ? [] : [content]);
    }

    internal void SetContent(Func<Element> factory)
    {
        _factory = factory;
        _content = null;
        _inner.ReplaceChildren();
        if (_expanded) EnsureContent();
    }

    // ──── State ────

    private void Toggle() => SetExpanded(!_expanded, notify: true);

    private void SetExpanded(bool open, bool notify)
    {
        if (_expanded == open) return;
        _expanded = open;
        _header.AccessibleExpanded = open;
        _header.NotifyAccessibilityPropertyChanged(AccessibilityProperties.IsExpanded);
        if (open) EnsureContent();
        else LeaveBody();
        MoveTo(open ? 1f : 0f);
        if (notify) _onExpandedChanged?.Invoke(open);
    }

    /// <summary>The state without motion: for a builder, before anything is on screen.</summary>
    private void SnapTo(bool open)
    {
        _tween.Cancel();
        _expanded = open;
        _header.AccessibleExpanded = open;
        if (open) EnsureContent();
        SetProgress(open ? 1f : 0f);
        Settle();
    }

    private void MoveTo(float to)
    {
        _tween.Cancel();
        // Only an expander on screen has a body to slide; the incoming twin of a rebuild and
        // one built but not yet shown just take the state.
        if (!IsHosted || _theme.TransitionSeconds <= 0f)
        {
            SetProgress(to);
            Settle();
            return;
        }
        Arrange();
        // A reversal mid-way takes the share of the time that is left to undo.
        float seconds = _theme.TransitionSeconds * MathF.Abs(to - _progress);
        _tween = Animate.Value(_progress, to, SetProgress)
            .Duration(seconds)
            .Ease(Easing.EaseOutCubic)
            .OnComplete(Settle)
            .Start();
    }

    private void SetProgress(float value)
    {
        _progress = value;
        _chevron.Turn = value;
        _chevron.MarkPaintDirty();
        SizeBody();
    }

    /// <summary>The motion is over: a closed body leaves the tree, and an open one goes back
    /// to its own height, so content that grows afterwards is not cut off.</summary>
    private void Settle()
    {
        if (!_expanded && _factory is not null)
        {
            _content = null;
            _inner.ReplaceChildren();
        }
        Arrange();
        SizeBody();
    }

    private void EnsureContent()
    {
        if (_factory is null || _content is not null) return;
        _content = _factory();
        _inner.ReplaceChildren(_content);
    }

    /// <summary>A body that is closing takes the keyboard with it; the header gets it, as the
    /// thing that was just used to close it.</summary>
    private void LeaveBody()
    {
        if (FocusManager?.FocusedElement is not { } focused) return;
        for (Element? e = focused; e is not null; e = e.Parent)
        {
            if (ReferenceEquals(e, _body))
            {
                _header.Focus();
                return;
            }
        }
    }

    private void SizeBody()
    {
        if (_expanded && _progress >= 1f)
        {
            _body.Box.Height = Dimension.Auto;
            _body.Box.OverflowY = Overflow.Visible;
        }
        else
        {
            // The content's height from the last layout: on the first frame of opening it
            // has not had one and the body starts at nothing, which it would anyway.
            float natural = _inner.Layout.Height + _body.Box.BorderWidth.Vertical;
            _body.Box.Height = Dimension.Px(natural * _progress);
            _body.Box.OverflowY = Overflow.Hidden;
        }
        _body.MarkLayoutDirty();
    }

    private bool BodyShown => _expanded || _progress > 0f;

    private void Arrange()
    {
        bool shown = BodyShown;
        bool present = Children.Count == 2;
        if (shown != present || Children.Count == 0)
            ReplaceChildren(shown ? [_header, _body] : [_header]);
        Corners(shown);
    }

    /// <summary>
    /// The header is the expander as far as assistive technology goes. Showing only the title,
    /// it is named by that text, which then is not read a second time as a label inside it;
    /// with a header of the app's or actions in it, by the title outright, so the name is not
    /// the header's every word. An automation id given to the expander is the header's.
    /// </summary>
    private void NameHeader()
    {
        _header.AccessibleName = _headerContent is null && _actionItems.Count == 0 ? null : _title;
        _header.AutomationId = AutomationId;
    }

    private void ArrangeHeader()
    {
        var parts = new List<Element>(4);
        bool leading = _theme.Chevron == ExpanderChevron.Leading;
        if (leading) parts.Add(_chevron);
        parts.Add(_titleSlot);
        if (_actionItems.Count > 0) parts.Add(_actions);
        if (!leading) parts.Add(_chevron);
        _header.ReplaceChildren([.. parts]);
    }

    // ──── Look ────

    protected internal override void ApplyTheme(Theme theme)
    {
        var t = theme.Expander;
        bool chevronMoved = t.Chevron != _theme.Chevron;
        // StyleParts writes the parts' own box and flex styles, which mark nothing: a theme that
        // changes them lays out each part they size, and one that keeps them (a light/dark
        // switch) lays out nothing. The title's font goes through the style pass.
        bool resized = chevronMoved || !SameLayout(_theme, t);
        _theme = t;
        _chevronColor = _theme.ChevronColor ?? theme.Secondary;
        _hover = _theme.HoverBackground ?? theme.SurfaceVariant;
        StyleParts();
        if (chevronMoved) ArrangeHeader();
        Corners(BodyShown);
        if (resized)
        {
            SizeBody(); // a body part-way open counts its border
            _header.MarkLayoutDirty();
            _actions.MarkLayoutDirty();
            _chevron.MarkLayoutDirty();
            _inner.MarkLayoutDirty();
            MarkLayoutDirty();
        }
        // The parts resolve after this element in the same style pass, but only if flagged;
        // their style was just written.
        _header.MarkStyleDirty();
        _titleText.MarkStyleDirty();
        _actions.MarkStyleDirty();
        _body.MarkStyleDirty();
        _inner.MarkStyleDirty();
    }

    /// <summary>Whether two themes size the parts alike: what <see cref="StyleParts"/> writes into
    /// their box and flex styles and the chevron's size.</summary>
    private static bool SameLayout(ExpanderTheme a, ExpanderTheme b) =>
        a.HeaderPadding == b.HeaderPadding && a.HeaderMinHeight == b.HeaderMinHeight
        && a.HeaderGap == b.HeaderGap && a.ChevronSize == b.ChevronSize
        && (a.Border is null) == (b.Border is null) && a.BorderWidth == b.BorderWidth
        && a.BodyPadding == b.BodyPadding;

    private void StyleParts()
    {
        var t = _theme;
        _header.Box.Padding = t.HeaderPadding;
        _header.Box.MinHeight = Dimension.Px(t.HeaderMinHeight);
        _header.Flex.Gap = t.HeaderGap;
        _header.BaseColor = t.HeaderBackground ?? Color.Transparent;
        _header.HoverColor = _hover;
        _header.PressedColor = t.PressedBackground ?? _hover;
        _actions.Flex.Gap = t.HeaderGap;

        _titleText.Inherited.FontSize = t.FontSize;
        _titleText.Inherited.FontWeight = t.FontWeight;
        _titleText.Inherited.TextColor = t.Text;

        _chevron.Size = t.ChevronSize;
        _chevron.StrokeWidth = t.ChevronStrokeWidth;
        _chevron.Color = _chevronColor;
        _chevron.Leading = t.Chevron == ExpanderChevron.Leading;
        _chevron.Turn = _progress;

        bool card = t.Border is not null;
        var border = t.Border ?? Color.Transparent;
        float w = card ? t.BorderWidth : 0f;
        _header.BorderColor = border;
        _header.Box.BorderWidth = new Thickness(w);
        _body.BorderColor = border;
        _body.Box.BorderWidth = new Thickness(0, w, w, w);
        _body.BackgroundColor = t.BodyBackground ?? Color.Transparent;

        _inner.Box.Padding = t.BodyPadding ?? (t.Chevron == ExpanderChevron.Leading
            ? new Thickness(4, t.HeaderPadding.Right, 8, t.HeaderPadding.Left + t.ChevronSize + t.HeaderGap)
            : new Thickness(4, t.HeaderPadding.Right, 8, t.HeaderPadding.Left));
        _header.RefreshLook();
    }

    /// <summary>One shape for a card: the header takes every corner when it is alone and the
    /// top ones when the body hangs under it. A bare disclosure's header keeps all four, since
    /// its hover fill has no body to join.</summary>
    private void Corners(bool open)
    {
        float r = _theme.CornerRadius;
        bool joined = open && (_theme.Border is not null || _theme.BodyBackground is not null);
        _header.CornerRadius = joined ? new CornerRadius(r, r, 0, 0) : new CornerRadius(r);
        _body.CornerRadius = new CornerRadius(0, 0, r, r);
        // Hover and press put back the look they captured when they began; this is the rest
        // look now, or the pointer leaving an opened card would give it its round corners back.
        _header.RefreshLook();
    }

    // ──── Reconciliation ────

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is not ExpanderElement s) return;

        Title = s._title;
        _onExpandedChanged = s._onExpandedChanged;

        // The header's own content, if the app gave one.
        var header = s._headerContent is null ? null : Adopt(_headerContent, s._headerContent);
        if (!ReferenceEquals(header, _headerContent))
        {
            _headerContent = header;
            _titleSlot.ReplaceChildren(header ?? _titleText);
        }

        // The actions, by position, kept where they are the same kind of element.
        var actions = new List<Element>(s._actionItems.Count);
        for (int i = 0; i < s._actionItems.Count; i++)
        {
            var incoming = s._actionItems[i];
            var live = i < _actionItems.Count ? _actionItems[i] : null;
            actions.Add(live is not null && live.Key == incoming.Key ? Adopt(live, incoming) : incoming);
        }
        bool actionsChanged = actions.Count != _actionItems.Count;
        for (int i = 0; !actionsChanged && i < actions.Count; i++)
            actionsChanged = !ReferenceEquals(actions[i], _actionItems[i]);
        if (actionsChanged)
        {
            _actionItems = actions;
            _actions.ReplaceChildren([.. actions]);
            ArrangeHeader();
        }
        NameHeader();

        // The content: a function is asked again only while the body is shown, an element is
        // patched whether or not it is.
        if (s._factory is not null)
        {
            _factory = s._factory;
            // A twin built open has already asked the function once; use what it got.
            if (BodyShown) PatchContent(s._content ?? _factory());
            else if (_content is not null) { _content = null; _inner.ReplaceChildren(); }
        }
        else
        {
            _factory = null;
            if (s._content is null) { if (_content is not null) { _content = null; _inner.ReplaceChildren(); } }
            else PatchContent(s._content);
        }

        if (s._controlled)
        {
            _controlled = true;
            SetExpanded(s._expanded, notify: false);
        }
    }

    private void PatchContent(Element incoming)
    {
        var next = Adopt(_content, incoming);
        if (ReferenceEquals(next, _content)) return;
        _content = next;
        _inner.ReplaceChildren(next);
    }

    private static Element Adopt(Element? live, Element incoming) =>
        live is not null && TreeDiffer.TryPatch(live, incoming) ? live : incoming;

    protected override void OnMounted()
    {
        base.OnMounted();
        // Set by the builder chain after construction, so read here.
        NameHeader();
    }

    protected override void OnUnmounted()
    {
        base.OnUnmounted();
        if (!_tween.IsComplete)
        {
            _tween.Cancel();
            SetProgress(_expanded ? 1f : 0f);
            Settle();
        }
    }

    // ──── Parts ────

    /// <summary>The toggle: a focusable button-like row that lights under the pointer.</summary>
    private sealed class HeaderRowElement : BoxElement
    {
        private const KeyModifiers Chord = KeyModifiers.Shift | KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Super;
        private readonly ExpanderElement _owner;
        public Color BaseColor, HoverColor, PressedColor;

        public HeaderRowElement(ExpanderElement owner)
        {
            _owner = owner;
            IsFocusable = true;
            ShowsFocusVisual = true;
            Role = AccessibilityRole.Button;
            AccessibleExpanded = false;
            Flex.Direction = FlexDirection.Row;
            Flex.AlignItems = AlignItems.Center;
            Flex.FlexShrink = 0;
            Inherited.Cursor = CursorType.Pointer;

            // Static and reading the element handed in, like a button's: the interaction
            // config is the element's own, never patched, but the habit costs nothing.
            AddHoverStyle(static e => ((HeaderRowElement)e).Lit(pressed: false));
            AddPressedStyle(static e => ((HeaderRowElement)e).Lit(pressed: true));

            OnOwn<ClickEvent>(evt =>
            {
                // An action's click passes through here on its way up; it is not a toggle.
                if (InActions(evt.Target)) return;
                _owner.Toggle();
            });
            OnOwn<KeyDownEvent>(evt =>
            {
                if (!ReferenceEquals(evt.Target, this)) return;
                // Lock keys are not modifiers here: NumLock on must not turn Enter into a chord.
                if (evt.Key is KeyCode.Enter or KeyCode.Space && (evt.Modifiers & Chord) == 0)
                {
                    _owner.Toggle();
                    evt.StopPropagation = true;
                }
            });
        }

        private bool InActions(Element target)
        {
            for (Element? e = target; e is not null && !ReferenceEquals(e, this); e = e.Parent)
                if (ReferenceEquals(e, _owner._actions)) return true;
            return false;
        }

        private void Lit(bool pressed) => BackgroundColor = pressed ? PressedColor : HoverColor;

        /// <summary>Writes the rest fill and re-applies whatever state the pointer is in, so a
        /// theme change mid-hover keeps the header lit in the new colours.</summary>
        public void RefreshLook()
        {
            BackgroundColor = BaseColor;
            RefreshInteractionAfterPatch();
        }
    }

    /// <summary>
    /// Two strokes meeting at a point, turned by how open the expander is: a quarter turn from
    /// right to down at the leading end, a half turn from down to up at the trailing end.
    /// </summary>
    internal sealed class ChevronElement : Element
    {
        public float Size = 9f;
        public float StrokeWidth = 1.5f;
        public Color Color;
        public bool Leading = true;
        /// <summary>0 closed, 1 open.</summary>
        public float Turn;

        public ChevronElement() => Flex.FlexShrink = 0;

        /// <summary>The direction it points in degrees, 0 being down.</summary>
        public float Angle => Leading ? -90f + 90f * Turn : 180f * Turn;

        internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx) =>
            new(Size, Size);

        /// <summary>Unbounded: turning about its centre, the chevron's ends swing past its box.</summary>
        public override Rect? PaintBounds(IRenderContext ctx) => null;

        public override void Render(IRenderContext ctx)
        {
            var box = Layout.AbsoluteContentBox;
            float cx = box.X + box.Width / 2f;
            float cy = box.Y + box.Height / 2f;
            float w = Size, h = Size / 2f;
            float rad = Angle * MathF.PI / 180f;
            float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
            Point P(float x, float y) => new(cx + x * cos - y * sin, cy + x * sin + y * cos);
            // A down chevron centred on its box; turned about that centre.
            var left = P(-w / 2f, -h / 2f);
            var tip = P(0f, h / 2f);
            var right = P(w / 2f, -h / 2f);
            ctx.DrawLine(left, tip, Color, StrokeWidth);
            ctx.DrawLine(tip, right, Color, StrokeWidth);
        }
    }
}
