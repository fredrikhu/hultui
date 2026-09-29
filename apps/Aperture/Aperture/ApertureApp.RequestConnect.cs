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
/// Connecting a request server, from the Settings page. Three doors, in the order they are
/// offered:
///
/// <list type="bullet">
/// <item><b>Your Jellyfin account, nothing typed.</b> Seerr 3.4 signs Jellyfin users in
/// through Jellyfin's Quick Connect. Aperture already holds a Jellyfin session, so it
/// approves the code itself: an address and one button.</item>
/// <item><b>Your Jellyfin password</b>, for an older Jellyseerr, or a Jellyfin with Quick
/// Connect switched off. It is sent in the one sign-in request and dropped.</item>
/// <item><b>An API key</b> from the request server's own settings. It acts as the
/// administrator, so Aperture names the Jellyfin user it is acting for when the request
/// server knows them — requests still land under the right name.</item>
/// </list>
///
/// <para>The first two end in a session cookie, which is what the OS credential store
/// keeps; requests are then made as <i>you</i>, with your quota and your permissions.</para>
/// </summary>
public sealed partial class ApertureApp
{
    private string _requestAddress = "";
    private string _requestPassword = "";
    private string _requestApiKey = "";
    private bool _requestUseApiKey;

    /// <summary>Set once the passwordless door turned out to be shut — an older server, or
    /// Quick Connect off in Jellyfin — so the form asks for the password instead.</summary>
    private bool _requestNeedsPassword;

    /// <summary>Bumped on each connect attempt, and by "try again" on a stored session
    /// that failed to resume.</summary>
    private int _requestConnectAttempt;
    private int _requestConnectRun;
    private ApertureFailure? _requestConnectFailure;

    private readonly AsyncLoader<RequestConnectOutcome?> _requestConnect = new(null);

    internal ApertureFailure? RequestConnectFailure => _requestConnectFailure;
    internal bool RequestConnecting => _requestConnect.IsLoading || _requestResume.IsLoading;

    internal void StartRequestConnect()
    {
        if (_library is null) return;
        if (string.IsNullOrWhiteSpace(_requestAddress))
        {
            _requestConnectFailure = new ApertureFailure(ApertureFailureKind.Invalid, $"Enter {RequestServerTitle}'s address");
            Rerender();
            return;
        }
        if (_requestUseApiKey ? string.IsNullOrWhiteSpace(_requestApiKey) : _requestNeedsPassword && _requestPassword.Length == 0)
        {
            _requestConnectFailure = new ApertureFailure(ApertureFailureKind.Invalid,
                _requestUseApiKey ? "Paste the API key" : "Enter your Jellyfin password");
            Rerender();
            return;
        }
        _requestConnectFailure = null;
        _requestConnectRun++;
        Rerender();
    }

    private void EnsureRequestConnect()
    {
        if (_requestConnectRun == 0 || _library is not { } lib) return;
        var address = _requestAddress.Trim();
        var password = _requestPassword;
        var apiKey = _requestApiKey.Trim();
        bool useKey = _requestUseApiKey;
        var jellyfinServerId = lib.Server.Id;
        var jellyfinUserId = lib.User.Id;
        var jellyfinUserName = lib.User.Name;

        _requestConnect.Load(("request-connect", _requestConnectRun), () =>
        {
            var found = SeerrConnection.DiscoverAsync(address).GetAwaiter().GetResult();
            if (!found.Ok) return (RequestConnectOutcome?)new RequestConnectOutcome(null, found.Failure);
            var server = found.Value;

            if (useKey)
                return Outcome(SeerrConnection.SignInWithApiKeyAsync(server, jellyfinServerId, apiKey, jellyfinUserId)
                    .GetAwaiter().GetResult());
            if (password.Length > 0)
                return Outcome(SeerrConnection.SignInAsync(server, jellyfinServerId, jellyfinUserName, password)
                    .GetAwaiter().GetResult());
            if (!server.SupportsQuickConnect)
                return new RequestConnectOutcome(null, new ApertureFailure(ApertureFailureKind.Invalid,
                    $"{server.Title} {server.Version} signs in with your Jellyfin password — enter it below"),
                    NeedsPassword: true);

            var bridged = SeerrConnection.SignInWithJellyfinAsync(server, jellyfinServerId, lib.AuthorizeQuickConnectAsync)
                .GetAwaiter().GetResult();
            // A shut Quick Connect is not the end: the password is the other way in.
            return Outcome(bridged, needsPasswordOnFailure: bridged.Failure?.Kind is ApertureFailureKind.Forbidden
                or ApertureFailureKind.Unauthorized or ApertureFailureKind.Server);
        }, outcome =>
        {
            // The password did its one job; it is not kept anywhere past this line.
            _requestPassword = "";
            if (outcome?.Server is SeerrServer server && _library?.Server.Id == jellyfinServerId)
            {
                _requestApiKey = "";
                _requestNeedsPassword = false;
                bool stored = _settings.RememberRequestServer(server.Account, server.Secret);
                AdoptRequestServer(server);
                Flash(stored
                    ? $"Connected to {server.Server.Title} as {server.User.DisplayName}"
                    : $"Connected to {server.Server.Title} — this machine can't keep the session, so it won't survive a restart",
                    stored ? ToastSeverity.Success : ToastSeverity.Warning);
            }
            else
            {
                outcome?.Server?.Dispose();
                _requestConnectFailure = outcome?.Failure;
                if (outcome?.NeedsPassword == true) _requestNeedsPassword = true;
            }
        });

        static RequestConnectOutcome? Outcome(ApertureResult<SeerrServer> result, bool needsPasswordOnFailure = false) =>
            new RequestConnectOutcome(result.Ok ? result.Value : null, result.Failure,
                NeedsPassword: !result.Ok && needsPasswordOnFailure);
    }

