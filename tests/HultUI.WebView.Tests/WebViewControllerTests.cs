using HultUI.Core.Builders;
using HultUI.Headless;
using HultUI.WebView.Testing;
using Xunit;

namespace HultUI.WebView.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// <see cref="WebViewController"/> against the fake engine: what it holds until the
/// engine is ready, how navigation state follows the engine, what the application is
/// asked, and what it is never shown.
/// </summary>
public class WebViewControllerTests
{
    private static readonly Uri A = new("https://a.test/");
    private static readonly Uri B = new("https://b.test/");

    /// <summary>A controller whose view is ready at once, without an element.</summary>
    private static (WebViewController Web, FakeWebEngineView View) Ready(FakeWebEngine? engine = null, WebViewOptions? options = null)
    {
        engine ??= new FakeWebEngine();
        var web = new WebViewController(engine, options);
        var view = engine.Views[^1];
        view.RaiseReady();
        return (web, view);
    }

    [Fact]
    public void Commands_BeforeTheEngineIsReady_AreHeld_AndSentInOrder()
    {
        using var session = HeadlessSession.Create();
        var engine = new FakeWebEngine();
        using var web = new WebViewController(engine);
        var view = Assert.Single(engine.Views);

        web.Navigate(A);
        web.PostMessage("{\"hello\":1}");
        Assert.Equal(WebViewState.Initializing, web.State);
        Assert.Equal(["Attach"], view.Calls);

        view.RaiseReady();

        Assert.Equal(WebViewState.Ready, web.State);
        Assert.Equal("Navigate https://a.test/", view.Calls[1]);
        Assert.Contains("PostMessage {\"hello\":1}", view.Calls);
        Assert.True(view.Calls.IndexOf("PostMessage {\"hello\":1}") > 1);
        Assert.Equal(A, web.Source);
    }

    [Fact]
    public void DevTools_OpenOnlyWhereTheOptionsAllowThem_AndWaitForTheEngine()
    {
        using var session = HeadlessSession.Create();
        var engine = new FakeWebEngine { Capabilities = WebEngineCapabilities.DevTools };

        // Off by default: a page from the internet must not get developer tools by accident.
        var (closed, closedView) = Ready(engine);
        Assert.Throws<InvalidOperationException>(closed.OpenDevTools);
        Assert.DoesNotContain("OpenDevTools", closedView.Calls);
        closed.Dispose();

        // On, before the engine is ready: held, then sent, as every command is.
        using var web = new WebViewController(engine, new WebViewOptions { DevTools = true });
        var view = engine.Views[^1];
        web.OpenDevTools();
        Assert.DoesNotContain("OpenDevTools", view.Calls);
        view.RaiseReady();
        Assert.Contains("OpenDevTools", view.Calls);
    }

    [Fact]
    public void DevTools_OnAnEngineThatHasNone_SayItIsNotSupported()
    {
        using var session = HeadlessSession.Create();
        var (web, view) = Ready(new FakeWebEngine(), new WebViewOptions { DevTools = true });
        using (web)
        {
            Assert.Throws<NotSupportedException>(web.OpenDevTools);
            Assert.DoesNotContain("OpenDevTools", view.Calls);
        }
    }

    [Fact]
    public async Task AScript_AwaitedBeforeReady_CompletesOnceTheEngineIs()
    {
        using var session = HeadlessSession.Create();
        var engine = new FakeWebEngine();
        using var web = new WebViewController(engine);
        var view = engine.Views[0];
        view.Script = s => s == "1+1" ? "2" : "null";

        var result = web.ExecuteScriptAsync("1+1");
        Assert.False(result.IsCompleted);

        view.RaiseReady();
        Assert.Equal("2", await result);
    }

    [Fact]
    public async Task AnEngineThatFails_FaultsHeldScripts_AndDropsHeldCommands()
    {
        using var session = HeadlessSession.Create();
        var engine = new FakeWebEngine();
        using var web = new WebViewController(engine);
        var view = engine.Views[0];
        web.Navigate(A);
        var script = web.ExecuteScriptAsync("1");

        view.RaiseFailed(WebFailure.InitializationFailed, "no GPU process");

        Assert.Equal(WebViewState.Failed, web.State);
        Assert.Equal("no GPU process", web.Error);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => script);
        Assert.Equal("no GPU process", ex.Message);

