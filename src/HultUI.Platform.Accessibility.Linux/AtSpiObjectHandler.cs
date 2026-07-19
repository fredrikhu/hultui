using System.Runtime.Versioning;
using Tmds.DBus.Protocol;

namespace HultUI.Platform.Accessibility.Linux;

/// <summary>
/// Answers every incoming method call on the <c>/org/a11y/atspi/accessible/*</c>
/// subtree. A single handler serves the whole tree (<see cref="HandlesChildPaths"/>
/// = true), dispatching by the request's object path against the current
/// <see cref="IAtSpiTree"/> snapshot — so nodes appear and disappear by
/// dictionary mutation, never by registering or tearing down bus objects.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class AtSpiObjectHandler : IPathMethodHandler
{
    private readonly IAtSpiTree _tree;

    public AtSpiObjectHandler(IAtSpiTree tree) => _tree = tree;

    public string Path => AtSpiConstants.AccessibleNamespace;
    public bool HandlesChildPaths => true;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        string iface = request.InterfaceAsString ?? "";
        string member = request.MemberAsString ?? "";

        if (iface == AtSpiConstants.PeerInterface)
        {
            HandlePeer(context, member);
            return default;
        }

        if (iface == AtSpiConstants.IntrospectableInterface && member == "Introspect")
        {
            Reply(context, "s", (ref MessageWriter w) => w.WriteString(IntrospectXml));
            return default;
        }

        string path = request.PathAsString ?? "";
        var node = _tree.Find(path);
        if (node is null)
        {
            context.ReplyError("org.freedesktop.DBus.Error.UnknownObject", path);
            return default;
        }

        switch (iface)
        {
            case AtSpiConstants.PropertiesInterface: HandleProperties(context, member, node); break;
            case AtSpiConstants.AccessibleInterface: HandleAccessible(context, member, node); break;
            case AtSpiConstants.ComponentInterface: HandleComponent(context, member, node); break;
            case AtSpiConstants.ApplicationInterface: HandleApplication(context, member, node); break;
            case AtSpiConstants.ActionInterface: HandleAction(context, member, node); break;
            default: context.ReplyUnknownMethodError(); break;
        }
        return default;
    }

    // ──────────────────────────────────────────── org.freedesktop.DBus.Peer

    private static void HandlePeer(MethodContext context, string member)
    {
        switch (member)
        {
            case "Ping":
                Reply(context, "", static (ref MessageWriter _) => { });
                break;
            case "GetMachineId":
                Reply(context, "s", static (ref MessageWriter w) => w.WriteString(MachineId));
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    // ────────────────────────────────────── org.freedesktop.DBus.Properties

    private void HandleProperties(MethodContext context, string member, AtSpiAccessible node)
    {
        switch (member)
        {
            case "Get":
            {
                var reader = context.Request.GetBodyReader();
                string iface = reader.ReadString();
                string prop = reader.ReadString();
                Reply(context, "v", (ref MessageWriter w) => WritePropertyVariant(ref w, iface, prop, node));
                break;
            }
            case "GetAll":
            {
                var reader = context.Request.GetBodyReader();
                string iface = reader.ReadString();
                Reply(context, "a{sv}", (ref MessageWriter w) => WriteAllProperties(ref w, iface, node));
                break;
            }
            case "Set":
            {
                var reader = context.Request.GetBodyReader();
                string iface = reader.ReadString();
                string prop = reader.ReadString();
                if (iface == AtSpiConstants.ApplicationInterface && prop == "Id")
                {
                    reader.ReadSignature(); // variant signature "i"
                    _tree.ApplicationId = reader.ReadInt32();
                }
                Reply(context, "", static (ref MessageWriter _) => { });
                break;
            }
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    private void WritePropertyVariant(ref MessageWriter w, string iface, string prop, AtSpiAccessible node)
    {
        if (iface == AtSpiConstants.AccessibleInterface)
        {
            switch (prop)
            {
                case "Name": w.WriteVariantString(node.Name); return;
                case "Description": w.WriteVariantString(node.Description); return;
                case "HelpText": w.WriteVariantString(""); return;
                case "Locale": w.WriteVariantString(Locale); return;
                case "AccessibleId": w.WriteVariantString(""); return;
                case "ChildCount": w.WriteVariantInt32(node.ChildCount); return;
                case "Parent":
                    if (node.IsRoot)
                        AtSpiMarshal.WriteVariantObjectRef(ref w, _tree.Desktop.BusName, _tree.Desktop.Path);
                    else
                        AtSpiMarshal.WriteVariantObjectRef(ref w, _tree.BusName, node.ParentPath);
                    return;
            }
        }
        else if (iface == AtSpiConstants.ApplicationInterface)
        {
            switch (prop)
            {
                case "ToolkitName": w.WriteVariantString("HultUI"); return;
                case "Version": w.WriteVariantString(FrameworkVersion); return;
                case "AtspiVersion": w.WriteVariantString("2.1"); return;
                case "Id": w.WriteVariantInt32(_tree.ApplicationId); return;
            }
        }
        else if (iface == AtSpiConstants.ActionInterface && prop is "NActions" or "NbActions")
        {
            w.WriteVariantInt32(node.HasAction ? 1 : 0);
            return;
        }

        // Unknown property — an empty string keeps the reply well-formed.
        w.WriteVariantString("");
    }

    private void WriteAllProperties(ref MessageWriter w, string iface, AtSpiAccessible node)
    {
        var dict = w.WriteDictionaryStart();
        if (iface == AtSpiConstants.AccessibleInterface)
        {
            WriteStringEntry(ref w, "Name", node.Name);
            WriteStringEntry(ref w, "Description", node.Description);
            WriteStringEntry(ref w, "Locale", Locale);
            WriteStringEntry(ref w, "AccessibleId", "");
            WriteInt32Entry(ref w, "ChildCount", node.ChildCount);
            w.WriteDictionaryEntryStart();
            w.WriteString("Parent");
            if (node.IsRoot)
                AtSpiMarshal.WriteVariantObjectRef(ref w, _tree.Desktop.BusName, _tree.Desktop.Path);
            else
                AtSpiMarshal.WriteVariantObjectRef(ref w, _tree.BusName, node.ParentPath);
        }
        else if (iface == AtSpiConstants.ApplicationInterface)
        {
            WriteStringEntry(ref w, "ToolkitName", "HultUI");
            WriteStringEntry(ref w, "Version", FrameworkVersion);
            WriteStringEntry(ref w, "AtspiVersion", "2.1");
            WriteInt32Entry(ref w, "Id", _tree.ApplicationId);
        }
        w.WriteDictionaryEnd(dict);
    }

    private static void WriteStringEntry(ref MessageWriter w, string key, string value)
    {
        w.WriteDictionaryEntryStart();
        w.WriteString(key);
        w.WriteVariantString(value);
    }

    private static void WriteInt32Entry(ref MessageWriter w, string key, int value)
    {
        w.WriteDictionaryEntryStart();
        w.WriteString(key);
        w.WriteVariantInt32(value);
    }

    // ─────────────────────────────────────────── org.a11y.atspi.Accessible

    private void HandleAccessible(MethodContext context, string member, AtSpiAccessible node)
    {
        switch (member)
        {
            case "GetChildAtIndex":
            {
                var reader = context.Request.GetBodyReader();
                int index = reader.ReadInt32();
                string childPath = index >= 0 && index < node.ChildPaths.Count
                    ? node.ChildPaths[index] : null!;
                Reply(context, "(so)", (ref MessageWriter w) =>
                {
                    if (childPath is null) AtSpiMarshal.WriteNullRef(ref w);
                    else AtSpiMarshal.WriteObjectRef(ref w, _tree.BusName, childPath);
                });
                break;
            }
            case "GetChildren":
                Reply(context, "a(so)", (ref MessageWriter w) =>
                {
                    var arr = w.WriteArrayStart(DBusType.Struct);
                    foreach (var childPath in node.ChildPaths)
                        AtSpiMarshal.WriteObjectRef(ref w, _tree.BusName, childPath);
                    w.WriteArrayEnd(arr);
                });
                break;
            case "GetIndexInParent":
                Reply(context, "i", (ref MessageWriter w) => w.WriteInt32(node.IndexInParent));
                break;
            case "GetRole":
                Reply(context, "u", (ref MessageWriter w) => w.WriteUInt32(node.Role));
                break;
            case "GetRoleName":
            case "GetLocalizedRoleName":
                Reply(context, "s", (ref MessageWriter w) => w.WriteString(AtSpiConstants.RoleName(node.Role)));
                break;
            case "GetState":
                Reply(context, "au", (ref MessageWriter w) => AtSpiMarshal.WriteStateSet(ref w, node));
                break;
            case "GetAttributes":
                Reply(context, "a{ss}", static (ref MessageWriter w) =>
                {
                    var d = w.WriteDictionaryStart();
                    w.WriteDictionaryEnd(d);
                });
                break;
            case "GetApplication":
                Reply(context, "(so)", (ref MessageWriter w) =>
                    AtSpiMarshal.WriteObjectRef(ref w, _tree.BusName, AtSpiConstants.RootPath));
                break;
            case "GetInterfaces":
                Reply(context, "as", (ref MessageWriter w) => w.WriteArray(node.Interfaces));
                break;
            case "GetRelationSet":
                Reply(context, "a(ua(so))", static (ref MessageWriter w) =>
                {
                    var arr = w.WriteArrayStart(DBusType.Struct);
                    w.WriteArrayEnd(arr);
                });
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    // ──────────────────────────────────────────── org.a11y.atspi.Component

    private void HandleComponent(MethodContext context, string member, AtSpiAccessible node)
    {
        // Bounds are stored in device pixels (layout × display scale). The
        // coordType arg (SCREEN vs WINDOW) is ignored: under Wayland a client has
        // no reliable window screen origin, so both resolve to window-relative
        // pixels. On X11 the screen offset could be added when it lands.
        switch (member)
        {
            case "GetExtents":
                Reply(context, "(iiii)", (ref MessageWriter w) =>
                {
                    w.WriteStructureStart();
                    w.WriteInt32(node.X);
                    w.WriteInt32(node.Y);
                    w.WriteInt32(node.Width);
                    w.WriteInt32(node.Height);
                });
                break;
            case "GetPosition":
                Reply(context, "ii", (ref MessageWriter w) =>
                {
                    w.WriteInt32(node.X);
                    w.WriteInt32(node.Y);
                });
                break;
            case "GetSize":
                Reply(context, "ii", (ref MessageWriter w) =>
                {
                    w.WriteInt32(node.Width);
                    w.WriteInt32(node.Height);
                });
                break;
            case "GetLayer":
                Reply(context, "u", static (ref MessageWriter w) => w.WriteUInt32(AtSpiConstants.LayerWidget));
                break;
            case "GetMDIZOrder":
                Reply(context, "n", static (ref MessageWriter w) => w.WriteInt16(0));
                break;
            case "GetAlpha":
                Reply(context, "d", static (ref MessageWriter w) => w.WriteDouble(1.0));
                break;
            case "Contains":
            {
                var reader = context.Request.GetBodyReader();
                int x = reader.ReadInt32();
                int y = reader.ReadInt32();
                bool inside = x >= node.X && x < node.X + node.Width &&
                              y >= node.Y && y < node.Y + node.Height;
                Reply(context, "b", (ref MessageWriter w) => w.WriteBool(inside));
                break;
            }
            case "GetAccessibleAtPoint":
                Reply(context, "(so)", static (ref MessageWriter w) => AtSpiMarshal.WriteNullRef(ref w));
                break;
            case "GrabFocus":
                Reply(context, "b", static (ref MessageWriter w) => w.WriteBool(false));
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    // ────────────────────────────────────────── org.a11y.atspi.Application

    private void HandleApplication(MethodContext context, string member, AtSpiAccessible node)
    {
        _ = node;
        switch (member)
        {
            case "GetLocale":
                Reply(context, "s", static (ref MessageWriter w) => w.WriteString(Locale));
                break;
            case "RegisterEventListener":
            case "DeregisterEventListener":
                // We always emit; there is nothing to gate, so accept and ignore.
                Reply(context, "", static (ref MessageWriter _) => { });
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    // ─────────────────────────────────────────────── org.a11y.atspi.Action

    private void HandleAction(MethodContext context, string member, AtSpiAccessible node)
    {
        string action = node.ActionName;
        switch (member)
        {
            case "GetName":
            case "GetLocalizedName":
                Reply(context, "s", (ref MessageWriter w) => w.WriteString(action));
                break;
            case "GetDescription":
            case "GetKeyBinding":
                Reply(context, "s", static (ref MessageWriter w) => w.WriteString(""));
                break;
            case "GetActions":
                Reply(context, "a(sss)", (ref MessageWriter w) =>
                {
                    var arr = w.WriteArrayStart(DBusType.Struct);
                    if (node.HasAction)
                    {
                        w.WriteStructureStart();
                        w.WriteString(action);
                        w.WriteString("");
                        w.WriteString("");
                    }
                    w.WriteArrayEnd(arr);
                });
                break;
            case "DoAction":
                bool performed = _tree.DoAction(node);
                Reply(context, "b", (ref MessageWriter w) => w.WriteBool(performed));
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    // ──────────────────────────────────────────────────────────── helpers

    private static void Reply(MethodContext context, string signature, AtSpiMarshal.BodyWriter writeBody) =>
        AtSpiMarshal.Reply(context, signature, writeBody);

    private static readonly string Locale =
        System.Globalization.CultureInfo.CurrentCulture.Name.Replace('-', '_') + ".UTF-8";

    private static readonly string FrameworkVersion =
        typeof(AtSpiObjectHandler).Assembly.GetName().Version?.ToString() ?? "1.0";

    private static readonly string MachineId = ReadMachineId();

    private static string ReadMachineId()
    {
        foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
        {
            try
            {
                if (File.Exists(path)) return File.ReadAllText(path).Trim();
            }
            catch { /* fall through */ }
        }
        return new string('0', 32);
    }

    private const string IntrospectXml =
        "<!DOCTYPE node PUBLIC \"-//freedesktop//DTD D-BUS Object Introspection 1.0//EN\" " +
        "\"http://www.freedesktop.org/standards/dbus/1.0/introspect.dtd\">\n" +
        "<node>\n" +
        "  <interface name=\"org.a11y.atspi.Accessible\"/>\n" +
        "  <interface name=\"org.a11y.atspi.Component\"/>\n" +
        "</node>";
}
