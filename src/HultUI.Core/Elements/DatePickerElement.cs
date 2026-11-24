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
///
/// <para><b>Typing.</b> With <see cref="AllowTyping"/> the date is also typed, a part at a time,
/// as a <see cref="TimePickerElement"/>'s time is: see there.</para>
/// </summary>
public class DatePickerElement : Element
{
    /// <summary>Which part of the date a segment edits, with <see cref="AllowTyping"/>.</summary>
    public enum Segment { Year, Month, Day }

    private DateOnly? _value;
    private bool _focused;
    private bool _open;
    private OverlayHandle _popupHandle;
    private CalendarElement? _calendar;

    // Set when the host dismissed the popup because of a press on the field itself: that
    // press's own PointerDown arrives right after the dismissal and must read as "close"
    // (which already happened), not "open again". Mirrors DropdownElement. A dismissal with no
    // press (the window was deactivated, a frame press, DismissPopups) is cleared after the next
    // frame.
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
            CommitPendingYear();
            ClosePopup();
            MarkPaintDirty();
        });
        OnOwn<KeyDownEvent>(OnKeyDown);
        NotifyValueChanged();
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
            ResetTyping();
            if (_calendar is not null) _calendar.SelectedDate = value;
            NotifyValueChanged();
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

    // The node is made again: the date is its value, and its name too when the app gave none
    // (AccessibilityTreeBuilder.ResolveName, DisplayText). Writing the date into AccessibleName
    // replaced the name an app gave: "Start date" read as "2026-09-15".
    private void NotifyValueChanged() => NotifyAccessibilityPropertyChanged(AccessibilityProperties.Value);

    // ── Open / close ─────────────────────────────────────────────────────

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.Button != 0) return;
        // Typed into, the date is where a press puts the keyboard; only the chevron opens the
        // calendar. A press that closed the calendar lands on the date as well.
        float x = evt.LocalPosition.X + Layout.AbsoluteContentBox.X - Layout.AbsoluteBorderBox.X;
        if (Typing && x < ChevronLeft)
        {
            _suppressOpenFromDismiss = false;
            ClosePopup();
            CommitPendingYear();
            if (SegmentIndexAt(x) is { } index && index != _segment)
            {
                _segment = index;
                ResetTyping();
            }
            Focus();
            MarkPaintDirty();
            evt.StopPropagation = true;
            return;
        }
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
        CommitPendingYear();

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
        var box = WindowBorderBox;
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
        // Only the press that dismissed it is swallowed, and that press arrives before the next
        // frame; a dismissal with no press (the window was deactivated) must not eat a later click.
        OverlayHost?.RequestPostFrameCallback(() => _suppressOpenFromDismiss = false);
        MarkPaintDirty();
    }

    private void Commit(DateOnly? date)
    {
        if (_value == date) return;
        _value = date;
        NotifyValueChanged();
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
                ResetTyping();
                evt.StopPropagation = true;
                return;
            }
            if (Typing && HandleTypingKey(evt))
            {
                evt.StopPropagation = true;
                return;
            }
            // Down is a part's step while typing, so Alt+Down and F4 open it then, as on Windows.
            if (evt.Key is KeyCode.Enter or KeyCode.Space or KeyCode.Down or KeyCode.F4)
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

    // ── Typing ───────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the date can be typed, a part at a time, as a <see cref="TimePickerElement"/>'s
    /// time is: a press on the year, month or day puts the keyboard there, digits type into it and
    /// move on when no further digit could fit, Left and Right move between the parts, and Up and
    /// Down step the one you are on. The calendar then opens from the chevron, Alt+Down or F4, as
    /// well as Enter and Space. Off by default, when any press opens the calendar.
    /// </summary>
    /// <remarks>
    /// Needs a numeric <see cref="Format"/>: the culture's short date (<c>"d"</c>, the default) or
    /// a pattern of <c>y</c>, <c>M</c> and <c>d</c>. A month or day by name, or any other standard
    /// format, leaves the field as it is without it. A year is taken once its four digits are in,
    /// or when the keyboard leaves it with fewer: two then read as the culture's two-digit year.
    /// </remarks>
    public bool AllowTyping
    {
        get => _allowTyping;
        set
        {
            if (_allowTyping == value) return;
            _allowTyping = value;
            _segment = 0;
            ResetTyping();
            MarkLayoutDirty();
        }
    }
    private bool _allowTyping;

    /// <summary>Which part the keyboard is on, with <see cref="AllowTyping"/>.</summary>
    public Segment ActiveSegment => KindOf(SegmentToken(_segment).Kind);

    private int _segment;
    // Digits typed into the current part since it was entered, as in TimePickerElement.
    private int _typed;
    private int _typedCount;

    private bool Typing => _allowTyping && Tokens() is not null;

    /// <summary>A run of the format: a part (its letter, y, M or d, and how many digits it
    /// shows) or the literal text between parts (Kind '\0').</summary>
    private readonly record struct Token(char Kind, int Length, string Literal);

    private string? _tokensFormat;
    private CultureInfo? _tokensCulture;
    private List<Token>? _tokens;

    private List<Token>? Tokens()
    {
        if (_tokensFormat == Format && ReferenceEquals(_tokensCulture, Culture)) return _tokens;
        _tokensFormat = Format;
        _tokensCulture = Culture;
        var dtf = Culture.DateTimeFormat;
        _tokens = Format == "d" ? ParseFormat(dtf.ShortDatePattern, dtf.DateSeparator)
            : Format.Length == 1 ? null   // another standard format: not numeric
            : ParseFormat(Format, dtf.DateSeparator);
        return _tokens;
    }

    /// <summary>The parts of a numeric date pattern, or null when it is not one: a letter other
    /// than y, M or d, a month or day by name, or not exactly one each of year, month and day.</summary>
    private static List<Token>? ParseFormat(string pattern, string separator)
    {
        var tokens = new List<Token>();
        var literal = new System.Text.StringBuilder();
        void Flush()
        {
            if (literal.Length == 0) return;
            tokens.Add(new Token('\0', 0, literal.ToString()));
            literal.Clear();
        }
        for (int i = 0; i < pattern.Length;)
        {
            char c = pattern[i];
            if (c is 'y' or 'M' or 'd')
            {
                int n = 1;
                while (i + n < pattern.Length && pattern[i + n] == c) n++;
                if (c != 'y' && n > 2) return null;
                Flush();
                tokens.Add(new Token(c, c == 'y' ? (n <= 2 ? 2 : 4) : n, ""));
                i += n;
            }
            else if (c is '\'' or '"')
            {
                int end = pattern.IndexOf(c, i + 1);
                if (end < 0) return null;
                literal.Append(pattern, i + 1, end - i - 1);
                i = end + 1;
            }
            else if (c == '\\' && i + 1 < pattern.Length) { literal.Append(pattern[i + 1]); i += 2; }
            else if (c == '/') { literal.Append(separator); i++; }
            else if (char.IsLetter(c)) return null;
            else { literal.Append(c); i++; }
        }
        Flush();
        var kinds = tokens.Where(t => t.Kind != '\0').Select(t => t.Kind).ToList();
        return kinds.Count == 3 && kinds.Distinct().Count() == 3 ? tokens : null;
    }

    private Token SegmentToken(int index) =>
        Tokens() is { } tokens ? tokens.Where(t => t.Kind != '\0').ElementAt(index) : new Token('y', 4, "");

    private static Segment KindOf(char kind) => kind switch
    {
        'y' => Segment.Year,
        'M' => Segment.Month,
        _ => Segment.Day,
    };

    private void ResetTyping()
    {
        _typed = 0;
        _typedCount = 0;
    }

    private bool HandleTypingKey(KeyDownEvent evt)
    {
        if (evt.Digit is { } digit)
        {
            TypeDigit(digit);
            return true;
        }
        bool plain = (evt.Modifiers & (KeyModifiers.Alt | KeyModifiers.Ctrl)) == 0;
        switch (evt.Key)
        {
            case KeyCode.Left when plain: MoveSegment(-1); return true;
            case KeyCode.Right when plain: MoveSegment(1); return true;
            case KeyCode.Up when plain: Step(1); return true;
            case KeyCode.Down when plain: Step(-1); return true;
            default: return false;
        }
    }

    /// <summary>Moves the keyboard to the part before or after, clamped at both ends so Tab
    /// still leaves the field. A year being typed is taken first.</summary>
    public void MoveSegment(int delta)
    {
        if (!Typing) return;
        CommitPendingYear();
        int next = Math.Clamp(_segment + delta, 0, 2);
        ResetTyping();
        if (next == _segment) return;
        _segment = next;
        MarkPaintDirty();
    }

    /// <summary>Puts the keyboard on a part.</summary>
    public void FocusSegment(Segment segment)
    {
        if (!Typing) return;
        CommitPendingYear();
        ResetTyping();
        for (int i = 0; i < 3; i++)
        {
            if (KindOf(SegmentToken(i).Kind) != segment) continue;
            _segment = i;
            MarkPaintDirty();
            return;
        }
    }

    /// <summary>
    /// Feeds one digit into the part the keyboard is on. A month or day takes it at once and
    /// moves on as soon as no further digit could be valid — after one digit for 2–9 in the
    /// month, after two otherwise; a second digit that would overshoot starts a fresh number. A
    /// four-digit year waits for its four.
    /// </summary>
    public void TypeDigit(int digit)
    {
        if (!Typing || digit is < 0 or > 9) return;
        var token = SegmentToken(_segment);
        if (token.Kind == 'y' && token.Length == 4)
        {
            _typed = _typedCount == 0 ? digit : _typed * 10 + digit;
            _typedCount++;
            if (_typedCount < 4)
            {
                MarkPaintDirty();
                return;
            }
            int year = _typed;
            ResetTyping();
            SetPart('y', year);
            MoveSegment(1);
            return;
        }

        var current = _value ?? Today;
        int max = token.Kind switch
        {
            'M' => 12,
            'd' => DateTime.DaysInMonth(current.Year, current.Month),
            _ => 99,
        };
        int candidate = _typedCount == 0 ? digit : _typed * 10 + digit;
        if (candidate > max)
        {
            candidate = digit;
            _typedCount = 0;
        }
        _typed = candidate;
        _typedCount++;
        SetPart(token.Kind, token.Kind == 'y' ? Culture.Calendar.ToFourDigitYear(candidate) : Math.Max(1, candidate));
        if (_typedCount >= 2 || candidate * 10 > max)
        {
            ResetTyping();
            MoveSegment(1);
        }
    }

    /// <summary>Steps the part the keyboard is on, wrapping within its own range: a month past
    /// December is January of the same year, a day past the month's last is its first.</summary>
    public void Step(int direction)
    {
        if (!Typing) return;
        CommitPendingYear();
        ResetTyping();
        var d = _value ?? Today;
        switch (SegmentToken(_segment).Kind)
        {
            case 'y': SetPart('y', d.Year + direction); break;
            case 'M': SetPart('M', ((d.Month - 1 + direction) % 12 + 12) % 12 + 1); break;
            default:
                int days = DateTime.DaysInMonth(d.Year, d.Month);
                SetPart('d', ((d.Day - 1 + direction) % days + days) % days + 1);
                break;
        }
    }

    /// <summary>A year left with fewer than four digits in it is taken as it is: two read as
    /// the culture's two-digit year, as a typed date would.</summary>
    private void CommitPendingYear()
    {
        if (_typedCount == 0 || !Typing) return;
        var token = SegmentToken(_segment);
        if (token.Kind != 'y' || token.Length != 4) return;
        int typed = _typed, count = _typedCount;
        ResetTyping();
        SetPart('y', count <= 2 ? Culture.Calendar.ToFourDigitYear(typed) : typed);
    }

    /// <summary>The date with one part replaced, the day kept inside its month and the whole
    /// inside <see cref="MinDate"/> and <see cref="MaxDate"/>. An empty field starts from today.</summary>
    private void SetPart(char kind, int number)
    {
        var d = _value ?? Today;
        int year = kind == 'y' ? Math.Clamp(number, 1, 9999) : d.Year;
        int month = kind == 'M' ? Math.Clamp(number, 1, 12) : d.Month;
        int day = Math.Clamp(kind == 'd' ? number : d.Day, 1, DateTime.DaysInMonth(year, month));
        var date = new DateOnly(year, month, day);
        if (MinDate is { } min && date < min) date = min;
        if (MaxDate is { } max && date > max) date = max;
        Commit(date);
        MarkPaintDirty();
    }

    // ── Layout of the typed parts ────────────────────────────────────────

    private TimeFieldTheme _segmentTheme = new();
    private float _digitWidth = 8f;
    private float _chevronWidth = 8f;
    private readonly Dictionary<string, float> _literalWidths = [];

    /// <summary>Where the chevron's own room starts, from the field's left edge: a press past it
    /// opens the calendar.</summary>
    private float ChevronLeft =>
        Layout.AbsoluteBorderBox.Width - _theme.Padding.Right - _chevronWidth - _theme.ChevronGap;

    /// <summary>One drawn run: a part, in digit cells so the date does not shuffle as it changes,
    /// or the text between parts.</summary>
    private IEnumerable<(string Text, int Segment, float Width)> TypingParts()
    {
        float pad = _segmentTheme.SegmentPaddingX * 2;
        int index = 0;
        foreach (var token in Tokens()!)
        {
            if (token.Kind == '\0')
            {
                yield return (token.Literal, -1, _literalWidths.GetValueOrDefault(token.Literal, _digitWidth * token.Literal.Length));
                continue;
            }
            int cells = token.Kind == 'y' ? token.Length : 2;
            yield return (PartText(token, index), index, _digitWidth * cells + pad);
            index++;
        }
    }

    private string PartText(Token token, int index)
    {
        // A year being typed shows what is in it so far.
        if (index == _segment && _typedCount > 0 && token.Kind == 'y' && token.Length == 4)
            return _typed.ToString(CultureInfo.InvariantCulture);
        if (_value is not { } d) return new string('-', token.Kind == 'y' ? token.Length : 2);
        int n = token.Kind switch
        {
            'y' => token.Length == 4 ? d.Year : d.Year % 100,
            'M' => d.Month,
            _ => d.Day,
        };
        string format = token.Kind == 'y' ? new string('0', token.Length) : token.Length == 2 ? "00" : "0";
        return n.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>The part nearest a horizontal offset from the field's left edge.</summary>
    private int? SegmentIndexAt(float x)
    {
        int? best = null;
        float bestDistance = float.MaxValue;
        float left = _theme.Padding.Left;
        foreach (var part in TypingParts())
        {
            float right = left + part.Width;
            if (part.Segment >= 0)
            {
                if (x >= left && x < right) return part.Segment;
                float distance = x < left ? left - x : x - right;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = part.Segment;
                }
            }
            left = right;
        }
        return best;
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
        // The typed parts look like a time field's, so a date beside a time reads as one pair.
        _segmentTheme = theme.Get<TimeFieldTheme>();
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
        _chevronWidth = chevronWidth;

        if (Typing)
        {
            // Digit cells, as a time field's: "0" rather than the live date, so it never resizes.
            _digitWidth = ctx.MeasureText("0", style, float.MaxValue, 1).Width;
            foreach (var token in Tokens()!.Where(t => t.Kind == '\0'))
                _literalWidths[token.Literal] = ctx.MeasureText(token.Literal, style, float.MaxValue, 1).Width;
            float parts = TypingParts().Sum(p => p.Width);
            return new Size(
                Math.Max(_theme.MinWidth, _theme.Padding.Left + parts + _theme.ChevronGap + chevronWidth + _theme.Padding.Right),
                _theme.Padding.Top + glyph.Height + _theme.Padding.Bottom);
        }

        float width = Math.Max(_theme.MinWidth,
            _theme.Padding.Left + Math.Max(Math.Max(sample, current), placeholder)
            + _theme.ChevronGap + chevronWidth + _theme.Padding.Right);
        return new Size(width, _theme.Padding.Top + glyph.Height + _theme.Padding.Bottom);
    }

    // The date (or the placeholder), in the theme's font, below the theme's top padding from
    // the top of the border box — where Render draws it.
    internal override float? MeasureBaseline(Size borderBox, IMeasureContext ctx) =>
        _theme.Padding.Top + ctx.MeasureFirstBaseline(DisplayText,
            ResolvedInherited with { FontSize = _theme.FontSize }, float.PositiveInfinity, 1, TextOverflow.Ellipsis);

    /// <summary>Unbounded: laid out shorter than it measures, it draws its text past the box.</summary>
    public override Rect? PaintBounds(IRenderContext ctx) => null;

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
        _chevronWidth = chevronWidth;

        float textLeft = box.X + _theme.Padding.Left;
        float textRight = box.Right - _theme.Padding.Right - chevronWidth - _theme.ChevronGap;
        float top = box.Y + _theme.Padding.Top;
        float height = box.Height - _theme.Padding.Top - _theme.Padding.Bottom;
        if (Typing)
        {
            // Part by part, the one the keyboard is on lit as a time field lights its segment.
            float x = textLeft;
            var partStyle = style with { TextAlign = TextAlign.Center };
            foreach (var part in TypingParts())
            {
                var rect = new Rect(x, top, part.Width, height);
                bool active = _focused && !_open && part.Segment == _segment;
                if (active)
                    ctx.DrawRoundedRect(rect, new CornerRadius(_segmentTheme.SegmentCornerRadius),
                        _segmentTheme.ActiveSegmentBackground, Color.Transparent, default);
                var color = active ? _segmentTheme.ActiveSegmentTextColor
                    : part.Segment < 0 ? _segmentTheme.SeparatorColor
                    : _value is null ? _theme.PlaceholderTextColor
                    : _theme.TextColor;
                ctx.DrawText(part.Text, rect, partStyle with { TextColor = color }, TextOverflow.Clip, 1);
                x += part.Width;
            }
        }
        else
        {
            ctx.DrawText(DisplayText,
                new Rect(textLeft, top, Math.Max(0, textRight - textLeft), height),
                style, TextOverflow.Ellipsis, 1);
        }

        ChevronPainter.Draw(ctx, _theme.ChevronShape,
            new Rect(box.Right - _theme.Padding.Right - chevronWidth, box.Y, chevronWidth, box.Height),
            _theme.ChevronColor, _theme.ChevronStrokeWidth, chevronStyle, _theme.FontSize);
    }

    internal override void PatchFrom(Element source)
    {
        string shown = DisplayText;
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
        AllowTyping = p.AllowTyping;
        OnValueChanged = p.OnValueChanged;
        Value = p._value;
        // A new format, culture or placeholder prints the same date differently: what a reader
        // reads changes with it (the value setter tells only a new date).
        if (DisplayText != shown) NotifyValueChanged();
    }
}
