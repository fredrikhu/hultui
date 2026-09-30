using Aperture.Core;
using Aperture.Core.Requests;
using Aperture.Theme;
using HultUI;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace Aperture;

/// <summary>
/// Reporting a problem with what is playing — out of sync, no sound, the wrong subtitles —
/// to the request server, which is where whoever looks after the library reads them. From
/// the player, because that is where the problem is noticed: a panel on the same side as
/// the chapters, with the film still visible beside it.
///
/// <para>The report is filed against the request server's own record of the title, which
/// has to be found first: the TMDB id Jellyfin scraped (a series', for an episode), then
/// the request server's record for it. A title it has no record of cannot take a report,
/// and the panel says so rather than offering a form that will fail.</para>
/// </summary>
public sealed partial class ApertureApp
{
    private bool _reportOpen;
    private IssueType? _reportType;
    private string _reportMessage = "";
    private bool _reportThisEpisode = true;

    internal bool ReportOpen => _reportOpen;

    /// <summary>Whether reporting is on offer at all: a request server this user may file
    /// problems with.</summary>
    internal bool CanReportProblems =>
        _requestServer?.User is { } user
        && (user.Has(RequestPermission.CreateIssues) || user.Has(RequestPermission.ManageIssues));

    internal void ToggleReport()
    {
        if (!CanReportProblems) return;
        _reportOpen = !_reportOpen;
        if (_reportOpen)
        {
            // One side panel at a time.
            _chaptersOpen = false;
            _queueOpen = false;
            _reportType = null;
            _reportMessage = "";
            _reportThisEpisode = true;
        }
        Rerender();
    }

    private void CloseReport()
    {
        if (!_reportOpen) return;
        _reportOpen = false;
        Rerender();
    }

    /// <summary>What a report about the playing item is filed against, or why it cannot be.</summary>
    internal sealed record IssueTarget(int? MediaId, string Title, int? Season, int? Episode, string? Problem);

    private readonly AsyncLoader<IssueTarget?> _issueTarget = new(null);

    private void EnsureIssueTarget(MediaItem item)
    {
        if (_requestServer is not { } server || _library is not { } lib) return;
        _issueTarget.Load(("issue-target", _requestGeneration, item.Id), () =>
        {
            // An episode is reported against its series, with its season and number.
            bool episode = item.Kind == MediaKind.Episode;
            int? tmdb = item.TmdbId;
            string title = episode ? item.SeriesName ?? item.Name : item.Name;
            if (episode && item.SeriesId is { Length: > 0 } seriesId)
            {
                var series = lib.GetItemAsync(seriesId).GetAwaiter().GetResult();
                tmdb = series.Ok ? series.Value.TmdbId : null;
            }
            else if (tmdb is null)
            {
                var full = lib.GetItemAsync(item.Id).GetAwaiter().GetResult();
                tmdb = full.Ok ? full.Value.TmdbId : null;
            }

            if (tmdb is not { } id)
                return (IssueTarget?)new IssueTarget(null, title, null, null,
                    $"Jellyfin has no TMDB id for {title}, so {RequestServerTitle} can't be told which title this is.");

            var detail = server.GetDetailAsync(episode ? DiscoverKind.Tv : DiscoverKind.Movie, id).GetAwaiter().GetResult();
            if (!detail.Ok || detail.Value.MediaId is null)
                return new IssueTarget(null, title, null, null,
                    $"{RequestServerTitle} has no record of {title} yet, so it can't take a report on it.");
            return new IssueTarget(detail.Value.MediaId, title,
                episode ? item.ParentIndexNumber : null, episode ? item.IndexNumber : null, null);
        });
    }

    private static readonly (IssueType Type, string Label)[] IssueTypes =
    [
        (IssueType.Video, "Picture"),
        (IssueType.Audio, "Sound"),
        (IssueType.Subtitles, "Subtitles"),
        (IssueType.Other, "Something else"),
    ];

    private void SendReport(IssueTarget target)
    {
        if (_requestServer is not { } server || target.MediaId is not { } mediaId || _reportType is not { } type) return;
        bool narrow = target.Episode is not null && _reportThisEpisode;
        var issue = new NewIssue(mediaId, type, _reportMessage.Trim(),
            narrow ? target.Season : null, narrow ? target.Episode : null);
        _ = ApplyRequestAsync(() => server.ReportIssueAsync(issue), _ => $"Reported — {RequestServerTitle} has it");
        _reportOpen = false;
        Rerender();
    }

