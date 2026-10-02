using System.Diagnostics;
using HultUI.WebView.Cef;
using HultUI.WebView.Cef.Interop;
using Xunit;

namespace HultUI.WebView.Engines.Tests;

/// <summary>Skips unless a CEF this engine can use is installed: Linux, with the library, its
/// resources and the helper (the CI runner has none).</summary>
public sealed class CefFactAttribute : FactAttribute
{
    public CefFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "CEF is the Linux engine.";
        else if (!CefLibrary.Loaded.IsLoaded) Skip = "No usable CEF: " + CefLibrary.Loaded.Error;
    }
}

/// <summary>Skips off Linux: a fact about Linux's paths or processes (slashes, symbolic
/// links, <c>/proc</c>) that needs no CEF. Windows builds paths with backslashes and creates
/// symbolic links only with a privilege.</summary>
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "A fact about Linux's paths and processes.";
    }
}

/// <summary>
/// Step 5.1 of the Linux engine: where CEF is looked for, whether it is there, and CEF's one
/// start and one stop per process. The switches run anywhere, the search on Linux; the rest needs
/// CEF, and the stop needs a process of its own (HultUI.WebView.Engines.Tests.CefProcess),
/// because it happens when that process exits.
/// </summary>
public class CefEngineTests
{
    [LinuxFact]
    public void CefIsLookedFor_InTheBriefsOrder()
    {
        var dirs = CefLibrary.CandidateDirectories("/opt/my-cef", "/app", "linux-x64").ToList();
        Assert.Equal(["/opt/my-cef", "/app/cef", "/app/runtimes/linux-x64/native/cef", "/usr/lib64/cef", "/usr/lib/cef"], dirs);
        Assert.Equal("/app/cef", CefLibrary.CandidateDirectories(null, "/app", "linux-x64").First());
    }

    [LinuxFact]
    public void TheHelper_IsLookedFor_BesideTheApplication_ThenWhereThePackagePutsIt()
    {
        var helpers = CefLibrary.CandidateHelpers("/opt/helper", "/app", "linux-arm64").ToList();
        Assert.Equal(["/opt/helper", "/app/hultui-cef-helper", "/app/runtimes/linux-arm64/native/hultui-cef-helper"], helpers);
    }

