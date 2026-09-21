using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Builders;

public static class ElementExtensions
{
    // ──── Size ────
    public static T Width<T>(this T el, Dimension w) where T : Element { el.Box.Width = w; return el; }
    public static T Height<T>(this T el, Dimension h) where T : Element { el.Box.Height = h; return el; }
    public static T MinWidth<T>(this T el, Dimension w) where T : Element { el.Box.MinWidth = w; return el; }
    public static T MinHeight<T>(this T el, Dimension h) where T : Element { el.Box.MinHeight = h; return el; }
    public static T MaxWidth<T>(this T el, Dimension w) where T : Element { el.Box.MaxWidth = w; return el; }
    public static T MaxHeight<T>(this T el, Dimension h) where T : Element { el.Box.MaxHeight = h; return el; }
    public static T Size<T>(this T el, Dimension w, Dimension h) where T : Element { el.Box.Width = w; el.Box.Height = h; return el; }

    // ──── Spacing ────
    public static T Padding<T>(this T el, Thickness p) where T : Element { el.Box.Padding = p; return el; }
    public static T Padding<T>(this T el, float uniform) where T : Element { el.Box.Padding = new Thickness(uniform); return el; }
    public static T Margin<T>(this T el, Thickness m) where T : Element { el.Box.Margin = m; return el; }
    public static T Margin<T>(this T el, float uniform) where T : Element { el.Box.Margin = new Thickness(uniform); return el; }

    // ──── Flex properties ────
    public static T FlexDirection<T>(this T el, FlexDirection dir) where T : Element { el.Flex.Direction = dir; return el; }
    public static T FlexWrap<T>(this T el, FlexWrap wrap) where T : Element { el.Flex.Wrap = wrap; return el; }
    public static T FlexGrow<T>(this T el, float grow) where T : Element { el.Flex.FlexGrow = grow; return el; }
    public static T FlexShrink<T>(this T el, float shrink) where T : Element { el.Flex.FlexShrink = shrink; return el; }
    public static T FlexBasis<T>(this T el, Dimension basis) where T : Element { el.Flex.FlexBasis = basis; return el; }
    public static T Gap<T>(this T el, float gap) where T : Element { el.Flex.Gap = gap; return el; }
    public static T JustifyContent<T>(this T el, JustifyContent jc) where T : Element { el.Flex.JustifyContent = jc; return el; }
    public static T AlignItems<T>(this T el, AlignItems ai) where T : Element { el.Flex.AlignItems = ai; el.Grid.AlignItems = ai; return el; }
    public static T AlignSelf<T>(this T el, AlignSelf a) where T : Element { el.Flex.AlignSelf = a; el.GridItem.AlignSelf = a; return el; }
    public static T AlignContent<T>(this T el, AlignContent ac) where T : Element { el.Flex.AlignContent = ac; return el; }

    // ──── Box visual properties ────
    public static T Background<T>(this T el, Color c) where T : BoxElement { el.BackgroundColor = c; return el; }
    public static T Background<T>(this T el, Gradient gradient) where T : BoxElement { el.BackgroundGradient = gradient; return el; }
    public static T BorderColor<T>(this T el, Color c) where T : BoxElement { el.BorderColor = c; return el; }
    public static T BorderWidth<T>(this T el, Thickness w) where T : BoxElement { el.Box.BorderWidth = w; return el; }
    public static T BorderWidth<T>(this T el, float uniform) where T : BoxElement { el.Box.BorderWidth = new Thickness(uniform); return el; }
    public static T BorderStyle<T>(this T el, BorderStyle style) where T : BoxElement { el.BorderStyle = style; return el; }
    /// <summary>Sets a single-sided border via its edge widths, e.g. a bottom rule or a left accent.</summary>
    public static T Border<T>(this T el, Thickness widths, Color color, BorderStyle style = Layout.BorderStyle.Solid) where T : BoxElement
    {
        el.Box.BorderWidth = widths;
        el.BorderColor = color;
        el.BorderStyle = style;
        return el;
    }
    /// <summary>
    /// A ring outside the border box that takes no space — for a selected or active state on
    /// an element that sizes to its content. A border there would grow the element by twice
    /// its width and shove every sibling along; this cannot, because layout never reads it.
    /// </summary>
    /// <param name="el">The box to ring.</param>
    /// <param name="width">Ring thickness. Zero draws nothing, so a caller can switch it off
    /// without branching.</param>
    /// <param name="color">Ring colour.</param>
    /// <param name="offset">Distance outward from the border edge. Negative pulls the ring
    /// inward, so <c>-width</c> puts it exactly where a border would have been.</param>
    public static T Outline<T>(this T el, float width, Color color, float offset = 0f) where T : BoxElement
    { el.Outline = new Outline(width, color, offset); return el; }

