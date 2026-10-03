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
        Assert.Equal([("ozone-platform", "x11"), ("use-fake-device-for-media-stream", null), ("v", "1")],
            CefRuntime.Switches("x11", " --use-fake-device-for-media-stream  v=1 "));
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
            var host = CefRuntime.HostName();
            Assert.Null(CefRuntime.HeldByAnother(profile));

            File.CreateSymbolicLink(lockPath, $"{host}-1");   // init: alive, not us
            Assert.Equal(1, CefRuntime.HeldByAnother(profile));
            File.Delete(lockPath);

            File.CreateSymbolicLink(lockPath, $"{host}-{Environment.ProcessId}");
            Assert.Null(CefRuntime.HeldByAnother(profile));
            File.Delete(lockPath);

            File.CreateSymbolicLink(lockPath, $"{host}-{int.MaxValue}");   // no such process
            Assert.Null(CefRuntime.HeldByAnother(profile));
            File.Delete(lockPath);

            File.CreateSymbolicLink(lockPath, "another-host-1");
            Assert.Null(CefRuntime.HeldByAnother(profile));
        }
        finally { Directory.Delete(profile, recursive: true); }
    }

    [LinuxFact]
    public void OnAHostNameWithADot_TheLockIsStillRecognised()
    {
        // Chromium writes the whole host name; Environment.MachineName would stop at the dot.
        var profile = Directory.CreateTempSubdirectory("hultui-cef-lock-").FullName;
        try
        {
            File.CreateSymbolicLink(Path.Combine(profile, "SingletonLock"), "build-3.example.com-1");
            Assert.Equal(1, CefRuntime.HeldByAnother(profile, hostName: "build-3.example.com"));
            Assert.Null(CefRuntime.HeldByAnother(profile, hostName: "build-3"));
        }
        finally { Directory.Delete(profile, recursive: true); }
    }

    [LinuxFact]
    public void TheProfileRule_TheFirstFolderRules()
    {
        const string root = "/home/u/.local/share/App/CEF";
        Assert.Equal((null, null), CefRuntime.ResolveFolder(root, root, null));
        Assert.Equal((null, null), CefRuntime.ResolveFolder(root, root, root + "/"));
        Assert.Equal(((string?)(root + "/work"), (string?)null), CefRuntime.ResolveFolder(root, root, root + "/work"));

        var (child, error) = CefRuntime.ResolveFolder(root, root, "/home/u/.local/share/App/CEF2");   // a sibling, not inside
        Assert.Null(child);
        Assert.Contains("one profile per process", error);
        Assert.Contains(root, error);
        Assert.Contains("/home/u/.local/share/App/CEF2", error);

        // A second instance runs on a temporary profile: a folder inside the preferred one is
        // placed inside the temporary one.
        Assert.Equal(((string?)"/tmp/hultui-cef-9/work", (string?)null), CefRuntime.ResolveFolder(root, "/tmp/hultui-cef-9", root + "/work"));
    }

    [CefFact]
    public void TheProfileRule_InAProcess_SharesInsideAndRefusesOutside()
    {
        var root = Directory.CreateTempSubdirectory("hultui-cef-root-").FullName;
        try
        {
            var run = RunCefProcess(Guid.NewGuid().ToString("N"), "--folders", root);
            Assert.True(run.ExitCode == 0, run.Output);
            Assert.Contains("root: loaded", run.Output);
            Assert.Contains("profile: " + root, run.Output);
            Assert.Contains("inside: loaded", run.Output);   // a request context of its own, string pages served there too
            Assert.Contains("none: loaded", run.Output);     // no folder: the process's profile
            Assert.Contains("outside: failed: CEF keeps one profile per process", run.Output);
            Assert.Contains("inner folder has files: True", run.Output);
            Assert.Contains("outside folder made: False", run.Output);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [CefFact]
    public void DotNetsSignalHandlers_SurviveCef()
    {
        // Found while fixing the review: Chromium resets SIGSEGV and friends, so a null
        // dereference ended the process instead of throwing; and it takes SIGCHLD, so a process
        // started before CEF could never be waited for (the tests that start one hung).
        var run = RunCefProcess(Guid.NewGuid().ToString("N"), "--signals");
        Assert.True(run.ExitCode == 0, run.Output);
        var before = run.Output.Split('\n').First(l => l.StartsWith("before CEF:"))["before CEF:".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var after = run.Output.Split('\n').First(l => l.StartsWith("after CEF:"))["after CEF:".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Empty(before.Except(after));
        // Caught is not enough: Chromium's SIGCHLD was caught before and after, by Chromium.
        // Every handler .NET had (SIGRTMIN for the collector's suspensions, TERM, INT and QUIT
        // for a clean exit, the faults for exceptions) is still .NET's.
        Assert.Contains("handlers changed by CEF's start: none", run.Output);
        Assert.Contains("a page drew: True, the restore after it ran: True", run.Output);
        Assert.Contains("handlers changed by a page: none", run.Output);
        Assert.Matches(@"handlers changed by a page: none \(of \d+: .*\bSEGV\b.*\bTERM\b.*\bCHLD\b", run.Output);
        Assert.Contains("a process started after CEF is seen to exit: True", run.Output);
        Assert.Contains("a null dereference is a NullReferenceException", run.Output);
    }

    [CefFact]
    public void PagesDisposedWhileCefMakesThem_GoQuietly()
    {
        // Found in step 5.5: a view disposed while CEF created its browser closed it inside
        // on_after_created, which runs inside the creation, and the process segfaulted there
        // (every run of this, before the fix). Eighty pages placed and disposed in turn.
        var run = RunCefProcess(Guid.NewGuid().ToString("N"), "--quick-dispose", "x11", "40");
        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("disposed", run.Output);
        Assert.Contains("process exit: shutting down", run.Output);
    }

    [CefFact]
    public void AFirstProcessStartedWhileCefStarts_KeepsDotNetsSigchld()
    {
        // The cause of the process tests' rare timeouts (review of 5.3 and 5.4, item 2): .NET's
        // SIGCHLD handler, installed while CEF started, was overwritten by the default saved
        // before it, and no child was reaped after.
        var run = RunCefProcess(Guid.NewGuid().ToString("N"), "--first-child-while-cef-starts");
        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("CEF started: True", run.Output);
        Assert.Contains("a process started after CEF is seen to exit: True", run.Output);
    }

    [LinuxFact]
    public void OnlyChromiumsChanges_ArePutBack()
    {
        nint libc = System.Runtime.InteropServices.NativeLibrary.GetExport(
            System.Runtime.InteropServices.NativeLibrary.Load("libc.so.6"), "getpid");
        Assert.True(CefRuntime.ChromiumTook(0, libc));      // a handler reset to the default
        Assert.False(CefRuntime.ChromiumTook(libc, 0));     // someone else's since: theirs stays
        Assert.False(CefRuntime.ChromiumTook(0, 0));
    }

    [CefFact]
    public void AnApplicationsSignalHandler_Runs_AndCefStopsCleanly()
    {
        // Chromium took SIGTERM and SIGINT for a shutdown of its own: an application's
        // PosixSignalRegistration (or Console.CancelKeyPress) never ran.
        var mark = Guid.NewGuid().ToString("N");
        var run = RunCefProcess(mark, ["--on-term", "--hold", "30000"], terminateAfterMs: 4000);
        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("started: CEF ", run.Output);
        Assert.Contains("the application's SIGTERM handler ran", run.Output);
        Assert.Contains("process exit: shutting down", run.Output);
        Assert.Matches(@"shut down in \d+ ms on 'CEF startup'", run.Output);
    }

    [LinuxFact]
    public void TheHostName_IsTheKernels_Whole()
        => Assert.Equal(File.ReadAllText("/proc/sys/kernel/hostname").Trim(), CefRuntime.HostName());

    private static string ThreadName() => File.ReadAllText($"/proc/self/task/{gettid()}/comm").Trim();

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int gettid();

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int kill(int pid, int signal);

    internal static (int ExitCode, string Output) RunCefProcess(string mark, params string[] args)
        => RunCefProcess(mark, args, terminateAfterMs: null);

    private static (int ExitCode, string Output) RunCefProcess(string mark, string[] args, int? terminateAfterMs)
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
        if (terminateAfterMs is { } wait && !p.WaitForExit(wait)) kill(p.Id, 15);   // SIGTERM, as a kill does
        if (!p.WaitForExit(60_000))
        {
            // Review of 5.3 and 5.4, item 2: say what was still running, so a timeout says why.
            var running = string.Join("\n", ProcessesMarked(mark)) + "\n" + WhyNotExited(p.Id);
            p.Kill(entireProcessTree: true);
            p.WaitForExit(5000);
            // Bounded: a pipe may be held open by what the child left behind.
            Task.WaitAll([stdout, stderr], 5000);
            return (-1, "timed out\n" + (stdout.IsCompleted ? stdout.Result : "") + "\n--- stderr ---\n" + (stderr.IsCompleted ? stderr.Result : "")
                + "\n--- still running ---\n" + running);
        }
        return (p.ExitCode, stdout.Result + stderr.Result);
    }

    /// <summary>A child the harness gave up on: its state (Z: exited, never reaped) and this
    /// process's signals (a SIGCHLD pending or blocked everywhere would leave it so).</summary>
    private static string WhyNotExited(int pid)
    {
        string Read(string path) { try { return File.ReadAllText(path); } catch (Exception ex) { return ex.GetType().Name; } }
        var stat = Read($"/proc/{pid}/stat");
        var state = stat.LastIndexOf(')') is var close and > 0 && close + 2 < stat.Length ? stat[(close + 2)..(close + 3)] : stat;
        var mine = Read("/proc/self/status").Split('\n').Where(l => l.StartsWith("Sig") || l.StartsWith("ShdPnd"));
        int blocking = 0, threads = 0;
        foreach (var task in Directory.GetDirectories("/proc/self/task"))
        {
            threads++;
            var blk = Read(Path.Combine(task, "status")).Split('\n').FirstOrDefault(l => l.StartsWith("SigBlk:"))?[7..].Trim() ?? "0";
            if ((Convert.ToUInt64(blk, 16) & (1UL << 16)) != 0) blocking++;
        }
        var handler = BitConverter.ToUInt64(CefRuntime.SaveSignal(17), 0);
        return $"child {pid} state {state}; this process: {string.Join("; ", mine)}; {blocking} of {threads} threads block SIGCHLD; SIGCHLD handler {handler:X}";
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

/// <summary>Review of 5.1 and 5.2, item 1: string documents are kept per view, a bounded number.</summary>
public class CefStringPagesTests
{
    [Fact]
    public void AView_KeepsItsNewestStringDocuments_NotAll()
    {
        var owner = Guid.NewGuid().ToString("N");
        var uris = Enumerable.Range(0, StringPages.KeptPerView + 3).Select(i => StringPages.Add(owner, $"<p>{i}</p>")).ToList();

        Assert.All(uris.Take(3), u => Assert.False(StringPages.IsServed(u)));
        Assert.All(uris.Skip(3), u => Assert.True(StringPages.IsServed(u)));

        var other = StringPages.Add(Guid.NewGuid().ToString("N"), "<p>another view's</p>");
        StringPages.Forget(owner);
        Assert.All(uris, u => Assert.False(StringPages.IsServed(u)));
        Assert.True(StringPages.IsServed(other));
    }
}

/// <summary>Review of 5.1 and 5.2, item 4: an exception that leaves an
/// <c>[UnmanagedCallersOnly]</c> method ends the process, so every one in the engine catches.</summary>
public class CefCallbackTests
{
    [Fact]
    public void EveryCallbackFromCef_CatchesWhatItThrows()
    {
        var callbacks = typeof(CefEngine).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
            .Where(m => m.IsDefined(typeof(System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute), false))
            .ToList();
        Assert.True(callbacks.Count > 20, $"only {callbacks.Count} callbacks found");
        var without = callbacks
            .Where(m => !(m.GetMethodBody()?.ExceptionHandlingClauses.Any(c => c.Flags == System.Reflection.ExceptionHandlingClauseOptions.Clause) ?? false))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();
        Assert.True(without.Count == 0, "no catch in: " + string.Join(", ", without));
    }
}
