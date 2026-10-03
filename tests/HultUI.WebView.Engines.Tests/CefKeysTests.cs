using HultUI.Core.Events;
using HultUI.WebView.Cef;
using Xunit;

namespace HultUI.WebView.Engines.Tests;

/// <summary>The keys CEF is told of: a key named by <see cref="KeyCode"/> must be the same key the
/// page sees for its scan code. Plain tables, so these run without CEF.</summary>
public class CefKeysTests
{
    [Theory]
    [InlineData(KeyCode.Numpad0, 0x62)]
    [InlineData(KeyCode.Numpad1, 0x59)]
    [InlineData(KeyCode.Numpad9, 0x61)]
    [InlineData(KeyCode.NumpadDecimal, 0x63)]
    [InlineData(KeyCode.NumpadAdd, 0x57)]
    [InlineData(KeyCode.NumpadDivide, 0x54)]
    [InlineData(KeyCode.NumpadEnter, 0x58)]
    [InlineData(KeyCode.NumpadEquals, 0x67)]
    [InlineData(KeyCode.F13, 0x68)]
    [InlineData(KeyCode.F24, 0x73)]
    public void AKeypadOrF13UpKey_ByName_IsTheKeyItsScanCodeIs(KeyCode key, int usage)
    {
        Assert.Equal(usage, CefKeys.Usage(key, 0));
        Assert.NotEqual(0, CefKeys.WindowsKeyCode(key, 0));
        Assert.Equal(CefKeys.WindowsKeyCode(KeyCode.Unknown, (ushort)usage), CefKeys.WindowsKeyCode(key, 0));
        Assert.Equal(CefKeys.Flags(KeyModifiers.None, KeyCode.Unknown, (ushort)usage), CefKeys.Flags(KeyModifiers.None, key));
        Assert.Equal(CefKeys.ControlCharacter(KeyCode.Unknown, (ushort)usage), CefKeys.ControlCharacter(key, 0));
    }
}
