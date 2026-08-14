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

    /// <summary>Creates an empty repository with one root commit already in it.</summary>
    public static TempRepo CreateWithInitialCommit()
    {
        var repo = CreateEmpty();
        repo.Write("README.md", "hello\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "initial");
        return repo;
    }

    public static TempRepo CreateEmpty()
    {
        PrepareEnvironment();

        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), FixturePrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        var repo = new TempRepo(path);
        repo.Git("init", "-b", "main");
        repo.Git("config", "user.name", "Junction Tests");
        repo.Git("config", "user.email", "tests@example.invalid");
        repo.Git("config", "commit.gpgsign", "false");
        repo.Git("config", "core.autocrlf", "false");
        return repo;
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
    /// </summary>
    private static bool ForceDelete(string path)
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
