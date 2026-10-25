using System.Runtime.CompilerServices;
using Junction.Git;

namespace Junction.Tests;

/// <summary>
/// A throwaway git repository on disk, plus the environment isolation the tests need.
///
/// <para>Junction talks to the git CLI, so the only honest way to test that layer is
/// against a real repository. The hazard is the developer's own git configuration:
/// <c>core.autocrlf</c> (on by default in Git for Windows' system config) rewrites the
/// line endings this suite asserts on, and <c>commit.gpgsign</c> or a global hooks path
/// makes commits fail on some machines and not others. Both config layers are pointed at
/// empty files for the whole test process, which is why <c>AssemblyInfo.cs</c> disables
/// parallelization.</para>
/// </summary>
internal sealed class TempRepo : IDisposable
{
    private static readonly object EnvLock = new();
    private static bool _envPrepared;

    /// <summary>Names every fixture, so creating one and sweeping one cannot drift apart.</summary>
    private const string FixturePrefix = "junction-git-";

    public string Path { get; }
    public GitRepository Repo { get; }

    private TempRepo(string path)
    {
        Path = path;
        Repo = new GitRepository(path);
    }

    /// <summary>Creates a repository with one root commit already in it. Every call gets its
    /// own copy of the same repository, so the commit is identical across fixtures (same id,
    /// same date) — see <see cref="CreateFrom"/>.</summary>
    public static TempRepo CreateWithInitialCommit() => CreateFrom("initial-commit", () =>
    {
        var repo = CreateEmpty();
        repo.Write("README.md", "hello\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "initial");
        return repo;
    });

    public static TempRepo CreateEmpty()
    {
        var repo = new TempRepo(NewFixturePath());
        repo.Git("init", "-b", "main");
        // Written straight into the repository's config rather than with four `git config`
        // calls: the suite's time is mostly git process starts, and these were four of the
        // five an empty fixture used to make. No automatic maintenance: every commit started
        // a detached `git maintenance run --auto` beside the test, one more process working in
        // the fixture's .git while the app under test reads and watches it.
        File.AppendAllText(System.IO.Path.Combine(repo.Path, ".git", "config"),
            "[user]\n\tname = Junction Tests\n\temail = tests@example.invalid\n" +
            "[commit]\n\tgpgsign = false\n" +
            "[core]\n\tautocrlf = false\n" +
            "[maintenance]\n\tauto = false\n" +
            "[gc]\n\tauto = 0\n");
        return repo;
    }

    private static readonly Dictionary<string, TempRepo> Templates = new();

    /// <summary>
    /// A fresh copy of a fixture that <paramref name="build"/> makes once per test process.
    ///
    /// <para>The suite's time goes on starting git, and a fixture made of commits pays for
    /// every one of them in every test that wants it — sixty commits, branches and tags is
    /// close to two hundred processes, about five seconds, for a repository that comes out
    /// the same each time. Copying the finished repository costs a few milliseconds. Each
    /// caller still owns its copy outright, so a test that commits, rebases or deletes
    /// cannot disturb the next one.</para>
    ///
    /// <para>What a copy cannot give is uniqueness: every copy holds the same commits, with
    /// the same ids and the same dates, made when the first test asked. A test that needs
    /// commits newer than its own start, or repositories that differ, builds them itself.</para>
    /// </summary>
    public static TempRepo CreateFrom(string template, Func<TempRepo> build)
    {
        TempRepo? source;
        lock (Templates)
        {
            if (!Templates.TryGetValue(template, out source))
            {
                source = build();
                Templates[template] = source;
                if (Templates.Count == 1)
                    AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeTemplates();
            }
        }

        var copy = new TempRepo(NewFixturePath());
        CopyTree(source.Path, copy.Path);
        return copy;
    }

    private static void DisposeTemplates()
    {
        lock (Templates)
        {
            foreach (var template in Templates.Values) template.Dispose();
            Templates.Clear();
        }
    }

    private static string NewFixturePath()
    {
        PrepareEnvironment();
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), FixturePrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CopyTree(string from, string to)
    {
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(System.IO.Path.Combine(to, System.IO.Path.GetRelativePath(from, dir)));
        // git's object files are read-only and stay so in the copy; ForceDelete copes.
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, System.IO.Path.Combine(to, System.IO.Path.GetRelativePath(from, file)));
    }

