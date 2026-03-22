using HultUI.Core.Platform;

namespace HultUI.Platform.SDL3;

public sealed class SDL3Clipboard : IClipboard
{
    private readonly SDL3Window _window;

    public SDL3Clipboard(SDL3Window window)
    {
        _window = window;
    }

    public string? GetText() => _window.GetClipboardText();
    public void SetText(string text) => _window.SetClipboardText(text);
}
