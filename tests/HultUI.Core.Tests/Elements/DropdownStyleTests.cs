using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// The layered style for the dropdown-shaped fields: one <see cref="DropdownStyle"/> laid
/// over <see cref="DropdownTheme"/>, shared by the dropdown, the number field and the two
/// pickers so a compact stepper and a form dropdown can differ without a wrapper box.
/// </summary>
public class DropdownStyleTests
{
    private static readonly DropdownStyle Compact = new()
    {
        Padding = new Thickness(2, 6), FontSize = 11f, MinWidth = 48f,
    };

    [Fact]
    public void Resolve_LaysTheStyleOverTheTheme_AndKeepsWhatItDoesNotName()
    {
        var themed = new DropdownTheme { FontSize = 15f, CornerRadius = 9f, BorderColor = new Color(1, 2, 3) };
        var resolved = themed.Resolve(Compact);

        Assert.Equal(new Thickness(2, 6), resolved.Padding);
        Assert.Equal(11f, resolved.FontSize);
        Assert.Equal(48f, resolved.MinWidth);
        Assert.Equal(9f, resolved.CornerRadius);                 // untouched
        Assert.Equal(new Color(1, 2, 3), resolved.BorderColor);  // untouched
        Assert.Equal(themed.ChevronShape, resolved.ChevronShape);
    }

    [Fact]
    public void Resolve_WithNoStyle_IsTheThemeItself()
    {
        var themed = new DropdownTheme();
        Assert.Same(themed, themed.Resolve(null));
    }

    [Fact]
    public void EveryFieldInTheFamily_TakesTheStyle_AndPatchesIt()
    {
        var dropdown = Dropdown.New(["a", "b"], 0).Style(Compact);
        var number = NumericUpDown.New(3).Style(Compact);
        var date = DatePicker.New().Style(Compact);
        var time = TimePicker.New().Style(Compact);
        Assert.Same(Compact, dropdown.Style);
        Assert.Same(Compact, number.Style);
        Assert.Same(Compact, date.Style);
        Assert.Same(Compact, time.Style);

        var liveDropdown = Dropdown.New(["a", "b"], 0);
        liveDropdown.PatchFrom(dropdown);
        Assert.Same(Compact, liveDropdown.Style);
        liveDropdown.PatchFrom(Dropdown.New(["a", "b"], 0));
        Assert.Null(liveDropdown.Style);

        var liveNumber = NumericUpDown.New(3);
        liveNumber.PatchFrom(number);
        Assert.Same(Compact, liveNumber.Style);

        var liveDate = DatePicker.New();
        liveDate.PatchFrom(date);
        Assert.Same(Compact, liveDate.Style);

        var liveTime = TimePicker.New();
        liveTime.PatchFrom(time);
        Assert.Same(Compact, liveTime.Style);
    }

    [Fact]
    public void ANumberField_MeasuresToItsStyle()
    {
        var plain = NumericUpDown.New(3);
        var compact = NumericUpDown.New(3).Style(Compact);
        plain.ApplyTheme(Theme.Light);
        compact.ApplyTheme(Theme.Light);

        var ctx = new StubMeasureContext();
        var plainSize = plain.MeasureContent(1000f, 1000f, ctx);
        var compactSize = compact.MeasureContent(1000f, 1000f, ctx);

        // Less padding above and below the digits: the theme's 6 + 6 against the style's 2 + 2.
        Assert.Equal(plainSize.Height - 8f, compactSize.Height, 1);
        Assert.True(compactSize.Width < plainSize.Width);
    }
}
