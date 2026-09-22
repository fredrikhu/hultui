using Aperture.Core;
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
/// Signing in without typing a password into this window: the server gives out a code,
/// someone types it into a Jellyfin that is already signed in, and the secret this client
/// kept turns into an access token.
///
/// <para>It is the better door for a ten-foot screen, and the safer one on a machine that
/// is not yours — nothing secret is typed here, and the code alone is worth nothing to
/// anyone who reads it over your shoulder.</para>
///
/// <para>The waiting is a timer, not a long call. One request per tick, so the code is on
/// screen the whole time it is being waited on and Cancel answers at once; a client that
/// blocks for five minutes inside one request cannot draw the thing it is waiting for.</para>
/// </summary>
public sealed partial class ApertureApp
{
    private readonly AsyncLoader<QuickConnectStart?> _quickConnectStart = new(null);
    private readonly AsyncLoader<SignInOutcome?> _quickConnectPoll = new(null);

    /// <summary>What asking for a code came back with: the code and the server it is for,
    /// or why there is none.</summary>
    internal sealed record QuickConnectStart(QuickConnectCode? Code, ServerInfo? Server, ApertureFailure? Failure);

    /// <summary>Identifies the attempt. Bumped to abandon one: a request already on its
    /// way cannot be stopped, so its result is discarded by the number it was made under.</summary>
    private int _quickConnectAttempt;

    private bool _quickConnectActive;
    private QuickConnectCode? _quickConnectCode;
    private ServerInfo? _quickConnectServer;

    /// <summary>Counts the polls. The loader is keyed on it, so each tick is one request
    /// and a rebuild between ticks asks nothing.</summary>
    private int _quickConnectTick;

    /// <summary>When to give up. The server expires the code itself; this is so a window
    /// left open overnight stops asking.</summary>
    private long _quickConnectDeadline;

    private System.Threading.Timer? _quickConnectTimer;
    private CancellationTokenSource? _quickConnectCts;

    internal string? QuickConnectCode => _quickConnectCode?.Code;
    internal bool QuickConnectActive => _quickConnectActive;

    /// <summary>Starts an attempt. Needs an address and nothing else — that is the point.</summary>
    internal void BeginQuickConnect()
    {
        if (string.IsNullOrWhiteSpace(_address))
        {
            _connectFailure = new ApertureFailure(ApertureFailureKind.Invalid, "Enter your server's address");
            Rerender();
            return;
        }

        StopQuickConnectTimer();
        _quickConnectCts?.Cancel();
        _quickConnectCts?.Dispose();
        _quickConnectCts = new CancellationTokenSource();
        _connectFailure = null;
        _quickConnectCode = null;
        _quickConnectServer = null;
        _quickConnectTick = 0;
        _quickConnectActive = true;
        _quickConnectAttempt++;
        Rerender();
    }

    /// <summary>Gives up on the code on screen. The server expires it on its own; this is
    /// about the window, not the request.</summary>
    internal void CancelQuickConnect()
    {
        if (!_quickConnectActive) return;
        EndQuickConnect();
        Rerender();
    }

    /// <summary>Tears the attempt down: nothing in flight belongs to us afterwards.</summary>
    private void EndQuickConnect()
    {
        _quickConnectAttempt++;
        _quickConnectActive = false;
        _quickConnectCode = null;
        _quickConnectServer = null;
        StopQuickConnectTimer();
        _quickConnectCts?.Cancel();
        _quickConnectCts?.Dispose();
        _quickConnectCts = null;
    }

    /// <summary>Asks for a code, once per attempt, and starts the clock when one arrives.</summary>
    private void EnsureQuickConnect()
    {
        if (!_quickConnectActive || _quickConnectCts is not { } cts) return;

        int attempt = _quickConnectAttempt;
        var device = _settings.Device;
        var address = _address.Trim();
        var ct = cts.Token;

        _quickConnectStart.Load(("quickconnect", attempt), () =>
        {
            var probe = JellyfinConnection.DiscoverAsync(address, device, ct).GetAwaiter().GetResult();
            if (!probe.Ok) return (QuickConnectStart?)new QuickConnectStart(null, null, probe.Failure);

            var started = JellyfinConnection.StartQuickConnectAsync(probe.Value, device, ct).GetAwaiter().GetResult();
            return (QuickConnectStart?)new QuickConnectStart(
                started.Ok ? started.Value : null, probe.Value, started.Failure);
        }, start =>
        {
            if (attempt != _quickConnectAttempt) return;

            if (start?.Code is { } code)
            {
                _quickConnectCode = code;
                _quickConnectServer = start.Server;
                _connectServer = start.Server;
                _quickConnectDeadline = Environment.TickCount64
                    + (long)JellyfinConnection.QuickConnectTimeout.TotalMilliseconds;
                StartQuickConnectTimer();
            }
            else
            {
                _connectServer = start?.Server;
                _connectFailure = start?.Failure;
                EndQuickConnect();
            }
            Rerender();
        });

        if (_quickConnectCode is not null) EnsureQuickConnectPoll();
    }

