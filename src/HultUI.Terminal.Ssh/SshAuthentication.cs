using System.Security.Cryptography;
using Renci.SshNet;
using Renci.SshNet.Common;
using SshNet.Agent;

namespace HultUI.Terminal.Ssh;

/// <summary>Asks for the user's password. <see cref="Attempt"/> counts from 1, so a dialog can say
/// it is asking again.</summary>
public sealed record SshPasswordPrompt(string User, string Host, int Attempt);

/// <summary>Asks for a private key file's passphrase.</summary>
public sealed record SshPassphrasePrompt(string KeyFile, int Attempt);

/// <summary>The server's own questions (keyboard-interactive): often a one-time code. Shown as the
/// server wrote them; <see cref="SshQuestion.Echo"/> false means a hidden answer.</summary>
public sealed record SshInteractivePrompt(string User, string Host, string Instruction,
    IReadOnlyList<SshQuestion> Questions);

/// <summary>One of a server's keyboard-interactive questions.</summary>
public readonly record struct SshQuestion(string Text, bool Echo);

/// <summary>
/// One way to sign in. A connection tries those it was given in order, each the server accepts,
/// until one succeeds.
/// </summary>
/// <remarks>
/// <para><b>Prompts</b> are called on a background thread, the one signing in, which waits for the
/// answer: show the dialog on the UI thread and complete the task from there. A null answer
/// cancels that way of signing in, and the next one is tried.</para>
/// <para><b>Nothing is kept.</b> A password or passphrase is used for this sign-in and not stored;
/// remembering one (in the platform's keyring, never a settings file) is the application's
/// choice, made in the prompt.</para>
/// </remarks>
public abstract class SshAuthentication
{
    private protected SshAuthentication() { }

    /// <summary>A password, asked for when the server accepts passwords; asked again up to
    /// <paramref name="attempts"/> times while it is wrong.</summary>
    public static SshAuthentication Password(Func<SshPasswordPrompt, CancellationToken, Task<string?>> ask, int attempts = 3)
    {
        ArgumentNullException.ThrowIfNull(ask);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        return new PasswordWay(ask, attempts);
    }

