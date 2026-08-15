using HultUI.Core.Platform;

namespace HultUI.Headless;

/// <summary>
/// In-memory clipboard, isolated per headless window — tests can exercise
/// copy/paste without touching (or being affected by) the real OS clipboard.
/// </summary>
public sealed class HeadlessClipboard : IClipboard
{
    /// <summary>The current clipboard content; settable directly from tests.</summary>
    public string? Text { get; set; }

    /// <summary>Non-text formats, by MIME type. Settable directly from tests.</summary>
    public Dictionary<string, byte[]> Data { get; } = new(StringComparer.Ordinal);

    public string? GetText() => Text;

    public void SetText(string text)
    {
        // Taking the clipboard as text drops the other representations, matching a real
        // platform: an owner offers one selection, not an accumulating pile of formats.
        Data.Clear();
        Text = text;
    }

    public bool HasFormat(string mimeType) =>
        Data.ContainsKey(mimeType) || (Text is not null && mimeType == ClipboardFormats.PlainText);

    public byte[]? GetData(string mimeType)
    {
        if (Data.TryGetValue(mimeType, out var bytes)) return bytes;
        return Text is not null && mimeType == ClipboardFormats.PlainText
            ? System.Text.Encoding.UTF8.GetBytes(Text)
            : null;
    }

    public void SetData(IReadOnlyDictionary<string, byte[]> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data.Clear();
        Text = null;
        foreach (var (mime, bytes) in data)
        {
            Data[mime] = bytes.ToArray();
            if (mime == ClipboardFormats.PlainText)
                Text = System.Text.Encoding.UTF8.GetString(bytes);
        }
    }

    public void Clear()
    {
        Data.Clear();
        Text = null;
    }
}
