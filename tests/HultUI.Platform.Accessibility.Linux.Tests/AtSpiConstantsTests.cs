using HultUI.Core.Accessibility;
using HultUI.Platform.Accessibility.Linux;
using Xunit;

namespace HultUI.Platform.Accessibility.Linux.Tests;

/// <summary>
/// Pins the AT-SPI2 numeric role codes and names. These are ABI-stable values
/// from <c>atspi-constants.h</c>, verified by introspecting a live GTK
/// application on the a11y bus — an assistive technology reads the number and
/// announces the widget type, so a wrong value silently mislabels every control.
/// </summary>
public class AtSpiConstantsTests
{
    [Theory]
    [InlineData(AccessibilityRole.Button, 43u, "push button")]
    [InlineData(AccessibilityRole.Checkbox, 7u, "check box")]
    [InlineData(AccessibilityRole.RadioButton, 44u, "radio button")]
    [InlineData(AccessibilityRole.TextInput, 61u, "text")]
    [InlineData(AccessibilityRole.Label, 29u, "label")]
    [InlineData(AccessibilityRole.Image, 27u, "image")]
    [InlineData(AccessibilityRole.ScrollView, 49u, "scroll pane")]
    [InlineData(AccessibilityRole.Slider, 51u, "slider")]
    [InlineData(AccessibilityRole.ProgressBar, 42u, "progress bar")]
    [InlineData(AccessibilityRole.SpinButton, 52u, "spin button")]
    [InlineData(AccessibilityRole.Switch, 62u, "toggle button")]
    [InlineData(AccessibilityRole.Tab, 37u, "page tab")]
    // The content shown for a tab, which W3C Core-AAM maps to a scroll pane. It was the page
    // tab list (38) — the strip of tabs — so a reader announced each panel as a row of tabs.
    [InlineData(AccessibilityRole.TabPanel, 49u, "scroll pane")]
    [InlineData(AccessibilityRole.TabList, 38u, "page tab list")]
    [InlineData(AccessibilityRole.Separator, 50u, "separator")]
    [InlineData(AccessibilityRole.List, 31u, "list")]
    [InlineData(AccessibilityRole.ListItem, 32u, "list item")]
    [InlineData(AccessibilityRole.Document, 95u, "document web")]
    [InlineData(AccessibilityRole.Dialog, 16u, "dialog")]
    [InlineData(AccessibilityRole.Alert, 2u, "alert")]
    [InlineData(AccessibilityRole.Heading, 83u, "heading")]
    [InlineData(AccessibilityRole.Link, 88u, "link")]
    [InlineData(AccessibilityRole.Group, 39u, "panel")]
    [InlineData(AccessibilityRole.Application, 75u, "application")]
    [InlineData(AccessibilityRole.MenuBar, 34u, "menu bar")]
    [InlineData(AccessibilityRole.MenuItem, 35u, "menu item")]
    [InlineData(AccessibilityRole.ColumnHeader, 57u, "table column header")]
    public void MapRole_MatchesAtSpiAbi(AccessibilityRole role, uint expectedCode, string expectedName)
    {
        uint code = AtSpiConstants.MapRole(role);
        Assert.Equal(expectedCode, code);
        Assert.Equal(expectedName, AtSpiConstants.RoleName(code));
    }

    [Fact]
    public void MapRole_CoversEveryRole_WithoutInvalid()
    {
        foreach (AccessibilityRole role in Enum.GetValues<AccessibilityRole>())
        {
            uint code = AtSpiConstants.MapRole(role);
            Assert.NotEqual(AtSpiConstants.RoleInvalid, code);
            // Every mapped code has a concrete, non-"unknown" name.
            Assert.NotEqual("unknown", AtSpiConstants.RoleName(code));
        }
    }

    [Theory]
    [InlineData(AccessibilityProperties.Name, "accessible-name")]
    [InlineData(AccessibilityProperties.Description, "accessible-description")]
    [InlineData(AccessibilityProperties.Value, "accessible-value")]
    public void MapProperty_MapsChangeNames(string property, string expected) =>
        Assert.Equal(expected, AtSpiConstants.MapProperty(property));

    [Fact]
    public void MapProperty_UnknownProperty_IsEmpty() =>
        Assert.Equal("", AtSpiConstants.MapProperty(AccessibilityProperties.BoundingRect));
}
