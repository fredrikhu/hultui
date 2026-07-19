using System.Runtime.Versioning;
using Tmds.DBus.Protocol;

namespace HultUI.Platform.Accessibility.Linux;

/// <summary>
/// Low-level marshalling helpers for the AT-SPI wire types that appear
/// everywhere in the protocol: the <c>(so)</c> object reference, the
/// variant-wrapped forms of it, and the <c>au</c> state bitfield.
/// </summary>
/// <remarks>
/// <see cref="MessageWriter"/> and <see cref="Reader"/> are ref structs that
/// carry mutable cursor state, so every helper takes them <c>ref</c> — a
/// by-value copy would write to a detached cursor and silently drop bytes.
/// </remarks>
[SupportedOSPlatform("linux")]
internal static class AtSpiMarshal
{
    /// <summary>Writes the body of an outgoing reply into a ref-struct writer.</summary>
    public delegate void BodyWriter(ref MessageWriter writer);

    /// <summary>
    /// Builds and sends a method reply with the given body signature. Pass an
    /// empty signature for a reply with no body.
    /// </summary>
    public static void Reply(MethodContext context, string signature, BodyWriter body)
    {
        // MessageWriter is a ref struct, so it cannot be a 'using' variable and
        // be passed by ref at the same time — dispose it by hand instead.
        var writer = context.CreateReplyWriter(signature.Length == 0 ? null! : signature);
        try
        {
            body(ref writer);
            context.Reply(writer.CreateMessage());
        }
        finally { writer.Dispose(); }
    }

    /// <summary>Writes a bare <c>(so)</c> object reference (bus name + path).</summary>
    public static void WriteObjectRef(ref MessageWriter writer, string busName, string path)
    {
        writer.WriteStructureStart();
        writer.WriteString(busName);
        writer.WriteObjectPath(path);
    }

    /// <summary>Writes the AT-SPI null reference (<c>""</c>, <c>/org/a11y/atspi/null</c>).</summary>
    public static void WriteNullRef(ref MessageWriter writer) =>
        WriteObjectRef(ref writer, "", AtSpiConstants.NullPath);

    /// <summary>Writes a <c>(so)</c> reference wrapped in a variant.</summary>
    public static void WriteVariantObjectRef(ref MessageWriter writer, string busName, string path)
    {
        writer.WriteSignature("(so)");
        WriteObjectRef(ref writer, busName, path);
    }

    /// <summary>Reads a <c>(so)</c> reference from a message body.</summary>
    public static (string BusName, string Path) ReadObjectRef(ref Reader reader)
    {
        reader.AlignStruct();
        string name = reader.ReadString();
        string path = reader.ReadObjectPathAsString();
        return (name, path);
    }

    /// <summary>
    /// Writes an accessible's state set as the AT-SPI <c>au</c> value: two
    /// uint32 words holding the 64-bit <c>AtspiStateType</c> bitfield.
    /// </summary>
    public static void WriteStateSet(ref MessageWriter writer, AtSpiAccessible node)
    {
        uint low = 0;
        void Set(int bit) => low |= 1u << bit;

        Set(AtSpiConstants.StateVisible);
        if (!node.Offscreen) Set(AtSpiConstants.StateShowing);
        if (node.Enabled)
        {
            Set(AtSpiConstants.StateEnabled);
            Set(AtSpiConstants.StateSensitive);
        }
        if (node.Focusable) Set(AtSpiConstants.StateFocusable);
        if (node.Focused) Set(AtSpiConstants.StateFocused);
        if (node.Selectable) Set(AtSpiConstants.StateSelectable);
        if (node.Selected) Set(AtSpiConstants.StateSelected);
        if (node.Toggled == true) Set(AtSpiConstants.StateChecked);
        if (node.IsRoot) Set(AtSpiConstants.StateActive);

        // Every state we model lives in the low word; the high word stays 0.
        writer.WriteArray(new[] { low, 0u });
    }
}