    public static T CornerRadius<T>(this T el, CornerRadius r) where T : BoxElement { el.CornerRadius = r; return el; }
    public static T CornerRadius<T>(this T el, float uniform) where T : BoxElement { el.CornerRadius = new CornerRadius(uniform); return el; }
    // Re-read el.BoxShadows after the null-init so Add goes through the
    // DirtyList the getter exposes (a compound `??=` would hand back the raw
    // right-hand list instead).
    public static T BoxShadow<T>(this T el, BoxShadow shadow) where T : BoxElement { if (el.BoxShadows is null) el.BoxShadows = new List<BoxShadow>(); el.BoxShadows!.Add(shadow); return el; }
    public static T BoxShadow<T>(this T el, float offsetX, float offsetY, float blur, Color color) where T : BoxElement => el.BoxShadow(new BoxShadow(offsetX, offsetY, blur, color));
    public static T BoxShadow<T>(this T el, float offsetX, float offsetY, float blur, float spread, Color color) where T : BoxElement => el.BoxShadow(new BoxShadow(offsetX, offsetY, blur, spread, color));

    // ──── Overflow ────
    public static T Overflow<T>(this T el, Overflow overflow) where T : Element { el.Box.OverflowX = overflow; el.Box.OverflowY = overflow; return el; }
    public static T OverflowX<T>(this T el, Overflow overflow) where T : Element { el.Box.OverflowX = overflow; return el; }
    public static T OverflowY<T>(this T el, Overflow overflow) where T : Element { el.Box.OverflowY = overflow; return el; }

    // ──── Inherited style ────
    public static T FontFamily<T>(this T el, string family) where T : Element { el.Inherited.FontFamily = family; return el; }
    public static T FontSize<T>(this T el, float size) where T : Element { el.Inherited.FontSize = size; return el; }
    public static T FontWeight<T>(this T el, FontWeight weight) where T : Element { el.Inherited.FontWeight = weight; return el; }
    public static T FontStyle<T>(this T el, FontSlant slant) where T : Element { el.Inherited.FontSlant = slant; return el; }
    public static T Italic<T>(this T el, bool italic = true) where T : Element { el.Inherited.FontSlant = italic ? FontSlant.Italic : FontSlant.Normal; return el; }
    public static T TextColor<T>(this T el, Color color) where T : Element { el.Inherited.TextColor = color; return el; }
    public static T TextAlign<T>(this T el, TextAlign align) where T : Element { el.Inherited.TextAlign = align; return el; }
    /// <summary>Absolute line-box height in pixels (0 = derive from font metrics).</summary>
    public static T LineHeight<T>(this T el, float px) where T : Element { el.Inherited.LineHeight = px; return el; }
    /// <summary>Extra spacing in pixels inserted after each glyph.</summary>
    public static T LetterSpacing<T>(this T el, float px) where T : Element { el.Inherited.LetterSpacing = px; return el; }
    public static T TextDecoration<T>(this T el, TextDecoration decoration) where T : Element { el.Inherited.Decoration = decoration; return el; }
    public static T Underline<T>(this T el, bool on = true) where T : Element
    {
        var current = el.Inherited.Decoration ?? Styling.TextDecoration.None;
        el.Inherited.Decoration = on ? current | Styling.TextDecoration.Underline : current & ~Styling.TextDecoration.Underline;
        return el;
    }
    public static T Strikethrough<T>(this T el, bool on = true) where T : Element
    {
        var current = el.Inherited.Decoration ?? Styling.TextDecoration.None;
        el.Inherited.Decoration = on ? current | Styling.TextDecoration.LineThrough : current & ~Styling.TextDecoration.LineThrough;
        return el;
    }
    public static T Cursor<T>(this T el, CursorType cursor) where T : Element { el.Inherited.Cursor = cursor; return el; }

    // ──── Text properties ────
    public static T TextOverflow<T>(this T el, TextOverflow overflow) where T : TextElement { el.Overflow = overflow; return el; }
    public static T MaxLines<T>(this T el, int lines) where T : TextElement { el.MaxLines = lines; return el; }

