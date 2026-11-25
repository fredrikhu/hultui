using System.Globalization;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="DatePickerElement.AllowTyping"/>: the date typed a part at a time, as a time
/// field's time is, in the order the format puts the parts in, with the calendar still a press on
/// the chevron or a key away. Dates and cultures pinned, as in <see cref="DatePickerTests"/>.
/// </summary>
public class DatePickerTypingTests
{
    private static readonly DateOnly Today = new(2026, 9, 15);

    private static DatePickerElement Mount(HeadlessSession session, DateOnly? value, string format = "yyyy-MM-dd",
        CultureInfo? culture = null, List<DateOnly?>? changes = null)
    {
        var picker = DatePicker.New(value);
        picker.Today = Today;
        picker.Culture = culture ?? CultureInfo.InvariantCulture;
        picker.Format = format;
        picker.AllowTyping = true;
        if (changes is not null) picker.Changed(changes.Add);
        session.Window.SetRoot(Box.New().Padding(new Thickness(40)).Children(picker));
        session.PumpFrame();
        picker.Focus();
        return picker;
    }

    private static void Type(HeadlessSession session, string digits)
    {
        foreach (char d in digits)
            session.Window.PressKey((KeyCode)((int)KeyCode.Digit0 + (d - '0')));
    }

    [Fact]
    public void AFullDate_IsTypedPartByPart_InTheFormatsOrder()
    {
        using var session = HeadlessSession.Create();
        var changes = new List<DateOnly?>();
        var picker = Mount(session, new DateOnly(2026, 11, 24), changes: changes);
        picker.FocusSegment(DatePickerElement.Segment.Year);

        // The year waits for its four digits; a 3 in the month cannot be extended, so moves on.
        Type(session, "2025");
        Assert.Equal(DatePickerElement.Segment.Month, picker.ActiveSegment);
        Type(session, "3");
        Assert.Equal(DatePickerElement.Segment.Day, picker.ActiveSegment);
        Type(session, "15");

        Assert.Equal(new DateOnly(2025, 3, 15), picker.Value);
        Assert.Equal(new DateOnly(2025, 3, 15), changes[^1]);
        Assert.False(picker.IsOpen);
    }

    [Fact]
    public void TheCulturesShortDate_SetsTheOrder()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new DateOnly(2026, 11, 24), format: "d", culture: CultureInfo.GetCultureInfo("en-US"));

        // M/d/yyyy: month first.
        picker.FocusSegment(DatePickerElement.Segment.Month);
        Type(session, "12");
        Assert.Equal(DatePickerElement.Segment.Day, picker.ActiveSegment);
        Type(session, "31");
        Assert.Equal(DatePickerElement.Segment.Year, picker.ActiveSegment);
        Type(session, "2024");

        Assert.Equal(new DateOnly(2024, 12, 31), picker.Value);
    }

    [Fact]
    public void AYearLeftWithTwoDigits_IsTheCulturesTwoDigitYear()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new DateOnly(2026, 11, 24));
        picker.FocusSegment(DatePickerElement.Segment.Year);

        Type(session, "25");
        session.Window.PressKey(KeyCode.Right);

        Assert.Equal(new DateOnly(2025, 11, 24), picker.Value);
        Assert.Equal(DatePickerElement.Segment.Month, picker.ActiveSegment);
    }

    [Fact]
    public void UpAndDown_StepThePart_WrappingWithinIt()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new DateOnly(2026, 1, 31));
        picker.FocusSegment(DatePickerElement.Segment.Day);

        session.Window.PressKey(KeyCode.Up);
        Assert.Equal(new DateOnly(2026, 1, 1), picker.Value);

        picker.FocusSegment(DatePickerElement.Segment.Month);
        session.Window.PressKey(KeyCode.Down);
        Assert.Equal(new DateOnly(2026, 12, 1), picker.Value);
    }

    [Fact]
    public void AMonthWithFewerDays_KeepsTheDayInsideIt()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new DateOnly(2026, 1, 31));
        picker.FocusSegment(DatePickerElement.Segment.Month);

        Type(session, "2");

        Assert.Equal(new DateOnly(2026, 2, 28), picker.Value);
    }

    [Fact]
    public void APressOnTheDate_PutsTheKeyboardOnAPart_AndOneOnTheChevron_OpensTheCalendar()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new DateOnly(2026, 11, 24));
        var box = picker.Layout.AbsoluteBorderBox;
        picker.FocusSegment(DatePickerElement.Segment.Day);

        session.Window.Click(box.X + 6, box.Y + box.Height / 2);
        session.PumpFrame();
        Assert.False(picker.IsOpen);
        Assert.Equal(DatePickerElement.Segment.Year, picker.ActiveSegment);

        session.Window.Click(box.Right - 4, box.Y + box.Height / 2);
        session.PumpFrame();
        Assert.True(picker.IsOpen);
    }

    [Fact]
    public void AltDownAndF4_OpenTheCalendar_ButPlainDownStepsThePart()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new DateOnly(2026, 11, 24));
        picker.FocusSegment(DatePickerElement.Segment.Day);

        session.Window.PressKey(KeyCode.Down);
        Assert.False(picker.IsOpen);
        Assert.Equal(new DateOnly(2026, 11, 23), picker.Value);

        session.Window.PressKey(KeyCode.Down, KeyModifiers.Alt);
        Assert.True(picker.IsOpen);
        session.Window.PressKey(KeyCode.Escape);

        session.Window.PressKey(KeyCode.F4);
        Assert.True(picker.IsOpen);
    }

    [Fact]
    public void AFormatWithAMonthByName_IsNotTyped()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new DateOnly(2026, 11, 24), format: "dd MMM yyyy");

        Type(session, "1");

        Assert.Equal(new DateOnly(2026, 11, 24), picker.Value);
    }
}
