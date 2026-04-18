using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Components.Shared;

/// <summary>
/// State for a pending passphrase/password prompt shown as a modal dialog.
/// </summary>
public sealed class PasswordDialogState
{
    public required string Prompt { get; init; }
    public required Action<string?> OnResult { get; init; }
    internal string Value { get; set; } = "";
}

/// <summary>
/// Modal dialog that collects a passphrase from the user. Used to intercept
/// SSH key passphrase prompts that would otherwise appear in the terminal.
/// </summary>
public static class PasswordDialog
{
    private static readonly Color BackdropColor = new(0, 0, 0, 140);
    private static readonly Color DialogBg = Color.FromHex("#2a2a48");
    private static readonly Color DialogBorder = Color.FromHex("#3a3a5a");

    public static Element Build(PasswordDialogState state, Action dismiss)
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
            state.OnResult(null);
            dismiss();
        });

        var panel = Box.New()
            .Width(420)
            .Padding(new Thickness(20, 24, 20, 24))
            .CornerRadius(8f)
            .Background(DialogBg)
            .BorderColor(DialogBorder)
            .BorderWidth(1);
        panel.On<ClickEvent>(e => e.StopPropagation = true);

        var content = Flex.Column(gap: 16);

        content.AddChild(
            Text.New("Passphrase Required").FontSize(16f).FontWeight(FontWeight.Bold)
                .TextColor(GitClientColors.TextPrimary)
        );

        content.AddChild(
            Text.New(state.Prompt).FontSize(12f)
                .TextColor(GitClientColors.TextSecondary)
        );

        var input = TextInput.New(state.Value)
            .PlaceholderText("Enter passphrase…")
            .OnTextChanged(text => state.Value = text)
            .FontSize(13f)
            .FlexGrow(1)
            .Height(Dimension.Px(30))
            .Background(GitClientColors.Background)
            .CornerRadius(4f)
            .BorderColor(GitClientColors.Border)
            .TextColor(GitClientColors.TextPrimary);

        input.On<KeyDownEvent>(e =>
        {
            if (e.Key == KeyCode.Enter)
            {
                e.StopPropagation = true;
                state.OnResult(state.Value);
                dismiss();
            }
            else if (e.Key == KeyCode.Escape)
            {
                e.StopPropagation = true;
                state.OnResult(null);
                dismiss();
            }
        });

        content.AddChild(input);

        // Buttons
        var buttons = Flex.Row(gap: 8)
            .JustifyContent(JustifyContent.FlexEnd)
            .AlignItems(AlignItems.Center);

        buttons.AddChild(BuildButton("Cancel", false, () =>
        {
            state.OnResult(null);
            dismiss();
        }));

        buttons.AddChild(BuildButton("Unlock", true, () =>
        {
            state.OnResult(state.Value);
            dismiss();
        }));

        content.AddChild(buttons);
        panel.AddChild(content);
        backdrop.AddChild(panel);
        return backdrop;
    }

    private static Element BuildButton(string label, bool primary, Action onClick)
    {
        var bg = primary ? GitClientColors.ButtonPrimary : GitClientColors.ButtonSecondary;
        var hover = primary ? GitClientColors.ButtonPrimaryHover : GitClientColors.ButtonSecondaryHover;

        var btn = Box.New()
            .Padding(new Thickness(8, 16, 8, 16))
            .CornerRadius(4f)
            .Background(bg)
            .Cursor(CursorType.Pointer)
            .Children(
                Text.New(label).FontSize(13f).FontWeight(FontWeight.SemiBold)
                    .TextColor(Color.White)
            );
        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = hover);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = bg);
        btn.On<ClickEvent>(e => { e.StopPropagation = true; onClick(); });
        return btn;
    }
}
