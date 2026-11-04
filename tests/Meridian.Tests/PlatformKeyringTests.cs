using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// A fact that runs against this machine's real keyring, and skips everywhere else: it writes
/// entries (under a fresh temp collection root, removed afterwards), so it needs
/// <c>MERIDIAN_KEYRING_TESTS=1</c>. CI's <c>meridian-keyring</c> job sets it on Linux, with a
/// gnome-keyring in a private D-Bus session, and on macOS, with a throwaway keychain.
/// </summary>
public sealed class PlatformKeyringFactAttribute : FactAttribute
{
    public PlatformKeyringFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MERIDIAN_KEYRING_TESTS") != "1")
            Skip = "Writes to this machine's keyring: set MERIDIAN_KEYRING_TESTS=1 to run.";
    }
}

/// <summary>The store <see cref="DefaultKeyring"/> picks on this OS — the Credential Manager, the
/// login keychain or the Secret Service — for real. The unit suites fake it; these are what prove
/// the P/Invoke.</summary>
public sealed class PlatformKeyringTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-keyring-" + Guid.NewGuid().ToString("N"));
    private readonly DefaultKeyring _keyring = new();
    private static readonly string?[] Environments = [null, "local", "staging"];
    private static readonly string[] Keys = ["client_secret", "api_token", "empty"];

    public void Dispose()
    {
        var keyring = new DefaultKeyring();   // past this instance's memory, to the store itself
        foreach (var env in Environments)
            foreach (var key in Keys)
                keyring.Delete(_root, env, key);
    }

    [PlatformKeyringFact]
    public void AValueIsStoredReplacedAndForgotten()
    {
        Assert.Null(_keyring.Unavailable);
        Assert.Null(_keyring.Get(_root, "local", "client_secret"));

        Assert.True(_keyring.Set(_root, "local", "client_secret", "first"));
        Assert.Equal("first", _keyring.Get(_root, "local", "client_secret"));
        Assert.True(_keyring.Set(_root, "local", "client_secret", "second"));
        Assert.Equal("second", _keyring.Get(_root, "local", "client_secret"));

        Assert.True(_keyring.Delete(_root, "local", "client_secret"));
        Assert.Null(_keyring.Get(_root, "local", "client_secret"));
        Assert.False(_keyring.Delete(_root, "local", "client_secret"));
    }

    [PlatformKeyringFact]
    public void AnEnvironmentsOwnValueAndTheSharedOneAreSeparateEntries()
    {
        Assert.True(_keyring.Set(_root, null, "client_secret", "shared"));
        Assert.True(_keyring.Set(_root, "staging", "client_secret", "staging-own"));

        Assert.Equal("shared", _keyring.Get(_root, null, "client_secret"));
        Assert.Equal("staging-own", _keyring.Get(_root, "staging", "client_secret"));
        Assert.Null(_keyring.Get(_root, "local", "client_secret"));
        Assert.Null(_keyring.Get(_root, "staging", "api_token"));

        Assert.True(_keyring.Delete(_root, "staging", "client_secret"));
        Assert.Equal("shared", _keyring.Get(_root, null, "client_secret"));
    }

    /// <summary>The stores remember answers for the process: a second instance proves the value
    /// reached the platform's keyring, and that a forget did too.</summary>
    [PlatformKeyringFact]
    public void AnotherInstanceSeesWhatThisOneStoredAndForgot()
    {
        Assert.True(_keyring.Set(_root, "local", "api_token", "persisted"));
        Assert.Equal("persisted", new DefaultKeyring().Get(_root, "local", "api_token"));

        Assert.True(_keyring.Delete(_root, "local", "api_token"));
        Assert.Null(new DefaultKeyring().Get(_root, "local", "api_token"));
    }

    [PlatformKeyringFact]
    public void UnicodeAndEmptyValuesSurvive()
    {
        const string unicode = "pä55wörd 🔑 = ; | \"quoted\"";
        Assert.True(_keyring.Set(_root, "local", "client_secret", unicode));
        Assert.True(_keyring.Set(_root, "local", "empty", ""));

        var fresh = new DefaultKeyring();
        Assert.Equal(unicode, fresh.Get(_root, "local", "client_secret"));
        Assert.Equal("", fresh.Get(_root, "local", "empty"));
    }
}
