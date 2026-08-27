using System.Globalization;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Accessibility;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The date field and the calendar it opens.
///
/// <para>Dates are pinned rather than taken from the clock — <c>Today</c> is a property
/// for exactly this reason — so nothing here starts failing on the first of a month.
/// The culture is pinned too: weekday order and month names differ by country, and a
/// test that asserted the developer's own locale would pass on one CI leg and fail on
/// the next.</para>
/// </summary>
public class DatePickerTests
{
    private static readonly DateOnly Today = new(2026, 9, 15);       // a Tuesday
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static DatePickerElement Mount(HeadlessSession session, DateOnly? value = null)
    {
        var picker = DatePicker.New(value);
        picker.Today = Today;
        picker.Culture = Invariant;
        picker.FirstDayOfWeek = DayOfWeek.Monday;
        picker.Format = "yyyy-MM-dd";
        session.Window.SetRoot(Box.New().Padding(new Thickness(40)).Children(picker));
        session.PumpFrame();
        return picker;
    }

    // ── The field ────────────────────────────────────────────────────────

    [Fact]
    public void AnEmptyFieldShowsItsPlaceholder()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session);

        Assert.Null(picker.Value);
        Assert.Equal("Select a date…", picker.DisplayText);
    }

    [Fact]
    public void AValueIsPrintedInTheGivenFormat()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new DateOnly(2026, 3, 7));

        Assert.Equal("2026-03-07", picker.DisplayText);
    }

    [Fact]
    public void TheFieldKeepsItsWidthWhateverDateIsInIt()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session);
        float empty = picker.Layout.Width;

        picker.Value = new DateOnly(2026, 12, 26);
        session.PumpFrame();

        // Measured against a sample date, not the live one — otherwise the control moves
        // its neighbours around every time someone picks a date.
        Assert.Equal(empty, picker.Layout.Width, 0.5f);
    }

    // ── Opening ──────────────────────────────────────────────────────────

    [Fact]
    public void ClickingOpensACalendarAndClickingAgainClosesIt()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session);

        session.Window.Click(picker);
        session.PumpFrame();

        Assert.True(picker.IsOpen);
        Assert.IsType<CalendarElement>(Assert.Single(session.Window.OverlayContents));

        session.Window.Click(picker);
        session.PumpFrame();

        Assert.False(picker.IsOpen);
        Assert.Empty(session.Window.OverlayContents);
    }

    [Fact]
    public void AnEmptyFieldOpensOnTodayRatherThanOnYearOne()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session);
        picker.Focus();

        session.Window.PressKey(KeyCode.Down);
        session.PumpFrame();

        Assert.Equal(new DateOnly(2026, 9, 1), picker.Calendar!.DisplayMonth);
        Assert.Equal(Today, picker.Calendar.FocusedDate);
    }

    [Fact]
    public void AFieldWithAValueOpensOnItsMonthWithItSelected()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new DateOnly(2025, 2, 20));
        picker.Focus();

        session.Window.PressKey(KeyCode.Enter);
        session.PumpFrame();

        Assert.Equal(new DateOnly(2025, 2, 1), picker.Calendar!.DisplayMonth);
        Assert.Equal(new DateOnly(2025, 2, 20), picker.Calendar.SelectedDate);
    }

    // ── Choosing ─────────────────────────────────────────────────────────

    [Fact]
    public void ArrowKeysMoveTheFocusCellAndEnterCommitsIt()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, Today);
        DateOnly? fired = null;
        int fireCount = 0;
        picker.Changed(v => { fired = v; fireCount++; });
        picker.Focus();

        session.Window.PressKey(KeyCode.Enter);       // open
        session.Window.PressKey(KeyCode.Right);       // 16th
        session.Window.PressKey(KeyCode.Down);        // 23rd
        session.Window.PressKey(KeyCode.Enter);       // commit
        session.PumpFrame();

        Assert.Equal(new DateOnly(2026, 9, 23), picker.Value);
        Assert.False(picker.IsOpen);
        Assert.Equal(new DateOnly(2026, 9, 23), fired);
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void PageKeysStepMonthsAndShiftStepsYears()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, Today);
        picker.Focus();
        session.Window.PressKey(KeyCode.Enter);

        session.Window.PressKey(KeyCode.PageDown);
        Assert.Equal(new DateOnly(2026, 10, 15), picker.Calendar!.FocusedDate);

        session.Window.PressKey(KeyCode.PageUp, KeyModifiers.Shift);
        Assert.Equal(new DateOnly(2025, 10, 15), picker.Calendar.FocusedDate);
    }

    [Fact]
    public void EscapeClosesWithoutChangingTheValue()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, Today);
        bool fired = false;
        picker.Changed(_ => fired = true);
        picker.Focus();

        session.Window.PressKey(KeyCode.Enter);
        session.Window.PressKey(KeyCode.Right);
        session.Window.PressKey(KeyCode.Escape);
        session.PumpFrame();

        Assert.False(picker.IsOpen);
        Assert.Equal(Today, picker.Value);
        Assert.False(fired);
    }

    [Fact]
    public void DeleteClearsTheFieldAndSaysSo()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, Today);
        DateOnly? fired = Today;
        picker.Changed(v => fired = v);
        picker.Focus();

        session.Window.PressKey(KeyCode.Delete);
        session.PumpFrame();

        Assert.Null(picker.Value);
        Assert.Null(fired);
    }

    [Fact]
    public void ARequiredFieldRefusesToBeCleared()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, Today);
        picker.AllowClear = false;
        picker.Focus();

        session.Window.PressKey(KeyCode.Delete);
        session.PumpFrame();

        Assert.Equal(Today, picker.Value);
    }

    // ── Range ────────────────────────────────────────────────────────────

    [Fact]
    public void TheKeyboardCannotWalkPastTheAllowedRange()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, Today);
        picker.MinDate = new DateOnly(2026, 9, 14);
        picker.MaxDate = new DateOnly(2026, 9, 16);
        picker.Focus();
        session.Window.PressKey(KeyCode.Enter);

        // Up is a whole week back, which is well past the minimum.
        session.Window.PressKey(KeyCode.Up);
        Assert.Equal(new DateOnly(2026, 9, 14), picker.Calendar!.FocusedDate);

        session.Window.PressKey(KeyCode.Down);
        session.Window.PressKey(KeyCode.Down);
        Assert.Equal(new DateOnly(2026, 9, 16), picker.Calendar.FocusedDate);
    }

    [Fact]
    public void ADayOutsideTheRangeCannotBeSelected()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, Today);
        picker.MinDate = new DateOnly(2026, 9, 15);
        picker.Focus();
        session.Window.PressKey(KeyCode.Enter);

        var calendar = picker.Calendar!;
        Assert.False(calendar.IsSelectable(new DateOnly(2026, 9, 14)));
        calendar.SelectDate(new DateOnly(2026, 9, 14));

        Assert.Equal(Today, calendar.SelectedDate);
        Assert.Equal(Today, picker.Value);
    }

    // ── The grid ─────────────────────────────────────────────────────────

    /// <summary>The grid always starts on the configured first day of the week, and
    /// always covers six rows — which is what stops the popup resizing month to month
    /// and moving the day under the pointer.</summary>
    [Theory]
    [InlineData(DayOfWeek.Monday, 2026, 9, "2026-08-31")]
    [InlineData(DayOfWeek.Sunday, 2026, 9, "2026-08-30")]
    // February 2026 starts on a Sunday: with a Monday week start the whole of the
    // previous week is on screen ahead of it.
    [InlineData(DayOfWeek.Monday, 2026, 2, "2026-01-26")]
    public void TheGridStartsOnTheWeekContainingTheFirst(
        DayOfWeek firstDay, int year, int month, string expected)
    {
        var calendar = new CalendarElement
        {
            Culture = Invariant,
            FirstDayOfWeek = firstDay,
            DisplayMonth = new DateOnly(year, month, 1),
        };

        Assert.Equal(DateOnly.Parse(expected, Invariant), calendar.DateAt(0, 0));
        // Six rows of seven, last cell included.
        Assert.Equal(calendar.DateAt(0, 0).AddDays(41), calendar.DateAt(5, 6));
    }

    /// <summary>Stepping months keeps the day number where the month is long enough for
    /// it. From 31 March, a month back is the end of February, not the 3rd of March.</summary>
    [Fact]
    public void SteppingBackFromALongMonthLandsInsideTheShortOne()
    {
        var calendar = new CalendarElement
        {
            Culture = Invariant,
            SelectedDate = new DateOnly(2026, 3, 31),
        };

        calendar.ShiftMonths(-1);

        Assert.Equal(new DateOnly(2026, 2, 1), calendar.DisplayMonth);
        Assert.Equal(new DateOnly(2026, 2, 28), calendar.FocusedDate);
    }

    [Fact]
    public void ClickingADayInTheCalendarCommitsItAndClosesThePopup()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, Today);
        DateOnly? fired = null;
        picker.Changed(v => fired = v);

        session.Window.Click(picker);
        session.PumpFrame();
        var calendar = (CalendarElement)Assert.Single(session.Window.OverlayContents);

        // Row 2, column 0 of a Monday-start September 2026 is the 14th.
        var target = calendar.DateAt(2, 0);
        Assert.Equal(new DateOnly(2026, 9, 14), target);
        calendar.SelectDate(target);
        session.PumpFrame();

        Assert.Equal(target, picker.Value);
        Assert.Equal(target, fired);
        Assert.False(picker.IsOpen);
    }

    // ── Accessibility ────────────────────────────────────────────────────

    [Fact]
    public void TheFieldReportsItsValueAndWhetherItIsOpen()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, Today);
        picker.Focus();

        var node = session.Window.FindByRole(AccessibilityRole.ComboBox);
        Assert.NotNull(node);
        Assert.Equal("2026-09-15", node!.Value);

        session.Window.PressKey(KeyCode.Enter);
        session.PumpFrame();
        Assert.True(picker.AccessibleExpanded);
    }
}
