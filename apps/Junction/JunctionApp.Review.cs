using System.Text;
using System.Text.RegularExpressions;
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.RichText;
using HultUI.Core.Styling;
using HultUI.RichText.Markdown;
using Junction.Git;
using Junction.GitLab;
using Junction.Theme;

namespace Junction;

/// <summary>
/// Reviewing a merge request in the diff — design §K. The request opens as a Commit-view
/// range compare between its <c>diff_refs</c> base and head (both fetched first, so every
/// position sent back is one GitLab already knows), and the diff grows a 22px review gutter,
/// a caret, a range band, thread cards between the rows, a composer, and — once <i>Start
/// review</i> is pressed — a pending strip over GitLab's draft notes.
/// </summary>
/// <remarks>
/// Thread and composer cards are rows of the same virtualised list as the code, with heights
/// measured up front the way the wrapped diff measures its lines; they carry a
/// <see cref="Element.Key"/>, which is what keeps a field inside one focused across the
/// rebuilds every keystroke causes. Line codes are GitLab's <c>sha1(path)_old_new</c>, with
/// the old and new counters walked from each hunk header exactly as GitLab's parser walks
/// them — an added line's old number is the old counter where it stands, not blank.
/// </remarks>
public partial class JunctionApp
{
    // ── State ────────────────────────────────────────────────────────────────────────

    private sealed class ReviewSession(string path, long iid)
    {
        public string Path { get; } = path;
        public long Iid { get; } = iid;
        public GlMergeRequest? Mr;
        public string BaseSha = "", HeadSha = "", StartSha = "";
        public bool Ready;
        public string Status = "Loading merge request…";
        public string? Error;
        /// <summary>Draft notes instead of published ones — "Start review".</summary>
        public bool ReviewMode;
        /// <summary>The file the caret, range and composer belong to.</summary>
        public string? File;
        public string? OpenFile;
        public int Caret = -1;
        public (int Anchor, int From, int To)? Range;
        public bool RangeStopped;
        public bool Dragging;
        public readonly HashSet<string> Expanded = new(StringComparer.Ordinal);
        public bool ExpandAll;
        public string? FocusThread;
        public string? RevealThread;
        public ReviewComposer? Composer;
        /// <summary>The replies being written, by discussion id: a thread in here shows its
        /// reply editor.</summary>
        public readonly Dictionary<string, ReviewDraft> Replies = new(StringComparer.Ordinal);
        public string? Busy;
        /// <summary>Bumped whenever a card's height may have changed, so the row heights are
        /// measured again.</summary>
        public int HeightVersion;
        /// <summary>What the cards on screen are made of — thread ids, note counts and
        /// lengths, open state — so a reply that lands from the server, or a thread that
        /// grows a tag, measures the rows again without anyone having to bump the version.</summary>
        public int RowsSignature;
        public int HeightKey => HashCode.Combine(HeightVersion, RowsSignature);
        /// <summary>The line rows of the file on screen, in order, with their positions.</summary>
        public List<DiffRow> Lines = [];
        public int[] RowOfLine = [];
        /// <summary>The threads whose cards follow each line of the file on screen.</summary>
        public Dictionary<int, List<ThreadView>> AnchoredByLine = new();
        public Dictionary<string, (int Threads, int Unresolved)> ByFile = new(StringComparer.Ordinal);

        public int Pending;   // draft notes on the server, mine
        /// <summary>The threads seen on the first load. A thread a later refresh brings in
        /// carries a <i>new</i> tag until it is opened, so a live reload says what it
        /// changed rather than moving the cards under the reader.</summary>
        public readonly HashSet<string> KnownThreads = new(StringComparer.Ordinal);
        public bool ThreadsSeeded;
        /// <summary>When the drafts were last submitted or discarded; a drafts value loaded
        /// before that is stale and is read as none.</summary>
        public DateTimeOffset DraftsSettledAt = DateTimeOffset.MinValue;
    }

    private sealed class ReviewComposer(string file, int from, int to, bool stopped)
    {
        public string File { get; } = file;
        public int From { get; } = from;
        public int To { get; } = to;
        public bool Stopped { get; } = stopped;
        public ReviewDraft Draft = new();
        public string? Failure;
    }

    /// <summary>A comment being written, the composer's or a reply's: the document, the
    /// editor showing it, and that editor's formatting bar.</summary>
    private sealed class ReviewDraft
    {
        public RichDocument Doc = RichDocument.Empty;
        public readonly Ref<RichTextEditorElement> Editor = new();
        public RichTextToolbar? Bar;
        /// <summary>What GitLab is sent.</summary>
        public string Markdown => RichMarkdown.Write(Doc).Trim();
    }

    internal sealed record ReviewLine(bool NewSide, int? OldLine, int? NewLine, string LineCode, string? Type)
    {
        public int Number => NewSide ? NewLine ?? 0 : OldLine ?? 0;
        public string Where => $"line {Number} ({(NewSide ? "new" : "old")})";
    }

    private sealed record ThreadNote(string Author, DateTimeOffset? When, string Body, bool Pending);

    private sealed record ThreadView(
        string Id,
        string? DiscussionId,
        IReadOnlyList<ThreadNote> Notes,
        bool Resolved,
        string? ResolvedBy,
        bool Pending,
        bool Outdated,
        string Where,
        long? FirstNoteId,
        long? DraftId,
        bool New = false);

    private ReviewSession? _review;
    // The editor asked for focus — the composer's or a reply's — and how many frames are left
    // to wait for its row.
    private Ref<RichTextEditorElement>? _reviewFocusTarget;
    private int _pendingReviewFocus;
    private int _pendingReviewReveal = -1;
    private readonly Dictionary<(string Path, long Iid), int> _noteGen = new();

    private readonly KeyedAsyncLoader<(string Path, long Iid), Hosted<IReadOnlyList<GlDraftNote>>> _draftLoaders =
        new(Hosted<IReadOnlyList<GlDraftNote>>.Empty([]), capacity: 8);

    internal bool ReviewOpen => _review is not null;
    internal bool ReviewReady => _review is { Ready: true };
    /// <summary>Where the review stands, for a test or the driver to print when it does not open.</summary>
    internal string ReviewState => _review is { } rv ? $"ready={rv.Ready} status=\"{rv.Status}\" error=\"{rv.Error}\"" : "no review";
    internal int ReviewCaret => _review?.Caret ?? -1;
    internal (int From, int To)? ReviewRange => _review?.Range is { } r ? (r.From, r.To) : null;

    private bool ReviewRowsActive => _review is { Ready: true } && View == JunctionView.Commit;

    // ── Entry and exit ───────────────────────────────────────────────────────────────

    /// <summary>Opens the request for review — from the detail page's button, its <c>r</c>
    /// key, a changed-file row or a discussion row.</summary>
    internal void StartReview(string path, long iid, string? openFile = null, string? focusThread = null)
    {
        // The Commit view compares in the active repository, so a request from another one
        // (the workspace-scoped list, the Overview strip) makes that one active first.
        if (!PathEquals(path, _activeRepoPath)) SelectRepo(path);
        if (_review is { } current && current.Path == path && current.Iid == iid)
        {
            if (openFile != null) { current.OpenFile = openFile; _selectedFilePath = openFile; }
            if (focusThread != null) { current.FocusThread = focusThread; current.Expanded.Add(focusThread); current.RevealThread = focusThread; }
            CloseMergeRequests();
            SetView(JunctionView.Commit);
            Rerender();
            return;
        }
        var rv = new ReviewSession(path, iid)
        {
            OpenFile = openFile,
            FocusThread = focusThread,
            RevealThread = focusThread,
            Mr = MergeRequestsFor(path).Value.FirstOrDefault(m => m.Iid == iid),
        };
        if (focusThread != null) rv.Expanded.Add(focusThread);
        _review = rv;
        CloseMergeRequests();
        SetView(JunctionView.Commit);
        Rerender();
        LoadReview(rv);
    }