    /// <summary>Lets the user select and copy this label's text with the mouse
    /// (drag to select, double/triple-click, Ctrl+C, right-click Copy).</summary>
    public static T Selectable<T>(this T el, bool value = true) where T : TextElement { el.Selectable = value; return el; }

    /// <summary>Lets a mouse drag select across every label sharing the same <paramref name="group"/>
    /// (e.g. a diff's code lines); Ctrl+C copies the whole span. <paramref name="index"/> is the
    /// label's line number in the group's model, which is what the selection is anchored to — so it
    /// survives the label being rebuilt or recycled. See <see cref="TextSelectionGroup"/>.</summary>
    public static T SelectionGroup<T>(this T el, TextSelectionGroup? group, int index) where T : TextElement
    { el.SelectionGroup = group; el.SelectionIndex = index; return el; }

    /// <summary>Per-range text colors (syntax highlighting); see <see cref="TextElement.Spans"/>.</summary>
    public static T Spans<T>(this T el, IReadOnlyList<Rendering.TextSpan>? spans) where T : TextElement { el.Spans = spans; return el; }

    /// <summary>Clickable ranges within the text, and what to do when one is clicked.
    /// Style them separately with <see cref="TextElement.Spans"/>; see <see cref="TextElement.Links"/>.</summary>
    public static T Links<T>(this T el, IReadOnlyList<TextLink>? links, Action<string> onActivate) where T : TextElement
    { el.Links = links; el.LinkActivated = onActivate; return el; }

    // ──── Image properties ────
    public static T ImageFit<T>(this T el, ImageFit fit) where T : ImageElement { el.Fit = fit; return el; }
    public static T ImageSource<T>(this T el, IImageSource source) where T : ImageElement { el.Source = source; return el; }

    // ──── ScrollView properties ────
    public static T ScrollSpeed<T>(this T el, float speed) where T : ScrollViewElement { el.ScrollSpeed = speed; return el; }
    /// <summary>Names who scrolls the view's content sideways when the view cannot itself — a
    /// <see cref="HorizontalPan"/> under a virtualized list — so the middle-click autoscroll's
    /// horizontal delta reaches it. See <see cref="ScrollViewElement.SidewaysScroller"/>.</summary>
    public static T ScrollSidewaysWith<T>(this T el, ISidewaysScroller? scroller) where T : ScrollViewElement { el.SidewaysScroller = scroller; return el; }

    // ──── TextInput properties ────
    public static T PlaceholderText<T>(this T el, string text) where T : TextInputElement { el.Placeholder = text; return el; }

    /// <summary>Hands the box to the caller: no background, border, corner radius or themed
    /// padding of its own, for an input composed into a control that draws its own — a search
    /// box with a magnifier beside the text, a cell editor. Named unlike the property it sets
    /// (<see cref="TextInputElement.DrawsChrome"/>): a same-named extension would be
    /// unreachable, because <c>input.Chromeless()</c> would bind to invoking the bool.</summary>
    public static T Chromeless<T>(this T el, bool value = true) where T : TextInputElement { el.DrawsChrome = !value; return el; }
    public static T OnTextChanged<T>(this T el, Action<string> handler) where T : TextInputElement { el.TextChanged = handler; return el; }
    /// <summary>A reusable look for this field, layered over the theme's; see <see cref="TextInputStyle"/>.</summary>
    public static T Style<T>(this T el, TextInputStyle? style) where T : TextInputElement { el.Style = style; return el; }

    // ──── TextArea properties ────
    public static TextAreaElement PlaceholderText(this TextAreaElement el, string text) { el.Placeholder = text; return el; }
    /// <summary>A reusable look for this area, layered over the theme's; see <see cref="TextInputStyle"/>.</summary>
    public static TextAreaElement Style(this TextAreaElement el, TextInputStyle? style) { el.Style = style; return el; }
    public static TextAreaElement OnTextChanged(this TextAreaElement el, Action<string> handler) { el.TextChanged = handler; return el; }
    public static TextAreaElement ReadOnly(this TextAreaElement el, bool value = true) { el.ReadOnly = value; return el; }
    public static TextAreaElement Spans(this TextAreaElement el, IReadOnlyList<TextSpan>? spans) { el.Spans = spans; return el; }
    public static TextInputElement Spans(this TextInputElement el, IReadOnlyList<TextSpan>? spans) { el.Spans = spans; return el; }

    // ──── Checkbox properties ────
    public static T OnCheckedChanged<T>(this T el, Action<bool> handler) where T : CheckboxElement { el.OnChanged = handler; return el; }

