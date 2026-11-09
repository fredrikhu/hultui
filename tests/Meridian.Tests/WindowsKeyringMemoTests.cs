using HultUI.Core.Events;
using HultUI.Headless;
using Meridian.Core;
using Xunit;
using static Meridian.Core.WindowsCredentialKeyring;

namespace Meridian.Tests;

/// <summary>The Windows keyring remembers its answers for the process, as the Linux and macOS ones
/// do: the Environments table and the Request tab ask for every secret at each build, and each
/// read was a CredReadW call into another process (22–66 a key typed in the table, 59 a frame of a
/// response-pane drag; plan 17 round 9). A fake stands in for the Credential Manager: these never
/// touch the machine's own.</summary>
public class WindowsKeyringMemoTests : IDisposable
{
    private const string Root = @"C:\src\payments-api";
    private readonly string _dir = Directory.CreateTempSubdirectory("meridian-memo-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>The Credential Manager's three calls over a dictionary that matches names in any
    /// letter case, as Windows does, counting each call.</summary>
    private sealed class FakeCredentials : ICredentialStore
    {
        public readonly Dictionary<string, string> Store = new(StringComparer.OrdinalIgnoreCase);
        public int Reads, Writes, Deletes;
        public bool Fail;

        public StoreAnswer Read(string target, out string? value)
        {
            Reads++;
            value = null;
            if (Fail) return StoreAnswer.Failed;
            return Store.TryGetValue(target, out value) ? StoreAnswer.Done : StoreAnswer.Missing;
        }

        public bool Write(string target, string value)
        {
            Writes++;
            if (Fail) return false;
            Store[target] = value;
            return true;
        }

        public StoreAnswer Delete(string target)
        {
            Deletes++;
            if (Fail) return StoreAnswer.Failed;
            return Store.Remove(target) ? StoreAnswer.Done : StoreAnswer.Missing;
        }
    }

    [Fact]
    public void EachEntryIsReadFromTheStoreOnce_AndTheAppsOwnChangesShowAtOnce()
    {
        var store = new FakeCredentials();
        store.Store[Target(Root, "local", "client_secret")] = "own";
        var keyring = new WindowsCredentialKeyring(store);

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal("own", keyring.Get(Root, "local", "client_secret"));
            Assert.Null(keyring.Get(Root, null, "client_secret"));   // none: remembered too
        }
        Assert.Equal(2, store.Reads);

        // The app's own store and forget go through the memo.
        Assert.True(keyring.Set(Root, null, "client_secret", "shared"));
        Assert.Equal("shared", keyring.Get(Root, null, "client_secret"));
        Assert.True(keyring.Delete(Root, "local", "client_secret"));
        Assert.Null(keyring.Get(Root, "local", "client_secret"));
        Assert.False(keyring.Delete(Root, "local", "client_secret"));
        Assert.Equal(2, store.Reads);
        Assert.Equal("shared", store.Store[Target(Root, null, "client_secret")]);
        Assert.False(store.Store.ContainsKey(Target(Root, "local", "client_secret")));

        // The store matches names in any letter case, and so does the memo.
        Assert.Equal("shared", keyring.Get(Root.ToUpperInvariant(), null, "CLIENT_SECRET"));
        Assert.True(keyring.Set(Root, null, "Client_Secret", "renamed"));
        Assert.Equal("renamed", keyring.Get(Root, null, "client_secret"));
        Assert.Equal(2, store.Reads);

        // A change made outside the app shows after a restart: a new instance, a new memo.
        store.Store[Target(Root, "staging", "api_token")] = "first";
        Assert.Equal("first", keyring.Get(Root, "staging", "api_token"));
        store.Store[Target(Root, "staging", "api_token")] = "changed in Credential Manager";
        Assert.Equal("first", keyring.Get(Root, "staging", "api_token"));
        Assert.Equal("changed in Credential Manager", new WindowsCredentialKeyring(store).Get(Root, "staging", "api_token"));
    }

    [Fact]
    public void AFailedCallIsAskedAgain()
    {
        var store = new FakeCredentials();
        store.Store[Target(Root, "local", "client_secret")] = "own";
        var keyring = new WindowsCredentialKeyring(store);

        store.Fail = true;
        Assert.Null(keyring.Get(Root, "local", "client_secret"));
        store.Fail = false;
        Assert.Equal("own", keyring.Get(Root, "local", "client_secret"));
        Assert.Equal(2, store.Reads);

        // A write that failed leaves the entry unknown: the next read asks the store.
        store.Fail = true;
        Assert.False(keyring.Set(Root, "local", "client_secret", "lost"));
        Assert.False(keyring.Delete(Root, "local", "client_secret"));
        store.Fail = false;
        Assert.Equal("own", keyring.Get(Root, "local", "client_secret"));
        Assert.Equal(3, store.Reads);
    }

    /// <summary>In the app: every build of the Environments table resolves every secret cell (the
    /// environment's own value, then the shared one), and with the memo the store is read once per
    /// entry however many builds there are.</summary>
    [Fact]
    public void TheEnvironmentsTableReadsTheStoreOncePerEntry()
    {
        File.WriteAllText(Path.Combine(_dir, "echo.req"), "GET {{base_url}}/echo\n");
        Directory.CreateDirectory(Path.Combine(_dir, "envs"));
        foreach (var env in new[] { "local", "staging", "ci" })
            File.WriteAllText(Path.Combine(_dir, "envs", env + ".env"),
                $"base_url=https://{env}.example\ntimeout_ms=30000\nsecret api_token\nsecret client_secret\n");
        var store = new FakeCredentials();
        using var session = HeadlessSession.Create(1440, 900);
        var app = new MeridianApp(session.Application, session.Window.Window, keyring: new WindowsCredentialKeyring(store));
        try
        {
            session.Application.SetRoot(app.Mount());
            app.OpenWorkspace(_dir);
            session.PumpFrame();
            string root = app.Workspace!.Root;
            store.Store[Target(root, "local", "api_token")] = "tok-local";
            store.Store[Target(root, null, "client_secret")] = "shared-secret";

            session.Window.PressKey(KeyCode.Digit5);
            session.PumpFrames(2);
            int builds = app.Builds;
            for (int i = 0; i < 6; i++)
            {
                session.Window.PressKey(KeyCode.Digit1);
                session.PumpFrames(2);
                session.Window.PressKey(KeyCode.Digit5);
                session.PumpFrames(2);
            }
            Assert.True(app.Builds - builds >= 12, "the table was built again");
            Assert.NotNull(session.Window.FindByAutomationId("secret-value-api_token-local"));
            Assert.NotNull(session.Window.FindByAutomationId("secret-value-client_secret-staging"));
            Assert.NotNull(session.Window.FindByAutomationId("set-secret-api_token-staging"));

            // Each environment's own entry and the shared one, for both keys: 3 × 2 + 2 entries.
            Assert.Equal(8, store.Reads);
        }
        finally
        {
            app.Dispose();
        }
    }
}
