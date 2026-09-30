using Aperture.Core;
using Aperture.Core.Requests;
using HultUI;
using HultUI.Core.Elements;

namespace Aperture;

/// <summary>
/// "Dune: Part Two is in your library" — said once, when something this user asked for
/// arrives. The request server has no push channel (no WebSocket, no server-sent events),
/// so this looks, on a slow timer: once shortly after connecting, then every few minutes.
///
/// <para>What has been announced is remembered in the settings file, per request server,
/// so a restart does not announce everything again; the very first look records what is
/// already there and announces none of it — nobody wants twenty toasts for last year's
/// requests the first time they connect.</para>
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>How long after connecting the first look is taken — long enough not to
    /// compete with the page the app opened on.</summary>
    internal static readonly TimeSpan FirstArrivalCheck = TimeSpan.FromSeconds(20);

    /// <summary>How often after that. A download takes minutes at best; this is plenty.</summary>
    internal static readonly TimeSpan ArrivalInterval = TimeSpan.FromMinutes(5);

    private System.Threading.Timer? _arrivalTimer;

    /// <summary>What has been announced, when there is no account row to keep it in (the
    /// screenshot driver and the tests hand the app a request server directly).</summary>
    private HashSet<int>? _seenAvailable;
    private bool _checkingArrivals;

    private void StartArrivalWatch()
    {
        StopArrivalWatch();
        _arrivalTimer = new System.Threading.Timer(_ => Application.InvokeOnMainThread(() =>
        {
            _ = CheckArrivalsAsync();
            // The same slow look for answers to a reporter's own problem reports.
            CheckMyIssues();
        }), null, FirstArrivalCheck, ArrivalInterval);
    }

    private void StopArrivalWatch()
    {
        _arrivalTimer?.Dispose();
        _arrivalTimer = null;
        _seenAvailable = null;
        ForgetMyIssues();
    }

    /// <summary>One look: this user's available requests, against what has been announced.
    /// Called on the UI thread; the request itself runs off it.</summary>
    internal async Task CheckArrivalsAsync()
    {
        if (_requestServer is not { } server || _checkingArrivals) return;
        _checkingArrivals = true;
        ApertureResult<RequestPage> result;
        try
        {
            result = await Task.Run(() => server.GetRequestsAsync(RequestFilter.Available, mine: true, 0, 20))
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Disconnected while it was asking.
            Application.InvokeOnMainThread(() => _checkingArrivals = false);
            return;
        }
        Application.InvokeOnMainThread(() =>
        {
            _checkingArrivals = false;
            // Quietly: a request server that is off is not worth a toast every five minutes.
            if (!result.Ok || _requestServer != server) return;
            Announce(result.Value.Items);
        });
    }

    private void Announce(IReadOnlyList<MediaRequest> available)
    {
        var account = _library is { } lib ? _settings.RequestServerFor(lib.Server.Id) : null;
        var seen = account?.SeenAvailable is { } stored ? stored.ToHashSet() : _seenAvailable;

        var arrived = seen is null ? [] : available.Where(r => !seen.Contains(r.Id) && r.Title is not null).ToList();
        var now = (seen ?? []).Concat(available.Select(r => r.Id)).ToHashSet();
        if (account is not null)
        {
            // The newest few hundred are plenty to remember; the list is only ever asked
            // about the latest twenty.
            account.SeenAvailable = now.OrderByDescending(id => id).Take(200).ToList();
            _settings.Save();
        }
        else
        {
            _seenAvailable = now;
        }

        if (arrived.Count == 1)
        {
            var request = arrived[0];
            Flash($"{request.Title} is in your library", ToastSeverity.Success,
                ("Open", () =>
                {
                    if (request.JellyfinItemId is { } id) Navigate(new Route(ApertureView.Detail, id, request.Title));
                    else Navigate(new Route(ApertureView.DiscoverTitle, request.MediaKey, request.Title));
                }));
        }
        else if (arrived.Count > 1)
        {
            Flash($"{arrived.Count} of your requests are in your library", ToastSeverity.Success,
                ("Show", () =>
                {
                    SetRequestListFilter(RequestFilter.Available);
                    SetRequestListMine(true);
                    Navigate(new Route(ApertureView.Requests));
                }));
        }
    }
}
