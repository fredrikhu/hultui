using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Meridian.Platform;
using Meridian.Tests.Fixtures;
using Xunit;

namespace Meridian.Tests;

/// <summary>P5's GUI: assertion rows with live previews, extractions feeding
/// the variable store on send, the chain tab, and the tree badges — over the
/// real loopback server.</summary>
public class ChainingViewTests : IClassFixture<TestHttpServer>, IDisposable
{
    private readonly TestHttpServer _server;
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-chain-").FullName;

    public ChainingViewTests(TestHttpServer server)
    {
        _server = server;
        // login extracts ok=true into {{flag}}; consumer uses it.
        Write("login.req", $"""
            GET {_server.Origin}/ok
            name Login
            assert status equals 200
            assert body $.ok equals true
            extract body $.ok -> flag
            """.ReplaceLineEndings("\n") + "\n");
        Write("consumer.req", $"POST {_server.Origin}/echo\nname Consumer\nbody text\nflag={{{{flag}}}}\nend\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class NoPicker : IFolderPicker
    {
        public bool IsAvailable => true;
        public Task<string?> PickFolderAsync(string? d = null, CancellationToken c = default) => Task.FromResult<string?>(null);
    }

    private void Hosted(Action<HeadlessSession, MeridianApp> body)
    {
        using var session = HeadlessSession.Create(1440, 900);
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window, new NoPicker());
            session.Application.SetRoot(app.Mount());
            app.OpenWorkspace(_root);
            session.PumpFrame();
            body(session, app);
        }
        finally
        {
            app?.Dispose();
        }
    }

    private static bool Contains(HeadlessSession session, string text) =>
        Walk(session.Window.AccessibilityTree).Any(n =>
            n.Name?.Contains(text, StringComparison.Ordinal) == true
            || n.Value?.Contains(text, StringComparison.Ordinal) == true);

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    private static void Click(HeadlessSession session, string automationId)
    {
        var el = session.Window.FindByAutomationId(automationId);
        Assert.True(el != null, $"#{automationId} not found");
        el!.Click();
        session.PumpFrames(2);
    }

    private void SendAndWait(HeadlessSession session, MeridianApp app)
    {
        Click(session, "send");
        session.PumpUntil(() => app.ActiveTab!.Result != null && app.ActiveTab.Sending == null, TimeSpan.FromSeconds(10));
        session.PumpFrames(2);
    }

    // ── Assertions tab ───────────────────────────────────────────────────

    [Fact]
    public void BeforeASendThePreviewSaysSo()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-login");
            Click(session, "subtab-assertions");

            Assert.True(Contains(session, "— send to preview"));
        });
    }

    [Fact]
    public void AfterASendRowsShowLiveVerdictsAndTheTabCounts()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-login");
            SendAndWait(session, app);
            Click(session, "subtab-assertions");

            // Both assertions pass against {"ok":true}: status 200, $.ok true.
            Assert.True(Contains(session, "✓ 200"), "status verdict missing");
            Assert.True(Contains(session, "✓ true"), "body verdict missing");
            Assert.True(Contains(session, "Assertions 2✓"), "tab label should carry the verdict");
        });
    }

    [Fact]
    public void AFailingAssertionShowsExpectedVsActual()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-login");
            SendAndWait(session, app);
            app.ActiveTab!.Doc.SetAssert(0, "status equals 999");
            session.PumpFrames(2);
            Click(session, "subtab-assertions");

            Assert.True(Contains(session, "✗ got 200"), "the failing row must show the actual");
            Assert.True(Contains(session, "Assertions 1/2✗"), "tab label should count the failure");
        });
    }

    [Fact]
    public void TheEmptyStateOffersStartersThatWriteRealLines()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-consumer");
            Click(session, "subtab-assertions");
            Assert.True(Contains(session, "counts this request as ✓ if the response is 2xx"));

            Click(session, "starter-status");

            Assert.Equal("status equals 200", app.ActiveTab!.Doc.Asserts.Single().Args);
            Assert.True(app.ActiveTab.Dirty);
        });
    }

    [Fact]
    public void EditingAfterASendRaisesTheStaleBanner()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-login");
            SendAndWait(session, app);
            Click(session, "subtab-assertions");
            Assert.Null(session.Window.FindByAutomationId("stale-banner"));

            app.ActiveTab!.Doc.SetRequestLine("GET", $"{_server.Origin}/teapot");
            // The direct doc mutation is a test seam with no MarkDirty of its
            // own; re-clicking the sub-tab is the rebuild.
            Click(session, "subtab-assertions");

            Assert.NotNull(session.Window.FindByAutomationId("stale-banner"));
        });
    }

    // ── Extractions ──────────────────────────────────────────────────────

    [Fact]
    public void ASendAppliesExtractionsAndTheChainConsumesThem()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-login");
            SendAndWait(session, app);

            Assert.Equal("true", app.Extracted["flag"]);

            // The consumer resolves {{flag}} from the store and the echo
            // server proves the value went over the wire.
            Click(session, "req-consumer");
            SendAndWait(session, app);
            Assert.Contains("flag=true", app.ActiveTab!.Result!.Response!.Body.Text);
        });
    }

    [Fact]
    public void ADeadExtractionSaysWhenItLastMatched()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-login");
            SendAndWait(session, app);   // matches: run 1

            // The API "changes": the path now selects nothing.
            app.ActiveTab!.Doc.SetExtract(0, "body $.gone -> flag");
            SendAndWait(session, app);   // run 2: no match
            Click(session, "subtab-assertions");

            Assert.NotNull(session.Window.FindByAutomationId("extract-dead"));
            Assert.True(Contains(session, "last matched 1 run ago"), "the row must remember when it worked");
        });
    }

    // ── Chain tab + tree badges ──────────────────────────────────────────

    [Fact]
    public void TheChainTabShowsUsesProducesAndRunOrder()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-consumer");
            Click(session, "subtab-chain");

            Assert.True(Contains(session, "{{flag}}"), "the used variable is missing");
            Assert.True(Contains(session, "← Login"), "the producer is missing");
            Assert.True(Contains(session, "not yet produced this session"));

            var order = session.Window.FindByAutomationId("chain-order");
            Assert.True(order != null && Contains(session, "Login → this"), "run order missing");
        });
    }

    [Fact]
    public void TheProducerSideListsItsConsumers()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-login");
            Click(session, "subtab-chain");

            Assert.True(Contains(session, "produces"));
            Assert.True(Contains(session, "→ used by Consumer"));
        });
    }

    [Fact]
    public void TheTreeCarriesTheChainBadges()
    {
        Hosted((session, app) =>
        {
            Assert.True(Contains(session, "→flag"), "the produces badge is missing");
            Assert.True(Contains(session, "⛓1"), "the uses badge is missing");
        });
    }
}
