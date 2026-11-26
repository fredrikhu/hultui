using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Platform.Accessibility.Android.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// Each role as TalkBack reads it (docs/design/TALKBACK.md §3): the widget class it reads the role
/// from, what the node says and where (text, content description, hint), its states, and the actions
/// a double tap, a swipe or the volume keys reach.
/// </summary>
public class NodeMappingTests
{
    private static Harness Page(params Element[] children) =>
        new(Box.New().Width(400).Height(800).Children(children));

    [Fact]
    public void AButton_IsAnAndroidButton_ThatSaysItsLabel_AndIsClicked()
    {
        var save = Button.New("Save");
        using var h = Page(save);

        var node = h.Node(save);
        Assert.Equal(AndroidClasses.Button, node.ClassName);
        Assert.Equal("Save", node.Text);
        Assert.True(node.Has(AndroidNodeFlags.Clickable | AndroidNodeFlags.Enabled | AndroidNodeFlags.VisibleToUser));
        Assert.True(node.Can(AndroidActions.Click | AndroidActions.AccessibilityFocus));
        Assert.Null(node.RoleDescription);
    }

    [Fact]
    public void ADisabledButton_IsNotEnabled_AndOffersNoClick()
    {
        var save = Button.New("Save").Enabled(false);
        using var h = Page(save);

        var node = h.Node(save);
        Assert.False(node.Has(AndroidNodeFlags.Enabled));
        Assert.False(node.Has(AndroidNodeFlags.Clickable));
        Assert.False(node.Can(AndroidActions.Click));
    }

    [Fact]
    public void ACheckbox_SwitchAndRadio_AreCheckable_AndSayWhetherTheyAreChecked()
    {
        var wifi = Checkbox.New(true, "Wi-Fi");
        var bluetooth = ToggleSwitch.New(false, "Bluetooth");
        var small = RadioButton.New("size", false, "Small");
        var large = RadioButton.New("size", true, "Large");
        using var h = Page(wifi, bluetooth, small, large);

        var check = h.Node(wifi);
        Assert.Equal(AndroidClasses.CheckBox, check.ClassName);
        Assert.Equal("Wi-Fi", check.Text);
        Assert.True(check.Has(AndroidNodeFlags.Checkable | AndroidNodeFlags.Checked));

        var toggle = h.Node(bluetooth);
        Assert.Equal(AndroidClasses.Switch, toggle.ClassName);
        Assert.True(toggle.Has(AndroidNodeFlags.Checkable));
        Assert.False(toggle.Has(AndroidNodeFlags.Checked));

        Assert.Equal(AndroidClasses.RadioButton, h.Node(small).ClassName);
        Assert.False(h.Node(small).Has(AndroidNodeFlags.Checked));
        Assert.True(h.Node(small).Can(AndroidActions.Select));
        Assert.True(h.Node(large).Has(AndroidNodeFlags.Checkable | AndroidNodeFlags.Checked));
    }

    [Fact]
    public void ATextField_IsAnEditText_WhoseTextIsWhatIsTyped_AndWhoseNameIsTheHint()
    {
        var name = TextInput.New("Ada").AccessibleName("Your name").Width(200);
        using var h = Page(name);

        var node = h.Node(name);
        Assert.Equal(AndroidClasses.EditText, node.ClassName);
        Assert.Equal("Ada", node.Text);
        Assert.Equal("Your name", node.HintText);
        Assert.True(node.Has(AndroidNodeFlags.Editable | AndroidNodeFlags.Focusable));
        Assert.True(node.Can(AndroidActions.SetText | AndroidActions.Focus));
        Assert.False(node.Has(AndroidNodeFlags.MultiLine));
    }

    [Fact]
    public void APasswordField_SaysItIsOne_AndNeverItsText()
    {
        var password = TextInput.New("hunter2").Password().AccessibleName("Password").Width(200);
        using var h = Page(password);

        var node = h.Node(password);
        Assert.True(node.Has(AndroidNodeFlags.Password));
        Assert.DoesNotContain("hunter2", node.Text ?? "");
    }

    [Fact]
    public void AnImage_SaysItsNameAsItsContentDescription()
    {
        var logo = Image.New().AccessibleName("Logo").Width(40).Height(40);
        using var h = Page(logo);

        var node = h.Node(logo);
        Assert.Equal(AndroidClasses.ImageView, node.ClassName);
        Assert.Equal("Logo", node.ContentDescription);
        Assert.Null(node.Text);
    }

