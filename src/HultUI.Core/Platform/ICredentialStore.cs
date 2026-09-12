namespace HultUI.Core.Platform;

/// <summary>
/// Where an application keeps secrets the user would not want in a settings file — API
/// tokens, passwords, refresh tokens. Entries are addressed by a <c>service</c> (the app and
/// the thing the secret is for, e.g. <c>junction|gitlab|gitlab.com</c>) and an
/// <c>account</c> within it (<c>token</c>, <c>refresh</c>), so one application's entries
/// never collide with another's, nor two hosts' with each other.
/// </summary>
/// <remarks>
/// The store is the operating system's own — the Windows Credential Manager today — so a
/// secret survives reinstalling the app and never travels with a copied config folder.
/// Platforms without a store return <c>false</c> from writes and <c>null</c> from reads;
/// callers treat that as "the user has to enter it each time", not as an error.
/// </remarks>
public interface ICredentialStore
{
    /// <summary>The stored secret, or <c>null</c> when there is none (or no store).</summary>
    string? Read(string service, string account);

    /// <summary>Stores or replaces a secret. <c>false</c> when the platform has no store,
    /// or the write was refused.</summary>
    bool Write(string service, string account, string secret);

    /// <summary>Removes a secret. <c>true</c> when it is gone afterwards — including when
    /// it was never there.</summary>
    bool Delete(string service, string account);
}

/// <summary>A platform with nowhere to keep a secret. Every write fails, every read is
/// empty — the honest answer, so the app asks the user rather than pretending it saved.</summary>
public sealed class NoCredentialStore : ICredentialStore
{
    public string? Read(string service, string account) => null;
    public bool Write(string service, string account, string secret) => false;
    public bool Delete(string service, string account) => true;
}

/// <summary>An in-process store for tests and for hosting an app headless, where touching
/// the real Credential Manager would leave entries behind on the developer's machine.</summary>
public sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<(string, string), string> _entries = new();
    private readonly object _lock = new();

    public string? Read(string service, string account)
    {
        lock (_lock) return _entries.TryGetValue((service, account), out var v) ? v : null;
    }

    public bool Write(string service, string account, string secret)
    {
        lock (_lock) _entries[(service, account)] = secret;
        return true;
    }

    public bool Delete(string service, string account)
    {
        lock (_lock) _entries.Remove((service, account));
        return true;
    }

    /// <summary>How many secrets are held — what a test asserts on after a sign-out.</summary>
    public int Count { get { lock (_lock) return _entries.Count; } }
}
