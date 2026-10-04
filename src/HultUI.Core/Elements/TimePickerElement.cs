using System.Globalization;
using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// A time of day, as a segmented field: <c>14:30</c>, or <c>02:30 PM</c> in twelve-hour
/// form, with seconds when asked for. Each part is its own segment — click one, or step
/// between them with Left and Right; Up and Down change the one you are on, digits type
/// into it, and the wheel over it nudges it.
///
/// <para><b>Why not a dropdown of times.</b> A list is fine for "9:00, 9:30, 10:00" and
/// useless for 14:37: it either has 1,440 rows or it cannot express the value. A field
/// with segments is what every operating system settles on, it needs no popup at all, and
/// it is reachable by keyboard alone.</para>
///
/// <para><b>Typing.</b> Digits fill the active segment left to right and move on when no
/// further digit could fit — <c>5</c> in the hour is 05 and moves on, because 5x is not an
/// hour, while <c>1</c> waits to see whether 1, 13 or 19 was meant. That rule is what
/// makes typing a time feel like typing rather than like operating a spinner.</para>
/// </summary>
public class TimePickerElement : Element
{
    /// <summary>Which part of the time a segment edits.</summary>
    public enum Segment { Hour, Minute, Second, Meridiem }

    private const string Empty = "--";

    private TimeOnly? _value;
    private bool _focused;
    private int _segment;
    // Digits typed into the current segment since it was entered. Cleared by moving
    // segments, by any non-typing edit, and once the segment is full.
    private int _typed;
    private int _typedCount;

    private DropdownTheme _boxTheme = new();
    private TimeFieldTheme _theme = new();
    private Color _focusRingColor = new(59, 130, 246, 180);

    public TimePickerElement()
    {
        IsFocusable = true;
        // A spinner is a text field with structure, and TextInput is the role in this
        // model that carries a readable value. The segments are not separately exposed:
        // they are one value being edited, and the value is what a reader needs.
        Role = AccessibilityRole.TextInput;
        Inherited.Cursor = CursorType.Pointer;

        OnOwn<PointerDownEvent>(OnPointerDown);
        OnOwn<FocusEvent>(_ => { _focused = true; MarkPaintDirty(); });
        OnOwn<BlurEvent>(_ => { _focused = false; ResetTyping(); MarkPaintDirty(); });
        OnOwn<KeyDownEvent>(OnKeyDown);
        OnOwn<ScrollEvent>(OnScroll);
        UpdateAccessibleName();
    }

    public static TimePickerElement New(TimeOnly? value = null,
        [OriginFile] string? file = null, [OriginLine] int line = 0)
    {
        var picker = new TimePickerElement();
        picker.Value = value;
        return (TimePickerElement)picker.WithOrigin(file, line);
    }

    // ── Model ────────────────────────────────────────────────────────────

    /// <summary>The time, or null for an empty field. Setting it from code does not raise
    /// <see cref="OnValueChanged"/>.</summary>
    public TimeOnly? Value
    {
        get => _value;
        set
        {
            var normalized = value is { } t ? Truncate(t) : (TimeOnly?)null;
            if (_value == normalized) return;
            _value = normalized;
            ResetTyping();
            UpdateAccessibleName();
            MarkPaintDirty();
        }
    }

    /// <summary>Whether a seconds segment is shown and edited.</summary>
    public bool ShowSeconds
    {
        get => _showSeconds;
        set
        {
            if (_showSeconds == value) return;
            _showSeconds = value;
            _segment = Math.Min(_segment, SegmentCount - 1);
            Value = _value;                 // re-truncate: seconds may have just been dropped
            MarkLayoutDirty();
        }
    }
    private bool _showSeconds;

    /// <summary>Twelve-hour form with an AM/PM segment. The stored
    /// <see cref="Value"/> is always a plain time of day either way.</summary>
    public bool Use12Hour
    {
        get => _use12Hour;
        set { if (_use12Hour == value) return; _use12Hour = value; _segment = Math.Min(_segment, SegmentCount - 1); MarkLayoutDirty(); }
    }
    private bool _use12Hour;

