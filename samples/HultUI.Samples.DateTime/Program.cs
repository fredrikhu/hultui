using System.Globalization;
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

// Date and time pickers: the field that opens a calendar, the calendar on its own, and
// the segmented time field in each of its forms.

using var app = new Application("HultUI - Date & time", 700, 780);

var heading = Color.FromHex("#1E293B");
var muted = Color.FromHex("#64748B");

var today = DateOnly.FromDateTime(DateTime.Now);
var chosen = Text.New("Nothing chosen yet.").FontSize(12f).TextColor(muted);

var root = ScrollView.New().Background(Color.FromHex("#EEF2F7")).Children(
    Flex.Column(gap: 24).Padding(32).Children(

    Text.New("Date & time").FontSize(28f).FontWeight(FontWeight.Bold).TextColor(heading).FlexShrink(0),

    Section("DATE FIELD",
        Flex.Column(gap: 10).Children(
            Flex.Row(gap: 12).AlignItems(AlignItems.Center).Children(
                DatePicker.New(today).Changed(d => chosen.Content = $"Date: {d:yyyy-MM-dd}"),
                chosen),
            Note("Click, Enter, Space or Down opens the calendar. Arrows move a day, "
                 + "Page steps a month, Shift+Page a year. Enter picks; Escape closes; Delete clears."))),

    Section("A RANGE",
        Flex.Column(gap: 10).Children(
            DateRange(),
            Note("Days outside the range are drawn dimmed, and neither the pointer nor the "
                 + "keyboard can land on one."))),

    Section("CALENDAR IN PLACE",
        Flex.Column(gap: 10).Children(
            MonthCalendar.New(today).Selected(d => Console.WriteLine($"calendar: {d:O}")),
            Note("The same control the field opens — six rows always, so paging through the "
                 + "months never moves the day under your pointer."))),

    Section("TIME FIELD",
        Flex.Column(gap: 10).Children(
            Flex.Row(gap: 12).AlignItems(AlignItems.Center).Children(
                TimePicker.New(new TimeOnly(14, 30)),
                WithSeconds(),
                TwelveHour()),
            Note("Left and Right pick a segment, Up and Down step it, digits type into it, "
                 + "and the wheel over a segment nudges it. A and P set AM/PM."))),

    Section("QUARTER HOURS",
        Flex.Column(gap: 10).Children(
            QuarterHours(),
            Note("MinuteStep = 15: the arrows move in quarters, and every minute is still "
                 + "typeable.")))
));

app.SetRoot(root);
app.Run();
return;

Element DateRange()
{
    var picker = DatePicker.New(today);
    picker.MinDate = today.AddDays(-3);
    picker.MaxDate = today.AddDays(10);
    return picker;
}

Element WithSeconds()
{
    var picker = TimePicker.New(new TimeOnly(14, 30, 15));
    picker.ShowSeconds = true;
    return picker;
}

Element TwelveHour()
{
    var picker = TimePicker.New(new TimeOnly(14, 30));
    picker.Use12Hour = true;
    picker.Culture = CultureInfo.InvariantCulture;
    return picker;
}

Element QuarterHours()
{
    var picker = TimePicker.New(new TimeOnly(9, 0));
    picker.MinuteStep = 15;
    return picker;
}

Element Note(string text) => Text.New(text).FontSize(12f).TextColor(muted);

Element Section(string title, Element body) =>
    Flex.Column(gap: 8).FlexShrink(0).Children(
        Text.New(title).FontSize(12f).FontWeight(FontWeight.SemiBold)
            .TextColor(Color.FromHex("#4F46E5")).LetterSpacing(1.5f),
        body);
