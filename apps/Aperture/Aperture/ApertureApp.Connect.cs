using Aperture.Core;
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
/// The front door: an address, a user, a password — and, when a token from last time is
/// still good, none of them. Sign-in is the one place in the app where a failure is the
/// content rather than a toast, so every kind of it has its own sentence.
/// </summary>
public sealed partial class ApertureApp
{
    private string _address = "";
    private string _username = "";
    private string _password = "";

    /// <summary>Bumped on each sign-in attempt; the loader is keyed on it, so pressing
    /// Connect twice runs two attempts and a rebuild runs none.</summary>
    private int _signInAttempt;

    private readonly AsyncLoader<SignInOutcome?> _signIn = new(null);
    private readonly AsyncLoader<SignInOutcome?> _autoConnect = new(null);

    /// <summary>What an attempt came back with: a session, or why not — plus the server,
    /// when it was at least found (a wrong password should still name what it was wrong at).</summary>
    internal sealed record SignInOutcome(IMediaLibrary? Library, ServerInfo? Server, ApertureFailure? Failure);

    private ApertureFailure? _connectFailure;
    private ServerInfo? _connectServer;

    /// <summary>True once the stored token has been tried, so the form does not flash up
    /// underneath a session that is about to open.</summary>
    private bool _autoConnectDone;

    /// <summary>
    /// Re-opens last session's server with the token from the credential store. The
    /// account row and the token are stored apart, so either can be missing: no row is a
    /// first run, a row without a token is a machine whose credential store refused the
    /// write, and both land on the form with the address already filled in.
    /// </summary>
    private void EnsureAutoConnect()
    {
        if (_autoConnectDone || _library is not null) return;
        if (_settings.Data.Active is not { } account || _settings.TokenFor(account.ServerId) is not { } token)
        {
            _autoConnectDone = true;
            return;
        }

        var device = _settings.Device;
        var server = account.ToServerInfo();
        _autoConnect.Load("auto", () =>
        {
            var result = JellyfinConnection.ResumeAsync(server, token, device).GetAwaiter().GetResult();
            return (SignInOutcome?)new SignInOutcome(result.Ok ? result.Value : null, server, result.Failure);
        }, outcome =>
        {
            _autoConnectDone = true;
            if (outcome?.Library is { } library)
            {
                Adopt(library, remember: false);
            }
            else if (outcome?.Failure is { } failure)
            {
                // A token the server no longer honours is the ordinary case here (it was
                // revoked, or the server was rebuilt); say so on the form rather than in a
                // toast that disappears before anyone reads it.
                _connectFailure = failure;
                _connectServer = server;
                _address = account.Address;
                _username = account.UserName;
            }
        });
    }

    /// <summary>Signs in with what is in the form. One producer does the whole walk —
    /// address to server to session — because to the person waiting it is one action.</summary>
    private void StartSignIn()
    {
        if (string.IsNullOrWhiteSpace(_address)) { _connectFailure = new ApertureFailure(ApertureFailureKind.Invalid, "Enter your server's address"); Rerender(); return; }
        if (string.IsNullOrWhiteSpace(_username)) { _connectFailure = new ApertureFailure(ApertureFailureKind.Invalid, "Enter your username"); Rerender(); return; }

        _connectFailure = null;
        _signInAttempt++;
        Rerender();
    }

    private void EnsureSignIn()
    {
        if (_signInAttempt == 0) return;
        var device = _settings.Device;
        var address = _address.Trim();
        var user = _username.Trim();
        var password = _password;

        _signIn.Load(("signin", _signInAttempt), () =>
        {
            var probe = JellyfinConnection.DiscoverAsync(address, device).GetAwaiter().GetResult();
            if (!probe.Ok) return (SignInOutcome?)new SignInOutcome(null, null, probe.Failure);

            var session = JellyfinConnection.SignInAsync(probe.Value, user, password, device).GetAwaiter().GetResult();
            return (SignInOutcome?)new SignInOutcome(session.Ok ? session.Value : null, probe.Value, session.Failure);
        }, outcome =>
        {
            _connectServer = outcome?.Server;
            _connectFailure = outcome?.Failure;
            if (outcome?.Library is { } library)
            {
                // The password was used for one request and is not kept — not in a field,
                // not in memory past this line.
                _password = "";
                Adopt(library, remember: true);
            }
        });
    }

