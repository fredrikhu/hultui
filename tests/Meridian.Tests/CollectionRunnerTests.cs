using Meridian.Core;
using Meridian.Tests.Fixtures;
using Xunit;

namespace Meridian.Tests;

/// <summary>The run engine against the loopback server: chain order, verdicts,
/// bail, dependent-skip, abort, and the reports.</summary>
public class CollectionRunnerTests : IClassFixture<TestHttpServer>
{
    private readonly TestHttpServer _server;

    public CollectionRunnerTests(TestHttpServer server) => _server = server;

    private static CollectionRunner.Request Req(string path, string name, string text) =>
        new(path, name, ReqDocument.Parse(text));

    private async Task<(RunSummary Summary, List<RunRequestResult> Stream)> Run(
        IReadOnlyList<CollectionRunner.Request> scope, bool bail = false,
        CancellationToken ct = default)
    {
        using var engine = new HttpEngine();
        var stream = new List<RunRequestResult>();
        var summary = await new CollectionRunner(engine).RunAsync(
            scope, new Dictionary<string, string>(), bail, stream.Add, ct);
        return (summary, stream);
    }

    // ── Order + chaining ─────────────────────────────────────────────────

    [Fact]
    public async Task DependenciesRunFirstAndExtractionsFlowDownstream()
    {
        // Scope listed consumer-first on purpose: chain order must reorder.
        var (summary, stream) = await Run([
            Req("consumer.req", "Consumer", $"POST {_server.Origin}/echo\nbody text\nflag={{{{flag}}}}\nend\n"),
            Req("login.req", "Login", $"GET {_server.Origin}/ok\nextract body $.ok -> flag\n"),
        ]);

        Assert.Equal(["Login", "Consumer"], stream.Select(r => r.DisplayName));
        Assert.All(summary.Results, r => Assert.Equal(RunVerdict.Pass, r.Verdict));
        Assert.Contains("flag=true", summary.Results[1].Send!.Response!.Body.Text);
        Assert.Equal(0, summary.ExitCode);
    }

    [Fact]
    public async Task NoAssertionsMeansTwoxxIsTheBar()
    {
        var (summary, _) = await Run([
            Req("ok.req", "Ok", $"GET {_server.Origin}/ok\n"),
            Req("teapot.req", "Teapot", $"GET {_server.Origin}/teapot\n"),
        ]);

        Assert.Equal(RunVerdict.Pass, summary.Results.Single(r => r.DisplayName == "Ok").Verdict);
        var teapot = summary.Results.Single(r => r.DisplayName == "Teapot");
        Assert.Equal(RunVerdict.Fail, teapot.Verdict);
        Assert.Equal("expected 2xx, got 418", teapot.Detail);
    }

    [Fact]
    public async Task AFailedAssertionIsRedWithExpectedAndActual()
    {
        var (summary, _) = await Run([
            Req("ok.req", "Ok", $"GET {_server.Origin}/ok\nassert body $.ok equals false\n"),
        ]);

        var result = summary.Results.Single();
        Assert.Equal(RunVerdict.Fail, result.Verdict);
        Assert.Contains("expected \"false\", got \"true\"", result.Detail);
        Assert.Equal(1, summary.ExitCode);
    }

    [Fact]
    public async Task StopOnFailureSkipsTheRemainder()
    {
        var (summary, _) = await Run([
            Req("a.req", "A", $"GET {_server.Origin}/teapot\n"),
            Req("b.req", "B", $"GET {_server.Origin}/ok\n"),
            Req("c.req", "C", $"GET {_server.Origin}/ok\n"),
        ], bail: true);

        Assert.Equal(RunVerdict.Fail, summary.Results[0].Verdict);
        Assert.All(summary.Results.Skip(1), r =>
        {
            Assert.Equal(RunVerdict.Skipped, r.Verdict);
            Assert.Contains("stopped on failure", r.Detail);
        });
    }

    [Fact]
    public async Task AnUnreachableHostIsInfraAndTheRunContinues()
    {
        int deadPort = FreePort();
        var (summary, _) = await Run([
            Req("dead.req", "Dead", $"GET http://127.0.0.1:{deadPort}/x\n"),
            Req("ok.req", "Ok", $"GET {_server.Origin}/ok\n"),
        ]);

        Assert.Equal(RunVerdict.Infra, summary.Results[0].Verdict);
        Assert.Contains("connection refused", summary.Results[0].Detail);
        Assert.Equal(RunVerdict.Pass, summary.Results[1].Verdict);
        Assert.Equal(1, summary.ExitCode);   // infra still fails the build
    }

