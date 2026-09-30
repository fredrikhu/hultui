using Aperture.Core;
using Aperture.Core.Requests;
using Aperture.Theme;
using HultUI;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace Aperture;

/// <summary>
/// Advanced requests: which Radarr or Sonarr a request goes to, at which quality, into
/// which folder. Only for someone the server lets choose (<see cref="RequestPermission.RequestAdvanced"/>,
/// or anyone who manages requests) — everyone else's request takes the server's defaults,
/// and so does theirs unless they open the choices: a plain Request stays one click.
///
/// <para>The choices are read from <c>/service/radarr|sonarr</c>, never from
/// <c>/settings/*</c>, which is admin-only and carries the downloaders' API keys. Asking a
/// server for its profiles and folders asks the downloader itself, live, so the chips say
/// "…" until it answers.</para>
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>The film whose options card is open, by key; null when none is.</summary>
    private string? _optionsFor;
    private bool _options4k;
    private int? _optionServerId;
    private int? _optionProfileId;
    private string? _optionFolder;

    private readonly AsyncLoader<IReadOnlyList<RequestService>> _services = new([]);
    private readonly AsyncLoader<RequestServiceDetails?> _serviceDetails = new(null);

    /// <summary>Whether this user may say where a request goes.</summary>
    internal bool CanRequestAdvanced =>
        _requestServer?.User is { } user && (user.Has(RequestPermission.RequestAdvanced) || user.CanManageRequests);

    internal void OpenRequestOptions(DiscoverItem item, bool is4k)
    {
        _optionsFor = item.Key;
        _options4k = is4k;
        ResetOptionChoices();
        Rerender();
    }

    private void CloseRequestOptions()
    {
        _optionsFor = null;
        Rerender();
    }

    private void ResetOptionChoices()
    {
        _optionServerId = null;
        _optionProfileId = null;
        _optionFolder = null;
    }

    private void EnsureServices(DiscoverKind kind, bool is4k)
    {
        if (_requestServer is not { } server) return;
        _services.Load(("services", _requestGeneration, _dataGeneration, kind), () =>
            FetchRequest(() => server.GetServicesAsync(kind), (IReadOnlyList<RequestService>)[], "The download servers"));
        if (ChosenService(kind, is4k) is { } chosen)
            _serviceDetails.Load(("service", _requestGeneration, _dataGeneration, kind, chosen.Id), () =>
                FetchRequest<RequestServiceDetails?>(async () =>
                {
                    var result = await server.GetServiceDetailsAsync(kind, chosen.Id).ConfigureAwait(false);
                    return result.Ok
                        ? ApertureResult<RequestServiceDetails?>.Success(result.Value)
                        : result.As<RequestServiceDetails?>();
                }, null, $"{chosen.Name}'s settings"));
    }

    /// <summary>The servers for this kind and quality — a 4K request goes to a 4K server.</summary>
    private IReadOnlyList<RequestService> ServicesFor(bool is4k) => _services.Value.Where(s => s.Is4k == is4k).ToList();

    private RequestService? ChosenService(DiscoverKind kind, bool is4k)
    {
        var servers = ServicesFor(is4k);
        return servers.FirstOrDefault(s => s.Id == _optionServerId)
               ?? servers.FirstOrDefault(s => s.IsDefault)
               ?? servers.FirstOrDefault();
    }

    /// <summary>The choices as they stand, for the request — null when the servers have not
    /// been read, in which case the server's defaults apply, as for anyone else.</summary>
    private RequestOptions? CurrentOptions(DiscoverKind kind, bool is4k)
    {
        if (ChosenService(kind, is4k) is not { } service) return null;
        return new RequestOptions(service.Id,
            _optionProfileId ?? service.ActiveProfileId,
            _optionFolder ?? service.ActiveDirectory);
    }

    /// <summary>
    /// The three choices as chips, each opening a menu: server (only when there is more
    /// than one to choose between), quality profile, root folder with the room left in it.
    /// </summary>
    private Element OptionChips(DiscoverKind kind, bool is4k)
    {
        EnsureServices(kind, is4k);
        var row = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center).AutomationId("request-options");
        row.Flex.Wrap = FlexWrap.Wrap;
        row.Gap(Halide.S2);

        var servers = ServicesFor(is4k);
        var service = ChosenService(kind, is4k);
        if (service is null)
        {
            row.AddChild(Ui.Line(_services.IsLoading ? "Asking for the download servers…"
                : $"No {(is4k ? "4K " : "")}{(kind == DiscoverKind.Movie ? "Radarr" : "Sonarr")} is set up — the request will go to the default",
                11.5f, Halide.TextMuted));
            return row;
        }

        var details = _serviceDetails.Value is { } d && d.Server.Id == service.Id ? d : null;
        if (servers.Count > 1)
            row.AddChild(OptionChip("Server", service.Name, "request-option-server", () => servers.Select(s =>
                new ContextMenuItem
                {
                    Label = s.Name + (s.IsDefault ? " (default)" : ""),
                    IsChecked = s.Id == service.Id,
                    Action = () => { _optionServerId = s.Id; _optionProfileId = null; _optionFolder = null; Rerender(); },
                }).ToList()));

        var profileId = _optionProfileId ?? service.ActiveProfileId;
        var profileName = details?.Profiles.FirstOrDefault(p => p.Id == profileId)?.Name ?? (details is null ? "…" : "default");
        row.AddChild(OptionChip("Quality", profileName, "request-option-quality", () => (details?.Profiles ?? []).Select(p =>
            new ContextMenuItem
            {
                Label = p.Name + (p.Id == service.ActiveProfileId ? " (default)" : ""),
                IsChecked = p.Id == profileId,
                Action = () => { _optionProfileId = p.Id; Rerender(); },
            }).ToList()));

        var folder = _optionFolder ?? service.ActiveDirectory;
        var folderInfo = details?.RootFolders.FirstOrDefault(f => f.Path == folder);
        var folderLabel = folder is null ? (details is null ? "…" : "default")
            : folderInfo?.FreeSpace is { } free ? $"{folder} · {Bytes(free)} free" : folder;
        row.AddChild(OptionChip("Folder", folderLabel, "request-option-folder", () => (details?.RootFolders ?? []).Select(f =>
            new ContextMenuItem
            {
                Label = f.FreeSpace is { } room ? $"{f.Path} · {Bytes(room)} free" : f.Path,
                IsChecked = f.Path == folder,
                Action = () => { _optionFolder = f.Path; Rerender(); },
            }).ToList()));
        return row;
    }

    private Element OptionChip(string label, string value, string automationId, Func<IReadOnlyList<ContextMenuItem>> menu)
    {
        var chip = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
            .Padding(new Thickness(5, Halide.S3, 5, Halide.S3))
            .CornerRadius(Halide.Radius1)
            .BorderColor(Halide.Border).BorderWidth(1f)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.ComboBox)
            .AccessibleName($"{label}: {value}")
            .AutomationId(automationId)
            .ContextMenu(menu);
        chip.AddChild(Ui.Line(label, 11.5f, Halide.TextMuted));
        chip.AddChild(Ui.Line(value, 11.5f, Halide.Text));
        chip.OnClick(e => _window.OpenContextMenu(e.CurrentTarget, e.Position));
        return chip.HoverBg(Halide.Raised);
    }

    /// <summary>"1.8 TB", "340 GB" — the room left in a folder, to the precision anyone
    /// deciding where a film goes needs.</summary>
    internal static string Bytes(long bytes)
    {
        const double k = 1000;
        double b = bytes;
        return b >= k * k * k * k ? $"{b / (k * k * k * k):0.#} TB"
            : b >= k * k * k ? $"{b / (k * k * k):0} GB"
            : $"{Math.Max(0, b / (k * k)):0} MB";
    }

    /// <summary>A film's request with its choices: the chips, then Cancel and Request —
    /// inline under the hero, like the season picker.</summary>
    private Element RequestOptionsCard(DiscoverDetail detail)
    {
        var item = detail.Item;
        var card = Flex.Column(gap: Halide.S3)
            .Padding(new Thickness(Halide.S4))
            .CornerRadius(Halide.Radius2)
            .Background(Halide.Surface)
            .BorderColor(Halide.Border).BorderWidth(1f)
            .MaxWidth(Dimension.Px(720))
            .AutomationId("request-options-card");
        card.AddChild(Text.New(_options4k ? $"Request {item.Title} in 4K" : $"Request {item.Title}")
            .FontSize(14f).FontWeight(FontWeight.SemiBold).TextColor(Halide.Text));
        card.AddChild(OptionChips(DiscoverKind.Movie, _options4k));

        var actions = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        actions.AddChild(Box.New().FlexGrow(1));
        actions.AddChild(Ui.Button(ButtonVariant.Secondary, "Cancel", CloseRequestOptions, "request-options-cancel"));
        bool is4k = _options4k;
        actions.AddChild(Ui.Button(ButtonVariant.Primary, "Request", () =>
        {
            MakeRequest(new NewRequest(DiscoverKind.Movie, item.TmdbId, [], is4k,
                Options: CurrentOptions(DiscoverKind.Movie, is4k)), is4k ? item.Title + " (4K)" : item.Title);
            _optionsFor = null;
        }, "request-options-confirm", Icons.Request));
        card.AddChild(actions);
        return card;
    }
}
