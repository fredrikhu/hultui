using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using HultUI.Samples.WebView;
using HultUI.WebView;
using HultUI.WebView.Testing;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace HultUI.Samples.WebView.Tests;

/// <summary>
/// The browser window over the fake engine: the home page loads once the page is placed,
/// the address bar navigates and history follows, the page's messages reach the status
/// line, and the toolbar's dropdown opens over the page without the page seeing its clicks.
/// </summary>
public class WebViewSampleTests
{
    private sealed class Window : IDisposable
    {
        public required HeadlessSession Session { get; init; }
        public required FakeWebEngine Engine { get; init; }
        public required WebViewSampleApp App { get; init; }
        public FakeWebEngineView View => Engine.Views[0];
        public HeadlessWindow Headless => Session.Window;

        public void Dispose()
        {
            App.Dispose();
            Session.Dispose();
        }
    }

    private static Window Open()
    {
        var session = HeadlessSession.Create(1100, 760);
        var engine = new FakeWebEngine();
        var app = new WebViewSampleApp(engine);
        session.Application.SetRoot(app.Mount());
        session.PumpFrames(3);
        return new Window { Session = session, Engine = engine, App = app };
    }

    [Fact]
    public void TheHomePage_LoadsOnceThePageIsPlaced()
    {
        using var w = Open();

        Assert.Equal(WebViewState.Ready, w.App.Web.State);
        Assert.Contains("NavigateToString", w.View.Calls);
        Assert.Equal("Home", w.App.Web.Title);
        Assert.NotNull(w.Headless.FindByText("Home"));
    }

    [Fact]
    public void TypingAnAddress_Navigates_AndBackReturnsHome()
    {
        using var w = Open();
        var address = w.Headless.FindByRole(AccessibilityRole.TextInput, "Address")!;
        Assert.False(w.Headless.FindByRole(AccessibilityRole.Button, "Back")!.IsEnabled);

        address.Click();
        w.Headless.TypeText("example.org");
        w.Headless.PressKey(KeyCode.Enter);
        w.Session.PumpFrames(2);

        Assert.Equal(new Uri("https://example.org/"), w.App.Web.Source);
        Assert.Equal("https://example.org/", w.Headless.FindByRole(AccessibilityRole.TextInput, "Address")!.Value);

        var back = w.Headless.FindByRole(AccessibilityRole.Button, "Back")!;
        Assert.True(back.IsEnabled);
        back.Click();
        w.Session.PumpFrames(2);
        Assert.Equal("Home", w.App.Web.Title);
    }

    [Fact]
    public void AMessageFromThePage_ShowsInTheStatusLine()
    {
        using var w = Open();

        w.View.RaiseMessage("{\"clicked\":1}", origin: null);
        w.Session.PumpFrames(2);

        Assert.NotNull(w.Headless.FindByText("Page said: {\"clicked\":1}"));
    }

    [Fact]
    public void TheToolbarDropdown_OpensOverThePage_AndThePageNeverSeesItsClick()
    {
        using var w = Open();
        var dropdown = w.Headless.FindByRole(AccessibilityRole.ComboBox)!;
        dropdown.Click();
        w.Session.PumpFrame();

        // The menu's rows are virtualized, which FindByText cannot resolve: walk the tree.
        var item = Find(w.Headless.AccessibilityTree, n => n.Role == AccessibilityRole.ListItem && n.Name == "…and this")!;
        var page = w.App.Page.ScreenContentBox;
        Assert.True(item.BoundingRect.Y > page.Y, "the menu should reach down over the page");

        w.View.Pointers.Clear();
        w.Headless.Click(item.BoundingRect.X + item.BoundingRect.Width / 2, item.BoundingRect.Y + item.BoundingRect.Height / 2);
        w.Session.PumpFrame();

        Assert.Equal("…and this", w.Headless.FindByRole(AccessibilityRole.ComboBox)!.Name);

        // Not the press, which the menu took, nor the release, which lands on the page once
        // the menu has closed.
        Assert.DoesNotContain(w.View.Pointers, p => p.Action is WebPointerAction.Down or WebPointerAction.Up);
    }

    private static AccessibilityNode? Find(AccessibilityNode node, Func<AccessibilityNode, bool> match)
    {
        if (match(node)) return node;
        foreach (var child in node.Children)
            if (Find(child, match) is { } found) return found;
        return null;
    }
}