    /// <summary>One request per tick: has anyone approved it yet, and if so, sign in.</summary>
    private void EnsureQuickConnectPoll()
    {
        if (_quickConnectCode is not { } code || _quickConnectServer is not { } server) return;
        if (_quickConnectCts is not { } cts) return;

        int attempt = _quickConnectAttempt;
        int tick = _quickConnectTick;
        var device = _settings.Device;
        var ct = cts.Token;

        _quickConnectPoll.Load(("quickconnect-poll", attempt, tick), () =>
        {
            var polled = JellyfinConnection.PollQuickConnectAsync(server, code, device, ct).GetAwaiter().GetResult();
            if (!polled.Ok) return (SignInOutcome?)new SignInOutcome(null, server, polled.Failure);
            // Not approved yet is not a failure and not a session: the next tick asks again.
            if (!polled.Value) return null;

            var session = JellyfinConnection.CompleteQuickConnectAsync(server, code, device, ct).GetAwaiter().GetResult();
            return (SignInOutcome?)new SignInOutcome(session.Ok ? session.Value : null, server, session.Failure);
        }, outcome =>
        {
            if (attempt != _quickConnectAttempt)
            {
                outcome?.Library?.Dispose();
                return;
            }
            if (outcome is null) return;      // still waiting; the timer will ask again

            var library = outcome.Library;
            var failure = outcome.Failure;
            EndQuickConnect();
            _connectServer = outcome.Server;
            _connectFailure = failure;
            if (library is not null) Adopt(library, remember: true);
            Rerender();
        });
    }

    private void StartQuickConnectTimer()
    {
        StopQuickConnectTimer();
        _quickConnectTimer = new System.Threading.Timer(
            _ => Application.InvokeOnMainThread(QuickConnectTick), null,
            (int)JellyfinConnection.QuickConnectPollInterval.TotalMilliseconds,
            (int)JellyfinConnection.QuickConnectPollInterval.TotalMilliseconds);
    }

    private void StopQuickConnectTimer()
    {
        _quickConnectTimer?.Dispose();
        _quickConnectTimer = null;
    }

    private void QuickConnectTick()
    {
        if (!_quickConnectActive || _quickConnectCode is null) { StopQuickConnectTimer(); return; }

        if (Environment.TickCount64 >= _quickConnectDeadline)
        {
            var host = _quickConnectServer?.Host ?? _address;
            EndQuickConnect();
            _connectFailure = new ApertureFailure(ApertureFailureKind.Timeout,
                $"Nobody approved the code on {host}");
            Rerender();
            return;
        }

        _quickConnectTick++;
        Rerender();      // the build is where the next poll is asked for
    }

    /// <summary>
    /// The code, large enough to read from across a room, with what to do with it. The
    /// characters are spaced because they are transcribed by hand, one at a time, onto
    /// another device.
    /// </summary>
    private Element QuickConnectPanel()
    {
        var panel = Flex.Column(gap: Halide.S3).FlexShrink(0)
            .Padding(new Thickness(Halide.S4))
            .CornerRadius(Halide.Radius2)
            .Background(Halide.Raised)
            .BorderColor(Halide.Border).BorderWidth(1f)
            .AccessibleRole(AccessibilityRole.Group)
            .AutomationId("quickconnect-panel");

        if (_quickConnectCode is { } code)
        {
            var big = Ui.Mono(string.Join(' ', code.Code.ToCharArray()), 30f, Halide.Accent300);
            big.AccessibleName = $"Quick Connect code {string.Join(", ", code.Code.ToCharArray())}";
            big.AutomationId("quickconnect-code");
            panel.AddChild(big);
            panel.AddChild(Text.New($"On a device already signed in to {_quickConnectServer?.Name ?? "your server"}, "
                    + "open the user menu, choose Quick Connect, and type this code.")
                .FontSize(12f).TextColor(Halide.TextMuted));
            panel.AddChild(BusyLine("Waiting for someone to approve it…"));
        }
        else
        {
            panel.AddChild(BusyLine("Asking the server for a code…"));
        }

        var actions = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        actions.AddChild(Box.New().FlexGrow(1));
        actions.AddChild(Ui.Button(ButtonVariant.Secondary, "Cancel", CancelQuickConnect, "quickconnect-cancel"));
        panel.AddChild(actions);
        return panel;
    }
}