    // ──── ToggleSwitch properties ────
    public static T OnToggled<T>(this T el, Action<bool> handler) where T : ToggleSwitchElement { el.OnChanged = handler; return el; }

    // ──── ProgressBar properties ────
    public static ProgressBarElement Value(this ProgressBarElement el, double value) { el.Value = value; return el; }
    public static ProgressBarElement Range(this ProgressBarElement el, double min, double max) { el.Minimum = min; el.Maximum = max; return el; }
    /// <summary>Show activity without a known fraction: a segment sweeps the track until
    /// <see cref="ProgressBarElement.IsIndeterminate"/> is cleared.</summary>
    public static ProgressBarElement Indeterminate(this ProgressBarElement el, bool indeterminate = true) { el.IsIndeterminate = indeterminate; return el; }

    // ──── NumericUpDown properties ────
    public static NumericUpDownElement Value(this NumericUpDownElement el, double value) { el.Value = value; return el; }
    public static NumericUpDownElement Range(this NumericUpDownElement el, double min, double max) { el.Minimum = min; el.Maximum = max; return el; }
    public static NumericUpDownElement Step(this NumericUpDownElement el, double step) { el.Step = step; return el; }
    public static NumericUpDownElement LargeStep(this NumericUpDownElement el, double step) { el.LargeStep = step; return el; }
    /// <summary>How the value is written: <c>.Format(v => $"{v:0.#} pt")</c>. Typing still
    /// parses a plain number.</summary>
    public static NumericUpDownElement Format(this NumericUpDownElement el, Func<double, string> format) { el.Formatter = format; return el; }
    public static NumericUpDownElement OnValueChanged(this NumericUpDownElement el, Action<double> handler) { el.OnChanged = handler; return el; }

    // ──── The dropdown-shaped fields' style ────
    /// <summary>A reusable look for this field, laid over the theme's; see <see cref="DropdownStyle"/>.</summary>
    public static DropdownElement Style(this DropdownElement el, DropdownStyle? style) { el.Style = style; return el; }
    /// <inheritdoc cref="Style(DropdownElement, DropdownStyle?)"/>
    public static NumericUpDownElement Style(this NumericUpDownElement el, DropdownStyle? style) { el.Style = style; return el; }
    /// <inheritdoc cref="Style(DropdownElement, DropdownStyle?)"/>
    public static DatePickerElement Style(this DatePickerElement el, DropdownStyle? style) { el.Style = style; return el; }
    /// <inheritdoc cref="Style(DropdownElement, DropdownStyle?)"/>
    public static TimePickerElement Style(this TimePickerElement el, DropdownStyle? style) { el.Style = style; return el; }

    // ──── Slider properties ────
    public static SliderElement Value(this SliderElement el, double value) { el.Value = value; return el; }
    public static SliderElement Range(this SliderElement el, double min, double max) { el.Minimum = min; el.Maximum = max; return el; }
    public static SliderElement Step(this SliderElement el, double step) { el.Step = step; return el; }
    public static SliderElement OnValueChanged(this SliderElement el, Action<double> handler) { el.OnChanged = handler; return el; }

    // ──── RadioButton properties ────
    public static T OnRadioChanged<T>(this T el, Action<bool> handler) where T : RadioButtonElement { el.OnChanged = handler; return el; }

    // ButtonElement
    public static T Variant<T>(this T el, ButtonVariant variant) where T : ButtonElement { el.Variant = variant; return el; }
    /// <summary>A reusable look for this button, layered over its variant's; see <see cref="ButtonStyle"/>.</summary>
    public static T Style<T>(this T el, ButtonStyle? style) where T : ButtonElement { el.Style = style; return el; }
    /// <summary>Off: dimmed, unfocusable, and its click goes nowhere — but its tooltip still shows.</summary>
    public static T Enabled<T>(this T el, bool enabled = true) where T : ButtonElement { el.IsEnabled = enabled; return el; }

    // ──── Identity ────
    /// <summary>
    /// A developer-facing label for this element. It shows in the inspector as
    /// <c>#name</c>, in frame traces, and — only when nothing better exists — as the
    /// element's accessible name. Nothing looks elements up by it, so duplicates are
    /// fine; a repeated construct naming every instance the same reads correctly.
    /// Use <see cref="AutomationId"/> instead for something selectors resolve.
    /// </summary>
    public static T Name<T>(this T el, string name) where T : Element { el.Name = name; return el; }

