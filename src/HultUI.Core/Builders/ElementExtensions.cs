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
    public static T AlignItems<T>(this T el, AlignItems ai) where T : Element { el.Flex.AlignItems = ai; return el; }
    public static T AlignSelf<T>(this T el, AlignSelf a) where T : Element { el.Flex.AlignSelf = a; return el; }
    public static T AlignContent<T>(this T el, AlignContent ac) where T : Element { el.Flex.AlignContent = ac; return el; }

    // ──── Box visual properties ────
    public static T Background<T>(this T el, Color c) where T : BoxElement { el.BackgroundColor = c; return el; }
    public static T BorderColor<T>(this T el, Color c) where T : BoxElement { el.BorderColor = c; return el; }
    public static T CornerRadius<T>(this T el, CornerRadius r) where T : BoxElement { el.CornerRadius = r; return el; }
    public static T CornerRadius<T>(this T el, float uniform) where T : BoxElement { el.CornerRadius = new CornerRadius(uniform); return el; }

    // ──── Inherited style ────
    public static T FontFamily<T>(this T el, string family) where T : Element { el.Inherited.FontFamily = family; return el; }
    public static T FontSize<T>(this T el, float size) where T : Element { el.Inherited.FontSize = size; return el; }
    public static T FontWeight<T>(this T el, FontWeight weight) where T : Element { el.Inherited.FontWeight = weight; return el; }
    public static T TextColor<T>(this T el, Color color) where T : Element { el.Inherited.TextColor = color; return el; }
    public static T Cursor<T>(this T el, CursorType cursor) where T : Element { el.Inherited.Cursor = cursor; return el; }

    // ──── Image properties ────
    public static T ImageFit<T>(this T el, ImageFit fit) where T : ImageElement { el.Fit = fit; return el; }
    public static T ImageSource<T>(this T el, IImageSource source) where T : ImageElement { el.Source = source; return el; }

    // ──── ScrollView properties ────
    public static T ScrollSpeed<T>(this T el, float speed) where T : ScrollViewElement { el.ScrollSpeed = speed; return el; }

    // ──── TextInput properties ────
    public static T PlaceholderText<T>(this T el, string text) where T : TextInputElement { el.Placeholder = text; return el; }
    public static T OnTextChanged<T>(this T el, Action<string> handler) where T : TextInputElement { el.TextChanged = handler; return el; }

    // ──── Checkbox properties ────
    public static T OnCheckedChanged<T>(this T el, Action<bool> handler) where T : CheckboxElement { el.OnChanged = handler; return el; }

    // ──── RadioButton properties ────
    public static T OnRadioChanged<T>(this T el, Action<bool> handler) where T : RadioButtonElement { el.OnChanged = handler; return el; }

    // ──── Accessibility ────
    public static T AccessibleRole<T>(this T el, AccessibilityRole role) where T : Element { el.Role = role; return el; }
    public static T AccessibleName<T>(this T el, string name) where T : Element { el.AccessibleName = name; return el; }
    public static T AccessibleDescription<T>(this T el, string desc) where T : Element { el.AccessibleDescription = desc; return el; }

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

    // ──── Children ────
    public static T Children<T>(this T el, params Element[] children) where T : Element
    {
        el.ReplaceChildren(children);
        return el;
    }
}
