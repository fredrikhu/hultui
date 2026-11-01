using HultUI;
using HultUI.Core.Rendering;
using HultUI.Syntax;
using Junction.Git;

namespace Junction;

public partial class JunctionApp
{
    // ── Syntax highlighting for diff panes ───────────────────────────────
    //
    // A diff picks each line's spans by line number: deletions from the old side, additions and
    // context from the new. A line's colour needs the lines above it (per-hunk tokenization would
    // mis-colour multi-line comments and strings at hunk edges), so each side is tokenized from
    // its first line to the last line the diff shows of it, and not at all when the diff shows
    // none: no old side for a file that only gained lines. Results are cached; while a tokenize
    // is in flight the diff simply renders uncoloured and re-renders when the result lands.
    //
    // One tokenize runs at a time, on a worker, and it is the latest one asked for: a file, a
    // commit or a scheme the reader has left is cancelled, and what they went on to waits in
    // one slot, which the next ask takes over. A held key leaves nothing queued behind it.

    private sealed record FileHighlight(
        IReadOnlyList<TextSpan>[]? OldLines,
        IReadOnlyList<TextSpan>[]? NewLines,
        DiffShownLines? Shown = null)
    {
        public static readonly FileHighlight None = new(null, null);

        /// <summary>Whether this holds the colour of every line <paramref name="shown"/> asks
        /// for. A commit's key is its content, and two diffs of it can show different lines
        /// (with whitespace ignored, for one); a result with nothing tokenized is the same for
        /// any diff, and a failure is not retried.</summary>
        public bool Covers(DiffShownLines shown) => Shown is null || Shown.Holds(shown);
    }

    /// <summary>The lines a diff shows of each side, in rising order.</summary>
    private sealed record DiffShownLines(LineRange[] Old, LineRange[] New)
    {
        public bool Holds(DiffShownLines other) => Holds(Old, other.Old) && Holds(New, other.New);

        private static bool Holds(LineRange[] have, LineRange[] want)
        {
            int at = 0;
            foreach (var r in want)
            {
                while (at < have.Length && have[at].Last < r.First) at++;
                if (at == have.Length || have[at].First > r.First || have[at].Last < r.Last) return false;
            }
            return true;
        }

        public static DiffShownLines Of(GitFileDiff diff)
        {
            var old = new List<LineRange>();
            var @new = new List<LineRange>();
            foreach (var hunk in diff.Hunks)
                foreach (var line in hunk.Lines)
                {
                    if (line.Kind == DiffLineKind.HunkHeader) continue;
                    if (line.Kind == DiffLineKind.Deletion) Add(old, line.OldLineNumber);
                    else Add(@new, line.NewLineNumber);
                }
            return new DiffShownLines(old.ToArray(), @new.ToArray());
        }

        private static void Add(List<LineRange> ranges, int? number)
        {
            if (number is not { } n) return;
            if (ranges.Count > 0 && ranges[^1].Last >= n - 1) ranges[^1] = ranges[^1] with { Last = Math.Max(ranges[^1].Last, n) };
            else ranges.Add(new LineRange(n, n));
        }
    }

    /// <summary>One file's colours, asked for. <paramref name="Rest"/>: wait until the reader
    /// has stayed on it (<see cref="HighlightRest"/>) before starting. <paramref name="Scheme"/>:
    /// which scheme it is coloured under, of those so far.</summary>
    private sealed record HighlightAsk(string Key, GitRepository Repo, string Path, ExternalDiffMode Mode,
        string? CommitHash, string? BaseHash, DiffShownLines Shown, bool Rest, int Scheme);

    /// <summary>
    /// How long the selection stays on a file before its colours are asked for, when nothing says
    /// the reader is staying: a commit's opening file, which nothing on screen waits for yet, and
    /// a file reached while the last one was still tokenizing. A key held down moves on long
    /// before they would land: the process starts and the two file reads, which a tokenize cannot
    /// be cancelled out of, were spent for everything passed. Keys repeat every 33 ms, so this is
    /// a held key's gaps a few times over and less than a reader's next key.
    /// </summary>
    internal TimeSpan HighlightRest { get; set; } = TimeSpan.FromMilliseconds(120);

    private readonly Dictionary<string, FileHighlight> _highlights = new(StringComparer.Ordinal);

    // The worker's one tokenize, its cancel, and the ask that takes over when it ends.
    private HighlightAsk? _highlightRunning;
    private CancellationTokenSource? _highlightCancel;
    private HighlightAsk? _highlightNext;
    private int _highlightScheme;

    // The lines of the diff asked about last: every build asks.
    private (GitFileDiff? Diff, DiffShownLines? Shown) _highlightLinesOf;

    /// <summary>Tokenizes under way or waiting, for tests. One cancelled is not one of them.</summary>
    internal int HighlightsPending =>
        (HighlightRunningLive ? 1 : 0) + (_highlightNext is null ? 0 : 1);