    private Element BuildReportPanel()
    {
        var panel = Flex.Column(gap: 0)
            .Width(360)
            .Background(Halide.Surface)
            .BorderColor(Halide.Border).BorderWidth(new Thickness(0, 0, 0, 1))
            .AccessibleRole(AccessibilityRole.Dialog)
            .AccessibleName("Report a problem")
            .AutomationId("report");
        panel.Position.Type = PositionType.Absolute;
        panel.Position.Right = Dimension.Px(0);
        panel.Position.Top = Dimension.Px(0);
        panel.Box.Height = Dimension.Percent(100);
        panel.ZIndex(12);

        var head = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center).FlexShrink(0)
            .Height(56).Padding(new Thickness(0, Halide.S4, 0, Halide.S4))
            .BorderColor(Halide.Border).BorderWidth(new Thickness(0, 0, 1, 0));
        head.AddChild(Text.New("Report a problem").FontSize(14f).FontWeight(FontWeight.SemiBold).TextColor(Halide.Text));
        head.AddChild(Box.New().FlexGrow(1));
        head.AddChild(Ui.IconButton(Icons.WinClose, "Close", CloseReport, 12f, Halide.TextMuted, "report-close"));
        panel.AddChild(head);

        var body = Flex.Column(gap: Halide.S3).Padding(new Thickness(Halide.S4));
        if (_playingItem is { } item) EnsureIssueTarget(item);
        var target = _issueTarget.Value;

        if (target is null || _playingItem is null)
        {
            body.AddChild(Hint(Icons.Loading, $"Finding this on {RequestServerTitle}…"));
        }
        else if (target.Problem is { } problem)
        {
            body.AddChild(Text.New(problem).FontSize(12.5f).TextColor(Halide.TextMuted));
        }
        else
        {
            body.AddChild(Text.New($"What's wrong with {target.Title}?").FontSize(12.5f).TextColor(Halide.TextDim));

            var types = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
            types.Flex.Wrap = FlexWrap.Wrap;
            types.Gap(Halide.S2);
            foreach (var (type, label) in IssueTypes)
            {
                var t = type;
                types.AddChild(FilterChip(label, _reportType == t, () => { _reportType = t; Rerender(); },
                    "report-type-" + t.ToString().ToLowerInvariant()));
            }
            body.AddChild(types);

            if (target.Episode is { } episode)
            {
                var only = Checkbox.New(_reportThisEpisode, $"Only this episode (S{target.Season} · E{episode})");
                only.LabelGap = 10f;
                only.CheckedColor = Halide.Accent;
                only.CheckmarkColor = new Color(35, 23, 4);
                only.BoxColor = Halide.TextMuted;
                only.TextColor(Halide.TextDim).FontSize(12f);
                only.AutomationId = "report-this-episode";
                only.Key = "report-this-episode-" + (_reportThisEpisode ? "on" : "off");
                only.OnCheckedChanged(on => { _reportThisEpisode = on; Rerender(); });
                body.AddChild(only);
            }

            var box = Flex.Column(gap: 0).Padding(new Thickness(6, 10, 6, 10))
                .Background(Halide.Bg).BorderColor(Halide.Border).BorderWidth(1f).CornerRadius(Halide.Radius1);
            var message = TextArea.New(_reportMessage)
                .PlaceholderText("What happens, and roughly where — \"out of sync from 40 minutes in\"")
                .OnTextChanged(v => { _reportMessage = v; MarkDirty(); })
                .Height(Dimension.Px(110)).TextColor(Halide.Text);
            message.AutomationId = "report-message";
            message.AccessibleName = "What's wrong";
            box.AddChild(message);
            body.AddChild(box);

            bool ready = _reportType is not null && _reportMessage.Trim().Length > 0;
            var actions = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
            actions.AddChild(Box.New().FlexGrow(1));
            var send = Ui.Button(ButtonVariant.Primary, "Send report", ready ? () => SendReport(target) : null,
                "report-send", Icons.Report);
            send.IsEnabled = ready;
            actions.AddChild(send);
            body.AddChild(actions);
            body.AddChild(Text.New($"It goes to {RequestServerTitle}, where whoever looks after the library sees it.")
                .FontSize(11f).TextColor(Halide.TextFaint));
        }
        panel.AddChild(body);

        panel.On<KeyDownEvent>(e =>
        {
            // Stopped here: the sheet under the panel reads Escape as "stop the film".
            if (e.Key == KeyCode.Escape) { CloseReport(); e.Handled = true; e.StopPropagation = true; }
        });
        return panel;
    }
}
