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
/// until one succeeds; when none does, <see cref="SshSignInException"/> says what each did.
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

    /// <summary>A private key file (OpenSSH, PEM, PuTTY or PKCS#8 format). Its passphrase is asked
    /// for only when the file has one, and again while it is wrong; without
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

    /// <summary>SSH's name for the method: what the server lists when it says what it takes.</summary>
    internal abstract string MethodName { get; }

    /// <summary>What a person would call this way, for <see cref="SshSignInException"/>.</summary>
    internal abstract string Label { get; }

    internal abstract AuthenticationMethod Create(string user, string host, SignInLog.Entry entry, CancellationToken cancellation);

    /// <summary>Waits for a prompt's answer on the signing-in thread.</summary>
    private static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

    /// <summary>The base of the methods here: a way whose own checks fail without asking the
    /// server still learns what the server takes, for the report.</summary>
    private abstract class Way(string user, SignInLog.Entry entry) : AuthenticationMethod(user)
    {
        protected SignInLog.Entry Entry { get; } = entry;

        /// <summary>Runs SSH.NET's own method for this way, and takes what the server said it
        /// accepts.</summary>
        protected AuthenticationResult Run(AuthenticationMethod inner, Session session)
        {
            using (inner)
            {
                var result = inner.Authenticate(session);
                Entry.Log.Learn(inner.AllowedAuthentications);
                AllowedAuthentications = inner.AllowedAuthentications;
                return result;
            }
        }

        /// <summary>Failed before asking the server: note why, and ask the server what it takes
        /// (one round trip, only on this path). A server that takes nobody's word signs in here.</summary>
        protected AuthenticationResult Skipped(Session session, string why)
        {
            Entry.Result = why;
            return Entry.Log.AskServer(session, Username) ? AuthenticationResult.Success : AuthenticationResult.Failure;
        }
    }

    private sealed class PasswordWay(Func<SshPasswordPrompt, CancellationToken, Task<string?>> ask, int attempts) : SshAuthentication
    {
        private Func<SshPasswordPrompt, CancellationToken, Task<string?>> Ask { get; } = ask;
        private int Attempts { get; } = attempts;

        internal override string MethodName => "password";
        internal override string Label => "password";

        internal override AuthenticationMethod Create(string user, string host, SignInLog.Entry entry, CancellationToken cancellation) =>
            new Method(user, host, this, entry, cancellation);

        /// <summary>SSH.NET's password method takes the password up front; this asks only when the
        /// server takes passwords, and asks again when it was wrong.</summary>
        private sealed class Method(string user, string host, PasswordWay way, SignInLog.Entry entry, CancellationToken cancellation)
            : Way(user, entry)
        {
            public override string Name => "password";

            public override AuthenticationResult Authenticate(Session session)
            {
                for (int attempt = 1; attempt <= way.Attempts; attempt++)
                {
                    var password = Wait(way.Ask(new SshPasswordPrompt(Username, host, attempt), cancellation));
                    if (password is null)
                    {
                        Entry.Result = attempt == 1 ? "not given" : "refused, then not given again";
                        return AuthenticationResult.Failure;
                    }
                    var result = Run(new PasswordAuthenticationMethod(Username, password), session);
                    if (result != AuthenticationResult.Failure) return result;
                    Entry.Result = attempt == 1 ? "refused" : $"refused {attempt} times";
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

        internal override string MethodName => "publickey";
        internal override string Label => "key file " + Path;

        internal override AuthenticationMethod Create(string user, string host, SignInLog.Entry entry, CancellationToken cancellation) =>
            new Method(user, this, entry, cancellation);

        private sealed class Method(string user, KeyFileWay way, SignInLog.Entry entry, CancellationToken cancellation) : Way(user, entry)
        {
            public override string Name => "publickey";

            public override AuthenticationResult Authenticate(Session session)
            {
                var (key, why) = Load();
                if (key is null) return Skipped(session, why!);
                try
                {
                    var result = Run(new PrivateKeyAuthenticationMethod(Username, key), session);
                    if (result == AuthenticationResult.Failure) Entry.Result = "refused";
                    return result;
                }
                finally { (key as IDisposable)?.Dispose(); }
            }

            /// <summary>The key, asking for its passphrase when it has one; or why there is none.
            /// PKCS#8 files are read by .NET first (<see cref="Pkcs8Keys"/>); everything else by
            /// SSH.NET.</summary>
            private (IPrivateKeySource? Key, string? Why) Load()
            {
                if (!File.Exists(way.Path)) return (null, "not found");
                string text;
                try { text = File.ReadAllText(way.Path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (null, "could not be read: " + ex.Message); }
                bool pkcs8 = Pkcs8Keys.Is(text), encrypted = Pkcs8Keys.IsEncrypted(text);
                try
                {
                    if (pkcs8 && !encrypted && Pkcs8Keys.Read(text, null) is { } plain) return (plain, null);
                    if (!encrypted)
                    {
                        try { return (new PrivateKeyFile(way.Path), null); }
                        catch (SshPassPhraseNullOrEmptyException) { } // it has one: ask
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return (null, "not a key this can read: " + ex.Message);
                }
                if (way.Passphrase is null) return (null, "has a passphrase, and nothing to ask for it");
                for (int attempt = 1; attempt <= way.Attempts; attempt++)
                {
                    var phrase = Wait(way.Passphrase(new SshPassphrasePrompt(way.Path, attempt), cancellation));
                    if (phrase is null) return (null, attempt == 1 ? "passphrase not given" : "passphrase wrong, then not given again");
                    // The file needs a passphrase, so failing now means this one is wrong,
                    // whichever library says so.
                    try { return ((pkcs8 ? Pkcs8Keys.Read(text, phrase) : null) ?? new PrivateKeyFile(way.Path, phrase), null); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { }
                }
                return (null, way.Attempts == 1 ? "passphrase wrong" : $"passphrase wrong {way.Attempts} times");
            }
        }
    }

    private sealed class AgentWay(string? socket) : SshAuthentication
    {
        internal override string MethodName => "publickey";
        internal override string Label => "SSH agent";

        internal override AuthenticationMethod Create(string user, string host, SignInLog.Entry entry, CancellationToken cancellation) =>
            new Method(user, socket, entry);

        private sealed class Method(string user, string? socket, SignInLog.Entry entry) : Way(user, entry)
        {
            public override string Name => "publickey";

            public override AuthenticationResult Authenticate(Session session)
            {
                var (keys, why) = Identities();
                if (keys.Length == 0) return Skipped(session, why);
                var result = Run(new PrivateKeyAuthenticationMethod(Username, keys), session);
                if (result == AuthenticationResult.Failure) Entry.Result = keys.Length == 1 ? "its key was refused" : $"its {keys.Length} keys were refused";
                return result;
            }

            /// <summary>The agent's keys; none when there is no agent to ask, and why.</summary>
            private (IPrivateKeySource[] Keys, string Why) Identities()
            {
                var timeout = TimeSpan.FromSeconds(5);
                var named = socket ?? Environment.GetEnvironmentVariable("SSH_AUTH_SOCK");
                IEnumerable<(string Name, Func<SshAgent> Open)> agents = named is not null
                    ? [(named, () => new SshAgent(named, timeout))]
                    : OperatingSystem.IsWindows()
                        ? [("Windows' OpenSSH agent", () => new SshAgent("openssh-ssh-agent", timeout)), ("Pageant", () => new Pageant(timeout))]
                        : [];
                var looked = new List<string>();
                foreach (var (name, open) in agents)
                {
                    try
                    {
                        var keys = open().RequestIdentities();
                        if (keys.Length > 0) return (keys, "");
                        looked.Add(name + " holds no keys");
                    }
                    catch (Exception ex) when (ex is IOException or TimeoutException or SshAgentException
                        or UnauthorizedAccessException or System.Net.Sockets.SocketException or InvalidOperationException)
                    {
                        looked.Add(name + " is not running");
                    }
                }
                return ([], looked.Count == 0 ? "none (SSH_AUTH_SOCK is not set)" : string.Join("; ", looked));
            }
        }
    }

    private sealed class InteractiveWay(Func<SshInteractivePrompt, CancellationToken, Task<IReadOnlyList<string>?>> answer)
        : SshAuthentication
    {
        private Func<SshInteractivePrompt, CancellationToken, Task<IReadOnlyList<string>?>> Answer { get; } = answer;

        internal override string MethodName => "keyboard-interactive";
        internal override string Label => "the server's questions";

        internal override AuthenticationMethod Create(string user, string host, SignInLog.Entry entry, CancellationToken cancellation) =>
            new Method(user, host, this, entry, cancellation);

        private sealed class Method(string user, string host, InteractiveWay way, SignInLog.Entry entry, CancellationToken cancellation)
            : Way(user, entry)
        {
            public override string Name => "keyboard-interactive";

            public override AuthenticationResult Authenticate(Session session)
            {
                bool asked = false, cancelled = false;
                var inner = new KeyboardInteractiveAuthenticationMethod(Username);
                inner.AuthenticationPrompt += (_, e) =>
                {
                    asked |= e.Prompts.Count > 0;
                    var questions = e.Prompts.Select(p => new SshQuestion(p.Request, p.IsEchoed)).ToList();
                    var answers = questions.Count == 0 ? [] // an instruction alone: nothing to answer
                        : Wait(way.Answer(new SshInteractivePrompt(Username, host, e.Instruction ?? "", questions), cancellation));
                    cancelled |= answers is null;
                    // A null answer cannot abort the exchange; empty answers fail it.
                    for (int i = 0; i < e.Prompts.Count; i++)
                        e.Prompts[i].Response = answers is not null && i < answers.Count ? answers[i] : "";
                };
                var result = Run(inner, session);
                if (result == AuthenticationResult.Failure)
                    Entry.Result = !asked ? "refused without asking" : cancelled ? "not answered" : "answers refused";
                return result;
            }
        }
    }
}

/// <summary>What each way of signing in did, and what the server takes: what
/// <see cref="SshSignInException"/> reports.</summary>
internal sealed class SignInLog
{
    private readonly List<Entry> _entries = [];

    /// <summary>What the server said it takes, once anything asked it.</summary>
    public string[]? ServerAccepts { get; private set; }

    public Entry Add(SshAuthentication way)
    {
        var entry = new Entry(this, way.Label, way.MethodName);
        _entries.Add(entry);
        return entry;
    }

    /// <summary>Takes the server's list, without "none": not a way to sign in, though some
    /// servers list it.</summary>
    public void Learn(string[]? allowed)
    {
        if (allowed?.Where(m => m != "none").ToArray() is { Length: > 0 } methods) ServerAccepts = methods;
    }

    /// <summary>Asks the server what it takes, with SSH's "none" method, when nothing has yet.
    /// True when it lets the user in without anything.</summary>
    public bool AskServer(Session session, string user)
    {
        if (ServerAccepts is not null) return false;
        try
        {
            using var none = new NoneAuthenticationMethod(user);
            var result = none.Authenticate(session);
            Learn(none.AllowedAuthentications);
            return result == AuthenticationResult.Success;
        }
        catch (SshException) { return false; }
    }

    /// <summary>One line per way, in the order given.</summary>
    public IReadOnlyList<string> Lines() => _entries.Select(e => $"{e.Label}: {e.Result ?? NotTried(e)}").ToList();

    private string NotTried(Entry entry) =>
        ServerAccepts is { } accepts && !accepts.Contains(entry.MethodName, StringComparer.Ordinal)
            ? "this server does not take it"
            : "not tried";

    public sealed class Entry(SignInLog log, string label, string methodName)
    {
        public SignInLog Log { get; } = log;
        public string Label { get; } = label;
        public string MethodName { get; } = methodName;

        /// <summary>What happened; null while it has not run.</summary>
        public string? Result { get; set; }
    }
}

/// <summary>
/// No way of signing in was accepted. <see cref="Exception.Message"/> says what each did, and what
/// the server takes, one line each: what a terminal can show as it is.
/// </summary>
public sealed class SshSignInException : Exception
{
    internal SshSignInException(string user, string host, IReadOnlyList<string> serverAccepts, IReadOnlyList<string> attempts,
        Exception innerException)
        : base(Describe(user, host, serverAccepts, attempts), innerException)
    {
        User = user;
        Host = host;
        ServerAccepts = serverAccepts;
        Attempts = attempts;
    }

    /// <summary>The user signing in.</summary>
    public string User { get; }

    /// <summary>The host.</summary>
    public string Host { get; }

    /// <summary>SSH's names for the methods the server takes (<c>publickey</c>, <c>password</c>,
    /// <c>keyboard-interactive</c>); empty when it was never asked.</summary>
    public IReadOnlyList<string> ServerAccepts { get; }

    /// <summary>What each way given did, in order: <c>"key file ~/.ssh/id_ed25519: not
    /// found"</c>.</summary>
    public IReadOnlyList<string> Attempts { get; }

    private static string Describe(string user, string host, IReadOnlyList<string> accepts, IReadOnlyList<string> attempts)
    {
        var lines = new List<string> { $"Could not sign in to {host} as {user}." };
        if (accepts.Count > 0) lines.Add("The server takes: " + string.Join(", ", accepts) + ".");
        lines.AddRange(attempts.Select(a => "  " + a));
        return string.Join("\n", lines);
    }
}
