using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;

namespace HultUI.Core.RichText;

/// <summary>
/// What a rich editor puts on the clipboard: its own format, which another editor pastes without
/// losing anything; HTML, which browsers, Word and mail read; and plain text for everything else.
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
                };
                result.Add(block.WithContent(kind == BlockKind.Rule ? "" : text, spans));
            }
            return result.Count == 0 ? null : new RichFragment(result.ToImmutable());
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or ArgumentException)
        {
            return null;
        }
    }

    internal static string Hex(Color c) =>
        c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";
}

/// <summary>
/// Blocks as HTML: what other applications read when a rich editor's selection is pasted into
/// them. Runs of list items become nested lists; marks become the elements a browser would use.
/// </summary>
public static class RichHtml
{
    public static string Write(IReadOnlyList<RichBlock> blocks)
    {
        var sb = new StringBuilder();
        // The lists open around the current block, innermost last: their kind and depth.
        var open = new Stack<(string Tag, int Depth)>();

        foreach (var b in blocks)
        {
            if (b.IsListItem)
            {
                string tag = b.Kind == BlockKind.NumberedItem ? "ol" : "ul";
                while (open.Count > 0 && (open.Peek().Depth > b.Indent || (open.Peek().Depth == b.Indent && open.Peek().Tag != tag)))
                    sb.Append("</").Append(open.Pop().Tag).Append('>');
                while (open.Count == 0 || open.Peek().Depth < b.Indent)
                {
                    int depth = open.Count == 0 ? 0 : open.Peek().Depth + 1;
                    if (depth > b.Indent) break;
                    string t = depth == b.Indent ? tag : "ul";
                    sb.Append('<').Append(t).Append('>');
                    open.Push((t, depth));
                }
                sb.Append("<li>");
                if (b.Kind == BlockKind.TaskItem)
                    sb.Append(b.Checked ? "<input type=\"checkbox\" checked disabled> " : "<input type=\"checkbox\" disabled> ");
                Inline(sb, b);
                sb.Append("</li>");
                continue;
            }

            while (open.Count > 0) sb.Append("</").Append(open.Pop().Tag).Append('>');
            string align = b.Align switch
            {
                BlockAlign.Center => " style=\"text-align:center\"",
                BlockAlign.End => " style=\"text-align:right\"",
                _ => "",
            };
            switch (b.Kind)
            {
                case BlockKind.Heading:
                    sb.Append("<h").Append(b.HeadingLevel).Append(align).Append('>');
                    Inline(sb, b);
                    sb.Append("</h").Append(b.HeadingLevel).Append('>');
                    break;
                case BlockKind.Quote:
                    sb.Append("<blockquote>");
                    Inline(sb, b);
                    sb.Append("</blockquote>");
                    break;
                case BlockKind.CodeBlock or BlockKind.Raw:
                    sb.Append("<pre><code>").Append(WebUtility.HtmlEncode(b.Text)).Append("</code></pre>");
                    break;
                case BlockKind.Rule:
                    sb.Append("<hr>");
                    break;
                default:
                    sb.Append("<p").Append(align).Append('>');
                    Inline(sb, b);
                    sb.Append("</p>");
                    break;
            }
        }
        while (open.Count > 0) sb.Append("</").Append(open.Pop().Tag).Append('>');
        return sb.ToString();
    }

    private static void Inline(StringBuilder sb, RichBlock block)
    {
        foreach (var run in RichSpans.Runs(block.Spans, block.Text.Length))
        {
            var m = run.Marks;
            var close = new Stack<string>();
            void Open(string tag, string? attributes = null)
            {
                sb.Append('<').Append(tag);
                if (attributes is not null) sb.Append(' ').Append(attributes);
                sb.Append('>');
                close.Push(tag);
            }

            if (m.Link is { } link) Open("a", $"href=\"{WebUtility.HtmlEncode(link)}\"");
            if (m.Has(InlineStyle.Bold)) Open("strong");
            if (m.Has(InlineStyle.Italic)) Open("em");
            if (m.Has(InlineStyle.Underline)) Open("u");
            if (m.Has(InlineStyle.Strike)) Open("s");
            if (m.Has(InlineStyle.Code)) Open("code");
            var css = new List<string>();
            if (m.Color is { } c) css.Add("color:" + RichClipboard.Hex(c));
            if (m.Highlight is { } h) css.Add("background-color:" + RichClipboard.Hex(h));
            if (m.Size is { } size) css.Add("font-size:" + size.ToString("0.##", CultureInfo.InvariantCulture) + "px");
            if (m.Family is { } family) css.Add("font-family:" + WebUtility.HtmlEncode(family));
            if (css.Count > 0) Open("span", $"style=\"{string.Join(";", css)}\"");

            string text = block.Text.Substring(run.Start, run.Length);
            sb.Append(WebUtility.HtmlEncode(text).Replace("\n", "<br>"));
            while (close.Count > 0) sb.Append("</").Append(close.Pop()).Append('>');
        }
    }
}