    /// <summary>Runs git in the repo and fails loudly — arranging a fixture must not
    /// half-succeed and leave a later assertion to explain why.</summary>
    public GitResult Git(params string[] args)
    {
        var result = GitProcess.Run(Path, args);
        if (!result.Success)
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} failed ({result.ExitCode}): {result.Stderr}{result.Stdout}");
        return result;
    }

    public void Write(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    /// <summary>Writes a file byte-for-byte. Binary fixtures have to go in this way: the
    /// text helper would put the platform's encoding between the bytes and the repository,
    /// and the NUL bytes are the whole point of the fixture.</summary>
    public void WriteBytes(string relativePath, byte[] content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
    }

    public string Read(string relativePath) =>
        File.ReadAllText(System.IO.Path.Combine(Path, relativePath));

    public string Head => Git("rev-parse", "HEAD").Stdout.Trim();

    private static void PrepareEnvironment()
    {
        lock (EnvLock)
        {
            if (_envPrepared) return;

            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "junction-test-gitconfig");
            Directory.CreateDirectory(dir);
            var empty = System.IO.Path.Combine(dir, "empty-gitconfig");
            if (!File.Exists(empty)) File.WriteAllText(empty, "");

            // Supported since git 2.32. Child processes inherit these.
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", empty);
            Environment.SetEnvironmentVariable("GIT_CONFIG_SYSTEM", empty);
            // Keeps a stale credential helper or a slow network prompt out of the run.
            Environment.SetEnvironmentVariable("GIT_TERMINAL_PROMPT", "0");
            // A fixture is a copy, so every file in it has a new time, and the first `git status`
            // the app runs on one — the sidebar's summary, the Changes view — finds the index's
            // stat data stale and writes the index back. The app's watcher cannot tell that
            // write from another program's: it reloads the repository 300 ms later, rebuilding
            // the window in the middle of whatever a test measures across frames (the "made
            // once" label test, the motion that builds nothing). Optional locks off, a read-only
            // git call writes nothing. Commands that have to write still take their locks.
            Environment.SetEnvironmentVariable("GIT_OPTIONAL_LOCKS", "0");
            _envPrepared = true;
        }
    }

    public void Dispose() => ForceDelete(Path);

    /// <summary>
    /// Removes a fixture, retrying briefly. Returns whether it is gone.
    ///
    /// <para>Two things fight the delete. git marks pack files read-only, so the attributes
    /// have to come off first; and the app under test watches its repository, so for a moment
    /// after it is disposed the watcher still holds a handle to the directory. Windows honours
    /// the file deletions and refuses the directory itself, which is how a single unretried
    /// delete left thousands of empty husks behind — every one of them reported as success,
    /// because the failure was swallowed.</para>
    ///
    /// <para>One case retrying cannot win: every git call runs with the repository as its
    /// working directory, and Windows pins a running process's current directory. The Changes
    /// view's loaders outlive the app that started them, so a <c>git status</c> can still be
    /// running when the fixture goes — which is why that one view leaves a husk and the
    /// others do not. Deleting the directory out from under it is the right thing to attempt;
    /// waiting for it is not this class's business.</para>
    ///
    /// <para>So this stays best-effort: a fixture that outlives its run is litter, not a
    /// broken test, and throwing here would fail a passing test because a virus scanner
    /// opened a file. <see cref="SweepStaleFixtures"/> is what stops litter accumulating.</para>
    ///
    /// <para>Internal rather than private because a test that pulls a worktree directory out
    /// from under a *running* app hits the same handle race deliberately — the app watches
    /// every repository in the workspace — and a bare <c>Directory.Delete</c> there fails
    /// intermittently for reasons that have nothing to do with what is under test.</para>
    /// </summary>
    internal static bool ForceDelete(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var dir = new DirectoryInfo(path);
                if (!dir.Exists) return true;
                foreach (var entry in dir.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                    if ((entry.Attributes & FileAttributes.ReadOnly) != 0)
                        entry.Attributes &= ~FileAttributes.ReadOnly;
                dir.Delete(true);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 6) return !Directory.Exists(path);
                // A watcher that was disposed releases its handle in milliseconds. One that
                // was only dropped releases it when it is finalized, which is why waiting
                // alone left roughly one husk per run — the directory's own handle outlives
                // the files, so the delete empties it and then fails.
                if (attempt == 2) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                Thread.Sleep(25 * (1 << attempt));   // 25ms doubling to 800ms, ~1.6s in all
            }
        }
    }

    /// <summary>
    /// Clears fixtures left behind by earlier runs, before this one adds its own.
    ///
    /// <para>Retrying makes a leak unlikely rather than impossible, and the cost of the
    /// remaining case compounds: the directories are cheap individually, but a week of them
    /// buried the temp folder under thousands. Sweeping at startup makes each run clean up
    /// after the last, so no single unlucky delete accumulates.</para>
    ///
    /// <para>Only fixtures older than an hour are touched. Test assemblies run as separate
    /// processes and can overlap, and deleting the repository another process is mid-test
    /// against would be a far worse bug than the litter this removes.</para>
    /// </summary>
    [ModuleInitializer]
    internal static void SweepStaleFixtures()
    {
        try
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromHours(1);
            foreach (var dir in Directory.EnumerateDirectories(System.IO.Path.GetTempPath(), FixturePrefix + "*"))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(dir) < cutoff) ForceDelete(dir);
                }
                catch { /* raced with another sweep, or gone already */ }
            }
        }
        catch { /* no temp folder to sweep is not a test failure */ }
    }
}
