using System.Text;
using HultUI.Headless;
using Renci.SshNet.Common;
using Xunit;

namespace HultUI.Terminal.Ssh.Tests;

/// <summary>
/// An SSH connection against a real SSH server in the test process: whom it trusts, how it signs
/// in, the shell's terminal, the exit code, flow control, and the ends a session can come to.
/// </summary>
public sealed class SshConnectionTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly TestSshServer _server = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("hultui-ssh-").FullName;
    private readonly List<IDisposable> _owned = [];

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
        _server.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private string KnownHostsPath => Path.Combine(_dir, "ssh", "known_hosts");

    private SshAuthentication ThePassword => SshAuthentication.Password((_, _) => Task.FromResult(_server.Password));

    private SshConnectOptions Options(params SshAuthentication[] ways) => new()
    {
        Host = "127.0.0.1",
        Port = _server.Port,
        User = _server.User,
        Authentication = ways.Length > 0 ? ways : [ThePassword],
        KnownHosts = new KnownHostsFile(KnownHostsPath),
        VerifyHostKey = (_, _) => Task.FromResult(HostKeyDecision.AcceptOnce),
        ConnectTimeout = Patience,
        KeepAlive = TimeSpan.Zero,
    };

    private SshConnection Connect(SshConnectOptions options)
    {
        SshConnection connection;
        try { connection = SshConnection.ConnectAsync(options).WaitAsync(Patience).GetAwaiter().GetResult(); }
        catch (Exception ex) when (!_server.Errors.IsEmpty && ex is not (SshHostKeyRejectedException or SshSignInException))
        {
            throw new InvalidOperationException("the server said: " + string.Join("\n", _server.Errors), ex);
        }
        _owned.Add(connection);
        return connection;
    }

    private Reader Read(SshConnection connection)
    {
        var reader = new Reader(connection.Output);
        reader.WaitFor("ready");
        return reader;
    }

    private static void Send(SshConnection connection, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        connection.Input.Write(bytes, 0, bytes.Length);
        connection.Input.Flush();
    }

    private static Task<int?> ExitOf(SshConnection connection)
    {
        var exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Exited += code => exited.TrySetResult(code);
        return exited.Task;
    }

    private string HostLine(byte[] key) =>
        $"[127.0.0.1]:{_server.Port} {KnownHostsFile.KeyType(key)} {Convert.ToBase64String(key)}";

    // ──── Whom it trusts ────

    [Fact]
    public void AnUnknownHost_IsAskedAbout_AndTrustingIt_RecordsIt()
    {
        var asked = new List<SshHostKey>();
        var options = Options();
        Connect(options with
        {
            VerifyHostKey = (key, _) => { asked.Add(key); return Task.FromResult(HostKeyDecision.Trust); },
        });

        var key = Assert.Single(asked);
        Assert.Equal(HostKeyStatus.Unknown, key.Status);
        Assert.Equal("ecdsa-sha2-nistp256", key.Type);
        Assert.Equal(KnownHostsFile.Fingerprint(_server.HostKeyBlob), key.Fingerprint);
        Assert.Equal([HostLine(_server.HostKeyBlob)], File.ReadAllLines(KnownHostsPath));

        // Next time it is known, and not asked about.
        var again = Connect(options with { VerifyHostKey = (_, _) => throw new InvalidOperationException("asked") });
        Assert.Equal(HostKeyStatus.Known, again.HostKey.Status);
    }

    [Fact]
    public void AChangedKey_IsAskedAbout_WithTheOneOnFile_AndRejected_ConnectsNothing()
    {
        var old = TestKeys.Blob(TestKeys.New());
        Directory.CreateDirectory(Path.GetDirectoryName(KnownHostsPath)!);
        File.WriteAllText(KnownHostsPath, HostLine(old) + "\n");
        SshHostKey? asked = null;

        var rejected = Assert.Throws<SshHostKeyRejectedException>(() => Connect(Options() with
        {
            VerifyHostKey = (key, _) => { asked = key; return Task.FromResult(HostKeyDecision.Reject); },
        }));

        Assert.Equal(HostKeyStatus.Changed, rejected.HostKey.Status);
        Assert.Equal([KnownHostsFile.Fingerprint(old)], asked!.FingerprintsOnFile);
        Assert.Equal([HostLine(old)], File.ReadAllLines(KnownHostsPath));
        Assert.Equal(0, _server.PasswordAttempts); // nothing was sent to a host not trusted
    }

    [Fact]
    public void TrustingAChangedKey_ReplacesTheOneOnFile()
    {
        var old = TestKeys.Blob(TestKeys.New());
        Directory.CreateDirectory(Path.GetDirectoryName(KnownHostsPath)!);
        File.WriteAllText(KnownHostsPath, HostLine(old) + "\n");

        Connect(Options() with { VerifyHostKey = (_, _) => Task.FromResult(HostKeyDecision.Trust) });

        Assert.Equal([HostLine(_server.HostKeyBlob)], File.ReadAllLines(KnownHostsPath));
    }

    [Fact]
    public void WithoutAVerifier_AnUnknownKey_DoesNotConnect()
    {
        // SSH.NET trusts every key unless told otherwise.
        var rejected = Assert.Throws<SshHostKeyRejectedException>(() => Connect(Options() with { VerifyHostKey = null }));
        Assert.Equal(HostKeyStatus.Unknown, rejected.HostKey.Status);
        Assert.Equal(0, _server.PasswordAttempts);
    }

    [Fact]
    public void ARevokedKey_NeverConnects_AndIsNotAskedAbout()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(KnownHostsPath)!);
        File.WriteAllText(KnownHostsPath, "@revoked * ecdsa-sha2-nistp256 " + Convert.ToBase64String(_server.HostKeyBlob) + "\n");

        var rejected = Assert.Throws<SshHostKeyRejectedException>(() => Connect(Options() with
        {
            VerifyHostKey = (_, _) => throw new InvalidOperationException("asked"),
        }));
        Assert.Equal(HostKeyStatus.Revoked, rejected.HostKey.Status);
    }

    [Fact]
    public void AcceptingOnce_RemembersNothing()
    {
        var connection = Connect(Options());
        Assert.Equal(HostKeyStatus.Unknown, connection.HostKey.Status);
        Assert.False(File.Exists(KnownHostsPath));
    }

    // ──── Signing in ────

    [Fact]
    public void APassword_IsAskedAgain_WhileItIsWrong()
    {
        var prompts = new List<SshPasswordPrompt>();
        Connect(Options(SshAuthentication.Password((prompt, _) =>
        {
            prompts.Add(prompt);
            return Task.FromResult<string?>(prompt.Attempt == 1 ? "wrong" : "secret");
        })));

        Assert.Equal([1, 2], prompts.Select(p => p.Attempt));
        Assert.All(prompts, p => Assert.Equal(("tester", "127.0.0.1"), (p.User, p.Host)));
        Assert.Equal(2, _server.PasswordAttempts);
    }

    [Fact]
    public void CancellingThePassword_FailsTheSignIn_AndSaysSo()
    {
        int asked = 0;
        var failed = Assert.Throws<SshSignInException>(() => Connect(Options(SshAuthentication.Password((_, _) =>
        {
            asked++;
            return Task.FromResult<string?>(null);
        }))));
        Assert.Equal(1, asked);
        Assert.Equal(0, _server.PasswordAttempts);
        Assert.Equal(["password: not given"], failed.Attempts);
    }

    [Fact]
    public void WrongPasswords_AreCounted()
    {
        var failed = Assert.Throws<SshSignInException>(() => Connect(Options(SshAuthentication.Password(
            (_, _) => Task.FromResult<string?>("wrong")))));
        Assert.Equal(["password: refused 3 times"], failed.Attempts);
        Assert.Equal(3, _server.PasswordAttempts);
    }

    /// <summary>
    /// What a first try with a host looks like: no agent, no default key files, and a server that
    /// would take a password nobody offered. "Permission denied (publickey)" said none of that.
    /// </summary>
    [Fact]
    public void NothingToOffer_SaysWhatEachWayFound_AndWhatTheServerTakes()
    {
        var agent = "hultui-no-such-agent-" + Guid.NewGuid().ToString("N");
        var missing = Path.Combine(_dir, "id_ed25519");
        var failed = Assert.Throws<SshSignInException>(() => Connect(Options(
            SshAuthentication.Agent(agent), SshAuthentication.KeyFile(missing))));

        Assert.Contains("password", failed.ServerAccepts);
        Assert.Contains("publickey", failed.ServerAccepts);
        Assert.Equal([$"SSH agent: {agent} is not running", $"key file {missing}: not found"], failed.Attempts);
        Assert.Equal(
            $"Could not sign in to 127.0.0.1 as tester.\nThe server takes: {string.Join(", ", failed.ServerAccepts)}.\n" +
            $"  SSH agent: {agent} is not running\n  key file {missing}: not found",
            failed.Message);
    }

    [Fact]
    public void AWayTheServerDoesNotTake_IsNotAskedFor_AndSaysSo()
    {
        _server.Methods = ["publickey"];
        bool asked = false;
        var missing = Path.Combine(_dir, "id_rsa");
        var failed = Assert.Throws<SshSignInException>(() => Connect(Options(
            SshAuthentication.KeyFile(missing),
            SshAuthentication.Password((_, _) => { asked = true; return Task.FromResult<string?>("secret"); }))));

        Assert.False(asked);
        Assert.Equal(["publickey"], failed.ServerAccepts);
        Assert.Equal([$"key file {missing}: not found", "password: this server does not take it"], failed.Attempts);
    }

    [Fact]
    public void AKeyFileWithAPassphrase_AndNothingToAskForIt_SaysSo()
    {
        using var key = TestKeys.New();
        _server.Password = null;
        _server.AuthorizedKey = TestKeys.Blob(key);
        var path = TestKeys.File(key, passphrase: "secret phrase");
        try
        {
            var failed = Assert.Throws<SshSignInException>(() => Connect(Options(SshAuthentication.KeyFile(path))));
            Assert.Equal([$"key file {path}: has a passphrase, and nothing to ask for it"], failed.Attempts);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AKeyFile_SignsIn()
    {
        using var key = TestKeys.New();
        _server.Password = null;
        _server.AuthorizedKey = TestKeys.Blob(key);
        var path = TestKeys.File(key);
        try { Connect(Options(SshAuthentication.KeyFile(path))); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnEncryptedKeyFile_AsksForItsPassphrase_AgainWhileItIsWrong()
    {
        using var key = TestKeys.New();
        _server.Password = null;
        _server.AuthorizedKey = TestKeys.Blob(key);
        var path = TestKeys.File(key, passphrase: "open sesame");
        var prompts = new List<SshPassphrasePrompt>();
        try
        {
            Connect(Options(SshAuthentication.KeyFile(path, (prompt, _) =>
            {
                prompts.Add(prompt);
                return Task.FromResult<string?>(prompt.Attempt == 1 ? "wrong" : "open sesame");
            })));
        }
        finally { File.Delete(path); }
        Assert.Equal([1, 2], prompts.Select(p => p.Attempt));
        Assert.All(prompts, p => Assert.Equal(path, p.KeyFile));
    }

    [Fact]
    public void APkcs8Key_SignsIn()
    {
        // Read by .NET, as every PKCS#8 file is (SSH.NET fails on the encrypted kind).
        using var key = TestKeys.New();
        _server.Password = null;
        _server.AuthorizedKey = TestKeys.Blob(key);
        var path = TestKeys.File(key, pkcs8: true);
        try { Connect(Options(SshAuthentication.KeyFile(path))); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnEncryptedRsaKey_SignsIn()
    {
        using var key = System.Security.Cryptography.RSA.Create(2048);
        _server.Password = null;
        _server.AuthorizedKey = TestKeys.Blob(key);
        var path = TestKeys.File(key, passphrase: "rsa phrase");
        try { Connect(Options(SshAuthentication.KeyFile(path, (_, _) => Task.FromResult<string?>("rsa phrase")))); }
        finally { File.Delete(path); }
    }

    /// <summary>The key most users have: what <c>ssh-keygen</c> writes, in OpenSSH's own format,
    /// with a passphrase.</summary>
    [SshKeygenFact]
    public void AKeyFromSshKeygen_WithAPassphrase_SignsIn()
    {
        var path = Path.Combine(_dir, "id_ecdsa");
        var keygen = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SshKeygenFactAttribute.Program!)
        {
            ArgumentList = { "-q", "-t", "ecdsa", "-b", "256", "-N", "keygen phrase", "-C", "test", "-f", path },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        Assert.True(keygen.WaitForExit(Patience), "ssh-keygen did not finish");
        Assert.Equal(0, keygen.ExitCode);
        Assert.Contains("BEGIN OPENSSH PRIVATE KEY", File.ReadAllText(path));
        _server.Password = null;
        _server.AuthorizedKey = Convert.FromBase64String(File.ReadAllText(path + ".pub").Split(' ')[1]);
        int asked = 0;

        Connect(Options(SshAuthentication.KeyFile(path, (_, _) => { asked++; return Task.FromResult<string?>("keygen phrase"); })));

        Assert.Equal(1, asked);
    }

    [Fact]
    public void TheAgent_SignsIn()
    {
        using var key = TestKeys.New();
        _server.Password = null;
        _server.AuthorizedKey = TestKeys.Blob(key);
        using var agent = new FakeSshAgent(key);

        Connect(Options(SshAuthentication.Agent(agent.Socket)));

        Assert.Equal(1, agent.Signatures);
    }

    [Fact]
    public void NoAgent_IsSkipped_ForTheNextWay()
    {
        Connect(Options(SshAuthentication.Agent("hultui-no-such-agent-" + Guid.NewGuid().ToString("N")), ThePassword));
        Assert.Equal(1, _server.PasswordAttempts);
    }

    [Fact]
    public void TheServersOwnQuestions_AreAnswered()
    {
        _server.Password = null;
        _server.InteractiveCode = "123456";
        SshInteractivePrompt? asked = null;

        Connect(Options(SshAuthentication.KeyboardInteractive((prompt, _) =>
        {
            asked = prompt;
            return Task.FromResult<IReadOnlyList<string>?>(["123456"]);
        })));

        Assert.Equal("Enter the code from your device.", asked!.Instruction);
        Assert.Equal([new SshQuestion("Code: ", Echo: false)], asked.Questions);
    }

    // ──── The shell ────

    [Fact]
    public void TheShell_HasATerminal_OfTheTypeAndSizeAsked_WithUtf8Input()
    {
        var connection = Connect(Options() with { Columns = 100, Rows = 30 });
        Read(connection);

        var pty = _server.Pty!;
        Assert.Equal(("xterm-256color", 100u, 30u), (pty.Term, pty.Columns, pty.Rows));
        Assert.Equal(1u, pty.Modes.GetValueOrDefault((byte)42)); // IUTF8
        Assert.Equal(0x7Fu, pty.Modes.GetValueOrDefault((byte)3)); // VERASE
    }

    [Fact]
    public void InputAndOutput_AndResizes_ReachTheShell()
    {
        var connection = Connect(Options());
        var output = Read(connection);

        Send(connection, "hello\r");
        output.WaitFor("[hello]");

        connection.Resize(120, 40);
        Send(connection, "size\r");
        output.WaitFor("120x40");
        Assert.Contains((120u, 40u), _server.WindowChanges);
    }

    [Fact]
    public async Task TheExitCode_IsTheShells()
    {
        var connection = Connect(Options());
        var exited = ExitOf(connection);
        var output = Read(connection);

        Send(connection, "exit 3\r");

        Assert.Equal(3, await exited.WaitAsync(Patience));
        output.WaitForEnd();
    }

    [Fact]
    public async Task AShellEndedByASignal_Exits128PlusItsNumber()
    {
        var connection = Connect(Options());
        var exited = ExitOf(connection);
        Read(connection);

        Send(connection, "kill TERM\r");

        Assert.Equal(128 + 15, await exited.WaitAsync(Patience));
    }

    [Fact]
    public async Task ALostConnection_EndsTheSession_WithoutACode()
    {
        var connection = Connect(Options());
        var exited = ExitOf(connection);
        var output = Read(connection);

        Send(connection, "drop\r");

        Assert.Null(await exited.WaitAsync(Patience));
        output.WaitForEnd();
    }

    [Fact]
    public async Task Terminate_EndsTheSession_AndTheServerSeesItGo()
    {
        var connection = Connect(Options());
        var exited = ExitOf(connection);
        var output = Read(connection);

        connection.Terminate();

        Assert.Null(await exited.WaitAsync(Patience));
        output.WaitForEnd();
        await _server.ShellClosed.Task.WaitAsync(Patience);
    }

    [Fact]
    public void OutputNotRead_HoldsTheServerBack()
    {
        const long cap = 256 * 1024, flood = 32L * 1024 * 1024;
        var connection = Connect(Options() with { MaxBufferedBytes = cap });
        var output = Read(connection);
        output.Pause();

        Send(connection, $"flood {flood}\r");
        Thread.Sleep(1500);

        // Held at the cap, give or take the packet that crossed it, and the server stopped.
        Assert.InRange(connection.Buffered, cap / 2, cap + 128 * 1024);
        Assert.InRange(_server.FloodSent, 1, flood / 2);

        output.Resume();
        output.WaitFor("done");
        Assert.Equal(flood, _server.FloodSent);
    }

    [Fact]
    public void WindowsOpenSshServers_RunTheShellUnderConPty()
    {
        Assert.Equal(TerminalConnectionTraits.ReflowsItself | TerminalConnectionTraits.WantsWin32Input,
            SshConnection.TraitsOf("SSH-2.0-OpenSSH_for_Windows_9.5"));
        Assert.Equal(TerminalConnectionTraits.None, SshConnection.TraitsOf("SSH-2.0-OpenSSH_9.6p1 Ubuntu-3ubuntu13"));
        Assert.Equal(TerminalConnectionTraits.None, Connect(Options()).Traits);
    }

    [Fact]
    public void InATerminal_TheShellRuns_AndItsExitCodeIsTheSessions()
    {
        using var session = HeadlessSession.Create(200, 100);
        var controller = new TerminalController(Connect(Options()));
        _owned.Insert(0, controller);
        controller.Resize(90, 20);

        session.PumpUntil(() => controller.ReadScreen().Contains("ready"), Patience);
        controller.Send("size\r");
        session.PumpUntil(() => controller.ReadScreen().Contains("90x20"), Patience);
        controller.Send("exit 7\r");
        session.PumpUntil(() => controller.State == TerminalState.Exited, Patience);
        Assert.Equal(7, controller.ExitCode);
    }

    /// <summary>Reads a connection's output on a thread of its own, as the controller's reader does;
    /// it can be paused to leave the output unread.</summary>
    private sealed class Reader
    {
        private readonly StringBuilder _text = new();
        private readonly ManualResetEventSlim _running = new(true);
        private volatile bool _ended;

        public Reader(Stream output)
        {
            new Thread(() =>
            {
                var buffer = new byte[16 * 1024];
                while (true)
                {
                    _running.Wait();
                    int n;
                    try { n = output.Read(buffer, 0, buffer.Length); }
                    catch (IOException) { n = 0; }
                    if (n == 0) { _ended = true; return; }
                    lock (_text) _text.Append(Encoding.UTF8.GetString(buffer, 0, n));
                }
            }) { IsBackground = true }.Start();
        }

        public string Text { get { lock (_text) return _text.ToString(); } }

        public void Pause() => _running.Reset();
        public void Resume() => _running.Set();

        public void WaitFor(string text)
        {
            if (!SpinWait.SpinUntil(() => Text.Contains(text, StringComparison.Ordinal), Patience))
                Assert.Fail($"'{text}' never came; the output was:\n{Tail(Text)}");
        }

        public void WaitForEnd()
        {
            if (!SpinWait.SpinUntil(() => _ended, Patience)) Assert.Fail("the output never ended");
        }

        private static string Tail(string text) => text.Length > 400 ? "…" + text[^400..] : text;
    }
}