    /// <summary>Fetches what the review needs: the request's <c>diff_refs</c> (only the single
    /// request carries them), then its head under <c>refs/remotes/&lt;remote&gt;/merge-requests/N</c>,
    /// then the base — an ancestor of the head, so it comes down with it.</summary>
    private async void LoadReview(ReviewSession rv)
    {
        if (GitLabRemoteFor(rv.Path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client)
        {
            rv.Error = "This repository is not on GitLab, or its host has no account.";
            MarkDirty();
            return;
        }
        var repo = RepoFor(rv.Path);
        var remoteName = HostingRemoteName(rv.Path);

        var detail = await Task.Run(() =>
        {
            var id = ResolveProjectId(client, remote);
            return id is null
                ? GitLabResult<GlMergeRequest>.Fail(GitLabFailureKind.NotFound, $"Project not found on {remote.Host}")
                : Sync(client.GetMergeRequestAsync(id.Value, rv.Iid));
        });
        if (!ReferenceEquals(_review, rv)) return;
        if (!detail.Ok)
        {
            rv.Error = detail.Failure!.UserMessage(remote.Host, "Merge request");
            MarkDirty();
            return;
        }
        rv.Mr = detail.Value;
        if (detail.Value.DiffRefs is not { BaseSha: { Length: > 0 } b, HeadSha: { Length: > 0 } h } refs)
        {
            rv.Error = "GitLab has not computed this request's diff yet — try again in a moment.";
            MarkDirty();
            return;
        }
        rv.BaseSha = b;
        rv.HeadSha = h;
        rv.StartSha = refs.StartSha ?? b;
        rv.Status = $"Fetching refs/merge-requests/{rv.Iid}/head from {remoteName}…";
        MarkDirty();

        var fetch = await repo.FetchRefspecAsync(remoteName,
            $"+refs/merge-requests/{rv.Iid}/head:refs/remotes/{remoteName}/merge-requests/{rv.Iid}", NoAskPass);
        if (!ReferenceEquals(_review, rv)) return;
        if (!fetch.Success)
        {
            rv.Error = FirstLine(fetch.Stderr, "Could not fetch the merge request");
            MarkDirty();
            return;
        }
        // The base is the merge base of the two branches, so it came down with the head; a
        // request whose head was rebased since GitLab computed its refs is the exception.
        bool haveBase = await Task.Run(() => repo.GetCommit(b) is not null);
        if (!haveBase && rv.Mr?.TargetBranch is { Length: > 0 } target)
        {
            rv.Status = $"Fetching {target} from {remoteName}…";
            MarkDirty();
            await repo.FetchRefspecAsync(remoteName, target, NoAskPass);
            haveBase = await Task.Run(() => repo.GetCommit(b) is not null);
        }
        if (!ReferenceEquals(_review, rv)) return;
        if (!haveBase)
        {
            rv.Error = $"Commit {Short7(b)} is not in this clone — GitLab's diff was computed against a head this clone does not have.";
            MarkDirty();
            return;
        }
        rv.Ready = true;
        if (rv.OpenFile != null) _selectedFilePath = rv.OpenFile;
        BumpDataGen(rv.Path);   // the new remote ref belongs in Branches and History too
        Rerender();
    }

    /// <summary>Leaves the review. Pending draft notes stay on the server — they are the
    /// reader's, not this window's — so leaving only asks when there are some, and offers
    /// to discard them.</summary>
    private void EndReview(bool backToRequest)
    {
        if (_review is not { } rv) return;
        if (rv.ReviewMode && rv.Pending > 0)
        {
            OpenDialog(new DialogSpec(
                Title: "Leave the review?",
                Body: $"{rv.Pending} comment{(rv.Pending == 1 ? " is" : "s are")} pending. Left as they are, they stay as drafts on GitLab until you submit or discard them.",
                Placeholder: null,
                ConfirmLabel: "Leave",
                OnConfirm: (_, discard) => { if (discard) DiscardDrafts(rv); FinishEndReview(rv, backToRequest); },
                CheckboxLabel: "Discard the pending comments",
                CheckboxInitial: false));
            return;
        }
        FinishEndReview(rv, backToRequest);
    }

    private void FinishEndReview(ReviewSession rv, bool backToRequest)
    {
        if (!ReferenceEquals(_review, rv)) return;
        _review = null;
        _pendingReviewFocus = 0;
        _reviewFocusTarget = null;
        if (backToRequest) OpenMergeRequest(rv.Path, rv.Iid);
        else Rerender();
    }

    // ── Data ─────────────────────────────────────────────────────────────────────────

    private int NoteGen(string path, long iid) => _noteGen.GetValueOrDefault((path, iid));

    /// <summary>Reloads the threads and drafts of one request — after a post, a reply, a
    /// resolve — without touching the rest of the hosted data.</summary>
    private void RefreshNotes(string path, long iid)
    {
        _noteGen[(path, iid)] = NoteGen(path, iid) + 1;
        Rerender();
    }

    private Hosted<IReadOnlyList<GlDraftNote>> DraftNotesFor(string path, long iid)
    {
        if (GitLabRemoteFor(path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client)
            return Hosted<IReadOnlyList<GlDraftNote>>.Empty([]);
        var loader = _draftLoaders.For((path, iid));
        var prev = loader.Value;
        loader.Load($"{path}#{iid}#{HostedGen(path)}#{NoteGen(path, iid)}", () =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return prev;
            var d = Sync(client.ListDraftNotesAsync(id.Value, iid));
            return d.Ok ? new Hosted<IReadOnlyList<GlDraftNote>>(d.Value, DateTimeOffset.UtcNow, null, true) : Keep(prev, d.Failure!);
        });
        return loader.Value;
    }

    /// <summary>The threads of the request as the diff shows them: a discussion with a
    /// position that is not a system note, plus the reader's own pending drafts — a draft
    /// with a discussion id rides inside that thread as a pending note, one without is a
    /// pending thread of its own.</summary>
    private List<(ThreadView View, GlPosition? Position)> ReviewThreads(ReviewSession rv)
    {
        var discussions = DiscussionsFor(rv.Path, rv.Iid).Value;
        // Drafts loaded before the last submit or discard describe a review that is over;
        // until the reload lands they count for nothing.
        var draftsHosted = DraftNotesFor(rv.Path, rv.Iid);
        var drafts = draftsHosted.AsOf > rv.DraftsSettledAt ? draftsHosted.Value : [];
        rv.Pending = drafts.Count;
        // Drafts on the server mean a review is under way, whoever's window started it —
        // the strip and the tag come back with them.
        if (drafts.Count > 0) rv.ReviewMode = true;
        var host = GitLabRemoteFor(rv.Path)?.Host;
        var me = _settings.GitLabAccounts.FirstOrDefault(a => string.Equals(a.Host, host, StringComparison.OrdinalIgnoreCase))?.Name ?? "You";
        var list = new List<(ThreadView, GlPosition?)>();
        foreach (var d in discussions)
        {
            if (d.IndividualNote || d.First is not { System: false } first || first.Position is null) continue;
            var notes = d.Notes.Where(n => !n.System)
                .Select(n => new ThreadNote(n.Author?.Name ?? "", n.CreatedAt, n.Body, false)).ToList();
            foreach (var draft in drafts)
                if (draft.DiscussionId == d.Id)
                    notes.Add(new ThreadNote(me, null, draft.Note, true));
            // Outdated is decided where the rows are woven: a thread from an older head whose
            // line is still in the diff is shown on it, as GitLab shows it; only a line that is
            // gone makes the thread outdated.
            list.Add((new ThreadView(d.Id, d.Id, notes, d.IsResolved, first.ResolvedBy?.Name, false, false,
                WhereOf(first.Position), first.Id, null), first.Position));
        }
        foreach (var draft in drafts)
        {
            if (draft.DiscussionId is { Length: > 0 } || draft.Position is null) continue;
            list.Add((new ThreadView("draft:" + draft.Id, null, [new ThreadNote(me, null, draft.Note, true)], false, null,
                true, false, WhereOf(draft.Position), null, draft.Id), draft.Position));
        }

        // The first loaded set is what the reader started from; anything a refresh adds
        // after that is new until opened. Drafts are the reader's own and never new.
        var discussionsHosted = DiscussionsFor(rv.Path, rv.Iid);
        if (discussionsHosted.Loaded)
        {
            if (!rv.ThreadsSeeded)
            {
                foreach (var (view, _) in list) rv.KnownThreads.Add(view.Id);
                rv.ThreadsSeeded = true;
            }
            else
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var (view, pos) = list[i];
                    if (!view.Pending && !rv.KnownThreads.Contains(view.Id))
                        list[i] = (view with { New = true }, pos);
                }
            }
        }
        return list;
    }

    /// <summary>Opening a thread is having seen it: the <i>new</i> tag comes off.</summary>
    private static void MarkThreadSeen(ReviewSession rv, string id) => rv.KnownThreads.Add(id);

    private static string WhereOf(GlPosition pos)
    {
        if (pos.LineRange is { Start: { } s, End: { } e })
        {
            bool newSide = e.Type != "old";
            int from = newSide ? s.NewLine ?? s.OldLine ?? 0 : s.OldLine ?? 0;
            int to = newSide ? e.NewLine ?? e.OldLine ?? 0 : e.OldLine ?? 0;
            return from != to ? $"lines {from}–{to} ({(newSide ? "new" : "old")})" : $"line {to} ({(newSide ? "new" : "old")})";
        }
        return pos.NewLine is { } n ? $"line {n} (new)" : $"line {pos.OldLine} (old)";
    }

    /// <summary>The end line a thread anchors to: its side and number.</summary>
    private static (bool NewSide, int Number)? AnchorOf(GlPosition pos)
    {
        if (pos.LineRange is { End: { } e })
        {
            if (e.Type == "old" && e.OldLine is { } o) return (false, o);
            if (e.NewLine is { } n) return (true, n);
            if (e.OldLine is { } o2) return (false, o2);
        }
        if (pos.NewLine is { } nl) return (true, nl);
        if (pos.OldLine is { } ol) return (false, ol);
        return null;
    }

    private static bool PositionIsFile(GlPosition pos, GitFileDiff diff) =>
        string.Equals(pos.NewPath, diff.Path, StringComparison.Ordinal)
        || string.Equals(pos.OldPath, diff.OldPath ?? diff.Path, StringComparison.Ordinal);

    // ── Rows ─────────────────────────────────────────────────────────────────────────

    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeaderRegex();

    /// <summary>GitLab's line code: the SHA-1 of the path, then the old and new counters.</summary>
    internal static string LineCode(string path, int oldPos, int newPos) =>
        $"{Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(path)))}_{oldPos}_{newPos}";

    /// <summary>
    /// The flattened unified rows with the review's rows woven in: every line row learns its
    /// position, each thread's card follows the line it anchors to, the composer follows the
    /// end of its range, and threads whose lines are gone at this head are listed at the end.
    /// </summary>
    private List<DiffRow> WithReviewRows(GitFileDiff diff, List<DiffRow> lines, ReviewSession rv)
    {
        // Positions, walked as GitLab's parser walks them.
        int oldPos = 0, newPos = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            var r = lines[i];
            if (r.Type == DiffRowType.Hunk)
            {
                var m = HunkHeaderRegex().Match(r.Hunk);
                if (m.Success) { oldPos = int.Parse(m.Groups[1].Value); newPos = int.Parse(m.Groups[2].Value); }
                lines[i] = r with { LineIndex = i };
                continue;
            }
            var l = r.Line!;
            var code = LineCode(diff.Path, oldPos, newPos);
            ReviewLine review = l.Kind switch
            {
                DiffLineKind.Addition => new ReviewLine(true, null, l.NewLineNumber, code, "new"),
                DiffLineKind.Deletion => new ReviewLine(false, l.OldLineNumber, null, code, "old"),
                _ => new ReviewLine(true, l.OldLineNumber, l.NewLineNumber, code, null),
            };
            switch (l.Kind)
            {
                case DiffLineKind.Addition: newPos++; break;
                case DiffLineKind.Deletion: oldPos++; break;
                default: oldPos++; newPos++; break;
            }
            lines[i] = r with { LineIndex = i, Review = review };
        }

        // File switch: the caret and range belong to the file they were placed in.
        if (rv.File != diff.Path)
        {
            rv.File = diff.Path;
            rv.Caret = -1;
            rv.Range = null;
            rv.RangeStopped = false;
            if (rv.Composer is { } c && c.File != diff.Path) rv.Composer = null;
        }
        rv.Lines = lines;

        var after = new Dictionary<int, List<DiffRow>>();
        var outdated = new List<DiffRow>();
        foreach (var (view, pos) in ReviewThreads(rv).OrderBy(t => t.View.Notes.FirstOrDefault()?.When ?? DateTimeOffset.MaxValue))
        {
            if (pos is null || !PositionIsFile(pos, diff)) continue;
            int at = AnchorOf(pos) is { } a ? FindLine(lines, a.NewSide, a.Number) : -1;
            var row = new DiffRow { Type = DiffRowType.Thread, Thread = at >= 0 ? view : view with { Outdated = true }, LineIndex = at };
            if (at >= 0) (after.TryGetValue(at, out var bucket) ? bucket : after[at] = []).Add(row);
            else outdated.Add(row);
        }
        if (rv.Composer is { } composer && composer.File == diff.Path && composer.To >= 0 && composer.To < lines.Count)
            (after.TryGetValue(composer.To, out var b) ? b : after[composer.To] = [])
                .Add(new DiffRow { Type = DiffRowType.Composer, LineIndex = composer.To });

        var rows = new List<DiffRow>(lines.Count + after.Count + outdated.Count + 1);
        var rowOfLine = new int[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            rowOfLine[i] = rows.Count;
            rows.Add(lines[i]);
            if (after.TryGetValue(i, out var cards)) rows.AddRange(cards);
        }
        if (outdated.Count > 0)
        {
            rows.Add(new DiffRow { Type = DiffRowType.Section, LineIndex = -1 });
            rows.AddRange(outdated);
        }
        rv.RowOfLine = rowOfLine;
        rv.AnchoredByLine = after.ToDictionary(kv => kv.Key,
            kv => kv.Value.Where(r => r.Thread is not null).Select(r => r.Thread!).ToList());

        var signature = new HashCode();
        foreach (var r in rows)
        {
            if (r.Thread is { } t)
            {
                signature.Add(t.Id);
                signature.Add(t.Notes.Count);
                signature.Add(t.Resolved);
                signature.Add(t.Outdated);
                signature.Add(t.New);
                signature.Add(ThreadExpanded(rv, t));
                signature.Add(Replying(rv, t));
                foreach (var n in t.Notes) signature.Add(n.Body.Length);
            }
            else if (r.Type == DiffRowType.Composer)
                signature.Add(rv.Composer?.Failure is null ? 1 : 2);
        }
        // A picture that arrived or a section that opened changes a card's height with none
        // of the above changing.
        signature.Add(_mdLayoutGen);
        rv.RowsSignature = signature.ToHashCode();

        // A thread asked for (n / p, a discussion row) is brought on screen once it has a row.
        if (rv.RevealThread is { } reveal)
        {
            int idx = rows.FindIndex(r => r.Thread?.Id == reveal);
            if (idx >= 0) { _pendingReviewReveal = idx; rv.RevealThread = null; }
        }
        return rows;
    }

    private static int FindLine(List<DiffRow> lines, bool newSide, int number)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Type != DiffRowType.Unified || lines[i].Line is not { } line) continue;
            if (newSide ? line.NewLineNumber == number && line.Kind != DiffLineKind.Deletion
                        : line.OldLineNumber == number && line.Kind != DiffLineKind.Addition)
                return i;
        }
        return -1;
    }

    /// <summary>Thread counts per file, for the tree's pills. Read from the discussions only —
    /// the diff of each file is not needed to count them.</summary>
    private void SyncReviewByFile(ReviewSession rv)
    {
        rv.ByFile.Clear();
        foreach (var (view, pos) in ReviewThreads(rv))
        {
            if (pos is null) continue;
            var file = pos.NewPath ?? pos.OldPath ?? "";
            var cur = rv.ByFile.GetValueOrDefault(file);
            rv.ByFile[file] = (cur.Threads + 1, cur.Unresolved + (view.Resolved || view.Pending ? 0 : 1));
        }
    }

    // ── Heights ──────────────────────────────────────────────────────────────────────

    // Card geometry, shared by the measurement and the builders so the two cannot drift.
    private const float CardMarginTop = 4f, CardMarginBottom = 8f, CardMarginLeft = 130f, CardMarginRight = 14f;
    private const float CardPadY = 10f, CardPadX = 12f, CardBorder = 1f;
    private const float CardLineHeight = 22f, CardGap = 8f, NoteGap = 10f, NoteHeaderGap = 4f;
    private const float CardFooterHeight = 30f, ComposerAreaHeight = 72f, ComposerButtonsHeight = 30f, FailureLineHeight = 18f;
    // A comment being written: the formatting bar, kept to one line, over an editor of fixed
    // height that scrolls what it cannot show, so the card is the height measured for it.
    private const float DraftBarHeight = 26f, DraftBarGap = 4f;
    private const float DraftHeight = DraftBarHeight + DraftBarGap + ComposerAreaHeight;
    private const float SectionRowHeight = 34f;
    private const float NoteFontSize = 12.5f;

    private float _diffBodyWidth;

    /// <summary>The width a note's text wraps into: the body, less the card's margins,
    /// border and padding.</summary>
    private float CardTextWidth => Math.Max(40f, _diffBodyWidth - CardMarginLeft - CardMarginRight - 2 * CardBorder - 2 * CardPadX);

    private static MdLook NoteLook => new(NoteFontSize, Nocturne.On(0.84));

    private bool ThreadExpanded(ReviewSession rv, ThreadView t) =>
        rv.ExpandAll || rv.Expanded.Contains(t.Id) || t.Id == rv.FocusThread;

    /// <summary>Whether a reply to <paramref name="t"/> is being written: its card shows the
    /// reply editor.</summary>
    private static bool Replying(ReviewSession rv, ThreadView t) =>
        t.DiscussionId is { } did && rv.Replies.ContainsKey(did);

    private float ThreadRowHeight(ReviewSession rv, ThreadView t, float textWidth)
    {
        float inner;
        if (!ThreadExpanded(rv, t))
            inner = CardLineHeight;
        else
        {
            float notes = 0;
            for (int i = 0; i < t.Notes.Count; i++)
            {
                if (i > 0) notes += NoteGap;
                notes += CardLineHeight + NoteHeaderGap + NoteBodyHeight(t.Notes[i].Body, textWidth);
            }
            inner = CardLineHeight + CardGap + notes + CardGap
                + (Replying(rv, t) ? DraftHeight + CardGap : 0) + CardFooterHeight;
        }
        return CardMarginTop + 2 * CardBorder + 2 * CardPadY + inner + CardMarginBottom;
    }

    private float ComposerRowHeight(ReviewComposer c)
    {
        float inner = CardLineHeight + CardGap + DraftHeight + CardGap + ComposerButtonsHeight
            + (c.Failure is null ? 0 : CardGap + FailureLineHeight);
        return CardMarginTop + 2 * CardBorder + 2 * CardPadY + inner + CardMarginBottom;
    }

    /// <summary>A note body is drawn as markdown, so its height is the markdown's.</summary>
    private float NoteBodyHeight(string body, float width) =>
        MarkdownHeight(MarkdownReader.Read(body), NoteLook, width);

    private float ReviewRowHeight(DiffRow r, float textWidth) => r.Type switch
    {
        DiffRowType.Thread => ThreadRowHeight(_review!, r.Thread!, textWidth),
        DiffRowType.Composer => ComposerRowHeight(_review!.Composer!),
        DiffRowType.Section => SectionRowHeight,
        _ => DiffRowHeight,
    };

    // ── Row builders ─────────────────────────────────────────────────────────────────

    /// <summary>The gutter cell of a line row: the <c>+</c> everyone gets, lit on the caret
    /// row and inside a range; a thread mark where threads anchor; the caret's edge.</summary>
    private Element ReviewGutterCell(ReviewSession rv, DiffRow row, bool inRange, bool caret, IReadOnlyList<ThreadView> anchored)
    {
        var cell = Box.New().Width(22).FlexShrink(0)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center);
        if (inRange) cell.Background(Nocturne.Accent800);
        if (caret || inRange) { cell.BorderColor(Nocturne.Accent500); cell.BorderWidth(new Thickness(0, 0, 0, 3)); }

        var mark = Box.New().Width(16).Height(16).CornerRadius(4f)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer);
        int line = row.LineIndex;
        if (anchored.Count > 0)
        {
            bool unresolved = anchored.Any(t => !t.Resolved);
            mark.AddChild(Ui.Icon(unresolved ? Icons.ChatCircleFill : Icons.ChatCircle, 13f,
                unresolved ? Nocturne.Accent300 : Nocturne.On(0.45)));
            mark.Tooltip(anchored.Count == 1 ? "Show or hide the thread" : $"Show or hide {anchored.Count} threads");
            mark.On<ClickEvent>(e =>
            {
                e.StopPropagation = true;
                bool anyOpen = anchored.Any(t => ThreadExpanded(rv, t));
                foreach (var t in anchored)
                {
                    if (anyOpen) { rv.Expanded.Remove(t.Id); if (rv.FocusThread == t.Id) rv.FocusThread = null; }
                    else rv.Expanded.Add(t.Id);
                }
                if (anyOpen) rv.ExpandAll = false;
                rv.HeightVersion++;
                MarkDirty();
            });
            mark.AutomationId = "review-mark";
        }
        else
        {
            bool lit = caret || inRange;
            if (lit) mark.Background(Nocturne.Accent800).Outline(1f, Nocturne.Accent);
            mark.AddChild(Ui.Icon(Icons.Plus, 10f, lit ? Nocturne.Accent100 : Nocturne.On(0.28)));
            mark.Tooltip("Comment on this line — drag or ⇧-click for a range");
            mark.AutomationId = "review-plus";
            mark.On<PointerDownEvent>(e =>
            {
                if (e.Button != PointerEvent.LeftButton) return;
                e.StopPropagation = true;
                if (e.Modifiers.HasFlag(KeyModifiers.Shift) && rv.Caret >= 0) ExtendRangeTo(rv, line);
                else { rv.Caret = line; rv.Range = (line, line, line); rv.RangeStopped = false; }
                rv.Dragging = true;
                MarkDirty();
            });
        }
        cell.AddChild(mark);
        return cell;
    }

    /// <summary>What every line row does while reviewing: extends a drag, ends one in the
    /// composer, and places the caret on a plain click.</summary>
    private void WireReviewRow(ReviewSession rv, BoxElement row, int line)
    {
        row.On<PointerMoveEvent>(_ =>
        {
            if (!rv.Dragging) return;
            if (rv.Range is { } r && r.To == line) return;
            ExtendRangeTo(rv, line);
            MarkDirty();
        });
        row.On<PointerUpEvent>(e =>
        {
            if (!rv.Dragging) return;
            e.StopPropagation = true;
            rv.Dragging = false;
            OpenComposer(rv);
        });
        row.On<ClickEvent>(e =>
        {
            if (rv.Dragging) return;
            if (e.Modifiers.HasFlag(KeyModifiers.Shift) && rv.Caret >= 0) { ExtendRangeTo(rv, line); OpenComposer(rv); return; }
            if (rv.Caret == line) return;
            rv.Caret = line;
            MarkDirty();
        });
    }

    /// <summary>The range grows from its anchor to <paramref name="line"/>, stopping where
    /// the side changes: GitLab cannot take a range that mixes old and new lines.</summary>
    private void ExtendRangeTo(ReviewSession rv, int line)
    {
        int anchor = rv.Range?.Anchor ?? rv.Caret;
        if (anchor < 0) anchor = line;
        if (LineAt(rv, anchor) is not { } anchorLine) return;
        int step = line >= anchor ? 1 : -1;
        int last = anchor;
        bool stopped = false;
        for (int i = anchor; i != line + step; i += step)
        {
            if (LineAt(rv, i) is not { } l) continue;   // hunk headers are skipped, not stopped at
            if (l.NewSide != anchorLine.NewSide) { stopped = true; break; }
            last = i;
        }
        rv.Range = (anchor, Math.Min(anchor, last), Math.Max(anchor, last));
        rv.RangeStopped = stopped;
        rv.Caret = last;
    }

    private IReadOnlyList<ThreadView> AnchoredThreads(int lineIndex) =>
        _review is { } rv && rv.AnchoredByLine.TryGetValue(lineIndex, out var threads) ? threads : [];

    private static ReviewLine? LineAt(ReviewSession rv, int i) =>
        i >= 0 && i < rv.Lines.Count ? rv.Lines[i].Review : null;

    private Element ThreadCardRow(ReviewSession rv, DiffRow r)
    {
        var t = r.Thread!;
        bool open = ThreadExpanded(rv, t);
        var outer = Box.New().Padding(new Thickness(CardMarginTop, CardMarginRight, CardMarginBottom, CardMarginLeft));
        outer.Key = "thread:" + t.Id;
        outer.AutomationId = "review-thread";

        var card = Flex.Column(gap: CardGap).Padding(new Thickness(CardPadY, CardPadX, CardPadY, CardPadX))
            .Background(Nocturne.Surface).BorderColor(t.Outdated ? Nocturne.Neutral600 : Nocturne.Neutral700)
            .BorderWidth(CardBorder).CornerRadius(Nocturne.RadiusMd);
        if (t.Id == rv.FocusThread) card.Outline(1f, Nocturne.Accent);
        if (t.Resolved) card.Opacity(0.72f);
        card.AccessibleName = (open ? "Thread on " : "Collapsed thread on ") + t.Where;

        var first = t.Notes.Count > 0 ? t.Notes[0] : new ThreadNote("", null, "", false);
        if (!open)
        {
            var line = Flex.Row(gap: 8).AlignItems(AlignItems.Center).Height(CardLineHeight).Cursor(CursorType.Pointer);
            line.AddChild(Ui.Icon(t.Resolved ? Icons.ChatCircle : Icons.ChatCircleFill, 13f,
                t.Resolved ? Nocturne.On(0.45) : Nocturne.Accent300).FlexShrink(0));
            line.AddChild(Text.New(first.Author).FontSize(12f).FontWeight(FontWeight.Medium).FlexShrink(0));
            line.AddChild(Text.New(FirstTextLine(first.Body)).FontSize(12f).TextColor(Nocturne.On(0.7))
                .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0)).MaxLines(1).OverflowMode(TextOverflow.Ellipsis));
            if (t.Resolved) line.AddChild(Text.New($"resolved by {t.ResolvedBy}").FontSize(11f).TextColor(Nocturne.On(0.45)).FlexShrink(0));
            if (t.New) line.AddChild(Ui.Tag("new", Nocturne.Accent, Nocturne.Bg, 9.5f));
            if (t.Pending) line.AddChild(Ui.Tag("pending", Nocturne.Accent800, Nocturne.Accent100, 9.5f));
            if (t.Outdated) line.AddChild(Ui.Tag("outdated", Nocturne.Neutral800, Nocturne.Neutral100, 9.5f));
            line.AddChild(Ui.Mono($"{t.Notes.Count} note{(t.Notes.Count == 1 ? "" : "s")} · {(t.Outdated ? "was " : "")}{t.Where}", 10.5f, Nocturne.On(0.45)).FlexShrink(0));
            line.AddChild(Text.New("Show").FontSize(11.5f).TextColor(Nocturne.Accent300).FlexShrink(0));
            line.On<ClickEvent>(e => { e.StopPropagation = true; ToggleThread(rv, t.Id, true); });
            card.AddChild(line);
        }
        else
        {
            var head = Flex.Row(gap: 8).AlignItems(AlignItems.Center).Height(CardLineHeight);
            head.AddChild(Text.New($"Thread on {(t.Outdated ? "what was " : "")}{t.Where}").FontSize(12.5f).FontWeight(FontWeight.Medium)
                .FlexShrink(1).MaxLines(1).OverflowMode(TextOverflow.Ellipsis));
            if (t.New) head.AddChild(Ui.Tag("new", Nocturne.Accent, Nocturne.Bg, 9.5f));
            if (t.Pending) head.AddChild(Ui.Tag("pending", Nocturne.Accent800, Nocturne.Accent100, 9.5f));
            if (t.Resolved) head.AddChild(Ui.Tag($"resolved by {t.ResolvedBy}", Nocturne.Neutral800, Nocturne.Neutral100, 9.5f));
            if (t.Outdated) head.AddChild(Ui.Tag("outdated", Nocturne.Neutral800, Nocturne.Neutral100, 9.5f));
            head.AddChild(Box.New().FlexGrow(1));
            var hide = Text.New("Hide").FontSize(11.5f).TextColor(Nocturne.Accent300).Cursor(CursorType.Pointer);
            hide.On<ClickEvent>(e => { e.StopPropagation = true; ToggleThread(rv, t.Id, false); });
            head.AddChild(hide);
            card.AddChild(head);

            var notes = Flex.Column(gap: NoteGap);
            foreach (var n in t.Notes)
            {
                var note = Flex.Column(gap: NoteHeaderGap);
                var nh = Flex.Row(gap: 6).AlignItems(AlignItems.Center).Height(CardLineHeight);
                nh.AddChild(Box.New().Width(22).Height(22).CornerRadius(11f).Background(Nocturne.Accent800)
                    .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center).FlexShrink(0)
                    .Children(Text.New(Initials(n.Author)).FontSize(9f).TextColor(Nocturne.Accent100)));
                nh.AddChild(Text.New(n.Author).FontSize(12f).FontWeight(FontWeight.Medium).FlexShrink(0));
                nh.AddChild(Text.New(n.Pending ? "not published yet" : RelativeTime(n.When)).FontSize(11f)
                    .TextColor(n.Pending ? Nocturne.Accent300 : Nocturne.On(0.45)));
                note.AddChild(nh);
                note.AddChild(MarkdownView(MarkdownReader.Read(n.Body), NoteLook));
                notes.AddChild(note);
            }
            card.AddChild(notes);

            var footer = Flex.Row(gap: 8).AlignItems(AlignItems.Center).Height(CardFooterHeight);
            if (t.DiscussionId is { } did)
            {
                // A reply is written in an editor between the notes and the footer, opened from
                // a field-shaped button: an editor in every open card would make each one a
                // composer tall.
                if (rv.Replies.TryGetValue(did, out var draft))
                {
                    var block = DraftBlock(rv, draft, "Write a reply…", "review-reply-text", "Reply to thread");
                    // Esc and Ctrl+Enter ahead of the editor's own handling of them.
                    block.On<KeyDownEvent>(EventPhase.Capture, e =>
                    {
                        if (e.Key == KeyCode.Escape) { e.StopPropagation = true; CloseReply(rv, did); }
                        else if (Shortcuts.Pressed("review.post", e)) { e.StopPropagation = true; PostReply(rv, t); }
                    });
                    card.AddChild(block);

                    var send = Ui.PrimaryButton(rv.ReviewMode ? "Add to review" : "Reply", null, () => PostReply(rv, t), 11.5f)
                        .Tooltip(Shortcuts.Keys("review.post"));
                    send.AccessibleName = "Reply";
                    footer.AddChild(send.FlexShrink(0));
                }
                else
                {
                    var reply = Button.New(ButtonVariant.Secondary).JustifyContent(JustifyContent.FlexStart)
                        .Height(Dimension.Px(CardFooterHeight)).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
                        .Children(Text.New("Reply…").FontSize(12.5f).TextColor(Nocturne.On(0.45)));
                    reply.OnClick(_ => OpenReply(rv, did));
                    reply.AutomationId = "review-reply";
                    reply.AccessibleName = "Reply to thread";
                    footer.AddChild(reply);
                }
                var resolve = Ui.SecondaryButton(t.Resolved ? "Unresolve" : "Resolve", null, () => ResolveThread(rv, t, !t.Resolved), 11.5f);
                resolve.AccessibleName = t.Resolved ? "Unresolve" : "Resolve";
                footer.AddChild(resolve.FlexShrink(0));
                if (rv.Mr?.WebUrl is { } url && t.FirstNoteId is { } noteId)
                    footer.AddChild(Ui.IconButton(Icons.ArrowSquareOut, () => OpenUrl($"{url}#note_{noteId}"), "Open in the browser", 30f).FlexShrink(0));
                if (rv.Replies.ContainsKey(did))
                {
                    footer.AddChild(Box.New().FlexGrow(1));
                    var cancel = Ui.GhostButton("Cancel", null, () => CloseReply(rv, did), 11.5f).Tooltip("Esc");
                    cancel.AccessibleName = "Cancel reply";
                    footer.AddChild(cancel.FlexShrink(0));
                }
            }
            else
            {
                footer.AddChild(Text.New("Pending — published with the review").FontSize(11.5f).TextColor(Nocturne.On(0.5)).FlexGrow(1));
                if (t.DraftId is { } draftId)
                    footer.AddChild(Ui.GhostButton("Delete", Icons.Trash, () => DeleteDraft(rv, draftId), 11.5f));
            }
            card.AddChild(footer);
        }
        card.On<ClickEvent>(_ =>
        {
            bool wasNew = t.New;
            MarkThreadSeen(rv, t.Id);
            if (rv.FocusThread != t.Id || wasNew) { rv.FocusThread = t.Id; MarkDirty(); }
        });
        outer.AddChild(card);
        return outer;
    }

    private void ToggleThread(ReviewSession rv, string id, bool open)
    {
        MarkThreadSeen(rv, id);
        if (open) rv.Expanded.Add(id);
        else { rv.Expanded.Remove(id); rv.ExpandAll = false; if (rv.FocusThread == id) rv.FocusThread = null; }
        rv.HeightVersion++;
        MarkDirty();
    }

    private Element ComposerRow(ReviewSession rv, DiffRow r)
    {
        var c = rv.Composer!;
        var outer = Box.New().Padding(new Thickness(CardMarginTop, CardMarginRight, CardMarginBottom, CardMarginLeft));
        outer.Key = "composer";
        outer.AutomationId = "review-composer";

        var card = Flex.Column(gap: CardGap).Padding(new Thickness(CardPadY, CardPadX, CardPadY, CardPadX))
            .Background(Nocturne.Surface).BorderColor(Nocturne.Accent).BorderWidth(CardBorder).CornerRadius(Nocturne.RadiusMd);

        var from = LineAt(rv, c.From);
        var to = LineAt(rv, c.To);
        string where = from != null && to != null && c.From != c.To
            ? $"lines {from.Number}–{to.Number} ({(to.NewSide ? "new" : "old")})"
            : to?.Where ?? "";
        var head = Flex.Row(gap: 6).AlignItems(AlignItems.Center).Height(CardLineHeight);
        head.AddChild(Text.New($"New comment on {where}").FontSize(12.5f).FontWeight(FontWeight.Medium));
        if (c.Stopped) head.AddChild(Text.New("· range stops at the side boundary").FontSize(11.5f).TextColor(Nocturne.On(0.5)));
        card.AddChild(head);

        card.AddChild(DraftBlock(rv, c.Draft, "Write a comment…", "review-composer-text", "Comment"));

        var buttons = Flex.Row(gap: 8).AlignItems(AlignItems.Center).Height(ComposerButtonsHeight);
        if (rv.Busy is { } busy)
            buttons.AddChild(BusyButton(busy));
        else
        {
            var add = Ui.PrimaryButton(rv.ReviewMode ? "Add to review" : "Add comment", null, () => SubmitComposer(rv), 11.5f)
                .Tooltip(Shortcuts.Keys("review.post"));
            add.AccessibleName = rv.ReviewMode ? "Add to review" : "Add comment";
            buttons.AddChild(add);
            if (!rv.ReviewMode)
            {
                var start = Ui.GhostButton("Start review", Icons.NotePencil, () => StartReviewMode(rv), 11.5f)
                    .Tooltip("Keep this and the following comments to yourself until you submit them together");
                start.AccessibleName = "Start review";
                buttons.AddChild(start);
            }
        }
        buttons.AddChild(Box.New().FlexGrow(1));
        var cancel = Ui.GhostButton("Cancel", null, () => CloseComposer(rv), 11.5f).Tooltip("Esc");
        cancel.AccessibleName = "Cancel comment";
        buttons.AddChild(cancel);
        card.AddChild(buttons);

        if (c.Failure is { } failure)
            card.AddChild(Flex.Row(gap: 6).AlignItems(AlignItems.Center).Height(FailureLineHeight).Children(
                Ui.Icon(Icons.XCircle, 12f, Nocturne.DangerText),
                Text.New(failure).FontSize(11.5f).TextColor(Nocturne.DangerText).MaxLines(1).OverflowMode(TextOverflow.Ellipsis)));

        // Esc and Ctrl+Enter ahead of the area's own handling of them.
        card.On<KeyDownEvent>(EventPhase.Capture, e =>
        {
            if (e.Key == KeyCode.Escape) { e.StopPropagation = true; CloseComposer(rv); }
            else if (Shortcuts.Pressed("review.post", e)) { e.StopPropagation = true; SubmitComposer(rv); }
        });
        outer.AddChild(card);
        return outer;
    }

    /// <summary>The formatting bar over the editor of <paramref name="draft"/>, the two
    /// <see cref="DraftHeight"/> tall together. What of the bar does not fit the card's width
    /// is cut off rather than wrapped onto a line nobody measured.</summary>
    private Element DraftBlock(ReviewSession rv, ReviewDraft draft, string placeholder, string automationId, string name)
    {
        var bar = (draft.Bar ??= MarkdownBar(draft.Editor)).Build()
            .FlexWrap(FlexWrap.NoWrap).Height(DraftBarHeight).Overflow(Overflow.Hidden);
        var editor = MarkdownEditor(draft.Doc, draft.Bar, rv.Path, d => { draft.Doc = d; MarkDirty(); })
            .PlaceholderText(placeholder)
            .Height(Dimension.Px(ComposerAreaHeight))
            .Style(Ui.Field(verticalPadding: 6f));
        editor.AutomationId = automationId;
        editor.AccessibleName = name;
        // The bar reads the editor on screen through the ref: built before the ref moves to
        // this freshly built one.
        editor.WithRef(draft.Editor);
        return Flex.Column(gap: DraftBarGap).Children(bar, editor);
    }

    private Element OutdatedSectionRow() =>
        Flex.Row(gap: 8).AlignItems(AlignItems.Center).Height(SectionRowHeight)
            .Padding(new Thickness(0, 14, 0, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0))
            .Children(
                Text.New("Outdated threads").FontSize(11.5f).FontWeight(FontWeight.Medium).TextColor(Nocturne.On(0.7)),
                Text.New("· lines that no longer exist at this head").FontSize(11.5f).TextColor(Nocturne.On(0.45)));

    // ── Composer and posting ─────────────────────────────────────────────────────────

    private void OpenComposer(ReviewSession rv)
    {
        if (rv.File is null || rv.Caret < 0) return;
        var (from, to) = rv.Range is { } r ? (r.From, r.To) : (rv.Caret, rv.Caret);
        if (rv.Composer is not { } existing || existing.From != from || existing.To != to)
        {
            // A new range keeps what was being written.
            rv.Composer = new ReviewComposer(rv.File, from, to, rv.RangeStopped) { Draft = rv.Composer?.Draft ?? new() };
            rv.HeightVersion++;
        }
        FocusDraft(rv.Composer.Draft);
    }

    /// <summary>Focuses <paramref name="draft"/>'s editor once its row exists: a row is
    /// realized by the list during layout, so <see cref="ServiceReviewAfterBuild"/> waits for
    /// the frame.</summary>
    private void FocusDraft(ReviewDraft draft)
    {
        _reviewFocusTarget = draft.Editor;
        _pendingReviewFocus = 3;
        MarkDirty();
    }

    private void OpenReply(ReviewSession rv, string discussionId)
    {
        if (!rv.Replies.TryGetValue(discussionId, out var draft))
            rv.Replies[discussionId] = draft = new ReviewDraft();
        FocusDraft(draft);
    }

    /// <summary>Drops the reply being written to the thread, and its editor with it.</summary>
    private void CloseReply(ReviewSession rv, string discussionId)
    {
        rv.Replies.Remove(discussionId);
        _diffBodyRef.Current?.Focus();
        MarkDirty();
    }

    private void CloseComposer(ReviewSession rv)
    {
        rv.Composer = null;
        rv.HeightVersion++;
        _diffBodyRef.Current?.Focus();
        MarkDirty();
    }

    /// <summary>What GitLab is told about where a comment sits: the request's three SHAs,
    /// both paths, and the line — or the range, each end with its line code and side.
    /// Context lines carry both numbers and no type, as GitLab itself reports them.</summary>
    internal static GlPosition PositionFor(string baseSha, string headSha, string startSha, string newPath, string oldPath,
        ReviewLine from, ReviewLine to)
    {
        var pos = new GlPosition
        {
            PositionType = "text", BaseSha = baseSha, HeadSha = headSha, StartSha = startSha,
            NewPath = newPath, OldPath = oldPath,
        };
        if (to.NewSide) { pos.NewLine = to.NewLine; pos.OldLine = to.Type is null ? to.OldLine : null; }
        else pos.OldLine = to.OldLine;
        if (!ReferenceEquals(from, to))
            pos.LineRange = new GlLineRange
            {
                Start = new GlLinePosition { LineCode = from.LineCode, Type = from.Type, OldLine = from.OldLine, NewLine = from.NewLine },
                End = new GlLinePosition { LineCode = to.LineCode, Type = to.Type, OldLine = to.OldLine, NewLine = to.NewLine },
            };
        return pos;
    }

    private GlPosition? ComposerPosition(ReviewSession rv, ReviewComposer c)
    {
        if (LineAt(rv, c.From) is not { } from || LineAt(rv, c.To) is not { } to) return null;
        var detail = _commitDetail.Value;
        string oldPath = detail.DiffByPath.TryGetValue(c.File, out var d) ? d.OldPath ?? c.File : c.File;
        return PositionFor(rv.BaseSha, rv.HeadSha, rv.StartSha, c.File, oldPath, c.From == c.To ? to : from, to);
    }

    private async void SubmitComposer(ReviewSession rv)
    {
        if (rv.Composer is not { } c || rv.Busy != null) return;
        var body = c.Draft.Markdown;
        if (body.Length == 0) { Flash("Write a comment first"); return; }
        if (ComposerPosition(rv, c) is not { } position) { Flash("This line has no position GitLab knows"); return; }
        if (GitLabRemoteFor(rv.Path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client) return;

        rv.Busy = rv.ReviewMode ? "Adding…" : "Posting…";
        c.Failure = null;
        rv.HeightVersion++;
        MarkDirty();
        bool asDraft = rv.ReviewMode;
        var failure = await Task.Run(() =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return new GitLabFailure(GitLabFailureKind.NotFound, $"Project not found on {remote.Host}");
            if (asDraft)
            {
                var d = Sync(client.CreateDraftNoteAsync(id.Value, rv.Iid, body, position));
                return d.Ok ? null : d.Failure;
            }
            var r = Sync(client.CreateDiscussionAsync(id.Value, rv.Iid, body, position));
            if (r.Ok) return null;
            return r.Failure;
        });
        if (!ReferenceEquals(_review, rv)) return;
        rv.Busy = null;
        if (failure != null)
        {
            c.Failure = "Could not post: " + failure.UserMessage(remote.Host, "Merge request");
            rv.HeightVersion++;
            MarkDirty();
            return;
        }
        rv.Composer = null;
        rv.Range = null;
        rv.RangeStopped = false;
        rv.HeightVersion++;
        Flash(asDraft ? "Added to your review" : "Comment posted");
        RefreshNotes(rv.Path, rv.Iid);
    }

    /// <summary>"Start review": from here on comments are drafts, and the one being written
    /// goes first.</summary>
    private void StartReviewMode(ReviewSession rv)
    {
        rv.ReviewMode = true;
        rv.HeightVersion++;
        if (rv.Composer is { } c && c.Draft.Markdown.Length > 0) SubmitComposer(rv);
        else MarkDirty();
    }

    private async void PostReply(ReviewSession rv, ThreadView t)
    {
        if (t.DiscussionId is not { } did || rv.Busy != null || !rv.Replies.TryGetValue(did, out var draft)) return;
        var body = draft.Markdown;
        if (body.Length == 0) { Flash("Write a reply first"); return; }
        if (GitLabRemoteFor(rv.Path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client) return;
        rv.Busy = "Replying…";
        MarkDirty();
        bool asDraft = rv.ReviewMode;
        var failure = await Task.Run(() =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return new GitLabFailure(GitLabFailureKind.NotFound, $"Project not found on {remote.Host}");
            if (asDraft) { var d = Sync(client.CreateDraftNoteAsync(id.Value, rv.Iid, body, null, did)); return d.Ok ? null : d.Failure; }
            var r = Sync(client.ReplyToDiscussionAsync(id.Value, rv.Iid, did, body));
            return r.Ok ? null : r.Failure;
        });
        if (!ReferenceEquals(_review, rv)) return;
        rv.Busy = null;
        if (failure != null) { Flash("Could not reply: " + failure.UserMessage(remote.Host, "Merge request")); MarkDirty(); return; }
        // Posted: the editor goes, and the keys go back to the diff rather than to nothing.
        bool hadFocus = draft.Editor.Current is { IsFocused: true };
        rv.Replies.Remove(did);
        rv.HeightVersion++;
        if (hadFocus) _diffBodyRef.Current?.Focus();
        Flash(asDraft ? "Reply added to your review" : "Reply posted");
        RefreshNotes(rv.Path, rv.Iid);
    }

    private async void ResolveThread(ReviewSession rv, ThreadView t, bool resolved)
    {
        if (t.DiscussionId is not { } did || rv.Busy != null) return;
        if (GitLabRemoteFor(rv.Path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client) return;
        rv.Busy = resolved ? "Resolving…" : "Reopening…";
        MarkDirty();
        var failure = await Task.Run(() =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return new GitLabFailure(GitLabFailureKind.NotFound, $"Project not found on {remote.Host}");
            var r = Sync(client.ResolveDiscussionAsync(id.Value, rv.Iid, did, resolved));
            return r.Ok ? null : r.Failure;
        });
        if (!ReferenceEquals(_review, rv)) return;
        rv.Busy = null;
        if (failure != null) { Flash($"Could not {(resolved ? "resolve" : "reopen")}: " + failure.UserMessage(remote.Host, "Merge request")); MarkDirty(); return; }
        rv.HeightVersion++;
        Flash(resolved ? "Thread resolved" : "Thread reopened");
        RefreshNotes(rv.Path, rv.Iid);
    }

    private async void DeleteDraft(ReviewSession rv, long draftId)
    {
        if (rv.Busy != null) return;
        if (GitLabRemoteFor(rv.Path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client) return;
        rv.Busy = "Deleting…";
        MarkDirty();
        var failure = await Task.Run(() =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return new GitLabFailure(GitLabFailureKind.NotFound, $"Project not found on {remote.Host}");
            var r = Sync(client.DeleteDraftNoteAsync(id.Value, rv.Iid, draftId));
            return r.Ok ? null : r.Failure;
        });
        if (!ReferenceEquals(_review, rv)) return;
        rv.Busy = null;
        if (failure != null) { Flash("Could not delete the draft: " + failure.UserMessage(remote.Host, "Merge request")); MarkDirty(); return; }
        rv.HeightVersion++;
        RefreshNotes(rv.Path, rv.Iid);
    }

    // ── Review mode: pending strip, submit, discard ──────────────────────────────────

    private Element BuildPendingStrip(ReviewSession rv)
    {
        var strip = Flex.Row(gap: 10).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(8, 14, 8, 14))
            .Background(Nocturne.Accent900)
            .BorderColor(Nocturne.Accent700).BorderWidth(new Thickness(1, 0, 0, 0));
        strip.AutomationId = "review-pending";
        strip.AddChild(Ui.Icon(Icons.NotePencil, 14f, Nocturne.Accent200));
        strip.AddChild(Text.New(rv.Pending == 0
                ? "Reviewing · comments you add now are yours alone until you submit them"
                : $"{rv.Pending} comment{(rv.Pending == 1 ? "" : "s")} pending · only you can see them until you submit")
            .FontSize(12f).TextColor(Nocturne.Accent100).FlexGrow(1).FlexShrink(1).MaxLines(1).OverflowMode(TextOverflow.Ellipsis));
        if (rv.Busy is { } busy)
            strip.AddChild(BusyButton(busy));
        else
        {
            var submit = Ui.PrimaryButton("Submit review", Icons.CaretDown, () => { }, 12f);
            submit.AccessibleName = "Submit review";
            submit.On<ClickEvent>(e =>
            {
                e.StopPropagation = true;
                var box = e.CurrentTarget.Layout.AbsoluteBorderBox;
                _popoverX = Math.Max(8f, box.X + box.Width - PopoverWidth);
                _popoverY = box.Y - 4f - 3 * 34f;
                _popover =
                [
                    Item(Icons.ChatCircle, "Comment", () => SubmitReview(rv, null)),
                    Item(Icons.SealCheck, "Approve", () => SubmitReview(rv, "approve")),
                    Item(Icons.Prohibit, "Request changes", () => SubmitReview(rv, "requested_changes")),
                ];
                MarkDirty();
            });
            strip.AddChild(submit.FlexShrink(0));
            var discard = Ui.GhostButton("Discard", null, () => ConfirmDiscardReview(rv), 12f);
            discard.AccessibleName = "Discard review";
            strip.AddChild(discard.FlexShrink(0));
        }
        return strip;
    }

    /// <summary>The submit menu's three verbs, for callers that cannot click a popover — tests.</summary>
    internal void SubmitOpenReview(string? verdict)
    {
        if (_review is { } rv) SubmitReview(rv, verdict);
    }

    /// <summary>Publishes the drafts as one review. <paramref name="verdict"/>: null to just
    /// comment, <c>approve</c> to approve afterwards, <c>requested_changes</c> to set the
    /// reviewer state with the publish.</summary>
    private async void SubmitReview(ReviewSession rv, string? verdict)
    {
        if (rv.Busy != null) return;
        if (GitLabRemoteFor(rv.Path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client) return;
        rv.Busy = "Submitting…";
        MarkDirty();
        var sha = rv.HeadSha;
        var failure = await Task.Run(() =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return new GitLabFailure(GitLabFailureKind.NotFound, $"Project not found on {remote.Host}");
            if (rv.Pending > 0)
            {
                var p = Sync(client.BulkPublishDraftNotesAsync(id.Value, rv.Iid, verdict == "requested_changes" ? verdict : null));
                if (!p.Ok) return p.Failure;
            }
            if (verdict == "approve")
            {
                var a = Sync(client.ApproveAsync(id.Value, rv.Iid, sha));
                if (!a.Ok) return a.Failure;
            }
            return null;
        });
        if (!ReferenceEquals(_review, rv)) return;
        rv.Busy = null;
        if (failure != null) { Flash("Could not submit: " + failure.UserMessage(remote.Host, "Merge request")); MarkDirty(); return; }
        rv.ReviewMode = false;
        rv.DraftsSettledAt = DateTimeOffset.UtcNow;
        rv.HeightVersion++;
        Flash(verdict switch { "approve" => "Review submitted and approved", "requested_changes" => "Review submitted — changes requested", _ => "Review submitted" });
        RefreshHosted(rv.Path);
        RefreshNotes(rv.Path, rv.Iid);
    }

    private void ConfirmDiscardReview(ReviewSession rv)
    {
        if (rv.Pending == 0) { rv.ReviewMode = false; rv.HeightVersion++; MarkDirty(); return; }
        OpenDialog(new DialogSpec(
            Title: "Discard the review?",
            Body: $"{rv.Pending} pending comment{(rv.Pending == 1 ? "" : "s")} will be deleted from GitLab. Published threads are not touched.",
            Placeholder: null,
            ConfirmLabel: "Discard",
            OnConfirm: (_, _) => DiscardDrafts(rv)));
    }

    private async void DiscardDrafts(ReviewSession rv)
    {
        if (GitLabRemoteFor(rv.Path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client) return;
        rv.Busy = "Discarding…";
        MarkDirty();
        var failure = await Task.Run(() =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return new GitLabFailure(GitLabFailureKind.NotFound, $"Project not found on {remote.Host}");
            var drafts = Sync(client.ListDraftNotesAsync(id.Value, rv.Iid));
            if (!drafts.Ok) return drafts.Failure;
            foreach (var d in drafts.Value)
            {
                var r = Sync(client.DeleteDraftNoteAsync(id.Value, rv.Iid, d.Id));
                if (!r.Ok) return r.Failure;
            }
            return null;
        });
        rv.Busy = null;
        if (failure != null) { Flash("Could not discard: " + failure.UserMessage(remote.Host, "Merge request")); MarkDirty(); return; }
        rv.ReviewMode = false;
        rv.DraftsSettledAt = DateTimeOffset.UtcNow;
        rv.HeightVersion++;
        if (ReferenceEquals(_review, rv)) RefreshNotes(rv.Path, rv.Iid);
        else Rerender();
    }

    // ── Header, legend, tree pills ───────────────────────────────────────────────────

    /// <summary>The Commit view's header while reviewing: back to the request, its pill and
    /// title, the review tag, and the thread count with its stepping arrows.</summary>
    private Element BuildReviewHeader(ReviewSession rv)
    {
        SyncReviewByFile(rv);
        var mr = rv.Mr;
        var box = Flex.Column(gap: 8).FlexShrink(0)
            .Padding(new Thickness(12, 16, 12, 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        box.AutomationId = "review-header";

        var row1 = Flex.Row(gap: 10).AlignItems(AlignItems.Center);
        var back = Ui.GhostButton("Merge request", Icons.ArrowLeft, () => EndReview(backToRequest: true));
        back.AccessibleName = "Back to the merge request";
        row1.AddChild(back);
        row1.AddChild(Box.New().Padding(new Thickness(2, 8, 2, 8)).CornerRadius(999f).Background(Nocturne.Accent800)
            .Children(Ui.Mono($"!{rv.Iid}", 12f, Nocturne.Accent100)));
        row1.AddChild(Text.New(mr?.Title ?? "Loading…").FontSize(15f).FontWeight(FontWeight.Medium)
            .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        if (rv.ReviewMode)
            row1.AddChild(Ui.Tag($"Reviewing · {rv.Pending} pending", Nocturne.Accent800, Nocturne.Accent100, 10f));
        if (mr?.WebUrl is { } url)
            row1.AddChild(Ui.IconButton(Icons.ArrowSquareOut, () => OpenUrl(url), "Open in the browser", 30f));
        box.AddChild(row1);

        var row2 = Flex.Row(gap: 10).AlignItems(AlignItems.Center);
        if (mr != null)
        {
            AddBranchPair(row2, mr, 11.5f);
            row2.AddChild(Text.New("·").FontSize(11.5f).TextColor(Nocturne.On(0.35)));
            row2.AddChild(Text.New(mr.Author?.Name ?? "").FontSize(11.5f).TextColor(Nocturne.On(0.55)));
            row2.AddChild(Text.New("·").FontSize(11.5f).TextColor(Nocturne.On(0.35)));
        }
        if (!rv.Ready)
        {
            row2.AddChild(rv.Error is { } error
                ? Text.New(error).FontSize(11.5f).TextColor(Nocturne.DangerText).FlexShrink(1).MaxLines(1).OverflowMode(TextOverflow.Ellipsis)
                : Text.New(rv.Status).FontSize(11.5f).TextColor(Nocturne.On(0.55)));
            if (rv.Error != null)
                row2.AddChild(Ui.GhostButton("Retry", Icons.ArrowsClockwise, () => { rv.Error = null; rv.Status = "Loading merge request…"; MarkDirty(); LoadReview(rv); }, 11.5f));
        }
        else
        {
            int threads = rv.ByFile.Values.Sum(v => v.Threads), unresolved = rv.ByFile.Values.Sum(v => v.Unresolved);
            var counts = Text.New($"{threads} thread{(threads == 1 ? "" : "s")} · {unresolved} unresolved").FontSize(11.5f).TextColor(Nocturne.On(0.55));
            counts.AutomationId = "review-threads";
            row2.AddChild(counts);
            row2.AddChild(Ui.IconButton(Icons.CaretUp, () => StepUnresolved(rv, -1), $"Previous unresolved thread ({Shortcuts.Keys("review.previous")})", 26f));
            row2.AddChild(Ui.IconButton(Icons.CaretDown, () => StepUnresolved(rv, 1), $"Next unresolved thread ({Shortcuts.Keys("review.next")})", 26f));
            var expand = Ui.GhostButton(rv.ExpandAll ? "Collapse all" : "Expand all", null, () => ToggleExpandAll(rv), 11.5f).Tooltip(Shortcuts.Keys("review.expand"));
            expand.AccessibleName = rv.ExpandAll ? "Collapse all" : "Expand all";
            row2.AddChild(expand);
        }
        row2.AddChild(Box.New().FlexGrow(1));
        var hosted = DiscussionsFor(rv.Path, rv.Iid);
        if (hosted.Loaded)
            row2.AddChild(Text.New(AsOfText(hosted.AsOf)).FontSize(10.5f).TextColor(hosted.Stale ? Nocturne.MarkAmber : Nocturne.On(0.4)));
        box.AddChild(row2);
        return box;
    }

    /// <summary>What the Commit view shows while the review's refs are on their way, or
    /// when they did not come.</summary>
    private Element BuildReviewPendingView(ReviewSession rv)
    {
        var col = Flex.Column(gap: 0).FlexGrow(1);
        col.AddChild(BuildReviewHeader(rv));
        var body = Flex.Column(gap: 8).FlexGrow(1).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center);
        if (rv.Error is { } error)
        {
            body.AddChild(Ui.Icon(Icons.XCircle, 26f, Nocturne.DangerText));
            body.AddChild(Text.New("Could not open the review").FontSize(14f).FontWeight(FontWeight.Medium).TextColor(Nocturne.On(0.7)));
            body.AddChild(Text.New(error).FontSize(12f).TextColor(Nocturne.On(0.5)));
        }
        else
        {
            body.AddChild(Spinner(22f, Nocturne.On(0.35)));
            body.AddChild(Text.New(rv.Status).FontSize(12f).TextColor(Nocturne.On(0.45)));
        }
        col.AddChild(body);
        return col;
    }

    private Element BuildReviewLegend()
    {
        var legend = Flex.Row(gap: 14).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(6, 14, 6, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        legend.AutomationId = "review-legend";
        foreach (var (key, what) in Shortcuts.Legend("Review").Append(("drag + or ⇧-click", "for a range")))
            legend.AddChild(Flex.Row(gap: 4).AlignItems(AlignItems.Center).Children(
                Text.New(key).FontSize(10.5f).FontWeight(FontWeight.SemiBold).TextColor(Nocturne.On(0.68)),
                Text.New(what).FontSize(10.5f).TextColor(Nocturne.On(0.45))));
        return legend;
    }

    /// <summary>The thread-count pill and unresolved dot a file row carries while reviewing.</summary>
    private Element? ReviewFilePill(string path)
    {
        if (_review is not { Ready: true } rv || !rv.ByFile.TryGetValue(path, out var counts)) return null;
        var row = Flex.Row(gap: 4).AlignItems(AlignItems.Center).FlexShrink(0);
        row.AddChild(Flex.Row(gap: 3).AlignItems(AlignItems.Center).Padding(new Thickness(0, 6, 0, 6)).CornerRadius(999f)
            .Background(Nocturne.Neutral800).Children(
                Ui.Icon(Icons.ChatCircle, 10f, Nocturne.Neutral100),
                Text.New(counts.Threads.ToString()).FontSize(10f).TextColor(Nocturne.Neutral100)));
        if (counts.Unresolved > 0)
            row.AddChild(Box.New().Width(6).Height(6).CornerRadius(3f).Background(Nocturne.Accent).Tooltip($"{counts.Unresolved} unresolved"));
        row.AutomationId = "review-file-pill";
        return row;
    }

    private void ToggleExpandAll(ReviewSession rv)
    {
        rv.ExpandAll = !rv.ExpandAll;
        if (!rv.ExpandAll) rv.Expanded.Clear();
        rv.HeightVersion++;
        MarkDirty();
    }

    /// <summary>Walks to the next (or previous) unresolved thread, across files in the tree's
    /// order — opening the file, expanding the thread and bringing its card on screen.</summary>
    private void StepUnresolved(ReviewSession rv, int direction)
    {
        var files = _commitFileRows.Where(r => !r.IsFolder).Select(r => r.Path).ToList();
        if (files.Count == 0) files = _commitDetail.Value.Files.Select(f => f.Path).ToList();
        var candidates = new List<(int FileIndex, int Line, string Id)>();
        foreach (var (view, pos) in ReviewThreads(rv))
        {
            if (view.Resolved || view.Pending || pos is null) continue;
            int fi = files.IndexOf(pos.NewPath ?? pos.OldPath ?? "");
            if (fi < 0) continue;
            candidates.Add((fi, AnchorOf(pos)?.Number ?? 0, view.Id));
        }
        if (candidates.Count == 0) { Flash("No unresolved threads"); return; }
        candidates.Sort((a, b) => a.FileIndex != b.FileIndex ? a.FileIndex.CompareTo(b.FileIndex)
            : a.Line != b.Line ? a.Line.CompareTo(b.Line) : string.CompareOrdinal(a.Id, b.Id));
        int at = rv.FocusThread is { } focus ? candidates.FindIndex(c => c.Id == focus) : -1;
        int next;
        if (at >= 0) next = (at + direction + candidates.Count) % candidates.Count;
        else
        {
            // From the caret: the first thread past it in this file, else the file's first.
            int fileIndex = files.IndexOf(_selectedFilePath ?? "");
            int caretLine = LineAt(rv, rv.Caret)?.Number ?? -1;
            next = direction > 0
                ? candidates.FindIndex(c => c.FileIndex > fileIndex || (c.FileIndex == fileIndex && c.Line > caretLine))
                : candidates.FindLastIndex(c => c.FileIndex < fileIndex || (c.FileIndex == fileIndex && c.Line < caretLine));
            if (next < 0) next = direction > 0 ? 0 : candidates.Count - 1;
        }
        var target = candidates[next];
        rv.FocusThread = target.Id;
        rv.Expanded.Add(target.Id);
        rv.RevealThread = target.Id;
        rv.HeightVersion++;
        var file = files[target.FileIndex];
        if (file != _selectedFilePath) { _selectedFilePath = file; _commitCursorPath = file; rv.OpenFile = file; Rerender(); }
        else MarkDirty();
    }

    // ── Keys ─────────────────────────────────────────────────────────────────────────

    /// <summary>The review's keys, once the diff is on screen: <c>↑↓</c> move the caret,
    /// <c>⇧↑↓</c> extend the range, <c>c</c> opens the composer, <c>n</c>/<c>p</c> step
    /// through unresolved threads, <c>e</c> expands them all, and <c>Esc</c> closes the
    /// composer, then clears the range, then leaves.</summary>
    private bool HandleReviewKey(KeyDownEvent e)
    {
        if (_review is not { Ready: true } rv || View != JunctionView.Commit) return false;
        bool shift = e.Modifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case KeyCode.Escape:
                if (rv.Composer != null) CloseComposer(rv);
                else if (rv.Range != null) { rv.Range = null; rv.RangeStopped = false; MarkDirty(); }
                else EndReview(backToRequest: true);
                return true;
            case KeyCode.Down or KeyCode.Up when rv.Lines.Count > 0:
                MoveCaret(rv, e.Key == KeyCode.Down ? 1 : -1, shift);
                return true;
        }
        if (rv.Caret >= 0 && Shortcuts.Pressed("review.comment", e)) { OpenComposer(rv); return true; }
        if (Shortcuts.Pressed("review.next", e)) { StepUnresolved(rv, 1); return true; }
        if (Shortcuts.Pressed("review.previous", e)) { StepUnresolved(rv, -1); return true; }
        if (Shortcuts.Pressed("review.expand", e)) { ToggleExpandAll(rv); return true; }
        return false;
    }

    private void MoveCaret(ReviewSession rv, int direction, bool extend)
    {
        int from = rv.Caret < 0 ? (direction > 0 ? -1 : rv.Lines.Count) : rv.Caret;
        int next = from + direction;
        while (next >= 0 && next < rv.Lines.Count && rv.Lines[next].Review is null) next += direction;
        if (next < 0 || next >= rv.Lines.Count) return;
        if (extend)
        {
            if (rv.Range is null) rv.Range = (from < 0 ? next : from, Math.Min(from < 0 ? next : from, next), Math.Max(from < 0 ? next : from, next));
            ExtendRangeTo(rv, next);
        }
        else
        {
            rv.Caret = next;
            rv.Range = null;
            rv.RangeStopped = false;
        }
        if (rv.Caret >= 0 && rv.Caret < rv.RowOfLine.Length) RevealDiffLine(rv.RowOfLine[rv.Caret]);
        MarkDirty();
    }

    /// <summary>After a build: the editor asked for focus — the composer's or a reply's —
    /// gets it once its row exists (a row is realized by the list during layout, so this waits
    /// for the frame), and a thread asked for is scrolled to.</summary>
    private void ServiceReviewAfterBuild()
    {
        if (_pendingReviewReveal >= 0)
        {
            int row = _pendingReviewReveal;
            _pendingReviewReveal = -1;
            RevealDiffLine(row);
        }
        if (_pendingReviewFocus > 0)
        {
            _window.RequestPostFrameCallback(() =>
            {
                if (_pendingReviewFocus <= 0) return;
                if (_reviewFocusTarget?.Current is { IsDetached: false } editor && editor.Parent != null)
                {
                    _pendingReviewFocus = 0;
                    editor.Focus();
                }
                else if (--_pendingReviewFocus > 0)
                {
                    // A reply's card is the one just clicked, so on screen; the composer's row
                    // may be below the fold.
                    if (_review is { Composer: { } c } rv && ReferenceEquals(_reviewFocusTarget, c.Draft.Editor)
                        && c.To >= 0 && c.To < rv.RowOfLine.Length)
                        RevealDiffLine(rv.RowOfLine[c.To] + 1);
                    MarkDirty();
                }
            });
        }
    }
}
