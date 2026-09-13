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
    /// <summary>A one-field modal prompt, optionally with a checkbox.
    /// <see cref="Validate"/> returns an error message to reject the value (kept
    /// open), or null to accept. <see cref="OnConfirm"/> receives the field value
    /// and the checkbox state (false when the prompt has no checkbox).
    /// A null <see cref="Placeholder"/> drops the field entirely — that is the
    /// confirm-only shape, see <see cref="OpenConfirm"/>.</summary>
    /// <param name="Info">An informational line under the body — the protected-branch rule
    /// GitLab enforces, drawn in a quiet box with a lock — so the card says what the server
    /// will say, before it is asked.</param>
    /// <param name="CheckboxDisabledReason">When set, the checkbox is drawn but cannot be
    /// ticked, and this suffix says why (<c>— not allowed on this branch</c>).</param>
    private sealed record DialogSpec(
        string Title,
        string? Body,
        string? Placeholder,
        string ConfirmLabel,
        Action<string, bool> OnConfirm,
        Func<string, string?>? Validate = null,
        string? CheckboxLabel = null,
        bool CheckboxInitial = true,
        string? Info = null,
        string? CheckboxDisabledReason = null);

    private void OpenDialog(DialogSpec spec, string initial = "")
    {
        _dialog = spec;
        _dialogValue = initial;
        _dialogChecked = spec.CheckboxDisabledReason is null && spec.CheckboxInitial;
        // Only ask for focus when there is a field to give it to: _dialogRef still
        // points at the last prompt's (unmounted) input, and focusing that corpse
        // takes the keyboard away from the root handler this dialog's Enter needs.
        _pendingDialogFocus = spec.Placeholder != null;
        MarkDirty();
    }

    /// <summary>A confirm-only modal: the same card without a field, for the
    /// destructive actions that have to say what they are about to destroy before
    /// they do it. Enter accepts (handled by the root key handler), Esc cancels.</summary>
    private void OpenConfirm(string title, string body, string confirmLabel, Action onConfirm) =>
        OpenDialog(new DialogSpec(title, body, null, confirmLabel, (_, _) => onConfirm()));

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
        var checkbox = _dialogChecked;
        CloseDialog();
        onConfirm(value, checkbox);
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

        // Key every child: the field and the checkbox are both optional, so without
        // keys the actions row can land at the index the field had and be patched
        // from it — a prompt that had a field and one that doesn't share this card.
        var title = Text.New(spec.Title).FontSize(19f).FontWeight(FontWeight.SemiBold);
        title.Key = "dialog-title";
        card.AddChild(title);
        if (!string.IsNullOrEmpty(spec.Body))
        {
            var body = Text.New(spec.Body).FontSize(13f).TextColor(Nocturne.On(0.72));
            body.Key = "dialog-body";
            card.AddChild(body);
        }

        // Input: bordered wrapper; the field itself is flush/borderless via theme.
        // A confirm-only prompt (no placeholder) has no field at all.
        if (spec.Placeholder is { } placeholder)
        {
            var inputBox = Flex.Row(gap: 0).AlignItems(AlignItems.Center)
                .Height(38).Padding(new Thickness(0, 12, 0, 12))
                .Background(Nocturne.Bg)
                .BorderColor(Nocturne.Divider).BorderWidth(1f)
                .CornerRadius(Nocturne.RadiusMd);
            var input = TextInput.New(_dialogValue);
            input.PlaceholderText(placeholder)
                .OnTextChanged(v => { _dialogValue = v; MarkDirty(); })
                .Height(Dimension.Px(30))
                .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
                .TextColor(Nocturne.Text)
                .WithRef(_dialogRef);
            input.On<KeyDownEvent>(DialogKey);
            inputBox.AddChild(input);
            inputBox.Key = "dialog-input";
            card.AddChild(inputBox);
        }

        // The rule the host enforces, when there is one: a quiet box, a lock, one sentence.
        if (spec.Info is { } info)
        {
            var box = Flex.Row(gap: 8).AlignItems(AlignItems.FlexStart)
                .Padding(new Thickness(8, 10, 8, 10)).CornerRadius(Nocturne.RadiusSm)
                .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f);
            box.AddChild(Ui.Icon(Icons.Lock, 13f, Nocturne.Accent300).FlexShrink(0));
            box.AddChild(Text.New(info).FontSize(12f).TextColor(Nocturne.On(0.78)).FlexShrink(1));
            box.Key = "dialog-info";
            box.AutomationId = "dialog-info";
            card.AddChild(box);
        }

        // Optional checkbox. Disabled, it is still drawn — a choice that has gone missing
        // reads as a bug, a choice that says why it is off reads as a rule.
        if (spec.CheckboxLabel is { } checkLabel)
        {
            if (spec.CheckboxDisabledReason is { } why)
            {
                var off = Flex.Row(gap: 9).AlignItems(AlignItems.FlexStart);
                off.AddChild(Ui.Icon(Icons.Square, 14f, Nocturne.On(0.3)).FlexShrink(0));
                off.AddChild(Text.New($"{checkLabel} {why}").FontSize(13f).TextColor(Nocturne.On(0.38)).FlexShrink(1));
                off.Key = "dialog-check-off";
                off.AutomationId = "dialog-check-off";
                card.AddChild(off);
            }
            else
            {
                var check = Checkbox.New(_dialogChecked, checkLabel)
                    .OnCheckedChanged(v => { _dialogChecked = v; MarkDirty(); });
                check.LabelGap = 9f;
                check.TextColor(Nocturne.On(0.82)).FontSize(13f);
                check.Key = "dialog-check";
                card.AddChild(check);
            }
        }

        // Actions.
        var actions = Flex.Row(gap: 8).JustifyContent(JustifyContent.FlexEnd).AlignItems(AlignItems.Center);
        actions.Key = "dialog-actions";
        actions.AddChild(Ui.SecondaryButton("Cancel", null, CloseDialog, 12.5f));
        actions.AddChild(Ui.PrimaryButton(spec.ConfirmLabel, null, ConfirmDialog, 12.5f));
        card.AddChild(actions);

        backdrop.AddChild(card);
        return backdrop;
    }

    // ── Prompts ──────────────────────────────────────────────────────────

    private void PromptNewBranch() => OpenDialog(new DialogSpec(
        "New branch",
        "Creates a branch at the current HEAD.",
        "feature/my-branch",
        "Create",
        (name, checkout) => DoCreateBranch(name, startPoint: null, checkout: checkout),
        Validate: BranchName.Validate,
        CheckboxLabel: "Check out after creating"), initial: _settings.LastBranchRoot);

    private void DoCreateBranch(string name, string? startPoint, bool checkout)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.CreateBranch(name, startPoint);
        if (!res.Success) { Flash(FirstLine(res.Stderr, "Create branch failed")); return; }
        RememberBranchRoot(name);
        if (checkout)
        {
            var co = repo.Checkout(name);
            if (!co.Success) { Flash(FirstLine(co.Stderr, $"Created {name}, checkout failed")); RefreshAll(); return; }
        }
        Flash($"Created {name}");
        RefreshAll();
    }

    /// <summary>Records the created branch's root so the next prompt opens on it. Recorded
    /// only after git accepted the name, so a rejected one cannot poison the seed.</summary>
    private void RememberBranchRoot(string name)
    {
        var root = name[..(name.LastIndexOf('/') + 1)];   // "" when there is no slash
        if (root == _settings.LastBranchRoot) return;
        _settings.LastBranchRoot = root;
        Persist();
    }

    private void PromptCreateBranchAt(string startPoint, string startLabel) => OpenDialog(new DialogSpec(
        "Create branch",
        $"Creates a branch at {startLabel}.",
        "feature/my-branch",
        "Create",
        (name, checkout) => DoCreateBranch(name, startPoint, checkout: checkout),
        Validate: BranchName.Validate,
        CheckboxLabel: "Check out after creating"), initial: _settings.LastBranchRoot);

    private void PromptCreateTagAt(string commit, string commitLabel) => OpenDialog(new DialogSpec(
        "Create tag",
        $"Tags {commitLabel}.",
        "v1.0.0",
        "Create tag",
        (name, _) =>
        {
            if (ActiveRepo is not { } repo) return;
            var res = repo.CreateTag(name, commit);
            Flash(res.Success ? $"Tagged {name}" : FirstLine(res.Stderr, "Create tag failed"));
            RefreshAll();
        },
        Validate: n => string.IsNullOrWhiteSpace(n) ? "Enter a tag name" : null));

    private void PromptRenameBranch(string oldName) => OpenDialog(new DialogSpec(
        "Rename branch",
        $"Renames {oldName}.",
        "new-name",
        "Rename",
        (newName, _) =>
        {
            if (ActiveRepo is not { } repo) return;
            var res = repo.RenameBranch(oldName, newName);
            Flash(res.Success ? $"Renamed to {newName}" : FirstLine(res.Stderr, "Rename failed"));
            RefreshAll();
        },
        Validate: BranchName.Validate), initial: oldName);

    private void PromptSetUpstream(string branch) => OpenDialog(new DialogSpec(
        "Set upstream",
        $"Sets the upstream tracking branch for {branch}.",
        "origin/" + branch,
        "Set upstream",
        (upstream, _) =>
        {
            if (ActiveRepo is not { } repo) return;
            var res = repo.SetUpstream(branch, upstream);
            Flash(res.Success ? $"Upstream set to {upstream}" : FirstLine(res.Stderr, "Set upstream failed"));
            RefreshAll();
        },
        Validate: u => string.IsNullOrWhiteSpace(u) ? "Enter an upstream ref" : null));
}
