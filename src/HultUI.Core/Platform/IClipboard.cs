namespace HultUI.Core.Platform;

/// <summary>Well-known clipboard MIME types, so callers do not retype the strings.</summary>
public static class ClipboardFormats
{
    public const string PlainText = "text/plain;charset=utf-8";
    public const string Html = "text/html";
    public const string Rtf = "text/rtf";
    public const string Png = "image/png";
    public const string Jpeg = "image/jpeg";
    public const string Svg = "image/svg+xml";
    public const string Uris = "text/uri-list";
}

/// <summary>
/// The system clipboard.
///
/// <para>Text has its own pair of methods because it is what most code wants and every
/// platform special-cases it. Everything else goes through the MIME-typed data methods:
/// the platform clipboard is a set of alternative representations of one selection, not a
/// single value, and "copy this as rich text <i>and</i> as plain text so it pastes usefully
/// anywhere" is the normal case rather than an advanced one.</para>
/// </summary>
public interface IClipboard
{
    string? GetText();

    void SetText(string text);

    /// <summary>Whether the clipboard currently offers this MIME type.</summary>
    bool HasFormat(string mimeType);

    /// <summary>
    /// The clipboard contents in this MIME type, or null when it is not on offer.
    /// </summary>
    byte[]? GetData(string mimeType);

    /// <summary>
    /// Replaces the clipboard with one selection offered in several formats at once —
    /// the receiving app picks whichever it understands. Ordering carries no preference;
    /// that is the consumer's decision.
    ///
    /// <para>Pass every representation in a single call. Two calls would not add a format
    /// to the first, it would replace it, because taking ownership of the clipboard
    /// discards whatever was there.</para>
    ///
    /// <para>MIME types are mapped to whatever the platform actually uses, so the data is
    /// visible to other applications rather than only to other HultUI apps:
    /// <see cref="ClipboardFormats.PlainText"/> and <see cref="ClipboardFormats.Html"/> in
    /// particular land in the formats Word, the browsers and every text editor look for.
    /// Anything unmapped is offered under its MIME string and round-trips verbatim.</para>
    /// </summary>
    void SetData(IReadOnlyDictionary<string, byte[]> data);

    /// <summary>Empties the clipboard.</summary>
    void Clear();
}
