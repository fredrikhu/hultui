using System.Runtime.Versioning;
using HultUI.Core.Elements;
using Tmds.DBus.Protocol;

namespace HultUI.Platform.Accessibility.Linux;

/// <summary>
/// Owns the connection to the AT-SPI bus and emits high-level signals on
/// behalf of the bridge. The a11y bus is a separate D-Bus daemon from the
/// session bus — we discover its address by calling <c>GetAddress</c> on
/// <c>org.a11y.Bus</c> on the session bus, then dial the returned address.
/// </summary>
/// <remarks>
/// Many of the emit methods below are stubs while the per-object D-Bus
/// interface export is being built out. The exact D-Bus message bodies are
/// tracked in the AT-SPI2 spec:
///   https://accessibility.linuxfoundation.org/a11yspecs/atspi/adoc/atspi-dbus.html
/// Once verified on a real desktop the method bodies will construct proper
/// <see cref="MessageWriter"/> payloads.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class AtSpiBus : IDisposable
{
    private readonly DBusConnection _sessionDBusConnection;
    private readonly DBusConnection _a11yDBusConnection;

    private AtSpiBus(DBusConnection sessionDBusConnection, DBusConnection a11yDBusConnection)
    {
        _sessionDBusConnection = sessionDBusConnection;
        _a11yDBusConnection = a11yDBusConnection;
    }

    /// <summary>
    /// Opens both bus connections and returns an <see cref="AtSpiBus"/>. Returns
    /// null if either bus is unavailable — caller should treat that as "AT-SPI
    /// not present" and fall back to the null bridge.
    /// </summary>
    public static AtSpiBus? Open()
    {
        var sessionAddress = DBusAddress.Session;
        if (sessionAddress is null) return null;

        var sessionDBusConnection = new DBusConnection(sessionAddress);
        try
        {
            sessionDBusConnection.ConnectAsync().GetAwaiter().GetResult();
        }
        catch
        {
            sessionDBusConnection.Dispose();
            return null;
        }

        var a11yAddress = QueryA11yBusAddress(sessionDBusConnection);
        if (a11yAddress is null)
        {
            sessionDBusConnection.Dispose();
            return null;
        }

        var a11yDBusConnection = new DBusConnection(a11yAddress);
        try
        {
            a11yDBusConnection.ConnectAsync().GetAwaiter().GetResult();
        }
        catch
        {
            a11yDBusConnection.Dispose();
            sessionDBusConnection.Dispose();
            return null;
        }

        return new AtSpiBus(sessionDBusConnection, a11yDBusConnection);
    }

    private static string? QueryA11yBusAddress(DBusConnection session)
    {
        // TODO: Call org.a11y.Bus.GetAddress on /org/a11y/bus over the session
        // connection. The concrete Tmds.DBus.Protocol message-construction API
        // differs across versions; leaving as a stub until verified against a
        // live AT-SPI daemon (verifying through mock is not meaningful here).
        // Returning null short-circuits Open() so the bridge stays inactive.
        _ = session;
        return null;
    }

    /// <summary>
    /// Calls <c>org.a11y.atspi.Socket.Embed</c> on the registry with our
    /// application root path. The registry returns an "embed socket" string
    /// that applications embedding into plugs would use; for an ordinary app
    /// we just need a successful reply to know we're live.
    /// </summary>
    public bool RegisterApplication()
    {
        // TODO: Construct Embed call once per-object interface export is
        // wired. For now we return false so the bridge stays inactive — this
        // lets us land the structure without emitting malformed D-Bus traffic.
        return false;
    }

    // ──── Signal emitters (stubs — wire up in the follow-up).
    public void EmitChildrenChanged(ElementId? parentId, ElementId childId, bool isAdd) { }
    public void EmitPropertyChange(ElementId id, string atSpiPropertyName) { }
    public void EmitStateChanged(ElementId id, string state, bool active) { }

    internal DBusConnection A11yDBusConnection => _a11yDBusConnection;

    public void Dispose()
    {
        _a11yDBusConnection.Dispose();
        _sessionDBusConnection.Dispose();
    }
}
