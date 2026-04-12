using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Components.Shared;

/// <summary>
/// Holds the state for a pending confirm dialog.
/// </summary>
public sealed class ConfirmDialogState
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required Action OnConfirm { get; init; }
    public Action? OnCancel { get; init; }
    public string ConfirmLabel { get; init; } = "Confirm";
    public string CancelLabel { get; init; } = "Cancel";
    public bool Destructive { get; init; }
}

/// <summary>
/// Renders a modal confirm dialog as an absolutely-positioned overlay.
/// Add the result of <see cref="Build"/> as a child of the root layout
/// container when a dialog is active.
/// </summary>
public static class ConfirmDialog
{
    private static readonly Color BackdropColor = new(0, 0, 0, 140);
    private static readonly Color DialogBg = Color.FromHex("#2a2a48");
    private static readonly Color DialogBorder = Color.FromHex("#3a3a5a");
    private static readonly Color DestructiveColor = Color.FromHex("#ef4444");
    private static readonly Color DestructiveHover = Color.FromHex("#dc2626");

    public static Element Build(ConfirmDialogState state, Action dismiss)
    {
        // Full-screen backdrop
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

        // Clicking the backdrop cancels
        backdrop.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            state.OnCancel?.Invoke();
            dismiss();
        });

        // Dialog panel
        var panel = Box.New()
            .Width(400)
            .Padding(new Thickness(20, 24, 20, 24))
            .CornerRadius(8f)
            .Background(DialogBg)
            .BorderColor(DialogBorder)
            .BorderWidth(1);

        // Stop clicks inside the panel from dismissing
        panel.On<ClickEvent>(e => e.StopPropagation = true);

        var content = Flex.Column(gap: 16);

        // Title
        content.AddChild(
            Text.New(state.Title).FontSize(16f).FontWeight(FontWeight.Bold)
                .TextColor(GitClientColors.TextPrimary)
        );

        // Body
        content.AddChild(
            Text.New(state.Body).FontSize(13f)
                .TextColor(GitClientColors.TextSecondary)
        );

        // Buttons
        var buttons = Flex.Row(gap: 8)
            .JustifyContent(JustifyContent.FlexEnd)
            .AlignItems(AlignItems.Center);

        buttons.AddChild(BuildButton(state.CancelLabel, false, () =>
        {
            state.OnCancel?.Invoke();
            dismiss();
        }));

        buttons.AddChild(BuildButton(state.ConfirmLabel, state.Destructive, () =>
        {
            state.OnConfirm();
            dismiss();
        }));

        content.AddChild(buttons);
        panel.AddChild(content);
        backdrop.AddChild(panel);

        return backdrop;
    }

    private static Element BuildButton(string label, bool destructive, Action onClick)
    {
        var bg = destructive ? DestructiveColor : GitClientColors.ButtonPrimary;
        var hoverBg = destructive ? DestructiveHover : GitClientColors.ButtonPrimaryHover;

        var btn = Box.New()
            .Padding(new Thickness(8, 16, 8, 16))
            .CornerRadius(4f)
            .Background(bg)
            .Cursor(CursorType.Pointer)
            .Children(
                Text.New(label).FontSize(13f).FontWeight(FontWeight.SemiBold)
                    .TextColor(Color.White)
            );

        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = hoverBg);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = bg);
        btn.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            onClick();
        });

        return btn;
    }
}
