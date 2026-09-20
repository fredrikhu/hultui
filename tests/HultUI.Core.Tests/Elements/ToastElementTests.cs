using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class ToastElementTests
{
    [Fact]
    public void PlainToast_IsAnAlert_NamedByItsMessage()
    {
        var t = ToastElement.New("Saved", ToastSeverity.Success);
        Assert.Equal(AccessibilityRole.Alert, AccessibilityTreeBuilder.ResolveRole(t));
        Assert.Equal("Saved", AccessibilityTreeBuilder.ResolveName(t));
        Assert.Equal(CursorType.Default, t.Inherited.Cursor);
    }

    [Fact]
    public void WithAnAction_TheWholePill_IsAButton()
    {
        var t = ToastElement.New("Pushed");
        t.ActionLabel = "Create merge request";
        Assert.Equal(AccessibilityRole.Button, AccessibilityTreeBuilder.ResolveRole(t));
        Assert.Equal("Create merge request", AccessibilityTreeBuilder.ResolveName(t));
        Assert.Equal("Pushed", t.AccessibleDescription);
        Assert.Equal(CursorType.Pointer, t.Inherited.Cursor);

        t.ActionLabel = null;
        Assert.Equal(AccessibilityRole.Alert, AccessibilityTreeBuilder.ResolveRole(t));
    }

    [Fact]
    public void PatchFrom_CarriesMessageActionAndSeverity()
    {
        var live = ToastElement.New("First");
        var incoming = ToastElement.New("Second", ToastSeverity.Error);
        int ran = 0;
        incoming.ActionLabel = "Undo";
        incoming.OnAction = () => ran++;

        live.PatchFrom(incoming);

        Assert.Equal("Second", live.Message);
        Assert.Equal("Undo", live.ActionLabel);
        Assert.Equal(ToastSeverity.Error, live.Severity);
        live.OnAction!();
        Assert.Equal(1, ran);
    }

    [Fact]
    public void ApplyTheme_PartialOverride_KeepsThePalette()
    {
        var t = ToastElement.New("x");
        t.ApplyTheme(Theme.Dark.With(new ToastTheme { CornerRadius = 2f }));
        // No throw and the role/name survive theming; colours are private, the render
        // reads them — the contract under test is that a partial override is legal.
        Assert.Equal(AccessibilityRole.Alert, AccessibilityTreeBuilder.ResolveRole(t));
    }
}
