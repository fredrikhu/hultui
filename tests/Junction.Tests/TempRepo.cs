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
            System.IO.Path.GetTempPath(), "junction-git-" + Guid.NewGuid().ToString("N"));
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

    public void Dispose()
    {
        // git marks pack files read-only; plain recursive delete trips over them.
        try { ForceDelete(new DirectoryInfo(Path)); } catch { /* best-effort temp cleanup */ }
    }

    private static void ForceDelete(DirectoryInfo dir)
    {
        if (!dir.Exists) return;
        foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
            file.Attributes = FileAttributes.Normal;
        dir.Delete(true);
    }
}
