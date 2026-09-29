using System.Text.Json;
using System.Text.Json.Serialization;
using HultUI.Core.Platform;

namespace Aperture.Core;

/// <summary>One server Aperture has been signed into. <b>No token lives here</b> — the
/// settings file carries who and where, the OS credential store carries the secret under
/// <see cref="ApertureSettings.CredentialService"/>.</summary>
public sealed class ServerAccountData
{
    /// <summary>The address that answered, normalized: <c>http://jellyfin.home:8096</c>.</summary>
    public string Address { get; set; } = "";
    /// <summary>The server's own id — the key everything joins on, so a server that moves
    /// to a new address is still recognised as itself.</summary>
    public string ServerId { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string Version { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public string? UserImageTag { get; set; }
    public DateTimeOffset SignedInAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }

    public ServerInfo ToServerInfo() => new(Address, ServerName, Version, ServerId);
}

/// <summary>How a request server was signed into, which decides what the secret in the
/// credential store is.</summary>
public enum RequestServerAuth
{
    /// <summary>The user's own Jellyfin account: the secret is the session cookie the
    /// request server handed back, and it acts as that user with that user's quota.</summary>
    Session,
    /// <summary>The server's API key from its settings page. It acts as the administrator
    /// unless a user is named, which is why <see cref="RequestServerAccountData.UserId"/>
    /// is kept beside it.</summary>
    ApiKey,
}

/// <summary>
/// The request server (Jellyseerr, or Seerr — its successor, same API) paired with a Jellyfin
/// server. <b>No secret lives here</b>: the session cookie or API key is in the credential
/// store under <see cref="ApertureSettings.RequestCredentialService"/>, keyed by the
/// Jellyfin server id — a request server is only ever used alongside the library it fills.
/// </summary>
public sealed class RequestServerAccountData
{
    /// <summary>The Jellyfin server this pairs with.</summary>
    public string JellyfinServerId { get; set; } = "";
    /// <summary>The address that answered, normalized: <c>http://jellyseerr.home:5055</c>.</summary>
    public string Address { get; set; } = "";
    public RequestServerAuth Auth { get; set; }
    /// <summary>The request server's own user id — who requests are made as.</summary>
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public DateTimeOffset SignedInAt { get; set; }
}

/// <summary>How a library draws what it holds.</summary>
public enum LibraryLayout { Grid, List }

public sealed class ApertureSettingsData
{
    /// <summary>This installation's device id, generated on first run and never changed:
    /// the server keys sessions and "now playing" off it.</summary>
    public string DeviceId { get; set; } = "";

    public List<ServerAccountData> Servers { get; set; } = [];

    /// <summary>Request servers, one per Jellyfin server at most.</summary>
    public List<RequestServerAccountData> RequestServers { get; set; } = [];

    /// <summary>The server id to open on launch. Null means the connect screen.</summary>
    public string? ActiveServerId { get; set; }

    /// <summary>Volume 0–1, as the player last left it. A volume that resets to full on
    /// every launch is the kind of thing that makes people stop using an app at night.</summary>
    public float Volume { get; set; } = 1f;

    public bool Muted { get; set; }

    /// <summary>
    /// The audio language last chosen by hand, as the file labelled it ("eng", "Japanese").
    /// Applied to the next file that has a track matching it, which is what stops an
    /// evening of episodes from resetting to the file's default every time.
    /// </summary>
    public string? PreferredAudioLanguage { get; set; }

    /// <summary>The subtitle language last chosen, on the same terms.</summary>
    public string? PreferredSubtitleLanguage { get; set; }

    /// <summary>How a library draws its items: a shelf of posters, or a list of rows.
    /// A preference about reading, not about one library, so it is remembered once.</summary>
    public LibraryLayout Layout { get; set; } = LibraryLayout.Grid;

    /// <summary>Whether the poster grid captions its cards. Off is for people who know
    /// their own shelves by their covers; the accessible name still carries the title, so
    /// nothing is lost to a screen reader.</summary>
    public bool ShowCardTitles { get; set; } = true;

