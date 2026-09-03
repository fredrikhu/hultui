using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// Drives the WinForms host sample as a real UI Automation client — the interface a
/// screen reader uses — to prove that a HultUI tree hosted inside someone else's window
/// is still readable, operable and typeable.
///
/// <para>None of this is guarded anywhere else. A hosted tree loses its accessibility
/// silently: the bridge falls back to the Null one and everything still renders, so
/// only a test that reads the tree from outside the process notices. The same goes for
/// the keyboard path, which no unit test can reach because it starts at a Win32
/// message.</para>
/// </summary>
[Collection("ui")] // never run UI automation tests in parallel
public sealed class WinFormsHostSampleTests : IDisposable
{
    private readonly Application _app;
    private readonly UIA3Automation _automation;
    private readonly Window _window;

    public WinFormsHostSampleTests()
    {
        _app = Application.Launch(SamplePath.WinFormsHost());
        _automation = new UIA3Automation();
        _window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(20))
            ?? throw new InvalidOperationException("the sample's window did not appear");

        // The first frames have to land before the tree is worth reading.
        Retry.WhileNull(
            () => _window.FindFirstDescendant(cf => cf.ByAutomationId("click-me")),
            timeout: TimeSpan.FromSeconds(15),
            throwOnTimeout: true,
            timeoutMessage: "the hosted HultUI tree never appeared to UI Automation");

        // Tab traversal runs through WinForms' dialog-key handling, which starts at the
        // control holding Win32 focus — and a control in a background window holds none.
        // Typing reaches the tree either way, so this only matters for the Tab test, but
        // it is cheap and makes the whole fixture behave like a real session.
        HostedInput.EnsureForeground(_window.Properties.NativeWindowHandle.Value);
    }

    private AutomationElement Find(string automationId) =>
        _window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
        ?? throw new InvalidOperationException($"no element with AutomationId '{automationId}'");

    // ──── accessibility ────

    [UiFact]
    public void HostedTree_IsExposedToAutomation()
    {
        var button = Find("click-me");
        Assert.Equal(ControlType.Button, button.ControlType);
        Assert.Equal("Click me", button.Name);
        Assert.Equal("clicked 0 times", Find("click-count").Name);
    }

    [UiFact]
    public void HostedTree_RespondsToInvoke()
    {
        Find("click-me").AsButton().Invoke();

        Retry.WhileFalse(
            () => Find("click-count").Name == "clicked 1 time",
            timeout: TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "Invoke did not reach the click handler");
    }

    [UiFact]
    public void HostedElements_ReportScreenBounds_InsideTheHostingControl()
    {
        // The bridge converts with ClientToScreen against the handle it was given, so a
        // wrong handle (or a missed DPI scale) puts the whole tree somewhere a screen
        // reader would draw its highlight over empty desktop.
        var button = Find("click-me");
        var host = HostedInput.HostHandle(button);
        Assert.NotEqual(IntPtr.Zero, host);

        var bounds = button.BoundingRectangle;
        Assert.True(bounds.Width > 0 && bounds.Height > 0, $"degenerate bounds {bounds}");
        Assert.True(_window.BoundingRectangle.Contains(bounds),
            $"button at {bounds} is outside the window at {_window.BoundingRectangle}");
    }

    [UiFact]
    public void TheHostsOwnControls_AreStillThere()
    {
        // The point of the control is coexistence: if HultUI's provider had replaced the
        // form's subtree rather than joining it, this would be gone.
        Assert.NotNull(_window.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataGrid)));
        Assert.NotNull(_window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Tab)));
    }

    // ──── keyboard ────

    [UiFact]
    public void Typing_ReachesAFocusedTextField()
    {
        var field = Find("field-one");
        var host = HostedInput.HostHandle(field);
        HostedInput.Click(field);
        WaitFor(() => true, "click");

        HostedInput.Type(host, "Hello");

        Retry.WhileFalse(
            () => HostedInput.Read(Find("field-one")) == "Hello",
            timeout: TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: $"typed text never arrived (field reads '{HostedInput.Read(Find("field-one"))}')");
    }

    [UiFact]
    public void EditingKeys_TakeTheKeyPath_NotTheTextPath()
    {
        // Backspace arrives as WM_CHAR too, as a control character. If the translator
        // let those through as text, this would append a character instead of deleting.
        var field = Find("field-one");
        var host = HostedInput.HostHandle(field);
        HostedInput.Click(field);
        HostedInput.Type(host, "Hello");
        WaitFor(() => HostedInput.Read(Find("field-one")) == "Hello", "typing");

        HostedInput.Press(host, HostedInput.VK_BACK);

        Retry.WhileFalse(
            () => HostedInput.Read(Find("field-one")) == "Hell",
            timeout: TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "Backspace did not edit the field");
    }

    [UiFact]
    public void Tab_MovesThroughTheTree_ThenLeavesIt()
    {
        var field = Find("field-one");
        var host = HostedInput.HostHandle(field);
        HostedInput.Click(field);
        HostedInput.Type(host, "a");
        WaitFor(() => HostedInput.Read(Find("field-one")) == "a", "typing into the first field");

        // Through: the next character belongs to the second field.
        HostedInput.Press(host, HostedInput.VK_TAB);
        HostedInput.Type(host, "b");
        Retry.WhileFalse(() => HostedInput.Read(Find("field-two")) == "b",
            timeout: TimeSpan.FromSeconds(5), throwOnTimeout: false);
        // Reported together, because "b" landing in the first field (Tab did not move) and
        // landing nowhere (Tab left too early) are different bugs.
        Assert.Equal("one='a' two='b'",
            $"one='{HostedInput.Read(Find("field-one"))}' two='{HostedInput.Read(Find("field-two"))}'");

        // Out: from the last element, Tab leaves rather than wrapping to the first, so
        // nothing in the tree takes the next character.
        HostedInput.Press(host, HostedInput.VK_TAB);
        HostedInput.Type(host, "c");
        Thread.Sleep(600);

        Assert.Equal("a", HostedInput.Read(Find("field-one")));
        Assert.Equal("b", HostedInput.Read(Find("field-two")));
    }

    private static void WaitFor(Func<bool> condition, string what)
        => Retry.WhileFalse(condition, timeout: TimeSpan.FromSeconds(5),
            throwOnTimeout: true, timeoutMessage: $"timed out waiting for {what}");

    public void Dispose()
    {
        _automation.Dispose();
        _app.Close();
        _app.Dispose();
    }
}
