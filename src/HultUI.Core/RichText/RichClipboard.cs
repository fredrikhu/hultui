using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;

namespace HultUI.Core.RichText;

/// <summary>
/// What a rich editor puts on the clipboard: its own format, which another editor pastes without
/// losing anything; HTML, which browsers, Word and mail read; RTF, which word processors read;
/// and plain text for everything else.
/// </summary>
public static class RichClipboard
{
    /// <summary>The editor's own format: a fragment as JSON.</summary>
    public const string MimeType = "application/x-hultui-rich+json";

    /// <summary>Every format a copy of <paramref name="fragment"/> offers, for
    /// <see cref="IClipboard.SetData"/>.</summary>
    public static IReadOnlyDictionary<string, byte[]> Formats(RichFragment fragment) => new Dictionary<string, byte[]>
    {
        [ClipboardFormats.PlainText] = Encoding.UTF8.GetBytes(fragment.ToPlainText()),
        [ClipboardFormats.Html] = Encoding.UTF8.GetBytes(RichHtml.Write(fragment.Blocks)),
        // RTF is 7-bit: everything past ASCII is written as \u escapes.
        [ClipboardFormats.Rtf] = Encoding.ASCII.GetBytes(RichRtf.Write(fragment.Blocks)),
        [MimeType] = Write(fragment),
    };

    // ── The editor's own format ──

    public static byte[] Write(RichFragment fragment)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNumber("v", 1);
            w.WriteStartArray("blocks");
            foreach (var b in fragment.Blocks)
            {
                w.WriteStartObject();
                w.WriteString("kind", b.Kind.ToString());
                w.WriteString("text", b.Text);
                if (b.HeadingLevel != 0) w.WriteNumber("heading", b.HeadingLevel);
                if (b.Indent != 0) w.WriteNumber("indent", b.Indent);
                if (b.Align != BlockAlign.Start) w.WriteString("align", b.Align.ToString());
                if (b.Checked) w.WriteBoolean("checked", true);
                if (b.Info is { } info) w.WriteString("info", info);
                if (b.IsTableCell)
                {
                    w.WriteNumber("row", b.Row);
                    w.WriteNumber("column", b.Column);
                }
                if (b.Image is { } image)
                {
                    w.WriteStartObject("image");
                    w.WriteString("source", image.Source);
                    if (image.Alt.Length > 0) w.WriteString("alt", image.Alt);
                    if (image.Title is { } title) w.WriteString("title", title);
                    if (image.Width is { } width) w.WriteNumber("width", width);
                    if (image.Link is { } href) w.WriteString("link", href);
                    w.WriteEndObject();
                }
                if (b.Spans.Length > 0)
                {
                    w.WriteStartArray("spans");
                    foreach (var s in b.Spans)
                    {
                        w.WriteStartObject();
                        w.WriteNumber("start", s.Start);
                        w.WriteNumber("length", s.Length);
                        var m = s.Marks;
                        if (m.Style != InlineStyle.None) w.WriteNumber("style", (int)m.Style);
                        if (m.Color is { } c) w.WriteString("color", Hex(c));
                        if (m.Highlight is { } h) w.WriteString("highlight", Hex(h));
                        if (m.Size is { } size) w.WriteNumber("size", size);
                        if (m.Family is { } family) w.WriteString("family", family);
                        if (m.Link is { } link) w.WriteString("link", link);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>The fragment in <paramref name="data"/>, or null when it is not one this version
    /// understands — the clipboard is anyone's to write.</summary>
    public static RichFragment? Read(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (!root.TryGetProperty("blocks", out var blocks) || blocks.ValueKind != JsonValueKind.Array) return null;

            var result = ImmutableArray.CreateBuilder<RichBlock>();
            foreach (var b in blocks.EnumerateArray())
            {
                if (!Enum.TryParse<BlockKind>(b.GetProperty("kind").GetString(), out var kind)) kind = BlockKind.Paragraph;
                string text = b.GetProperty("text").GetString() ?? "";
                var spans = new List<RichSpan>();
                if (b.TryGetProperty("spans", out var list))
                    foreach (var s in list.EnumerateArray())
                        spans.Add(new RichSpan(s.GetProperty("start").GetInt32(), s.GetProperty("length").GetInt32(),
                            new RichMarks(
                                s.TryGetProperty("style", out var st) ? (InlineStyle)st.GetInt32() : InlineStyle.None,
                                s.TryGetProperty("color", out var c) ? Color.FromHex(c.GetString()!) : null,
                                s.TryGetProperty("highlight", out var h) ? Color.FromHex(h.GetString()!) : null,
                                s.TryGetProperty("size", out var size) ? size.GetSingle() : null,
                                s.TryGetProperty("family", out var f) ? f.GetString() : null,
                                s.TryGetProperty("link", out var l) ? l.GetString() : null)));

                var block = new RichBlock
                {
                    Kind = kind,
                    HeadingLevel = b.TryGetProperty("heading", out var hl) ? Math.Clamp(hl.GetInt32(), 1, 6) : kind == BlockKind.Heading ? 1 : 0,
                    Indent = b.TryGetProperty("indent", out var ind) ? Math.Clamp(ind.GetInt32(), 0, RichBlock.MaxIndent) : 0,
                    Align = b.TryGetProperty("align", out var al) && Enum.TryParse<BlockAlign>(al.GetString(), out var a) ? a : BlockAlign.Start,
                    Checked = b.TryGetProperty("checked", out var ch) && ch.GetBoolean(),
                    Info = b.TryGetProperty("info", out var inf) ? inf.GetString() : null,
                    Row = b.TryGetProperty("row", out var row) ? Math.Max(0, row.GetInt32()) : 0,
                    Column = b.TryGetProperty("column", out var col) ? Math.Max(0, col.GetInt32()) : 0,
                    Image = b.TryGetProperty("image", out var img) ? ReadImage(img) : null,
                };
                // A picture without one to show is nothing at all.
                if (kind == BlockKind.Image && block.Image is null) continue;
                result.Add(block.WithContent(block.IsVoid ? "" : text, spans));
            }
            return result.Count == 0 ? null : new RichFragment(result.ToImmutable());
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static RichImage? ReadImage(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty("source", out var source) && source.GetString() is { } src
            ? new RichImage(src,
                e.TryGetProperty("alt", out var alt) ? alt.GetString() ?? "" : "",
                e.TryGetProperty("title", out var title) ? title.GetString() : null,
                e.TryGetProperty("width", out var width) ? width.GetSingle() : null,
                e.TryGetProperty("link", out var link) ? link.GetString() : null)
            : null;

    internal static string Hex(Color c) =>
        c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";
}