    /// <summary>Lines walked by the tokenizes that landed so far, for tests.</summary>
    internal int HighlightLinesTokenized { get; private set; }

    /// <summary>Tokenizes that got past their rest and read the file, for tests.</summary>
    internal int HighlightsRead => _highlightsRead;

    private int _highlightsRead;

    private bool HighlightRunningLive =>
        _highlightRunning is not null && _highlightCancel is { IsCancellationRequested: false };

    /// <summary>
    /// The highlight the diff on screen asked for in the last build, or null when the build
    /// showed no diff. A tokenize that lands for this key is what that build drew uncoloured,
    /// and only then is a rebuild owed. Any other — the prefetch started from History, a file
    /// the reader has already moved past, a working-tree version a refresh replaced — goes into
    /// the cache without one, and the build that shows it later finds it there. Build clears
    /// it; <see cref="HighlightForCurrent"/> sets it.
    /// </summary>
    private string? _highlightShown;

    /// <summary>
    /// Throw away spans coloured by the previous theme. Spans are plain colours once handed
    /// out — nothing about one says which theme produced it — so a scheme change leaves every
    /// tokenized diff carrying the old palette until the cache is dropped and the next build
    /// asks again. A tokenize in flight is cancelled, and what it made, if it got that far, is
    /// not kept: the next build's ask waits for it to stop and runs under the new theme.
    /// </summary>
    private void RetokenizeCode()
    {
        _highlights.Clear();
        _highlightScheme++;
        _highlightNext = null;
        _highlightCancel?.Cancel();
    }

    /// <summary>The highlight for <paramref name="diff"/> in the active view, or null while none
    /// is available (still tokenizing, in which case a rebuild follows). Called from the build
    /// that puts the diff on screen, and only from there: it is what marks the result as one the
    /// screen is waiting for (see <see cref="_highlightShown"/>).</summary>
    private FileHighlight? HighlightForCurrent(GitFileDiff diff)
    {
        if (diff.Path.Length == 0) return null;
        if (View == JunctionView.Commit)
        {
            // Multi-select compare: old side is the file at the oldest selected
            // commit, new side at the newest (matches GetRangeDiff's line numbers).
            if (_detailRange is { } r)
                return HighlightFor(diff, ExternalDiffMode.Commit, r.HeadHash, r.BaseHash, onScreen: true);
            if (_detailHash is { } hash)
                return HighlightFor(diff, ExternalDiffMode.Commit, hash, onScreen: true);
        }
        if (View == JunctionView.Changes)
            return HighlightFor(diff, _changeSelStaged ? ExternalDiffMode.Staged : ExternalDiffMode.Unstaged, null,
                onScreen: true);
        return null;
    }

    /// <param name="rest">A prefetch from the log: wait for the selection to stay.</param>
    private FileHighlight? HighlightFor(GitFileDiff diff, ExternalDiffMode mode, string? commitHash,
        string? baseHash = null, bool onScreen = false, bool rest = false)
    {
        if (ActiveRepo is not { } repo) return null;
        var path = diff.Path;
        if (!SyntaxHighlighter.CanHighlight(path)) return null;

        // Commit content is immutable → content-addressed key (old ref explicit so
        // a single-commit view and a range compare ending at the same commit don't
        // collide). Working/index content changes with the repo's data generation,
        // exactly like the Changes loader, so stale highlights die with the gen bump.
        string key = mode == ExternalDiffMode.Commit
            ? $"{repo.Path}|{baseHash ?? commitHash + "^"}..{commitHash}|{path}"
            : $"{repo.Path}|{DataGen(_activeRepoPath)}|{mode}|{path}";

        if (onScreen) _highlightShown = key;
        if (!ReferenceEquals(_highlightLinesOf.Diff, diff)) _highlightLinesOf = (diff, DiffShownLines.Of(diff));
        var shown = _highlightLinesOf.Shown!;

        _highlights.TryGetValue(key, out var hit);
        if (hit is not null && hit.Covers(shown)) return hit;

        // Already on its way, unless all it is doing is waiting for a selection to stay: the
        // screen is waiting now.
        if (HighlightRunningLive && _highlightRunning is { } run && run.Key == key && run.Shown.Holds(shown)
            && !(onScreen && run.Rest)) return hit;
        if (_highlightNext is { } next && next.Key == key && next.Shown.Holds(shown) && !(onScreen && next.Rest)) return hit;

        // Another file's tokenize still going: the reader went past it, and may be going past
        // this one. Each step of a walk would otherwise read two whole files for nothing, which
        // on files of 8,000 lines is two large-heap strings a key. Nothing is lost by the file
        // they stop on but the rest.
        rest |= HighlightRunningLive && _highlightRunning!.Key != key;
        AskHighlight(new HighlightAsk(key, repo, path, mode, commitHash, baseHash, shown, rest, _highlightScheme));
        return hit;   // what is known so far, if anything: the rest follows
    }

