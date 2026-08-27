using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

/// <summary>
/// A month grid, chosen from in place.
///
/// <para>Named <c>MonthCalendar</c> rather than <c>Calendar</c> because
/// <see cref="System.Globalization.Calendar"/> already owns that name, and any file that
/// formats a date has that namespace imported — a builder called <c>Calendar</c> would be
/// ambiguous in exactly the files most likely to want one.</para>
/// </summary>
public static class MonthCalendar
{
    public static CalendarElement New(DateOnly? selected = null,
        [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        CalendarElement.New(selected, file, line);
}

/// <summary>A date field that opens a <see cref="MonthCalendar"/> beneath itself.</summary>
public static class DatePicker
{
    public static DatePickerElement New(DateOnly? value = null,
        [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        DatePickerElement.New(value, file, line);
}

/// <summary>A segmented time-of-day field.</summary>
public static class TimePicker
{
    public static TimePickerElement New(TimeOnly? value = null,
        [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        TimePickerElement.New(value, file, line);
}
