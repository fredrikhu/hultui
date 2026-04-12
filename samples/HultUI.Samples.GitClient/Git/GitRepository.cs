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
            "--format", $"%(refname:short){FieldSep}%(refname){FieldSep}%(HEAD){FieldSep}%(upstream:short){FieldSep}%(upstream:track,nobracket)",
            "refs/heads/", "refs/remotes/");

        if (!result.Success) return [];

        var branches = new List<GitBranch>();
        foreach (var line in result.Lines)
        {
            var parts = line.Split(FieldSep);
            if (parts.Length < 5) continue;

            var name = parts[0];
            var fullName = parts[1];

            // Skip symbolic refs like refs/remotes/origin/HEAD
            if (fullName.EndsWith("/HEAD")) continue;

            var isCurrent = parts[2] == "*";
            var isRemote = fullName.StartsWith("refs/remotes/");
            var trackingBranch = string.IsNullOrEmpty(parts[3]) ? null : parts[3];

            var (ahead, behind) = ParseAheadBehind(parts[4]);

            branches.Add(new GitBranch(name, fullName, isCurrent, isRemote, trackingBranch, ahead, behind));
        }

        return branches;
    }

    public string GetCurrentBranch()
    {
        var result = GitProcess.Run(Path, "rev-parse", "--abbrev-ref", "HEAD");
        return result.Success ? result.Stdout.Trim() : "HEAD";
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

    public List<GitCommit> GetLog(int maxCount = 500)
    {
        var format = string.Join(FieldSep, ["%H", "%h", "%an", "%ae", "%aI", "%s", "%P", "%D"]);

        var result = GitProcess.Run(Path,
            "log",
            $"--max-count={maxCount}",
            $"--format={format}{RecordSep}",
            "--all");

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
        // suppressing output entirely.
        var result = GitProcess.Run(Path, "diff-tree", "--no-commit-id", "-r", "--numstat", "-M", "-m", "--first-parent", commitHash);
        if (!result.Success) return [];

        var changes = new List<GitFileChange>();
        foreach (var line in result.Lines)
        {
            var parts = line.Split('\t');
            if (parts.Length < 3) continue;

            int.TryParse(parts[0], out var additions);
            int.TryParse(parts[1], out var deletions);

            var path = parts[2];
            string? oldPath = null;
            var kind = FileChangeKind.Modified;

            if (path.Contains(" => "))
            {
                // Handle renames like "old => new" or "{old => new}/path"
                kind = FileChangeKind.Renamed;
                var renameParts = path.Split(" => ", 2);
                oldPath = renameParts[0];
                path = renameParts[1];
            }
            else if (additions > 0 && deletions == 0)
            {
                kind = FileChangeKind.Added;
            }
            else if (additions == 0 && deletions > 0)
            {
                kind = FileChangeKind.Deleted;
            }

            changes.Add(new GitFileChange(path, oldPath, kind, additions, deletions));
        }

        return changes;
    }

    public List<GitFileDiff> GetCommitDiff(string commitHash)
    {
        var result = GitProcess.Run(Path, "diff-tree", "--no-commit-id", "-p", "-M", "-m", "--first-parent", commitHash);
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

    public GitResult Fetch()
    {
        return GitProcess.Run(Path, "fetch", "--all", "--prune");
    }

    public GitResult Pull()
    {
        return GitProcess.Run(Path, "pull");
    }

    public GitResult Push()
    {
        return GitProcess.Run(Path, "push");
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

    // ── Cherry-pick ──

    public GitResult CherryPick(string commitHash)
    {
        return GitProcess.Run(Path, "cherry-pick", commitHash);
    }

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

    public GitResult StashCreate(string? message = null)
    {
        return string.IsNullOrWhiteSpace(message)
            ? GitProcess.Run(Path, "stash", "push", "--include-untracked")
            : GitProcess.Run(Path, "stash", "push", "--include-untracked", "-m", message);
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
