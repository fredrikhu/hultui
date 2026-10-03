using HultUI.WebView.Cef;
using Xunit;

namespace HultUI.WebView.Engines.Tests;

/// <summary>
/// Step 5.7: whether Chromium's sandbox can start, judged before CEF, which otherwise ends the
/// process ("No usable sandbox!"), as it did on Ubuntu 24.04 without a profile.
/// </summary>
public class CefSandboxTests
{
    private static CefSandbox.Facts Ubuntu(string label = "unconfined", string exe = "/opt/app/App") =>
        new("1\n", label + "\n", "63304\n", null, IsRoot: false, HasSetuidSandbox: false, exe);

    [Fact]
    public void Ubuntu_WithoutAProfile_IsRefused_AndSaysWhatToInstall()
    {
        var why = CefSandbox.Judge(Ubuntu());
        Assert.NotNull(why);
        Assert.Contains("AppArmor profile", why);
        Assert.Contains("/opt/app/App", why);
        Assert.DoesNotContain("dotnet host", why);
    }

    [Fact]
    public void Ubuntu_UnderAProfile_Starts()
    {
        Assert.Null(CefSandbox.Judge(Ubuntu("hultui-sample (unconfined)")));
        Assert.Null(CefSandbox.Judge(Ubuntu("hultui-sample (enforce)")));
    }

    [Fact]
    public void UnderTheDotnetHost_TheReasonSaysToShipAnExecutable()
        => Assert.Contains("dotnet host", CefSandbox.Judge(Ubuntu(exe: "/usr/lib/dotnet/dotnet")));

    [Fact]
    public void ASetuidSandbox_NeedsNoNamespaces()
        => Assert.Null(CefSandbox.Judge(Ubuntu() with { HasSetuidSandbox = true }));

    /// <summary>Chromium refuses a root browser process without --no-sandbox and ends it (seen
    /// on Debian 13 and Ubuntu 24.04 with sudo), on any distribution, with a setuid sandbox or not.</summary>
    [Fact]
    public void Root_IsRefused_Everywhere_UnlessTheApplicationChoseNoSandbox()
    {
        var fedora = new CefSandbox.Facts(null, "unconfined", "63304", null, IsRoot: true, HasSetuidSandbox: false, "/opt/app/App");
        foreach (var root in new[] { fedora, fedora with { HasSetuidSandbox = true }, Ubuntu() with { IsRoot = true },
                     Ubuntu("app (unconfined)") with { IsRoot = true } })
            Assert.Contains("as root", CefSandbox.Judge(root));
        Assert.Null(CefSandbox.Judge(fedora with { NoSandbox = true }));
        Assert.Null(CefSandbox.Judge(Ubuntu() with { IsRoot = true, NoSandbox = true }));
    }

    [Fact]
    public void NoSandbox_NeedsNoProfileEither()
        => Assert.Null(CefSandbox.Judge(Ubuntu() with { NoSandbox = true }));

    [Fact]
    public void Fedora_Debian_AndAnUnrestrictedKernel_Start()
    {
        // Fedora and Debian have no such sysctl; a kernel with it at 0 allows everyone.
        Assert.Null(CefSandbox.Judge(new(null, "unconfined", "63304", null, false, false, "/opt/app/App")));
        Assert.Null(CefSandbox.Judge(Ubuntu() with { RestrictUnprivilegedUserns = "0" }));
        Assert.Null(CefSandbox.Judge(new(null, null, "63304", "1", false, false, "/opt/app/App")));
    }

    [Fact]
    public void UserNamespacesTurnedOff_AreRefusedToo()
    {
        Assert.Contains("turned off", CefSandbox.Judge(new(null, null, "0", null, false, false, "/opt/app/App")));
        Assert.Contains("turned off", CefSandbox.Judge(new(null, null, "63304", "0", false, false, "/opt/app/App")));
    }

    [LinuxFact]
    public void OnThisMachine_TheSandboxCanStart()
        => Assert.Null(CefSandbox.Problem("/nonexistent", noSandbox: false));   // the engine tests run CEF here
}
