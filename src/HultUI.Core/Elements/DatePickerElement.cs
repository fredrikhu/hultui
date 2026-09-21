using System.Globalization;
using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// A field showing a date, opening a <see cref="CalendarElement"/> beneath itself.
///
/// <para><b>Keyboard.</b> The W3C combobox pattern, the same one
/// <see cref="DropdownElement"/> follows: focus stays on the field while the calendar is
/// open and the arrows are forwarded into it, so nothing about which control the keyboard
/// is "in" ever has to be guessed. Enter, Space or Down opens; the navigation keys move
/// the calendar's focus cell; Enter commits; Escape closes and keeps the old value;
/// Delete or Backspace clears the field when <see cref="AllowClear"/> is set.</para>
///
/// <para>The field measures to its formatted date rather than to whatever is currently in
/// it, so picking a date never resizes the control.</para>
/// </summary>
public class DatePickerElement : Element
{

    private DateOnly? _value;
    private bool _focused;
    private bool _open;
    private OverlayHandle _popupHandle;
    private CalendarElement? _calendar;

    // Set when the host dismissed the popup because of a press on the field itself: that
    // press's own PointerDown arrives right after the dismissal and must read as "close"
    // (which already happened), not "open again". Mirrors DropdownElement.
    private bool _suppressOpenFromDismiss;

    private DropdownTheme _theme = new();
    private Color _focusRingColor = new(59, 130, 246, 180);

    public DatePickerElement()
    {
        IsFocusable = true;
        Role = AccessibilityRole.ComboBox;
        AccessibleExpanded = false;
        Inherited.Cursor = CursorType.Pointer;

        OnOwn<PointerDownEvent>(OnPointerDown);
        OnOwn<FocusEvent>(_ => { _focused = true; MarkPaintDirty(); });
        OnOwn<BlurEvent>(_ =>
        {
            _focused = false;
            _suppressOpenFromDismiss = false;
            ClosePopup();
            MarkPaintDirty();
        });
        OnOwn<KeyDownEvent>(OnKeyDown);
        UpdateAccessibleName();
    }

    public static DatePickerElement New(DateOnly? value = null,
        [OriginFile] string? file = null, [OriginLine] int line = 0)
    {
        var picker = new DatePickerElement();
        picker.Value = value;
        return (DatePickerElement)picker.WithOrigin(file, line);
    }

    // ── Model ────────────────────────────────────────────────────────────

