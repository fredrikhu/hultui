using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Junction;

public partial class JunctionApp
{
    /// <summary>
    /// Placeholder for a view whose real content lands in a later slice. Keeps
    /// tab-switching (and the keyboard 1–5 shortcuts) working end-to-end now.
    /// </summary>
    private Element BuildStubView(string title, string message)
    {
        return Flex.Column(gap: 10).FlexGrow(1)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(
                Text.New(title).FontSize(18f).FontWeight(FontWeight.Medium).TextColor(Nocturne.On(0.70)),
                Text.New(message).FontSize(13f).TextColor(Nocturne.On(0.45)));
    }

    private Element BuildToast(string message)
    {
        var strip = Flex.Row(gap: 0)
            .Width(Dimension.Percent(100))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.FlexEnd)
            .Padding(new Thickness(0, 0, 22, 0))
            .ZIndex(50);
        strip.Position.Type = PositionType.Absolute;
        strip.Position.Left = Dimension.Px(0);
        strip.Position.Bottom = Dimension.Px(0);

        var pill = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(9, 16, 9, 16))
            .CornerRadius(Nocturne.RadiusMd)
            .Background(Nocturne.Neutral800)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .Children(
                Ui.Icon(Icons.CheckCircle, 15f, Nocturne.Accent300),
                Text.New(message).FontSize(12.5f).TextColor(Nocturne.Text));

        strip.AddChild(pill);
        return strip;
    }
}
