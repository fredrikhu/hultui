using System.Runtime.Versioning;
using Tmds.DBus.Protocol;

namespace HultUI.Platform.Accessibility.Linux;

/// <summary>
/// Serves <c>org.a11y.atspi.Cache.GetItems</c> at <c>/org/a11y/atspi/cache</c>.
/// Assistive technologies fetch the whole accessible tree in one call here
/// rather than walking it property-by-property — the same denormalization our
/// render-side tree diff exists to avoid.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class AtSpiCacheHandler : IPathMethodHandler
{
    // The AT-SPI cache item: self-ref, app-ref, parent-ref, index-in-parent,
    // child-count, interfaces, name, role, description, state-set.
    private const string CacheItemSignature = "a((so)(so)(so)iiassusau)";

    private readonly IAtSpiTree _tree;

    public AtSpiCacheHandler(IAtSpiTree tree) => _tree = tree;

    public string Path => AtSpiConstants.CachePath;
    public bool HandlesChildPaths => false;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        string iface = context.Request.InterfaceAsString ?? "";
        string member = context.Request.MemberAsString ?? "";

        if (iface == AtSpiConstants.PeerInterface && member == "Ping")
        {
            AtSpiMarshal.Reply(context, "", static (ref MessageWriter _) => { });
            return default;
        }

        if (iface == AtSpiConstants.CacheInterface && member == "GetItems")
        {
            AtSpiMarshal.Reply(context, CacheItemSignature, WriteItems);
            return default;
        }

        context.ReplyUnknownMethodError();
        return default;
    }

    private void WriteItems(ref MessageWriter writer)
    {
        var array = writer.WriteArrayStart(DBusType.Struct);
        foreach (var node in _tree.All)
            AtSpiMarshal.WriteCacheItem(ref writer, _tree, node);
        writer.WriteArrayEnd(array);
    }
}
