using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Git;
using Junction.GitLab;
using Junction.Theme;

namespace Junction;

/// <summary>
/// Which repositories are on GitLab, and the small marks that say so: the logo after a repo's
/// name in the sidebar, on the Overview card and in the title bar, the "Hosted on" line in
/// Repository settings, and the first-run strip that offers to connect. Every hosted surface
/// in the app keys off <see cref="GitLabActive"/>: a repository whose remote is not on GitLab,
/// or whose host has no account, must look exactly like the app did before any of this.
/// </summary>
public partial class JunctionApp
{
    /// <summary>
    /// The GitLab project a repository's remote points at, or null. The remote is
    /// <c>origin</c>, or the one chosen in Repository settings, or the only one there is;
    /// its URL has to parse as a hosted repository and its host has to read as GitLab —
    /// gitlab.com, anything with "gitlab" in the name, or a host an account exists for.
    /// Anything else (GitHub, a bare server) is not GitLab-backed, and gets no mark.
    /// </summary>
    private GitLabRemote? GitLabRemoteFor(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var remotes = SummaryFor(path).Remotes;
        if (remotes.Count == 0) return null;

        GitRemote? remote = null;
        if (_settings.RepoHostingRemote.TryGetValue(NormPath(path), out var chosen))
            remote = remotes.FirstOrDefault(r => r.Name == chosen);
        remote ??= remotes.FirstOrDefault(r => r.Name == "origin");
        remote ??= remotes.Count == 1 ? remotes[0] : null;
        if (remote is null) return null;

        var parsed = GitLabRemote.TryParse(remote.FetchUrl);
        if (parsed is null || !LooksLikeGitLab(parsed.Host)) return null;
        return parsed;
    }

    private bool LooksLikeGitLab(string host) =>
        host == "gitlab.com"
        || host.EndsWith(".gitlab.com", StringComparison.Ordinal)
        || host.Contains("gitlab", StringComparison.OrdinalIgnoreCase)
        || _gitlab.HasAccount(host);

    /// <summary>Project detected <i>and</i> an account for its host: the gate every hosted
    /// surface stands behind.</summary>
    private bool GitLabActive(string path) =>
        GitLabRemoteFor(path) is { } r && _gitlab.HasAccount(r.Host);

    /// <summary>
    /// The 12px logo after a repository's name: 55% when the host has an account, 26% when
    /// it does not — present either way, so a repo that could be connected is told apart
    /// from one that is not on GitLab at all.
    /// </summary>
    private Element GitLabMark(GitLabRemote remote, float size = 12f)
    {
        bool connected = _gitlab.HasAccount(remote.Host);
        // The glyph itself, not a box around it: the row's AlignItems centres it beside the
        // name it follows.
        var mark = Ui.Icon(Icons.GitLabLogo, size, Nocturne.On(connected ? 0.55 : 0.26))
            .FlexShrink(0)
            .Tooltip(connected
                ? $"{remote.ProjectPath} on {remote.Host}"
                : $"Connect an account for {remote.Host} to use GitLab features");
        mark.AutomationId = "gitlab-mark";
        return mark;
    }

    /// <summary>The mark for <paramref name="path"/>, or null when it is not GitLab-backed.</summary>
    private Element? GitLabMarkFor(string path, float size = 12f) =>
        GitLabRemoteFor(path) is { } r ? GitLabMark(r, size) : null;

    // ── First-run strip ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The one-line offer above the History list: this repository is on a GitLab host the
    /// app has no account for. Dismissed once per host, remembered, and forgotten again on
    /// sign-out so a removed account brings the offer back. Never a modal, never repeated.
    /// </summary>
    private Element? BuildFirstRunStrip()
    {
        if (!HasActiveRepo || GitLabRemoteFor(_activeRepoPath) is not { } remote) return null;
        if (_gitlab.HasAccount(remote.Host)) return null;
        if (_settings.GitLabDismissedHosts.Contains(remote.Host, StringComparer.OrdinalIgnoreCase)) return null;

        var repoName = System.IO.Path.GetFileName(_activeRepoPath.TrimEnd('/', '\\'));
        var strip = Flex.Row(gap: 10).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(7, 12, 7, 16))
            .Background(Nocturne.Alpha(Nocturne.Accent, 0.06))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        strip.AutomationId = "gitlab-first-run";