    /// <summary>How far Up and Down move the minute segment. Fifteen makes a field that
    /// only ever wants quarter hours reachable in four presses; the default of one leaves
    /// every minute typeable.</summary>
    public int MinuteStep { get; set; } = 1;

    /// <summary>Names AM and PM.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.CurrentCulture;

    /// <summary>Whether Delete and Backspace empty the field.</summary>
    public bool AllowClear { get; set; } = true;

    /// <summary>Runs when the user changes the time — arrows, typing, wheel or clearing.
    /// Never raised by assigning <see cref="Value"/>.</summary>
    public Action<TimeOnly?>? OnValueChanged { get; set; }

    /// <summary>Fluent registration of <see cref="OnValueChanged"/>.</summary>
    public TimePickerElement Changed(Action<TimeOnly?> handler)
    {
        OnValueChanged = handler;
        return this;
    }

    /// <summary>Which segment the keyboard is on.</summary>
    public Segment ActiveSegment => SegmentAt(_segment);

    /// <summary>What the field reads, with <c>--</c> for the parts that are unset.</summary>
    public string DisplayText => string.Concat(Parts().Select(p => p.Text));

    private int SegmentCount => 2 + (_showSeconds ? 1 : 0) + (_use12Hour ? 1 : 0);

    private Segment SegmentAt(int index)
    {
        if (index == 0) return Segment.Hour;
        if (index == 1) return Segment.Minute;
        if (_showSeconds && index == 2) return Segment.Second;
        return Segment.Meridiem;
    }

    private TimeOnly Truncate(TimeOnly t) =>
        _showSeconds ? new TimeOnly(t.Hour, t.Minute, t.Second) : new TimeOnly(t.Hour, t.Minute);

    private void UpdateAccessibleName()
    {
        AccessibleName = _value is null ? "No time set" : DisplayText;
        NotifyAccessibilityPropertyChanged(AccessibilityProperties.Value);
    }

    private void ResetTyping()
    {
        _typed = 0;
        _typedCount = 0;
    }

    // ── Editing ──────────────────────────────────────────────────────────

    /// <summary>Applies an edit and raises <see cref="OnValueChanged"/> if it changed
    /// anything. An edit on an empty field starts from midnight — every other starting
    /// point is a guess, and midnight is the one the user can see is a starting point.</summary>
    private void Edit(Func<TimeOnly, TimeOnly> change)
    {
        var before = _value;
        var next = Truncate(change(_value ?? new TimeOnly(0, 0)));
        _value = next;
        UpdateAccessibleName();
        MarkPaintDirty();
        if (before != next) OnValueChanged?.Invoke(next);
    }

    /// <summary>Steps the active segment, wrapping at its own end: 23:59 up a minute is
    /// 23:00, not tomorrow. A time of day has no days to carry into.</summary>
    public void Step(int direction)
    {
        ResetTyping();
        var segment = ActiveSegment;
        Edit(t => segment switch
        {
            Segment.Hour => t.AddHours(direction),
            Segment.Minute => WrapMinutes(t, direction * Math.Max(1, MinuteStep)),
            Segment.Second => WrapSeconds(t, direction),
            // AM/PM is a twelve-hour swing whichever way you press it.
            _ => t.AddHours(12),
        });
    }

    // AddMinutes would roll into the hour; a segment spinner changes its own field only.
    private static TimeOnly WrapMinutes(TimeOnly t, int delta) =>
        new(t.Hour, ((t.Minute + delta) % 60 + 60) % 60, t.Second);

    private static TimeOnly WrapSeconds(TimeOnly t, int delta) =>
        new(t.Hour, t.Minute, ((t.Second + delta) % 60 + 60) % 60);

    /// <summary>Empties the field.</summary>
    public void Clear()
    {
        if (_value is null) return;
        _value = null;
        ResetTyping();
        UpdateAccessibleName();
        MarkPaintDirty();
        OnValueChanged?.Invoke(null);
    }

    /// <summary>Moves the active segment, clamped at both ends rather than wrapping —
    /// Right at the last segment should leave Tab free to move on.</summary>
    public void MoveSegment(int delta)
    {
        int next = Math.Clamp(_segment + delta, 0, SegmentCount - 1);
        if (next == _segment) return;
        _segment = next;
        ResetTyping();
        MarkPaintDirty();
    }