    [Fact]
    public void AHeading_IsMarkedAsOne()
    {
        var title = Text.New("Settings");
        title.Role = AccessibilityRole.Heading;
        using var h = Page(title);

        var node = h.Node(title);
        Assert.Equal(AndroidClasses.TextView, node.ClassName);
        Assert.Equal("Settings", node.Text);
        Assert.True(node.Has(AndroidNodeFlags.Heading));
    }

    [Fact]
    public void ASlider_IsASeekBar_WithItsRange_AndTheVolumeKeysStepIt()
    {
        var volume = Slider.New(30, 0, 100).AccessibleName("Volume").Width(200);
        using var h = Page(volume);

        var node = h.Node(volume);
        Assert.Equal(AndroidClasses.SeekBar, node.ClassName);
        Assert.Equal("Volume", node.ContentDescription);
        Assert.Equal(new AndroidRange(AndroidRangeType.Int, 0, 100, 30), node.Range);
        Assert.True(node.Can(AndroidActions.SetProgress | AndroidActions.ScrollForward | AndroidActions.ScrollBackward));
    }

    [Fact]
    public void AnExpander_OffersToExpand_AndSaysItIsCollapsed()
    {
        var details = Expander.New("Details", Text.New("The body"));
        using var h = Page(details);

        var node = Assert.Single(h.ReadAll(), n => n.Expanded is not null);
        Assert.False(node.Expanded);
        Assert.True(node.Can(AndroidActions.Expand));
        Assert.False(node.Can(AndroidActions.Collapse));
    }

    [Fact]
    public void ADropdown_IsASpinner_ThatSaysItsChoice_WithItsNameAsTheHint()
    {
        var colour = Dropdown.New(["Red", "Green"], 1).AccessibleName("Colour").Width(150);
        using var h = Page(colour);

        var node = h.Node(colour);
        Assert.Equal(AndroidClasses.Spinner, node.ClassName);
        Assert.Equal("Green", node.Text);
        Assert.Equal("Colour", node.HintText);
        Assert.True(node.Can(AndroidActions.Click));
        Assert.Null(node.Expanded);
    }

    [Fact]
    public void AList_CountsItsItems_AndEachItemKnowsItsRow()
    {
        var list = Box.New();
        list.Role = AccessibilityRole.List;
        list.AccessibleName = "Fruit";
        var items = new[] { "Apple", "Pear", "Plum" }.Select(name =>
        {
            var item = Text.New(name);
            item.Role = AccessibilityRole.ListItem;
            return (Element)item;
        }).ToArray();
        list.Children(items);
        using var h = Page(list);

        var node = h.Node(list);
        Assert.Equal(AndroidClasses.ListView, node.ClassName);
        Assert.Equal(new AndroidCollection(3, 1, false, AndroidSelectionMode.None), node.Collection);
        // A container's name is its title, not text: text would make it a stop in front of its items.
        Assert.Null(node.Text);
        Assert.Equal("Fruit", node.ContainerTitle);
        Assert.Equal(2, h.Node(items[2]).CollectionItem?.Row);
        Assert.Equal("Plum", h.Node(items[2]).Text);
    }

    [Fact]
    public void AContainerThatTakesTheKeys_IsNotFocusable_SoItsLabelsStayStopsOfTheirOwn()
    {
        var group = Box.New().Children(Text.New("First"), Text.New("Second"));
        group.Role = AccessibilityRole.Group;
        group.IsFocusable = true;
        var field = TextInput.New("x").Width(100);
        using var h = Page(group, field);

        Assert.False(h.Node(group).Has(AndroidNodeFlags.Focusable));
        Assert.False(h.Node(group).Can(AndroidActions.Focus));
        Assert.True(h.Node(field).Has(AndroidNodeFlags.Focusable));
    }

    [Fact]
    public void ADialog_HasItsNameAsItsPaneTitle()
    {
        var dialog = Box.New().Children(Button.New("Delete"));
        dialog.Role = AccessibilityRole.Dialog;
        dialog.AccessibleName = "Delete the file?";
        using var h = Page(dialog);

        var node = h.Node(dialog);
        Assert.Equal("Delete the file?", node.PaneTitle);
        Assert.Null(node.Text);
    }

    [Fact]
    public void ALink_AndATab_SayTheirRoleInWords()
    {
        var link = Text.New("Help");
        link.Role = AccessibilityRole.Link;
        var tab = Text.New("Music");
        tab.Role = AccessibilityRole.Tab;
        using var h = Page(link, tab);

        Assert.Equal("Link", h.Node(link).RoleDescription);
        Assert.Equal("Tab", h.Node(tab).RoleDescription);
        Assert.True(h.Node(link).Can(AndroidActions.Click));
    }
}