        // Nothing will ever run them, including what is asked from now on.
        view.RaiseReady();
        web.Navigate(B);
        Assert.DoesNotContain(view.Calls, c => c.StartsWith("Navigate", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnavailableEngine_FailsAtOnce_WithItsReason_AndCreatesNoView()
    {
        using var session = HeadlessSession.Create();
        var engine = new FakeWebEngine
        {
            Availability = WebEngineAvailability.Missing("The runtime is not installed.", new Uri("https://example.test/get")),
        };

        using var web = new WebViewController(engine);

        Assert.Equal(WebViewState.Failed, web.State);
        Assert.Equal("The runtime is not installed.", web.Error);
        Assert.Empty(engine.Views);
        web.Navigate(A);   // harmless
    }

    [Fact]
    public void Navigating_FollowsTheEngine_ForSourceTitleAndHistory()
    {
        using var session = HeadlessSession.Create();
        var engine = new FakeWebEngine();
        engine.Pages[A] = new FakePage("Page A", HultUI.Core.Primitives.Color.Red);
        var (web, _) = Ready(engine);
        using var _web = web;

        web.Navigate(A);
        web.Navigate(B);
        Assert.Equal(B, web.Source);
        Assert.Equal("b.test", web.Title);
        Assert.True(web.CanGoBack);
        Assert.False(web.CanGoForward);

        web.GoBack();
        Assert.Equal(A, web.Source);
        Assert.Equal("Page A", web.Title);
        Assert.False(web.CanGoBack);
        Assert.True(web.CanGoForward);
    }

    [Fact]
    public void ANavigationTheAppCancels_NeverHappens_AndReportsWhy()
    {
        using var session = HeadlessSession.Create();
        var (web, view) = Ready();
        using var _web = web;
        web.Navigate(A);

        var completed = new List<WebNavigationResult>();
        web.NavigationStarting += request => { if (request.Uri.Host == "b.test") request.Cancel(); };
        web.NavigationCompleted += completed.Add;
        web.Navigate(B);

        Assert.Equal(A, web.Source);
        Assert.False(web.IsLoading);
        Assert.Contains("Cancelled https://b.test/", view.Calls);
        var result = Assert.Single(completed);
        Assert.False(result.IsSuccess);
        Assert.Equal("Cancelled", result.Error);
    }

    [Fact]
    public void AHandlerThatThrows_StillAnswersTheEngine()
    {
        using var session = HeadlessSession.Create();
        var (web, view) = Ready();
        using var _web = web;
        web.NavigationStarting += _ => throw new InvalidOperationException("app bug");

        Assert.Throws<InvalidOperationException>(() => web.Navigate(A));

        // Answered (allowed) despite the throw: an engine waiting on it is not left hanging.
        Assert.Equal(A, web.Source);
        Assert.DoesNotContain("Cancelled https://a.test/", view.Calls);
    }

    [Fact]
    public void IsLoading_IsTrueFromAnAllowedStart_UntilTheNavigationEnds()
    {
        using var session = HeadlessSession.Create();
        var engine = new FakeWebEngine { AutoComplete = false };
        var (web, view) = Ready(engine);
        using var _web = web;

        web.Navigate(A);
        Assert.True(web.IsLoading);
        Assert.Null(web.Source);

        view.CompleteNavigation();
        Assert.False(web.IsLoading);
        Assert.Equal(A, web.Source);

        web.Navigate(B);
        web.Stop();
        Assert.False(web.IsLoading);
        Assert.Equal(A, web.Source);
    }

    [Fact]
    public void Messages_ReachTheApp_OnlyFromOriginsTheOptionsAllow()
    {
        using var session = HeadlessSession.Create();
        var (web, view) = Ready();
        using var _web = web;
        var received = new List<WebMessage>();
        web.MessageReceived += received.Add;

        view.RaiseMessage("1", new Uri("https://evil.test"));
        view.RaiseMessage("2", new Uri(WebViewOptions.AppOrigin + "/page.html"));
        view.RaiseMessage("3", origin: null);                         // a string document
        view.RaiseMessage("4", new Uri("http://app.local"));          // wrong scheme

        Assert.Equal(["2", "3"], received.Select(m => m.Json));
    }

    [Fact]
    public void AWildcardOrigin_AcceptsMessagesFromAnyPage()
    {
        using var session = HeadlessSession.Create();
        var (web, view) = Ready(options: new WebViewOptions { MessageOrigins = ["*"] });
        using var _web = web;
        var received = new List<WebMessage>();
        web.MessageReceived += received.Add;

        view.RaiseMessage("1", new Uri("https://anywhere.test:8443"));

        Assert.Equal(new Uri("https://anywhere.test:8443"), Assert.Single(received).Origin);
    }

    [Fact]
    public void ANewWindow_IsTheAppsToDecide()
    {
        using var session = HeadlessSession.Create();
        var (web, view) = Ready();
        using var _web = web;
        WebNewWindowRequest? asked = null;
        web.NewWindowRequested += r => asked = r;

        view.RaiseNewWindow(B);

        Assert.Equal(new WebNewWindowRequest(B, true), asked);
        Assert.Null(web.Source);   // the page itself did not go anywhere
    }

    [Fact]
    public void ACrashedPage_IsReportedAndRecoveredByReload()
    {
        using var session = HeadlessSession.Create();
        var (web, view) = Ready();
        using var _web = web;
        web.Navigate(A);

        view.Crash();
        Assert.Equal(WebViewState.Crashed, web.State);
        Assert.NotNull(web.Error);

        web.Reload();
        Assert.Equal(WebViewState.Ready, web.State);
        Assert.Null(web.Error);
    }

    [Fact]
    public void EventsFromAnotherThread_AreAppliedOnTheUIThread_AtTheNextFrame()
    {
        using var session = HeadlessSession.Create();
        var (web, view) = Ready();
        using var _web = web;

        Task.Run(() => view.RaiseTitle("from a worker")).Wait();
        Assert.Equal("", web.Title);

        session.PumpFrame();
        Assert.Equal("from a worker", web.Title);
    }

    [Fact]
    public async Task Disposing_FaultsHeldScripts_AndDisposesTheView()
    {
        using var session = HeadlessSession.Create();
        var engine = new FakeWebEngine();
        var web = new WebViewController(engine);
        var script = web.ExecuteScriptAsync("1");

        web.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => script);
        Assert.True(engine.Views[0].IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => web.Navigate(A));
    }

    [Fact]
    public void AReactiveReader_OfTheTitle_RebuildsWhenItChanges()
    {
        using var session = HeadlessSession.Create();
        var (web, view) = Ready();
        using var _web = web;
        var title = Text.New("");
        session.Application.SetRoot(Box.New().Children(title));
        using var effect = new HultUI.Core.Reactivity.Effect(() => title.Content = web.Title);
        session.PumpFrame();

        view.RaiseTitle("Reactive");
        session.PumpFrame();

        Assert.Equal("Reactive", title.Content);
    }
}
