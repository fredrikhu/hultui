using HultUI.Core.Builders;
using HultUI.Core.Dialogs;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Terminal;
using HultUI.Terminal.Ssh;

namespace HultUI.Samples.Terminal;

// Inside a HultUI.* namespace bare `Text` binds to the HultUI.Text namespace.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// <c>--ssh [user@]host[:port] [--accept-new]</c>: a shell on another machine instead of this one,
/// signed in the way <c>ssh</c> would.
/// </summary>
/// <remarks>
/// <para><b>The user's own setup.</b> <c>~/.ssh/config</c> says which name to connect to, as whom,
/// on which port and with which key files; <c>~/.ssh/known_hosts</c> which hosts are trusted. A
/// host's <c>IdentityFile</c>s are tried, or <c>ssh</c>'s default files when it names none; the
/// agent first unless <c>IdentitiesOnly</c>; then the server's own questions and a password, in
/// <c>ssh</c>'s order.</para>
/// <para><b>Asking.</b> Every question is a dialog in the window: an unknown host's fingerprint
/// (or, with <c>--accept-new</c>, trusted without asking, as OpenSSH's
/// <c>StrictHostKeyChecking=accept-new</c>), a changed key (never in one click), a password, a
/// passphrase, the server's questions. The terminal shows "Connecting" meanwhile
/// (<see cref="DeferredConnection"/>), and why it could not, if it could not.</para>
/// </remarks>
internal static class SshTarget
{
    /// <summary>What makes the session's connection, or null when the arguments name no host.</summary>
    /// <exception cref="ArgumentException">The target is not <c>[user@]host[:port]</c>.</exception>
    public static Func<TerminalSampleApp, ITerminalConnection>? From(string[] args)
    {
        int at = Array.IndexOf(args, "--ssh");
        if (at < 0) return null;
        var target = at + 1 < args.Length ? args[at + 1] : "";
        string? user = null;
        int? port = null;
        if (target.IndexOf('@') is int sign and >= 0) (user, target) = (target[..sign], target[(sign + 1)..]);
        if (target.LastIndexOf(':') is int colon and > 0 && int.TryParse(target[(colon + 1)..], out var p))
            (target, port) = (target[..colon], p);
        if (target.Length == 0 || user is { Length: 0 })
            throw new ArgumentException($"--ssh wants [user@]host[:port], not '{args.ElementAtOrDefault(at + 1)}'.");
        bool acceptNew = args.Contains("--accept-new");

        return app =>
        {
            var config = new SshConfigFile(SshConfigFile.UserFilePath).Resolve(target);
            var options = Options(app, config, user ?? config.User ?? Environment.UserName, port ?? config.Port ?? 22, acceptNew);
            var where = $"{options.User}@{options.Host}" + (options.Port == 22 ? "" : $":{options.Port}");
            app.RemoteName = "ssh " + where;
            var connecting = $"Connecting to {where}…";
            if (config.ProxyJump is { } jump)
                connecting += $"\n(ProxyJump {jump} in ~/.ssh/config is not supported: connecting directly.)";
            return new DeferredConnection(async ct => await SshConnection.ConnectAsync(options, ct),
                Dim(connecting), Describe);
        };
    }

    private static SshConnectOptions Options(TerminalSampleApp app, SshHostConfig config, string user, int port, bool acceptNew)
    {
        var ask = new Prompts(app);
        var ways = new List<SshAuthentication>();
        if (!config.IdentitiesOnly) ways.Add(SshAuthentication.Agent());
        foreach (var file in config.IdentityFiles.Count > 0 ? config.IdentityFiles : SshConfigFile.DefaultIdentityFiles)
            ways.Add(SshAuthentication.KeyFile(file, ask.Passphrase));
        ways.Add(SshAuthentication.KeyboardInteractive(ask.Questions));
        ways.Add(SshAuthentication.Password(ask.Password));
        return new SshConnectOptions
        {
            Host = config.HostName,
            Port = port,
            User = user,
            Authentication = ways,
            KnownHosts = new KnownHostsFile(config.UserKnownHostsFile ?? KnownHostsFile.UserFilePath),
            VerifyHostKey = (key, ct) => key.Status == HostKeyStatus.Unknown && acceptNew
                ? Task.FromResult(HostKeyDecision.Trust)
                : ask.HostKey(key, ct),
        };
    }

    /// <summary>What the terminal says when it could not connect.</summary>
    private static string Describe(Exception ex) => Red(ex switch
    {
        SshHostKeyRejectedException { HostKey.Status: HostKeyStatus.Revoked } r =>
            $"{r.HostKey.Host}'s key is revoked in known_hosts ({r.HostKey.Fingerprint}). Not connecting.",
        SshHostKeyRejectedException r => $"{r.HostKey.Host}'s key was not trusted. Not connecting.",
        System.Net.Sockets.SocketException s => $"Could not reach the host: {s.Message}",
        _ => ex.Message,
    });

