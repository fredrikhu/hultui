using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VesperCommander;

/// <summary>How listing a server's shares turned out — the pane maps these onto its states.</summary>
public enum ShareListStatus { Ok, Empty, Denied, Offline, Unsupported }

/// <summary>A server's shares, plus how the attempt went.</summary>
public sealed record ShareListing(ShareListStatus Status, IReadOnlyList<string> Shares)
{
    public static readonly ShareListing None = new(ShareListStatus.Unsupported, []);
}

/// <summary>
/// The shares a file server offers — what Explorer shows for <c>\\server</c>.
///
/// <para><b>Why this is not just a directory read.</b> <c>\\server</c> is the one UNC shape the
/// filesystem API cannot answer for at all: <c>Directory.Exists</c> is false and
/// <c>EnumerateFileSystemInfos</c> throws, because a server is not a directory — the share is
/// the first thing that is. Browsing one goes through the network provider instead, which is a
/// different API with its own error vocabulary.</para>
///
/// <para>Windows-only by nature. Elsewhere <see cref="ShareListStatus.Unsupported"/> comes back
/// and the pane says so rather than pretending the server was unreachable.</para>
/// </summary>
public static class NetworkShares
{
    /// <summary>Stands in for the network provider, so the pane's handling of every outcome can
    /// be tested without a file server. Null in every shipped path.</summary>
    internal static Func<string, ShareListing>? EnumerateOverride;

    /// <summary>The disk shares <paramref name="server"/> offers (leaf names, no
    /// <c>\\server\</c> prefix). Blocking — call it off the UI thread.</summary>
    public static ShareListing List(string server, CancellationToken ct = default)
    {
        if (EnumerateOverride is { } fake) return fake(server);
        if (!OperatingSystem.IsWindows()) return ShareListing.None;
        return Enumerate(@"\\" + server.TrimStart('\\'), ct);
    }

    // ── Interop ──────────────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private static ShareListing Enumerate(string remote, CancellationToken ct)
    {
        var resource = new NETRESOURCE
        {
            dwScope = RESOURCE_GLOBALNET,
            dwType = RESOURCETYPE_DISK,
            dwDisplayType = RESOURCEDISPLAYTYPE_SERVER,
            dwUsage = RESOURCEUSAGE_CONTAINER,
            lpRemoteName = Marshal.StringToHGlobalUni(remote),
        };

        IntPtr handle = IntPtr.Zero;
        try
        {
            int rc = WNetOpenEnumW(RESOURCE_GLOBALNET, RESOURCETYPE_DISK, 0, ref resource, out handle);
            if (rc != NO_ERROR) return new(StatusFor(rc), []);

            var shares = new List<string>();
            int bufferBytes = 16 * 1024;
            IntPtr buffer = Marshal.AllocHGlobal(bufferBytes);
            try
            {
                while (true)
                {
                    if (ct.IsCancellationRequested) return ShareListing.None;

                    uint count = unchecked((uint)-1);   // as many as fit
                    uint size = (uint)bufferBytes;
                    rc = WNetEnumResourceW(handle, ref count, buffer, ref size);

                    if (rc == ERROR_NO_MORE_ITEMS) break;
                    if (rc == ERROR_MORE_DATA)
                    {
                        // One entry did not fit. The call reports the size it wants; take it.
                        Marshal.FreeHGlobal(buffer);
                        bufferBytes = (int)Math.Max(size, (uint)bufferBytes * 2);
                        buffer = Marshal.AllocHGlobal(bufferBytes);
                        continue;
                    }
                    if (rc != NO_ERROR) return new(StatusFor(rc), shares);

                    int stride = Marshal.SizeOf<NETRESOURCE>();
                    for (uint i = 0; i < count; i++)
                    {
                        var entry = Marshal.PtrToStructure<NETRESOURCE>(buffer + (int)(i * stride));
                        if (NameOf(entry) is { } name) shares.Add(name);
                    }
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }

            shares.Sort(StringComparer.OrdinalIgnoreCase);
            return new(shares.Count == 0 ? ShareListStatus.Empty : ShareListStatus.Ok, shares);
        }
        catch { return new(ShareListStatus.Offline, []); }
        finally
        {
            if (handle != IntPtr.Zero) WNetCloseEnum(handle);
            Marshal.FreeHGlobal(resource.lpRemoteName);
        }
    }

    /// <summary>The share's own name out of the <c>\\server\share</c> the provider reports.</summary>
    private static string? NameOf(NETRESOURCE entry)
    {
        if (entry.lpRemoteName == IntPtr.Zero) return null;
        var full = Marshal.PtrToStringUni(entry.lpRemoteName);
        if (string.IsNullOrEmpty(full)) return null;
        int slash = full.LastIndexOf('\\');
        var name = slash >= 0 ? full[(slash + 1)..] : full;
        return name.Length == 0 ? null : name;
    }

    /// <summary>What a network-provider error means for the pane. The codes worth telling apart
    /// are "you may not ask" and "nothing answered"; everything else reads as offline, which is
    /// what an unexplained failure to reach a server amounts to.</summary>
    private static ShareListStatus StatusFor(int rc) => rc switch
    {
        ERROR_ACCESS_DENIED or ERROR_INVALID_PASSWORD or ERROR_LOGON_FAILURE => ShareListStatus.Denied,
        _ => ShareListStatus.Offline,
    };

    private const int NO_ERROR = 0;
    private const int ERROR_ACCESS_DENIED = 5;
    private const int ERROR_MORE_DATA = 234;
    private const int ERROR_NO_MORE_ITEMS = 259;
    private const int ERROR_INVALID_PASSWORD = 86;
    private const int ERROR_LOGON_FAILURE = 1326;

    private const uint RESOURCE_GLOBALNET = 0x00000002;
    private const uint RESOURCETYPE_DISK = 0x00000001;
    private const uint RESOURCEUSAGE_CONTAINER = 0x00000002;
    private const uint RESOURCEDISPLAYTYPE_SERVER = 0x00000002;

    // Pointer fields rather than marshalled strings: the enumeration writes an array of these
    // into a buffer we own, and reading them back by hand keeps the struct blittable — which
    // is what a NativeAOT publish wants.
    [StructLayout(LayoutKind.Sequential)]
    private struct NETRESOURCE
    {
        public uint dwScope;
        public uint dwType;
        public uint dwDisplayType;
        public uint dwUsage;
        public IntPtr lpLocalName;
        public IntPtr lpRemoteName;
        public IntPtr lpComment;
        public IntPtr lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetOpenEnumW(uint dwScope, uint dwType, uint dwUsage,
        ref NETRESOURCE lpNetResource, out IntPtr lphEnum);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetEnumResourceW(IntPtr hEnum, ref uint lpcCount, IntPtr lpBuffer,
        ref uint lpBufferSize);

    [DllImport("mpr.dll")]
    private static extern int WNetCloseEnum(IntPtr hEnum);
}
