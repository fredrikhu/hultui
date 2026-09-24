using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Reel;

/// <summary>One registry value the registration writes. The empty name is the key's default value.</summary>
/// <param name="Key">Relative to <c>HKEY_CURRENT_USER</c>.</param>
public readonly record struct RegistryEntry(string Key, string Name, string Value);

/// <summary>
/// Making Reel an option for video files on Windows — and only an option.
///
/// <para><b>An application cannot make itself the default.</b> Windows 8 ended that, and
/// Windows 10 and 11 enforce it with a signed <c>UserChoice</c> hash that only the shell can
/// write: anything that forges it is malware by Microsoft's definition, and the shell resets
/// the association when it notices. What an app may do is register itself — a ProgID, the
/// command to open a file with, and the extensions it handles — so that it appears under
/// "Open with", and in Settings as something the user can choose. Then it sends the user to
/// that page. So: <see cref="Register"/> writes the keys, and
/// <see cref="OpenDefaultAppsSettings"/> opens the page where the choice is made.</para>
///
/// <para>Everything is under <c>HKEY_CURRENT_USER</c>: no administrator, no machine-wide
/// change, and an uninstall that is a directory deletion leaves nothing behind but these,
/// which <see cref="Unregister"/> removes.</para>
///
/// <para>The registry is reached through advapi32 rather than <c>Microsoft.Win32.Registry</c>
/// because these projects target plain <c>net10.0</c>, where that API is not in the shared
/// framework — the same reason the window chrome talks to user32 directly.</para>
/// </summary>
public static class FileAssociations
{
    /// <summary>The application's key in the registry, and the name Settings shows.</summary>
    public const string ApplicationName = "Reel";

    /// <summary>The ProgID: this app's idea of "a video file", keyed to the app, not the format.</summary>
    public const string ProgId = "Reel.Video";

    private const string CapabilitiesKey = @"Software\Reel\Capabilities";

    /// <summary>
    /// Everything the registration writes, as data. Pure and ordered — a test can read it,
    /// and <see cref="Register"/> only has to write what this returns.
    /// </summary>
    public static IReadOnlyList<RegistryEntry> Plan(string exePath)
    {
        // Quoted, because Program Files has a space in it and an unquoted command there
        // hands the shell "C:\Program" with the rest as arguments.
        string command = $"\"{exePath}\" \"%1\"";
        var entries = new List<RegistryEntry>
        {
            new(@$"Software\Classes\{ProgId}", "", "Video file"),
            new(@$"Software\Classes\{ProgId}", "FriendlyTypeName", "Video file"),
            new(@$"Software\Classes\{ProgId}\DefaultIcon", "", $"\"{exePath}\",0"),
            new(@$"Software\Classes\{ProgId}\shell\open", "", "&Play with Reel"),
            new(@$"Software\Classes\{ProgId}\shell\open\command", "", command),

            new(CapabilitiesKey, "ApplicationName", ApplicationName),
            new(CapabilitiesKey, "ApplicationDescription", "Plays video files."),
            // What makes the app appear in Settings → Default apps at all.
            new(@"Software\RegisteredApplications", ApplicationName, CapabilitiesKey),
        };

        foreach (var extension in Playlist.Extensions)
        {
            // "Open with" without touching whatever currently owns the extension.
            entries.Add(new(@$"Software\Classes\{extension}\OpenWithProgids", ProgId, ""));
            entries.Add(new($@"{CapabilitiesKey}\FileAssociations", extension, ProgId));
        }
        return entries;
    }