    /// <summary>Puts the keyboard on a segment (ignored when it is not shown).</summary>
    public void FocusSegment(Segment segment)
    {
        int index = segment switch
        {
            Segment.Hour => 0,
            Segment.Minute => 1,
            Segment.Second => _showSeconds ? 2 : -1,
            _ => _use12Hour ? SegmentCount - 1 : -1,
        };
        if (index < 0 || index == _segment) return;
        _segment = index;
        ResetTyping();
        MarkPaintDirty();
    }

    /// <summary>
    /// Feeds one digit into the active segment.
    /// </summary>
    /// <remarks>
    /// The accumulated value is what has been typed so far in this segment, so a second
    /// digit extends the first rather than replacing it. The segment advances as soon as
    /// no further digit could be valid — which is after one digit for 3–9 in the hour, and
    /// after two everywhere else.
    /// </remarks>
    public void TypeDigit(int digit)
    {
        if (digit is < 0 or > 9) return;
        var segment = ActiveSegment;
        if (segment == Segment.Meridiem) return;

        int candidate = _typedCount == 0 ? digit : _typed * 10 + digit;
        int max = segment switch
        {
            Segment.Hour => _use12Hour ? 12 : 23,
            _ => 59,
        };
        int min = segment == Segment.Hour && _use12Hour ? 1 : 0;

        // A second digit that would overshoot starts a fresh number instead of being
        // dropped: typing 9 then 9 into the minutes means 09 and then 09, not 09 and a
        // beep.
        if (candidate > max)
        {
            candidate = digit;
            _typedCount = 0;
        }

        _typed = candidate;
        _typedCount++;
        ApplyTyped(segment, Math.Max(min, candidate));

        // Full, or no digit could follow: move on.
        if (_typedCount >= 2 || candidate * 10 > max)
        {
            ResetTyping();
            MoveSegment(1);
        }
    }

    private void ApplyTyped(Segment segment, int number)
    {
        Edit(t => segment switch
        {
            Segment.Hour => new TimeOnly(_use12Hour ? To24Hour(number, t.Hour >= 12) : number, t.Minute, t.Second),
            Segment.Minute => new TimeOnly(t.Hour, number, t.Second),
            _ => new TimeOnly(t.Hour, t.Minute, number),
        });
    }

    private static int To24Hour(int hour12, bool pm)
    {
        int h = hour12 % 12;
        return pm ? h + 12 : h;
    }

