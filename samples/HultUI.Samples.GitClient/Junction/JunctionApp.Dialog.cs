using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Junction;

public partial class JunctionApp
{
    /// <summary>A one-field modal prompt. <see cref="Validate"/> returns an error
    /// message to reject the value (kept open), or null to accept.</summary>
    private sealed record DialogSpec(
        string Title,
        string? Body,
        string Placeholder,
        string ConfirmLabel,
        Action<string> OnConfirm,
        Func<string, string?>? Validate = null);

    private void OpenDialog(DialogSpec spec, string initial = "")
    {
        _dialog = spec;
        _dialogValue = initial;
        _pendingDialogFocus = true;
        MarkDirty();
    }

    private void CloseDialog()
    {
        _dialog = null;
        _dialogValue = "";
        MarkDirty();
    }

    private void ConfirmDialog()
    {
        if (_dialog is not { } spec) return;
        var value = _dialogValue.Trim();
        if (spec.Validate?.Invoke(value) is { } error) { Flash(error); return; }
        var onConfirm = spec.OnConfirm;
        CloseDialog();
        onConfirm(value);
    }

    private void DialogKey(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Enter: ConfirmDialog(); e.StopPropagation = true; break;
            case KeyCode.Escape: CloseDialog(); e.StopPropagation = true; break;
        }
    }

    private Element BuildDialog(DialogSpec spec)
    {
        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(new Color(0, 0, 0, 140))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Padding(new Thickness(24))
            .ZIndex(45);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; CloseDialog(); });

        var card = Flex.Column(gap: 14)
            .Width(440)
            .Padding(new Thickness(20))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg);
        card.On<ClickEvent>(e => e.StopPropagation = true);

        card.AddChild(Text.New(spec.Title).FontSize(19f).FontWeight(FontWeight.SemiBold));
        if (!string.IsNullOrEmpty(spec.Body))
            card.AddChild(Text.New(spec.Body).FontSize(13f).TextColor(Nocturne.On(0.72)));

        // Input: bordered wrapper; the field itself is flush/borderless via theme.
        var inputBox = Flex.Row(gap: 0).AlignItems(AlignItems.Center)
            .Height(38).Padding(new Thickness(0, 12, 0, 12))
            .Background(Nocturne.Bg)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusMd);
        var input = TextInput.New(_dialogValue);
        input.PlaceholderText(spec.Placeholder)
            .OnTextChanged(v => { _dialogValue = v; MarkDirty(); })
            .Height(Dimension.Px(30))
            .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
            .TextColor(Nocturne.Text)
            .WithRef(_dialogRef);
        input.On<KeyDownEvent>(DialogKey);
        inputBox.AddChild(input);
        card.AddChild(inputBox);

        // Actions.
        var actions = Flex.Row(gap: 8).JustifyContent(JustifyContent.FlexEnd).AlignItems(AlignItems.Center);
        actions.AddChild(Ui.SecondaryButton("Cancel", null, CloseDialog, 12.5f));
        actions.AddChild(Ui.PrimaryButton(spec.ConfirmLabel, null, ConfirmDialog, 12.5f));
        card.AddChild(actions);

        backdrop.AddChild(card);
        return backdrop;
    }

    // ── Prompts ──────────────────────────────────────────────────────────

    private void PromptNewBranch() => OpenDialog(new DialogSpec(
        "New branch",
        "Creates a branch at the current HEAD and checks it out.",
        "feature/my-branch",
        "Create",
        name => DoCreateBranch(name, startPoint: null, checkout: true),
        Validate: NonEmptyBranch));

    private static string? NonEmptyBranch(string name) =>
        string.IsNullOrWhiteSpace(name) ? "Enter a branch name" : null;

    private void DoCreateBranch(string name, string? startPoint, bool checkout)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.CreateBranch(name, startPoint);
        if (!res.Success) { Flash(FirstLine(res.Stderr, "Create branch failed")); return; }
        if (checkout)
        {
            var co = repo.Checkout(name);
            if (!co.Success) { Flash(FirstLine(co.Stderr, $"Created {name}, checkout failed")); RefreshAll(); return; }
        }
        Flash($"Created {name}");
        RefreshAll();
    }
}