    /// <summary>
    /// The home rows in the order they are drawn, by key: <c>resume</c>, <c>nextup</c>,
    /// <c>upnext</c> for the combined row, and <c>latest:{libraryId}</c> for a library.
    /// Empty means the built-in order. A key that no longer exists is ignored rather than
    /// pruned — a library that is unshared for a week should come back where it was.
    /// </summary>
    public List<string> HomeRowOrder { get; set; } = [];

    /// <summary>Home rows that have been hidden, by the same keys. Hiding is not
    /// forgetting: home lists what it is not showing, and one click brings it back.</summary>
    public List<string> HiddenHomeRows { get; set; } = [];

    /// <summary>
    /// Whether "Continue watching" and "Next up" are drawn as one row. They answer the
    /// same question — what do I put on now — and on a server with an active household
    /// they are two half-empty shelves saying nearly the same thing.
    /// </summary>
    public bool CombineUpNext { get; set; }

    /// <summary>
    /// A ceiling for the stream in kilobits per second, or null for the file as it is.
    /// Null is right on a home network — the server sends the original and spends no CPU
    /// — and wrong over a link that cannot carry a 40 Mbit remux, which is the whole
    /// reason to set one.
    /// </summary>
    public int? MaxStreamingKbps { get; set; }

    /// <summary>Whether subtitles were on when they were last touched. Separate from the
    /// language because "off" is a choice a client must not quietly forget — a file whose
    /// only subtitle track happens to match a remembered language should not turn them
    /// back on for someone who switched them off.</summary>
    public bool SubtitlesOn { get; set; }

    public void Normalize()
    {
        Servers ??= [];
        Servers.RemoveAll(s => string.IsNullOrWhiteSpace(s.Address) || string.IsNullOrWhiteSpace(s.ServerId));
        RequestServers ??= [];
        RequestServers.RemoveAll(r => string.IsNullOrWhiteSpace(r.Address) || string.IsNullOrWhiteSpace(r.JellyfinServerId));
        // One per Jellyfin server: the last one written wins, which is the one in use.
        RequestServers = RequestServers.GroupBy(r => r.JellyfinServerId, StringComparer.Ordinal)
            .Select(g => g.Last()).ToList();
        HomeRowOrder ??= [];
        HiddenHomeRows ??= [];
        // A key listed twice would make "move up" swap a row with itself.
        HomeRowOrder = HomeRowOrder.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToList();
        HiddenHomeRows = HiddenHomeRows.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToList();
        if (string.IsNullOrWhiteSpace(DeviceId)) DeviceId = Guid.NewGuid().ToString("N");
        Volume = Math.Clamp(Volume, 0f, 1f);
        if (ActiveServerId is not null && !Servers.Any(s => s.ServerId == ActiveServerId))
            ActiveServerId = null;
    }

    public ServerAccountData? Active =>
        ActiveServerId is null ? null : Servers.FirstOrDefault(s => s.ServerId == ActiveServerId);
}

/// <summary>
/// Where Aperture keeps what it knows between runs: a JSON file for identity and
/// preferences, the OS credential store for access tokens. The split is Junction's — a
/// settings file gets copied around, mailed, checked into dotfiles; an access token must
/// not travel with it.
/// </summary>
public sealed class ApertureSettings
{
    /// <summary>Credential-store service name; the account within it is the server id, so
    /// two servers never collide.</summary>
    public const string CredentialService = "aperture|jellyfin";

    private readonly ICredentialStore _credentials;
    private readonly string _path;

    public ApertureSettings(ICredentialStore credentials, string? settingsPath = null)
    {
        _credentials = credentials;
        _path = settingsPath ?? DefaultPath;
        Data = Read(_path) ?? Read(_path + BackupSuffix) ?? new ApertureSettingsData();
        Data.Normalize();
    }

    public ApertureSettingsData Data { get; }

    /// <summary>This installation's identity, with the device id from the settings file.</summary>
    public DeviceIdentity Device => DeviceIdentity.New(Data.DeviceId);

    /// <summary>The access token for a server, or null when the store has none — which is
    /// also what a platform without a credential store returns, and means "ask the user to
    /// sign in again", not "something is broken".</summary>
    public string? TokenFor(string serverId) => _credentials.Read(CredentialService, serverId);

