using System.Globalization;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A name the app gives a date picker, a time picker or a calendar says what it is for, and
/// stays when the value changes, by code or by the user; the value is the node's value. The
/// controls wrote their value into their accessible name on every change, so "Start date" read
/// as "2026-09-15" and a reader heard nothing of what the date was for. One the app did not
/// name reads as its value, as before.
/// </summary>
public class PickerAccessibleNameTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static T Mount<T>(HeadlessSession session, T element) where T : Element
    {
        session.Window.SetRoot(Box.New().Padding(new Thickness(40)).Children(element));
        session.PumpFrame();
        return element;
    }

    private static DatePickerElement Date(DateOnly? value)
    {
        var picker = DatePicker.New(value);
        picker.Culture = Invariant;
        picker.Format = "yyyy-MM-dd";
        picker.Today = new DateOnly(2026, 9, 15);
        return picker;
    }

    private static TimePickerElement Time(TimeOnly? value)
    {
        var picker = TimePicker.New(value);
        picker.Culture = Invariant;
        return picker;
    }

    private static (string? Name, string? Value) Read(HeadlessSession session, AccessibilityRole role)
    {
        var node = session.Window.FindByRole(role);
        Assert.NotNull(node);
        return (node!.Name, node.Value);
    }

    // ── Date picker ──────────────────────────────────────────────────────

    [Fact]
    public void ADatePickersName_StaysThroughANewDate_AndTheDateIsTheValue()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, Date(new DateOnly(2026, 9, 15)).AccessibleName("Start date"));
        Assert.Equal(("Start date", "2026-09-15"), Read(session, AccessibilityRole.ComboBox));

        picker.Value = new DateOnly(2026, 10, 1);
        session.PumpFrame();
        Assert.Equal(("Start date", "2026-10-01"), Read(session, AccessibilityRole.ComboBox));

        // The user's choice, through the calendar: Enter opens it, Right moves a day, Enter picks.
        picker.Focus();
        session.Window.PressKey(KeyCode.Enter);
        session.Window.PressKey(KeyCode.Right);
        session.Window.PressKey(KeyCode.Enter);
        session.PumpFrame();
        Assert.Equal(new DateOnly(2026, 10, 2), picker.Value);
        Assert.Equal(("Start date", "2026-10-02"), Read(session, AccessibilityRole.ComboBox));
        Assert.Equal("Start date", picker.AccessibleName);
    }

    [Fact]
    public void ADatePickerWithNoName_ReadsAsItsDate_OrItsPlaceholder()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, Date(null));
        Assert.Equal(picker.Placeholder, Read(session, AccessibilityRole.ComboBox).Name);

        picker.Value = new DateOnly(2026, 9, 15);
        session.PumpFrame();
        Assert.Equal(("2026-09-15", "2026-09-15"), Read(session, AccessibilityRole.ComboBox));
        Assert.Null(picker.AccessibleName);
    }

    /// <summary>A format set after the date (as a builder chain does) is the one read: the name was
    /// written when the date was set and kept the format of that moment.</summary>
    [Fact]
    public void ADatePickerWithNoName_ReadsItsDateInTheFormatItShows()
    {
        using var session = HeadlessSession.Create();
        var picker = DatePicker.New(new DateOnly(2026, 9, 15));
        picker.Culture = Invariant;
        picker.Format = "dd MMM yyyy";
        Mount(session, picker);

        Assert.Equal("15 Sep 2026", Read(session, AccessibilityRole.ComboBox).Name);
    }

    // ── Time picker ──────────────────────────────────────────────────────

    [Fact]
    public void ATimePickersName_StaysThroughANewTime_AndTheTimeIsTheValue()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, Time(new TimeOnly(9, 30)).AccessibleName("Start"));
        Assert.Equal(("Start", "09:30"), Read(session, AccessibilityRole.TextInput));

        picker.Focus();
        session.Window.PressKey(KeyCode.Up);   // the hour
        session.PumpFrame();
        Assert.Equal(new TimeOnly(10, 30), picker.Value);
        Assert.Equal(("Start", "10:30"), Read(session, AccessibilityRole.TextInput));

        picker.Clear();
        session.PumpFrame();
        Assert.Equal(("Start", ""), Read(session, AccessibilityRole.TextInput));
        Assert.Equal("Start", picker.AccessibleName);
    }

    [Fact]
    public void ATimePickerWithNoName_ReadsAsItsTime_OrThatThereIsNone()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, Time(null));
        Assert.Equal("No time set", Read(session, AccessibilityRole.TextInput).Name);

        picker.Value = new TimeOnly(9, 30);
        session.PumpFrame();
        Assert.Equal("09:30", Read(session, AccessibilityRole.TextInput).Name);
        Assert.Null(picker.AccessibleName);
    }

    // ── Calendar ─────────────────────────────────────────────────────────

    [Fact]
    public void ACalendarsName_StaysThroughANewDate()
    {
        using var session = HeadlessSession.Create();
        var calendar = MonthCalendar.New(new DateOnly(2026, 9, 15));
        calendar.Culture = Invariant;
        Mount(session, calendar.AccessibleName("Delivery day"));
        Assert.Equal(("Delivery day", "09/15/2026"), Read(session, AccessibilityRole.Group));

        calendar.SelectedDate = new DateOnly(2026, 9, 20);
        session.PumpFrame();
        Assert.Equal(("Delivery day", "09/20/2026"), Read(session, AccessibilityRole.Group));
        Assert.Equal("Delivery day", calendar.AccessibleName);
    }

    [Fact]
    public void ACalendarWithNoName_ReadsAsItsDateWrittenOut()
    {
        using var session = HeadlessSession.Create();
        var calendar = MonthCalendar.New();
        calendar.Culture = Invariant;
        Mount(session, calendar);
        Assert.Equal("No date selected", Read(session, AccessibilityRole.Group).Name);

        calendar.SelectedDate = new DateOnly(2026, 9, 15);
        session.PumpFrame();
        Assert.Equal("Tuesday, 15 September 2026", Read(session, AccessibilityRole.Group).Name);
        Assert.Null(calendar.AccessibleName);
    }
}
