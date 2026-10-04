namespace HultUI.Core.Storage;

/// <summary>
/// Where an app keeps its files for the person using it: a folder named after the app, in the
/// place this platform keeps per-user configuration or data.
/// </summary>
/// <remarks>
/// <para><b>Never a relative path.</b> On Linux, .NET answers "" for the special folders whose
/// directory does not exist yet — <c>~/.local/share</c> and <c>~/.config</c> on a new account
/// — and an app that combined that "" with its name kept its settings in whatever directory it
/// happened to be started from (found on fresh Debian 13 and Ubuntu 24.04 accounts). So the
/// folders are asked for without the existence check, a value that is not a full path is
/// skipped (the XDG spec says to ignore a relative one, too), and with no home folder at all
/// the answer is the application's own folder — still one fixed place.</para>
/// </remarks>
public static class AppFolders
{
    /// <summary>
    /// The folder for <paramref name="appName"/>'s configuration:
    /// <c>$XDG_CONFIG_HOME/<i>appName</i></c> when that is set — on any platform, which is also
    /// how a test or a harness points an app at a scratch folder — else
    /// <c>%APPDATA%\<i>appName</i></c> on Windows and <c>~/.config/<i>appName</i></c> elsewhere.
    /// </summary>
    public static string Config(string appName) => ResolveConfig(appName,
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify));

    /// <summary>
    /// The folder for <paramref name="appName"/>'s data — state it keeps rather than choices
    /// the person made: <c>%LOCALAPPDATA%\<i>appName</i></c> on Windows, and elsewhere
    /// <c>$XDG_DATA_HOME/<i>appName</i></c>, by default <c>~/.local/share/<i>appName</i></c>.
    /// </summary>
    public static string Data(string appName) => ResolveData(appName,
        Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
        OperatingSystem.IsWindows());

    /// <summary><see cref="Config"/> from the values the environment gives.</summary>
    public static string ResolveConfig(string appName, string? xdgConfigHome, string? appData, string? userProfile)
    {
        var root = IsFull(xdgConfigHome) ? xdgConfigHome!
                 : IsFull(appData) ? appData!
                 : IsFull(userProfile) ? Path.Combine(userProfile!, ".config")
                 : AppContext.BaseDirectory;
        return Path.Combine(root, appName);
    }

    /// <summary><see cref="Data"/> from the values the environment gives. <paramref name="xdgDataHome"/>
    /// counts only off Windows, where the data folder has always been the local one.</summary>
    public static string ResolveData(string appName, string? xdgDataHome, string? localAppData,
        string? userProfile, bool isWindows)
    {
        var root = !isWindows && IsFull(xdgDataHome) ? xdgDataHome!
                 : IsFull(localAppData) ? localAppData!
                 : IsFull(userProfile) ? Path.Combine(userProfile!, ".local", "share")
                 : AppContext.BaseDirectory;
        return Path.Combine(root, appName);
    }

    private static bool IsFull(string? path) =>
        !string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path);
}
