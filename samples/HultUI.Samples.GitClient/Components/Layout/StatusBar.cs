using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Components.Layout;

public static class StatusBar
{
    public static Element Build(
        string branchName,
        int ahead,
        int behind,
        int changedFiles,
        Action onFetch,
        Action onPull,
        Action onPush,
        Action? onForcePush = null,
        Action? onPullFrom = null,
        Action? onPushTo = null,
        bool isMerging = false,
        Action? onMergeAbort = null,
        bool isRebasing = false,
        Action? onRebaseAbort = null,
        Action? onRebaseContinue = null,
        bool isBisecting = false,
        Action? onBisectGood = null,
        Action? onBisectBad = null,
        Action? onBisectSkip = null,
        Action? onBisectReset = null,
        string? message = null,
        bool messageIsError = false)
    {
        var aheadBehind = "";
        if (ahead > 0) aheadBehind += $" \u2191{ahead}";
        if (behind > 0) aheadBehind += $" \u2193{behind}";

        var children = new List<Element>
        {
            // Branch indicator
            Text.New("\u25CF").FontSize(10f).TextColor(GitClientColors.BranchCurrent),
            Text.New(branchName).FontSize(12f).FontWeight(FontWeight.SemiBold)
                .TextColor(GitClientColors.TextPrimary),

            // Ahead/behind
            aheadBehind.Length > 0
                ? Text.New(aheadBehind.Trim()).FontSize(12f).TextColor(GitClientColors.TextSecondary)
                : Box.New().Width(0),

            // Separator
            Box.New().Width(1).Height(16).Background(GitClientColors.Border),

            // Changed files
            Text.New($"{changedFiles} changed").FontSize(12f)
                .TextColor(changedFiles > 0 ? GitClientColors.StatusModified : GitClientColors.TextMuted),
        };

        // Merge-in-progress indicator
        if (isMerging)
        {
            children.Add(Box.New().Width(1).Height(16).Background(GitClientColors.Border));
            children.Add(
                Text.New("MERGING").FontSize(10f).FontWeight(FontWeight.Bold)
                    .TextColor(GitClientColors.StatusDeleted)
            );
            if (onMergeAbort != null)
                children.Add(ActionButton("Abort Merge", onMergeAbort));
        }

        // Rebase-in-progress indicator
        if (isRebasing)
        {
            children.Add(Box.New().Width(1).Height(16).Background(GitClientColors.Border));
            children.Add(
                Text.New("REBASING").FontSize(10f).FontWeight(FontWeight.Bold)
                    .TextColor(GitClientColors.StatusDeleted)
            );
            if (onRebaseContinue != null)
                children.Add(ActionButton("Continue", onRebaseContinue));
            if (onRebaseAbort != null)
                children.Add(ActionButton("Abort Rebase", onRebaseAbort));
        }

        // Bisect-in-progress indicator
        if (isBisecting)
        {
            children.Add(Box.New().Width(1).Height(16).Background(GitClientColors.Border));
            children.Add(
                Text.New("BISECTING").FontSize(10f).FontWeight(FontWeight.Bold)
                    .TextColor(GitClientColors.StatusModified)
            );
            if (onBisectGood != null)
                children.Add(ActionButton("Good", onBisectGood));
            if (onBisectBad != null)
                children.Add(ActionButton("Bad", onBisectBad));
            if (onBisectSkip != null)
                children.Add(ActionButton("Skip", onBisectSkip));
            if (onBisectReset != null)
                children.Add(ActionButton("Reset", onBisectReset));
        }

        // Spacer
        children.Add(Box.New().FlexGrow(1));

        // Operation result message
        if (message != null)
        {
            var msgColor = messageIsError ? GitClientColors.StatusDeleted : GitClientColors.BranchCurrent;
            var icon = messageIsError ? "\u2717 " : "\u2713 ";
            children.Add(
                Text.New(icon + message).FontSize(11f).TextColor(msgColor)
                    .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
            );
            children.Add(Box.New().Width(1).Height(16).Background(GitClientColors.Border));
        }

        // Action buttons
        children.Add(ActionButton("Fetch", onFetch));
        children.Add(ButtonWithDropdown("Pull", onPull, onPullFrom));
        children.Add(PushButton(onPush, onForcePush, onPushTo));

        return Flex.Row(gap: 16)
            .AlignItems(AlignItems.Center)
            .Height(Dimension.Px(32))
            .Background(GitClientColors.StatusBar)
            .Padding(new Thickness(0, 12, 0, 12))
            .FlexShrink(0)
            .Children(children.ToArray());
    }

