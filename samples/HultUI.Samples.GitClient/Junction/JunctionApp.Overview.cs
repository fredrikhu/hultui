using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Junction;

public partial class JunctionApp
{
    private Element BuildOverviewView()
    {
        var ws = ActiveWorkspace;
        var repos = ActiveWorkspaceRepos;

        // Children are added directly to the ScrollView so each gets the viewport
        // width (a wrapping flex column would leave the width-distributing grid
        // with no definite width to divide, collapsing the cards to zero).
        var scroll = ScrollView.New().FlexGrow(1).Padding(new Thickness(26, 28, 26, 28));

        // Header: WORKSPACE kicker + name · right-aligned repo/dirty summary.
        var dirtyCount = repos.Count(p => SummaryFor(p).Dirty > 0);
        var header = Flex.Row(gap: 16).AlignItems(AlignItems.FlexEnd)
            .JustifyContent(JustifyContent.SpaceBetween);
        var left = Flex.Column(gap: 6);
        left.AddChild(Text.New("WORKSPACE").FontSize(11f).LetterSpacing(1.1f).TextColor(Nocturne.Accent300));
        left.AddChild(Text.New(ws?.Name ?? "Workspace").FontSize(22f).FontWeight(FontWeight.SemiBold));
        header.AddChild(left);
        header.AddChild(Text.New($"{repos.Count} repositor{(repos.Count == 1 ? "y" : "ies")} · {dirtyCount} with uncommitted work")
            .FontSize(12f).TextColor(Nocturne.On(0.55)));
        scroll.AddChild(header);
        scroll.AddChild(Box.New().Height(22).FlexShrink(0));

        if (repos.Count == 0)
        {
            scroll.AddChild(Box.New().Padding(new Thickness(40)).JustifyContent(JustifyContent.Center)
                .AlignItems(AlignItems.Center)
                .Children(Text.New("No repositories in this workspace")
                    .FontSize(13f).TextColor(Nocturne.On(0.45))));
            return scroll;
        }

        // 3-column grid: rows of 3 cards, last row padded with spacers so the
        // card widths stay uniform (no flex-wrap so the columns are exactly 3).
        for (var i = 0; i < repos.Count; i += 3)
        {
            var gridRow = Flex.Row(gap: 14).AlignItems(AlignItems.Stretch).FlexShrink(0);
            for (var j = 0; j < 3; j++)
            {
                if (i + j < repos.Count)
                    gridRow.AddChild(RepoCard(repos[i + j]));
                else
                    gridRow.AddChild(Box.New().FlexGrow(1).FlexBasis(Dimension.Px(0)));
            }
            scroll.AddChild(gridRow);
            if (i + 3 < repos.Count) scroll.AddChild(Box.New().Height(14).FlexShrink(0));
        }
        return scroll;
    }

    private Element RepoCard(string path)
    {
        var name = System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));
        var sum = SummaryFor(path);
        var (tileBg, tileFg) = RepoTile(path);
        var pb = BranchName.Parse(sum.Branch);

        var card = Flex.Column(gap: 12).FlexGrow(1).FlexBasis(Dimension.Px(0))
            .Padding(new Thickness(15))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg);

        // Header: initial tile + name/subtitle + "N changed" tag.
        var head = Flex.Row(gap: 10).AlignItems(AlignItems.Center);
        head.AddChild(Ui.InitialTile(name, tileBg, tileFg, 30f, 13f));
        var nameCol = Flex.Column(gap: 1).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0));
        nameCol.AddChild(Text.New(name).FontSize(15f).FontWeight(FontWeight.Medium)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        nameCol.AddChild(Text.New(RepoSubtitle(path)).FontSize(11.5f).TextColor(Nocturne.On(0.52))
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        head.AddChild(nameCol);
        if (sum.Dirty > 0)
            head.AddChild(Ui.Tag($"{sum.Dirty} changed", Nocturne.Accent900, Nocturne.Accent300, 10f));
        card.AddChild(head);

        // Branch inset.
        var inset = Flex.Row(gap: 7).AlignItems(AlignItems.Center)
            .Padding(new Thickness(7, 9, 7, 9)).CornerRadius(Nocturne.RadiusRow)
            .Background(Nocturne.Bg);
        inset.AddChild(Ui.Tag(pb.ChipLabel, pb.ChipBg, pb.ChipFg, 9.5f));
        inset.AddChild(Ui.Mono(pb.Title, 11.5f, Nocturne.On(0.72))
            .FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        card.AddChild(inset);

        // Footer: ↑ahead ↓behind ⌗ver · Open →.
        var footer = Flex.Row(gap: 0).AlignItems(AlignItems.Center)
            .JustifyContent(JustifyContent.SpaceBetween);
        var stats = Flex.Row(gap: 12).AlignItems(AlignItems.Center);
        stats.AddChild(Stat(Icons.ArrowUp, sum.Ahead.ToString()));
        stats.AddChild(Stat(Icons.ArrowDown, sum.Behind.ToString()));
        if (sum.Version.Length > 0)
            stats.AddChild(Stat(Icons.Tag, sum.Version));
        footer.AddChild(stats);
        footer.AddChild(OpenRepoButton(path));
        card.AddChild(footer);

        return card;
    }

    private static Element Stat(string glyph, string value) =>
        Flex.Row(gap: 3).AlignItems(AlignItems.Center).Children(
            Ui.Icon(glyph, 11f, Nocturne.On(0.50)),
            Text.New(value).FontSize(11.5f).TextColor(Nocturne.On(0.55)));

    private Element OpenRepoButton(string path)
    {
        var b = Flex.Row(gap: 4).AlignItems(AlignItems.Center)
            .Padding(new Thickness(3, 5, 3, 5)).CornerRadius(Nocturne.RadiusRow)
            .Cursor(CursorType.Pointer);
        b.AddChild(Text.New("Open").FontSize(12f).FontWeight(FontWeight.Medium).TextColor(Nocturne.Accent));
        b.AddChild(Ui.Icon(Icons.ArrowRight, 12f, Nocturne.Accent));
        b.OnClick(_ => OpenRepo(path));
        return b.HoverBg(Nocturne.Alpha(Nocturne.Accent, 0.10), Color.Transparent);
    }

    private void OpenRepo(string path)
    {
        SelectRepo(path);
        SetView(JunctionView.History);
    }
}
