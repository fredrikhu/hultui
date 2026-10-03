using System.Runtime.Versioning;
using Tmds.DBus.Protocol;

namespace HultUI.Platform.Accessibility.Linux;

/// <summary>
/// Owns the two D-Bus connections the AT-SPI protocol needs — the session bus
/// (used only to discover the a11y bus address) and the a11y bus itself — plus
/// the outgoing traffic the bridge produces: the <c>Socket.Embed</c>
/// registration and the accessible-event signals.
/// </summary>
/// <remarks>
/// Incoming method calls are answered by the <see cref="IPathMethodHandler"/>s
/// the bridge registers through <see cref="AddMethodHandler"/>; this class only
/// deals with the calls and signals the application <em>originates</em>.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class AtSpiBus : IDisposable
{
    private readonly DBusConnection _session;
    private readonly DBusConnection _a11y;

    /// <summary>Our unique bus name on the a11y bus (e.g. <c>:1.42</c>).</summary>
    public string BusName { get; }

    /// <summary>The desktop reference returned by Embed — the parent of our root.</summary>
    public (string BusName, string Path) Desktop { get; private set; } =
        (AtSpiConstants.RegistryService, AtSpiConstants.RegistryRootPath);

    private AtSpiBus(DBusConnection session, DBusConnection a11y, string busName)
    {
        _session = session;
        _a11y = a11y;
        BusName = busName;
    }

    /// <summary>
    /// Connects the session bus, discovers the a11y bus via
    /// <c>org.a11y.Bus.GetAddress</c>, and connects it. Returns null when no
    /// a11y bus is reachable — the caller then stays on the null bridge.
    /// </summary>
    public static AtSpiBus? Open()
    {
        var sessionAddress = DBusAddress.Session;
        if (sessionAddress is null) return null;

        DBusConnection? session = null;
        DBusConnection? a11y = null;
        try
        {
            session = new DBusConnection(sessionAddress);
            session.ConnectAsync().GetAwaiter().GetResult();

            string? a11yAddress = QueryA11yBusAddress(session);
            if (string.IsNullOrEmpty(a11yAddress)) { session.Dispose(); return null; }

            a11y = new DBusConnection(a11yAddress);
            a11y.ConnectAsync().GetAwaiter().GetResult();

            string busName = a11y.UniqueName ?? "";
            return new AtSpiBus(session, a11y, busName);
        }
        catch
        {
            a11y?.Dispose();
            session?.Dispose();
            return null;
        }
    }

    private static string? QueryA11yBusAddress(DBusConnection session)
    {
        using var writer = session.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: AtSpiConstants.A11yBusService,
            path: AtSpiConstants.A11yBusPath,
            @interface: AtSpiConstants.A11yBusInterface,
            member: "GetAddress",
            signature: "",
            flags: MessageFlags.None);

        return session.CallMethodAsync(
            writer.CreateMessage(),
            static (Message message, object? _) => message.GetBodyReader().ReadString(),
            null).GetAwaiter().GetResult();
    }

    public void AddMethodHandler(IPathMethodHandler handler) => _a11y.AddMethodHandler(handler);

    // ──── org.a11y.Status: whether assistive technology is on (seam W11). Orca and
    // accerciser set IsEnabled (and Orca ScreenReaderEnabled) on the session bus.

    /// <summary>Whether <c>org.a11y.Status</c> says assistive technology is on.</summary>
    public bool ReadStatus() => ReadStatusProperty("IsEnabled") || ReadStatusProperty("ScreenReaderEnabled");

    private bool ReadStatusProperty(string name)
    {
        try
        {
            using var writer = _session.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: AtSpiConstants.A11yBusService,
                path: AtSpiConstants.A11yBusPath,
                @interface: "org.freedesktop.DBus.Properties",
                member: "Get",
                signature: "ss",
                flags: MessageFlags.None);
            writer.WriteString(AtSpiConstants.StatusInterface);
            writer.WriteString(name);
            return _session.CallMethodAsync(
                writer.CreateMessage(),
                static (Message message, object? _) => message.GetBodyReader().ReadVariantValue().GetBool(),
                null).GetAwaiter().GetResult();
        }
        catch
        {
            return false;   // no status to read: nobody said assistive technology is on
        }
    }

    /// <summary>Calls <paramref name="changed"/> with the status, on a thread-pool thread,
    /// whenever <c>org.a11y.Status</c>'s properties change.</summary>
    public void WatchStatus(Action<bool> changed)
    {
        try
        {
            var rule = new MatchRule
            {
                Type = MessageType.Signal,
                Interface = "org.freedesktop.DBus.Properties",
                Member = "PropertiesChanged",
                Path = AtSpiConstants.A11yBusPath,
            };
            _session.AddMatchAsync(rule,
                static (Message _, object? _) => true,
                (Exception? ex, bool _, object? _, object? _) =>
                {
                    if (ex is null) ThreadPool.QueueUserWorkItem(_ => changed(ReadStatus()));
                },
                ObserverFlags.None, null, null, false).GetAwaiter().GetResult();
        }
        catch
        {
            // Without the signal the status read at start stands.
        }
    }

    /// <summary>
    /// Registers the application root with the AT-SPI registry via
    /// <c>org.a11y.atspi.Socket.Embed</c>, passing our root reference and
    /// storing the desktop reference the registry returns (our root's parent).
    /// </summary>
    public bool RegisterApplication()
    {
        try
        {
            var writer = _a11y.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: AtSpiConstants.RegistryService,
                path: AtSpiConstants.RegistryRootPath,
                @interface: AtSpiConstants.SocketInterface,
                member: "Embed",
                signature: "(so)",
                flags: MessageFlags.None);
            AtSpiMarshal.WriteObjectRef(ref writer, BusName, AtSpiConstants.RootPath);
            var message = writer.CreateMessage();
            writer.Dispose();

            Desktop = _a11y.CallMethodAsync(
                message,
                static (Message message, object? _) =>
                {
                    var reader = message.GetBodyReader();
                    return AtSpiMarshal.ReadObjectRef(ref reader);
                },
                null).GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ──── Accessible-event signals. All are broadcast (null destination); the
    // registry and any listening AT match on interface/member/path.

    public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) =>
        EmitEvent("ChildrenChanged", parentPath, isAdd ? "add" : "remove", index, 0,
            (ref MessageWriter w) => AtSpiMarshal.WriteVariantObjectRef(ref w, BusName, childPath));

    public void EmitPropertyChange(string path, string atSpiProperty, string newValue) =>
        EmitEvent("PropertyChange", path, atSpiProperty, 0, 0,
            (ref MessageWriter w) => w.WriteVariantString(newValue));

    public void EmitStateChanged(string path, string state, bool active) =>
        EmitEvent("StateChanged", path, state, active ? 1 : 0, 0,
            static (ref MessageWriter w) => w.WriteVariantInt32(0));

    // ──── Cache signals. Keep AT clients' object caches warm so libatspi does
    // not treat every node we later signal as transient (and mark it defunct).

    public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) =>
        EmitCacheSignal("AddAccessible", "((so)(so)(so)iiassusau)",
            (ref MessageWriter w) => AtSpiMarshal.WriteCacheItem(ref w, tree, node));

    public void EmitCacheRemove(string path) =>
        EmitCacheSignal("RemoveAccessible", "(so)",
            (ref MessageWriter w) => AtSpiMarshal.WriteObjectRef(ref w, BusName, path));

    private void EmitCacheSignal(string member, string signature, WriteAnyData writeBody)
    {
        try
        {
            var writer = _a11y.GetMessageWriter();
            writer.WriteSignalHeader(
                destination: null!,
                path: AtSpiConstants.CachePath,
                @interface: AtSpiConstants.CacheInterface,
                member: member,
                signature: signature);
            writeBody(ref writer);
            var message = writer.CreateMessage();
            writer.Dispose();
            _a11y.TrySendMessage(message);
        }
        catch
        {
            // A dropped cache signal only means the client falls back to
            // GetItems / per-object queries — never fatal.
        }
    }

    private delegate void WriteAnyData(ref MessageWriter writer);

    private void EmitEvent(string member, string sourcePath, string detail, int detail1, int detail2, WriteAnyData writeAnyData)
    {
        try
        {
            var writer = _a11y.GetMessageWriter();
            // Signal signature: s detail, i detail1, i detail2, v any_data, (so) application.
            writer.WriteSignalHeader(
                destination: null!,
                path: sourcePath,
                @interface: AtSpiConstants.EventObjectInterface,
                member: member,
                signature: "siiv(so)");
            writer.WriteString(detail);
            writer.WriteInt32(detail1);
            writer.WriteInt32(detail2);
            writeAnyData(ref writer);
            AtSpiMarshal.WriteObjectRef(ref writer, BusName, AtSpiConstants.RootPath);
            var message = writer.CreateMessage();
            writer.Dispose();

            _a11y.TrySendMessage(message);
        }
        catch
        {
            // A dropped notification must never take down the UI thread; the
            // next structural rebuild re-syncs the client's tree anyway.
        }
    }

    public void Dispose()
    {
        _a11y.Dispose();
        _session.Dispose();
    }
}