    private static string Dim(string text) => "\x1b[2m" + text + "\x1b[22m";
    private static string Red(string text) => "\x1b[31m" + text.ReplaceLineEndings("\x1b[0m\n\x1b[31m") + "\x1b[0m";

    /// <summary>
    /// The questions, as dialogs over the terminal. SSH asks on its own thread, which waits: each
    /// is shown on the UI thread, and the answer handed back from there.
    /// </summary>
    private sealed class Prompts(TerminalSampleApp app)
    {
        public Task<string?> Password(SshPasswordPrompt prompt, CancellationToken cancellation) =>
            Secret(cancellation, $"Password for {prompt.User}@{prompt.Host}",
                prompt.Attempt > 1 ? "That was not it. Try again." : null, "Password");

        public Task<string?> Passphrase(SshPassphrasePrompt prompt, CancellationToken cancellation) =>
            Secret(cancellation, "Passphrase for " + Path.GetFileName(prompt.KeyFile),
                (prompt.Attempt > 1 ? "That was not it. Try again.\n" : "") + prompt.KeyFile, "Passphrase");

        public Task<IReadOnlyList<string>?> Questions(SshInteractivePrompt prompt, CancellationToken cancellation) =>
            Ask<IReadOnlyList<string>?>(cancellation, (close, theme) =>
            {
                var fields = prompt.Questions.Select(q => Field(theme, q.Text.Trim(), !q.Echo)).ToList();
                IReadOnlyList<string> Answers() => fields.Select(f => f.Text).ToList();
                var body = new List<Element> { Title(theme, $"{prompt.Host} asks") };
                if (prompt.Instruction.Length > 0) body.Add(Message(theme, prompt.Instruction));
                foreach (var (field, question) in fields.Zip(prompt.Questions))
                    body.Add(Flex.Column(gap: 6).Children(Message(theme, question.Text.Trim()), field));
                return (Panel(theme, body, Buttons(
                    (ButtonVariant.Secondary, "Cancel", () => close.Close(null)),
                    (ButtonVariant.Primary, "Sign in", () => close.Close(Answers())))),
                    fields.FirstOrDefault(), () => close.Close(Answers()), () => close.Close(null));
            });

        public Task<HostKeyDecision> HostKey(SshHostKey key, CancellationToken cancellation) =>
            Ask<HostKeyDecision>(cancellation, (close, theme) => key.Status == HostKeyStatus.Changed
                ? ChangedKey(key, close, theme)
                : UnknownHost(key, close, theme));

        private static (Element, Element?, Action?, Action) UnknownHost(SshHostKey key, IDialogController<HostKeyDecision> close, Theme theme)
        {
            var body = new List<Element>
            {
                Title(theme, $"{key.Host} is not a known host"),
                Message(theme, $"Its {key.Type} key has this fingerprint. Compare it with the one you were given before " +
                    "you trust it: it is added to ~/.ssh/known_hosts."),
                Fingerprint(theme, key.Fingerprint),
            };
            return (Panel(theme, body, Buttons(
                    (ButtonVariant.Secondary, "Cancel", () => close.Close(HostKeyDecision.Reject)),
                    (ButtonVariant.Secondary, "Connect once", () => close.Close(HostKeyDecision.AcceptOnce)),
                    (ButtonVariant.Primary, "Trust and connect", () => close.Close(HostKeyDecision.Trust)))),
                null, null, () => close.Close(HostKeyDecision.Reject));
        }

        /// <summary>The man-in-the-middle warning. Cancel is the easy answer; replacing the key
        /// takes typing the host's name.</summary>
        private static (Element, Element?, Action?, Action) ChangedKey(SshHostKey key, IDialogController<HostKeyDecision> close, Theme theme)
        {
            var confirm = Field(theme, $"To replace it, type {key.Host}", masked: false);
            var warning = new Color(232, 72, 72);
            var body = new List<Element>
            {
                Text.New($"{key.Host}'s key has changed").FontSize(16f).FontWeight(FontWeight.Bold).TextColor(warning),
                Message(theme, "Someone may be between you and the host, reading what you send. Or the host was " +
                    "reinstalled. Do not connect unless you know which."),
                Message(theme, "Now: " + key.Type),
                Fingerprint(theme, key.Fingerprint),
                Message(theme, "In ~/.ssh/known_hosts:"),
            };
            body.AddRange(key.FingerprintsOnFile.Select(f => Fingerprint(theme, f)));
            body.Add(confirm);
            void Replace()
            {
                if (confirm.Text.Trim().Equals(key.Host, StringComparison.OrdinalIgnoreCase)) close.Close(HostKeyDecision.Trust);
            }
            return (Panel(theme, body, Buttons(
                    (ButtonVariant.Danger, "Replace the key", Replace),
                    (ButtonVariant.Primary, "Cancel", () => close.Close(HostKeyDecision.Reject)))),
                null, null, () => close.Close(HostKeyDecision.Reject));
        }

