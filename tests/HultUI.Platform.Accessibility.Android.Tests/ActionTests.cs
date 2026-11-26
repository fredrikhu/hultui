using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Platform.Accessibility.Android.Tests;

/// <summary>
/// What TalkBack's gestures do (docs/design/TALKBACK.md §3): a double tap clicks the focused node as
/// its role means a click, the volume keys and scroll gestures step a slider or page a scroll view,
/// and TalkBack's own focus is kept by the bridge and told back. Everything but that focus goes to
/// the window's router, which runs it on the UI thread.
/// </summary>
public class ActionTests
{
    private static Harness Page(params Element[] children) =>
        new(Box.New().Width(400).Height(800).Children(children));

    [Fact]
    public void ADoubleTap_InvokesAButton_TogglesACheckbox_ChoosesARadio_AndOpensADropdown()
    {
        var save = Button.New("Save");
        var wifi = Checkbox.New(false, "Wi-Fi");
        var small = RadioButton.New("size", false, "Small");
        var colour = Dropdown.New(["Red", "Green"], 0).Width(150);
        using var h = Page(save, wifi, small, colour);

        Assert.True(h.Bridge.Perform(h.Id(save), AndroidActions.Click));
        Assert.True(h.Bridge.Perform(h.Id(wifi), AndroidActions.Click));
        Assert.True(h.Bridge.Perform(h.Id(small), AndroidActions.Click));
        Assert.True(h.Bridge.Perform(h.Id(colour), AndroidActions.Click));

        Assert.Equal([
            $"invoke {save.Id.Value}",
            $"toggle {wifi.Id.Value}",
            $"select {small.Id.Value}",
            $"expand {colour.Id.Value}",
        ], h.Host.Calls);
    }

    [Fact]
    public void AnActionTheNodeDoesNotOffer_IsRefused_AndRoutesNothing()
    {
        var save = Button.New("Save").Enabled(false);
        var label = Core.Builders.Text.New("Just words");
        using var h = Page(save, label);

        Assert.False(h.Bridge.Perform(h.Id(save), AndroidActions.Click));
        Assert.False(h.Bridge.Perform(h.Id(label), AndroidActions.SetText, new AndroidActionArgs(Text: "x")));
        Assert.False(h.Bridge.Perform(12345, AndroidActions.Click));
        Assert.Empty(h.Host.Calls);
    }

    [Fact]
    public void AFieldsTextIsSet_AndItTakesTheFocus()
    {
        var name = TextInput.New("").AccessibleName("Name").Width(200);
        using var h = Page(name);

        Assert.True(h.Bridge.Perform(h.Id(name), AndroidActions.SetText, new AndroidActionArgs(Text: "Grace")));
        Assert.True(h.Bridge.Perform(h.Id(name), AndroidActions.Focus));
        Assert.Equal([$"value {name.Id.Value} Grace", $"focus {name.Id.Value}"], h.Host.Calls);
    }

    [Fact]
    public void ASlider_IsSetToAValue_AndStepsForwardAndBack()
    {
        var volume = Slider.New(30, 0, 100).AccessibleName("Volume").Width(200);
        using var h = Page(volume);
        double small = ((Core.Accessibility.IRangeValueElement)volume).SmallChange;
        double step = small > 0 ? small : 5;

        Assert.True(h.Bridge.Perform(h.Id(volume), AndroidActions.SetProgress, new AndroidActionArgs(Progress: 75)));
        Assert.True(h.Bridge.Perform(h.Id(volume), AndroidActions.ScrollForward));
        Assert.True(h.Bridge.Perform(h.Id(volume), AndroidActions.ScrollBackward));
        Assert.Equal([
            $"range {volume.Id.Value} 75",
            $"range {volume.Id.Value} {30 + step}",
            $"range {volume.Id.Value} {30 - step}",
        ], h.Host.Calls);
    }

    [Fact]
    public void AScrollViewPages_ByItsViewport()
    {
        var page = ScrollView.Vertical().Width(300).Height(200).Children(Box.New().Height(900).FlexShrink(0));
        using var h = new Harness(page, width: 300, height: 200);

        Assert.True(h.Bridge.Perform(h.Id(page), AndroidActions.ScrollForward));
        Assert.True(h.Bridge.Perform(h.Id(page), AndroidActions.ScrollDown));
        Assert.False(h.Bridge.Perform(h.Id(page), AndroidActions.ScrollUp), "at the top: nothing above");
        Assert.Equal([$"scroll {page.Id.Value} 0 200", $"scroll {page.Id.Value} 0 200"], h.Host.Calls);
    }

    [Fact]
    public void ShowOnScreen_ScrollsTheNodeIntoView()
    {
        var save = Button.New("Save");
        using var h = Page(save);

        Assert.True(h.Bridge.Perform(h.Id(save), AndroidActions.ShowOnScreen));
        Assert.Equal([$"reveal {save.Id.Value}"], h.Host.Calls);
    }

    [Fact]
    public void TalkBacksFocus_IsKeptHere_ToldBack_AndShownOnTheNode()
    {
        var save = Button.New("Save");
        var cancel = Button.New("Cancel");
        using var h = Page(save, cancel);
        int a = h.Id(save), b = h.Id(cancel);

        Assert.True(h.Bridge.Perform(a, AndroidActions.AccessibilityFocus));
        Assert.Equal((AndroidEventType.ViewAccessibilityFocused, a), (Assert.Single(h.Sink.Events).Type, h.Sink.Events[0].Source));
        Assert.True(h.Node(save).Has(AndroidNodeFlags.AccessibilityFocused));
        Assert.True(h.Node(save).Can(AndroidActions.ClearAccessibilityFocus));
        Assert.False(h.Bridge.Perform(a, AndroidActions.AccessibilityFocus), "already there");
        h.Sink.Clear();

        Assert.True(h.Bridge.Perform(b, AndroidActions.AccessibilityFocus));
        Assert.Equal([(AndroidEventType.ViewAccessibilityFocusCleared, a), (AndroidEventType.ViewAccessibilityFocused, b)],
            h.Sink.Events.Select(e => (e.Type, e.Source)));
        Assert.False(h.Node(save).Has(AndroidNodeFlags.AccessibilityFocused));
        Assert.Equal(b, h.Bridge.AccessibilityFocusedId);
        h.Sink.Clear();

        Assert.False(h.Bridge.Perform(a, AndroidActions.ClearAccessibilityFocus), "not on it");
        Assert.True(h.Bridge.Perform(b, AndroidActions.ClearAccessibilityFocus));
        Assert.Equal(AndroidIds.None, h.Bridge.AccessibilityFocusedId);
        Assert.Empty(h.Host.Calls);
    }
}
