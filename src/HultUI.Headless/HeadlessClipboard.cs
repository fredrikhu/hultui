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

    public string? GetText() => Text;

    public void SetText(string text) => Text = text;
}
