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
        {
            writer.WriteStructureStart();

            // self
            AtSpiMarshal.WriteObjectRef(ref writer, _tree.BusName, node.Path);
            // application
            AtSpiMarshal.WriteObjectRef(ref writer, _tree.BusName, AtSpiConstants.RootPath);
            // parent
            if (node.IsRoot)
                AtSpiMarshal.WriteObjectRef(ref writer, _tree.Desktop.BusName, _tree.Desktop.Path);
            else
                AtSpiMarshal.WriteObjectRef(ref writer, _tree.BusName, node.ParentPath);

            writer.WriteInt32(node.IndexInParent);
            writer.WriteInt32(node.ChildCount);
            writer.WriteArray(node.Interfaces);
            writer.WriteString(node.Name);
            writer.WriteUInt32(node.Role);
            writer.WriteString(node.Description);
            AtSpiMarshal.WriteStateSet(ref writer, node);
        }
        writer.WriteArrayEnd(array);
    }
}
