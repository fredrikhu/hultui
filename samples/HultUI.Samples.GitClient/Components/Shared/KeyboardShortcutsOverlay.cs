using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Components.Shared;

/// <summary>
/// Full-screen overlay listing all keyboard shortcuts. Toggled by pressing "?".
/// </summary>
public static class KeyboardShortcutsOverlay
{
    private static readonly Color BackdropColor = new(0, 0, 0, 160);
    private static readonly Color DialogBg = Color.FromHex("#2a2a48");
    private static readonly Color DialogBorder = Color.FromHex("#3a3a5a");
    private static readonly Color KeyBg = Color.FromHex("#1e1e36");
    private static readonly Color KeyBorder = Color.FromHex("#4a4a6a");

    private static readonly (string Key, string Description)[] Shortcuts =
    [
        ("?", "Show / hide this help"),
        ("Ctrl+F", "Show / hide commit search"),
        ("Ctrl+L", "Focus commit search"),
        ("Ctrl+O", "Open repository"),
        ("Ctrl+W", "Toggle working copy view"),
        ("Escape", "Close dialog / overlay"),
    ];

    public static Element Build(Action dismiss)
    {
        var backdrop = Box.New()
            .Width(Dimension.Percent(100))
            .Height(Dimension.Percent(100))
            .Background(BackdropColor)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .ZIndex(100);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            dismiss();
        });

        var panel = Box.New()
            .Width(380)
            .Padding(new Thickness(20, 24, 20, 24))
            .CornerRadius(8f)
            .Background(DialogBg)
            .BorderColor(DialogBorder)
            .BorderWidth(1);
        panel.On<ClickEvent>(e => e.StopPropagation = true);

        var content = Flex.Column(gap: 16);

        content.AddChild(
            Text.New("Keyboard Shortcuts").FontSize(16f).FontWeight(FontWeight.Bold)
                .TextColor(GitClientColors.TextPrimary)
        );

        foreach (var (key, desc) in Shortcuts)
        {
            content.AddChild(BuildRow(key, desc));
        }

        // Dismiss hint
        content.AddChild(
            Text.New("Press ? or Escape to close")
                .FontSize(11f)
                .TextColor(GitClientColors.TextMuted)
        );

        panel.AddChild(content);
        backdrop.AddChild(panel);
        return backdrop;
    }

    private static Element BuildRow(string key, string description)
    {
        var row = Flex.Row(gap: 12)
            .AlignItems(AlignItems.Center);

        // Key badge
        var badge = Box.New()
            .Padding(new Thickness(4, 10, 4, 10))
            .CornerRadius(4f)
            .Background(KeyBg)
            .BorderColor(KeyBorder)
            .BorderWidth(1)
            .MinWidth(Dimension.Px(60))
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Children(
                Text.New(key).FontSize(12f).FontWeight(FontWeight.SemiBold)
                    .TextColor(GitClientColors.TextPrimary)
            );

        row.AddChild(badge);
        row.AddChild(
            Text.New(description).FontSize(13f)
                .TextColor(GitClientColors.TextSecondary)
        );

        return row;
    }
}