    /// <summary>Records a successful sign-in: identity to the file, token to the store.
    /// Returns false when the token could not be stored, in which case the session works
    /// but will not survive a restart — the UI says so rather than pretending.</summary>
    public bool Remember(ServerInfo server, SignedInUser user)
    {
        var existing = Data.Servers.FirstOrDefault(s => s.ServerId == server.Id);
        if (existing is null)
        {
            existing = new ServerAccountData { ServerId = server.Id, SignedInAt = DateTimeOffset.UtcNow };
            Data.Servers.Add(existing);
        }
        existing.Address = server.Address;
        existing.ServerName = server.Name;
        existing.Version = server.Version;
        existing.UserId = user.Id.ToString("N");
        existing.UserName = user.Name;
        existing.UserImageTag = user.PrimaryImageTag;
        existing.LastUsedAt = DateTimeOffset.UtcNow;
        Data.ActiveServerId = server.Id;

        bool stored = _credentials.Write(CredentialService, server.Id, user.AccessToken);
        Save();
        return stored;
    }

    /// <summary>Forgets a server: its token first (the part that matters), then its row —
    /// and the request server paired with it, whose session belongs to the same person.</summary>
    public void Forget(string serverId)
    {
        _credentials.Delete(CredentialService, serverId);
        Data.Servers.RemoveAll(s => s.ServerId == serverId);
        if (Data.ActiveServerId == serverId) Data.ActiveServerId = null;
        ForgetRequestServer(serverId, save: false);
        Save();
    }

    // ── The request server ───────────────────────────────────────────────

    /// <summary>Credential-store service for request-server secrets; the account is the
    /// Jellyfin server id the request server is paired with.</summary>
    public const string RequestCredentialService = "aperture|seerr";

    /// <summary>The request server paired with a Jellyfin server, or null.</summary>
    public RequestServerAccountData? RequestServerFor(string jellyfinServerId) =>
        Data.RequestServers.FirstOrDefault(r => r.JellyfinServerId == jellyfinServerId);

    /// <summary>The session cookie or API key for the request server paired with a Jellyfin
    /// server — which of the two is recorded in <see cref="RequestServerAccountData.Auth"/>.</summary>
    public string? RequestSecretFor(string jellyfinServerId) =>
        _credentials.Read(RequestCredentialService, jellyfinServerId);

    /// <summary>Records a request-server sign-in: who and where to the file, the secret to
    /// the store. False when the secret could not be stored — it works for this run only.</summary>
    public bool RememberRequestServer(RequestServerAccountData account, string secret)
    {
        Data.RequestServers.RemoveAll(r => r.JellyfinServerId == account.JellyfinServerId);
        Data.RequestServers.Add(account);
        bool stored = _credentials.Write(RequestCredentialService, account.JellyfinServerId, secret);
        Save();
        return stored;
    }

    /// <summary>Disconnects the request server paired with a Jellyfin server.</summary>
    public void ForgetRequestServer(string jellyfinServerId, bool save = true)
    {
        _credentials.Delete(RequestCredentialService, jellyfinServerId);
        Data.RequestServers.RemoveAll(r => r.JellyfinServerId == jellyfinServerId);
        if (save) Save();
    }

    public void Save()
    {
        var pending = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(pending, JsonSerializer.Serialize(Data, ApertureJsonContext.Default.ApertureSettingsData));
            if (File.Exists(_path)) File.Replace(pending, _path, _path + BackupSuffix, ignoreMetadataErrors: true);
            else File.Move(pending, _path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Persistence is best-effort; a half-written file is worse than none.
            try { File.Delete(pending); } catch (IOException) { /* nothing further to try */ }
        }
    }

    private const string BackupSuffix = ".bak";

    private static ApertureSettingsData? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var data = JsonSerializer.Deserialize(File.ReadAllText(path), ApertureJsonContext.Default.ApertureSettingsData);
            data?.Normalize();
            return data;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Where the file lives: XDG on Linux, <c>%APPDATA%\Aperture</c> on Windows —
    /// the same lookup the other three apps use.</summary>
    public static string DefaultPath => Path.Combine(ConfigDirectory, "settings.json");

    public static string ConfigDirectory
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(xdg)) return Path.Combine(xdg, "Aperture");
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData)) return Path.Combine(appData, "Aperture");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "Aperture");
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ApertureSettingsData))]
internal partial class ApertureJsonContext : JsonSerializerContext;
