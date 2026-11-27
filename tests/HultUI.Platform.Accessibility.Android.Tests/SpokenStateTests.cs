using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Platform.Accessibility.Android.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// What TalkBack says of a control's state and of what it holds, as Android's own views give it.
/// Each from the Fluent sample read through TalkBack on a Galaxy S25 (plan 20, phase 6): check boxes
/// that toggled in silence, a chosen radio button "Selected, checked", "column header" twice, an
/// InfoBar read twice, a link card's second line unread, and an expander that opened in silence.
/// </summary>
public class SpokenStateTests
{
    private static Harness Page(params Element[] children) =>
        new(Box.New().Width(400).Height(800).Children(children));

    /// <summary>A check box and a radio button say "Checked" or "Not checked" as their state
    /// description, as Android's compound buttons do from 11, which TalkBack says again when it
    /// changes. A chosen radio button is checked, not selected.</summary>
    [Fact]
    public void ACheckBoxAndARadioButton_SayCheckedOrNotChecked_AndAChosenRadioIsNotSelected()
    {
        var wifi = Checkbox.New(true, "Wi-Fi");
        var bluetooth = Checkbox.New(false, "Bluetooth");
        var small = RadioButton.New("size", false, "Small");
        var large = RadioButton.New("size", true, "Large");
        using var h = Page(wifi, bluetooth, small, large);

        Assert.Equal("Checked", h.Node(wifi).StateDescription);
        Assert.Equal("Not checked", h.Node(bluetooth).StateDescription);
        Assert.Equal("Not checked", h.Node(small).StateDescription);
        Assert.Equal("Checked", h.Node(large).StateDescription);
        Assert.True(h.Node(large).Has(AndroidNodeFlags.Checked));
        Assert.False(h.Node(large).Has(AndroidNodeFlags.Selected));
    }

    /// <summary>A column header has no role word of ours: TalkBack says "column header" for a
    /// collection's header item itself.</summary>
    [Fact]
    public void AColumnHeader_HasNoRoleWordOfItsOwn()
    {
        var type = Text.New("Type").AccessibleRole(AccessibilityRole.ColumnHeader);
        using var h = Page(Box.New().Children(type));
        Assert.Null(h.Node(type).RoleDescription);
    }

    /// <summary>An alert with a name is one stop: the text inside it, which the name says, is no
    /// stop of its own; a control inside it still is.</summary>
    [Fact]
    public void ANamedAlert_IsOneStop_AndItsTextNone_ButItsButtonIsOne()
    {
        var close = Button.New("Close");
        var bar = Box.New().AccessibleRole(AccessibilityRole.Alert).AccessibleName("Saved. Written to disk.")
            .Children(Text.New("Saved."), Text.New("Written to disk."), close);
        using var h = Page(bar);

        var all = h.ReadAll();
        Assert.Contains(all, n => n.Text == "Saved. Written to disk.");
        Assert.DoesNotContain(all, n => n.Text is "Saved." or "Written to disk.");
        Assert.Contains(all, n => n.Text == "Close");
    }

    /// <summary>A control's description is read after its name, in the content description TalkBack
    /// reads in place of the text; the text stays the name. As a hint it went unread.</summary>
    [Fact]
    public void AControlsDescription_IsReadAfterItsName()
    {
        var card = Button.New("Accessibility").AccessibleDescription("Contrast themes, narrator, magnifier");
        using var h = Page(card);

        var node = h.Node(card);
        Assert.Equal("Accessibility, Contrast themes, narrator, magnifier", node.ContentDescription);
        Assert.Equal("Accessibility", node.Text);
        Assert.Null(node.HintText);
    }

