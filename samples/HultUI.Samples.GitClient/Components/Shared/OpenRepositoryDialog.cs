using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Components.Shared;

public sealed class OpenRepositoryDialogState
{
    public string Path { get; set; } = "";
    public string? Error { get; set; }
    public bool PendingFocus { get; set; } = true;
    public Ref<TextInputElement> InputRef { get; } = new();
    public required Action<string> OnConfirm { get; init; }
}

public static class OpenRepositoryDialog
{
    private static readonly Color BackdropColor = new(0, 0, 0, 140);
    private static readonly Color DialogBg = Color.FromHex("#2a2a48");
    private static readonly Color DialogBorder = Color.FromHex("#3a3a5a");

    public static Element Build(OpenRepositoryDialogState state, Action dismiss)
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
            .Width(460)
            .Padding(new Thickness(20, 24, 20, 24))
            .CornerRadius(8f)
            .Background(DialogBg)
            .BorderColor(DialogBorder)
            .BorderWidth(1);
        panel.On<ClickEvent>(e => e.StopPropagation = true);

        var input = TextInput.New(state.Path)
            .PlaceholderText("Repository path")
            .OnTextChanged(value =>
            {
                state.Path = value;
                state.Error = null;
            })
            .FontSize(13f)
            .Height(Dimension.Px(30))
            .Background(GitClientColors.Background)
            .CornerRadius(4f)
            .BorderColor(GitClientColors.Border)
            .TextColor(GitClientColors.TextPrimary)
            .WithRef(state.InputRef);

        input.On<KeyDownEvent>(e =>
        {
            if (e.Key == KeyCode.Enter)
            {
                state.OnConfirm(state.Path);
                e.StopPropagation = true;
            }
            else if (e.Key == KeyCode.Escape)
            {
                dismiss();
                e.StopPropagation = true;
            }
        });

        var content = Flex.Column(gap: 14);
        content.AddChild(Text.New("Open Repository").FontSize(16f).FontWeight(FontWeight.Bold)
            .TextColor(GitClientColors.TextPrimary));
        content.AddChild(input);

        if (!string.IsNullOrWhiteSpace(state.Error))
            content.AddChild(Text.New(state.Error).FontSize(12f).TextColor(GitClientColors.StatusDeleted));

        var buttons = Flex.Row(gap: 8)
            .JustifyContent(JustifyContent.FlexEnd)
            .AlignItems(AlignItems.Center);
        buttons.AddChild(BuildButton("Cancel", GitClientColors.ButtonSecondary, GitClientColors.ButtonSecondaryHover, dismiss));
        buttons.AddChild(BuildButton("Open", GitClientColors.ButtonPrimary, GitClientColors.ButtonPrimaryHover,
            () => state.OnConfirm(state.Path)));

        content.AddChild(buttons);
        panel.AddChild(content);
        backdrop.AddChild(panel);
        return backdrop;
    }

    private static Element BuildButton(string label, Color bg, Color hoverBg, Action onClick)
    {
        var btn = Box.New()
            .Padding(new Thickness(8, 16, 8, 16))
            .CornerRadius(4f)
            .Background(bg)
            .Cursor(CursorType.Pointer)
            .Children(Text.New(label).FontSize(13f).FontWeight(FontWeight.SemiBold)
                .TextColor(Color.White));

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