        private Task<string?> Secret(CancellationToken cancellation, string title, string? message, string label) =>
            Ask<string?>(cancellation, (close, theme) =>
            {
                var field = Field(theme, label, masked: true);
                var body = new List<Element> { Title(theme, title) };
                if (message is not null) body.Add(Message(theme, message));
                body.Add(field);
                return (Panel(theme, body, Buttons(
                        (ButtonVariant.Secondary, "Cancel", () => close.Close(null)),
                        (ButtonVariant.Primary, "OK", () => close.Close(field.Text)))),
                    field, () => close.Close(field.Text), () => close.Close(null));
            });

        /// <summary>
        /// Shows a dialog over the terminal and waits for its answer. <paramref name="build"/>
        /// gives the panel, the element to focus, what Enter does (null: nothing) and what
        /// Escape does. The default answer when the terminal has gone or the wait is cancelled.
        /// </summary>
        private Task<T?> Ask<T>(CancellationToken cancellation,
            Func<IDialogController<T>, Theme, (Element Panel, Element? Focus, Action? Enter, Action Escape)> build)
        {
            var answer = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Application.InvokeOnMainThread(() =>
            {
                var terminal = app.Terminal;
                if (terminal?.OverlayHost is not { } host || cancellation.IsCancellationRequested)
                {
                    answer.TrySetResult(default);
                    return;
                }
                IDialogController<T>? controller = null;
                Element? focus = null;
                var shown = Dialog.Show<T>(host, close =>
                {
                    controller = close;
                    var (panel, first, enter, escape) = build(close, host.ActiveTheme ?? Theme.Dark);
                    focus = first;
                    panel.OnKeyDown(e =>
                    {
                        if (e.Key == KeyCode.Enter && enter is not null) { enter(); e.StopPropagation = true; }
                        else if (e.Key == KeyCode.Escape) { escape(); e.StopPropagation = true; }
                    });
                    return panel;
                }, new DialogOptions { DismissOnScrimClick = false });
                focus?.Focus();
                var registration = cancellation.Register(() => Application.InvokeOnMainThread(() => controller?.Close(default!)));
                shown.ContinueWith(t =>
                {
                    registration.Dispose();
                    answer.TrySetResult(t.Result);
                    Application.InvokeOnMainThread(() => app.Terminal?.Focus());
                }, TaskScheduler.Default);
            });
            return answer.Task;
        }

        private static Element Panel(Theme theme, List<Element> body, Element buttons)
        {
            var column = Flex.Column(gap: 14);
            column.ReplaceChildren([.. body, buttons]);
            return Box.New()
                .Width(460).Padding(new Thickness(20, 24, 20, 24)).CornerRadius(12f)
                .Background(theme.Surface).BorderColor(theme.Border).BorderWidth(1f)
                .BoxShadow(0, 18, 48, new Color(0, 0, 0, 90))
                .Children(column);
        }

        private static Element Buttons(params (ButtonVariant Variant, string Label, Action OnClick)[] buttons)
        {
            var row = Flex.Row(gap: 8).JustifyContent(JustifyContent.FlexEnd).AlignItems(AlignItems.Center);
            row.ReplaceChildren([.. buttons.Select(b =>
            {
                var button = Button.New(b.Label, b.Variant);
                button.On<ClickEvent>(e => { e.StopPropagation = true; b.OnClick(); });
                return (Element)button;
            })]);
            return row;
        }

        private static TextInputElement Field(Theme theme, string placeholder, bool masked)
        {
            var field = TextInput.New().PlaceholderText(placeholder);
            field.Masked = masked;
            return field;
        }

        private static Element Title(Theme theme, string text) =>
            Text.New(text).FontSize(16f).FontWeight(FontWeight.Bold).TextColor(theme.TextColor);

        private static Element Message(Theme theme, string text) =>
            Text.New(text).FontSize(13f).TextColor(theme.Secondary).LineHeight(19f);

        /// <summary>A fingerprint, in the monospace face and selectable, to compare and copy.</summary>
        private static Element Fingerprint(Theme theme, string fingerprint)
        {
            var text = Text.New(fingerprint).FontSize(13f).FontFamily(FontFamilies.Monospace).TextColor(theme.TextColor);
            text.Selectable = true;
            return text;
        }
    }
}
