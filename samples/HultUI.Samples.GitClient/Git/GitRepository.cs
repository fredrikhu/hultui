using System.Globalization;

namespace GitClient.Git;

/// <summary>
/// High-level interface to a git repository via the git CLI.
/// </summary>
public class GitRepository
{
    private const string FieldSep = "\x1f"; // ASCII Unit Separator
    private const string RecordSep = "\x1e"; // ASCII Record Separator

    public string Path { get; }

    public GitRepository(string path)
    {
        Path = path;
    }

    // ── Branches ──

    public List<GitBranch> GetBranches()
    {
        var result = GitProcess.Run(Path,
            "for-each-ref",
            "--format", $"%(refname:short){FieldSep}%(refname){FieldSep}%(HEAD){FieldSep}%(upstream:short){FieldSep}%(upstream:track,nobracket){FieldSep}%(if)%(*objectname)%(then)%(*objectname)%(else)%(objectname)%(end){FieldSep}%(contents:subject){FieldSep}%(committerdate:iso-strict)",
            "refs/heads/", "refs/remotes/");

        if (!result.Success) return [];

        var branches = new List<GitBranch>();
        foreach (var line in result.Lines)
        {
            var parts = line.Split(FieldSep);
            if (parts.Length < 6) continue;

            var name = parts[0];
            var fullName = parts[1];

            // Skip symbolic refs like refs/remotes/origin/HEAD
            if (fullName.EndsWith("/HEAD")) continue;

            var isCurrent = parts[2] == "*";
            var isRemote = fullName.StartsWith("refs/remotes/");
            var trackingBranch = string.IsNullOrEmpty(parts[3]) ? null : parts[3];

            var (ahead, behind) = ParseAheadBehind(parts[4]);

            var hash = parts[5];
            var subject = parts.Length > 6 ? parts[6] : "";
            DateTimeOffset commitDate = default;
            if (parts.Length > 7)
                DateTimeOffset.TryParse(parts[7], System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out commitDate);

            branches.Add(new GitBranch(name, fullName, isCurrent, isRemote, trackingBranch, ahead, behind, hash, subject, commitDate));
        }

        return branches;
    }

    public string GetCurrentBranch()
    {
        var result = GitProcess.Run(Path, "rev-parse", "--abbrev-ref", "HEAD");
        return result.Success ? result.Stdout.Trim() : "HEAD";
    }

    /// <summary>A short "version" label for at-a-glance display: the most recent
    /// reachable tag, or the short HEAD sha when the repo has no tags.</summary>
    public string GetDescribeVersion()
    {
        var tag = GitProcess.Run(Path, "describe", "--tags", "--abbrev=0");
        if (tag.Success && tag.Stdout.Trim().Length > 0) return tag.Stdout.Trim();
        var sha = GitProcess.Run(Path, "rev-parse", "--short", "HEAD");
        return sha.Success ? sha.Stdout.Trim() : "";
    }