    /// <summary>Whether these keys point at <i>this</i> executable. False on anything but
    /// Windows, and false when another copy of Reel registered itself.</summary>
    public static bool IsRegistered(string? exePath = null)
    {
        if (!OperatingSystem.IsWindows()) return false;
        exePath ??= Environment.ProcessPath;
        if (exePath is null) return false;
        var command = ReadString(@$"Software\Classes\{ProgId}\shell\open\command", "");
        return command is not null
            && command.Contains($"\"{exePath}\"", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes the registration for the current user, and tells the shell to re-read
    /// associations so "Open with" is right without a sign-out. Returns false when the
    /// platform is not Windows or a key could not be written.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool Register(string? exePath = null)
    {
        exePath ??= Environment.ProcessPath;
        if (exePath is null) return false;

        foreach (var entry in Plan(exePath))
            if (!WriteString(entry.Key, entry.Name, entry.Value)) return false;

        NotifyShell();
        return true;
    }

    /// <summary>Removes everything <see cref="Register"/> wrote. Missing keys are not a failure
    /// — unregistering twice is the same as unregistering once.</summary>
    [SupportedOSPlatform("windows")]
    public static bool Unregister()
    {
        DeleteTree(@$"Software\Classes\{ProgId}");
        DeleteTree(@"Software\Reel");
        DeleteValue(@"Software\RegisteredApplications", ApplicationName);
        foreach (var extension in Playlist.Extensions)
            DeleteValue(@$"Software\Classes\{extension}\OpenWithProgids", ProgId);

        NotifyShell();
        return true;
    }

    /// <summary>
    /// Opens the page where Windows lets the choice be made, at this app if it can. The
    /// last word is the user's, which is the whole shape of this on a modern Windows.
    /// </summary>
    public static void OpenDefaultAppsSettings()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            Process.Start(new ProcessStartInfo($"ms-settings:defaultapps?registeredAppUser={ApplicationName}")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }); }
            catch (Exception inner) when (inner is System.ComponentModel.Win32Exception or InvalidOperationException)
            { /* no Settings app to open; the registration still stands */ }
        }
    }

    // ── advapi32 ─────────────────────────────────────────────────────────

    private static readonly nint HkeyCurrentUser = unchecked((nint)0x80000001);
    private const int KeyWrite = 0x20006;
    private const int KeyRead = 0x20019;
    private const int RegSz = 1;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegCreateKeyExW")]
    private static extern int RegCreateKeyEx(nint key, string subKey, int reserved, string? className,
        int options, int desired, nint security, out nint result, out int disposition);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegSetValueExW")]
    private static extern int RegSetValueEx(nint key, string? valueName, int reserved, int type,
        byte[] data, int dataLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegGetValueW")]
    private static extern int RegGetValue(nint key, string subKey, string? value, int flags,
        out int type, StringBuilder? data, ref int dataLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegDeleteTreeW")]
    private static extern int RegDeleteTree(nint key, string subKey);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegOpenKeyExW")]
    private static extern int RegOpenKeyEx(nint key, string subKey, int options, int desired, out nint result);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegDeleteValueW")]
    private static extern int RegDeleteValue(nint key, string valueName);

    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(nint key);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);

    private static bool WriteString(string subKey, string name, string value)
    {
        if (RegCreateKeyEx(HkeyCurrentUser, subKey, 0, null, 0, KeyWrite, 0, out nint key, out _) != 0)
            return false;
        try
        {
            var bytes = Encoding.Unicode.GetBytes(value + '\0');
            return RegSetValueEx(key, name.Length == 0 ? null : name, 0, RegSz, bytes, bytes.Length) == 0;
        }
        finally { RegCloseKey(key); }
    }

    private static string? ReadString(string subKey, string name)
    {
        // RRF_RT_REG_SZ
        int size = 0;
        int flags = 0x00000002;
        if (RegGetValue(HkeyCurrentUser, subKey, name.Length == 0 ? null : name, flags, out _, null, ref size) != 0)
            return null;
        var buffer = new StringBuilder(size / 2 + 1);
        return RegGetValue(HkeyCurrentUser, subKey, name.Length == 0 ? null : name, flags, out _, buffer, ref size) == 0
            ? buffer.ToString()
            : null;
    }

    private static void DeleteTree(string subKey) => RegDeleteTree(HkeyCurrentUser, subKey);

    private static void DeleteValue(string subKey, string name)
    {
        if (RegOpenKeyEx(HkeyCurrentUser, subKey, 0, KeyWrite | KeyRead, out nint key) != 0) return;
        try { RegDeleteValue(key, name); }
        finally { RegCloseKey(key); }
    }

    /// <summary>SHCNE_ASSOCCHANGED: associations moved, re-read them.</summary>
    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, 0, 0);
}