    // ──── Accessibility ────
    public static T AccessibleRole<T>(this T el, AccessibilityRole role) where T : Element { el.Role = role; return el; }
    public static T AccessibleName<T>(this T el, string name) where T : Element { el.AccessibleName = name; return el; }
    public static T AccessibleDescription<T>(this T el, string desc) where T : Element { el.AccessibleDescription = desc; return el; }
    public static T AutomationId<T>(this T el, string id) where T : Element { el.AutomationId = id; return el; }
    public static T AccessibleExpanded<T>(this T el, bool expanded) where T : Element { el.AccessibleExpanded = expanded; return el; }

    // ──── Events ────
    public static T OnClick<T>(this T el, Action<ClickEvent> handler) where T : Element { el.On(handler); return el; }
    public static T OnPointerDown<T>(this T el, Action<PointerDownEvent> handler) where T : Element { el.On(handler); return el; }
    public static T OnPointerUp<T>(this T el, Action<PointerUpEvent> handler) where T : Element { el.On(handler); return el; }
    public static T OnPointerEnter<T>(this T el, Action<PointerEnterEvent> handler) where T : Element { el.On(handler); return el; }
    public static T OnPointerLeave<T>(this T el, Action<PointerLeaveEvent> handler) where T : Element { el.On(handler); return el; }
    public static T OnScroll<T>(this T el, Action<ScrollEvent> handler) where T : Element { el.On(handler); return el; }
    public static T OnKeyDown<T>(this T el, Action<KeyDownEvent> handler) where T : Element { el.On(handler); return el; }

    // ──── Visual ────
    public static T Opacity<T>(this T el, float opacity) where T : Element { el.Opacity = opacity; return el; }
    public static T ZIndex<T>(this T el, int zIndex) where T : Element { el.Position.ZIndex = zIndex; return el; }

    // ──── Interaction states ──── (declarative overrides re-applied on hover/press/focus)
    public static T OnHover<T>(this T el, Action<T> style) where T : Element { el.AddHoverStyle(e => style((T)e)); return el; }
    public static T OnPressed<T>(this T el, Action<T> style) where T : Element { el.AddPressedStyle(e => style((T)e)); return el; }
    public static T OnFocus<T>(this T el, Action<T> style) where T : Element { el.AddFocusStyle(e => style((T)e)); return el; }
    /// <summary>Animates changes to <paramref name="property"/> (e.g. from a hover override) over <paramref name="ms"/> milliseconds.</summary>
    public static T Transition<T>(this T el, Animation.TransitionProperty property, float ms = 150f, Func<float, float>? easing = null) where T : Element
    {
        el.AddTransition(property, ms / 1000f, easing ?? Animation.Easing.EaseOutCubic);
        return el;
    }

    // ──── Transform ──── (rotate/scale/translate pivot on the element's center; helpers merge)
    public static T Transform<T>(this T el, Transform2D transform) where T : Element { el.Transform = transform; return el; }
    public static T Rotate<T>(this T el, float degrees) where T : Element { el.Transform = (el.Transform ?? Transform2D.Identity) with { RotationDegrees = degrees }; return el; }
    public static T Scale<T>(this T el, float scale) where T : Element { el.Transform = (el.Transform ?? Transform2D.Identity) with { ScaleX = scale, ScaleY = scale }; return el; }
    public static T Scale<T>(this T el, float scaleX, float scaleY) where T : Element { el.Transform = (el.Transform ?? Transform2D.Identity) with { ScaleX = scaleX, ScaleY = scaleY }; return el; }
    public static T TranslateBy<T>(this T el, float dx, float dy) where T : Element { el.Transform = (el.Transform ?? Transform2D.Identity) with { TranslateX = dx, TranslateY = dy }; return el; }

    // ──── Drag & Drop ────
    public static T Draggable<T>(this T el, bool draggable = true) where T : Element { el.Draggable = draggable; return el; }
    public static T WindowDrag<T>(this T el, bool isDragRegion = true) where T : Element { el.IsWindowDragRegion = isDragRegion; return el; }