    /// <summary>The worker takes <paramref name="ask"/> next: at once when it is free, or when
    /// the end of what it is doing. A tokenize the screen is not waiting for is cancelled for
    /// it; one the screen is waiting for is not, for a prefetch.</summary>
    private void AskHighlight(HighlightAsk ask)
    {
        if (_highlightRunning is not { } running)
        {
            StartHighlight(ask);
            return;
        }
        if (running.Key != _highlightShown || running.Rest) _highlightCancel?.Cancel();
        if (_highlightNext is null || _highlightNext.Key != _highlightShown) _highlightNext = ask;
    }

    /// <summary>The selection moved: whatever was being tokenized for the last one is not wanted
    /// any more, unless the diff on screen is still waiting for it.</summary>
    private void DropHighlightPrefetch()
    {
        if (_highlightNext?.Key != _highlightShown) _highlightNext = null;
        if (_highlightRunning is { } running && running.Key != _highlightShown) _highlightCancel?.Cancel();
    }

    private void StartHighlight(HighlightAsk ask)
    {
        var cancel = new CancellationTokenSource();
        _highlightRunning = ask;
        _highlightCancel = cancel;
        var (clock, rest) = (_app.Time == TimeProvider.System ? TimeProvider.System : NoBudget, HighlightRest);
        var token = cancel.Token;

        Task.Run(async () =>
        {
            FileHighlight? result = null;
            int lines = 0;
            try
            {
                if (ask.Rest) await Task.Delay(rest, token);
                Interlocked.Increment(ref _highlightsRead);
                (result, lines) = Tokenize(ask, clock, token);
            }
            catch (OperationCanceledException)
            {
                // Replaced. Whoever wants it now will ask.
            }
            catch
            {
                result = FileHighlight.None;   // cache the failure so it isn't retried every build
            }

            Application.InvokeOnMainThread(() => HighlightLanded(ask, result, lines));
        });
    }

    /// <summary>What a tokenize's budgets run on outside a window. A headless session's clock
    /// moves with its frames, sixteen milliseconds each however few a worker has had, so it would
    /// run out on a file a test pumps its way through; on this one the whole text is coloured
    /// on every run.</summary>
    private static readonly TimeProvider NoBudget = new StandingClock();

    private sealed class StandingClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }

    // Runs off the UI thread.
    private static (FileHighlight, int Lines) Tokenize(HighlightAsk ask, TimeProvider clock, CancellationToken token)
    {
        var (repo, path, shown) = (ask.Repo, ask.Path, ask.Shown);
        bool wantOld = shown.Old.Length > 0, wantNew = shown.New.Length > 0;
        string? oldText = null, newText = null;
        if (wantOld)
            oldText = ask.Mode switch
            {
                ExternalDiffMode.Commit => repo.GetFileText(ask.BaseHash ?? ask.CommitHash + "^", path),
                ExternalDiffMode.Staged => repo.GetFileText("HEAD", path),
                _ => repo.GetIndexFileText(path) ?? repo.GetFileText("HEAD", path),
            };
        token.ThrowIfCancellationRequested();
        if (wantNew)
            newText = ask.Mode switch
            {
                ExternalDiffMode.Commit => repo.GetFileText(ask.CommitHash!, path),
                ExternalDiffMode.Staged => repo.GetIndexFileText(path),
                _ => repo.GetWorkingFileText(path),
            };
        var sides = SyntaxHighlighter.TokenizeDiff(path, oldText, shown.Old, newText, shown.New,
            SyntaxHighlighter.DefaultMaxLines, token, clock);
        return (new FileHighlight(sides.Old, sides.New, shown), sides.LinesWalked);
    }

    private void HighlightLanded(HighlightAsk ask, FileHighlight? result, int lines)
    {
        _highlightRunning = null;
        _highlightCancel?.Dispose();
        _highlightCancel = null;
        HighlightLinesTokenized += lines;

        // A tokenize that finished before the ask that replaced it is as good as any: the reader
        // may well come back to the file. A cancelled one that stopped short has no result.
        if (result is not null && ask.Scheme == _highlightScheme)
        {
            if (_highlights.Count >= 128) _highlights.Clear();   // crude cap; entries are per-file-version
            _highlights[ask.Key] = result;
            // Re-render the diff rows with their colours — when the diff on screen is the
            // one waiting for them. The History prefetch lands while the log is on screen,
            // and rebuilding the whole window for it doubled the cost of every move a
            // reader made through the log (two builds a key instead of one). The view's
            // alone: colours are nothing the chrome shows (it built 0.45 times a key in the
            // Commit view's file walk).
            if (ask.Key == _highlightShown) MarkViewDirty();
        }

        if (_highlightNext is { } next)
        {
            _highlightNext = null;
            StartHighlight(next);
        }
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
