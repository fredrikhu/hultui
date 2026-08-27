using System.Globalization;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The segmented time field: which segment the keyboard is on, what the arrows do to it,
/// and what typing digits into it means.
///
/// <para>The typing rules are where a time field is won or lost, so they get most of the
/// cases: a digit extends what is already in the segment, a digit that could not start a
/// valid pair moves on by itself, and one that would overshoot starts a fresh number
/// instead of being swallowed.</para>
///
/// <para>Stepping is pinned to <i>not</i> carrying between segments. A minute spinner that
/// rolled 23:59 into midnight would be editing the hour nobody pressed a key on.</para>
/// </summary>
public class TimePickerTests
{
    private static TimePickerElement Mount(HeadlessSession session, TimeOnly? value = null,
        bool seconds = false, bool twelveHour = false)
    {
        var picker = TimePicker.New(value);
        picker.Culture = CultureInfo.InvariantCulture;
        picker.ShowSeconds = seconds;
        picker.Use12Hour = twelveHour;
        session.Window.SetRoot(Box.New().Padding(new Thickness(40)).Children(picker));
        session.PumpFrame();
        picker.Focus();
        return picker;
    }

    // ── Display ──────────────────────────────────────────────────────────

    [Fact]
    public void AnEmptyFieldShowsDashes()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session);

        Assert.Null(picker.Value);
        Assert.Equal("--:--", picker.DisplayText);
    }

    [Theory]
    [InlineData(14, 30, false, false, "14:30")]
    [InlineData(9, 5, false, false, "09:05")]
    [InlineData(14, 30, true, false, "14:30:00")]
    [InlineData(14, 30, false, true, "02:30 PM")]
    [InlineData(0, 30, false, true, "12:30 AM")]
    [InlineData(12, 0, false, true, "12:00 PM")]
    public void TheFieldPrintsTheTimeInTheConfiguredForm(
        int hour, int minute, bool seconds, bool twelveHour, string expected)
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new TimeOnly(hour, minute), seconds, twelveHour);

        Assert.Equal(expected, picker.DisplayText);
    }

    // ── Stepping ─────────────────────────────────────────────────────────

    [Fact]
    public void UpAndDownStepTheSegmentTheKeyboardIsOn()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new TimeOnly(14, 30));

        session.Window.PressKey(KeyCode.Up);
        Assert.Equal(new TimeOnly(15, 30), picker.Value);

        session.Window.PressKey(KeyCode.Right);
        session.Window.PressKey(KeyCode.Down);
        Assert.Equal(new TimeOnly(15, 29), picker.Value);
    }

    /// <summary>Each segment wraps inside itself. A time of day has no days to carry
    /// into, and a minute spinner has no business changing the hour.</summary>
    [Theory]
    [InlineData(23, 30, 0, 1, 0, 30)]     // hour up from 23 → 00
    [InlineData(0, 30, 0, -1, 23, 30)]    // hour down from 00 → 23
    [InlineData(14, 59, 1, 1, 14, 0)]     // minute up from 59 → :00, hour unmoved
    [InlineData(14, 0, 1, -1, 14, 59)]    // minute down from 00 → :59
    public void SteppingWrapsWithinItsOwnSegment(
        int hour, int minute, int segment, int direction, int expectedHour, int expectedMinute)
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new TimeOnly(hour, minute));

        for (int i = 0; i < segment; i++) session.Window.PressKey(KeyCode.Right);
        session.Window.PressKey(direction > 0 ? KeyCode.Up : KeyCode.Down);

        Assert.Equal(new TimeOnly(expectedHour, expectedMinute), picker.Value);
    }

    [Fact]
    public void TheMinuteStepIsHonoured()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new TimeOnly(9, 0));
        picker.MinuteStep = 15;

        picker.FocusSegment(TimePickerElement.Segment.Minute);
        session.Window.PressKey(KeyCode.Up);
        session.Window.PressKey(KeyCode.Up);

        Assert.Equal(new TimeOnly(9, 30), picker.Value);
    }

    [Fact]
    public void SteppingAnEmptyFieldStartsFromMidnight()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session);

        session.Window.PressKey(KeyCode.Up);

        Assert.Equal(new TimeOnly(1, 0), picker.Value);
    }

    [Fact]
    public void TheWheelOverTheFieldStepsItToo()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new TimeOnly(14, 30));
        var box = picker.Layout.AbsoluteBorderBox;

        session.Window.Wheel(box.X + 6f, box.Y + box.Height / 2f, 0f, 1f);
        session.PumpFrame();

        Assert.Equal(new TimeOnly(15, 30), picker.Value);
    }

    // ── Typing ───────────────────────────────────────────────────────────

    [Fact]
    public void TypingAFullTimeWalksThroughTheSegments()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session);

        Type(session, "1", "4", "3", "0");

        Assert.Equal(new TimeOnly(14, 30), picker.Value);
    }

    /// <summary>A digit that cannot start a two-digit hour is the whole hour, and the
    /// field moves on by itself — 5 can only ever mean 05.</summary>
    [Fact]
    public void ADigitThatCannotBeExtendedAdvancesOnItsOwn()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session);

        Type(session, "5");

        Assert.Equal(new TimeOnly(5, 0), picker.Value);
        Assert.Equal(TimePickerElement.Segment.Minute, picker.ActiveSegment);
    }

    /// <summary>A digit that could still be extended waits: 1 might yet be 13 or 19.</summary>
    [Fact]
    public void ADigitThatCouldBeExtendedStaysPut()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session);

        Type(session, "1");

        Assert.Equal(new TimeOnly(1, 0), picker.Value);
        Assert.Equal(TimePickerElement.Segment.Hour, picker.ActiveSegment);

        Type(session, "9");
        Assert.Equal(new TimeOnly(19, 0), picker.Value);
    }

    /// <summary>A second digit that would overshoot starts a fresh number rather than
    /// being dropped: 9 then 9 in the minutes is 09 and then 09, not a beep.</summary>
    [Fact]
    public void ASecondDigitThatWouldOvershootStartsOver()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new TimeOnly(10, 0));
        picker.FocusSegment(TimePickerElement.Segment.Minute);

        Type(session, "9");
        Assert.Equal(new TimeOnly(10, 9), picker.Value);

        Type(session, "9");
        Assert.Equal(new TimeOnly(10, 9), picker.Value);
    }

    [Fact]
    public void TypingIntoTheSecondsSegmentWorksWhenItIsShown()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new TimeOnly(1, 2, 3), seconds: true);
        picker.FocusSegment(TimePickerElement.Segment.Second);

        Type(session, "4", "5");

        Assert.Equal(new TimeOnly(1, 2, 45), picker.Value);
        Assert.Equal("01:02:45", picker.DisplayText);
    }

    /// <summary>Seconds are dropped from the value when the segment is not shown, so a
    /// field that never offered them cannot silently carry one.</summary>
    [Fact]
    public void SecondsAreDroppedWhenTheSegmentIsHidden()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new TimeOnly(1, 2, 3));

        Assert.Equal(new TimeOnly(1, 2), picker.Value);
    }

    // ── Twelve-hour form ─────────────────────────────────────────────────

    [Fact]
    public void TypingAnHourInTwelveHourFormKeepsTheHalfOfTheDay()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new TimeOnly(14, 30), twelveHour: true);

        Type(session, "0", "3");

        Assert.Equal(new TimeOnly(15, 30), picker.Value);   // 03 PM, not 03 AM
    }

    [Fact]
    public void AAndPSetTheHalfOfTheDay()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new TimeOnly(14, 30), twelveHour: true);

        session.Window.PressKey(KeyCode.A);
        Assert.Equal(new TimeOnly(2, 30), picker.Value);

        session.Window.PressKey(KeyCode.P);
        Assert.Equal(new TimeOnly(14, 30), picker.Value);

        // Pressing the one it already is changes nothing.
        session.Window.PressKey(KeyCode.P);
        Assert.Equal(new TimeOnly(14, 30), picker.Value);
    }

    [Fact]
    public void TheMeridiemSegmentStepsByTwelveHoursWhicheverWayYouPress()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new TimeOnly(14, 30), twelveHour: true);
        picker.FocusSegment(TimePickerElement.Segment.Meridiem);

        session.Window.PressKey(KeyCode.Down);
        Assert.Equal(new TimeOnly(2, 30), picker.Value);
    }

    // ── Segments ─────────────────────────────────────────────────────────

    [Fact]
    public void LeftAndRightStopAtTheEndsSoTabStillWorks()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new TimeOnly(14, 30));

        session.Window.PressKey(KeyCode.Left);
        Assert.Equal(TimePickerElement.Segment.Hour, picker.ActiveSegment);

        session.Window.PressKey(KeyCode.Right);
        session.Window.PressKey(KeyCode.Right);
        Assert.Equal(TimePickerElement.Segment.Minute, picker.ActiveSegment);
    }

    [Fact]
    public void ClickingASegmentPutsTheKeyboardOnIt()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new TimeOnly(14, 30));
        var box = picker.Layout.AbsoluteBorderBox;

        // Well right of centre is the minutes half of an "HH:mm" field.
        session.Window.Click(box.Right - 8f, box.Y + box.Height / 2f);
        session.PumpFrame();

        Assert.Equal(TimePickerElement.Segment.Minute, picker.ActiveSegment);
    }

    // ── Clearing and reporting ───────────────────────────────────────────

    [Fact]
    public void DeleteEmptiesTheFieldAndSaysSo()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new TimeOnly(14, 30));
        TimeOnly? fired = new TimeOnly(14, 30);
        int fireCount = 0;
        picker.Changed(v => { fired = v; fireCount++; });

        session.Window.PressKey(KeyCode.Delete);
        session.PumpFrame();

        Assert.Null(picker.Value);
        Assert.Null(fired);
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void SettingTheValueFromCodeDoesNotRaiseTheChangeHandler()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new TimeOnly(14, 30));
        bool fired = false;
        picker.Changed(_ => fired = true);

        picker.Value = new TimeOnly(8, 0);
        session.PumpFrame();

        Assert.Equal(new TimeOnly(8, 0), picker.Value);
        Assert.False(fired);
    }

    [Fact]
    public void TheFieldReportsItsTimeToAssistiveTech()
    {
        using var session = HeadlessSession.Create();
        var picker = Mount(session, new TimeOnly(14, 30));
        session.PumpFrame();

        var node = session.Window.FindByRole(AccessibilityRole.TextInput);
        Assert.NotNull(node);
        Assert.Equal("14:30", node!.Value);
    }

    private static void Type(HeadlessSession session, params string[] digits)
    {
        foreach (var d in digits)
            session.Window.PressKey((KeyCode)((int)KeyCode.Digit0 + int.Parse(d, CultureInfo.InvariantCulture)));
    }
}
