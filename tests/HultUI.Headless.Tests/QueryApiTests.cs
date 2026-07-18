using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Headless.Tests;

using Text = HultUI.Core.Builders.Text;

public class QueryApiTests
{
    [Fact]
    public void GetByText_FindsLabelAndThrowsWithTreeDump()
    {
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Flex.Column().Padding(10).Children(
            Text.New("Hello, queries!")));
        session.PumpFrame();

        var found = session.Window.GetByText("Hello, queries!");
        Assert.Equal(AccessibilityRole.Label, found.Role);

        var ex = Assert.Throws<InvalidOperationException>(
            () => session.Window.GetByText("not there"));
        // The failure message shows the actual tree.
        Assert.Contains("Hello, queries!", ex.Message);
    }

    [Fact]
    public void ButtonInvoke_ParityWithUiaScenario()
    {
        // Mirrors the FlaUI AccessibilitySampleTests button scenario: find the
        // button by role+name, invoke it, observe the counter label update.
        using var session = HeadlessSession.Create();
        int count = 0;
        var label = Text.New("Count: 0");
        var button = Button.New("Increment");
        button.OnClick(_ => { count++; label.Content = $"Count: {count}"; });
        session.Window.SetRoot(Flex.Column(gap: 8).Padding(10).Children(button, label));
        session.PumpFrame();

        var buttonHandle = session.Window.FindByRole(AccessibilityRole.Button, "Increment");
        Assert.NotNull(buttonHandle);

        buttonHandle.Click();
        session.PumpFrame();

        Assert.NotNull(session.Window.FindByText("Count: 1"));
        Assert.Null(session.Window.FindByText("Count: 0"));
    }

    [Fact]
    public void CheckboxToggle_ReflectsInAccessibilityState()
    {
        using var session = HeadlessSession.Create();
        var checkbox = Checkbox.New(false, "Enable feature");
        session.Window.SetRoot(Flex.Column().Padding(10).Children(checkbox));
        session.PumpFrame();

        var handle = session.Window.FindByRole(AccessibilityRole.Checkbox);
        Assert.NotNull(handle);
        Assert.False(handle.IsToggled);

        handle.Click();
        session.PumpFrame();

        // Re-query: state lives in the fresh snapshot.
        Assert.True(session.Window.FindByRole(AccessibilityRole.Checkbox)!.IsToggled);
        Assert.True(checkbox.IsChecked);
    }

    [Fact]
    public void FindByAutomationId_LocatesElement()
    {
        using var session = HeadlessSession.Create();
        var input = TextInput.New("hello");
        input.AutomationId = "search-box";
        session.Window.SetRoot(Flex.Column().Padding(10).Children(input));
        session.PumpFrame();

        var handle = session.Window.FindByAutomationId("search-box");
        Assert.NotNull(handle);
        Assert.Equal(AccessibilityRole.TextInput, handle.Role);
        Assert.Equal("hello", handle.Value);
    }

    [Fact]
    public void FocusThroughHandle_RoutesKeyboard()
    {
        using var session = HeadlessSession.Create();
        var input = TextInput.New();
        input.AutomationId = "target";
        session.Window.SetRoot(Flex.Column().Padding(10).Children(input));
        session.PumpFrame();

        var handle = session.Window.FindByAutomationId("target")!;
        Assert.False(handle.IsFocused);

        handle.Focus();
        Assert.True(session.Window.FindByAutomationId("target")!.IsFocused);

        session.Window.TypeText("typed");
        Assert.Equal("typed", input.Text);
    }

    [Fact]
    public void InstalledBridge_ReceivesInitialTreeAndPropertyDiffs()
    {
        using var session = HeadlessSession.Create();
        var label = Text.New("before");
        session.Window.SetRoot(Flex.Column().Padding(10).Children(label));

        var bridge = session.Window.InstallAccessibilityBridge();
        session.PumpFrame();

        // First frame with an active bridge: the full tree arrives as one diff.
        Assert.NotNull(bridge.CurrentTree);
        Assert.NotEmpty(bridge.Diffs);

        bridge.ClearDiffs();
        label.Content = "after";
        session.PumpFrame();

        // The text change reached the bridge as a diff — the exact pipeline
        // real assistive tech (and later the Linux AT-SPI bridge) consumes.
        Assert.NotEmpty(bridge.Diffs);
    }

    [Fact]
    public void AsyncContent_FoundAfterPumpUntil()
    {
        using var session = HeadlessSession.Create();
        var label = Text.New("loading…");
        session.Window.SetRoot(Flex.Column().Padding(10).Children(label));
        session.PumpFrame();

        // Background work completes off-thread; the continuation posts back
        // to the UI queue, which only the pump drains.
        var task = Task.Run(async () =>
        {
            await Task.Delay(20);
            Application.InvokeOnMainThread(() => label.Content = "loaded!");
        });

        session.PumpUntil(() => session.Window.FindByText("loaded!") is not null);
        Assert.Null(session.Window.FindByText("loading…"));
    }
}