    /// <summary>The Settings card: the connected server's facts and a Disconnect button,
    /// or the form that connects one.</summary>
    private Element RequestServerCard()
    {
        EnsureRequestConnect();
        var card = Card(_requestServer is { } s ? s.Server.Title : "Requests");
        card.AutomationId("settings-requests");

        if (_requestServer is { } server)
        {
            card.AddChild(Row("Address", server.Server.Address));
            card.AddChild(Row("Version", server.Server.Version));
            var account = _library is { } lib ? _settings.RequestServerFor(lib.Server.Id) : null;
            card.AddChild(Row("Requests as", account?.Auth == RequestServerAuth.ApiKey
                ? $"{server.User.DisplayName} (API key)"
                : server.User.DisplayName));
            card.AddChild(Row("Allowed", PermissionsPhrase(server.User)));
            var actions = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
            actions.AddChild(Ui.Button(ButtonVariant.Secondary, "Disconnect", DisconnectRequestServer,
                "settings-requests-disconnect", Icons.SignOut));
            card.AddChild(actions);
            return card;
        }

        bool busy = RequestConnecting;
        var stored = _library is { } l ? _settings.RequestServerFor(l.Server.Id) : null;
        if (string.IsNullOrEmpty(_requestAddress) && stored is not null) _requestAddress = stored.Address;

        card.AddChild(Text.New("Connect Jellyseerr or Seerr to find films and series you don't have yet, and ask for them.")
            .FontSize(12f).TextColor(Halide.TextMuted));

        card.AddChild(Field("Address", "jellyseerr.home", _requestAddress, v => { _requestAddress = v; },
            "settings-requests-address", onSubmit: StartRequestConnect));
        if (_requestUseApiKey)
        {
            card.AddChild(Field("API key", "", _requestApiKey, v => { _requestApiKey = v; },
                "settings-requests-apikey", masked: true, onSubmit: StartRequestConnect));
            card.AddChild(Text.New("From its Settings → General. Requests are made as your Jellyfin user when it knows you, "
                                   + "and as its administrator when it does not.")
                .FontSize(11.5f).TextColor(Halide.TextFaint));
        }
        else if (_requestNeedsPassword)
        {
            var who = _library?.User.Name ?? "you";
            card.AddChild(Field($"Jellyfin password for {who}", "", _requestPassword, v => { _requestPassword = v; },
                "settings-requests-password", masked: true, onSubmit: StartRequestConnect));
            card.AddChild(Text.New("It signs you in with your Jellyfin account. The password is sent once and not "
                                   + "kept — the session it opens is, in the credential store.")
                .FontSize(11.5f).TextColor(Halide.TextFaint));
        }
        else
        {
            card.AddChild(Text.New($"You are signed in as {_library?.User.Name ?? "you"} — Aperture approves the sign-in "
                                   + "on Jellyfin itself, so there is nothing to type (Seerr 3.4 or later).")
                .FontSize(11.5f).TextColor(Halide.TextFaint));
        }

        if (busy) card.AddChild(BusyLine(_requestResume.IsLoading ? "Reconnecting…" : "Connecting…"));
        if (_requestConnectFailure is { } failure) card.AddChild(RequestFailureNote(failure));

        var row = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        // A stored session that did not resume (the server was off at launch) can simply
        // be tried again — no password needed for that.
        if (stored is not null && _library is { } paired && _settings.RequestSecretFor(paired.Server.Id) is not null)
            row.AddChild(Ui.Button(ButtonVariant.Secondary, "Reconnect", busy ? null : () =>
            {
                _requestConnectAttempt++;
                _requestConnectFailure = null;
                Rerender();
            }, "settings-requests-reconnect", Icons.Refresh));
        row.AddChild(Ui.Button(ButtonVariant.Ghost, _requestUseApiKey ? "Use your Jellyfin account instead" : "Use an API key instead",
            () => { _requestUseApiKey = !_requestUseApiKey; _requestConnectFailure = null; Rerender(); },
            "settings-requests-mode"));
        row.AddChild(Box.New().FlexGrow(1));
        var connect = Ui.Button(ButtonVariant.Primary, busy ? "Connecting…" : "Connect",
            busy ? null : StartRequestConnect, "settings-requests-connect");
        connect.IsEnabled = !busy;
        row.AddChild(connect);
        card.AddChild(row);
        return card;
    }