    /// <summary>Takes a signed-in session as the app's: remembers it, opens Home, and
    /// starts the loads the shell needs.</summary>
    private void Adopt(IMediaLibrary library, bool remember)
    {
        _library?.Dispose();
        _library = library;
        // Scrubber tiles are behind the session; artwork is not. One header covers both.
        _posters.Authorization = _settings.Device.AuthorizationHeader(library.User.AccessToken);
        _stage = ApertureStage.Browsing;
        _connectFailure = null;
        _dataGeneration++;
        GoHome();

        if (remember && !_settings.Remember(library.Server, library.User))
            Flash("Signed in, but this machine has nowhere to keep the token — you'll sign in again next launch.",
                ToastSeverity.Warning);
    }

    /// <summary>
    /// Ends the session. <paramref name="keepAccount"/> leaves the server in the settings
    /// (an expired token: the address and user are still right, only the secret is stale);
    /// clearing it is what the Settings view's Sign out does.
    /// </summary>
    internal void SignOut(bool keepAccount)
    {
        StopPlayback(report: true);
        var serverId = _library?.Server.Id;
        _library?.Dispose();
        _library = null;
        _posters.Authorization = null;
        _stage = ApertureStage.Connect;
        _autoConnectDone = true;      // do not immediately resume the token we just left
        _signInAttempt = 0;
        _gridItems.Clear();
        _gridKey = null;
        _dataGeneration++;

        if (serverId is not null && !keepAccount) _settings.Forget(serverId);
        else if (serverId is not null) { _settings.Data.ActiveServerId = serverId; _settings.Save(); }

        Rerender();
    }

    // ── The view ─────────────────────────────────────────────────────────

    private Element BuildConnectView()
    {
        EnsureAutoConnect();
        EnsureSignIn();
        EnsureQuickConnect();

        bool busy = _signIn.IsLoading || _autoConnect.IsLoading || !_autoConnectDone;

        var page = Flex.Column(gap: 0).FlexGrow(1)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Background(Halide.Bg);

        var card = Flex.Column(gap: Halide.S4)
            .Width(420).FlexShrink(0)
            .Padding(new Thickness(Halide.S6))
            .CornerRadius(Halide.Radius2)
            .Background(Halide.Surface)
            .BorderColor(Halide.Border).BorderWidth(1f)
            .AutomationId("connect-card");

        card.AddChild(Ui.Wordmark(20f, Halide.Text));
        card.AddChild(Text.New("Sign in to your Jellyfin server.")
            .FontSize(12.5f).TextColor(Halide.TextMuted));

        if (busy)
        {
            card.AddChild(BusyLine(_autoConnect.IsLoading || !_autoConnectDone
                ? "Reconnecting…"
                : $"Signing in to {_address}…"));
        }

        card.AddChild(Field("Server", "jellyfin.home", _address, v => { _address = v; }, "connect-address"));

        // While a code is on screen the password fields would be a second, contradictory
        // way in — the panel takes their place until the attempt is over.
        if (_quickConnectActive)
        {
            card.AddChild(QuickConnectPanel());
            if (_connectFailure is { } stopped) card.AddChild(FailureNote(stopped));
            page.AddChild(card);
            page.AddChild(Box.New().Height(Halide.S3));
            page.AddChild(Text.New("The code is worth nothing on its own — approving it is what signs this window in.")
                .FontSize(11.5f).TextColor(Halide.TextFaint));
            return page;
        }

        card.AddChild(Field("Username", "", _username, v => { _username = v; }, "connect-username"));
        card.AddChild(Field("Password", "", _password, v => { _password = v; }, "connect-password", masked: true));

        if (_connectFailure is { } failure)
            card.AddChild(FailureNote(failure));

        var actions = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        var code = Ui.Button(ButtonVariant.Secondary, "Sign in with a code",
            busy ? null : BeginQuickConnect, "connect-quickconnect");
        code.IsEnabled = !busy;
        actions.AddChild(code);
        actions.AddChild(Box.New().FlexGrow(1));
        var connect = Ui.Button(ButtonVariant.Primary, busy ? "Connecting…" : "Connect",
            busy ? null : StartSignIn, "connect-submit");
        connect.IsEnabled = !busy;
        actions.AddChild(connect);
        card.AddChild(actions);

        page.AddChild(card);

        // The port note, under the card: this is the single most common first failure
        // against a stock install, and saying it up front costs one line.
        page.AddChild(Box.New().Height(Halide.S3));
        page.AddChild(Text.New("A bare hostname is tried on port 8096 as well as 80 and 443.")
            .FontSize(11.5f).TextColor(Halide.TextFaint));
        return page;
    }

