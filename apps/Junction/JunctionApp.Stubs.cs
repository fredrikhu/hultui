using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Theme;

namespace Junction;

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
}