    // ── Input ────────────────────────────────────────────────────────────

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.Button != 0) return;
        Focus();
        if (SegmentIndexAt(evt.LocalPosition.X) is { } index && index != _segment)
        {
            _segment = index;
            ResetTyping();
        }
        MarkPaintDirty();
        evt.StopPropagation = true;
    }

    private void OnScroll(ScrollEvent evt)
    {
        // Not while a page is scrolled past it, and not on a sideways swipe — see
        // NumericUpDownElement.OnScroll.
        if (!evt.IsFor(this) || evt.RemainingDeltaY == 0) return;
        if (SegmentIndexAt(evt.LocalPosition.X) is { } index && index != _segment)
        {
            _segment = index;
            ResetTyping();
        }
        // Wheel up is away from the user, which reads as "later".
        Step(evt.RemainingDeltaY > 0 ? 1 : -1);
        evt.Consume(this, movedX: false, movedY: true);
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (!_focused) return;

        if (evt.Digit is { } digit)
        {
            TypeDigit(digit);
            evt.StopPropagation = true;
            return;
        }

        switch (evt.Key)
        {
            case KeyCode.Left: MoveSegment(-1); break;
            case KeyCode.Right: MoveSegment(1); break;
            case KeyCode.Up: Step(1); break;
            case KeyCode.Down: Step(-1); break;
            case KeyCode.Home: Edit(t => new TimeOnly(0, 0)); ResetTyping(); break;
            case KeyCode.End: Edit(_ => _showSeconds ? new TimeOnly(23, 59, 59) : new TimeOnly(23, 59)); ResetTyping(); break;
            // A for AM and P for PM, which is what the segment's own letters suggest and
            // what every native time field accepts.
            case KeyCode.A when _use12Hour: SetMeridiem(pm: false); break;
            case KeyCode.P when _use12Hour: SetMeridiem(pm: true); break;
            case KeyCode.Delete or KeyCode.Backspace when AllowClear: Clear(); break;
            default: return;
        }
        evt.StopPropagation = true;
    }

    private void SetMeridiem(bool pm)
    {
        ResetTyping();
        Edit(t => t.Hour >= 12 == pm ? t : t.AddHours(12));
    }

    /// <summary>
    /// Which segment a horizontal offset into the field lands in.
    ///
    /// <para>Nearest rather than exact: the separators and the field's own padding belong
    /// to nobody, and a click in one that did nothing would read as the control being
    /// broken. Every point in the field picks the segment closest to it, which is also
    /// what makes the two ends of the field big targets.</para>
    /// </summary>
    private int? SegmentIndexAt(float localX)
    {
        int? best = null;
        float bestDistance = float.MaxValue;
        // The same lead the render uses, so the segment under the pointer is the one that
        // was drawn there. Nearest-segment would paper over a mismatch, which is exactly why
        // it has to be derived from one place rather than assumed to be zero.
        float x = PartsLeadX(Layout.AbsoluteContentBox.Width);
        foreach (var part in Parts())
        {
            float right = x + part.Width;
            if (part.SegmentIndex >= 0)
            {
                if (localX >= x && localX < right) return part.SegmentIndex;
                float distance = localX < x ? x - localX : localX - right;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = part.SegmentIndex;
                }
            }
            x = right;
        }
        return best;
    }

    // ── Layout of the parts ──────────────────────────────────────────────

    /// <summary>
    /// How far in from the content edge the first segment starts: the parts, centred in
    /// whatever width the field ended up with.
    ///
    /// <para>Measure reserves <see cref="TimeFieldTheme.PaddingX"/> either side, and the
    /// render used to start flush at the content edge — which spent all of it on the right
    /// and left the digits visibly off-centre in their own box. Centring also keeps them put
    /// when a layout stretches the field past its natural width.</para>
    /// </summary>
    private float PartsLeadX(float contentWidth) =>
        Math.Max(0f, (contentWidth - Parts().Sum(p => p.Width)) / 2f);

    /// <summary>One drawn run of the field: a segment or a separator, its text, and how
    /// much room it takes. Widths are in character cells rather than measured text, so the
    /// digits do not shuffle sideways as they change.</summary>
    private readonly record struct Part(string Text, int SegmentIndex, float Width);

    // Every part's width comes off these, set at measure time from the font.
    private float _digitWidth = 8f;
    private float _separatorWidth = 4f;
    // AM/PM is words, not digits, and the two are not always the same length — measured
    // as the wider of the pair so the field does not resize at noon.
    private float _meridiemWidth = 20f;

    private IEnumerable<Part> Parts()
    {
        float pad = _theme.SegmentPaddingX * 2;
        float twoDigits = _digitWidth * 2 + pad;

        int index = 0;
        yield return new Part(HourText(), index++, twoDigits);
        yield return new Part(":", -1, _separatorWidth);
        yield return new Part(TwoDigits(_value?.Minute), index++, twoDigits);
        if (_showSeconds)
        {
            yield return new Part(":", -1, _separatorWidth);
            yield return new Part(TwoDigits(_value?.Second), index++, twoDigits);
        }
        if (_use12Hour)
        {
            yield return new Part(" ", -1, _separatorWidth);
            yield return new Part(MeridiemText(), index, _meridiemWidth + pad);
        }
    }

    private string HourText()
    {
        if (_value is not { } t) return Empty;
        int hour = _use12Hour ? (t.Hour % 12 == 0 ? 12 : t.Hour % 12) : t.Hour;
        return hour.ToString("00", CultureInfo.InvariantCulture);
    }

    private string MeridiemText() =>
        _value is { } t ? Designator(am: t.Hour < 12) : Empty;

    /// <summary>The culture's own AM/PM word, falling back to the English pair for the
    /// cultures (and the invariant one, on some runtimes) that define neither.</summary>
    private string Designator(bool am)
    {
        var text = am ? Culture.DateTimeFormat.AMDesignator : Culture.DateTimeFormat.PMDesignator;
        return string.IsNullOrEmpty(text) ? (am ? "AM" : "PM") : text;
    }

    private static string TwoDigits(int? n) =>
        n is { } v ? v.ToString("00", CultureInfo.InvariantCulture) : Empty;

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
        _boxTheme = theme.Dropdown.Resolve(_style);
        _theme = theme.Get<TimeFieldTheme>();
        _focusRingColor = theme.FocusRing;
        MarkLayoutDirty();
    }

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
    {
        var style = ResolvedInherited with { FontSize = _boxTheme.FontSize };
        // "0" rather than the live digits: every digit has to occupy the same cell or the
        // field twitches as the time changes.
        var digit = ctx.MeasureText("0", style, float.MaxValue, 1);
        _digitWidth = digit.Width;
        _separatorWidth = ctx.MeasureText(":", style, float.MaxValue, 1).Width;
        _meridiemWidth = Math.Max(
            Math.Max(ctx.MeasureText(Designator(am: true), style, float.MaxValue, 1).Width,
                     ctx.MeasureText(Designator(am: false), style, float.MaxValue, 1).Width),
            ctx.MeasureText(Empty, style, float.MaxValue, 1).Width);

        // Exactly its parts, with no minimum: a time field has a fixed number of digits in
        // it, so padding it out to some other control's minimum would only add space that
        // belongs to no segment.
        float content = Parts().Sum(p => p.Width);
        return new Size(
            _theme.PaddingX * 2 + content,
            _boxTheme.Padding.Top + digit.Height + _boxTheme.Padding.Bottom);
    }

    // The digits, in the box theme's font, below its top padding from the top of the border
    // box — where Render draws every segment.
    internal override float? MeasureBaseline(Size borderBox, IMeasureContext ctx) =>
        _boxTheme.Padding.Top + ctx.MeasureFirstBaseline("0",
            ResolvedInherited with { FontSize = _boxTheme.FontSize }, float.PositiveInfinity, 1);

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteBorderBox;
        ctx.DrawRoundedRect(box, new CornerRadius(_boxTheme.CornerRadius), _boxTheme.Background,
            _focused ? _focusRingColor : _boxTheme.BorderColor,
            new Thickness(_focused ? _boxTheme.FocusBorderWidth : _boxTheme.BorderWidth));

        var content = Layout.AbsoluteContentBox;
        var baseStyle = ResolvedInherited with
        {
            FontSize = _boxTheme.FontSize,
            TextAlign = TextAlign.Center,
        };

        float x = content.X + PartsLeadX(content.Width);
        float top = box.Y + _boxTheme.Padding.Top;
        float height = box.Height - _boxTheme.Padding.Top - _boxTheme.Padding.Bottom;

        foreach (var part in Parts())
        {
            var rect = new Rect(x, top, part.Width, height);
            bool active = _focused && part.SegmentIndex == _segment;
            bool separator = part.SegmentIndex < 0;

            if (active)
                ctx.DrawRoundedRect(rect, new CornerRadius(_theme.SegmentCornerRadius),
                    _theme.ActiveSegmentBackground, Color.Transparent, default);

            var color = active ? _theme.ActiveSegmentTextColor
                : separator || _value is null ? _theme.SeparatorColor
                : _boxTheme.TextColor;
            ctx.DrawText(part.Text, rect, baseStyle with { TextColor = color }, TextOverflow.Clip, 1);
            x += part.Width;
        }
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is not TimePickerElement p) return;
        MinuteStep = p.MinuteStep;
        Style = p.Style;
        Culture = p.Culture;
        AllowClear = p.AllowClear;
        OnValueChanged = p.OnValueChanged;
        ShowSeconds = p._showSeconds;
        Use12Hour = p._use12Hour;
        Value = p._value;
    }
}
