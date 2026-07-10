using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// Drives the Accessibility sample through FlaUI (a standard UI Automation
/// client) to prove HultUI's providers work with off-the-shelf automation
/// tooling — the same interfaces a screen reader uses. Elements are located by
/// the AutomationIds the sample sets.
/// </summary>
[Collection("ui")] // never run UI automation tests in parallel
public sealed class AccessibilitySampleTests : IDisposable
{
    private readonly Application _app;
    private readonly UIA3Automation _automation;
    private readonly Window _window;

    public AccessibilitySampleTests()
    {
        _app = Application.Launch(SamplePath.Accessibility());
        _automation = new UIA3Automation();
        _window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(15))
            ?? throw new InvalidOperationException("sample window did not appear");
    }

    private AutomationElement Find(string automationId) =>
        _window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
        ?? throw new InvalidOperationException($"no element with AutomationId '{automationId}'");

    [Fact]
    public void Button_HasExpectedControlTypeAndName()
    {
        var button = Find("click-me");
        Assert.Equal(ControlType.Button, button.ControlType);
        Assert.Equal("Click Me", button.Name);
    }

    [Fact]
    public void InvokingButton_UpdatesCountText()
    {
        Assert.Equal("Clicked 0 time(s)", Find("click-count").Name);

        Find("click-me").AsButton().Invoke();

        Retry.WhileFalse(
            () => Find("click-count").Name == "Clicked 1 time(s)",
            timeout: TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "count text did not update after Invoke");
    }

    [Fact]
    public void TogglingCheckbox_FlipsToggleState()
    {
        var checkbox = Find("enable-thing").AsCheckBox();
        Assert.Equal(ToggleState.Off, checkbox.Patterns.Toggle.Pattern.ToggleState.Value);

        checkbox.Patterns.Toggle.Pattern.Toggle();

        Retry.WhileFalse(
            () => Find("enable-thing").AsCheckBox().Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On,
            timeout: TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "checkbox did not toggle On");
    }

    [Fact]
    public void SettingTextInputValue_TakesEffect()
    {
        var input = Find("name-input");
        input.Patterns.Value.Pattern.SetValue("Fredrik");

        Retry.WhileFalse(
            () => Find("name-input").Patterns.Value.Pattern.Value.Value == "Fredrik",
            timeout: TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "text input value did not update");
    }

    public void Dispose()
    {
        _automation.Dispose();
        try { _app.Close(); } catch { /* best effort */ }
        _app.Dispose();
    }
}

[CollectionDefinition("ui", DisableParallelization = true)]
public sealed class UiCollection { }
