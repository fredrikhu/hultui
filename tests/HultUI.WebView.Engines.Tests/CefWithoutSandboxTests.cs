using System.Net;
using HultUI.WebView.Cef;
using Xunit;

namespace HultUI.WebView.Engines.Tests;

/// <summary>
/// Running without the sandbox (decided 2026-10-03): <see cref="CefEngineOptions.Sandbox"/>,
/// enforced to the application's own content unless
/// <see cref="CefEngineOptions.AllowUntrustedContentWithoutSandbox"/> says otherwise. The rules
/// here; the processes (CEF's mode is a process's) through the CEF test process.
/// </summary>
public class CefWithoutSandboxTests
{
    // ──── Rules ────

    [Fact]
    public void TheOption_LiftsRootAndTheAppArmorLimit()
    {
        var ubuntu = new CefSandbox.Facts("1", "unconfined", "63304", null, IsRoot: false, HasSetuidSandbox: false, "/opt/app/App");
        Assert.NotNull(CefSandbox.Judge(ubuntu));
        Assert.NotNull(CefSandbox.Judge(ubuntu with { IsRoot = true }));
        Assert.Null(CefSandbox.Judge(ubuntu with { NoSandbox = true }));
        Assert.Null(CefSandbox.Judge(ubuntu with { IsRoot = true, NoSandbox = true }));
    }

    [Fact]
    public void AnEngine_WantsTheSandbox_UnlessItsOptionSaysNot()
    {
        // The diagnostic switch takes the same path (checked in a process of its own below).
        Assert.True(CefRuntime.WantsSandbox(new CefEngineOptions()));
        Assert.False(CefRuntime.WantsSandbox(new CefEngineOptions { Sandbox = false }));
        Assert.True(new CefEngineOptions().Sandbox);
        Assert.False(new CefEngineOptions().AllowUntrustedContentWithoutSandbox);
    }

    [Fact]
    public void TwoEngines_AskingForDifferentModes_Conflict()
    {
        Assert.Null(CefRuntime.ModeConflict(null, wanted: true));    // not started: the first decides
        Assert.Null(CefRuntime.ModeConflict(null, wanted: false));
        Assert.Null(CefRuntime.ModeConflict(true, wanted: true));
        Assert.Null(CefRuntime.ModeConflict(false, wanted: false));
        Assert.Contains("with Chromium's sandbox", CefRuntime.ModeConflict(true, wanted: false));
        Assert.Contains("without Chromium's sandbox", CefRuntime.ModeConflict(false, wanted: true));
    }

    [Theory]
    [InlineData("https://hultui-string.invalid/abc/1", true)]   // the string pages
    [InlineData("https://app.local/index.html", true)]          // WebViewOptions.AppOrigin
    [InlineData("https://APP.LOCAL/x", true)]
    [InlineData("data:text/html,<p>hi", true)]
    [InlineData("blob:https://hultui-string.invalid/5b6c", true)]
    [InlineData("about:blank", true)]
    [InlineData("about:srcdoc", true)]
    [InlineData("https://example.org/", false)]                 // the internet
    [InlineData("http://app.local/", false)]                    // not the app's origin: another scheme
    [InlineData("https://app.local:8443/", false)]              // nor another port
    [InlineData("https://app.local.example.org/", false)]
    [InlineData("http://127.0.0.1:8080/", false)]               // the local network
    [InlineData("file:///etc/passwd", false)]
    [InlineData("wss://example.org/socket", false)]
    [InlineData("chrome://version", false)]
    [InlineData("about:version", false)]
    [InlineData("not a url", false)]
    public void TheApplicationsOwnContent_IsWhatPasses(string url, bool passes)
        => Assert.Equal(passes, CefView.IsOwnContent(url));

    // ──── Processes ────

    private static (int ExitCode, string Output) Run(string what, Dictionary<string, string?>? environment = null)
        => CefEngineTests.RunCefProcessWith(Guid.NewGuid().ToString("N"), ["--without-sandbox", what], environment);

