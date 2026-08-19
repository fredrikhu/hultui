using HultUI;
using HultUI.Core.Rendering;
using Junction.Git;

namespace Junction;

public partial class JunctionApp
{
    // ── Syntax highlighting for diff panes ───────────────────────────────
    //
    // Both sides of the selected file are tokenized in full (per-hunk
    // tokenization would mis-color multi-line comments/strings at hunk edges)
    // on a background thread, then diff lines pick their spans by line number:
    // deletions from the old side, additions/context from the new. Results are
    // cached; while a tokenize is in flight the diff simply renders uncolored
    // and re-renders when the result lands.

    private sealed record FileHighlight(
        IReadOnlyList<TextSpan>[]? OldLines,
        IReadOnlyList<TextSpan>[]? NewLines)
    {
        public static readonly FileHighlight None = new(null, null);
    }

    private readonly Dictionary<string, FileHighlight> _highlights = new(StringComparer.Ordinal);
    private readonly HashSet<string> _highlightsPending = new(StringComparer.Ordinal);

    /// <summary>
    /// Throw away spans coloured by the previous theme. Spans are plain colours once handed
    /// out — nothing about one says which theme produced it — so a scheme change leaves every
    /// tokenized diff carrying the old palette until the cache is dropped and the next build
    /// asks again. Tokenizes in flight are left alone: they land in the cache keyed as they
    /// were, and the entry is simply asked for again.
    /// </summary>
    private void RetokenizeCode()
    {
        _highlights.Clear();
        _highlightsPending.Clear();
    }

    /// <summary>The highlight for the diff the active view is showing for
    /// <paramref name="path"/>, or null while none is available (unsupported
    /// language, or still tokenizing — in which case a rebuild follows).</summary>
    private FileHighlight? HighlightForCurrent(string path)
    {
        if (path.Length == 0) return null;
        if (View == JunctionView.Commit)
        {
            // Multi-select compare: old side is the file at the oldest selected
            // commit, new side at the newest (matches GetRangeDiff's line numbers).
            if (_detailRange is { } r)
                return HighlightFor(path, ExternalDiffMode.Commit, r.HeadHash, r.BaseHash);
            if (_detailHash is { } hash)
                return HighlightFor(path, ExternalDiffMode.Commit, hash);
        }
        if (View == JunctionView.Changes)
            return HighlightFor(path, _changeSelStaged ? ExternalDiffMode.Staged : ExternalDiffMode.Unstaged, null);
        return null;
    }

    private FileHighlight? HighlightFor(string path, ExternalDiffMode mode, string? commitHash,
        string? baseHash = null)
    {
        if (ActiveRepo is not { } repo) return null;

        // Commit content is immutable → content-addressed key (old ref explicit so
        // a single-commit view and a range compare ending at the same commit don't
        // collide). Working/index content changes with the repo's data generation,
        // exactly like the Changes loader, so stale highlights die with the gen bump.
        string key = mode == ExternalDiffMode.Commit
            ? $"{repo.Path}|{baseHash ?? commitHash + "^"}..{commitHash}|{path}"
            : $"{repo.Path}|{DataGen(_activeRepoPath)}|{mode}|{path}";

        if (_highlights.TryGetValue(key, out var hit)) return hit;
        if (!_highlightsPending.Add(key)) return null;   // already tokenizing

        Task.Run(() =>
        {
            FileHighlight result;
            try
            {
                var (oldText, newText) = mode switch
                {
                    ExternalDiffMode.Commit => (
                        repo.GetFileText(baseHash ?? commitHash + "^", path),
                        repo.GetFileText(commitHash!, path)),
                    ExternalDiffMode.Staged => (
                        repo.GetFileText("HEAD", path),
                        repo.GetIndexFileText(path)),
                    _ => (
                        repo.GetIndexFileText(path) ?? repo.GetFileText("HEAD", path),
                        repo.GetWorkingFileText(path)),
                };
                result = new FileHighlight(
                    oldText == null ? null : SyntaxHighlighter.TokenizeLines(path, oldText),
                    newText == null ? null : SyntaxHighlighter.TokenizeLines(path, newText));
            }
            catch
            {
                result = FileHighlight.None;   // cache the failure so it isn't retried every build
            }

            Application.InvokeOnMainThread(() =>
            {
                _highlightsPending.Remove(key);
                if (_highlights.Count >= 128) _highlights.Clear();   // crude cap; entries are per-file-version
                _highlights[key] = result;
                MarkDirty();   // re-render the diff rows with their colors
            });
        });
        return null;
    }

    // ── Mapping diff lines onto tokenized file lines ─────────────────────

    private static IReadOnlyList<TextSpan>? SpansFor(FileHighlight? hl, GitDiffLine line) =>
        line.Kind == DiffLineKind.Deletion
            ? LineSpans(hl?.OldLines, line.OldLineNumber)
            : LineSpans(hl?.NewLines, line.NewLineNumber);

    private static IReadOnlyList<TextSpan>? LineSpans(IReadOnlyList<TextSpan>[]? lines, int? lineNumber) =>
        lines != null && lineNumber is { } n && n >= 1 && n <= lines.Length && lines[n - 1].Count > 0
            ? lines[n - 1]
            : null;
}