    private Element Field(string label, string placeholder, string value, Action<string> onChange,
        string automationId, bool masked = false)
    {
        var column = Flex.Column(gap: 6).FlexShrink(0);
        column.AddChild(Text.New(label).FontSize(11.5f).TextColor(Halide.TextMuted));

        var input = TextInput.New(value)
            .PlaceholderText(placeholder)
            .OnTextChanged(v => { onChange(v); MarkDirty(); })
            .Height(Dimension.Px(38)).FlexShrink(0)
            .TextColor(Halide.Text)
            .AutomationId(automationId);
        input.Masked = masked;
        input.AccessibleName = label;
        // Enter anywhere in the form submits it — the form is three fields and one verb.
        input.On<KeyDownEvent>(e =>
        {
            if (e.Key == KeyCode.Enter) { StartSignIn(); e.Handled = true; }
        });
        input.Key = "field-" + automationId;
        column.AddChild(input);
        return column;
    }

    private static Element BusyLine(string text)
    {
        var row = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        row.AddChild(Ui.Icon(Icons.Loading, 14f, Halide.Accent));
        row.AddChild(Text.New(text).FontSize(12f).TextColor(Halide.TextDim));
        return row;
    }

    /// <summary>The failure, as the form's own content: the sentence, and — where there is
    /// one — what to do about it.</summary>
    private Element FailureNote(ApertureFailure failure)
    {
        var host = _connectServer?.Host ?? _address;
        var (colour, hint) = failure.Kind switch
        {
            ApertureFailureKind.BadCredentials => (Halide.StatusFail, "Check the username and password."),
            ApertureFailureKind.Unauthorized => (Halide.StatusWarn, "The saved token no longer works — sign in again."),
            ApertureFailureKind.Network => (Halide.StatusFail, "Is the server on, and is that the right address?"),
            ApertureFailureKind.Timeout => (Halide.StatusWarn, "It answered too slowly. Try again."),
            ApertureFailureKind.NotJellyfin => (Halide.StatusWarn, "Something answered, but it wasn't Jellyfin."),
            ApertureFailureKind.Invalid => (Halide.StatusWarn, (string?)null),
            ApertureFailureKind.Server => (Halide.StatusFail, "The server itself had a problem."),
            _ => (Halide.StatusFail, (string?)null),
        };

        var box = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.FlexStart)
            .Padding(new Thickness(Halide.S2, Halide.S3, Halide.S2, Halide.S3))
            .CornerRadius(Halide.Radius1)
            .Background(Halide.Alpha(colour, 0.10))
            .BorderColor(Halide.Alpha(colour, 0.45)).BorderWidth(1f)
            .AccessibleRole(AccessibilityRole.Alert)
            .AutomationId("connect-failure");
        box.AddChild(Ui.Icon(Icons.Warning, 14f, colour).FlexShrink(0));

        var lines = Flex.Column(gap: 3).FlexShrink(1);
        lines.AddChild(Text.New(failure.UserMessage(host, "That")).FontSize(12.5f).TextColor(Halide.Text));
        if (hint is not null)
            lines.AddChild(Text.New(hint).FontSize(11.5f).TextColor(Halide.TextMuted));
        box.AddChild(lines);
        return box;
    }
}
