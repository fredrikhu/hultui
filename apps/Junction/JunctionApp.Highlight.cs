using System.Runtime.InteropServices;
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
    //
    // A cancelled tokenize is not wasted: its file's two texts are kept (_highlightTexts), and
    // SyntaxHighlighter keeps where the walk got to with them, so coming back to the file goes on
    // from there and reads nothing.
    //
    // A working-tree or index diff (the Changes view's) is keyed by the data generation, which
    // every working-tree event moves: an editor's save of any file, a new untracked file. Its
    // texts mostly did not change, so a tokenize at the new generation reads them and, when they
    // hash the same as the last generation's (_highlightSeen), takes that one's colours at once,
    // or goes on with the walk kept with its texts, instead of walking from line 1 again.

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

        /// <summary>These lines and <paramref name="margin"/> more at both ends of every range, on both
        /// sides; ranges that meet become one.</summary>
        public DiffShownLines Widened(int margin) => new(Widen(Old, margin), Widen(New, margin));

        private static LineRange[] Widen(LineRange[] ranges, int margin)
        {
            var wide = new List<LineRange>(ranges.Length);
            foreach (var r in ranges)
            {
                var w = new LineRange(Math.Max(1, r.First - margin), r.Last + margin);
                if (wide.Count > 0 && wide[^1].Last >= w.First - 1) wide[^1] = wide[^1] with { Last = w.Last };
                else wide.Add(w);
            }
            return wide.ToArray();
        }

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

    /// <summary>One file's colours, asked for. <paramref name="OldPath"/>: where its old side is,
    /// the name before a rename (<see cref="GitFileDiff.RenamedFrom"/>), else its path; read at the
    /// new name, a renamed file's old side was not found and its deleted lines stayed plain.
    /// <paramref name="Rest"/>: wait until the reader has stayed on it (<see cref="HighlightRest"/>)
    /// before starting. <paramref name="Scheme"/>: which scheme it is coloured under, of those so
    /// far. <paramref name="Shown"/>: the lines to colour; <paramref name="DiffLines"/>: the lines of
    /// the diff it was asked for, which <paramref name="Shown"/> may reach past (<see cref="SaveAskMargin"/>).</summary>
    private sealed record HighlightAsk(string Key, GitRepository Repo, string Path, string OldPath, ExternalDiffMode Mode,
        string? CommitHash, string? BaseHash, DiffShownLines Shown, bool Rest, int Scheme, DiffShownLines DiffLines);

    /// <summary>
    /// How long the selection stays on a file before its colours are asked for, when nothing says
    /// the reader is staying: a commit's opening file, which nothing on screen waits for yet, and
    /// a file reached while the last one was still tokenizing. A key held down moves on long
    /// before they would land: the process starts and the two file reads, which a tokenize cannot
    /// be cancelled out of, were spent for everything passed. Keys repeat every 33 ms, so this is
    /// a held key's gaps a few times over and less than a reader's next key.
    /// </summary>
    internal TimeSpan HighlightRest { get; set; } = TimeSpan.FromMilliseconds(120);

    /// <summary>For tests: what a tokenize waits for, after its rest and before it reads the file;
    /// null waits for nothing. A test that has to see a diff drawn plain before its colours land
    /// holds them with it. Left to the clock, they land within the frames the test pumps where git
    /// starts in a few milliseconds or the frames are slow: on Fedora 17–30 ms after the plain rows,
    /// in the first or second of three frames, and on Windows in a process that runs nothing
    /// before it (plan 17 round 15). The app's own cancel ends the wait.</summary>
    internal Task? HighlightHold { get; set; }

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

    /// <summary>Tokenizes that got past their rest, to read the file or go on with its kept texts
    /// (<see cref="HighlightFileReads"/> counts the reads), for tests.</summary>
    internal int HighlightsRead => _highlightsRead;

    /// <summary>Tokenizes whose result is on its way to the UI thread, for tests: what lands next
    /// frame lands after it.</summary>
    internal int HighlightsPosted => Volatile.Read(ref _highlightsPosted);

    /// <summary>Tokenizes that read their file's texts, rather than going on with kept ones, for
    /// tests.</summary>
    internal int HighlightFileReads => Volatile.Read(ref _highlightFileReads);

    private int _highlightsRead, _highlightsPosted, _highlightFileReads;

    /// <summary>A file's two versions as a tokenize read them; a side is read only when the diff
    /// shows lines of it. <see cref="Bytes"/>: the texts, the array of a slot a line that the walk
    /// kept with them fills (SyntaxHighlighter's spans of the lines shown are what the colours keep
    /// anyway), and with two versions the 9 bytes a line of the new one that the walk keeps for the
    /// old one's to take (the state each line left, and what it can give). <see cref="Hashes"/>: of
    /// a working-tree or index diff's texts, made where they were read. <see cref="Walked"/>: the
    /// lines the walk with these texts was asked for, which SyntaxHighlighter goes on with only when
    /// it is asked for the same lines.</summary>
    private sealed record HighlightTexts(string? Old, bool OldRead, string? New, bool NewRead)
    {
        public long Bytes { get; } = Size(Old, 8) + Size(New, Old is null ? 8 : 8 + 9);

        public TextHashes Hashes { get; init; }

        public DiffShownLines? Walked { get; init; }

        private static long Size(string? text, int perLine) =>
            text is null ? 0 : 2L * text.Length + (long)perLine * (text.AsSpan().Count('\n') + 1);
    }

    /// <summary>A hash of each side of a file a tokenize read: 0 for a side it did not read, 1 for
    /// one with no text (the file is not in the index, say), else a 64-bit hash of its characters,
    /// two of the runtime's 32-bit hashes (xxHash32 and Marvin, both seeded per process).</summary>
    private readonly record struct TextHashes(ulong Old, ulong New)
    {
        public static TextHashes Of(string? old, bool oldRead, string? @new, bool newRead) =>
            new(Hash(old, oldRead), Hash(@new, newRead));

        private static ulong Hash(string? text, bool read)
        {
            if (!read) return 0;
            if (text is null) return 1;
            var chars = text.AsSpan();
            var xx = new HashCode();
            xx.AddBytes(MemoryMarshal.AsBytes(chars));
            return ((ulong)(uint)xx.ToHashCode() << 32 | (uint)string.GetHashCode(chars)) | 2;
        }
    }

    /// <summary>
    /// The last tokenize of each working-tree or index diff's file (the Changes view's), by
    /// repository, mode and path (<see cref="FileOf"/>): the key it ran under, and the hashes of the
    /// texts it read. Every working-tree event moves the key's data generation, and an editor's save
    /// of another file, or a new untracked one, leaves this file's texts as they were: the file was
    /// read and walked from line 1 again, the diff on screen drew uncoloured until the walk landed
    /// (bigrepo's 675 KB file: 13,623 lines, 324 MB, 2.7 s), and the texts kept with a cancelled walk
    /// could never be found again (plan 17 round 17). A tokenize at the new generation still reads
    /// the file, to hash it; when the hashes are these, it takes what the last one left (see
    /// <see cref="PriorOf"/>). Whatever the hashes, what the last generation left goes when the new
    /// one's tokenize ends: no build asks for that key again.
    /// </summary>
    private readonly Dictionary<string, HighlightSeen> _highlightSeen = new(StringComparer.Ordinal);

    private readonly record struct HighlightSeen(string Key, TextHashes Hashes);

    /// <summary>What a working-tree or index diff's last generation left that a tokenize of the next
    /// takes when its texts hash to <see cref="Hashes"/>: the colours, when they hold the lines the
    /// diff shows, or else the texts kept with a cancelled walk.</summary>
    private sealed record HighlightPrior(TextHashes Hashes, FileHighlight? Colours, HighlightTexts? Texts);

    private static string FileOf(HighlightAsk ask) => $"{ask.Repo.Path}|{ask.Mode}|{ask.Path}";

    /// <summary>The <see cref="HighlightPrior"/> for <paramref name="ask"/>, or null: a commit's
    /// key is its content, and a file seen at this key already has it.</summary>
    private HighlightPrior? PriorOf(HighlightAsk ask)
    {
        if (ask.Mode == ExternalDiffMode.Commit || !_highlightSeen.TryGetValue(FileOf(ask), out var seen)
            || seen.Key == ask.Key) return null;
        var colours = _highlights.GetValueOrDefault(seen.Key);
        if (colours is not null && !colours.Covers(ask.DiffLines)) colours = null;
        var texts = colours is null ? _highlightTexts.GetValueOrDefault(seen.Key) : null;
        return colours is null && texts is null ? null : new HighlightPrior(seen.Hashes, colours, texts);
    }

    /// <summary>A tokenize of a working-tree or index diff read <paramref name="texts"/> at
    /// <paramref name="ask"/>'s key, or went on with them: the file's last generation is done with.</summary>
    private void SeeHighlightFile(HighlightAsk ask, HighlightTexts texts)
    {
        string file = FileOf(ask);
        if (_highlightSeen.TryGetValue(file, out var was) && was.Key != ask.Key)
        {
            ForgetHighlightTexts(was.Key);   // carried over to this key, or of other characters
            _highlights.Remove(was.Key);
        }
        _highlightSeen[file] = new HighlightSeen(ask.Key, texts.Hashes);
    }

    /// <summary>
    /// The texts of the files whose colours were cancelled part-way, by highlight key, the last
    /// touched at the end of <see cref="_highlightTextsOrder"/>. A tokenize handed the same strings
    /// goes on where the one before it stopped, since SyntaxHighlighter keeps a cancelled walk with
    /// its texts, and reads nothing. A reader going through big files at a reading pace cancels
    /// every walk: on bigrepo's 675 KB files each visit went back to line 1 and read both versions
    /// again, 2.6 MB of large-heap strings, and never got further (plan 17 round 16). Dropped when
    /// the colours land, and on a scheme change; held to <see cref="MaxKeptHighlightBytes"/>.
    /// </summary>
    private readonly Dictionary<string, HighlightTexts> _highlightTexts = new(StringComparer.Ordinal);
    private readonly List<string> _highlightTextsOrder = [];

    /// <summary>The most <see cref="_highlightTexts"/> holds: five of bigrepo's 675 KB files
    /// (2.7 MB of text a pair, 2.8 with what the walk keeps a line), the files a reader pages through
    /// before turning back, and a pair of the longest texts coloured at all (2,000,000 characters
    /// a side, 8 MB). Files of ordinary size, which finish before a key cancels them, keep
    /// nothing.</summary>
    internal const long MaxKeptHighlightBytes = 16L << 20;

    /// <summary>What <see cref="_highlightTexts"/> holds, for tests.</summary>
    internal long HighlightTextsKeptBytes { get; private set; }

    private void KeepHighlightTexts(string key, HighlightTexts texts)
    {
        ForgetHighlightTexts(key);
        if (texts.Bytes > MaxKeptHighlightBytes) return;
        _highlightTexts[key] = texts;
        _highlightTextsOrder.Add(key);
        HighlightTextsKeptBytes += texts.Bytes;
        _collectWhenIdle = true;
        while (HighlightTextsKeptBytes > MaxKeptHighlightBytes) ForgetHighlightTexts(_highlightTextsOrder[0]);
    }

    // ── Giving the walk's memory back ────────────────────────────────────
    //
    // A big file's walk allocates some 300 MB, nearly all of it garbage at once, and the texts kept
    // for it are large objects that survive. The collector sizes what it keeps committed from what
    // survives, and the kept texts also take away the large-heap reads that brought gen-2
    // collections (5-6 -> 0-1 per 12 revisits): after a walk through bigrepo the window held 202.7 MB
    // working set, 92 MB of it committed to the heap for 86 MB of objects, against 154.0 MB with no
    // texts kept, and a forced compacting gen-2 collection still left 72 MB committed for 24 MB live
    // (plan 17 round 17). Nothing at rest collects again. So once the worker has stood idle for
    // HighlightIdleCollect after a walk that kept texts, one aggressive collection runs, on the UI
    // thread between frames: it compacts (the large objects too) and gives the free memory back to
    // the system, 202.7 -> 134.0 MB in that window, in 8-12 ms. The kept texts stay. A compacting
    // gen-2 collection (the large objects too) kept the committed memory, 182 MB; a smaller bound or
    // ConserveMemory brought the gen-2s back and kept more (docs/DECISIONS.md).

    /// <summary>How long the worker stands idle, nothing started and nothing waiting, before the one
    /// collection that gives a walk's memory back; set by tests.</summary>
    internal TimeSpan HighlightIdleCollect { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>The idle collections run so far, for tests.</summary>
    internal int HighlightIdleCollections { get; private set; }

    // Texts were kept since the last idle collection; tokenizes started so far, which an idle check
    // compares with the count when it was scheduled.
    private bool _collectWhenIdle;
    private int _highlightStarts;

    private void CollectWhenIdle()
    {
        int starts = _highlightStarts;
        _ = Task.Delay(HighlightIdleCollect).ContinueWith(_ => Application.InvokeOnMainThread(() =>
        {
            if (!_collectWhenIdle || starts != _highlightStarts || _highlightRunning is not null || _highlightNext is not null)
                return;
            _collectWhenIdle = false;
            HighlightIdleCollections++;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }), TaskScheduler.Default);
    }

    private void ForgetHighlightTexts(string key)
    {
        if (!_highlightTexts.Remove(key, out var was)) return;
        _highlightTextsOrder.Remove(key);
        HighlightTextsKeptBytes -= was.Bytes;
    }

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
        _highlightTexts.Clear();   // the walks kept with them are the old theme's
        _highlightTextsOrder.Clear();
        HighlightTextsKeptBytes = 0;
        _highlightSeen.Clear();
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

    /// <summary>
    /// How many lines past each end of the diff's ranges the colours a save asks for before its rows
    /// land reach (<see cref="ReloadChangesView"/>). That ask is made for the diff on screen, and the
    /// rows that land are the diff after the save: a save that adds lines inside a hunk, or a line
    /// that joins one, shows lines the ask did not cover, and the file was tokenized a second time
    /// and the view built twice (plan 17 round 15). Eight covers a line added or removed anywhere a
    /// hunk's three lines of context reach (it grows by seven at most) and a block of up to eight
    /// lines pasted inside one; the walk runs from the file's first line either way.
    /// </summary>
    internal const int SaveAskMargin = 8;

    /// <param name="rest">A prefetch from the log: wait for the selection to stay.</param>
    /// <param name="margin">Lines to colour past both ends of every range the diff shows
    /// (<see cref="SaveAskMargin"/>).</param>
    private FileHighlight? HighlightFor(GitFileDiff diff, ExternalDiffMode mode, string? commitHash,
        string? baseHash = null, bool onScreen = false, bool rest = false, int margin = 0)
    {
        if (ActiveRepo is not { } repo) return null;
        var path = diff.Path;
        if (!SyntaxHighlighter.CanHighlight(path)) return null;

        // Commit content is immutable → content-addressed key (old ref explicit so
        // a single-commit view and a range compare ending at the same commit don't
        // collide). Working/index content changes with the repo's data generation,
        // exactly like the Changes loader; a generation whose texts hash the same as
        // the last one's takes its colours (_highlightSeen).
        string key = mode == ExternalDiffMode.Commit
            ? $"{repo.Path}|{baseHash ?? commitHash + "^"}..{commitHash}|{path}"
            : $"{repo.Path}|{ChangesGen(_activeRepoPath)}|{mode}|{path}";

        if (onScreen) _highlightShown = key;
        if (!ReferenceEquals(_highlightLinesOf.Diff, diff)) _highlightLinesOf = (diff, DiffShownLines.Of(diff));
        var shown = _highlightLinesOf.Shown!;
        if (margin > 0) shown = shown.Widened(margin);

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
        AskHighlight(new HighlightAsk(key, repo, path, diff.RenamedFrom ?? path, mode, commitHash, baseHash, shown, rest,
            _highlightScheme, _highlightLinesOf.Shown!));
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
        _highlightStarts++;
        var (clock, rest, hold) = (_app.Time == TimeProvider.System ? TimeProvider.System : NoBudget, HighlightRest, HighlightHold);
        var token = cancel.Token;
        var texts = _highlightTexts.GetValueOrDefault(ask.Key);
        var prior = texts is null ? PriorOf(ask) : null;

        Task.Run(async () =>
        {
            FileHighlight? result = null;
            int lines = 0;
            try
            {
                if (ask.Rest) await Task.Delay(rest, token);
                if (hold is not null) await hold.WaitAsync(token);
                Interlocked.Increment(ref _highlightsRead);
                texts = ReadHighlightTexts(ask, texts, token);
                if (prior is not null && prior.Hashes == texts.Hashes)
                {
                    // The file as it was at the generation before: its colours, or the strings a
                    // walk was kept with, in place of the same characters read again.
                    if (prior.Colours is { } colours) result = colours;
                    else texts = prior.Texts!;
                }
                if (result is null)
                {
                    // A walk kept with these texts goes on only for the lines it was asked for: those,
                    // when they hold the diff's (a save's ask reaches past them).
                    var shown = texts.Walked is { } walked && walked.Holds(ask.DiffLines) ? walked : ask.Shown;
                    if (!ReferenceEquals(shown, texts.Walked)) texts = texts with { Walked = shown };
                    (result, lines) = Tokenize(ask.Path, shown, texts, clock, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Replaced. Whoever wants it now will ask, and goes on from where this got to.
            }
            catch
            {
                result = FileHighlight.None;   // cache the failure so it isn't retried every build
            }

            Application.InvokeOnMainThread(() => HighlightLanded(ask, result, lines, texts));
            Interlocked.Increment(ref _highlightsPosted);
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

    /// <summary>The sides of the file <paramref name="ask"/> shows lines of, those in
    /// <paramref name="kept"/> as they are and the others read. Runs off the UI thread. A file's
    /// key is its content (a commit's, or the working tree's at one data generation), so a kept
    /// text is the one a read would give.</summary>
    private HighlightTexts ReadHighlightTexts(HighlightAsk ask, HighlightTexts? kept, CancellationToken token)
    {
        var (repo, path, oldPath, shown) = (ask.Repo, ask.Path, ask.OldPath, ask.Shown);
        string? oldText = kept?.Old, newText = kept?.New;
        bool oldRead = kept?.OldRead == true, newRead = kept?.NewRead == true, read = false;
        if (shown.Old.Length > 0 && !oldRead)
        {
            oldText = ask.Mode switch
            {
                ExternalDiffMode.Commit => repo.GetFileText(ask.BaseHash ?? ask.CommitHash + "^", oldPath),
                ExternalDiffMode.Staged => repo.GetFileText("HEAD", oldPath),
                _ => repo.GetIndexFileText(oldPath) ?? repo.GetFileText("HEAD", oldPath),
            };
            oldRead = read = true;
        }
        token.ThrowIfCancellationRequested();
        if (shown.New.Length > 0 && !newRead)
        {
            newText = ask.Mode switch
            {
                ExternalDiffMode.Commit => repo.GetFileText(ask.CommitHash!, path),
                ExternalDiffMode.Staged => repo.GetIndexFileText(path),
                _ => repo.GetWorkingFileText(path),
            };
            newRead = read = true;
        }
        if (!read && kept is not null) return kept;
        Interlocked.Increment(ref _highlightFileReads);
        return new HighlightTexts(oldText, oldRead, newText, newRead)
        {
            Hashes = ask.Mode == ExternalDiffMode.Commit ? default : TextHashes.Of(oldText, oldRead, newText, newRead),
        };
    }

    // Runs off the UI thread.
    private static (FileHighlight, int Lines) Tokenize(string path, DiffShownLines shown, HighlightTexts texts,
        TimeProvider clock, CancellationToken token)
    {
        var sides = SyntaxHighlighter.TokenizeDiff(path, shown.Old.Length > 0 ? texts.Old : null, shown.Old,
            shown.New.Length > 0 ? texts.New : null, shown.New, SyntaxHighlighter.DefaultMaxLines, token, clock);
        return (new FileHighlight(sides.Old, sides.New, shown), sides.LinesWalked);
    }

    private void HighlightLanded(HighlightAsk ask, FileHighlight? result, int lines, HighlightTexts? texts)
    {
        _highlightRunning = null;
        _highlightCancel?.Dispose();
        _highlightCancel = null;
        HighlightLinesTokenized += lines;

        // A cancelled tokenize keeps its texts, and so where it got to, for the next ask of the
        // file; one that landed has no more use for them. Before the next ask starts: it may be
        // this file's.
        if (ask.Scheme == _highlightScheme)
        {
            if (ask.Mode != ExternalDiffMode.Commit && texts is not null && !ReferenceEquals(result, FileHighlight.None))
                SeeHighlightFile(ask, texts);
            if (result is not null) ForgetHighlightTexts(ask.Key);
            else if (texts is not null) KeepHighlightTexts(ask.Key, texts);
        }

        // A tokenize that finished before the ask that replaced it is as good as any: the reader
        // may well come back to the file. A cancelled one that stopped short has no result.
        if (result is not null && ask.Scheme == _highlightScheme)
        {
            if (_highlights.Count >= 128)
            {
                _highlights.Clear();   // crude cap; entries are per-file-version
                _highlightSeen.Clear();
            }
            _highlights[ask.Key] = result;
            // Re-render the diff rows with their colours — when the diff on screen is the
            // one waiting for them. The History prefetch lands while the log is on screen,
            // and rebuilding the whole window for it doubled the cost of every move a
            // reader made through the log (two builds a key instead of one). The view's
            // alone: colours are nothing the chrome shows (it built 0.45 times a key in the
            // Commit view's file walk). The rows on screen are kept and recoloured; so is a
            // review's weave, which reads no colour (ReviewRowsFor).
            if (ask.Key == _highlightShown)
            {
                _reviewColoursLanded = true;
                MarkViewDirty();
            }
        }

        if (_highlightNext is { } next)
        {
            _highlightNext = null;
            StartHighlight(next);
        }
        else if (_collectWhenIdle) CollectWhenIdle();
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
