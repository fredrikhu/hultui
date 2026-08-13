using System.Diagnostics;
using Meridian.Tests.Fixtures;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// The CLI as CI runs it: the real built binary, a real collection, the real
/// loopback server. Nothing is stubbed — the point of this suite is that the
/// pipeline story works end to end, exit codes included.
/// </summary>
public class CliTests : IClassFixture<TestHttpServer>, IDisposable
{
    private readonly TestHttpServer _server;
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-cli-").FullName;

    public CliTests(TestHttpServer server)
    {
        _server = server;
        Write("api/login.req", $"GET {_server.Origin}/ok\nname Login\nextract body $.ok -> flag\n");
        Write("api/consume.req", $"POST {{{{base_url}}}}/echo\nname Consume\nbody text\nf={{{{flag}}}}\nend\n");
        Write("other/teapot.req", $"GET {_server.Origin}/teapot\nname Teapot\nassert status equals 200\n");
        Write("envs/ci.env", $"base_url={_server.Origin}\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    // ── Running the real binary ──────────────────────────────────────────

    /// <summary>
    /// The built CLI assembly, resolved from the CLI project's own output.
    ///
    /// <para>Deliberately NOT a same-directory lookup: the CLI's assembly is
    /// named <c>meridian</c> and the GUI's <c>Meridian</c>, which are the same
    /// file on Windows. A copy of either landing in this directory would be
    /// picked up and run as the CLI — one of them isn't, and the failure is an
    /// opaque CLR exit code. Resolving from the project's bin makes the
    /// identity unambiguous.</para>
    /// </summary>
    private static string CliDll
    {
        get
        {
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo != null && !Directory.Exists(Path.Combine(repo.FullName, "apps"))) repo = repo.Parent;
            Assert.True(repo != null, "could not locate the repository root");
            var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}")
                ? "Release" : "Debug";
            var built = Path.Combine(repo!.FullName, "apps", "Meridian", "Meridian.Cli",
                "bin", configuration, "net10.0", "meridian.dll");
            Assert.True(File.Exists(built), $"the CLI is not built at {built}");
            return built;
        }
    }