    private static bool Online()
    {
        try { return Dns.GetHostAddresses("example.org").Length > 0; }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException) { return false; }
    }

    [CefFact]
    public void WithoutTheSandbox_TheRenderersReallyRunWithoutIt()
    {
        var off = Run("page");
        var on = Run("sandboxed");
        Assert.True(off.ExitCode == 0, off.Output);
        Assert.Contains("sandboxed: False", off.Output);
        Assert.Contains("CEF starts without Chromium's sandbox", off.Output);
        Assert.Contains("drew: True", off.Output);
        // Each renderer in this process's user namespace, with no seccomp filter Chromium added
        // (a container may have one of its own on every process, counted for the process too).
        var mine = Filters(off.Output, "this process: seccomp filters ");
        var renderers = off.Output.Split('\n').Where(l => l.StartsWith("renderer:")).ToList();
        Assert.NotEmpty(renderers);
        Assert.All(renderers, r => Assert.Contains("this process's user namespace", r));
        Assert.All(renderers, r => Assert.EndsWith($"seccomp filters {mine}", r.Trim()));
        // The same page with the sandbox: namespaces of their own and a filter more.
        Assert.Contains("sandboxed: True", on.Output);
        Assert.All(on.Output.Split('\n').Where(l => l.StartsWith("renderer:")),
            r => Assert.Contains("a user namespace of its own", r));
        Assert.All(on.Output.Split('\n').Where(l => l.StartsWith("renderer:")),
            r => Assert.True(int.Parse(r.Trim().Split(' ')[^1]) > int.Parse(mine), r));
    }

    private static string Filters(string output, string prefix) =>
        output.Split('\n').First(l => l.StartsWith(prefix))[prefix.Length..].Trim();

    [CefFact]
    public void WithoutTheSandbox_OnlyTheApplicationsOwnContentLoads()
    {
        var run = Run("page");
        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("own page: loaded", run.Output);
        Assert.Contains("""page saw: "{\"img\":\"blocked\",\"script\":\"blocked\",\"fetch\":\"blocked\"}""", run.Output);
        Assert.Contains("refused without Chromium's sandbox (not the application's own content): https://example.org/frame", run.Output);
        Assert.Contains("navigation to https://example.org/: failed " + CefView.BlockedWithoutSandbox, run.Output);
        // Each refusal logged once.
        Assert.Single(run.Output.Split('\n'), l => l.EndsWith("https://cdnjs.cloudflare.com/ajax/libs/jquery/3.7.1/jquery.min.js"));
    }

    [CefFact]
    public void TheDiagnosticSwitch_IsEnforcedTheSameWay()
    {
        var run = Run("env", new() { ["HULTUI_CEF_SWITCHES"] = "no-sandbox" });
        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("sandboxed: False", run.Output);
        Assert.Contains("""page saw: "{\"img\":\"blocked\",\"script\":\"blocked\",\"fetch\":\"blocked\"}""", run.Output);
        Assert.Contains("navigation to https://example.org/: failed " + CefView.BlockedWithoutSandbox, run.Output);
    }

    [CefFact]
    public void AllowingUntrustedContent_LoadsTheSamePage()
    {
        if (!Online()) return;   // the content is the internet's
        var run = Run("untrusted");
        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("sandboxed: False", run.Output);
        Assert.Contains("""page saw: "{\"img\":\"loaded\",\"script\":\"loaded\",\"fetch\":\"loaded\"}""", run.Output);
        Assert.Contains("navigation to https://example.org/: loaded", run.Output);
        Assert.DoesNotContain("refused without Chromium's sandbox", run.Output);
    }

    [CefFact]
    public void ASecondEngine_InTheOtherMode_IsUnavailable_AndItsPagesFail()
    {
        var run = Run("conflict");
        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("second engine available: False CEF already runs in this process without Chromium's sandbox", run.Output);
        Assert.Contains("second page: failed InitializationFailed: CEF already runs in this process without", run.Output);
    }
}
