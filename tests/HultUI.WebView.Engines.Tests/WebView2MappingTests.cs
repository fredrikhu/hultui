using HultUI.Core.Events;
using HultUI.Core.Styling;
using HultUI.WebView.WebView2;
using HultUI.WebView.WebView2.Interop;
using Xunit;

namespace HultUI.WebView.Engines.Tests;

/// <summary>
/// The WebView2 engine's translations, which need no runtime and run on any OS: pointer
/// input to WebView2's mouse events, the origin messages are filtered by, cursors and error
/// names — plus the vtable slots the spike verified by calling them, as a check on the
/// generator. The engine itself is tested on a real window in HultUI.UiTests.
/// </summary>
public class WebView2MappingTests
{
    [Theory]
    [InlineData(WebPointerAction.Move, WebPointerButtons.None, 1, 512)]
    [InlineData(WebPointerAction.Down, WebPointerButtons.Left, 1, 513)]
    [InlineData(WebPointerAction.Down, WebPointerButtons.Left, 2, 515)]
    [InlineData(WebPointerAction.Up, WebPointerButtons.Left, 1, 514)]
    [InlineData(WebPointerAction.Down, WebPointerButtons.Right, 1, 516)]
    [InlineData(WebPointerAction.Up, WebPointerButtons.Right, 1, 517)]
    [InlineData(WebPointerAction.Down, WebPointerButtons.Middle, 1, 519)]
    [InlineData(WebPointerAction.Up, WebPointerButtons.Middle, 1, 520)]
    [InlineData(WebPointerAction.Leave, WebPointerButtons.None, 0, 675)]
    public void PointerInput_MapsToWebView2sMouseEventKinds(WebPointerAction action, WebPointerButtons button, int clicks, int kind)
    {
        var (mapped, _) = WebView2View.MouseEvent(new WebPointerInput(action, 5, 5, button, ClickCount: clicks));
        Assert.Equal(kind, mapped);
    }

    [Fact]
    public void TheSideButtons_AreXButtons_SaidWhichInTheMouseData()
    {
        Assert.Equal((523, 1u), WebView2View.MouseEvent(new WebPointerInput(WebPointerAction.Down, 0, 0, WebPointerButtons.Back, ClickCount: 1)));
        Assert.Equal((524, 2u), WebView2View.MouseEvent(new WebPointerInput(WebPointerAction.Up, 0, 0, WebPointerButtons.Forward, ClickCount: 1)));
    }

    [Fact]
    public void TheWheel_IsInWheelDeltas_AndHorizontalFlipsToWebView2sSense()
    {
        // One notch away from the user scrolls up: +120, WHEEL_DELTA.
        Assert.Equal((522, 120u), WebView2View.MouseEvent(new WebPointerInput(WebPointerAction.Wheel, 0, 0, WheelY: 1)));
        Assert.Equal((522, unchecked((uint)-60)), WebView2View.MouseEvent(new WebPointerInput(WebPointerAction.Wheel, 0, 0, WheelY: -0.5f)));
        // HultUI's positive horizontal scrolls left (as ScrollView reads it); WebView2's positive is right.
        Assert.Equal((526, unchecked((uint)-120)), WebView2View.MouseEvent(new WebPointerInput(WebPointerAction.Wheel, 0, 0, WheelX: 1)));
    }

    [Fact]
    public void HeldButtonsAndModifiers_BecomeVirtualKeys()
    {
        var input = new WebPointerInput(WebPointerAction.Move, 0, 0,
            Held: WebPointerButtons.Left | WebPointerButtons.Middle, Modifiers: KeyModifiers.Ctrl | KeyModifiers.Shift);
        Assert.Equal(0x1 | 0x10 | 0x4 | 0x8, WebView2View.VirtualKeys(input));
    }