    [Fact]
    public void ADirectory_NeedsTheResourcesBesideTheLibrary()
    {
        var root = Directory.CreateTempSubdirectory("hultui-cef-search-").FullName;
        try
        {
            // The official distribution as it unpacks: the library in Release, the rest in Resources.
            var release = Directory.CreateDirectory(Path.Combine(root, "dist", "Release")).FullName;
            Directory.CreateDirectory(Path.Combine(root, "dist", "Resources"));
            File.WriteAllText(Path.Combine(release, "libcef.so"), "");
            var flat = Directory.CreateDirectory(Path.Combine(root, "flat")).FullName;
            foreach (var f in new[] { "libcef.so", "icudtl.dat", "v8_context_snapshot.bin", "resources.pak" })
                File.WriteAllText(Path.Combine(flat, f), "");

            Assert.Equal(flat, CefLibrary.FindDirectory([Path.Combine(root, "none"), release, flat], out var incomplete));
            Assert.Contains("icudtl.dat", incomplete);
            Assert.Contains("Resources/ has to be merged", incomplete);

            Assert.Null(CefLibrary.FindDirectory([release], out incomplete));
            Assert.NotNull(incomplete);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TheOzonePlatform_FollowsTheWindowSystem()
    {
        // Spike L0, questions 1, 3 and 8: the window's own platform; Wayland's scale needs
        // fractional scaling off; with no window system, headless with ANGLE on GL.
        Assert.Equal([("ozone-platform", "wayland"), ("disable-features", "WaylandFractionalScaleV1")], CefRuntime.Switches("wayland"));
        Assert.Equal([("ozone-platform", "x11")], CefRuntime.Switches("x11"));
        Assert.Equal([("ozone-platform", "headless"), ("use-angle", "gl-egl")], CefRuntime.Switches(null));
    }

    [Fact]
    public void WithoutCef_TheEngineSaysWhy_AndDoesNotThrow()
    {
        if (CefLibrary.Loaded.IsLoaded) return;   // only meaningful where there is none
        var availability = new CefEngine().Availability;
        Assert.False(availability.IsAvailable);
        Assert.False(string.IsNullOrEmpty(availability.Reason));
    }

    [CefFact]
    public void WithCef_TheEngineIsAvailable_AndNamesItsVersion()
    {
        using var engine = new CefEngine();
        Assert.Equal("CEF", engine.Name);
        Assert.True(engine.Availability.IsAvailable, engine.Availability.Reason);
        Assert.Matches(@"^\d{3,}\.\d+\.\d+ \(Chromium \d+\.\d+\.\d+\.\d+\)$", engine.Version);
        Assert.True(int.Parse(engine.Version!.Split('.')[0]) >= 146);
    }

    [CefFact]
    public async Task CefStarts_OnceForTheProcess_AndRunsItsOwnUiThread()
    {
        var first = CefRuntime.Start(windowSystem: null);
        Assert.Null(await first.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.True(CefRuntime.IsRunning);
        Assert.Same(first, CefRuntime.Start("wayland"));   // the first start decided

        // Work posted to CEF runs on CEF's UI thread, which is not this one.
        var ran = new TaskCompletionSource<string>();
        Assert.True(CefRuntime.Post(() => ran.SetResult(ThreadName())));
        Assert.Equal("CrBrowserMain", await ran.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [CefFact]
    public async Task ASecondEngine_AfterTheFirstIsDisposed_StillWorks()
    {
        var first = new CefEngine();
        Assert.True(first.Availability.IsAvailable);
        Assert.Null(await CefRuntime.Start(null).WaitAsync(TimeSpan.FromSeconds(30)));
        first.Dispose();
        Assert.Throws<ObjectDisposedException>(() => first.CreateView(new WebViewOptions()));

        using var second = new CefEngine();
        Assert.True(second.Availability.IsAvailable, second.Availability.Reason);
        Assert.True(CefRuntime.IsRunning);
    }

    [CefFact]
    public void WhenTheProcessExits_CefShutsDownOnItsOwnThread_AndLeavesNothing()
    {
        // A marker in the environment, which every process CEF starts inherits: none of them
        // may outlive the process that started CEF.
        var mark = Guid.NewGuid().ToString("N");
        var run = RunCefProcess(mark, "--second-engine");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("started: CEF ", run.Output);
        Assert.Contains("a second engine after disposing the first: True", run.Output);
        Assert.Contains("process exit: shutting down", run.Output);
        Assert.Matches(@"shut down in \d+ ms on 'CEF startup'", run.Output);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        List<string> left;
        while ((left = ProcessesMarked(mark)).Count > 0 && DateTime.UtcNow < deadline) Thread.Sleep(100);
        Assert.True(left.Count == 0, "still running: " + string.Join("; ", left));
    }

    [CefFact]
    public void ASecondInstance_OfTheSameApplication_StartsOnATemporaryProfile()
    {
        // Chromium allows one browser per profile: a second cef_initialize on a held one hands
        // over to the first and fails, which left a second instance of an application without
        // pages until the engine looked for the lock first.
        var mark = Guid.NewGuid().ToString("N");
        var first = Task.Run(() => RunCefProcess(mark, "--hold", "4000"));
        Thread.Sleep(2000);   // the first is up and holds the profile
        var second = RunCefProcess(mark);
        var firstRun = first.Result;

        Assert.True(firstRun.ExitCode == 0, firstRun.Output);
        Assert.True(second.ExitCode == 0, second.Output);
        var firstProfile = ProfileOf(firstRun.Output);
        var secondProfile = ProfileOf(second.Output);
        Assert.EndsWith("/CEF", firstProfile);
        Assert.Contains("hultui-cef-", secondProfile);
        Assert.False(Directory.Exists(secondProfile), "the temporary profile was left behind: " + secondProfile);

        static string ProfileOf(string output) => output.Split('\n').First(l => l.StartsWith("profile: "))["profile: ".Length..].Trim();
    }

    [LinuxFact]
    public void AProfilesLock_IsHeld_OnlyByAnotherLiveProcessOnThisMachine()
    {
        var profile = Directory.CreateTempSubdirectory("hultui-cef-lock-").FullName;
        try
        {
            var lockPath = Path.Combine(profile, "SingletonLock");
            Assert.Null(CefRuntime.HeldByAnother(profile));

            File.CreateSymbolicLink(lockPath, $"{Environment.MachineName}-1");   // init: alive, not us
            Assert.Equal(1, CefRuntime.HeldByAnother(profile));
            File.Delete(lockPath);

            File.CreateSymbolicLink(lockPath, $"{Environment.MachineName}-{Environment.ProcessId}");
            Assert.Null(CefRuntime.HeldByAnother(profile));
            File.Delete(lockPath);

            File.CreateSymbolicLink(lockPath, $"{Environment.MachineName}-{int.MaxValue}");   // no such process
            Assert.Null(CefRuntime.HeldByAnother(profile));
            File.Delete(lockPath);

            File.CreateSymbolicLink(lockPath, "another-host-1");
            Assert.Null(CefRuntime.HeldByAnother(profile));
        }
        finally { Directory.Delete(profile, recursive: true); }
    }

    private static string ThreadName() => File.ReadAllText($"/proc/self/task/{gettid()}/comm").Trim();

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int gettid();

    private static (int ExitCode, string Output) RunCefProcess(string mark, params string[] args)
    {
        const string name = "HultUI.WebView.Engines.Tests.CefProcess";
        // Beside this test project: tests/<name>/bin/<config>/<tfm>/, as this assembly is.
        var tfm = new DirectoryInfo(AppContext.BaseDirectory);
        var config = tfm.Parent!;
        var tests = config.Parent!.Parent!.Parent!;
        var dll = Path.Combine(tests.FullName, name, "bin", config.Name, tfm.Name, name + ".dll");
        Assert.True(File.Exists(dll), "not built: " + dll);
        var start = new ProcessStartInfo("dotnet", [dll, .. args])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["HULTUI_TEST_MARK"] = mark;
        using var p = Process.Start(start)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(60_000)) { p.Kill(entireProcessTree: true); return (-1, "timed out\n" + stdout.Result); }
        return (p.ExitCode, stdout.Result + stderr.Result);
    }

    private static List<string> ProcessesMarked(string mark)
    {
        var found = new List<string>();
        var needle = "HULTUI_TEST_MARK=" + mark;
        foreach (var dir in Directory.GetDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
            try
            {
                if (File.ReadAllText(Path.Combine(dir, "environ")).Contains(needle))
                    found.Add($"{pid} {File.ReadAllText(Path.Combine(dir, "cmdline")).Replace('\0', ' ').Trim()}");
            }
            catch (Exception) { }   // gone, or not ours
        }
        return found;
    }
}