    [Fact]
    public async Task ConsumersOfAFailedProducerSkipInsteadOfFailing()
    {
        int deadPort = FreePort();
        var (summary, _) = await Run([
            Req("login.req", "Login", $"GET http://127.0.0.1:{deadPort}/x\nextract body $.t -> token\n"),
            Req("use.req", "Use", $"GET {_server.Origin}/ok\nheader A: {{{{token}}}}\n"),
        ]);

        Assert.Equal(RunVerdict.Infra, summary.Results[0].Verdict);
        var use = summary.Results[1];
        Assert.Equal(RunVerdict.Skipped, use.Verdict);
        Assert.Contains("login did not pass", use.Detail);
        Assert.Null(use.Send);   // never sent
    }

    [Fact]
    public async Task AnUnresolvedVariableIsInfraNamingIt()
    {
        var (summary, _) = await Run([
            Req("x.req", "X", "GET {{nowhere}}/x\n"),
        ]);

        Assert.Equal(RunVerdict.Infra, summary.Results.Single().Verdict);
        Assert.Contains("{{nowhere}}", summary.Results.Single().Detail);
    }

    [Fact]
    public async Task AbortSkipsWhatNeverRan()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();   // aborted before anything runs
        var (summary, _) = await Run([
            Req("a.req", "A", $"GET {_server.Origin}/ok\n"),
            Req("b.req", "B", $"GET {_server.Origin}/ok\n"),
        ], ct: cts.Token);

        Assert.True(summary.Aborted);
        Assert.Equal(2, summary.Skipped);
        Assert.All(summary.Results, r => Assert.Contains("aborted", r.Detail));
    }

    [Fact]
    public async Task ACycleFailsEveryMemberWithoutSending()
    {
        var (summary, _) = await Run([
            Req("a.req", "A", $"GET {_server.Origin}/x/{{{{vb}}}}\nextract body $.x -> va\n"),
            Req("b.req", "B", $"GET {_server.Origin}/x/{{{{va}}}}\nextract body $.x -> vb\n"),
        ]);

        Assert.All(summary.Results, r =>
        {
            Assert.Equal(RunVerdict.Infra, r.Verdict);
            Assert.Contains("cycle", r.Detail);
            Assert.Null(r.Send);
        });
    }

    // ── Reports ──────────────────────────────────────────────────────────

    [Fact]
    public async Task TheTextReportReadsLikeTheMock()
    {
        var (summary, _) = await Run([
            Req("ok.req", "Ok", $"GET {_server.Origin}/ok\n"),
            Req("teapot.req", "Teapot", $"GET {_server.Origin}/teapot\nassert status equals 200\n"),
        ]);

        var lines = RunReports.TextLines(summary, "payments-api/", "staging");
        Assert.Equal("payments-api/ (staging) 2 requests", lines[0]);
        Assert.StartsWith("✓ 1  ✗ 1  ! 0  − 0  wall ", lines[1]);
        Assert.Contains("✗ Teapot  status expected \"200\", got \"418\"", lines);
        Assert.Equal("exit code 1", lines[^1]);
    }

    [Fact]
    public async Task TheJUnitReportSpeaksCi()
    {
        int deadPort = FreePort();
        var (summary, _) = await Run([
            Req("ok.req", "Ok", $"GET {_server.Origin}/ok\n"),
            Req("teapot.req", "Teapot", $"GET {_server.Origin}/teapot\n"),
            Req("dead.req", "Dead", $"GET http://127.0.0.1:{deadPort}/x\n"),
        ]);

        var xml = RunReports.JUnit(summary, "payments-api/");
        Assert.Contains("tests=\"3\"", xml);
        Assert.Contains("failures=\"1\"", xml);
        Assert.Contains("errors=\"1\"", xml);
        Assert.Contains("<failure message=\"expected 2xx, got 418\"", xml);
        Assert.Contains("<error message=", xml);
        Assert.Contains("name=\"Ok\"", xml);
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