    [Theory]
    [InlineData("https://app.example/page.html?x=1", "https://app.example/")]
    [InlineData("http://localhost:8080/a/b", "http://localhost:8080/")]
    [InlineData("about:blank", null)]
    [InlineData("data:text/html,hi", null)]
    [InlineData("file:///C:/x.html", null)]
    [InlineData(null, null)]
    public void AMessagesOrigin_IsSchemeHostAndPort_OrOpaque(string? source, string? origin) =>
        Assert.Equal(origin is null ? null : new Uri(origin), WebView2View.OriginOf(source));

    [Fact]
    public void Cursors_MapFromWin32Ids()
    {
        Assert.Equal(CursorType.Pointer, WebView2View.CursorFor(32649));   // IDC_HAND
        Assert.Equal(CursorType.Text, WebView2View.CursorFor(32513));      // IDC_IBEAM
        Assert.Equal(CursorType.Default, WebView2View.CursorFor(32512));   // IDC_ARROW
        Assert.Equal(CursorType.Default, WebView2View.CursorFor(1));
    }

    [Fact]
    public void ACancelledNavigation_ReadsAsTheFakesDoes()
    {
        Assert.Equal("Cancelled", WebView2View.WebErrorText(14));   // OPERATION_CANCELED
        Assert.Equal("HostNameNotResolved", WebView2View.WebErrorText(13));
        Assert.Equal("Error 99", WebView2View.WebErrorText(99));
    }

    [Fact]
    public void TheGeneratedSlots_MatchTheOnesTheSpikeCalled()
    {
        // Both were called by hand in spikes/WebView2Capture and worked; the second was first
        // guessed as 12, which is why slots are generated now.
        Assert.Equal(11, ICoreWebView2CompositionController2.get_AutomationProvider);
        Assert.Equal(11, ICoreWebView2Environment4.GetAutomationProviderForWindow);
        Assert.Equal(new Guid("0b6a3d24-49cb-4806-ba20-b5e0734a7b26"), ICoreWebView2CompositionController2.IID);
    }

    [Fact]
    public void Availability_NeverThrows_AndSaysWhyWhenMissing()
    {
        using var engine = new WebView2Engine();
        var availability = engine.Availability;
        if (!availability.IsAvailable) Assert.False(string.IsNullOrEmpty(availability.Reason));
        if (!OperatingSystem.IsWindows()) Assert.False(availability.IsAvailable);
    }

    [Theory]
    [InlineData(0x4C, 0x26, KeyCode.L)]          // L
    [InlineData(0x57, 0x2C, KeyCode.Z)]          // AZERTY's W sits where US has Z: named by position
    [InlineData(0x74, 0x3F, KeyCode.F5)]
    [InlineData(0x31, 0x02, KeyCode.Digit1)]
    [InlineData(0x26, 0x48, KeyCode.Up)]         // extended: by virtual key
    [InlineData(0xBD, 0x0C, KeyCode.Minus)]
    public void WindowsKeys_AreNamedByPosition(uint vk, uint scan, KeyCode expected) =>
        Assert.Equal(expected, Win32Keys.ToKeyCode(vk, scan));

    [Theory]
    [InlineData(0x4C, KeyModifiers.Ctrl, true)]    // Ctrl+L
    [InlineData(0x46, KeyModifiers.Alt, true)]     // Alt+F
    [InlineData(0x74, KeyModifiers.None, true)]    // F5
    [InlineData(0x41, KeyModifiers.None, false)]   // typing
    [InlineData(0x41, KeyModifiers.Shift, false)]
    [InlineData(0x26, KeyModifiers.None, false)]   // arrows move the page's caret
    [InlineData(0x1B, KeyModifiers.None, false)]   // Escape closes the page's own menus
    [InlineData(0x11, KeyModifiers.Ctrl, false)]   // Ctrl itself
    public void OnlyShortcuts_AreOfferedToTheApp(uint vk, KeyModifiers modifiers, bool offered) =>
        Assert.Equal(offered, Win32Keys.IsShortcut(vk, modifiers));
}