    /// <summary>
    /// Resolves any commit-ish (hash, branch, tag, stash ref) into a <see cref="GitCommit"/>.
    /// Used to view stash entries through the same CommitDetail panel as regular commits —
    /// stashes are full commit objects, so this works without any special-casing downstream.
    /// </summary>
    public GitCommit? GetCommit(string refOrHash)
    {
        var format = string.Join(FieldSep, ["%H", "%h", "%an", "%ae", "%aI", "%s", "%P", "%D"]);
        var result = GitProcess.Run(Path, "log", "-1", $"--format={format}", refOrHash);
        if (!result.Success) return null;

        var line = result.Stdout.Trim();
        if (string.IsNullOrEmpty(line)) return null;

        var parts = line.Split(FieldSep);
        if (parts.Length < 8) return null;

        var parentHashes = string.IsNullOrWhiteSpace(parts[6])
            ? []
            : parts[6].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var refs = string.IsNullOrWhiteSpace(parts[7])
            ? []
            : parts[7].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (!DateTimeOffset.TryParse(parts[4], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            date = DateTimeOffset.MinValue;

        return new GitCommit(
            Hash: parts[0],
            ShortHash: parts[1],
            AuthorName: parts[2],
            AuthorEmail: parts[3],
            AuthorDate: date,
            Subject: parts[5],
            ParentHashes: parentHashes,
            Refs: refs
        );
    }

    // ── Log ──

    public List<GitCommit> GetLog(int maxCount = 500, string? author = null,
        string? path = null, DateTimeOffset? after = null, DateTimeOffset? before = null,
        bool followPath = false, IReadOnlyList<string>? includeRefs = null)
    {
        var format = string.Join(FieldSep, ["%H", "%h", "%an", "%ae", "%aI", "%s", "%P", "%D"]);

        var args = new List<string>
        {
            "log",
            $"--max-count={maxCount}",
            $"--format={format}{RecordSep}",
        };

        // --follow tracks history across renames but only works with a single file
        // and is incompatible with --all; use a linear, branch-scoped history instead.
        if (followPath && !string.IsNullOrWhiteSpace(path))
            args.Add("--follow");
        else if (includeRefs is { Count: > 0 })
            // "Include in history" — scope the graph to the reachable set of the
            // selected branches instead of every ref.
            args.AddRange(includeRefs);
        else
            args.Add("--all");

        if (!string.IsNullOrWhiteSpace(author))
            args.Add($"--author={author}");
        if (after.HasValue)
            args.Add($"--after={after.Value:yyyy-MM-dd}");
        if (before.HasValue)
            args.Add($"--before={before.Value:yyyy-MM-dd}");

        if (!string.IsNullOrWhiteSpace(path))
        {
            args.Add("--");
            args.Add(path);
        }

        var result = GitProcess.Run(Path, args.ToArray());

        if (!result.Success) return [];

        var commits = new List<GitCommit>();
        var records = result.Stdout.Split(RecordSep, StringSplitOptions.RemoveEmptyEntries);
        foreach (var record in records)
        {
            var line = record.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var parts = line.Split(FieldSep);
            if (parts.Length < 8) continue;

            var parentHashes = string.IsNullOrWhiteSpace(parts[6])
                ? []
                : parts[6].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var refs = string.IsNullOrWhiteSpace(parts[7])
                ? []
                : parts[7].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (!DateTimeOffset.TryParse(parts[4], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                date = DateTimeOffset.MinValue;

            commits.Add(new GitCommit(
                Hash: parts[0],
                ShortHash: parts[1],
                AuthorName: parts[2],
                AuthorEmail: parts[3],
                AuthorDate: date,
                Subject: parts[5],
                ParentHashes: parentHashes,
                Refs: refs
            ));
        }

        return commits;
    }

    /// <summary>
    /// Per-commit added/deleted line totals for the log, gathered in a single
    /// <c>git log --numstat</c> pass so the History rows can show +adds / −dels
    /// without a subprocess per commit. Binary files (numstat "-") count as 0.
    /// </summary>
    public Dictionary<string, (int Added, int Deleted)> GetLogStats(
        int maxCount = 500, IReadOnlyList<string>? refs = null)
    {
        var args = new List<string>
        {
            "log",
            $"--max-count={maxCount}",
            "--numstat",
            "--format=%x1e%H",   // record-separator (RS) + hash on its own line
        };
        if (refs is { Count: > 0 })
            args.AddRange(refs);
        else
            args.Add("--all");

        var result = GitProcess.Run(Path, args.ToArray());
        var stats = new Dictionary<string, (int, int)>();
        if (!result.Success) return stats;

        string? hash = null;
        int add = 0, del = 0;
        foreach (var raw in result.Stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line[0] == '\u001e')
            {
                if (hash != null) stats[hash] = (add, del);
                hash = line[1..].Trim();
                add = 0;
                del = 0;
                continue;
            }

            var cols = line.Split('\t');
            if (cols.Length >= 2)
            {
                if (int.TryParse(cols[0], out var a)) add += a;
                if (int.TryParse(cols[1], out var d)) del += d;
            }
        }
        if (hash != null) stats[hash] = (add, del);

        return stats;
    }

    // ── Status (working tree) ──

    public List<GitFileChange> GetStatus()
    {
        var result = GitProcess.Run(Path, "status", "--porcelain=v1", "-uall");
        if (!result.Success) return [];

        var changes = new List<GitFileChange>();
        foreach (var line in result.Lines)
        {
            if (line.Length < 4) continue;

            var xy = line[..2];
            var path = line[3..];

            string? oldPath = null;
            if (path.Contains(" -> "))
            {
                var parts = path.Split(" -> ", 2);
                oldPath = parts[0];
                path = parts[1];
            }

            var kind = ParseStatusCode(xy);
            changes.Add(new GitFileChange(path, oldPath, kind, 0, 0));
        }

        return changes;
    }

    // ── Diff ──

    public List<GitFileChange> GetCommitFiles(string commitHash)
    {
        // -m --first-parent: for merge commits (and stashes, which are merges
        // of HEAD/index/untracked), diff against the first parent instead of
        // suppressing output entirely. --root: the initial commit has no parent,
        // so without it diff-tree emits nothing — show it as a diff against the
        // empty tree (every file added). It's a no-op for non-root commits.
        return QueryFileChanges(
            ["diff-tree", "--no-commit-id", "-r", "--name-status", "-M", "-m", "--first-parent", "--root", commitHash],
            ["diff-tree", "--no-commit-id", "-r", "--numstat", "-M", "-m", "--first-parent", "--root", commitHash]);
    }

    public List<GitFileDiff> GetCommitDiff(string commitHash)
    {
        // --root so the initial (parentless) commit diffs against the empty tree.
        var result = GitProcess.Run(Path, "diff-tree", "--no-commit-id", "-p", "-M", "-m", "--first-parent", "--root", commitHash);
        return result.Success ? ParseUnifiedDiff(result.Stdout) : [];
    }

    /// <summary>File list of the tree diff between two arbitrary commits
    /// (Fork-style multi-select compare: base = oldest, head = newest).</summary>
    public List<GitFileChange> GetRangeFiles(string baseHash, string headHash)
    {
        return QueryFileChanges(
            ["diff", "--name-status", "-M", baseHash, headHash],
            ["diff", "--numstat", "-M", baseHash, headHash]);
    }

    /// <summary>Patch of the tree diff between two arbitrary commits.</summary>
    public List<GitFileDiff> GetRangeDiff(string baseHash, string headHash)
    {
        var result = GitProcess.Run(Path, "diff", "-M", baseHash, headHash);
        return result.Success ? ParseUnifiedDiff(result.Stdout) : [];
    }

    public List<GitFileDiff> GetWorkingDiff(bool staged, string? baseRef = null)
    {
        var args = staged
            ? baseRef != null
                ? new[] { "diff", "--cached", baseRef }
                : new[] { "diff", "--cached" }
            : new[] { "diff" };

        var result = GitProcess.Run(Path, args);
        return result.Success ? ParseUnifiedDiff(result.Stdout) : [];
    }

    public List<GitFileDiff> GetUntrackedFileDiff(string filePath)
    {
        var fullPath = System.IO.Path.Combine(Path, filePath);
        if (!File.Exists(fullPath)) return [];

        var lines = File.ReadAllLines(fullPath);
        var hunkLines = new List<GitDiffLine>
        {
            new(DiffLineKind.HunkHeader, $"@@ -0,0 +1,{lines.Length} @@", null, null),
        };

        for (var i = 0; i < lines.Length; i++)
            hunkLines.Add(new GitDiffLine(DiffLineKind.Addition, lines[i], null, i + 1));

        var diff = new GitFileDiff(filePath, null,
        [
            new GitDiffHunk($"@@ -0,0 +1,{lines.Length} @@", hunkLines),
        ])
        {
            OldPathHeader = "--- /dev/null",
            NewPathHeader = $"+++ b/{filePath}",
        };

        return [diff];
    }

    public string? GetFileText(string gitRef, string filePath)
    {
        var result = GitProcess.Run(Path, "show", $"{gitRef}:{filePath}");
        return result.Success ? result.Stdout : null;
    }

    public string? GetIndexFileText(string filePath)
    {
        var result = GitProcess.Run(Path, "show", $":{filePath}");
        return result.Success ? result.Stdout : null;
    }

    public string? GetWorkingFileText(string filePath)
    {
        var fullPath = System.IO.Path.Combine(Path, filePath);
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
    }

    /// <summary>
    /// Returns the staged file list diffed against a specific base ref.
    /// Used in amend mode to diff the index against HEAD~1 so that the
    /// previous commit's changes appear as staged.
    /// </summary>
    public List<GitFileChange> GetStagedFiles(string? baseRef = null)
    {
        var args = baseRef != null
            ? new[] { "diff", "--cached", "--name-status", "-M", baseRef }
            : new[] { "diff", "--cached", "--name-status", "-M" };

        var result = GitProcess.Run(Path, args);
        if (!result.Success) return [];

        var files = new List<GitFileChange>();
        foreach (var line in result.Lines)
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;

            var status = parts[0];
            var kind = status[0] switch
            {
                'A' => FileChangeKind.Added,
                'M' => FileChangeKind.Modified,
                'D' => FileChangeKind.Deleted,
                'R' => FileChangeKind.Renamed,
                'C' => FileChangeKind.Copied,
                _ => FileChangeKind.Modified,
            };

            // Renames/copies have 3 columns: status \t oldPath \t newPath
            var path = parts.Length >= 3 ? parts[2] : parts[1];
            var oldPath = parts.Length >= 3 ? parts[1] : null;

            files.Add(new GitFileChange(path, oldPath, kind, 0, 0));
        }
        return files;
    }

    /// <summary>Staged files (index vs HEAD) with per-file added/deleted counts.</summary>
    public List<GitFileChange> GetStagedChanges() => GetChangeList(cached: true, includeUntracked: false);

    /// <summary>Unstaged files (working tree vs index) with counts, plus untracked files.</summary>
    public List<GitFileChange> GetUnstagedChanges() => GetChangeList(cached: false, includeUntracked: true);

    private List<GitFileChange> GetChangeList(bool cached, bool includeUntracked)
    {
        string[] statusArgs = cached
            ? ["diff", "--cached", "--name-status", "-M"]
            : ["diff", "--name-status", "-M"];
        string[] numArgs = cached
            ? ["diff", "--cached", "--numstat", "-M"]
            : ["diff", "--numstat", "-M"];

        var list = QueryFileChanges(statusArgs, numArgs);

        if (includeUntracked)
        {
            foreach (var line in GitProcess.Run(Path, "status", "--porcelain=v1", "-uall").Lines)
                if (line.Length >= 4 && line[0] == '?' && line[1] == '?')
                    list.Add(new GitFileChange(line[3..], null, FileChangeKind.Untracked, 0, 0));
        }

        return list;
    }

    /// <summary>
    /// Builds a file-change list by joining <c>--name-status</c> letters (the
    /// authoritative change kind) with <c>--numstat</c> counts, keyed by new path.
    /// Numstat alone can't distinguish a purely-additive modification of an
    /// existing file from a newly added one.
    /// </summary>
    private List<GitFileChange> QueryFileChanges(string[] statusArgs, string[] numstatArgs)
    {
        var counts = new Dictionary<string, (int Add, int Del)>(StringComparer.Ordinal);
        foreach (var line in GitProcess.Run(Path, numstatArgs).Lines)
        {
            var parts = line.Split('\t');
            if (parts.Length < 3) continue;
            int.TryParse(parts[0], out var a);
            int.TryParse(parts[1], out var d);
            counts[NormalizeRenamePath(parts[2])] = (a, d);
        }

        var list = new List<GitFileChange>();
        foreach (var line in GitProcess.Run(Path, statusArgs).Lines)
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;

            var code = parts[0];
            string path;
            string? oldPath = null;
            FileChangeKind kind;
            if (code.StartsWith('R') || code.StartsWith('C'))
            {
                kind = code[0] == 'R' ? FileChangeKind.Renamed : FileChangeKind.Copied;
                oldPath = parts[1];
                path = parts.Length >= 3 ? parts[2] : parts[1];
            }
            else
            {
                path = parts[1];
                kind = code[0] switch
                {
                    'A' => FileChangeKind.Added,
                    'M' => FileChangeKind.Modified,
                    'D' => FileChangeKind.Deleted,
                    _ => FileChangeKind.Modified,
                };
            }

            var (add, del) = counts.TryGetValue(path, out var c) ? c : (0, 0);
            list.Add(new GitFileChange(path, oldPath, kind, add, del));
        }
        return list;
    }

    // numstat renders renames as "old => new" or "pre/{old => new}/post"; reduce
    // to the new path so counts key the same way name-status does.
    private static string NormalizeRenamePath(string p)
    {
        if (!p.Contains(" => ", StringComparison.Ordinal)) return p;
        var open = p.IndexOf('{');
        var close = p.IndexOf('}');
        if (open >= 0 && close > open)
        {
            var inner = p[(open + 1)..close];
            var arrow = inner.IndexOf(" => ", StringComparison.Ordinal);
            var newInner = arrow >= 0 ? inner[(arrow + 4)..] : inner;
            return p[..open] + newInner + p[(close + 1)..];
        }
        var idx = p.IndexOf(" => ", StringComparison.Ordinal);
        return p[(idx + 4)..];
    }

    // ── Staging ──

    public bool Stage(string filePath)
    {
        return GitProcess.Run(Path, "add", "--", filePath).Success;
    }

    public bool Unstage(string filePath)
    {
        return GitProcess.Run(Path, "restore", "--staged", "--", filePath).Success;
    }

    public bool StageAll()
    {
        return GitProcess.Run(Path, "add", "-A").Success;
    }

    public bool UnstageAll()
    {
        return GitProcess.Run(Path, "reset", "HEAD").Success;
    }

    /// <summary>
    /// Discard worktree changes for a file. For untracked files this removes
    /// the file via `git clean`; for tracked files it restores the worktree
    /// copy from HEAD via `git restore`.
    /// </summary>
    public bool DiscardChanges(GitFileChange file)
    {
        if (file.Kind == FileChangeKind.Untracked)
        {
            return GitProcess.Run(Path, "clean", "-f", "--", file.Path).Success;
        }
        return GitProcess.Run(Path, "restore", "--worktree", "--", file.Path).Success;
    }

    // ── Commit ──

    public GitResult Commit(string message)
    {
        return GitProcess.Run(Path, "commit", "-m", message);
    }

    public GitResult CommitAmend(string message)
    {
        return GitProcess.Run(Path, "commit", "--amend", "-m", message);
    }

    public string? GetLastCommitMessage()
    {
        var result = GitProcess.Run(Path, "log", "-1", "--format=%B");
        return result.Success ? result.Stdout.TrimEnd() : null;
    }

    // ── Remote operations ──

    public List<string> GetRemotes()
    {
        var result = GitProcess.Run(Path, "remote");
        return result.Success ? result.Lines.ToList() : [];
    }

    /// <summary>
    /// Returns the remote-tracking branches for the given remote,
    /// e.g. ["main", "feature/foo"] for remote "origin".
    /// </summary>
    public List<string> GetRemoteBranches(string remote)
    {
        var result = GitProcess.Run(Path, "branch", "-r", "--list", $"{remote}/*", "--format", "%(refname:lstrip=3)");
        if (!result.Success) return [];
        return result.Lines
            .Where(b => b != "HEAD")
            .ToList();
    }

    public GitResult Fetch(bool prune = true)
    {
        var args = new List<string> { "fetch", "--all" };
        if (prune) args.Add("--prune");
        return GitProcess.Run(Path, args.ToArray());
    }

    public Task<GitResult> FetchAsync(Func<string, Task<string?>>? askPass = null, bool prune = true)
    {
        var args = new List<string> { "fetch", "--all" };
        if (prune) args.Add("--prune");
        return GitProcess.RunAsync(Path, askPass, args.ToArray());
    }

    public GitResult Pull(PullOptions? options = null)
    {
        return GitProcess.Run(Path, BuildPullArgs(options).ToArray());
    }

    public Task<GitResult> PullAsync(Func<string, Task<string?>>? askPass = null, PullOptions? options = null)
    {
        return GitProcess.RunAsync(Path, askPass, BuildPullArgs(options).ToArray());
    }

    public Task<GitResult> PullFromAsync(string remote, string branch, Func<string, Task<string?>>? askPass = null, PullOptions? options = null)
    {
        var args = BuildPullArgs(options);
        args.Add(remote);
        args.Add(branch);
        return GitProcess.RunAsync(Path, askPass, args.ToArray());
    }

    /// <summary>Fast-forwards a *non-current* local branch to its upstream without
    /// checking it out: <c>git fetch &lt;remote&gt; &lt;remoteBranch&gt;:&lt;localBranch&gt;</c>.
    /// Git rejects the update (non-zero exit) if it isn't a fast-forward, so this is
    /// safe. Use <see cref="PullAsync"/> for the checked-out branch instead.</summary>
    public Task<GitResult> FastForwardBranchAsync(string remote, string remoteBranch, string localBranch,
        Func<string, Task<string?>>? askPass = null)
        => GitProcess.RunAsync(Path, askPass, "fetch", remote, $"{remoteBranch}:{localBranch}");

    private static List<string> BuildPullArgs(PullOptions? options)
    {
        options ??= new PullOptions();

        var args = new List<string> { "pull" };
        switch (options.Strategy)
        {
            case PullStrategy.Rebase:
                args.Add("--rebase");
                break;
            case PullStrategy.FastForwardOnly:
                args.Add("--ff-only");
                break;
        }

        if (options.AutoStash)
            args.Add("--autostash");

        if (options.Prune)
            args.Add("--prune");

        return args;
    }

    public GitResult Push()
    {
        return GitProcess.Run(Path, "push");
    }

    public Task<GitResult> PushAsync(Func<string, Task<string?>>? askPass = null)
    {
        return GitProcess.RunAsync(Path, askPass, "push");
    }

    public Task<GitResult> PushToAsync(string remote, string branch, Func<string, Task<string?>>? askPass = null)
    {
        return GitProcess.RunAsync(Path, askPass, "push", remote, branch);
    }

    public Task<GitResult> ForcePushAsync(Func<string, Task<string?>>? askPass = null)
    {
        return GitProcess.RunAsync(Path, askPass, "push", "--force-with-lease");
    }

    // ── Branch management ──

    public GitResult CreateBranch(string name, string? startPoint = null)
    {
        return startPoint != null
            ? GitProcess.Run(Path, "branch", name, startPoint)
            : GitProcess.Run(Path, "branch", name);
    }

    public GitResult DeleteBranch(string name, bool force = false)
    {
        return GitProcess.Run(Path, "branch", force ? "-D" : "-d", name);
    }

    public GitResult SetUpstream(string branch, string upstream)
    {
        return GitProcess.Run(Path, "branch", $"--set-upstream-to={upstream}", branch);
    }

    public GitResult RenameBranch(string oldName, string newName)
    {
        return GitProcess.Run(Path, "branch", "-m", oldName, newName);
    }

    // ── Checkout ──

    public GitResult Checkout(string branchName)
    {
        return GitProcess.Run(Path, "checkout", branchName);
    }

    public GitResult CheckoutDetached(string commitHash)
    {
        return GitProcess.Run(Path, "checkout", "--detach", commitHash);
    }

    // ── Merge ──

    public GitResult Merge(string branchName, MergeMode mode = MergeMode.Default)
    {
        var args = new List<string> { "merge" };
        switch (mode)
        {
            case MergeMode.NoFastForward: args.Add("--no-ff"); break;
            case MergeMode.FastForwardOnly: args.Add("--ff-only"); break;
            case MergeMode.Squash: args.Add("--squash"); break;
        }
        args.Add(branchName);
        return GitProcess.Run(Path, args.ToArray());
    }

    public GitResult MergeAbort()
    {
        return GitProcess.Run(Path, "merge", "--abort");
    }

    /// <summary>
    /// Finalises an in-progress merge once every conflict is resolved (staged),
    /// creating the merge commit from the prepared MERGE_MSG. Fails if any path is
    /// still unmerged — callers should only offer this when there are no conflicts
    /// left.
    /// </summary>
    public GitResult MergeContinue()
    {
        return GitProcess.Run(Path, "commit", "--no-edit");
    }

    /// <summary>
    /// The name of the branch being merged in ("theirs"), parsed from the prepared
    /// MERGE_MSG (e.g. <c>Merge branch 'feature'</c>). Returns null when the merge
    /// isn't of a named branch (e.g. a raw commit) or MERGE_MSG is absent.
    /// </summary>
    public string? GetMergingBranchName()
    {
        var msgPath = System.IO.Path.Combine(Path, ".git", "MERGE_MSG");
        if (!File.Exists(msgPath)) return null;
        string first;
        try { first = File.ReadLines(msgPath).FirstOrDefault() ?? ""; }
        catch (IOException) { return null; }

        // "Merge branch 'x'", "Merge remote-tracking branch 'origin/x'",
        // "Merge branches 'x' and 'y'" — take the first quoted name.
        int open = first.IndexOf('\'');
        if (open < 0) return null;
        int close = first.IndexOf('\'', open + 1);
        return close > open ? first[(open + 1)..close] : null;
    }

    /// <summary>
    /// Returns true if the repository is in a conflicted merge state
    /// (MERGE_HEAD exists).
    /// </summary>
    public bool IsMerging()
    {
        return File.Exists(System.IO.Path.Combine(Path, ".git", "MERGE_HEAD"));
    }

    /// <summary>
    /// Returns the list of currently conflicted file paths.
    /// </summary>
    public List<GitFileChange> GetConflictedFiles()
    {
        return GetStatus().Where(f => f.Kind == FileChangeKind.Conflicted).ToList();
    }

    /// <summary>
    /// Resolves a conflicted file by choosing "ours" (the current branch's version).
    /// </summary>
    public GitResult CheckoutOurs(string path)
    {
        var result = GitProcess.Run(Path, "checkout", "--ours", "--", path);
        if (result.Success) Stage(path);
        return result;
    }

    /// <summary>
    /// Resolves a conflicted file by choosing "theirs" (the incoming branch's version).
    /// </summary>
    public GitResult CheckoutTheirs(string path)
    {
        var result = GitProcess.Run(Path, "checkout", "--theirs", "--", path);
        if (result.Success) Stage(path);
        return result;
    }

    /// <summary>
    /// Returns a diff between "ours" (stage 2) and "theirs" (stage 3) for a
    /// conflicted file, suitable for display in the diff viewer.
    /// </summary>
    public List<GitFileDiff> GetConflictDiff(string path)
    {
        // Write ours and theirs to temp files, diff them
        var oursResult = GitProcess.Run(Path, "show", $":2:{path}");
        var theirsResult = GitProcess.Run(Path, "show", $":3:{path}");
        if (!oursResult.Success || !theirsResult.Success) return [];

        // Use git diff --no-index to generate a unified diff
        var oursFile = System.IO.Path.GetTempFileName();
        var theirsFile = System.IO.Path.GetTempFileName();
        try
        {
            File.WriteAllText(oursFile, oursResult.Stdout);
            File.WriteAllText(theirsFile, theirsResult.Stdout);

            // --no-index exits 1 when files differ (not an error)
            var diffResult = GitProcess.Run(Path, "diff", "--no-index",
                $"--src-prefix=a/", $"--dst-prefix=b/",
                oursFile, theirsFile);
            var diffs = ParseUnifiedDiff(diffResult.Stdout);

            // Fix up the paths (temp file names → original path)
            foreach (var d in diffs)
            {
                // Use reflection-free approach: create new record
                var fixedHunks = d.Hunks;
                var fixed_ = new GitFileDiff(path, path, fixedHunks)
                {
                    OldPathHeader = $"--- a/{path} (ours)",
                    NewPathHeader = $"+++ b/{path} (theirs)",
                };
                diffs[diffs.IndexOf(d)] = fixed_;
            }
            return diffs;
        }
        finally
        {
            File.Delete(oursFile);
            File.Delete(theirsFile);
        }
    }

    // ── Rebase ──

    public GitResult Rebase(string onto)
    {
        return GitProcess.Run(Path, "rebase", onto);
    }

    public GitResult RebaseAbort()
    {
        return GitProcess.Run(Path, "rebase", "--abort");
    }

    public GitResult RebaseContinue()
    {
        return GitProcess.Run(Path, "rebase", "--continue");
    }

    /// <summary>
    /// Returns the commits reachable from HEAD but not from <paramref name="baseRef"/>,
    /// in the order interactive rebase expects them (oldest first). This is the same
    /// range git would fill into the rebase-todo file.
    /// </summary>
    public List<GitCommit> GetRebaseRange(string baseRef)
    {
        var format = string.Join(FieldSep, ["%H", "%h", "%an", "%ae", "%aI", "%s", "%P", "%D"]);
        var result = GitProcess.Run(Path, "log",
            $"--format={format}{RecordSep}", "--reverse", $"{baseRef}..HEAD");

        if (!result.Success) return [];

        var commits = new List<GitCommit>();
        foreach (var record in result.Stdout.Split(RecordSep, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = record.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(FieldSep);
            if (parts.Length < 8) continue;

            var parentHashes = string.IsNullOrWhiteSpace(parts[6])
                ? []
                : parts[6].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (!DateTimeOffset.TryParse(parts[4], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                date = DateTimeOffset.MinValue;

            commits.Add(new GitCommit(
                Hash: parts[0], ShortHash: parts[1],
                AuthorName: parts[2], AuthorEmail: parts[3],
                AuthorDate: date, Subject: parts[5],
                ParentHashes: parentHashes, Refs: []));
        }
        return commits;
    }

    /// <summary>
    /// Starts an interactive rebase onto <paramref name="baseRef"/> using the supplied plan.
    /// Works by writing the todo list to a temp file and setting <c>GIT_SEQUENCE_EDITOR</c>
    /// to a <c>cp</c> command that overwrites git's auto-generated todo with ours. Squash
    /// and fixup combine messages without interactive editing (<c>GIT_EDITOR=true</c>
    /// accepts the default). Conflicts pause the rebase as usual — use RebaseContinue/Abort.
    /// </summary>
    public GitResult InteractiveRebase(string baseRef, IReadOnlyList<RebaseEntry> plan)
    {
        // Drop entries are simply omitted from the todo — writing "drop <hash>" also works,
        // but leaving them out is just as effective and keeps the todo file shorter.
        //
        // Reword: emit a "pick" followed by an "exec git commit --amend -m <msg>" line.
        // This avoids needing to intercept $GIT_EDITOR to supply a new message mid-rebase —
        // the amend happens immediately after git replays the commit.
        var lines = new List<string>();
        foreach (var e in plan)
        {
            if (e.Op == RebaseOp.Drop) continue;
            lines.Add($"{e.Op.ToKeyword()} {e.Hash} {e.Subject}");
            if (e.Op == RebaseOp.Reword)
            {
                var msg = string.IsNullOrWhiteSpace(e.NewMessage) ? e.Subject : e.NewMessage!;
                lines.Add($"exec git commit --amend -m {ShellQuote(msg)}");
            }
        }

        var todoPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"hultui-rebase-todo-{Guid.NewGuid():N}");
        File.WriteAllText(todoPath, string.Join('\n', lines) + '\n');

        try
        {
            var env = new Dictionary<string, string>
            {
                // git invokes "$GIT_SEQUENCE_EDITOR <todo-file>" via a shell — `cp src dst`
                // ends up overwriting git's generated todo with ours.
                ["GIT_SEQUENCE_EDITOR"] = $"cp {ShellQuote(todoPath)}",
                // Squash/fixup would otherwise open $GIT_EDITOR for the combined message.
                // `true` is a no-op that accepts whatever default git prepared.
                ["GIT_EDITOR"] = "true",
            };
            return GitProcess.RunWithEnv(Path, env, "rebase", "-i", baseRef);
        }
        finally
        {
            try { File.Delete(todoPath); } catch { /* best-effort cleanup */ }
        }
    }

    private static string ShellQuote(string path) =>
        "'" + path.Replace("'", "'\\''") + "'";

    /// <summary>
    /// Returns true if a rebase is in progress.
    /// </summary>
    public bool IsRebasing()
    {
        return Directory.Exists(System.IO.Path.Combine(Path, ".git", "rebase-merge"))
            || Directory.Exists(System.IO.Path.Combine(Path, ".git", "rebase-apply"));
    }

    // ── Cherry-pick ──

    /// <summary>Cherry-picks one or more commits; pass them oldest first so
    /// they apply in history order.</summary>
    public GitResult CherryPick(params string[] commitHashes)
    {
        return GitProcess.Run(Path, ["cherry-pick", .. commitHashes]);
    }

    // ── Patch export ──

    /// <summary>
    /// Am-compatible patch text (git format-patch: full authorship + message per
    /// commit). Null <paramref name="baseExclusive"/> → just <paramref name="head"/>'s
    /// own patch; otherwise every commit after <paramref name="baseExclusive"/> up to
    /// and including <paramref name="head"/>. Set <paramref name="fromRoot"/> when the
    /// span starts at the parentless initial commit — <c>root^</c> doesn't exist, so
    /// git needs <c>--root</c> to emit patches all the way back to the first commit.
    /// Returns null on failure (e.g. an unreachable range). Merge commits are skipped.
    /// </summary>
    public string? FormatPatch(string? baseExclusive, string head, bool fromRoot = false)
    {
        GitResult result;
        if (fromRoot)
            result = GitProcess.Run(Path, "format-patch", "--root", "--stdout", head);
        else if (baseExclusive == null)
            result = GitProcess.Run(Path, "format-patch", "-1", "--stdout", head);
        else
            result = GitProcess.Run(Path, "format-patch", "--stdout", $"{baseExclusive}..{head}");
        return result.Success ? result.Stdout : null;
    }

    // ── Revert ──

    public GitResult Revert(string commitHash)
    {
        return GitProcess.Run(Path, "revert", "--no-edit", commitHash);
    }

    // ── Reset ──

    public GitResult Reset(string commitHash, ResetMode mode)
    {
        var flag = mode switch
        {
            ResetMode.Soft => "--soft",
            ResetMode.Hard => "--hard",
            _ => "--mixed",
        };
        return GitProcess.Run(Path, "reset", flag, commitHash);
    }

    // ── Blame ──

    /// <summary>
    /// Runs <c>git blame --porcelain</c> on a file at the optional commit-ish, returning
    /// one <see cref="GitBlameLine"/> per line of the final file. Commit metadata is only
    /// emitted once per hash in porcelain output, so we accumulate it in a dictionary as
    /// we parse and look it up for subsequent lines from the same commit.
    /// </summary>
    public List<GitBlameLine> Blame(string filePath, string? commitish = null)
    {
        var args = new List<string> { "blame", "--porcelain" };
        if (!string.IsNullOrEmpty(commitish)) args.Add(commitish);
        args.Add("--");
        args.Add(filePath);

        var result = GitProcess.Run(Path, args.ToArray());
        if (!result.Success) return [];

        var commits = new Dictionary<string, (string Author, DateTimeOffset Date, string Summary)>();
        var lines = new List<GitBlameLine>();

        string? pendingHash = null;
        int pendingFinalLine = 0;
        string? pendingAuthor = null;
        long? pendingAuthorTime = null;
        string? pendingSummary = null;

        foreach (var raw in result.Stdout.Split('\n'))
        {
            if (raw.StartsWith('\t'))
            {
                // Content line — finalize the pending entry
                if (pendingHash is null) continue;

                if (!commits.TryGetValue(pendingHash, out var meta))
                {
                    meta = (
                        pendingAuthor ?? "",
                        pendingAuthorTime.HasValue
                            ? DateTimeOffset.FromUnixTimeSeconds(pendingAuthorTime.Value)
                            : DateTimeOffset.MinValue,
                        pendingSummary ?? ""
                    );
                    commits[pendingHash] = meta;
                }

                lines.Add(new GitBlameLine(
                    CommitHash: pendingHash,
                    ShortHash: pendingHash.Length >= 7 ? pendingHash[..7] : pendingHash,
                    AuthorName: meta.Author,
                    AuthorDate: meta.Date,
                    Summary: meta.Summary,
                    LineNumber: pendingFinalLine,
                    Content: raw[1..]
                ));

                pendingHash = null;
                pendingAuthor = null;
                pendingAuthorTime = null;
                pendingSummary = null;
            }
            else if (raw.Length > 40 && raw[40] == ' ' && IsHex(raw[0]))
            {
                // Header: <40-char hash> <orig> <final> [<count>]
                var parts = raw.Split(' ');
                if (parts.Length >= 3)
                {
                    pendingHash = parts[0];
                    int.TryParse(parts[2], out pendingFinalLine);
                }
            }
            else if (raw.StartsWith("author "))
            {
                pendingAuthor = raw[7..];
            }
            else if (raw.StartsWith("author-time "))
            {
                if (long.TryParse(raw[12..], out var t)) pendingAuthorTime = t;
            }
            else if (raw.StartsWith("summary "))
            {
                pendingSummary = raw[8..];
            }
        }

        return lines;
    }

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    // ── Tags ──

    public List<string> GetTags()
    {
        var result = GitProcess.Run(Path, "tag", "--sort=-creatordate");
        return result.Success ? [.. result.Lines] : [];
    }

    /// <summary>
    /// Creates a tag. If <paramref name="message"/> is non-empty, an annotated
    /// tag is created (`git tag -a -m`); otherwise a lightweight tag.
    /// </summary>
    public GitResult CreateTag(string name, string? commit = null, string? message = null)
    {
        var args = new List<string> { "tag" };
        if (!string.IsNullOrWhiteSpace(message))
        {
            args.Add("-a");
            args.Add("-m");
            args.Add(message);
        }
        args.Add(name);
        if (!string.IsNullOrWhiteSpace(commit)) args.Add(commit);
        return GitProcess.Run(Path, args.ToArray());
    }

    public GitResult DeleteTag(string name)
    {
        return GitProcess.Run(Path, "tag", "-d", name);
    }

    // ── Stashes ──

    public List<GitStash> GetStashes()
    {
        // %gd → "stash@{0}", %gs → "WIP on main: c8b05ce subject"
        var result = GitProcess.Run(Path, "stash", "list",
            $"--format=%gd{FieldSep}%gs");
        if (!result.Success) return [];

        var stashes = new List<GitStash>();
        foreach (var line in result.Lines)
        {
            var parts = line.Split(FieldSep);
            if (parts.Length < 2) continue;

            var stashRef = parts[0];
            var raw = parts[1];

            // raw is "WIP on <branch>: <subject>" or "On <branch>: <subject>"
            var colon = raw.IndexOf(':');
            string branch = "", subject = raw;
            if (colon >= 0)
            {
                var head = raw[..colon];
                subject = raw[(colon + 1)..].TrimStart();
                branch = head.StartsWith("WIP on ") ? head[7..]
                    : head.StartsWith("On ") ? head[3..]
                    : head;
            }

            stashes.Add(new GitStash(stashRef, branch, subject));
        }
        return stashes;
    }

    public GitResult StashCreate(string? message = null, bool includeUntracked = true)
    {
        var args = new List<string> { "stash", "push" };
        if (includeUntracked) args.Add("--include-untracked");
        if (!string.IsNullOrWhiteSpace(message)) { args.Add("-m"); args.Add(message); }
        return GitProcess.Run(Path, args.ToArray());
    }

    public GitResult StashApply(string stashRef)
    {
        return GitProcess.Run(Path, "stash", "apply", stashRef);
    }

    public GitResult StashPop(string stashRef)
    {
        return GitProcess.Run(Path, "stash", "pop", stashRef);
    }

    public GitResult StashDrop(string stashRef)
    {
        return GitProcess.Run(Path, "stash", "drop", stashRef);
    }

    // ── Bisect ──

    public bool IsBisecting()
    {
        return File.Exists(System.IO.Path.Combine(Path, ".git", "BISECT_START"));
    }

    public GitResult BisectStart(string badRef, string goodRef)
    {
        return GitProcess.Run(Path, "bisect", "start", badRef, goodRef);
    }

    public GitResult BisectGood()
    {
        return GitProcess.Run(Path, "bisect", "good");
    }

    public GitResult BisectBad()
    {
        return GitProcess.Run(Path, "bisect", "bad");
    }

    public GitResult BisectSkip()
    {
        return GitProcess.Run(Path, "bisect", "skip");
    }

    public GitResult BisectReset()
    {
        return GitProcess.Run(Path, "bisect", "reset");
    }

    /// <summary>
    /// Returns the bisect log showing the history of good/bad marks and
    /// remaining range. Returns null if not bisecting.
    /// </summary>
    public string? GetBisectLog()
    {
        if (!IsBisecting()) return null;
        var result = GitProcess.Run(Path, "bisect", "log");
        return result.Success ? result.Stdout.Trim() : null;
    }

    // ── Submodules ──

    public List<GitSubmodule> GetSubmodules()
    {
        // `git submodule status` shells out to a slow helper (~400ms on Windows)
        // even when there are no submodules. Skip it entirely unless a
        // .gitmodules file exists — a cheap filesystem check.
        if (!File.Exists(System.IO.Path.Combine(Path, ".gitmodules")))
            return [];

        var result = GitProcess.Run(Path, "submodule", "status");
        if (!result.Success) return [];

        var submodules = new List<GitSubmodule>();
        foreach (var line in result.Lines)
        {
            if (line.Length < 2) continue;
            // Format: " <hash> <path> (<describe>)" or "+<hash> <path> (<describe>)" for dirty
            var status = line[0];
            var rest = line[1..].TrimStart();
            var parts = rest.Split(' ', 2);
            if (parts.Length < 2) continue;

            var hash = parts[0];
            var pathAndDesc = parts[1];
            var parenIdx = pathAndDesc.IndexOf(" (");
            var path = parenIdx >= 0 ? pathAndDesc[..parenIdx] : pathAndDesc.Trim();
            var describe = parenIdx >= 0 ? pathAndDesc[(parenIdx + 2)..].TrimEnd(')') : null;

            submodules.Add(new GitSubmodule(
                path,
                hash,
                describe,
                Dirty: status == '+',
                Uninitialized: status == '-'
            ));
        }
        return submodules;
    }

    // ── Patch application ──

    /// <summary>
    /// Applies a unified diff patch via stdin to <c>git apply</c>.
    /// <paramref name="cached"/> targets the index (stage/unstage).
    /// <paramref name="reverse"/> inverts the patch (unstage or discard).
    /// </summary>
    public GitResult ApplyPatch(string patch, bool reverse = false, bool cached = false)
    {
        var args = new List<string> { "apply" };
        if (cached) args.Add("--cached");
        if (reverse) args.Add("--reverse");
        args.Add("--unidiff-zero"); // allow zero-context hunks from single-hunk extraction
        args.Add("-");
        return GitProcess.RunWithStdin(Path, patch, args.ToArray());
    }

    /// <summary>
    /// Reconstructs a minimal valid unified diff patch for a single hunk,
    /// suitable for piping to <c>git apply</c>.
    /// </summary>
    public static string BuildHunkPatch(GitFileDiff file, GitDiffHunk hunk)
    {
        // Use \n explicitly — git patches must use LF regardless of platform
        var sb = new System.Text.StringBuilder();
        sb.Append($"diff --git a/{file.Path} b/{file.Path}\n");
        sb.Append($"{file.OldPathHeader ?? $"--- a/{file.Path}"}\n");
        sb.Append($"{file.NewPathHeader ?? $"+++ b/{file.Path}"}\n");

        sb.Append($"{hunk.Header}\n");
        foreach (var line in hunk.Lines)
        {
            if (line.Kind == DiffLineKind.HunkHeader) continue;
            var prefix = line.Kind switch
            {
                DiffLineKind.Addition => "+",
                DiffLineKind.Deletion => "-",
                _ => " ",
            };
            sb.Append($"{prefix}{line.Content}\n");
        }

        return sb.ToString();
    }

    // ── Parsing helpers ──

    private static List<GitFileDiff> ParseUnifiedDiff(string diffText)
    {
        var diffs = new List<GitFileDiff>();
        GitFileDiff? currentFile = null;
        GitDiffHunk? currentHunk = null;
        int oldLine = 0, newLine = 0;

        foreach (var rawLine in diffText.Split('\n'))
        {
            if (rawLine.StartsWith("diff --git "))
            {
                var parts = rawLine.Split(" b/", 2);
                var path = parts.Length > 1 ? parts[1] : rawLine;
                currentFile = new GitFileDiff(path, null, []);
                diffs.Add(currentFile);
                currentHunk = null;
            }
            else if (rawLine.StartsWith("--- ") && currentFile != null && currentHunk == null)
            {
                currentFile.OldPathHeader = rawLine;
            }
            else if (rawLine.StartsWith("+++ ") && currentFile != null && currentHunk == null)
            {
                currentFile.NewPathHeader = rawLine;
            }
            else if (rawLine.StartsWith("@@ ") && currentFile != null)
            {
                // Parse hunk header: @@ -oldStart,oldCount +newStart,newCount @@
                var header = rawLine;
                currentHunk = new GitDiffHunk(header, []);
                currentFile.Hunks.Add(currentHunk);

                ParseHunkHeader(rawLine, out oldLine, out newLine);

                currentHunk.Lines.Add(new GitDiffLine(DiffLineKind.HunkHeader, rawLine, null, null));
            }
            else if (currentHunk != null)
            {
                if (rawLine.StartsWith('+'))
                {
                    currentHunk.Lines.Add(new GitDiffLine(DiffLineKind.Addition, rawLine[1..], null, newLine));
                    newLine++;
                }
                else if (rawLine.StartsWith('-'))
                {
                    currentHunk.Lines.Add(new GitDiffLine(DiffLineKind.Deletion, rawLine[1..], oldLine, null));
                    oldLine++;
                }
                else if (rawLine.StartsWith(' ') || rawLine.Length == 0)
                {
                    var content = rawLine.Length > 0 ? rawLine[1..] : "";
                    currentHunk.Lines.Add(new GitDiffLine(DiffLineKind.Context, content, oldLine, newLine));
                    oldLine++;
                    newLine++;
                }
            }
        }

        return diffs;
    }

    private static void ParseHunkHeader(string header, out int oldStart, out int newStart)
    {
        oldStart = 1;
        newStart = 1;

        // @@ -10,6 +10,8 @@ optional section heading
        var atEnd = header.IndexOf("@@", 2);
        if (atEnd < 0) return;

        var range = header[3..atEnd].Trim();
        var parts = range.Split(' ');
        if (parts.Length >= 1 && parts[0].StartsWith('-'))
        {
            var oldParts = parts[0][1..].Split(',');
            int.TryParse(oldParts[0], out oldStart);
        }
        if (parts.Length >= 2 && parts[1].StartsWith('+'))
        {
            var newParts = parts[1][1..].Split(',');
            int.TryParse(newParts[0], out newStart);
        }
    }

    private static FileChangeKind ParseStatusCode(string xy)
    {
        var index = xy[0];
        var worktree = xy[1];

        // Unmerged (conflict) entries: UU, AA, DD, AU, UA, DU, UD
        if (index == 'U' || worktree == 'U' || (index == 'A' && worktree == 'A') || (index == 'D' && worktree == 'D'))
            return FileChangeKind.Conflicted;

        // Use the most specific status
        var c = index != ' ' ? index : worktree;
        return c switch
        {
            'A' => FileChangeKind.Added,
            'M' => FileChangeKind.Modified,
            'D' => FileChangeKind.Deleted,
            'R' => FileChangeKind.Renamed,
            'C' => FileChangeKind.Copied,
            '?' => FileChangeKind.Untracked,
            _ => FileChangeKind.Modified,
        };
    }

    private static (int ahead, int behind) ParseAheadBehind(string track)
    {
        int ahead = 0, behind = 0;
        if (string.IsNullOrEmpty(track)) return (ahead, behind);

        foreach (var part in track.Split(',', StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("ahead "))
                int.TryParse(part[6..], out ahead);
            else if (part.StartsWith("behind "))
                int.TryParse(part[7..], out behind);
        }
        return (ahead, behind);
    }
}
