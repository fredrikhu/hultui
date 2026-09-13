using System.Globalization;

namespace Junction.Git;

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

    /// <summary>
    /// Runs a read query for one of the views and throws <see cref="GitCommandException"/>
    /// when git refuses it. The rest of this class answers a refused command with an empty
    /// result; the queries behind a view must not, because a view's loader keeps its
    /// previous value when the producer throws and treats an empty list as the truth —
    /// which moved the selection every time a writer still held a file open.
    /// </summary>
    private GitResult Query(params string[] args)
    {
        var result = GitProcess.Run(Path, args);
        if (!result.Success) throw new GitCommandException(args, result);
        return result;
    }

    // ── .git directory ──

    private string? _gitDir;

    /// <summary>
    /// The repository's actual git directory, absolute.
    ///
    /// <para>Asked of git rather than assembled as <c>&lt;path&gt;/.git</c>: in a linked
    /// worktree <c>.git</c> is a <i>file</i> pointing at
    /// <c>&lt;main&gt;/.git/worktrees/&lt;name&gt;</c>, and that is where the state files a
    /// half-finished merge or rebase leaves behind actually live. Probing the wrong place
    /// reports "nothing in progress" for a worktree mid-conflict — the state where getting
    /// it wrong costs the most.</para>
    ///
    /// <para>Cached: a repository's git directory does not move under a running app, and
    /// this is on the path of every status load.</para>
    /// </summary>
    private string GitDir()
    {
        if (_gitDir != null) return _gitDir;
        var result = GitProcess.Run(Path, "rev-parse", "--absolute-git-dir");
        var dir = result.Success ? result.Stdout.Trim() : "";
        return _gitDir = dir.Length > 0 ? dir : System.IO.Path.Combine(Path, ".git");
    }

    /// <summary>A path inside the git directory (<c>MERGE_HEAD</c>, <c>rebase-merge/onto</c>).</summary>
    private string GitPath(params string[] parts) =>
        System.IO.Path.Combine([GitDir(), .. parts]);

    /// <summary>The first line of a git-directory state file, trimmed; null when absent
    /// or unreadable. These files are written by git one short value at a time.</summary>
    private string? ReadGitFile(params string[] parts)
    {
        try
        {
            var full = GitPath(parts);
            if (!File.Exists(full)) return null;
            var text = File.ReadLines(full).FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    // ── Branches ──

    public List<GitBranch> GetBranches()
    {
        // lstrip=2, not :short — :short runs an ambiguity check per ref (progressively
        // shorter candidates probed against every ref pattern) that costs ~0.4 ms/ref
        // on Windows: ~450 ms of the listing on a 1000-branch repo, measured. lstrip=2
        // is a plain textual strip with the same result for every ref these patterns
        // match (refs/heads/x -> x, refs/remotes/origin/x -> origin/x) — and when a
        // name IS ambiguous, :short's disambiguated "heads/x" would break the
        // remote-prefix parsing anyway.
        var result = GitProcess.Run(Path,
            "for-each-ref",
            "--format", $"%(refname:lstrip=2){FieldSep}%(refname){FieldSep}%(HEAD){FieldSep}%(upstream:lstrip=2){FieldSep}%(upstream:track,nobracket){FieldSep}%(if)%(*objectname)%(then)%(*objectname)%(else)%(objectname)%(end){FieldSep}%(contents:subject){FieldSep}%(committerdate:iso-strict)",
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
            // The refs "Checkout locally" and a review fetch a merge request's head into
            // (refs/remotes/<remote>/merge-requests/N) are addressed by name where they are
            // used; listed among the remote branches they only read as clutter.
            if (isMergeRequestRef(fullName)) continue;

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

    /// <summary><c>refs/remotes/&lt;remote&gt;/merge-requests/&lt;n&gt;</c> — the merge-request
    /// heads Junction fetches for a checkout or a review.</summary>
    private static bool isMergeRequestRef(string fullName)
    {
        const string prefix = "refs/remotes/";
        if (!fullName.StartsWith(prefix, StringComparison.Ordinal)) return false;
        int slash = fullName.IndexOf('/', prefix.Length);
        if (slash < 0) return false;
        var rest = fullName.AsSpan(slash + 1);
        const string mr = "merge-requests/";
        return rest.StartsWith(mr, StringComparison.Ordinal) && rest.Length > mr.Length
            && rest[mr.Length..].ToString().All(char.IsAsciiDigit);
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
        // %B (the whole message) goes last and is not trimmed away with the rest: it runs to
        // several lines and holds the blank line that separates subject from body, which is
        // exactly what a reader wants to see and copy.
        var format = string.Join(FieldSep, ["%H", "%h", "%an", "%ae", "%aI", "%s", "%P", "%D", "%B"]);
        var result = GitProcess.Run(Path, "log", "-1", $"--format={format}", refOrHash);
        if (!result.Success) return null;

        var line = result.Stdout;
        if (string.IsNullOrWhiteSpace(line)) return null;

        var parts = line.Split(FieldSep);
        if (parts.Length < 8) return null;
        var message = parts.Length > 8 ? parts[8].TrimEnd() : "";

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
            Refs: refs,
            Message: message
        );
    }

    // ── Log ──

    /// <param name="skip">How many commits to leave out from the top — the next page of a log
    /// whose first <paramref name="skip"/> commits are already loaded. Git's order is
    /// deterministic for an unchanged repository, so consecutive pages join without a gap or
    /// an overlap; a repository that changed in between is reloaded from the top by the
    /// caller, whose cache key includes its data generation.</param>
    public List<GitCommit> GetLog(int maxCount = 500, string? author = null,
        string? path = null, DateTimeOffset? after = null, DateTimeOffset? before = null,
        bool followPath = false, IReadOnlyList<string>? includeRefs = null,
        string? pickaxe = null, bool pickaxeRegex = false, int skip = 0)
    {
        var format = string.Join(FieldSep, ["%H", "%h", "%an", "%ae", "%aI", "%s", "%P", "%D"]);

        var args = new List<string>
        {
            "log",
            $"--max-count={maxCount}",
            $"--format={format}{RecordSep}",
        };
        if (skip > 0) args.Add($"--skip={skip}");

        // Content search (pickaxe). -G matches commits whose diff adds/removes a
        // line matching the regex; -S matches commits that change the number of
        // occurrences of the literal string (when was a symbol introduced/removed).
        if (!string.IsNullOrEmpty(pickaxe))
            args.Add((pickaxeRegex ? "-G" : "-S") + pickaxe);

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

        var result = Query(args.ToArray());

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
        int maxCount = 500, IReadOnlyList<string>? refs = null,
        string? pickaxe = null, bool pickaxeRegex = false)
    {
        var args = new List<string>
        {
            "log",
            $"--max-count={maxCount}",
            "--numstat",
            "--format=%x1e%H",   // record-separator (RS) + hash on its own line
        };
        // Scope the numstat pass to the same pickaxe match set as GetLog so it does
        // no more work than the rows it feeds (and keys line up exactly).
        if (!string.IsNullOrEmpty(pickaxe))
            args.Add((pickaxeRegex ? "-G" : "-S") + pickaxe);
        if (refs is { Count: > 0 })
            args.AddRange(refs);
        else
            args.Add("--all");

        return ParseNumstat(GitProcess.Run(Path, args.ToArray()));
    }

    /// <summary>
    /// The same +/- totals for exactly the commits named, in one <c>git log --numstat</c>
    /// that reads its revisions from stdin rather than walking a range.
    ///
    /// <para>This is what lets the History view's counts be cached by hash: a commit's
    /// numstat never changes, so after a fetch or a commit only the hashes the log did not
    /// have before need computing — not the whole window again, which at a few milliseconds
    /// a commit is the one History cost that grows with how far back has been loaded.
    /// Stdin because a few thousand forty-character hashes do not fit a command line.</para>
    /// </summary>
    public Dictionary<string, (int Added, int Deleted)> GetStatsFor(IReadOnlyCollection<string> hashes)
    {
        if (hashes.Count == 0) return new();
        var stdin = string.Join("\n", hashes) + "\n";
        return ParseNumstat(GitProcess.RunWithStdin(Path, stdin,
            "log", "--no-walk=unsorted", "--stdin", "--numstat", "--format=%x1e%H"));
    }

    private static Dictionary<string, (int Added, int Deleted)> ParseNumstat(GitResult result)
    {
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

    public List<GitFileDiff> GetCommitDiff(string commitHash, bool ignoreWhitespace = false)
    {
        // --root so the initial (parentless) commit diffs against the empty tree.
        var args = new List<string>
            { "diff-tree", "--no-commit-id", "-p", "-M", "-m", "--first-parent", "--root" };
        if (ignoreWhitespace) args.Add(IgnoreWhitespaceFlag);
        args.Add(commitHash);
        return ParseUnifiedDiff(Query(args.ToArray()).Stdout);
    }

    /// <summary>
    /// The stable patch id of a commit's change against its first parent — the same for a
    /// commit and its rebased or amended copy as long as the change itself did not change,
    /// which is what tells two rewritten commits apart when author, second and subject all
    /// agree. Null when git has no such commit (an original pruned since it was rewritten)
    /// or the commit changes nothing.
    /// </summary>
    public string? GetPatchId(string commitHash)
    {
        var diff = GitProcess.Run(Path, "diff-tree", "-p", "-m", "--first-parent", "--root", commitHash);
        if (!diff.Success) return null;
        var result = GitProcess.RunWithStdin(Path, diff.Stdout, "patch-id", "--stable");
        if (!result.Success) return null;
        // "<patch id> <commit id>", one line.
        var line = result.Stdout.Trim();
        var space = line.IndexOf(' ');
        return space > 0 ? line[..space] : null;
    }

    /// <summary>
    /// <c>--ignore-all-space</c>: whitespace-only changes are hidden entirely and
    /// whitespace differences within a line are ignored when matching. Note this
    /// makes the diff a <b>display</b> artifact — patches synthesized from it will
    /// not apply, so interactive staging is disabled while it's on.
    /// </summary>
    private const string IgnoreWhitespaceFlag = "--ignore-all-space";

    /// <summary>File list of the tree diff between two arbitrary commits
    /// (Fork-style multi-select compare: base = oldest, head = newest).</summary>
    public List<GitFileChange> GetRangeFiles(string baseHash, string headHash)
    {
        return QueryFileChanges(
            ["diff", "--name-status", "-M", baseHash, headHash],
            ["diff", "--numstat", "-M", baseHash, headHash]);
    }

    /// <summary>Patch of the tree diff between two arbitrary commits.</summary>
    public List<GitFileDiff> GetRangeDiff(string baseHash, string headHash, bool ignoreWhitespace = false)
    {
        var args = new List<string> { "diff", "-M" };
        if (ignoreWhitespace) args.Add(IgnoreWhitespaceFlag);
        args.Add(baseHash);
        args.Add(headHash);
        return ParseUnifiedDiff(Query(args.ToArray()).Stdout);
    }

    public List<GitFileDiff> GetWorkingDiff(bool staged, string? baseRef = null, bool ignoreWhitespace = false)
    {
        var args = new List<string> { "diff" };
        if (staged) args.Add("--cached");
        if (ignoreWhitespace) args.Add(IgnoreWhitespaceFlag);
        if (staged && baseRef != null) args.Add(baseRef);

        return ParseUnifiedDiff(Query(args.ToArray()).Stdout);
    }

    public List<GitFileDiff> GetUntrackedFileDiff(string filePath)
    {
        var fullPath = System.IO.Path.Combine(Path, filePath);
        if (!File.Exists(fullPath)) return [];

        // An untracked file was never handed to git, so nothing has judged it binary yet.
        // Apply git's own test first: read as text, a new .pdf arrives as thousands of
        // mojibake "additions", each one measured, tokenized and laid out.
        if (LooksBinary(fullPath))
            return
            [
                new GitFileDiff(filePath, null, [])
                {
                    Binary = new GitBinaryDiff(FileChangeKind.Added, null, null),
                },
            ];

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
    /// Every file path in a commit's tree (recursive, blobs only), in git's sorted
    /// order. Used by the Files browser to show the repository as it was at a commit.
    /// </summary>
    public List<string> GetTreeFiles(string gitRef)
    {
        var result = Query("ls-tree", "-r", "--name-only", "-z", gitRef);
        // -z: NUL-separated, so paths with spaces or quotes need no unescaping.
        return result.Stdout
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    // ── Blob bytes (images / binaries) ──

    /// <summary>Raw bytes of a blob at a ref, or null if it doesn't exist there.</summary>
    public byte[]? GetFileBytes(string gitRef, string filePath)
    {
        var (ok, bytes) = GitProcess.RunBytes(Path, "show", $"{gitRef}:{filePath}");
        return ok ? bytes : null;
    }

    /// <summary>Raw bytes of the staged (index) copy of a file.</summary>
    public byte[]? GetIndexFileBytes(string filePath)
    {
        var (ok, bytes) = GitProcess.RunBytes(Path, "show", $":{filePath}");
        return ok ? bytes : null;
    }

    /// <summary>Raw bytes of the working-tree copy, or null if it's gone.</summary>
    public byte[]? GetWorkingFileBytes(string filePath)
    {
        var fullPath = System.IO.Path.Combine(Path, filePath);
        try { return File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null; }
        catch { return null; }
    }

    // ── Blob sizes (binary summaries) ──
    //
    // A binary summary needs the two sides' byte counts and nothing else, so these ask
    // git for the size instead of fetching the blob — a 40 MB asset would otherwise be
    // read into memory twice to print two numbers.

    /// <summary>Byte size of a blob at a ref, or null when the file isn't there.</summary>
    public long? GetBlobSize(string gitRef, string filePath) => CatFileSize($"{gitRef}:{filePath}");

    /// <summary>Byte size of the staged (index) copy, or null when it isn't staged.</summary>
    public long? GetIndexBlobSize(string filePath) => CatFileSize($":{filePath}");

    /// <summary>Byte size of the working-tree copy, or null if it's gone.</summary>
    public long? GetWorkingFileSize(string filePath)
    {
        var fullPath = System.IO.Path.Combine(Path, filePath);
        try { return File.Exists(fullPath) ? new FileInfo(fullPath).Length : null; }
        catch { return null; }
    }

    private long? CatFileSize(string spec)
    {
        var result = GitProcess.Run(Path, "cat-file", "-s", spec);
        return result.Success && long.TryParse(result.Stdout.Trim(), out var size) ? size : null;
    }

    /// <summary>git's own heuristic, and the one the Files browser already uses: a NUL
    /// byte in the first 8 KB means binary. Only the head of the file is read.</summary>
    internal static bool LooksBinary(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            var probe = new byte[8000];
            int read = stream.ReadAtLeast(probe, probe.Length, throwOnEndOfStream: false);
            for (int i = 0; i < read; i++)
                if (probe[i] == 0)
                    return true;
            return false;
        }
        catch { return false; }   // unreadable is not "binary"; the text path reports the error
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
            foreach (var line in Query("status", "--porcelain=v1", "-uall").Lines)
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
        foreach (var line in Query(numstatArgs).Lines)
        {
            var parts = line.Split('\t');
            if (parts.Length < 3) continue;
            int.TryParse(parts[0], out var a);
            int.TryParse(parts[1], out var d);
            counts[NormalizeRenamePath(parts[2])] = (a, d);
        }

        var list = new List<GitFileChange>();
        foreach (var line in Query(statusArgs).Lines)
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

    /// <summary>Stages several files in one <c>git add</c>. A single call rather than a
    /// loop so a multi-file stage lands as one index write — a loop that fails half way
    /// leaves the user looking at a list that is part staged for no stated reason.</summary>
    public bool Stage(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0) return true;
        return GitProcess.Run(Path, ["add", "--", .. filePaths]).Success;
    }

    /// <summary>Unstages several files in one <c>git restore --staged</c>.</summary>
    public bool Unstage(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0) return true;
        return GitProcess.Run(Path, ["restore", "--staged", "--", .. filePaths]).Success;
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
    public bool DiscardChanges(GitFileChange file) => DiscardChanges([file]);

    /// <summary>
    /// Discard worktree changes for several files at once. The two kinds need different
    /// commands, so the set is split and each half runs as one call — untracked files
    /// are deleted by <c>git clean</c>, tracked ones restored from the index.
    /// </summary>
    public bool DiscardChanges(IReadOnlyList<GitFileChange> files)
    {
        if (files.Count == 0) return true;
        var untracked = files.Where(f => f.Kind == FileChangeKind.Untracked).Select(f => f.Path).ToList();
        var tracked = files.Where(f => f.Kind != FileChangeKind.Untracked).Select(f => f.Path).ToList();

        var ok = true;
        if (untracked.Count > 0)
            ok &= GitProcess.Run(Path, ["clean", "-f", "--", .. untracked]).Success;
        if (tracked.Count > 0)
            ok &= GitProcess.Run(Path, ["restore", "--worktree", "--", .. tracked]).Success;
        return ok;
    }

    // ── Ignore / assume-unchanged ──

    /// <summary>Appends a line to the repo's <c>.gitignore</c> (creating it if
    /// needed), unless an identical line is already present. Returns false only on
    /// an I/O error.</summary>
    public bool AddToGitignore(string entry)
    {
        entry = entry.Replace('\\', '/').Trim();
        if (entry.Length == 0) return false;
        try
        {
            var file = System.IO.Path.Combine(Path, ".gitignore");
            var existing = File.Exists(file) ? File.ReadAllText(file) : "";
            var lines = existing.Split('\n').Select(l => l.TrimEnd('\r').Trim());
            if (lines.Contains(entry)) return true;   // already ignored
            var prefix = existing.Length == 0 || existing.EndsWith('\n') ? "" : "\n";
            File.AppendAllText(file, prefix + entry + "\n");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Toggles the assume-unchanged bit on a tracked file (<c>git update-index
    /// --[no-]assume-unchanged</c>) — git stops (or resumes) noticing local edits to it.</summary>
    public GitResult SetAssumeUnchanged(string path, bool assume) =>
        GitProcess.Run(Path, "update-index", assume ? "--assume-unchanged" : "--no-assume-unchanged", "--", path);

    /// <summary>Paths currently flagged assume-unchanged (the lowercase-'h' rows of
    /// <c>git ls-files -v</c>).</summary>
    public List<string> GetAssumeUnchangedFiles()
    {
        var result = GitProcess.Run(Path, "ls-files", "-v");
        if (!result.Success) return [];
        var paths = new List<string>();
        foreach (var line in result.Lines)
        {
            // "<tag> <path>" — a lowercase tag means assume-unchanged; 'h' specifically.
            if (line.Length >= 2 && line[0] == 'h' && line[1] == ' ')
                paths.Add(line[2..]);
        }
        return paths;
    }

    // ── Commit ──

    public GitResult Commit(string message, bool sign = false)
    {
        var args = new List<string> { "commit", "-m", message };
        if (sign) args.Insert(1, "-S");   // GPG/SSH sign; requires a configured signing key
        return GitProcess.Run(Path, args.ToArray());
    }

    public GitResult CommitAmend(string message, bool sign = false)
    {
        var args = new List<string> { "commit", "--amend", "-m", message };
        if (sign) args.Insert(1, "-S");
        return GitProcess.Run(Path, args.ToArray());
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

    /// <summary>Remotes with their fetch and push URLs, parsed from
    /// <c>git remote -v</c> (which lists a "(fetch)" and "(push)" line each).</summary>
    public List<GitRemote> GetRemotesDetailed()
    {
        var result = GitProcess.Run(Path, "remote", "-v");
        if (!result.Success) return [];

        // Preserve first-seen order; fill fetch/push as the two lines per remote arrive.
        var order = new List<string>();
        var fetch = new Dictionary<string, string>(StringComparer.Ordinal);
        var push = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in result.Lines)
        {
            // "<name>\t<url> (fetch)" / "<name>\t<url> (push)"
            var tab = line.IndexOf('\t');
            if (tab < 0) continue;
            var name = line[..tab];
            var rest = line[(tab + 1)..];
            var sp = rest.LastIndexOf(' ');
            var url = sp > 0 ? rest[..sp] : rest;
            var kind = sp > 0 ? rest[(sp + 1)..] : "";
            if (!order.Contains(name)) order.Add(name);
            if (kind == "(push)") push[name] = url;
            else fetch[name] = url;
        }

        // `remote -v` prints the URLs git will actually contact, with any url.<base>.insteadOf
        // rewrite applied. The URL the user wrote is the one that says where the repository is
        // hosted — a gitlab.com remote rewritten to an internal mirror for transport is still
        // a gitlab.com project — so the configured URL wins where the two differ.
        var configured = GitProcess.Run(Path, "config", "--get-regexp", @"^remote\..*\.(url|pushurl)$");
        if (configured.Success)
        {
            var cfgUrl = new Dictionary<string, string>(StringComparer.Ordinal);
            var cfgPush = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in configured.Lines)
            {
                var sp = line.IndexOf(' ');
                if (sp < 0 || !line.StartsWith("remote.", StringComparison.Ordinal)) continue;
                var key = line[..sp];
                var url = line[(sp + 1)..];
                bool isPush = key.EndsWith(".pushurl", StringComparison.Ordinal);
                var name = key["remote.".Length..^(isPush ? ".pushurl".Length : ".url".Length)];
                (isPush ? cfgPush : cfgUrl)[name] = url;
            }
            foreach (var name in order)
                if (cfgUrl.TryGetValue(name, out var url))
                {
                    fetch[name] = url;
                    push[name] = cfgPush.GetValueOrDefault(name, url);
                }
        }
        return order
            .Select(n => new GitRemote(n, fetch.GetValueOrDefault(n, ""), push.GetValueOrDefault(n, fetch.GetValueOrDefault(n, ""))))
            .ToList();
    }

    public GitResult AddRemote(string name, string url) =>
        GitProcess.Run(Path, "remote", "add", name, url);

    public GitResult RemoveRemote(string name) =>
        GitProcess.Run(Path, "remote", "remove", name);

    public GitResult RenameRemote(string oldName, string newName) =>
        GitProcess.Run(Path, "remote", "rename", oldName, newName);

    public GitResult SetRemoteUrl(string name, string url) =>
        GitProcess.Run(Path, "remote", "set-url", name, url);

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

    /// <summary>
    /// Every remote-tracking ref and what it points at, as one string.
    ///
    /// <para>A background fetch has to answer "did anything actually arrive?" before it
    /// throws away a repository's cached views, and git's own output does not say so
    /// reliably enough to parse. Comparing this before and after does, for the cost of one
    /// <c>for-each-ref</c>.</para>
    /// </summary>
    public string RemoteRefsFingerprint()
    {
        var result = GitProcess.Run(Path, "for-each-ref",
            "--format=%(objectname) %(refname)", "refs/remotes/", "refs/tags/");
        return result.Success ? result.Stdout : "";
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

    /// <summary>One refspec from one remote — how a merge request's head
    /// (<c>+refs/merge-requests/N/head:refs/remotes/origin/merge-requests/N</c>) is brought
    /// down without touching any branch.</summary>
    public Task<GitResult> FetchRefspecAsync(string remote, string refspec, Func<string, Task<string?>>? askPass = null)
        => GitProcess.RunAsync(Path, askPass, "fetch", remote, refspec);

    /// <summary>The subjects of the commits <paramref name="head"/> has that
    /// <paramref name="baseRef"/> does not, newest first — what a merge request's
    /// description is pre-filled from. Empty when either ref is unknown.</summary>
    public List<string> GetSubjectsBetween(string baseRef, string head, int max = 50)
    {
        var res = GitProcess.Run(Path, "log", "--format=%s", $"--max-count={max}", $"{baseRef}..{head}");
        if (!res.Success) return [];
        return res.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.TrimEnd('\r')).ToList();
    }

    /// <summary>Whether a local branch of that name exists.</summary>
    public bool LocalBranchExists(string name) =>
        GitProcess.Run(Path, "show-ref", "--verify", "--quiet", "refs/heads/" + name).Success;

    /// <summary>Fast-forwards the checked-out branch to <paramref name="commitish"/>;
    /// refuses (non-zero) when that would not be a fast-forward.</summary>
    public GitResult MergeFastForwardOnly(string commitish) =>
        GitProcess.Run(Path, "merge", "--ff-only", commitish);

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

    /// <summary>The upstream a branch tracks ("origin/main"), or null when it tracks
    /// nothing. Asked of the ref itself rather than of <c>@{u}</c>, which errors out
    /// noisily for exactly the case this is here to detect.</summary>
    public string? GetUpstream(string branch)
    {
        var result = GitProcess.Run(Path, "for-each-ref",
            "--format=%(upstream:lstrip=2)", $"refs/heads/{branch}");
        var name = result.Success ? result.Stdout.Trim() : "";
        return name.Length > 0 ? name : null;
    }

    /// <summary>
    /// Where an unpublished branch should go, in git's own order of preference:
    /// <c>remote.pushDefault</c>, then <c>origin</c>, then the only remote there is.
    /// Null when the repository has no remotes.
    /// </summary>
    public string? DefaultPushRemote()
    {
        var configured = GitProcess.Run(Path, "config", "--get", "remote.pushDefault");
        if (configured.Success && configured.Stdout.Trim() is { Length: > 0 } name)
            return name;

        var remotes = GetRemotes();
        if (remotes.Count == 0) return null;
        return remotes.Contains("origin") ? "origin" : remotes[0];
    }

    /// <summary>
    /// What pushing <paramref name="branch"/> (null: whatever is checked out) would do.
    ///
    /// <para>A branch created locally has no upstream, and a bare <c>git push</c> answers
    /// that with a fatal error and a command to copy. Junction publishes it instead — the
    /// answer is never "don't push", it is always "push, and record where" — so the
    /// decision is made here, off the UI thread, and <see cref="RunPushAsync"/> carries
    /// it out.</para>
    /// </summary>
    public PushPlan PlanPush(string? branch = null)
    {
        branch ??= GetCurrentBranch();
        if (string.IsNullOrEmpty(branch) || branch == "HEAD")
            return new PushPlan(null, "", false, "HEAD is detached — check out a branch to push.");

        if (GetUpstream(branch) != null)
            return new PushPlan(null, branch, false, null);

        var remote = DefaultPushRemote();
        return remote == null
            ? new PushPlan(null, branch, false, $"{branch} has no upstream and this repository has no remote.")
            : new PushPlan(remote, branch, true, null);
    }

    /// <summary>Carries out a <see cref="PlanPush"/>. Publishing uses
    /// <c>--set-upstream</c>, so the branch tracks its new remote afterwards and every
    /// later push, pull and ahead/behind count works without asking again.
    ///
    /// <para><paramref name="forceWithLease"/> adds <c>--force-with-lease</c>: it overwrites
    /// the remote branch, but only while the remote is still where this repository last saw
    /// it. A colleague who pushed in between makes the command fail instead of erasing their
    /// work, which is the whole difference from a bare <c>--force</c> — and the reason
    /// nothing here offers the bare one.</para></summary>
    public Task<GitResult> RunPushAsync(PushPlan plan, Func<string, Task<string?>>? askPass = null,
        bool forceWithLease = false)
    {
        if (plan.Problem != null)
            return Task.FromResult(new GitResult(1, "", plan.Problem));
        var args = new List<string> { "push" };
        if (forceWithLease) args.Add("--force-with-lease");
        if (plan.SetUpstream && plan.Remote != null)
        {
            args.Add("--set-upstream");
            args.Add(plan.Remote);
            args.Add(plan.Branch);
        }
        return GitProcess.RunAsync(Path, askPass, args.ToArray());
    }

    /// <inheritdoc cref="RunPushAsync" select="para"/>
    public Task<GitResult> PushToAsync(string remote, string branch, Func<string, Task<string?>>? askPass = null,
        bool forceWithLease = false)
    {
        return forceWithLease
            ? GitProcess.RunAsync(Path, askPass, "push", "--force-with-lease", remote, branch)
            : GitProcess.RunAsync(Path, askPass, "push", remote, branch);
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
        var msgPath = GitPath("MERGE_MSG");
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
        return File.Exists(GitPath("MERGE_HEAD"));
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

        // Dropping every commit leaves no todo lines at all, and git rejects an empty
        // todo with "error: nothing to do" rather than doing the obvious thing. `noop` is
        // git's own placeholder for that plan: the rebase runs, replays nothing, and leaves
        // the branch sitting on baseRef — which is exactly what dropping them all means.
        if (lines.Count == 0) lines.Add("noop");

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
        return Directory.Exists(GitPath("rebase-merge"))
            || Directory.Exists(GitPath("rebase-apply"));
    }

    // ── Sequencer (merge · rebase · cherry-pick · revert) ──
    //
    // Four commands that stop on a conflict and wait. Only the resume command and the
    // meaning of the two sides differ, so everything above this layer works in terms of
    // SequencerState rather than asking four separate questions.

    /// <summary>Which half-finished operation the repository is sitting in, if any.
    /// Checked in the order git itself resolves them: a rebase that hits a conflict while
    /// picking leaves a CHERRY_PICK_HEAD behind too, and it is still a rebase.</summary>
    public SequencerOp GetSequencerOp()
    {
        if (IsRebasing()) return SequencerOp.Rebase;
        if (File.Exists(GitPath("CHERRY_PICK_HEAD"))) return SequencerOp.CherryPick;
        if (File.Exists(GitPath("REVERT_HEAD"))) return SequencerOp.Revert;
        if (File.Exists(GitPath("MERGE_HEAD"))) return SequencerOp.Merge;
        return SequencerOp.None;
    }

    /// <summary>
    /// The in-progress operation and the identity of its two conflict sides, or null when
    /// the repository is in a normal state.
    ///
    /// <para>Reads git's own state files rather than shelling out per field: they are a few
    /// bytes each, this runs on every status load, and the values are exactly what git will
    /// act on when the operation resumes.</para>
    /// </summary>
    public SequencerState? GetSequencerState()
    {
        var op = GetSequencerOp();
        if (op == SequencerOp.None) return null;

        var current = GetCurrentBranch();
        switch (op)
        {
            case SequencerOp.Merge:
                return new SequencerState(op,
                    OursRef: "HEAD", TheirsRef: "MERGE_HEAD",
                    OursLabel: current, TheirsLabel: GetMergingBranchName() ?? "incoming",
                    Branch: current, Step: 0, Total: 0);

            case SequencerOp.Rebase:
            {
                // rebase-merge is the interactive/merge backend, rebase-apply the am one;
                // they spell the same three facts differently.
                bool merge = Directory.Exists(GitPath("rebase-merge"));
                string dir = merge ? "rebase-merge" : "rebase-apply";
                var headName = ReadGitFile(dir, "head-name");
                var branch = headName != null && headName.StartsWith("refs/heads/")
                    ? headName["refs/heads/".Length..]
                    : headName == "detached HEAD" ? null : headName;
                var onto = ReadGitFile(dir, "onto");
                int step = ParseCount(ReadGitFile(dir, merge ? "msgnum" : "next"));
                int total = ParseCount(ReadGitFile(dir, merge ? "end" : "last"));

                // The inversion that makes rebase conflicts confusing: git replays YOUR
                // commit onto the other branch, so the other branch is the checked-out
                // side ("ours") and your own commit arrives as "theirs".
                var ontoLabel = onto != null ? DescribeRef(onto) : "the new base";
                var replaying = File.Exists(GitPath("REBASE_HEAD")) ? "REBASE_HEAD" : null;
                return new SequencerState(op,
                    OursRef: onto ?? "HEAD", TheirsRef: replaying,
                    OursLabel: ontoLabel,
                    TheirsLabel: branch != null ? branch + " (being replayed)" : "the commit being replayed",
                    Branch: branch, Step: step, Total: total);
            }

            case SequencerOp.CherryPick:
                return new SequencerState(op,
                    OursRef: "HEAD", TheirsRef: "CHERRY_PICK_HEAD",
                    OursLabel: current, TheirsLabel: "the commit being picked",
                    Branch: current, Step: 0, Total: 0);

            default:
                return new SequencerState(op,
                    OursRef: "HEAD", TheirsRef: "REVERT_HEAD",
                    OursLabel: current, TheirsLabel: "the commit being reverted",
                    Branch: current, Step: 0, Total: 0);
        }
    }

    /// <summary>Resumes the given operation once every conflict is staged. A merge finishes
    /// with a plain commit (git only gained <c>merge --continue</c> in 2.12, and it is an
    /// alias for exactly that); the other three have their own continue.</summary>
    public GitResult SequencerContinue(SequencerOp op) => op switch
    {
        SequencerOp.Merge => MergeContinue(),
        SequencerOp.Rebase => RunSequencer("rebase", "--continue"),
        SequencerOp.CherryPick => RunSequencer("cherry-pick", "--continue"),
        SequencerOp.Revert => RunSequencer("revert", "--continue"),
        _ => new GitResult(1, "", "Nothing in progress."),
    };

    /// <summary>Throws the operation away and puts the working tree back.</summary>
    public GitResult SequencerAbort(SequencerOp op) => op switch
    {
        SequencerOp.Merge => MergeAbort(),
        SequencerOp.Rebase => RunSequencer("rebase", "--abort"),
        SequencerOp.CherryPick => RunSequencer("cherry-pick", "--abort"),
        SequencerOp.Revert => RunSequencer("revert", "--abort"),
        _ => new GitResult(1, "", "Nothing in progress."),
    };

    /// <summary>Drops the commit that conflicted and carries on with the rest.</summary>
    public GitResult SequencerSkip(SequencerOp op) => op switch
    {
        SequencerOp.Rebase => RunSequencer("rebase", "--skip"),
        SequencerOp.CherryPick => RunSequencer("cherry-pick", "--skip"),
        SequencerOp.Revert => RunSequencer("revert", "--skip"),
        _ => new GitResult(1, "", "This operation has nothing to skip."),
    };

    /// <summary>
    /// Runs a sequencer command with the editors stubbed out.
    ///
    /// <para><c>--continue</c> wants to open <c>$GIT_EDITOR</c> on the commit message it has
    /// prepared. There is no terminal behind a desktop app, so git would either fail with
    /// "terminal is dumb" or block forever on a stdin nobody is typing into; <c>true</c>
    /// exits 0 without touching the file, which git reads as "accepted as prepared" — the
    /// same thing <c>--no-edit</c> means on the merge path.</para>
    /// </summary>
    private GitResult RunSequencer(params string[] args)
    {
        var env = new Dictionary<string, string>
        {
            ["GIT_EDITOR"] = "true",
            ["GIT_SEQUENCE_EDITOR"] = "true",
        };
        return GitProcess.RunWithEnv(Path, env, args);
    }

    /// <summary>A branch or tag pointing at the given rev, else its short hash — so the
    /// side of a rebase conflict reads as "main" rather than as forty hex digits.</summary>
    private string DescribeRef(string rev)
    {
        var named = GitProcess.Run(Path, "name-rev", "--name-only", "--refs=refs/heads/*",
            "--refs=refs/remotes/*", "--refs=refs/tags/*", rev);
        var name = named.Success ? named.Stdout.Trim() : "";
        // name-rev answers "undefined" when nothing points near the commit, and
        // "main~3" when something does but not exactly — the latter is still useful.
        if (name.Length > 0 && name != "undefined") return name;
        return rev.Length > 7 ? rev[..7] : rev;
    }

    private static int ParseCount(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

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

    // ── Reflog ──

    /// <summary>
    /// HEAD's reflog, newest first — the recovery surface for work "lost" to a
    /// reset/rebase/checkout (the commit still has a reflog entry until gc).
    /// </summary>
    public List<GitReflogEntry> GetReflog(int maxCount = 200)
    {
        // %gd = selector (HEAD@{n}), %gs = reflog subject (the action), %cI = commit
        // ISO date, %s = the commit's own subject.
        var format = string.Join(FieldSep, ["%H", "%h", "%gd", "%gs", "%cI", "%s"]);
        var result = GitProcess.Run(Path, "reflog", $"--max-count={maxCount}", $"--format={format}");
        if (!result.Success) return [];

        var entries = new List<GitReflogEntry>();
        foreach (var raw in result.Stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var p = line.Split(FieldSep);
            if (p.Length < 6) continue;
            if (!DateTimeOffset.TryParse(p[4], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                date = DateTimeOffset.MinValue;
            entries.Add(new GitReflogEntry(p[0], p[1], p[2], p[3], date, p[5]));
        }
        return entries;
    }

    // ── Tags ──

    /// <summary>Every tag, newest first, with the commit each one points at.</summary>
    /// <remarks>
    /// <c>%(*objectname)</c> and <c>%(*contents:subject)</c> are the dereferenced —
    /// through-the-tag-object — commit fields, and are empty for a lightweight tag, whose
    /// ref already is the commit. Which of the two a row is shows up in
    /// <c>%(objecttype)</c>: "tag" for an annotated one, "commit" for a lightweight one.
    /// </remarks>
    public List<GitTag> GetTags()
    {
        // lstrip=2, not :short — see GetBranches: :short's per-ref ambiguity check
        // is the expensive part of listing a big ref set; refs/tags/x -> x either way.
        var result = GitProcess.Run(Path,
            "for-each-ref", "--sort=-creatordate",
            "--format", $"%(refname:lstrip=2){FieldSep}%(objecttype){FieldSep}%(objectname){FieldSep}" +
                        $"%(*objectname){FieldSep}%(creatordate:iso-strict){FieldSep}" +
                        $"%(contents:subject){FieldSep}%(*contents:subject)",
            "refs/tags/");
        if (!result.Success) return [];

        var tags = new List<GitTag>();
        foreach (var line in result.Lines)
        {
            var parts = line.Split(FieldSep);
            if (parts.Length < 5) continue;

            var name = parts[0];
            if (name.Length == 0) continue;
            bool annotated = parts[1] == "tag";
            // The tag object's own sha is no use in a list of commits; take the
            // dereferenced one whenever there is a tag object in the way.
            var hash = parts[3].Length > 0 ? parts[3] : parts[2];

            DateTimeOffset date = default;
            DateTimeOffset.TryParse(parts[4], CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

            // For an annotated tag `contents:subject` is the tag's own message, and the
            // commit's subject is behind the star; a lightweight tag has only the commit.
            var tagMessage = parts.Length > 5 ? parts[5] : "";
            var derefSubject = parts.Length > 6 ? parts[6] : "";
            var subject = annotated ? derefSubject : tagMessage;

            tags.Add(new GitTag(name, hash, subject, date, annotated, annotated ? tagMessage : ""));
        }
        return tags;
    }

    /// <summary>Push one tag to a remote (<c>git push &lt;remote&gt; &lt;tag&gt;</c>).</summary>
    public Task<GitResult> PushTagAsync(string remote, string name, Func<string, Task<string?>>? askPass = null)
        => GitProcess.RunAsync(Path, askPass, "push", remote, "refs/tags/" + name);

    /// <summary>Delete a tag from a remote. Separate from <see cref="DeleteTag"/>: deleting
    /// locally leaves the remote's copy, which is what most people actually want, so the
    /// two are never done together implicitly.</summary>
    public Task<GitResult> DeleteRemoteTagAsync(string remote, string name, Func<string, Task<string?>>? askPass = null)
        => GitProcess.RunAsync(Path, askPass, "push", remote, "--delete", "refs/tags/" + name);

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
        return File.Exists(GitPath("BISECT_START"));
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

    /// <summary>Updates every submodule to the commit the superproject records
    /// (<c>submodule update --recursive</c>); <paramref name="init"/> also clones/
    /// initializes any that aren't set up yet.</summary>
    public GitResult UpdateSubmodules(bool init = true)
    {
        var args = new List<string> { "submodule", "update", "--recursive" };
        if (init) args.Add("--init");
        return GitProcess.Run(Path, args.ToArray());
    }

    /// <summary>Updates a single submodule by path.</summary>
    public GitResult UpdateSubmodule(string path, bool init = true)
    {
        var args = new List<string> { "submodule", "update" };
        if (init) args.Add("--init");
        args.Add("--");
        args.Add(path);
        return GitProcess.Run(Path, args.ToArray());
    }

    /// <summary>Re-syncs each submodule's remote URL from <c>.gitmodules</c> (after
    /// the upstream URL changed).</summary>
    public GitResult SyncSubmodules() => GitProcess.Run(Path, "submodule", "sync", "--recursive");

    /// <summary>Deinitializes a submodule — empties its working tree but keeps the
    /// gitlink so it can be re-initialized. <paramref name="force"/> discards local
    /// changes inside it.</summary>
    public GitResult DeinitSubmodule(string path, bool force = false)
    {
        var args = new List<string> { "submodule", "deinit" };
        if (force) args.Add("--force");
        args.Add("--");
        args.Add(path);
        return GitProcess.Run(Path, args.ToArray());
    }

    /// <summary>Adds a new submodule at <paramref name="path"/> from
    /// <paramref name="url"/> (<c>submodule add</c>), staging .gitmodules + the gitlink.</summary>
    public GitResult AddSubmodule(string url, string path) =>
        GitProcess.Run(Path, "submodule", "add", url, path);

    // ── Worktrees ──

    /// <summary>
    /// Lists the repository's worktrees (the main one plus any linked ones) by
    /// parsing <c>git worktree list --porcelain</c>. The first entry is the main
    /// worktree; the one whose path matches this repo's is flagged current.
    /// </summary>
    public List<GitWorktree> GetWorktrees()
    {
        var result = GitProcess.Run(Path, "worktree", "list", "--porcelain");
        if (!result.Success) return [];

        var worktrees = new List<GitWorktree>();
        // Records are blank-line separated; each is a set of "key [value]" lines.
        string? path = null, head = null, branch = null, lockReason = null;
        bool bare = false, detached = false, locked = false, prunable = false;
        bool first = true;

        void Flush()
        {
            if (path == null) return;
            worktrees.Add(new GitWorktree(
                path, head ?? "", branch, bare, detached, locked, lockReason,
                IsMain: first, prunable));
            first = false;
            path = head = branch = lockReason = null;
            bare = detached = locked = prunable = false;
        }

        foreach (var raw in result.Stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) { Flush(); continue; }

            var sp = line.IndexOf(' ');
            var key = sp < 0 ? line : line[..sp];
            var val = sp < 0 ? "" : line[(sp + 1)..];
            switch (key)
            {
                // git prints forward slashes even on Windows; the rest of the app
                // stores OS-native paths (folder picker, active repo), so normalize
                // here or PathEquals/dedup against those would silently mismatch.
                case "worktree": path = val.Replace('/', System.IO.Path.DirectorySeparatorChar); break;
                case "HEAD": head = val; break;
                // "branch refs/heads/<name>" — strip the ref prefix to a short name.
                case "branch": branch = val.StartsWith("refs/heads/") ? val["refs/heads/".Length..] : val; break;
                case "bare": bare = true; break;
                case "detached": detached = true; break;
                case "locked": locked = true; lockReason = val.Length > 0 ? val : null; break;
                case "prunable": prunable = true; break;
            }
        }
        Flush();   // last record has no trailing blank line
        return worktrees;
    }

    /// <summary>
    /// Adds a worktree at <paramref name="path"/>. With <paramref name="newBranch"/>
    /// set, creates that branch (<c>-b</c>) starting at <paramref name="committish"/>
    /// (or HEAD); otherwise checks out <paramref name="committish"/> — an existing
    /// branch name, tag, or commit — directly.
    /// </summary>
    public GitResult AddWorktree(string path, string? committish = null, string? newBranch = null)
    {
        var args = new List<string> { "worktree", "add" };
        if (!string.IsNullOrWhiteSpace(newBranch)) { args.Add("-b"); args.Add(newBranch); }
        args.Add(path);
        if (!string.IsNullOrWhiteSpace(committish)) args.Add(committish);
        return GitProcess.Run(Path, args.ToArray());
    }

    /// <summary>Removes a worktree. <paramref name="force"/> allows removal even
    /// when it has uncommitted changes or is locked.</summary>
    public GitResult RemoveWorktree(string path, bool force = false)
    {
        var args = new List<string> { "worktree", "remove" };
        if (force) args.Add("--force");
        args.Add(path);
        return GitProcess.Run(Path, args.ToArray());
    }

    /// <summary>Prunes worktree administrative entries whose working directories
    /// are gone (<c>git worktree prune</c>).</summary>
    public GitResult PruneWorktrees() => GitProcess.Run(Path, "worktree", "prune");

    /// <summary>Locks a linked worktree so git refuses to prune or remove it — what
    /// you do to a checkout on a removable drive or network share, which would
    /// otherwise look prunable every time it is unmounted. <paramref name="reason"/>
    /// is the note git stores and reports back in <c>worktree list --porcelain</c>.
    /// Git rejects locking the main worktree.</summary>
    public GitResult LockWorktree(string path, string? reason = null)
    {
        var args = new List<string> { "worktree", "lock" };
        if (!string.IsNullOrWhiteSpace(reason)) { args.Add("--reason"); args.Add(reason); }
        args.Add(path);
        return GitProcess.Run(Path, args.ToArray());
    }

    /// <summary>Releases a lock taken by <see cref="LockWorktree"/>. A locked worktree
    /// cannot be removed — not even with <c>--force</c> — so this is the way out.</summary>
    public GitResult UnlockWorktree(string path) => GitProcess.Run(Path, "worktree", "unlock", path);

    // ── Patch application ──

    /// <summary>
    /// Applies a unified diff patch via stdin to <c>git apply</c>.
    /// <paramref name="cached"/> targets the index (stage/unstage).
    /// <paramref name="reverse"/> inverts the patch (unstage or discard).
    /// <paramref name="unidiffZero"/> passes <c>--unidiff-zero</c> for zero-context
    /// hunks; leave it off for full-context patches (e.g. line-level selection),
    /// where it would place hunks by line number and mis-apply insertions.
    /// </summary>
    public GitResult ApplyPatch(string patch, bool reverse = false, bool cached = false,
        bool unidiffZero = true)
    {
        var args = new List<string> { "apply" };
        if (cached) args.Add("--cached");
        if (reverse) args.Add("--reverse");
        if (unidiffZero) args.Add("--unidiff-zero");
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
    // internal rather than private so Junction.Tests can drive them off canned git
    // output. They are the layer where a wrong line number or a phantom row does real
    // damage (patch synthesis), and reaching them through a live repository would test
    // git's formatting as much as ours.

    internal static List<GitFileDiff> ParseUnifiedDiff(string diffText)
    {
        var diffs = new List<GitFileDiff>();
        GitFileDiff? currentFile = null;
        GitDiffHunk? currentHunk = null;
        int oldLine = 0, newLine = 0;

        // Extended-header state for the file being read. git puts these between the
        // "diff --git" line and the first hunk, so by the time a "Binary files … differ"
        // line arrives they are all known.
        var headerKind = FileChangeKind.Modified;
        string? oldBlob = null, newBlob = null;

        foreach (var rawLine in diffText.Split('\n'))
        {
            if (rawLine.StartsWith("diff --git "))
            {
                var parts = rawLine.Split(" b/", 2);
                var path = parts.Length > 1 ? parts[1] : rawLine;
                currentFile = new GitFileDiff(path, null, []);
                diffs.Add(currentFile);
                currentHunk = null;
                headerKind = FileChangeKind.Modified;
                oldBlob = newBlob = null;
            }
            else if (rawLine.StartsWith("--- ") && currentFile != null && currentHunk == null)
            {
                currentFile.OldPathHeader = rawLine;
            }
            else if (rawLine.StartsWith("+++ ") && currentFile != null && currentHunk == null)
            {
                currentFile.NewPathHeader = rawLine;
            }
            else if (currentFile != null && currentHunk == null && rawLine.StartsWith("new file mode "))
            {
                headerKind = FileChangeKind.Added;
            }
            else if (currentFile != null && currentHunk == null && rawLine.StartsWith("deleted file mode "))
            {
                headerKind = FileChangeKind.Deleted;
            }
            else if (currentFile != null && currentHunk == null && rawLine.StartsWith("rename from "))
            {
                headerKind = FileChangeKind.Renamed;
            }
            else if (currentFile != null && currentHunk == null && rawLine.StartsWith("index "))
            {
                // "similarity index 90%" does not match — that line has no trailing space
                // after "similarity", and this one is anchored at the start.
                (oldBlob, newBlob) = ParseIndexBlobs(rawLine);
            }
            else if (currentFile != null && currentHunk == null
                     && (rawLine.StartsWith("Binary files ") || rawLine.StartsWith("GIT binary patch")))
            {
                // git's stand-in for a patch it will not produce. Recorded rather than
                // dropped, so the viewer can say what changed instead of showing an empty
                // pane — and so nothing downstream tries to tokenize bytes that aren't text.
                currentFile.Binary = new GitBinaryDiff(headerKind, oldBlob, newBlob);
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
                else if (rawLine.StartsWith('\\'))
                {
                    // "\ No newline at end of file" attaches to the line just emitted;
                    // flag it so partial-staging patches can reproduce the marker.
                    if (currentHunk.Lines.Count > 0)
                        currentHunk.Lines[^1] = currentHunk.Lines[^1] with { NoNewlineAfter = true };
                }
                else if (rawLine.StartsWith(' '))
                {
                    // A blank context line is " " (space + empty content), length 1.
                    // A length-0 rawLine is only the trailing empty string from splitting
                    // the diff's final newline — NOT a real line. Treating it as context
                    // (the old behavior) appended a phantom blank row to each file's last
                    // hunk: invisible in the viewer but fatal to patch synthesis (it
                    // inflated the hunk counts). Skip it.
                    currentHunk.Lines.Add(new GitDiffLine(DiffLineKind.Context, rawLine[1..], oldLine, newLine));
                    oldLine++;
                    newLine++;
                }
            }
        }

        return diffs;
    }

    /// <summary>
    /// The two blob hashes on a diff's <c>index e0d84f4..9114530 100644</c> line. Git
    /// abbreviates them and omits the mode on an add or a delete; an all-zero hash means
    /// the file did not exist on that side and comes back null.
    /// </summary>
    private static (string? Old, string? New) ParseIndexBlobs(string line)
    {
        var body = line["index ".Length..].Trim();
        var space = body.IndexOf(' ');
        if (space >= 0) body = body[..space];              // drop the trailing file mode
        var dots = body.IndexOf("..", StringComparison.Ordinal);
        if (dots < 0) return (null, null);
        return (Blob(body[..dots]), Blob(body[(dots + 2)..]));

        static string? Blob(string hash) =>
            hash.Length == 0 || hash.All(c => c == '0') ? null : hash;
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

    internal static FileChangeKind ParseStatusCode(string xy)
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

    internal static (int ahead, int behind) ParseAheadBehind(string track)
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
