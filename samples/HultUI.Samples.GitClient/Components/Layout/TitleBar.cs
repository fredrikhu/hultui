using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Components.Layout;

public static class TitleBar
{
    public static Element Build(
        Window window,
        string branchName,
        int ahead,
        int behind,
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
        return Flex.Row(gap: 0)
            .WindowDrag()
            .AlignItems(AlignItems.Center)
            .Height(Dimension.Px(38))
            .Background(GitClientColors.TitleBar)
            .Padding(new Thickness(0, 8, 0, 8))
            .FlexShrink(0)
            .Children(
                BuildLeft(onFetch, onPull, onPush, onForcePush, onPullFrom, onPushTo,
                          isMerging, onMergeAbort,
                          isRebasing, onRebaseAbort, onRebaseContinue,
                          isBisecting, onBisectGood, onBisectBad, onBisectSkip, onBisectReset),
                BuildCenter(branchName, ahead, behind),
                BuildRight(window, message, messageIsError)
            );
    }

    private static Element BuildLeft(
        Action onFetch, Action onPull, Action onPush,
        Action? onForcePush, Action? onPullFrom, Action? onPushTo,
        bool isMerging, Action? onMergeAbort,
        bool isRebasing, Action? onRebaseAbort, Action? onRebaseContinue,
        bool isBisecting, Action? onBisectGood, Action? onBisectBad, Action? onBisectSkip, Action? onBisectReset)
    {
        var children = new List<Element>
        {
            ActionButton("Fetch", onFetch),
            ButtonWithDropdown("Pull", onPull, onPullFrom),
            PushButton(onPush, onForcePush, onPushTo),
        };

        if (isMerging)
        {
            children.Add(Separator());
            children.Add(Text.New("MERGING").FontSize(10f).FontWeight(FontWeight.Bold)
                .TextColor(GitClientColors.StatusDeleted));
            if (onMergeAbort != null)
                children.Add(ActionButton("Abort Merge", onMergeAbort));
        }

        if (isRebasing)
        {
            children.Add(Separator());
            children.Add(Text.New("REBASING").FontSize(10f).FontWeight(FontWeight.Bold)
                .TextColor(GitClientColors.StatusDeleted));
            if (onRebaseContinue != null)
                children.Add(ActionButton("Continue", onRebaseContinue));
            if (onRebaseAbort != null)
                children.Add(ActionButton("Abort Rebase", onRebaseAbort));
        }

        if (isBisecting)
        {
            children.Add(Separator());
            children.Add(Text.New("BISECTING").FontSize(10f).FontWeight(FontWeight.Bold)
                .TextColor(GitClientColors.StatusModified));
            if (onBisectGood != null) children.Add(ActionButton("Good", onBisectGood));
            if (onBisectBad != null) children.Add(ActionButton("Bad", onBisectBad));
            if (onBisectSkip != null) children.Add(ActionButton("Skip", onBisectSkip));
            if (onBisectReset != null) children.Add(ActionButton("Reset", onBisectReset));
        }

        return Flex.Row(gap: 6)
            .FlexGrow(1)
            .FlexBasis(Dimension.Px(0))
            .AlignItems(AlignItems.Center)
            .JustifyContent(JustifyContent.FlexStart)
            .Children(children.ToArray());
    }

    private static Element BuildCenter(string branchName, int ahead, int behind)
    {
        var aheadBehind = "";
        if (ahead > 0) aheadBehind += $"  ↑{ahead}";
        if (behind > 0) aheadBehind += $"  ↓{behind}";

        return Flex.Row(gap: 0)
            .FlexGrow(1)
            .FlexBasis(Dimension.Px(0))
            .AlignItems(AlignItems.Center)
            .JustifyContent(JustifyContent.Center)
            .Children(
                Text.New("HultGit").FontSize(13f).FontWeight(FontWeight.Bold)
                    .TextColor(GitClientColors.SelectionBorder),
                Text.New($"  →  {branchName}").FontSize(13f).FontWeight(FontWeight.SemiBold)
                    .TextColor(GitClientColors.BranchCurrent),
                aheadBehind.Length > 0
                    ? Text.New(aheadBehind).FontSize(12f).TextColor(GitClientColors.TextSecondary)
                    : Box.New().Width(0)
            );
    }

    private static Element BuildRight(Window window, string? message, bool messageIsError)
    {
        var children = new List<Element>();

        if (message != null)
        {
            var msgColor = messageIsError ? GitClientColors.StatusDeleted : GitClientColors.BranchCurrent;
            var icon = messageIsError ? "✗ " : "✓ ";
            children.Add(Text.New(icon + message).FontSize(11f).TextColor(msgColor)
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
            children.Add(Box.New().Width(8));
        }

        children.Add(WindowButton("−", GitClientColors.SurfaceHover, () => window.Minimize()));
        children.Add(WindowButton("□", GitClientColors.SurfaceHover, () => window.ToggleMaximize()));
        children.Add(WindowButton("×", new Color(200, 60, 60), () => window.Close()));

        return Flex.Row(gap: 0)
            .FlexGrow(1)
            .FlexBasis(Dimension.Px(0))
            .AlignItems(AlignItems.Center)
            .JustifyContent(JustifyContent.FlexEnd)
            .Children(children.ToArray());
    }

    private static Element Separator() =>
        Box.New().Width(1).Height(16).Background(GitClientColors.Border)
          .Margin(new Thickness(0, 4, 0, 4));

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
                    Text.New("▾").FontSize(10f).TextColor(GitClientColors.TextMuted)
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
                    Text.New("▾").FontSize(10f).TextColor(GitClientColors.TextMuted)
                );
            arrow.On<PointerEnterEvent>(_ => arrow.BackgroundColor = GitClientColors.ButtonSecondaryHover);
            arrow.On<PointerLeaveEvent>(_ => arrow.BackgroundColor = GitClientColors.ButtonSecondary);
            arrow.On<ClickEvent>(_ => onPushTo());
            row.AddChild(arrow);
        }

        return row;
    }

    private static Element WindowButton(string label, Color hoverColor, Action onClick)
    {
        var btn = Box.New()
            .Width(Dimension.Px(36)).Height(Dimension.Px(28))
            .CornerRadius(4f)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .Children(
                Text.New(label).FontSize(14f).TextColor(GitClientColors.TextSecondary)
            );

        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = hoverColor);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = Color.Transparent);
        btn.On<ClickEvent>(_ => onClick());

        return btn;
    }
}
