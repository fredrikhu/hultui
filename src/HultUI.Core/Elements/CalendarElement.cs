using System.Globalization;
using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// A month at a time: a header that steps through months, a row of weekday headings, and
/// a six-by-seven grid of days. Clicking a day selects it; the keyboard moves a focus
/// cell around and Enter selects wherever it is.
///
/// <para>Used on its own where a date is chosen inline, and as the popup of a
/// <see cref="DatePickerElement"/>. It draws itself rather than composing child elements:
/// forty-two boxes rebuilt on every hover would be forty-two allocations to move a
/// highlight, and every cell in a grid has to be the same size anyway, which is the one
/// thing flex layout is worst at.</para>
///
/// <para><b>Six rows, always.</b> A month needs four to six depending on its length and
/// where it starts. Sizing to the month means the popup changes height as you page
/// through it and the day under the pointer becomes a different day; a fixed six rows
/// (with the neighbouring months' days shown greyed) keeps every cell where it was.</para>
///
/// <para><b>Today is a property</b>, not a call to the clock, so a test can render a
/// calendar that will still look the same tomorrow.</para>
/// </summary>
public class CalendarElement : Element
{
    private const int Rows = 6;
    private const int Columns = 7;
    private const string PrevGlyph = "‹";
    private const string NextGlyph = "›";

    private DateOnly _today = DateOnly.FromDateTime(DateTime.Now);
    private DateOnly? _selected;
    private DateOnly _displayMonth;         // always the first of the shown month
    private DateOnly _focusDate;
    private DateOnly? _hoverDate;
    private DateOnly? _min;
    private DateOnly? _max;
    private bool _focused;
    private bool _prevHot;
    private bool _nextHot;

    private CalendarTheme _theme = new();

    public CalendarElement()
    {
        IsFocusable = true;
        // No grid role exists in this accessibility model, and inventing one would have to
        // be carried through both platform bridges to mean anything. A group whose value is
        // the focused date is what an assistive client can actually use today: the value
        // changes as the arrows move, which is the information a reader needs.
        Role = AccessibilityRole.Group;
        Inherited.Cursor = CursorType.Pointer;

        _displayMonth = FirstOfMonth(_today);
        _focusDate = _today;
        UpdateAccessibleValue();

        OnOwn<PointerDownEvent>(OnPointerDown);
        OnOwn<PointerMoveEvent>(OnPointerMove);
        OnOwn<PointerLeaveEvent>(_ =>
        {
            if (_hoverDate is null && !_prevHot && !_nextHot) return;
            _hoverDate = null;
            _prevHot = _nextHot = false;
            MarkPaintDirty();
        });
        OnOwn<FocusEvent>(_ => { _focused = true; MarkPaintDirty(); });
        OnOwn<BlurEvent>(_ => { _focused = false; MarkPaintDirty(); });
        OnOwn<KeyDownEvent>(OnKeyDown);
    }

    public static CalendarElement New(DateOnly? selected = null,
        [OriginFile] string? file = null, [OriginLine] int line = 0)
    {
        var cal = new CalendarElement();
        if (selected is { } d) cal.SelectedDate = d;
        return (CalendarElement)cal.WithOrigin(file, line);
    }

    // ── Model ────────────────────────────────────────────────────────────

