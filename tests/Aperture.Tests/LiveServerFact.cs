using Aperture.Core;
using Xunit;

// A HeadlessSession owns process-global state, so nothing here runs in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Aperture.Tests;

/// <summary>
/// A fact that runs only against a real Jellyfin server, and skips — visibly, with the
/// reason — everywhere else. Two environment variables arm it:
///
/// <list type="bullet">
/// <item><c>APERTURE_LIVE_SERVER</c> — the address to reach, e.g. <c>http://jellyfin.home:8096</c>.</item>
/// <item><c>APERTURE_API_KEY</c>, or <c>APERTURE_API_KEY_FILE</c> pointing at a file that
/// holds one — a key from Dashboard → API Keys. A key, not a password: nothing here needs
/// a credential that can change the account it belongs to, and a key can be revoked in one
/// click when a laptop is lost.</item>
/// </list>
///
/// <para>CI has neither, so these skip there. They exist because the part of a client that
/// breaks is never the part a fake can model: it's the server's real payload — an image tag
/// that arrives as an untyped bag, a resume list that includes seasons, a stream URL that
/// only works with <c>static=true</c>.</para>
/// </summary>
public sealed class LiveServerFactAttribute : FactAttribute
{
    public LiveServerFactAttribute()
    {
        if (LiveServer.Address is null)
            Skip = "No live server: set APERTURE_LIVE_SERVER (and APERTURE_API_KEY or APERTURE_API_KEY_FILE).";
        else if (LiveServer.ApiKey is null)
            Skip = "No API key: set APERTURE_API_KEY, or APERTURE_API_KEY_FILE pointing at a file holding one.";
    }
}

/// <summary>What the live tests connect with, read once from the environment.</summary>
public static class LiveServer
{
    public static string? Address => Value("APERTURE_LIVE_SERVER");

    /// <summary>The key itself, or the contents of the file the environment points at.
    /// The file form keeps the key out of shell history and out of a process listing.</summary>
    public static string? ApiKey
    {
        get
        {
            if (Value("APERTURE_API_KEY") is { } direct) return direct;
            if (Value("APERTURE_API_KEY_FILE") is { } path && File.Exists(path))
            {
                var text = File.ReadAllText(path).Trim();
                return text.Length > 0 ? text : null;
            }
            return null;
        }
    }

    /// <summary>The user to browse as, when the server has more than one.</summary>
    public static string? UserId => Value("APERTURE_USER");

    /// <summary>Signs in and hands back the library; the caller disposes it.</summary>
    public static async Task<IMediaLibrary> ConnectAsync(CancellationToken ct = default)
    {
        var device = DeviceIdentity.New("aperture-tests");
        var probe = await JellyfinConnection.DiscoverAsync(Address!, device, ct);
        Assert.True(probe.Ok, probe.Failure?.Message);
        var session = await JellyfinConnection.SignInWithApiKeyAsync(probe.Value, ApiKey!, UserId, device, ct);
        Assert.True(session.Ok, session.Failure?.Message);
        return session.Value;
    }

    private static string? Value(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
