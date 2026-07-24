using System.Text;

namespace GitClient.Git;

public enum StageOp
{
    /// <summary>Apply the selected changes forward to the index (git apply --cached).</summary>
    Stage,
    /// <summary>Reverse-apply the selected staged changes out of the index (--cached --reverse).</summary>
    Unstage,
    /// <summary>Reverse-apply the selected changes out of the working tree (--reverse).</summary>
    Discard,
}

/// <summary>
/// Synthesizes a minimal unified-diff patch for a <b>subset</b> of a file's diff
/// lines, suitable for <c>git apply</c>, so individual lines or hunks can be
/// staged / unstaged / discarded (the core of interactive staging).
/// </summary>
/// <remarks>
/// The patch's source side must equal the file git will apply it against:
/// <list type="bullet">
/// <item>Stage applies <b>forward</b> to the index — the index is the <i>old</i> side.
/// So an unselected addition (not in the index) is dropped, and an unselected
/// deletion (still in the index) is kept as context.</item>
/// <item>Unstage / Discard apply in <b>reverse</b> — the index / working tree is the
/// <i>new</i> side. The mapping flips: an unselected addition (present) becomes
/// context, an unselected deletion (absent) is dropped.</item>
/// </list>
/// Hunk headers are recomputed from the kept lines; a running delta keeps each
/// hunk's new-start consistent within the file.
/// </remarks>
public static class GitPatchBuilder
{
    /// <summary>
    /// Builds the patch for the lines in <paramref name="selected"/> (identified by
    /// value equality — each changed line is unique by side + number). Returns null
    /// when no selected line lands in any hunk (nothing to apply).
    /// </summary>
    public static string? BuildSelectionPatch(GitFileDiff file, StageOp op, ISet<GitDiffLine> selected)
    {
        bool sourceOld = op == StageOp.Stage;   // false ⇒ reverse apply (new side is the source)

        var body = new StringBuilder();
        int runningDelta = 0;
        bool anyHunk = false;

        foreach (var hunk in file.Hunks)
        {
            bool hunkHasSelection = hunk.Lines.Any(l =>
                l.Kind is DiffLineKind.Addition or DiffLineKind.Deletion && selected.Contains(l));
            if (!hunkHasSelection) continue;

            var outLines = new List<string>();
            int oldCount = 0, newCount = 0;

            void Emit(char prefix, GitDiffLine line)
            {
                outLines.Add(prefix + line.Content);
                if (line.NoNewlineAfter) outLines.Add("\\ No newline at end of file");
            }

            foreach (var line in hunk.Lines)
            {
                switch (line.Kind)
                {
                    case DiffLineKind.HunkHeader:
                        continue;
                    case DiffLineKind.Context:
                        Emit(' ', line); oldCount++; newCount++;
                        break;
                    case DiffLineKind.Addition:
                        if (selected.Contains(line)) { Emit('+', line); newCount++; }
                        else if (sourceOld) { /* not in the index yet — drop */ }
                        else { Emit(' ', line); oldCount++; newCount++; }   // keep staged: context
                        break;
                    case DiffLineKind.Deletion:
                        if (selected.Contains(line)) { Emit('-', line); oldCount++; }
                        else if (sourceOld) { Emit(' ', line); oldCount++; newCount++; }   // still in index: context
                        else { /* not on the new side — drop */ }
                        break;
                }
            }

            int oldStart = ParseOldStart(hunk.Header);
            int newStart = oldStart + runningDelta;
            body.Append($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@\n");
            foreach (var ol in outLines) body.Append(ol).Append('\n');
            runningDelta += newCount - oldCount;
            anyHunk = true;
        }

        if (!anyHunk) return null;

        var sb = new StringBuilder();
        sb.Append($"diff --git a/{file.Path} b/{file.Path}\n");
        sb.Append(file.OldPathHeader ?? $"--- a/{file.Path}").Append('\n');
        sb.Append(file.NewPathHeader ?? $"+++ b/{file.Path}").Append('\n');
        sb.Append(body);
        return sb.ToString();
    }

    /// <summary>Parses the old-side start line from a "@@ -a,b +c,d @@" header
    /// (count omitted ⇒ 1). Falls back to 1 on a malformed header.</summary>
    private static int ParseOldStart(string header)
    {
        var at = header.IndexOf('-');
        if (at < 0) return 1;
        int i = at + 1, start = i;
        while (i < header.Length && char.IsDigit(header[i])) i++;
        return i > start && int.TryParse(header[start..i], out var n) ? n : 1;
    }
}