    private (int Exit, string Out, string Err) Cli(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _root,
            // The CLI emits UTF-8 (it sets OutputEncoding); decode it as such,
            // or the report glyphs arrive mangled and every assertion on them
            // fails for the wrong reason.
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        psi.ArgumentList.Add(CliDll);
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "the CLI did not exit");
        return (process.ExitCode, stdout, stderr);
    }

    // ── Argument parsing, through the real binary ────────────────────────
    // Not in-process: the CLI's assembly is named "meridian", which cannot be
    // referenced from here without clobbering the GUI's Meridian.dll on a
    // case-insensitive filesystem. Driving the binary is better verification
    // anyway — it proves the exit code and the words a CI log really sees.

    private const int ExitUsage = 64;

    [Theory]
    [InlineData(new[] { "run", ".", "--env" }, "--env needs a name")]
    [InlineData(new[] { "run", ".", "--report" }, "--report needs")]
    [InlineData(new[] { "run", ".", "--report", "html:out" }, "unknown report format")]
    [InlineData(new[] { "run", ".", "--wat" }, "unknown option")]
    [InlineData(new[] { "sing", "." }, "unknown command")]
    [InlineData(new[] { "run" }, "needs a collection path")]
    public void BadArgumentsExitWithUsageAndSayWhy(string[] args, string fragment)
    {
        var (exit, _, stderr) = Cli(args);

        Assert.Equal(ExitUsage, exit);
        Assert.Contains(fragment, stderr, StringComparison.OrdinalIgnoreCase);
    }

    // ── End to end ───────────────────────────────────────────────────────

    [Fact]
    public void AFailingRunExitsOneAndQuotesTheFailure()
    {
        var (exit, stdout, _) = Cli("run", ".", "--env", "ci");

        Assert.Equal(1, exit);
        Assert.Contains("✗ Teapot", stdout);
        Assert.Contains("status expected \"200\", got \"418\"", stdout);
        Assert.Contains("exit code 1", stdout);
    }

    [Fact]
    public void AGreenScopeExitsZero()
    {
        var (exit, stdout, _) = Cli("run", "api", "--env", "ci");

        Assert.Equal(0, exit);
        Assert.Contains("✓ Login", stdout);
        Assert.Contains("✓ Consume", stdout);
        Assert.Contains("exit code 0", stdout);
    }

    [Fact]
    public void TheCliChainsExactlyAsTheAppDoes()
    {
        // consume.req uses {{flag}} (extracted by login) and {{base_url}} (env).
        var (exit, stdout, _) = Cli("run", "api", "--env", "ci");

        Assert.Equal(0, exit);
        // Login must print before Consume: chain order, not directory order.
        Assert.True(stdout.IndexOf("Login", StringComparison.Ordinal)
            < stdout.IndexOf("Consume", StringComparison.Ordinal), stdout);
    }

    [Fact]
    public void TheTextOutputIsTheSameReportTheAppShows()
    {
        var (_, stdout, _) = Cli("run", ".", "--env", "ci");

        // The exact header/counts/exit shape from RunReports — the CI log and
        // the app's summary card are one set of words.
        Assert.Contains($"{Path.GetFileName(_root)}/ (ci) 3 requests", stdout);
        Assert.Matches(@"✓ 2  ✗ 1  ! 0  − 0  wall [\d.]+s", stdout);
    }

    [Fact]
    public void JUnitIsWrittenWhereAsked()
    {
        string report = Path.Combine("reports", "run.xml");
        var (exit, stdout, _) = Cli("run", ".", "--env", "ci", "--report", "junit:" + report);

        Assert.Equal(1, exit);
        var full = Path.Combine(_root, report);
        Assert.True(File.Exists(full), $"{full} was not written");
        var xml = File.ReadAllText(full);
        Assert.Contains("<testsuite", xml);
        Assert.Contains("failures=\"1\"", xml);
        Assert.Contains("name=\"Teapot\"", xml);
        Assert.Contains("junit: " + report, stdout);
    }

    [Fact]
    public void BailStopsAtTheFirstFailure()
    {
        var (exit, stdout, _) = Cli("run", ".", "--env", "ci", "--bail");

        Assert.Equal(1, exit);
        Assert.Contains("−", stdout);   // something was skipped
    }

    [Fact]
    public void QuietPrintsOnlyTheSummary()
    {
        var (_, stdout, _) = Cli("run", "api", "--env", "ci", "--quiet");

        Assert.DoesNotContain("✓ Login", stdout);   // no per-request stream
        Assert.Contains("exit code 0", stdout);     // but the report is there
    }

    [Fact]
    public void ListNamesTheScopeWithoutSending()
    {
        var (exit, stdout, _) = Cli("list", "api");

        Assert.Equal(0, exit);
        Assert.Contains("Login", stdout);
        Assert.Contains("Consume", stdout);
        Assert.DoesNotContain("Teapot", stdout);    // outside the scope
        Assert.Contains("2 requests", stdout);
    }

    [Fact]
    public void AnUnknownEnvironmentIsAUsageErrorListingTheRealOnes()
    {
        var (exit, _, stderr) = Cli("run", ".", "--env", "nope");

        Assert.Equal(ExitUsage, exit);
        Assert.Contains("no environment \"nope\"", stderr);
        Assert.Contains("ci", stderr);
    }

    [Fact]
    public void AMissingPathIsAUsageError()
    {
        var (exit, _, stderr) = Cli("run", "no-such-folder");

        Assert.Equal(ExitUsage, exit);
        Assert.Contains("not a folder", stderr);
    }

    [Fact]
    public void NoArgumentsPrintsUsage()
    {
        var (exit, _, stderr) = Cli();

        Assert.Equal(ExitUsage, exit);
        Assert.Contains("usage:", stderr);
        Assert.Contains("--report junit:", stderr);
    }

    [Fact]
    public void ASecretMissingOnThisMachineIsNamedBeforeTheRun()
    {
        Write("envs/sec.env", $"base_url={_server.Origin}\nsecret api_token\n");
        var (_, _, stderr) = Cli("run", "api", "--env", "sec");

        Assert.Contains("secret api_token is not set on this machine", stderr);
    }

    [Fact]
    public void ACiInjectedEnvironmentVariableResolvesASecret()
    {
        // The whole point of the env-var fallback: CI has no keyring.
        Write("envs/sec.env", $"base_url={_server.Origin}\nsecret injected_token\n");
        Write("api/uses-secret.req", "GET {{base_url}}/ok\nname UsesSecret\nheader A: {{injected_token}}\n");

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _root,
        };
        psi.ArgumentList.Add(CliDll);
        foreach (var arg in new[] { "run", "api", "--env", "sec" }) psi.ArgumentList.Add(arg);
        psi.Environment["injected_token"] = "from-ci";

        using var process = Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);

        Assert.DoesNotContain("not set on this machine", stderr);
        Assert.Contains("✓ UsesSecret", stdout);
        Assert.Equal(0, process.ExitCode);
    }
}