    /// <summary>Marks this element as the window's maximize/restore button so the
    /// Windows custom frame reports HTMAXBUTTON there — enabling the Windows 11
    /// Snap Layouts flyout on hover. No effect off Windows.</summary>
    public static T WindowMaximizeButton<T>(this T el, bool isMaximizeButton = true) where T : Element { el.IsWindowMaximizeButton = isMaximizeButton; return el; }
    public static T OnDragStart<T>(this T el, Action<DragStartEvent> handler) where T : Element { el.On(handler); return el; }
    public static T OnDragEnter<T>(this T el, Action<DragEnterEvent> handler) where T : Element { el.On(handler); return el; }
    public static T OnDragLeave<T>(this T el, Action<DragLeaveEvent> handler) where T : Element { el.On(handler); return el; }
    public static T OnDragOver<T>(this T el, Action<DragOverEvent> handler) where T : Element { el.On(handler); return el; }
    public static T OnDrop<T>(this T el, Action<DropEvent> handler) where T : Element { el.On(handler); return el; }
    public static T OnDragEnd<T>(this T el, Action<DragEndEvent> handler) where T : Element { el.On(handler); return el; }

    // ──── Grid container ────
    public static T RowGap<T>(this T el, float gap) where T : Element { el.Grid.RowGap = gap; el.Flex.RowGap = gap; return el; }
    public static T ColumnGap<T>(this T el, float gap) where T : Element { el.Grid.ColumnGap = gap; el.Flex.ColumnGap = gap; return el; }
    public static T JustifyItems<T>(this T el, JustifyItems items) where T : Element { el.Grid.JustifyItems = items; return el; }

    // ──── Grid item placement ────
    public static T GridRow<T>(this T el, int row) where T : Element { el.GridItem.Row = row; return el; }
    public static T GridColumn<T>(this T el, int col) where T : Element { el.GridItem.Column = col; return el; }
    public static T GridCell<T>(this T el, int row, int col) where T : Element { el.GridItem.Row = row; el.GridItem.Column = col; return el; }
    public static T JustifySelf<T>(this T el, JustifySelf self) where T : Element { el.GridItem.JustifySelf = self; return el; }

    // ──── VirtualList properties ────
    public static VirtualListElement<T> ItemHeight<T>(this VirtualListElement<T> el, float height) { el.ItemHeight = height; return el; }

    /// <summary>Per-row height for a list whose rows differ in size; see <see cref="VirtualListElement{T}.ItemHeightFor"/>.
    /// Named unlike the property it sets: a same-named extension would be unreachable, because
    /// <c>list.ItemHeightFor(f)</c> binds to invoking the delegate property instead.</summary>
    public static VirtualListElement<T> RowHeights<T>(this VirtualListElement<T> el, Func<T, int, float>? height) { el.ItemHeightFor = height; return el; }
    public static VirtualListElement<T> Items<T>(this VirtualListElement<T> el, IReadOnlyList<T> items) { el.Items = items; return el; }
    public static VirtualListElement<T> RenderWith<T>(this VirtualListElement<T> el, Func<T, Element> template) { el.ItemTemplate = template; return el; }
    public static VirtualListElement<T> AccessibleItemLabel<T>(this VirtualListElement<T> el, Func<T, string?> label) { el.AccessibleLabel = label; return el; }
    public static VirtualListElement<T> OnActivateItem<T>(this VirtualListElement<T> el, Action<T> handler) { el.OnItemActivate = handler; return el; }
    public static VirtualListElement<T> SelectedIndex<T>(this VirtualListElement<T> el, int index) { el.SelectedIndex = index; return el; }
    public static VirtualListElement<T> CanSelectMultiple<T>(this VirtualListElement<T> el, bool value = true) { el.CanSelectMultiple = value; return el; }
    public static VirtualListElement<T> OnSelectionChanged<T>(this VirtualListElement<T> el, Action<IReadOnlyList<int>> handler) { el.OnSelectionChanged = handler; return el; }
    /// <summary>Let the focused list scroll itself with Up/Down/Page/Home/End.
    /// Chorded keys (Shift/Ctrl/Alt/Super) still bubble to the app.</summary>
    public static VirtualListElement<T> KeyboardScroll<T>(this VirtualListElement<T> el, bool enabled = true) { el.KeyboardScrollEnabled = enabled; return el; }

    // ──── Tooltip ────
    public static T Tooltip<T>(this T el, string text) where T : Element { el.TooltipText = text; return el; }

    // ──── Context Menu ────
    public static T ContextMenu<T>(this T el, Func<IReadOnlyList<ContextMenuItem>> itemsProvider) where T : Element { el.ContextMenuItems = itemsProvider; return el; }
    public static T ContextMenu<T>(this T el, IReadOnlyList<ContextMenuItem> items) where T : Element { el.ContextMenuItems = () => items; return el; }

    // ──── Children ────
    public static T Children<T>(this T el, params Element[] children) where T : Element
    {
        el.ReplaceChildren(children);
        return el;
    }
}
