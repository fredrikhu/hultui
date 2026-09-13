using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.GitLab;
using Junction.Theme;

namespace Junction;

/// <summary>
/// Clone from GitLab — design §I: the clone modal's URL / GitLab switch. The GitLab side
/// lists the projects the signed-in user is at least a developer on (search as you type,
/// one account chip per host when there are several), and picking one fills the URL the
/// ordinary clone then runs with. Six skeleton rows while the first page loads; a sentence
/// when there is nothing.
/// </summary>
public partial class JunctionApp
{
    private readonly KeyedAsyncLoader<(string Host, string Search), Hosted<IReadOnlyList<GlProject>>> _membershipLoaders =
        new(Hosted<IReadOnlyList<GlProject>>.Empty([]), capacity: 16);

    private Hosted<IReadOnlyList<GlProject>> MembershipProjectsFor(string host, string search)
    {
        if (_gitlab.ClientFor(host) is not { } client) return Hosted<IReadOnlyList<GlProject>>.Empty([]);
        var loader = _membershipLoaders.For((host, search));
        var prev = loader.Value;
        loader.Load($"{host}#{search}", () =>
        {
            var r = Sync(client.ListMembershipProjectsAsync(30, search.Length > 0 ? search : null, maxPages: 2));
            return r.Ok ? new Hosted<IReadOnlyList<GlProject>>(r.Value, DateTimeOffset.UtcNow, null, true) : Keep(prev, r.Failure!);
        }, NoteAuthFailure<IReadOnlyList<GlProject>>(host));
        return loader.Value;
    }

    private bool MembershipFirstLoading(string host, string search) =>
        _membershipLoaders.For((host, search)) is { IsLoading: true, Value.Loaded: false };

    /// <summary>The URL / GitLab segmented switch at the top of the clone modal.</summary>
    private Element CloneSourceSwitch(ModalForm f)
    {
        var seg = OptionSegmented(["URL", "GitLab"], f.FromGitLab ? 1 : 0, i =>
        {
            f.FromGitLab = i == 1;
            if (f.FromGitLab && f.CloneHost.Length == 0) f.CloneHost = _gitlab.All[0].Host;
            Rerender();   // a tracked rebuild: the project loader is new and has to be subscribed
        }, "Clone source");
        seg.AutomationId = "clone-source";
        return seg;
    }

    private Element BuildCloneFromGitLab(ModalForm f)
    {
        var col = Flex.Column(gap: 10);
        if (f.CloneHost.Length == 0) f.CloneHost = _gitlab.All[0].Host;

        if (_gitlab.All.Count > 1)
        {
            var hosts = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
            foreach (var account in _gitlab.All)
            {
                var host = account.Host;
                hosts.AddChild(OptionChip($"{account.Username} on {host}", f.CloneHost == host, () => { f.CloneHost = host; f.CloneProject = null; Rerender(); }));
            }
            col.AddChild(ModalField("Account", hosts));
        }

        var search = ModalInput(f.CloneSearch, "Search projects you are a member of…", v => { f.CloneSearch = v; Rerender(); }, _modalNameRef, mono: false);
        col.AddChild(ModalField("Project", search));

        // A fixed height, not a maximum: every keystroke re-keys the search, and a list that
        // changed height with each answer moved the fields under it and grew the dialog a
        // scrollbar it lost a moment later.
        var list = ScrollView.New().Height(260).FlexShrink(0)
            .BorderColor(Nocturne.Divider).BorderWidth(1f).CornerRadius(Nocturne.RadiusMd);
        list.AutomationId = "clone-projects";
        var host2 = f.CloneHost;
        var term = f.CloneSearch.Trim();
        var projects = MembershipProjectsFor(host2, term);
        if (projects.Loaded) f.CloneLastProjects = projects.Value;
        // Until this search answers, the last answer stays on screen, dimmed. Skeleton rows
        // are for the very first load only, when there is nothing to keep showing.
        bool waiting = !projects.Loaded;
        var showing = projects.Loaded ? projects.Value : f.CloneLastProjects;
        if (waiting && showing.Count == 0 && projects.Failure is null)
        {
            for (int i = 0; i < 6; i++)
                list.AddChild(Flex.Row(gap: 10).AlignItems(AlignItems.Center).Padding(new Thickness(9, 12, 9, 12)).Children(
                    Box.New().Width(26).Height(26).CornerRadius(Nocturne.RadiusRow).Background(Nocturne.Neutral800),
                    Box.New().Width(180 + (i % 3) * 40).Height(12).CornerRadius(6f).Background(Nocturne.Neutral800)));
        }
        else if (showing.Count == 0)
        {
            list.AddChild(Text.New(projects.Failure is { } failure
                    ? failure.UserMessage(host2, "Projects")
                    : term.Length > 0 ? $"No project on {host2} matches \"{term}\"."
                    : $"No projects on {host2} where you are at least a developer.")
                .FontSize(12f).TextColor(Nocturne.On(0.5)).Padding(new Thickness(14, 12, 14, 12)));
        }
        else
        {
            foreach (var p in showing)
            {
                var row = CloneProjectRow(f, p);
                if (waiting) row.Opacity(0.55f);
                list.AddChild(row);
            }
        }
        col.AddChild(list);

        if (f.CloneProject is { } chosen)
        {
            var line = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
            line.AddChild(Ui.Icon(Icons.CheckCircle, 12f, Nocturne.Accent300));
            line.AddChild(Ui.Mono(chosen.HttpUrlToRepo ?? chosen.WebUrl ?? chosen.PathWithNamespace, 11.5f, Nocturne.On(0.62))
                .FlexShrink(1).MaxLines(1).OverflowMode(TextOverflow.Ellipsis));
            line.AutomationId = "clone-chosen";
            col.AddChild(line);
        }
        return col;
    }

    private Element CloneProjectRow(ModalForm f, GlProject p)
    {
        bool selected = f.CloneProject?.Id == p.Id;
        var normalBg = selected ? Nocturne.Accent900 : Color.Transparent;
        var row = Flex.Row(gap: 10).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 12, 8, 12)).Background(normalBg).Cursor(CursorType.Pointer);
        row.Key = "clone-project:" + p.Id + (selected ? "#s" : "");
        row.AccessibleName = p.PathWithNamespace;
        var (bg, fg) = Ui.RepoTileColors(p.PathWithNamespace);
        row.AddChild(Ui.InitialTile(p.Name, bg, fg, 26f, 10f).FlexShrink(0));
        var text = Flex.Column(gap: 2).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0));
        text.AddChild(Ui.Mono(p.PathWithNamespace, 12f, Nocturne.Text).MaxLines(1).OverflowMode(TextOverflow.Ellipsis));
        var kind = p.Namespace?.Kind == "group" ? "group" : "personal";
        var activity = p.LastActivityAt is { } at ? $"active {RelativeTime(at)}" : "";
        text.AddChild(Text.New(activity.Length > 0 ? $"{activity} · {kind}" : kind).FontSize(10.5f).TextColor(Nocturne.On(0.45)));
        row.AddChild(text);
        if (p.Visibility is { Length: > 0 } vis)
            row.AddChild(Ui.Tag(vis, Nocturne.Neutral800, Nocturne.Neutral100, 9f));
        row.OnClick(e =>
        {
            e.StopPropagation = true;
            f.CloneProject = p;
            f.Url = p.HttpUrlToRepo ?? p.WebUrl ?? "";
            MarkDirty();
        });
        if (!selected) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }
}