    /// <summary>A private key file (OpenSSH, PEM or PuTTY format). Its passphrase is asked for
    /// only when the file has one, and again while it is wrong; without
    /// <paramref name="passphrase"/> an encrypted file is skipped. A missing file is skipped
    /// too.</summary>
    public static SshAuthentication KeyFile(string path,
        Func<SshPassphrasePrompt, CancellationToken, Task<string?>>? passphrase = null, int attempts = 3)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        return new KeyFileWay(path, passphrase, attempts);
    }

    /// <summary>The keys in the user's SSH agent. By default the agent at <c>SSH_AUTH_SOCK</c>; on
    /// Windows without it, Windows' OpenSSH agent and then Pageant. <paramref name="socket"/> names
    /// another: a Unix socket path, or a Windows pipe name. No agent, or none with keys, is
    /// skipped.</summary>
    public static SshAuthentication Agent(string? socket = null) => new AgentWay(socket);

    /// <summary>The server's own questions, answered by <paramref name="answer"/> in order.</summary>
    public static SshAuthentication KeyboardInteractive(
        Func<SshInteractivePrompt, CancellationToken, Task<IReadOnlyList<string>?>> answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return new InteractiveWay(answer);
    }

    internal abstract AuthenticationMethod Create(string user, string host, CancellationToken cancellation);

    /// <summary>Waits for a prompt's answer on the signing-in thread.</summary>
    private static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private sealed class PasswordWay(Func<SshPasswordPrompt, CancellationToken, Task<string?>> ask, int attempts) : SshAuthentication
    {
        private Func<SshPasswordPrompt, CancellationToken, Task<string?>> Ask { get; } = ask;
        private int Attempts { get; } = attempts;

        internal override AuthenticationMethod Create(string user, string host, CancellationToken cancellation) =>
            new Method(user, host, this, cancellation);

        /// <summary>SSH.NET's password method takes the password up front; this asks only when the
        /// server takes passwords, and asks again when it was wrong.</summary>
        private sealed class Method(string user, string host, PasswordWay way, CancellationToken cancellation)
            : AuthenticationMethod(user)
        {
            public override string Name => "password";

            public override AuthenticationResult Authenticate(Session session)
            {
                for (int attempt = 1; attempt <= way.Attempts; attempt++)
                {
                    var password = Wait(way.Ask(new SshPasswordPrompt(Username, host, attempt), cancellation));
                    if (password is null) return AuthenticationResult.Failure;
                    using var inner = new PasswordAuthenticationMethod(Username, password);
                    var result = inner.Authenticate(session);
                    AllowedAuthentications = inner.AllowedAuthentications;
                    if (result != AuthenticationResult.Failure) return result;
                }
                return AuthenticationResult.Failure;
            }
        }
    }

    private sealed class KeyFileWay(string path, Func<SshPassphrasePrompt, CancellationToken, Task<string?>>? passphrase, int attempts)
        : SshAuthentication
    {
        private string Path { get; } = path;
        private Func<SshPassphrasePrompt, CancellationToken, Task<string?>>? Passphrase { get; } = passphrase;
        private int Attempts { get; } = attempts;

        internal override AuthenticationMethod Create(string user, string host, CancellationToken cancellation) =>
            new Method(user, this, cancellation);

        private sealed class Method(string user, KeyFileWay way, CancellationToken cancellation) : AuthenticationMethod(user)
        {
            public override string Name => "publickey";

            public override AuthenticationResult Authenticate(Session session)
            {
                var key = Load();
                if (key is null) return AuthenticationResult.Failure;
                try
                {
                    using var inner = new PrivateKeyAuthenticationMethod(Username, key);
                    var result = inner.Authenticate(session);
                    AllowedAuthentications = inner.AllowedAuthentications;
                    return result;
                }
                finally { (key as IDisposable)?.Dispose(); }
            }

            /// <summary>The key, asking for its passphrase when it has one. PKCS#8 files are read
            /// by .NET first (<see cref="Pkcs8Keys"/>); everything else by SSH.NET.</summary>
            private IPrivateKeySource? Load()
            {
                if (!File.Exists(way.Path)) return null;
                var text = File.ReadAllText(way.Path);
                bool pkcs8 = Pkcs8Keys.Is(text), encrypted = Pkcs8Keys.IsEncrypted(text);
                if (pkcs8 && !encrypted && Pkcs8Keys.Read(text, null) is { } plain) return plain;
                if (!encrypted)
                {
                    try { return new PrivateKeyFile(way.Path); }
                    catch (SshPassPhraseNullOrEmptyException) { } // it has one: ask
                }
                if (way.Passphrase is null) return null;
                for (int attempt = 1; attempt <= way.Attempts; attempt++)
                {
                    var phrase = Wait(way.Passphrase(new SshPassphrasePrompt(way.Path, attempt), cancellation));
                    if (phrase is null) return null;
                    // The file needs a passphrase, so failing now means this one is wrong,
                    // whichever library says so.
                    try { return (pkcs8 ? Pkcs8Keys.Read(text, phrase) : null) ?? new PrivateKeyFile(way.Path, phrase); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { }
                }
                return null;
            }
        }
    }

    private sealed class AgentWay(string? socket) : SshAuthentication
    {
        internal override AuthenticationMethod Create(string user, string host, CancellationToken cancellation) =>
            new Method(user, socket);

        private sealed class Method(string user, string? socket) : AuthenticationMethod(user)
        {
            public override string Name => "publickey";

            public override AuthenticationResult Authenticate(Session session)
            {
                var keys = Identities();
                if (keys.Length == 0) return AuthenticationResult.Failure;
                using var inner = new PrivateKeyAuthenticationMethod(Username, keys);
                var result = inner.Authenticate(session);
                AllowedAuthentications = inner.AllowedAuthentications;
                return result;
            }

            /// <summary>The agent's keys; none when there is no agent to ask.</summary>
            private IPrivateKeySource[] Identities()
            {
                var timeout = TimeSpan.FromSeconds(5);
                var named = socket ?? Environment.GetEnvironmentVariable("SSH_AUTH_SOCK");
                IEnumerable<Func<SshAgent>> agents = named is not null
                    ? [() => new SshAgent(named, timeout)]
                    : OperatingSystem.IsWindows()
                        ? [() => new SshAgent("openssh-ssh-agent", timeout), () => new Pageant(timeout)]
                        : [];
                foreach (var agent in agents)
                {
                    try
                    {
                        var keys = agent().RequestIdentities();
                        if (keys.Length > 0) return keys;
                    }
                    catch (Exception ex) when (ex is IOException or TimeoutException or SshAgentException
                        or UnauthorizedAccessException or System.Net.Sockets.SocketException or InvalidOperationException) { }
                }
                return [];
            }
        }
    }

    private sealed class InteractiveWay(Func<SshInteractivePrompt, CancellationToken, Task<IReadOnlyList<string>?>> answer)
        : SshAuthentication
    {
        internal override AuthenticationMethod Create(string user, string host, CancellationToken cancellation)
        {
            var method = new KeyboardInteractiveAuthenticationMethod(user);
            method.AuthenticationPrompt += (_, e) =>
            {
                var questions = e.Prompts.Select(p => new SshQuestion(p.Request, p.IsEchoed)).ToList();
                var answers = questions.Count == 0 ? [] // an instruction alone: nothing to answer
                    : Wait(answer(new SshInteractivePrompt(user, host, e.Instruction ?? "", questions), cancellation));
                // A null answer cannot abort the exchange; empty answers fail it.
                for (int i = 0; i < e.Prompts.Count; i++)
                    e.Prompts[i].Response = answers is not null && i < answers.Count ? answers[i] : "";
            };
            return method;
        }
    }
}