    /// <summary>A scroll under an alert TalkBack is on tells the host its subtree changed, which
    /// moves the focus box, not the alert, whose change TalkBack speaks again as a live region's:
    /// it read the "Could not connect" InfoBar a second time as its swipe scrolled on. A focused
    /// node that is no live region is told itself.</summary>
    [Fact]
    public void AScrollUnderAFocusedAlert_TellsTheHost_NotTheAlert()
    {
        var alert = Box.New().Height(40).FlexShrink(0).AccessibleRole(AccessibilityRole.Alert)
            .AccessibleName("Could not connect.").Children(Text.New("Could not connect."));
        var page = ScrollView.Vertical().Width(300).Height(200)
            .Children(Flex.Column().FlexShrink(0).Children(alert, Box.New().Height(900).FlexShrink(0)));
        using var h = new Harness(page, width: 300, height: 200);
        h.ReadAll();
        int alertId = h.Id(alert);
        Assert.True(h.Bridge.Perform(alertId, AndroidActions.AccessibilityFocus));
        h.Sink.Clear();

        page.ScrollOffsetY = 20;
        h.Apply();
        string events = string.Join(", ", h.Sink.Events.Select(e => $"{e.Type} {e.Source} {e.Changes}"));
        Assert.DoesNotContain(h.Sink.Events, e => e.Type == AndroidEventType.WindowContentChanged && e.Source == alertId);
        Assert.True(h.Sink.Events.Any(e => e.Type == AndroidEventType.WindowContentChanged && e.Source == AndroidIds.Host
            && (e.Changes & AndroidContentChange.Subtree) != 0), events);
    }

    /// <summary>The node TalkBack is on, picked (one of a set, a swatch), is said to be selected: of
    /// the selection event TalkBack says the name alone. Another one picked is not said.</summary>
    [Fact]
    public void ANodeTalkBackIsOn_Picked_IsSaidSelected_AndAnotherNot()
    {
        var blue = Button.New("Blue");
        blue.AccessibleSelected = false;
        var green = Button.New("Green");
        green.AccessibleSelected = false;
        using var h = Page(blue, green);
        h.ReadAll();
        Assert.True(h.Bridge.Perform(h.Id(blue), AndroidActions.AccessibilityFocus));
        h.Sink.Clear();

        green.AccessibleSelected = true;
        h.Apply();
        Assert.DoesNotContain(h.Sink.Events, e => e.Source == AndroidIds.Announcer);

        green.AccessibleSelected = false;
        blue.AccessibleSelected = true;
        h.Apply();
        Assert.Contains(h.Sink.Events, e => e.Source == AndroidIds.Announcer);
        Assert.Equal("Selected", h.Bridge.Node(AndroidIds.Announcer)!.Text);
    }

    /// <summary>A control that is off is a stop, read as disabled, as Android's own disabled views
    /// are: with none, a finger on the rich text toolbar's Redo put TalkBack's focus on the whole
    /// toolbar.</summary>
    [Fact]
    public void ADisabledControl_IsAStop_ThatIsNotEnabled()
    {
        var redo = Button.New("Redo").Enabled(false);
        using var h = Page(redo);
        var node = h.Node(redo);
        Assert.True(node.Has(AndroidNodeFlags.Focusable));
        Assert.False(node.Has(AndroidNodeFlags.Enabled));
        Assert.False(node.Has(AndroidNodeFlags.Clickable));
    }

    /// <summary>The node TalkBack is on, opened or closed, is said to have done so ("Expanded",
    /// "Collapsed"); another one is not.</summary>
    [Fact]
    public void AnExpanderTalkBackIsOn_OpeningOrClosing_IsSaid_AndAnotherNot()
    {
        var notifications = Button.New("Notifications");
        notifications.AccessibleExpanded = false;
        var advanced = Button.New("Advanced");
        advanced.AccessibleExpanded = false;
        using var h = Page(notifications, advanced);
        h.ReadAll();
        Assert.True(h.Bridge.Perform(h.Id(notifications), AndroidActions.AccessibilityFocus));
        h.Sink.Clear();

        advanced.AccessibleExpanded = true;
        h.Apply();
        Assert.DoesNotContain(h.Sink.Events, e => e.Source == AndroidIds.Announcer);

        notifications.AccessibleExpanded = true;
        h.Apply();
        Assert.Contains(h.Sink.Events, e => e.Source == AndroidIds.Announcer);
        Assert.Equal("Expanded", h.Bridge.Node(AndroidIds.Announcer)!.Text);
        notifications.AccessibleExpanded = false;
        h.Apply();
        Assert.Equal("Collapsed", h.Bridge.Node(AndroidIds.Announcer)!.Text);
    }
}
