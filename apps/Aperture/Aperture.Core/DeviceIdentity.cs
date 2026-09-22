using System.Reflection;

namespace Aperture.Core;

/// <summary>
/// How this installation names itself to a Jellyfin server. The server shows all four
/// fields in Dashboard → Devices and in "now playing", and it keys a user's sessions and
/// resume points off <see cref="DeviceId"/> — so that id must be stable across restarts
/// (it is generated once and kept in the settings file) and different on every machine.
/// </summary>
/// <param name="DeviceId">A GUID string, generated on first run.</param>
public sealed record DeviceIdentity(string Client, string Version, string DeviceName, string DeviceId)
{
    public const string ClientName = "Aperture";

    /// <summary>The assembly's informational version, trimmed of any <c>+sha</c> suffix.</summary>
    public static string AppVersion
    {
        get
        {
            var v = typeof(DeviceIdentity).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(DeviceIdentity).Assembly.GetName().Version?.ToString()
                ?? "0.1.0";
            int plus = v.IndexOf('+');
            return plus > 0 ? v[..plus] : v;
        }
    }

    /// <summary>This machine, with a freshly minted device id — for a first run, or a test.</summary>
    public static DeviceIdentity New(string? deviceId = null) =>
        new(ClientName, AppVersion, SafeMachineName(), deviceId ?? Guid.NewGuid().ToString("N"));

    /// <summary>
    /// The <c>Authorization</c> header Jellyfin expects on every request, token or not.
    /// The scheme is <c>MediaBrowser</c> and the parameters are quoted key/value pairs;
    /// a server rejects a request that omits the header even for anonymous endpoints it
    /// would otherwise serve, which is why the anonymous probe sends it too.
    /// </summary>
    public string AuthorizationHeader(string? token = null)
    {
        var header = $"MediaBrowser Client=\"{Escape(Client)}\", Device=\"{Escape(DeviceName)}\", " +
                     $"DeviceId=\"{Escape(DeviceId)}\", Version=\"{Escape(Version)}\"";
        return string.IsNullOrEmpty(token) ? header : header + $", Token=\"{Escape(token)}\"";
    }

    /// <summary>A quote or a backslash in a machine name would break the header's parsing
    /// on the server; both are escaped rather than stripped so the name still reads.</summary>
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>Machine names are user-visible in the server's device list, so a blank or
    /// unavailable one becomes something honest rather than empty quotes.</summary>
    private static string SafeMachineName()
    {
        try
        {
            var name = Environment.MachineName;
            return string.IsNullOrWhiteSpace(name) ? "Unknown device" : name;
        }
        catch (InvalidOperationException)
        {
            return "Unknown device";
        }
    }
}