    /// <summary>The chosen date, or null for none. Setting it from code does not raise
    /// <see cref="OnValueChanged"/>.</summary>
    public DateOnly? Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            if (_calendar is not null) _calendar.SelectedDate = value;
            UpdateAccessibleName();
            // Layout, not just paint: with a long format the field's width follows its
            // value (see MeasureContent), so a repaint alone would clip the new date.
            MarkLayoutDirty();
        }
    }

    /// <summary>How the field prints the date. The calendar is unaffected.</summary>
    public string Format { get; set; } = "d";

    /// <summary>Shown, in the placeholder colour, while there is no date.</summary>
    public string Placeholder { get; set; } = "Select a date…";

    /// <summary>Formats the field and names the calendar's months and weekdays.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.CurrentCulture;

    /// <summary>Earliest selectable day, inclusive.</summary>
    public DateOnly? MinDate { get; set; }

    /// <summary>Latest selectable day, inclusive.</summary>
    public DateOnly? MaxDate { get; set; }

    /// <summary>What the calendar rings as today. Settable for the same reason
    /// <see cref="CalendarElement.Today"/> is.</summary>
    public DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Which day the calendar's weeks start on.</summary>
    public DayOfWeek FirstDayOfWeek { get; set; } = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

    /// <summary>Whether Delete and Backspace empty the field. Off for a date that is
    /// required, so the control cannot be put into a state a form will reject.</summary>
    public bool AllowClear { get; set; } = true;

    /// <summary>Runs when the user picks or clears a date — never when
    /// <see cref="Value"/> is assigned from code.</summary>
    public Action<DateOnly?>? OnValueChanged { get; set; }

    /// <summary>Fluent registration of <see cref="OnValueChanged"/>.</summary>
    public DatePickerElement Changed(Action<DateOnly?> handler)
    {
        OnValueChanged = handler;
        return this;
    }

    /// <summary>True while the calendar is open.</summary>
    public bool IsOpen => _open;

    /// <summary>The open calendar, or null when closed. For tests and for a host that
    /// wants to drive the popup directly.</summary>
    public CalendarElement? Calendar => _calendar;

    /// <summary>What the field prints right now.</summary>
    public string DisplayText => _value is { } d ? d.ToString(Format, Culture) : Placeholder;

    private void UpdateAccessibleName()
    {
        AccessibleName = _value is { } d ? d.ToString(Format, Culture) : Placeholder;
        NotifyAccessibilityPropertyChanged(AccessibilityProperties.Value);
    }

    // ── Open / close ─────────────────────────────────────────────────────

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.Button != 0) return;
        if (_suppressOpenFromDismiss)
        {
            _suppressOpenFromDismiss = false;
            return;
        }
        if (_open) ClosePopup();
        else OpenPopup();
    }

    /// <summary>Opens the calendar under the field.</summary>
    public void OpenPopup()
    {
        if (_open || OverlayHost is null) return;

        var calendar = new CalendarElement
        {
            Today = Today,
            MinDate = MinDate,
            MaxDate = MaxDate,
            Culture = Culture,
            FirstDayOfWeek = FirstDayOfWeek,
            SelectedDate = _value,
        };
        // With no date yet, open on today rather than on whatever month a default
        // DateOnly would land in — there is no worse first screen than January 0001.
        if (_value is null) calendar.FocusedDate = Today;
        calendar.OnDateSelected = date =>
        {
            Commit(date);
            ClosePopup();
        };
        // No theme call here: the host style-resolves overlay content when it is shown,
        // which is what hands the calendar its CalendarTheme.

        // Screen box, not layout box — see the same line in DropdownElement.
        var box = ScreenBorderBox;
        _popupHandle = OverlayHost.ShowOverlay(calendar, box.X, box.Bottom + _theme.PopupGap,
            dismissOnOutsideClick: true, hitTestable: true, onDismissed: OnPopupDismissed);
        _calendar = calendar;
        _open = true;
        AccessibleExpanded = true;
        MarkPaintDirty();
    }

    /// <summary>Closes the calendar, leaving the value as it is.</summary>
    public void ClosePopup()
    {
        if (!_open) return;
        var handle = _popupHandle;
        _popupHandle = default;
        _calendar = null;
        _open = false;
        AccessibleExpanded = false;
        OverlayHost?.CloseOverlay(handle);
        MarkPaintDirty();
    }

    private void OnPopupDismissed()
    {
        _popupHandle = default;
        _calendar = null;
        _open = false;
        AccessibleExpanded = false;
        _suppressOpenFromDismiss = true;
        MarkPaintDirty();
    }

    private void Commit(DateOnly? date)
    {
        if (_value == date) return;
        _value = date;
        UpdateAccessibleName();
        MarkLayoutDirty();
        OnValueChanged?.Invoke(date);
    }

    // ── Keyboard ─────────────────────────────────────────────────────────

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (!_focused) return;

        if (!_open)
        {
            if (AllowClear && evt.Key is KeyCode.Delete or KeyCode.Backspace && _value is not null)
            {
                Commit(null);
                evt.StopPropagation = true;
                return;
            }
            if (evt.Key is KeyCode.Enter or KeyCode.Space or KeyCode.Down)
            {
                OpenPopup();
                evt.StopPropagation = true;
            }
            return;
        }

        if (evt.Key == KeyCode.Escape)
        {
            ClosePopup();
            evt.StopPropagation = true;
            return;
        }
        // Everything the calendar understands is its business; anything else keeps
        // bubbling, so Tab still moves on and the app's own shortcuts still fire.
        if (_calendar is { } calendar && calendar.HandleKey(evt.Key, evt.Modifiers))
            evt.StopPropagation = true;
    }

    // ── Theme / measure / render ─────────────────────────────────────────

    /// <summary>
    /// This field's own look for the box it borrows from the dropdown, laid over the theme's —
    /// see <see cref="DropdownStyle"/>. Compared by reference, so hand the same instance each build.
    /// </summary>
    public DropdownStyle? Style
    {
        get => _style;
        set
        {
            if (ReferenceEquals(_style, value)) return;
            _style = value;
            MarkStyleDirty();
        }
    }
    private DropdownStyle? _style;

    protected internal override void ApplyTheme(Theme theme)
    {
        // The same knobs as the dropdown's closed face, deliberately: a form with a
        // dropdown beside a date field should not have two differently-shaped boxes in it.
        _theme = theme.Dropdown.Resolve(_style);
        _focusRingColor = theme.FocusRing;
    }

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
    {
        var style = ResolvedInherited with { FontSize = _theme.FontSize };

        // Measured against a *sample* date as well as the live one, so a numeric format —
        // where the sample is always the widest a date gets — keeps the field a fixed size
        // whatever is picked. A long format ("Monday, 15 September") has no such widest
        // case, so the current value is measured too rather than left to ellipsize.
        float sample = ctx.MeasureText(new DateOnly(2026, 12, 26).ToString(Format, Culture),
            style, float.MaxValue, 1).Width;
        float current = _value is null ? 0f
            : ctx.MeasureText(DisplayText, style, float.MaxValue, 1).Width;
        float placeholder = ctx.MeasureText(Placeholder, style, float.MaxValue, 1).Width;
        // Height off the glyph either way, so the field does not resize with the mark's shape.
        var glyph = ctx.MeasureText(ChevronPainter.Glyph, style, float.MaxValue, 1);
        float chevronWidth = ChevronPainter.Width(_theme.ChevronShape, _theme.FontSize, ctx, style);

        float width = Math.Max(_theme.MinWidth,
            _theme.Padding.Left + Math.Max(Math.Max(sample, current), placeholder)
            + _theme.ChevronGap + chevronWidth + _theme.Padding.Right);
        return new Size(width, _theme.Padding.Top + glyph.Height + _theme.Padding.Bottom);
    }

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteBorderBox;
        ctx.DrawRoundedRect(box, new CornerRadius(_theme.CornerRadius), _theme.Background,
            _focused ? _focusRingColor : _theme.BorderColor,
            new Thickness(_focused ? _theme.FocusBorderWidth : _theme.BorderWidth));

        var style = ResolvedInherited with
        {
            FontSize = _theme.FontSize,
            TextColor = _value is null ? _theme.PlaceholderTextColor : _theme.TextColor,
        };
        var chevronStyle = style with { TextColor = _theme.ChevronColor };
        float chevronWidth = ChevronPainter.Width(_theme.ChevronShape, _theme.FontSize, ctx, chevronStyle);

        float textLeft = box.X + _theme.Padding.Left;
        float textRight = box.Right - _theme.Padding.Right - chevronWidth - _theme.ChevronGap;
        ctx.DrawText(DisplayText,
            new Rect(textLeft, box.Y + _theme.Padding.Top, Math.Max(0, textRight - textLeft),
                box.Height - _theme.Padding.Top - _theme.Padding.Bottom),
            style, TextOverflow.Ellipsis, 1);

        ChevronPainter.Draw(ctx, _theme.ChevronShape,
            new Rect(box.Right - _theme.Padding.Right - chevronWidth, box.Y, chevronWidth, box.Height),
            _theme.ChevronColor, _theme.ChevronStrokeWidth, chevronStyle, _theme.FontSize);
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is not DatePickerElement p) return;
        Format = p.Format;
        Placeholder = p.Placeholder;
        Style = p.Style;
        Culture = p.Culture;
        MinDate = p.MinDate;
        MaxDate = p.MaxDate;
        Today = p.Today;
        FirstDayOfWeek = p.FirstDayOfWeek;
        AllowClear = p.AllowClear;
        OnValueChanged = p.OnValueChanged;
        Value = p._value;
    }
}
