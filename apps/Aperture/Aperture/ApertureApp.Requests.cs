using Aperture.Core.Requests;
using Aperture.Theme;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace Aperture;

/// <summary>
/// The requests: what has been asked for, by whom, and what became of it. A list rather
/// than a shelf — the thing being read is the line under each title (who, when, which
/// seasons, where it stands), and posters in a row would hide it.
///
/// <para>Someone who approves requests sees everyone's, with the verbs on the row; anyone
/// else sees their own, and can withdraw one nobody has acted on yet.</para>
/// </summary>
public sealed partial class ApertureApp
{
    private static readonly (RequestFilter Filter, string Label)[] RequestFilters =
    [
        (RequestFilter.All, "All"),
        (RequestFilter.Pending, "Waiting"),
        (RequestFilter.Processing, "On its way"),
        (RequestFilter.Available, "Available"),
        (RequestFilter.Failed, "Failed"),
    ];

    private Element BuildRequestsView()
    {
        EnsureRequestList();
        var server = _requestServer;

        var column = Flex.Column(gap: 0).FlexGrow(1).AutomationId("requests-view");

        var bar = Flex.Row(gap: Halide.S3).FlexShrink(0)
            .Height(56).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Halide.S5, 0, Halide.S5))
            .BorderColor(Halide.Border).BorderWidth(new Thickness(0, 0, 1, 0));
        bar.AddChild(Text.New("Requests").FontSize(18f).FontWeight(FontWeight.SemiBold).TextColor(Halide.Text));
        if (RequestListTotal > 0) bar.AddChild(Ui.Mono(RequestListTotal.ToString(), 11.5f, Halide.TextFaint));
        bar.AddChild(Box.New().Width(Halide.S3));
        foreach (var (filter, label) in RequestFilters)
        {
            var f = filter;
            bar.AddChild(FilterChip(label, RequestListFilter == f, () => SetRequestListFilter(f),
                "requests-filter-" + f.ToString().ToLowerInvariant()));
        }
        bar.AddChild(Box.New().FlexGrow(1));
        if (server?.User.CanManageRequests == true)
        {
            bar.AddChild(FilterChip("Everyone's", !RequestListMine, () => SetRequestListMine(false), "requests-everyone"));
            bar.AddChild(FilterChip("Mine", RequestListMine, () => SetRequestListMine(true), "requests-mine"));
        }
        column.AddChild(bar);

        var items = RequestList;
        if (items.Count == 0)
        {
            SetCursorList("requests:empty", [], 1);
            column.AddChild(EmptyState(RequestListLoading ? Icons.Loading : Icons.Requests,
                RequestListLoading ? $"Asking {RequestServerTitle}…" : "No requests here",
                RequestListLoading ? null : RequestListFilter == RequestFilter.All
                    ? "A title you have not got has a Request button on its page."
                    : "Nothing matches that filter.",
                "requests-empty"));
            return column;
        }

        var scroll = ScrollView.Vertical().FlexGrow(1);
        var list = Flex.Column(gap: Halide.S1).Padding(new Thickness(Halide.S3, Halide.S5, Halide.S5, Halide.S5));
        var entries = items.Select(RequestAsItem).ToList();
        SetCursorList($"requests:{RequestListFilter}:{RequestListMine}", entries, 1);
        for (int i = 0; i < items.Count; i++) list.AddChild(RequestRow(items[i], i));

        if (RequestListHasMore)
        {
            var more = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center).Padding(new Thickness(Halide.S3, 0, 0, 0));
            more.AddChild(Ui.Button(ButtonVariant.Secondary,
                RequestListLoading ? "Loading…" : $"Show more ({RequestListTotal - items.Count} left)",
                RequestListLoading ? null : ShowMoreRequests, "requests-more"));
            list.AddChild(more);
        }
        scroll.AddChild(list);
        column.AddChild(scroll);
        return column;
    }

    /// <summary>
    /// One request: the poster, the title, the line that says who asked for what and
    /// when, where it stands, and the verbs this user has for it. The row opens the title
    /// — the verbs are buttons of their own, so a click meant for Approve never opens
    /// anything.
    /// </summary>
    private Element RequestRow(MediaRequest request, int index)
    {
        var item = RequestAsItem(request);
        var row = Flex.Row(gap: Halide.S3).AlignItems(AlignItems.Center)
            .Padding(new Thickness(Halide.S2))
            .CornerRadius(Halide.Radius2)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(RequestAccessibleName(request))
            .AutomationId("request-" + request.Id);
        row.IsFocusable = true;
        TrackCursor(row, index);
        if (IsCursor(index)) row.Outline(2f, Halide.Accent, offset: 1f);
        row.HoverBg(Halide.Raised);

        var thumb = Box.New().Width(46).Height(69).FlexShrink(0)
            .CornerRadius(Halide.Radius1).Background(Halide.Raised)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        thumb.Box.OverflowX = Overflow.Hidden;
        thumb.Box.OverflowY = Overflow.Hidden;
        var (image, status) = _posters.Get(RequestImages.Poster(request.PosterPath, 92, RequestImageBase));
        if (status == PosterCache.Status.Ready && image is { IsLoaded: true })
        {
            var picture = Image.New(image);
            picture.Fit = ImageFit.Cover;
            thumb.AddChild(picture.Size(Dimension.Percent(100), Dimension.Percent(100)));
        }
        else thumb.AddChild(Ui.Icon(request.Kind == DiscoverKind.Movie ? Icons.Movies : Icons.Shows, 16f, Halide.TextFaint));
        row.AddChild(thumb);

        var text = Flex.Column(gap: 3).FlexGrow(1).FlexShrink(1);
        var title = request.Title ?? $"TMDB {request.TmdbId}";
        if (request.Year is { } year) title += $" ({year})";
        text.AddChild(Ui.Line(title, 13f, Halide.Text));
        var what = request.Kind == DiscoverKind.Movie ? "Film" : SeasonsPhrase(request.Seasons) is { Length: > 0 } s ? s : "Series";
        if (request.Is4k) what += " · 4K";
        text.AddChild(Ui.Line($"{what} · {request.RequestedBy} · {Ui.Ago(request.CreatedAt)}", 11.5f, Halide.TextMuted));
        row.AddChild(text);

        row.AddChild(RequestStatusChip(request));

        var verbs = Flex.Row(gap: Halide.S1).AlignItems(AlignItems.Center).FlexShrink(0);
        if (_requestServer?.User.CanManageRequests == true && request.Status == RequestStatus.Pending)
        {
            verbs.AddChild(Ui.IconButton(Icons.Approve, "Approve", () => DecideRequest(request, true), 15f,
                Halide.StatusOk, $"request-{request.Id}-approve"));
            verbs.AddChild(Ui.IconButton(Icons.Decline, "Decline", () => DecideRequest(request, false), 15f,
                Halide.StatusFail, $"request-{request.Id}-decline"));
        }
        if (CanWithdraw(request))
            verbs.AddChild(Ui.IconButton(Icons.Withdraw, "Withdraw",
                () => WithdrawRequest(request), 15f, Halide.TextMuted, $"request-{request.Id}-withdraw"));
        // A click bubbles: without this, Approve would also open the title underneath it.
        verbs.OnClick(e => e.StopPropagation = true);
        row.AddChild(verbs);

        row.OnClick(_ => OpenDiscover(item));
        return row;
    }

    private static string RequestAccessibleName(MediaRequest request)
    {
        var parts = new List<string> { request.Title ?? $"TMDB {request.TmdbId}" };
        if (request.Seasons.Count > 0) parts.Add(SeasonsPhrase(request.Seasons));
        parts.Add($"requested by {request.RequestedBy}");
        parts.Add(request.Status switch
        {
            RequestStatus.Pending => "waiting for approval",
            RequestStatus.Declined => "declined",
            RequestStatus.Failed => "failed",
            _ when request.Availability == Availability.Available => "available",
            _ => "on its way",
        });
        return string.Join(", ", parts);
    }
}