    private static Element ActionButton(string label, Action onClick)
    {
        var btn = Box.New()
            .Padding(new Thickness(4, 10, 4, 10))
            .CornerRadius(4f)
            .Background(GitClientColors.ButtonSecondary)
            .Cursor(CursorType.Pointer)
            .Children(
                Text.New(label).FontSize(11f).FontWeight(FontWeight.SemiBold)
                    .TextColor(GitClientColors.TextPrimary)
            );

        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondaryHover);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondary);
        btn.On<ClickEvent>(_ => onClick());

        return btn;
    }

    private static Element ButtonWithDropdown(string label, Action onClick, Action? onDropdown)
    {
        var row = Flex.Row(gap: 0).AlignItems(AlignItems.Stretch);

        var btn = Box.New()
            .Padding(new Thickness(4, 10, 4, 10))
            .CornerRadius(onDropdown != null ? new CornerRadius(4, 0, 0, 4) : new CornerRadius(4))
            .Background(GitClientColors.ButtonSecondary)
            .Cursor(CursorType.Pointer)
            .Children(
                Text.New(label).FontSize(11f).FontWeight(FontWeight.SemiBold)
                    .TextColor(GitClientColors.TextPrimary)
            );
        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondaryHover);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondary);
        btn.On<ClickEvent>(_ => onClick());
        row.AddChild(btn);

        if (onDropdown != null)
        {
            var arrow = Box.New()
                .Padding(new Thickness(4, 4, 4, 4))
                .CornerRadius(new CornerRadius(0, 4, 4, 0))
                .Background(GitClientColors.ButtonSecondary)
                .Cursor(CursorType.Pointer)
                .JustifyContent(JustifyContent.Center)
                .AlignItems(AlignItems.Center)
                .Children(
                    Text.New("\u25BE").FontSize(10f).TextColor(GitClientColors.TextMuted)
                );
            arrow.On<PointerEnterEvent>(_ => arrow.BackgroundColor = GitClientColors.ButtonSecondaryHover);
            arrow.On<PointerLeaveEvent>(_ => arrow.BackgroundColor = GitClientColors.ButtonSecondary);
            arrow.On<ClickEvent>(_ => onDropdown());
            row.AddChild(arrow);
        }

        return row;
    }

    private static Element PushButton(Action onPush, Action? onForcePush, Action? onPushTo)
    {
        var row = Flex.Row(gap: 0).AlignItems(AlignItems.Stretch);

        var btn = Box.New()
            .Padding(new Thickness(4, 10, 4, 10))
            .CornerRadius(onPushTo != null ? new CornerRadius(4, 0, 0, 4) : new CornerRadius(4))
            .Background(GitClientColors.ButtonSecondary)
            .Cursor(CursorType.Pointer)
            .Tooltip("Hold Shift to force push")
            .Children(
                Text.New("Push").FontSize(11f).FontWeight(FontWeight.SemiBold)
                    .TextColor(GitClientColors.TextPrimary)
            );
        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondaryHover);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondary);
        btn.On<ClickEvent>(e =>
        {
            if (e.Modifiers.HasFlag(KeyModifiers.Shift) && onForcePush != null)
                onForcePush();
            else
                onPush();
        });
        row.AddChild(btn);

        if (onPushTo != null)
        {
            var arrow = Box.New()
                .Padding(new Thickness(4, 4, 4, 4))
                .CornerRadius(new CornerRadius(0, 4, 4, 0))
                .Background(GitClientColors.ButtonSecondary)
                .Cursor(CursorType.Pointer)
                .JustifyContent(JustifyContent.Center)
                .AlignItems(AlignItems.Center)
                .Children(
                    Text.New("\u25BE").FontSize(10f).TextColor(GitClientColors.TextMuted)
                );
            arrow.On<PointerEnterEvent>(_ => arrow.BackgroundColor = GitClientColors.ButtonSecondaryHover);
            arrow.On<PointerLeaveEvent>(_ => arrow.BackgroundColor = GitClientColors.ButtonSecondary);
            arrow.On<ClickEvent>(_ => onPushTo());
            row.AddChild(arrow);
        }

        return row;
    }
}
