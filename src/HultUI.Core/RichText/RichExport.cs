using System.Buffers.Binary;
using System.Text;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.RichText;

/// <summary>
/// How a document is written out as a file another program opens: by
/// <see cref="RichHtml.WriteDocument"/> as a web page, by <see cref="RichRtf.Write(RichDocument, RichExportOptions?)"/>
/// as a word processor's file.
/// </summary>
public sealed record RichExportOptions
{
    /// <summary>The document's title: the web page's, and the RTF file's properties.</summary>
    public string? Title { get; init; }

    /// <summary>The body face. Null: the browser's own sans-serif in HTML, Arial in RTF.</summary>
    public string? FontFamily { get; init; }

    /// <summary>The body size in layout units, which are CSS pixels: 0.75 pt each.</summary>
    public float FontSize { get; init; } = 14f;

    /// <summary>
    /// The editor's proportions: heading sizes and weight, the code face and scale, the indent,
    /// spacing and cell padding. Null: <see cref="RichTextTheme"/>'s defaults. Its colours are
    /// not used. A document is read on paper, whatever theme the editor was in, so the writers
    /// bring their own (<see cref="RichPaper"/>).
    /// </summary>
    public RichTextTheme? Theme { get; init; }

    /// <summary>
    /// A picture's file, to embed in the document; null leaves that picture out. HTML takes PNG,
    /// JPEG, GIF, WebP, BMP and SVG, and writes a picture it is not given by its source. RTF
    /// shows only PNG and JPEG, and writes any other picture as its alt text in brackets.
    /// </summary>
    public Func<RichImage, byte[]?>? Pictures { get; init; }

    internal RichTextTheme Look => Theme ?? Defaults;
    private static readonly RichTextTheme Defaults = new();
}

/// <summary>The colours an exported document is drawn in: dark text on white, whatever the
/// editor's theme.</summary>
internal static class RichPaper
{
    public static readonly Color Text = Color.FromHex("#1F2328");
    public static readonly Color Link = Color.FromHex("#0969DA");
    public static readonly Color Line = Color.FromHex("#D0D7DE");
    public static readonly Color Muted = Color.FromHex("#57606A");
    public static readonly Color Code = Color.FromHex("#F0F1F3");
    public static readonly Color Header = Color.FromHex("#F6F8FA");

    /// <summary>The text column of a page with inch margins: 6.5 in, in layout units.</summary>
    public const float TextWidth = 624f;

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>The names CSS gives generic families, which a word processor does not know.</summary>
    public static bool IsGeneric(string family) =>
        family is "serif" or "sans-serif" or "monospace" or "system-ui" or "cursive" or "fantasy"
            or "ui-monospace" or "ui-sans-serif" or "ui-serif";
}

/// <summary>A picture file's format and size in pixels, read from its header. A size of 0 is
/// one the header does not say (WebP, SVG).</summary>
internal readonly record struct RichPictureFile(string MediaType, int Width, int Height)
{
    public static RichPictureFile? Read(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 24 && d[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return new("image/png", BinaryPrimitives.ReadInt32BigEndian(d[16..]), BinaryPrimitives.ReadInt32BigEndian(d[20..]));
        if (d.Length >= 4 && d[0] == 0xFF && d[1] == 0xD8)
            return Jpeg(d);
        if (d.Length >= 10 && (d[..6].SequenceEqual("GIF87a"u8) || d[..6].SequenceEqual("GIF89a"u8)))
            return new("image/gif", BinaryPrimitives.ReadUInt16LittleEndian(d[6..]), BinaryPrimitives.ReadUInt16LittleEndian(d[8..]));
        if (d.Length >= 26 && d[0] == 'B' && d[1] == 'M')
            return new("image/bmp", BinaryPrimitives.ReadInt32LittleEndian(d[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(d[22..])));
        if (d.Length >= 12 && d[..4].SequenceEqual("RIFF"u8) && d[8..12].SequenceEqual("WEBP"u8))
            return new("image/webp", 0, 0);
        var head = Encoding.UTF8.GetString(d[..Math.Min(d.Length, 256)]).TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (head.StartsWith("<svg", StringComparison.Ordinal) || (head.StartsWith("<?xml", StringComparison.Ordinal) && head.Contains("<svg")))
            return new("image/svg+xml", 0, 0);
        return null;
    }

    // The size is in the first start-of-frame segment; every segment before it says how long it is.
    private static RichPictureFile? Jpeg(ReadOnlySpan<byte> d)
    {
        int i = 2;
        while (i + 4 <= d.Length)
        {
            if (d[i] != 0xFF) return null;
            byte marker = d[i + 1];
            if (marker == 0xFF) { i++; continue; }   // fill
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; }   // no length
            int length = BinaryPrimitives.ReadUInt16BigEndian(d[(i + 2)..]);
            bool frame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (frame && i + 9 <= d.Length)
                return new("image/jpeg", BinaryPrimitives.ReadUInt16BigEndian(d[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(d[(i + 5)..]));
            i += 2 + length;
        }
        return new("image/jpeg", 0, 0);
    }
}
