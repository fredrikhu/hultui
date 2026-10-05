using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A single-line flex container holds its line within its own min and max cross size (CSS
/// Flexbox §9.4 step 8), so AlignItems works in the box it ends up as. Before, where nothing
/// forced its cross size, the line was only as big as its items: a row with a min height put
/// centred text along its top, and a card with a min width left its stretched children at
/// their natural width.
/// </summary>
public sealed class SingleLineCrossClampTests
{
    private static Rect Painted(Element e) => LayoutProbe.Painted(e);

    [Fact]
    public void AMinHeightRowCentresItsTextWhereItsHeightIsNotForced()
    {
        using var session = HeadlessSession.Create(600, 200);
        var text = TextElement.New("Pull").FontSize(13);
        var bar = Flex.Row().MinHeight(48).AlignItems(AlignItems.Center).Children(text);
        var row = Flex.Row().AlignItems(AlignItems.Center).Height(100).Width(500).Children(bar);
        session.Window.SetRoot(Box.New().Children(row));
        session.PumpFrame();

        var b = Painted(bar);
        var t = Painted(text);
        Assert.InRange(t.Height, 10f, 30f);

        Assert.Equal(48f, b.Height, 0.01f);
        Assert.Equal(t.Y - b.Y, b.Bottom - t.Bottom, 0.01f);
    }

    [Fact]
    public void AMinWidthCardStretchesItsFieldAcrossIt()
    {
        using var session = HeadlessSession.Create(600, 300);
        var field = TextInput.New("x");
        var card = Flex.Column().MinWidth(320).Padding(new Thickness(16)).Children(field);
        var page = Flex.Column().AlignItems(AlignItems.Center).Width(600).Height(300).Children(card);
        session.Window.SetRoot(Box.New().Children(page));
        session.PumpFrame();

        Assert.Equal(320f, Painted(card).Width, 0.01f);
        Assert.Equal(LayoutProbe.Inner(card).Width, Painted(field).Width, 0.01f);
    }
}