    /// <summary>What a user may do, as a phrase: "request films and series · approve requests".</summary>
    internal static string PermissionsPhrase(RequestUser user)
    {
        var parts = new List<string>();
        bool movies = user.CanRequest(DiscoverKind.Movie), tv = user.CanRequest(DiscoverKind.Tv);
        parts.Add(movies && tv ? "request films and series" : movies ? "request films" : tv ? "request series" : "browse only");
        if (user.AutoApproves(DiscoverKind.Movie) && user.AutoApproves(DiscoverKind.Tv)) parts.Add("approved automatically");
        if (user.CanRequest4k(DiscoverKind.Movie) || user.CanRequest4k(DiscoverKind.Tv)) parts.Add("4K");
        if (user.CanManageRequests) parts.Add("approve requests");
        if (user.Has(RequestPermission.Admin)) parts.Add("administrator");
        return string.Join(" · ", parts);
    }

    private Element RequestFailureNote(ApertureFailure failure)
    {
        var host = _requestAddress.Trim();
        var (colour, message, hint) = failure.Kind switch
        {
            ApertureFailureKind.BadCredentials => (Halide.StatusFail, "That password was refused",
                $"It is your Jellyfin password — and your Jellyfin account has to be imported into {RequestServerTitle}."),
            ApertureFailureKind.Unauthorized => (Halide.StatusWarn, "The saved session has ended", "Sign in again."),
            ApertureFailureKind.Forbidden => (Halide.StatusFail, failure.Message, (string?)null),
            ApertureFailureKind.NotJellyfin => (Halide.StatusWarn, $"{host} answered, but not like Jellyseerr",
                "It usually listens on port 5055."),
            ApertureFailureKind.Invalid => (Halide.StatusWarn, failure.Message, null),
            _ => (Halide.StatusFail, failure.UserMessage(host), "Is it running, and is that the right address?"),
        };
        var box = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.FlexStart)
            .Padding(new Thickness(Halide.S2, Halide.S3, Halide.S2, Halide.S3))
            .CornerRadius(Halide.Radius1)
            .Background(Halide.Alpha(colour, 0.10))
            .BorderColor(Halide.Alpha(colour, 0.45)).BorderWidth(1f)
            .AccessibleRole(AccessibilityRole.Alert)
            .AutomationId("settings-requests-failure");
        box.AddChild(Ui.Icon(Icons.Warning, 14f, colour).FlexShrink(0));
        var lines = Flex.Column(gap: 3).FlexShrink(1);
        lines.AddChild(Text.New(message).FontSize(12.5f).TextColor(Halide.Text));
        if (hint is not null) lines.AddChild(Text.New(hint).FontSize(11.5f).TextColor(Halide.TextMuted));
        box.AddChild(lines);
        return box;
    }
}
