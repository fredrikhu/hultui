using System.Runtime.InteropServices;
using System.Text;
using HultUI.Core.Platform;
using WinFormsClipboardApi = System.Windows.Forms.Clipboard;

namespace HultUI.WinForms.Services;

/// <summary>
/// The system clipboard, through WinForms rather than SDL — so a HultUI control
/// copies and pastes with the same OS clipboard, in the same formats, as every other
/// control in the host application.
/// </summary>
/// <remarks>
/// The framework's clipboard is MIME-typed and Windows' is not, so the well-known
/// types are mapped to the Win32 format names other applications actually look for
/// (<c>HTML Format</c>, <c>Rich Text Format</c>) and anything unmapped round-trips
/// verbatim under its MIME string.
/// </remarks>
internal sealed class WinFormsClipboard : IClipboard
{
    public string? GetText()
        => Retry(() => WinFormsClipboardApi.ContainsText() ? WinFormsClipboardApi.GetText() : null);

    public void SetText(string text)
    {
        // Setting empty text throws on Windows; clearing is the honest equivalent.
        if (string.IsNullOrEmpty(text)) { Clear(); return; }
        Retry<object?>(() => { WinFormsClipboardApi.SetText(text); return null; });
    }

    public bool HasFormat(string mimeType)
        => Retry(() => WinFormsClipboardApi.ContainsData(ToWindowsFormat(mimeType)));

    public byte[]? GetData(string mimeType) => Retry(() =>
    {
        var format = ToWindowsFormat(mimeType);
        if (!WinFormsClipboardApi.ContainsData(format)) return null;

        return WinFormsClipboardApi.GetData(format) switch
        {
            byte[] bytes => bytes,
            // HTML and RTF arrive as strings; Windows stores HTML Format as UTF-8 with
            // its own byte-offset header, which is what a receiving app expects to see.
            string text => Encoding.UTF8.GetBytes(text),
            MemoryStream stream => stream.ToArray(),
            _ => null,
        };
    });

    public void SetData(IReadOnlyDictionary<string, byte[]> data)
    {
        if (data.Count == 0) { Clear(); return; }

        Retry<object?>(() =>
        {
            // One DataObject with every representation: taking ownership of the
            // clipboard discards what was there, so two calls would replace rather
            // than add.
            var payload = new DataObject();
            foreach (var (mimeType, bytes) in data)
            {
                var format = ToWindowsFormat(mimeType);
                // Text-shaped formats go across as strings — Windows stores HTML Format
                // and RTF that way, and a receiving app will not recognise a stream.
                // DataFormats' members are properties rather than constants, so this is a
                // comparison chain and not a pattern.
                if (format == DataFormats.UnicodeText || format == DataFormats.Text
                    || format == DataFormats.Html || format == DataFormats.Rtf)
                    payload.SetData(format, Encoding.UTF8.GetString(bytes));
                else
                    payload.SetData(format, new MemoryStream(bytes));
            }
            WinFormsClipboardApi.SetDataObject(payload, copy: true);
            return null;
        });
    }

    public void Clear() => Retry<object?>(() => { WinFormsClipboardApi.Clear(); return null; });

    private static string ToWindowsFormat(string mimeType) => mimeType switch
    {
        ClipboardFormats.PlainText => DataFormats.UnicodeText,
        ClipboardFormats.Html => DataFormats.Html,
        ClipboardFormats.Rtf => DataFormats.Rtf,
        ClipboardFormats.Png => "PNG",
        ClipboardFormats.Jpeg => "JFIF",
        ClipboardFormats.Uris => DataFormats.FileDrop,
        _ => mimeType,
    };

    /// <summary>
    /// The Windows clipboard is a single global resource another process can hold open,
    /// and the API throws <see cref="ExternalException"/> when it does. Every real
    /// clipboard user retries; failing a paste because another app was mid-copy would
    /// be the wrong answer to a transient condition.
    /// </summary>
    private static T? Retry<T>(Func<T?> operation)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return operation(); }
            catch (ExternalException) when (attempt < 4)
            {
                Thread.Sleep(10);
            }
            catch (ExternalException)
            {
                return default;   // still locked: behave as an empty clipboard
            }
        }
    }
}
