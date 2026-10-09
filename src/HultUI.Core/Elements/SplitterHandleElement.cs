using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>Which way a splitter's panes sit: side by side, or one above the other.</summary>
public enum SplitterAxis : byte
{
    /// <summary>Panes side by side; the handle is a vertical strip dragged left and right.</summary>
    Horizontal,
    /// <summary>Panes stacked; the handle is a horizontal strip dragged up and down.</summary>
    Vertical,
}

/// <summary>
/// The grip between two panes. It holds no size of its own — that is the
/// <see cref="Builders.PaneSplitter"/>'s, which builds one of these each time the owner
/// rebuilds — but it is the thing on screen: the resize cursor, the tint under the
/// pointer and while grabbed, a focus ring, and the arrow keys, which move the split
/// without a mouse. Reads as a separator.
/// </summary>
public class SplitterHandleElement : Element
{
    private bool _hover;
    private bool _focused;
    private float _thickness = 6f;
    private Color _hoverColor = new(79, 70, 229, 46);
    private Color _grabbedColor = new(79, 70, 229, 102);
    private Color _focusRingColor = new(59, 130, 246, 180);
    private float _keyStep = 10f;

    public SplitterAxis Axis { get; }

    /// <summary>Set by the splitter while a drag is in progress.</summary>
    public bool IsGrabbed { get; set; }

    /// <summary>Explicit colours, which win over the theme's. For an app whose palette the
    /// theme does not describe.</summary>
    public Color? HoverColor { get; set; }
    public Color? GrabbedColor { get; set; }

    /// <summary>Runs on an arrow key along the axis, with the signed distance to move.</summary>
    public Action<float>? OnKeyStep { get; set; }
    /// <summary>Runs on Home (false) and End (true).</summary>
    public Action<bool>? OnKeyEnd { get; set; }

    public SplitterHandleElement(SplitterAxis axis = SplitterAxis.Horizontal)
    {
        Axis = axis;
        IsFocusable = true;
        Role = AccessibilityRole.Separator;
        AccessibleName = "Resize";
        Inherited.Cursor = axis == SplitterAxis.Horizontal ? CursorType.ResizeEW : CursorType.ResizeNS;
        OnOwn<PointerEnterEvent>(_ => { _hover = true; MarkPaintDirty(); });
        OnOwn<PointerLeaveEvent>(_ => { _hover = false; MarkPaintDirty(); });
        OnOwn<FocusEvent>(_ => { _focused = true; MarkPaintDirty(); });
        OnOwn<BlurEvent>(_ => { _focused = false; MarkPaintDirty(); });
        OnOwn<KeyDownEvent>(evt =>
        {
            if (!_focused) return;
            bool horizontal = Axis == SplitterAxis.Horizontal;
            float step = _keyStep * (evt.Modifiers.HasFlag(KeyModifiers.Shift) ? 5f : 1f);
            switch (evt.Key)
            {
                case KeyCode.Left when horizontal: OnKeyStep?.Invoke(-step); break;
                case KeyCode.Right when horizontal: OnKeyStep?.Invoke(step); break;
                case KeyCode.Up when !horizontal: OnKeyStep?.Invoke(-step); break;
                case KeyCode.Down when !horizontal: OnKeyStep?.Invoke(step); break;
                case KeyCode.Home: OnKeyEnd?.Invoke(false); break;
                case KeyCode.End: OnKeyEnd?.Invoke(true); break;
                default: return;
            }
            evt.StopPropagation = true;
        });
    }

    protected internal override void ApplyTheme(Theme theme)
    {
        var t = theme.Splitter;
        _thickness = t.Thickness;
        _keyStep = t.KeyStep;
        _hoverColor = t.Hover ?? WithAlpha(theme.Primary, 46);
        _grabbedColor = t.Grabbed ?? WithAlpha(theme.Primary, 102);
        _focusRingColor = theme.FocusRing;
        MarkLayoutDirty();
    }

    private static Color WithAlpha(Color c, byte a) => new(c.R, c.G, c.B, a);

    // Thick along the axis it moves on; the other way it is whatever the row or column
    // stretches it to, like any leaf.
    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx) =>
        Axis == SplitterAxis.Horizontal ? new Size(_thickness, 0f) : new Size(0f, _thickness);

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteBorderBox;
        if (IsGrabbed)
            ctx.DrawRect(box, GrabbedColor ?? _grabbedColor);
        else if (_hover)
            ctx.DrawRect(box, HoverColor ?? _hoverColor);
        if (_focused)
            ctx.DrawRoundedRect(box, new CornerRadius(2f), Color.Transparent, _focusRingColor, new Thickness(1.5f));
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is not SplitterHandleElement h) return;
        IsGrabbed = h.IsGrabbed;
        HoverColor = h.HoverColor;
        GrabbedColor = h.GrabbedColor;
        OnKeyStep = h.OnKeyStep;
        OnKeyEnd = h.OnKeyEnd;
    }
}