        strip.AddChild(Ui.Icon(Icons.GitLabLogo, 14f, Nocturne.Accent300));
        strip.AddChild(Text.New($"Connect to {remote.Host} to see merge requests and pipelines for {repoName}.")
            .FontSize(12f).TextColor(Nocturne.On(0.78)).FlexGrow(1)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        var connect = Ui.GhostButton("Connect", null, () => OpenOptions(OptionsSection.GitLabAccounts));
        connect.AccessibleName = "Connect";
        strip.AddChild(connect);
        var dismiss = Box.New().Width(24).Height(24).FlexShrink(0).CornerRadius(Nocturne.RadiusSm)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer).Tooltip("Dismiss")
            .Children(Ui.Icon(Icons.X, 12f, Nocturne.On(0.6)));
        dismiss.AccessibleName = "Dismiss";
        dismiss.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
        dismiss.On<ClickEvent>(e => { e.StopPropagation = true; DismissFirstRun(remote.Host); });
        dismiss.HoverBg(Nocturne.Neutral900, Color.Transparent);
        strip.AddChild(dismiss);
        return strip;
    }

    private void DismissFirstRun(string host)
    {
        if (!_settings.GitLabDismissedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            _settings.GitLabDismissedHosts.Add(host.ToLowerInvariant());
        Persist();
        MarkDirty();
    }

    private void ForgetFirstRunDismissal(string host)
    {
        if (_settings.GitLabDismissedHosts.RemoveAll(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase)) > 0)
            Persist();
    }

    // ── Repository settings: "Hosted on" ─────────────────────────────────────────────

    /// <summary>
    /// The read-only line in Repository settings naming the detected project, with a way
    /// to pick another remote when the one that points at GitLab is not <c>origin</c>, and
    /// a pointer to Options when the host has no account.
    /// </summary>
    private Element BuildHostedOnField(string path, GitLabRemote remote)
    {
        bool connected = _gitlab.HasAccount(remote.Host);
        var remotes = SummaryFor(path).Remotes;
        var chosen = _settings.RepoHostingRemote.GetValueOrDefault(NormPath(path)) ?? "origin";

        var col = Flex.Column(gap: 6);
        col.AddChild(Text.New("Hosted on").FontSize(11f).TextColor(Nocturne.On(0.55)));

        var box = Flex.Row(gap: 9).AlignItems(AlignItems.Center)
            .Padding(new Thickness(9, 12, 9, 12)).CornerRadius(Nocturne.RadiusMd)
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f);
        box.AddChild(Ui.Icon(Icons.GitLabLogo, 14f, connected ? Nocturne.Accent300 : Nocturne.On(0.35)));
        box.AddChild(Ui.Mono(remote.ProjectPath, 12f, Nocturne.Text)
            .FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        var where = Flex.Row(gap: 4).AlignItems(AlignItems.Center).FlexShrink(1);
        where.AddChild(Text.New($"on {remote.Host} · detected from").FontSize(12f).TextColor(Nocturne.On(0.45)));
        where.AddChild(Ui.Mono(chosen, 11.5f, Nocturne.On(0.45)));
        box.AddChild(where);
        box.AddChild(Box.New().FlexGrow(1));
        if (remotes.Count > 1)
        {
            var pick = Ui.GhostButton("Use another remote…", null, () => { }, 11.5f);
            pick.On<ClickEvent>(e =>
            {
                e.StopPropagation = true;
                var b = e.CurrentTarget.Layout.AbsoluteBorderBox;
                _popoverX = b.X;
                _popoverY = b.Y + b.Height + 4;
                _popover = remotes.Select(r => Item(Icons.Cloud,
                    r.Name == chosen ? $"{r.Name}  ✓" : r.Name,
                    () => UseHostingRemote(path, r.Name))).ToList();
                MarkDirty();
            });
            box.AddChild(pick);
        }
        col.AddChild(box);

        if (!connected)
        {
            var line = Flex.Row(gap: 4).AlignItems(AlignItems.Center);
            line.AddChild(Text.New($"No account for {remote.Host} —").FontSize(11f).TextColor(Nocturne.On(0.5)));
            var link = Text.New("add one in Options").FontSize(11f).TextColor(Nocturne.Accent300)
                .Cursor(CursorType.Pointer);
            link.On<ClickEvent>(e => { e.StopPropagation = true; CloseModal(); OpenOptions(OptionsSection.GitLabAccounts); });
            line.AddChild(link);
            line.AddChild(Text.New("to see merge requests and pipelines.").FontSize(11f).TextColor(Nocturne.On(0.5)));
            col.AddChild(line);
        }
        return col;
    }

    private void UseHostingRemote(string path, string remoteName)
    {
        var key = NormPath(path);
        if (remoteName == "origin") _settings.RepoHostingRemote.Remove(key);
        else _settings.RepoHostingRemote[key] = remoteName;
        _popover = null;
        Persist();
        MarkDirty();
    }
}
