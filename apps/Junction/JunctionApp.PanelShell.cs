using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Theme;

namespace Junction;

/// <summary>
/// The shell every repository panel shares — worktrees, submodules, remotes, reflog: a
/// dimmed backdrop that closes on a click, a centred card, and a header of icon, title,
/// subtitle and close. Each panel supplies its own toolbar and rows. One place for the
/// z-order (42, under the Options dialog and the confirms) and the chrome, so the five
/// cannot drift from each other.
/// </summary>
public partial class JunctionApp
{
    /// <summary>The card a panel's header, toolbar and rows go into.</summary>
    private static BoxElement PanelCard(float width, float maxHeight)
    {
        var card = Flex.Column(gap: 0)
            .Width(width).MaxHeight(maxHeight)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg);
        card.On<ClickEvent>(e => e.StopPropagation = true);
        return card;
    }

    /// <summary>The backdrop the card sits on; a click outside the card closes the panel.</summary>
    private static Element PanelBackdrop(Element card, Action close)
    {
        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(new Color(0, 0, 0, 140))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Padding(new Thickness(24))
            .ZIndex(42);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; close(); });
        backdrop.AddChild(card);
        return backdrop;
    }

    /// <summary>Icon · title over a subtitle · close, with a hairline under it.</summary>
    private static Element PanelHeader(string icon, string title, string subtitle, Action close)
    {
        var bar = Flex.Row(gap: 10).AlignItems(AlignItems.Center)
            .Padding(new Thickness(14, 18, 14, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        bar.AddChild(Ui.Icon(icon, 16f, Nocturne.Accent300).FlexShrink(0));
        var titleCol = Flex.Column(gap: 1).FlexGrow(1);
        titleCol.AddChild(Text.New(title).FontSize(16f).FontWeight(FontWeight.SemiBold));
        titleCol.AddChild(Text.New(subtitle).FontSize(11.5f).TextColor(Nocturne.On(0.5))
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        bar.AddChild(titleCol);
        bar.AddChild(Ui.IconButton(Icons.WinClose, close, "Close (Esc)", 30f).FlexShrink(0));
        return bar;
    }

    /// <summary>The words a panel shows in place of rows — loading, or nothing to list.</summary>
    private static Element PanelPlaceholder(string text) =>
        Box.New().Padding(new Thickness(28)).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Text.New(text).FontSize(13f).TextColor(Nocturne.On(0.45)));

    /// <summary>The active repository's folder name, the subtitle every panel starts with.</summary>
    private string ActiveRepoName => System.IO.Path.GetFileName(_activeRepoPath.TrimEnd('/', '\\'));
}