    /// <summary>The chosen day, or null. Setting it moves the view to its month and puts
    /// the keyboard focus cell on it; it does not raise <see cref="OnDateSelected"/>,
    /// which is reserved for the user's own choice.</summary>
    public DateOnly? SelectedDate
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            if (value is { } d)
            {
                _displayMonth = FirstOfMonth(d);
                _focusDate = d;
            }
            UpdateAccessibleValue();
            MarkPaintDirty();
        }
    }

    /// <summary>The month on screen. Any date in the month will do; it is stored as the
    /// first of it.</summary>
    public DateOnly DisplayMonth
    {
        get => _displayMonth;
        set
        {
            var first = FirstOfMonth(value);
            if (_displayMonth == first) return;
            _displayMonth = first;
            MarkPaintDirty();
        }
    }

    /// <summary>The cell the keyboard is on. Always a real date, selected or not.</summary>
    public DateOnly FocusedDate
    {
        get => _focusDate;
        set
        {
            if (_focusDate == value) return;
            _focusDate = value;
            _displayMonth = FirstOfMonth(value);
            UpdateAccessibleValue();
            MarkPaintDirty();
        }
    }

    /// <summary>What the calendar rings as today. Settable so a rendered calendar can be
    /// compared against a baseline that does not expire.</summary>
    public DateOnly Today
    {
        get => _today;
        set { if (_today == value) return; _today = value; MarkPaintDirty(); }
    }

    /// <summary>Earliest selectable day, inclusive. Days before it are drawn dimmed and
    /// cannot be chosen by pointer or keyboard.</summary>
    public DateOnly? MinDate
    {
        get => _min;
        set { _min = value; MarkPaintDirty(); }
    }

    /// <summary>Latest selectable day, inclusive.</summary>
    public DateOnly? MaxDate
    {
        get => _max;
        set { _max = value; MarkPaintDirty(); }
    }

    /// <summary>Which day the week starts on. Defaults to the culture's own answer, which
    /// is the only one that is right in more than one country.</summary>
    public DayOfWeek FirstDayOfWeek { get; set; } = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

    /// <summary>Names the months and the weekdays. Explicit rather than read from the
    /// ambient culture at draw time, so a caller can pin it.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.CurrentCulture;

    /// <summary>Format of the header's month name.</summary>
    public string MonthFormat { get; set; } = "MMMM yyyy";

    /// <summary>Runs when the user picks a day — click, Enter or Space. Not raised by
    /// setting <see cref="SelectedDate"/> from code.</summary>
    public Action<DateOnly>? OnDateSelected { get; set; }

    /// <summary>Fluent registration of <see cref="OnDateSelected"/>.</summary>
    public CalendarElement Selected(Action<DateOnly> handler)
    {
        OnDateSelected = handler;
        return this;
    }

    /// <summary>Whether a day is inside <see cref="MinDate"/>/<see cref="MaxDate"/>.</summary>
    public bool IsSelectable(DateOnly date) =>
        (_min is not { } lo || date >= lo) && (_max is not { } hi || date <= hi);

    /// <summary>The date drawn in a given cell — row 0..5, column 0..6 — including the
    /// neighbouring months' days that fill the corners of the grid.</summary>
    public DateOnly DateAt(int row, int column) => GridStart().AddDays(row * Columns + column);

    /// <summary>The first cell of the grid: the <see cref="FirstDayOfWeek"/> on or before
    /// the first of the displayed month.</summary>
    private DateOnly GridStart()
    {
        int offset = ((int)_displayMonth.DayOfWeek - (int)FirstDayOfWeek + 7) % 7;
        return _displayMonth.AddDays(-offset);
    }

    private static DateOnly FirstOfMonth(DateOnly d) => new(d.Year, d.Month, 1);

    private void UpdateAccessibleValue()
    {
        AccessibleName = _selected is { } s
            ? s.ToString("D", Culture)
            : "No date selected";
        NotifyAccessibilityPropertyChanged(AccessibilityProperties.Value);
    }

    /// <summary>The value assistive tech reads: where the keyboard is, and what is chosen.</summary>
    internal string AccessibleValueText =>
        _selected is { } s ? s.ToString("d", Culture) : "";

    // ── Commands ─────────────────────────────────────────────────────────

    /// <summary>Steps the view by whole months, keeping the focus cell on the same day
    /// number where the month is long enough for it (31 January back a month lands on 28
    /// or 29 February, not on 3 March).</summary>
    public void ShiftMonths(int months)
    {
        var target = _displayMonth.AddMonths(months);
        _displayMonth = target;
        int day = Math.Min(_focusDate.Day, DateTime.DaysInMonth(target.Year, target.Month));
        _focusDate = new DateOnly(target.Year, target.Month, day);
        UpdateAccessibleValue();
        MarkPaintDirty();
    }

    /// <summary>Selects a day and raises <see cref="OnDateSelected"/>. Ignored when the
    /// day is outside the allowed range.</summary>
    public void SelectDate(DateOnly date)
    {
        if (!IsSelectable(date)) return;
        _selected = date;
        _focusDate = date;
        _displayMonth = FirstOfMonth(date);
        UpdateAccessibleValue();
        MarkPaintDirty();
        OnDateSelected?.Invoke(date);
    }

    // ── Input ────────────────────────────────────────────────────────────

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.Button != 0) return;
        Focus();
        var local = evt.LocalPosition;

        if (HeaderHit(local) is { } step)
        {
            ShiftMonths(step);
            evt.StopPropagation = true;
            return;
        }
        if (CellHit(local) is { } date)
        {
            SelectDate(date);
            evt.StopPropagation = true;
        }
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        var local = evt.LocalPosition;
        var date = CellHit(local);
        var step = HeaderHit(local);
        bool prev = step == -1;
        bool next = step == 1;
        if (date == _hoverDate && prev == _prevHot && next == _nextHot) return;
        _hoverDate = date;
        _prevHot = prev;
        _nextHot = next;
        MarkPaintDirty();
    }

    /// <summary>Where the grid actually starts inside the content box — the same
    /// centring the renderer applies, so the cells are where they look.</summary>
    private float GridLeft()
    {
        var pad = _theme.Padding;
        float slack = Math.Max(0f,
            Layout.AbsoluteContentBox.Width - pad.Left - pad.Right - Columns * _theme.CellWidth);
        return pad.Left + slack / 2f;
    }

    /// <summary>-1 for the previous-month arrow, 1 for the next, null for neither.</summary>
    private int? HeaderHit(Point local)
    {
        var pad = _theme.Padding;
        if (local.Y < pad.Top || local.Y > pad.Top + _theme.HeaderHeight) return null;
        float left = GridLeft();
        float right = left + Columns * _theme.CellWidth;
        if (local.X >= left && local.X < left + _theme.CellWidth) return -1;
        if (local.X <= right && local.X > right - _theme.CellWidth) return 1;
        return null;
    }

    private DateOnly? CellHit(Point local)
    {
        var pad = _theme.Padding;
        float gridTop = pad.Top + _theme.HeaderHeight + _theme.WeekdayHeight;
        float x = local.X - GridLeft();
        float y = local.Y - gridTop;
        if (x < 0 || y < 0) return null;

        int column = (int)(x / _theme.CellWidth);
        int row = (int)(y / _theme.CellHeight);
        if (column is < 0 or >= Columns || row is < 0 or >= Rows) return null;
        return DateAt(row, column);
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (!_focused) return;
        if (HandleKey(evt.Key, evt.Modifiers))
            evt.StopPropagation = true;
    }

    /// <summary>
    /// Applies a navigation key, returning whether it meant anything here.
    ///
    /// <para>Separate from the event handler because a <see cref="DatePickerElement"/>
    /// keeps the keyboard focus on itself while its calendar is open — the combobox
    /// pattern — and forwards the arrows in. The calendar in that case never receives a
    /// key event of its own.</para>
    /// </summary>
    internal bool HandleKey(KeyCode key, KeyModifiers modifiers)
    {
        bool shift = modifiers.HasFlag(KeyModifiers.Shift);

        switch (key)
        {
            case KeyCode.Left: MoveFocus(-1); break;
            case KeyCode.Right: MoveFocus(1); break;
            case KeyCode.Up: MoveFocus(-Columns); break;
            case KeyCode.Down: MoveFocus(Columns); break;
            // Shift turns the page keys into years — the only cheap way to reach a
            // birthdate without forty clicks on the arrow.
            case KeyCode.PageUp: ShiftFocusMonths(shift ? -12 : -1); break;
            case KeyCode.PageDown: ShiftFocusMonths(shift ? 12 : 1); break;
            case KeyCode.Home: SetFocusDate(FirstOfMonth(_focusDate)); break;
            case KeyCode.End:
                SetFocusDate(new DateOnly(_focusDate.Year, _focusDate.Month,
                    DateTime.DaysInMonth(_focusDate.Year, _focusDate.Month)));
                break;
            case KeyCode.Enter:
            case KeyCode.Space:
                SelectDate(_focusDate);
                break;
            default:
                return false;   // everything else keeps bubbling (Escape closes a picker)
        }
        return true;
    }

    private void MoveFocus(int days) => SetFocusDate(_focusDate.AddDays(days));

    private void ShiftFocusMonths(int months)
    {
        var target = _focusDate.AddMonths(months);
        SetFocusDate(target);
    }

    /// <summary>Moves the focus cell, clamped into the allowed range and dragging the
    /// displayed month along with it.</summary>
    private void SetFocusDate(DateOnly date)
    {
        if (_min is { } lo && date < lo) date = lo;
        if (_max is { } hi && date > hi) date = hi;
        if (date == _focusDate) return;
        _focusDate = date;
        _displayMonth = FirstOfMonth(date);
        UpdateAccessibleValue();
        MarkPaintDirty();
    }

    // ── Theme / measure / render ─────────────────────────────────────────

    protected internal override void ApplyTheme(Theme theme)
    {
        _theme = theme.Get<CalendarTheme>();
        MarkLayoutDirty();
    }

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
    {
        var pad = _theme.Padding;
        return new Size(
            pad.Left + Columns * _theme.CellWidth + pad.Right,
            pad.Top + _theme.HeaderHeight + _theme.WeekdayHeight + Rows * _theme.CellHeight + pad.Bottom);
    }

    /// <summary>Unbounded: laid out smaller than it measures, it draws its grid past the box.</summary>
    public override Rect? PaintBounds(IRenderContext ctx) => null;

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteBorderBox;
        ctx.DrawRoundedRect(box, new CornerRadius(_theme.CornerRadius), _theme.Background,
            _theme.BorderColor, new Thickness(_theme.BorderWidth));

        var content = Layout.AbsoluteContentBox;
        var pad = _theme.Padding;
        float gridWidth = Columns * _theme.CellWidth;
        // A calendar has one right size, but a flex parent will happily stretch it (a
        // column stretches its children by default). Centring the grid in whatever width
        // it ends up with means a stretched calendar still looks deliberate instead of
        // leaving the month huddled against the left edge of a wide box.
        float slack = Math.Max(0f, content.Width - pad.Left - pad.Right - gridWidth);
        float left = content.X + pad.Left + slack / 2f;
        float top = content.Y + pad.Top;

        RenderHeader(ctx, left, top, gridWidth);
        RenderWeekdays(ctx, left, top + _theme.HeaderHeight);
        RenderDays(ctx, left, top + _theme.HeaderHeight + _theme.WeekdayHeight);
    }

    private void RenderHeader(IRenderContext ctx, float left, float top, float gridWidth)
    {
        var style = ResolvedInherited with
        {
            FontSize = _theme.HeaderFontSize,
            TextColor = _theme.TextColor,
            FontWeight = FontWeight.Medium,
        };

        var title = _displayMonth.ToString(MonthFormat, Culture);
        var titleRect = new Rect(left + _theme.CellWidth, top,
            Math.Max(0, gridWidth - 2 * _theme.CellWidth), _theme.HeaderHeight);
        ctx.DrawText(title, Centred(ctx, titleRect, style), style with { TextAlign = TextAlign.Center },
            TextOverflow.Ellipsis, 1);

        Arrow(PrevGlyph, left, _prevHot);
        Arrow(NextGlyph, left + gridWidth - _theme.CellWidth, _nextHot);

        void Arrow(string glyph, float x, bool hot)
        {
            var rect = new Rect(x, top, _theme.CellWidth, _theme.HeaderHeight);
            if (hot)
                ctx.DrawRoundedRect(rect, new CornerRadius(_theme.CellCornerRadius),
                    _theme.HoverBackground, Color.Transparent, default);
            ctx.DrawText(glyph, Centred(ctx, rect, style),
                style with { TextColor = _theme.MutedTextColor, TextAlign = TextAlign.Center },
                TextOverflow.Clip, 1);
        }
    }

    private void RenderWeekdays(IRenderContext ctx, float left, float top)
    {
        var style = ResolvedInherited with
        {
            FontSize = _theme.WeekdayFontSize,
            TextColor = _theme.MutedTextColor,
            TextAlign = TextAlign.Center,
        };
        var names = Culture.DateTimeFormat.ShortestDayNames;
        for (int i = 0; i < Columns; i++)
        {
            int day = ((int)FirstDayOfWeek + i) % 7;
            var rect = new Rect(left + i * _theme.CellWidth, top, _theme.CellWidth, _theme.WeekdayHeight);
            ctx.DrawText(names[day], Centred(ctx, rect, style), style, TextOverflow.Clip, 1);
        }
    }

    /// <summary>
    /// The band inside <paramref name="cell"/> that holds one line of text, centred.
    ///
    /// <para><see cref="IRenderContext.DrawText"/> lays lines out from the top of the rect it
    /// is given — <see cref="TextAlign"/> is horizontal only — so a label handed a whole cell
    /// sits high in it by exactly the leftover height. Every label this calendar draws lives
    /// in a cell taller than its own line, so every one of them goes through here.</para>
    ///
    /// <para>The height comes from the font rather than from the string: measuring the digits
    /// would let a row of "1"s sit differently from a row with a "7" in it.</para>
    /// </summary>
    private static Rect Centred(IRenderContext ctx, Rect cell, ResolvedInheritedStyle style)
    {
        float lineHeight = ctx.MeasureText("0", style).Height;
        if (lineHeight <= 0f || lineHeight >= cell.Height) return cell;
        return new Rect(cell.X, cell.Y + (cell.Height - lineHeight) / 2f, cell.Width, lineHeight);
    }

    private void RenderDays(IRenderContext ctx, float left, float top)
    {
        var baseStyle = ResolvedInherited with { FontSize = _theme.FontSize, TextAlign = TextAlign.Center };
        var radius = new CornerRadius(_theme.CellCornerRadius);

        for (int row = 0; row < Rows; row++)
        for (int col = 0; col < Columns; col++)
        {
            var date = DateAt(row, col);
            var cell = new Rect(left + col * _theme.CellWidth, top + row * _theme.CellHeight,
                _theme.CellWidth, _theme.CellHeight);
            // A pip of margin so adjacent selected days don't merge into a bar.
            var pill = new Rect(cell.X + 1f, cell.Y + 1f, cell.Width - 2f, cell.Height - 2f);

            bool selected = _selected == date;
            bool enabled = IsSelectable(date);
            bool outside = date.Month != _displayMonth.Month;

            if (selected)
                ctx.DrawRoundedRect(pill, radius, _theme.SelectedBackground, Color.Transparent, default);
            else if (_hoverDate == date && enabled)
                ctx.DrawRoundedRect(pill, radius, _theme.HoverBackground, Color.Transparent, default);

            // Today gets an outline rather than a fill, so it can coexist with the
            // selection on the same cell without either of them disappearing.
            if (date == _today && !selected)
                ctx.DrawRoundedRect(pill, radius, Color.Transparent, _theme.TodayColor, new Thickness(1f));

            if (_focused && date == _focusDate)
                ctx.DrawRoundedRect(pill, radius, Color.Transparent, _theme.FocusRingColor, new Thickness(1.5f));

            var color = !enabled ? _theme.DisabledTextColor
                : selected ? _theme.SelectedTextColor
                : outside ? _theme.MutedTextColor
                : _theme.TextColor;
            ctx.DrawText(date.Day.ToString(CultureInfo.InvariantCulture), Centred(ctx, cell, baseStyle),
                baseStyle with { TextColor = color }, TextOverflow.Clip, 1);
        }
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is not CalendarElement c) return;
        _today = c._today;
        _min = c._min;
        _max = c._max;
        FirstDayOfWeek = c.FirstDayOfWeek;
        Culture = c.Culture;
        MonthFormat = c.MonthFormat;
        OnDateSelected = c.OnDateSelected;
        // Selection last: it moves the displayed month and the focus cell, and the
        // rebuild's own answer for those is what should win.
        SelectedDate = c._selected;
        _displayMonth = c._displayMonth;
        _focusDate = c._focusDate;
        UpdateAccessibleValue();
    }
}
