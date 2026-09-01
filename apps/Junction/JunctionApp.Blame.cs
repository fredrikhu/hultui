using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Junction.Git;
using Junction.Theme;

namespace Junction;

public partial class JunctionApp
{
    // ── Blame in the Files view ──────────────────────────────────────────
    //
    // The same file, with a gutter saying who last touched each line and when. It replaces
    // the content pane rather than sitting beside it: a blame gutter beside a body that can
    // also scroll independently is two scroll positions to keep in step, and there is only
    // ever one thing being read.
    //
    // The source keeps its syntax colouring. `git blame <commitish>` answers for the same
    // revision the pane already fetched and tokenized, so line i of the blame is line i of
    // the content — the per-line spans are reused as they are rather than re-tokenizing.

    /// <summary>Whether the content pane shows blame instead of the source. Session state,
    /// like the diff's split/wrap choice; it follows the reader from file to file.</summary>
    private bool _filesBlame;

    /// <summary>Bounded for the same reason the file bodies are: a blame is one record per
    /// line of the file, and browsing a tree would otherwise pin one per file visited.</summary>
    private readonly KeyedAsyncLoader<string, IReadOnlyList<GitBlameLine>> _blameLoaders =
        new([], capacity: 12, comparer: StringComparer.Ordinal);

    /// <summary>One blame row. Follows the code size the same way the diff's row does, so
    /// the text does not outgrow the line it sits on.</summary>
    private float BlameRowHeight => MathF.Round(DiffCodeSize) + 8f;
    private const float BlameGutterWidth = 250f;

    private void ToggleFilesBlame()
    {
        _filesBlame = !_filesBlame;
        // Rerender, not MarkDirty: switching on activates the per-file blame loader, and an
        // untracked rebuild never subscribes to it — the pane would sit on "Loading…" until
        // some other tracked rebuild happened to come along.
        Rerender();
    }

    /// <summary>Opens a tree row straight into blame — the context-menu entry, which has to
    /// both change the selection and switch mode. One Rerender covers both: each activates a
    /// per-file loader, and an untracked rebuild would subscribe to neither.</summary>
    private void BlameTreeFile(string path)
    {
        _filesSelPath = path;
        _filesCursorPath = path;
        _filesBlame = true;
        Rerender();
    }

    /// <summary>The toggle in the file-content header. Lit while blame is showing, in the same
    /// bordered-chip shape as the diff pane's "ignore whitespace".</summary>
    private Element BlameToggle()
    {
        bool on = _filesBlame;
        var b = Flex.Row(gap: 5).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(4, 8, 4, 8))
            .CornerRadius(Nocturne.RadiusSm)
            .BorderColor(on ? Nocturne.Accent : Nocturne.Divider).BorderWidth(1f)
            .Cursor(CursorType.Pointer)
            .Tooltip(on ? "Back to the source" : "Blame — who last changed each line");
        if (on) b.Background(Nocturne.Alpha(Nocturne.Accent, 0.14));
        b.AddChild(Ui.Icon(Icons.Blame, 12f, on ? Nocturne.Accent300 : Nocturne.On(0.55)));
        b.AddChild(Text.New("Blame").FontSize(11.5f).FontWeight(FontWeight.Medium)
            .TextColor(on ? Nocturne.Accent300 : Nocturne.On(0.58)));
        // Keyed with its state: the lit and unlit chips are two variants at one position, and
        // a patched-over element does not inherit the new handler.
        b.Key = "files-blame" + (on ? "#on" : "");
        b.OnClick(e => { e.StopPropagation = true; ToggleFilesBlame(); });
        if (!on) b.HoverBg(Nocturne.Alpha(Nocturne.Text, 0.07), Color.Transparent);
        return b;
    }

    /// <summary>The blame body for the open file, or a stand-in while git is still answering.
    /// <paramref name="content"/> is the pane's already-parsed source, whose per-line syntax
    /// spans this reuses.</summary>
    private Element BuildBlameBody(string reference, string path, FileContent content)
    {
        var key = $"{_activeRepoPath}|{reference}|{path}";
        var repoPath = _activeRepoPath;
        // git blame is O(history of the file) — comfortably the slowest thing this view asks
        // for, and the reason it goes through a loader rather than being read inline.
        var loader = _blameLoaders.For(key);
        loader.Load(key, () => (IReadOnlyList<GitBlameLine>)RepoFor(repoPath).Blame(path, reference));
        var lines = loader.Value;

        if (lines.Count == 0)
            return Box.New().FlexGrow(1).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Background(Nocturne.Bg)
                .Children(Text.New(loader.IsLoading ? "Blaming…" : "No blame for this file")
                    .FontSize(13f).TextColor(Nocturne.On(0.45)));

        // A run of consecutive lines from one commit shows its heading once, the way every
        // other blame view does — repeating the same hash, name and date down forty lines
        // buries the two places the answer actually changes.
        //
        // Keyed by line number rather than by the record, the way the Branches list keys by
        // full name: the template is handed the item alone, and GitBlameLine is a record, so
        // two identical lines of an identical file compare equal and would share an entry.
        var runStart = new Dictionary<int, bool>(lines.Count);
        for (int i = 0; i < lines.Count; i++)
            runStart[lines[i].LineNumber] = i == 0
                || !string.Equals(lines[i].CommitHash, lines[i - 1].CommitHash, StringComparison.Ordinal);

        return VirtualListElement<GitBlameLine>.New()
            .ItemHeight(BlameRowHeight)
            .Items(lines)
            .RenderWith(l => BlameRow(l, content, runStart.GetValueOrDefault(l.LineNumber)))
            // Off-screen rows are never realized, so assistive tech only ever sees this string.
            .AccessibleItemLabel(l => $"line {l.LineNumber}, {l.ShortHash}, {l.AuthorName}, {l.Content}")
            .OnActivateItem(l => OpenBlameCommit(l.CommitHash))
            .FlexGrow(1)
            .Background(Nocturne.Bg)
            .Padding(new Thickness(6, 0, 6, 0));
    }

    private Element BlameRow(GitBlameLine line, FileContent content, bool runStart)
    {
        int i = line.LineNumber - 1;   // git counts from 1, the parsed arrays from 0
        // The blame and the content are the same revision, so line i of one is line i of the
        // other and the syntax spans transfer as they are. The guard is for the frame where a
        // new file's content has landed and its blame has not (or the reverse), when the pane
        // is briefly holding two different files' answers: fall back to git's own copy of the
        // line, uncoloured, rather than colouring it against the wrong file's tokens.
        bool aligned = content.Lines is { } src && i >= 0 && i < src.Length;
        var text = aligned ? content.Lines![i] : line.Content;
        var spans = aligned && content.Spans is { } perLine && i < perLine.Length ? perLine[i] : null;

        var row = Flex.Row(gap: 0).AlignItems(AlignItems.Stretch)
            .Height(BlameRowHeight).FlexShrink(0)
            .Cursor(CursorType.Pointer)
            .Tooltip($"{line.ShortHash} · {line.AuthorName} · {line.AuthorDate.LocalDateTime:yyyy-MM-dd HH:mm}\n{line.Summary}");
        // Every row swaps between a headed and a headless variant as runs shift under an edit,
        // and the list recycles its rows — both are reasons the key has to name the variant.
        row.Key = $"blame:{line.LineNumber}{(runStart ? "#h" : "")}";

        var gutter = Flex.Row(gap: 8).Width(BlameGutterWidth).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 10, 0, 8))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0));
        if (runStart)
        {
            // A run's first line carries the answer; the rest of the run is deliberately blank
            // so the eye can see where authorship changes without reading anything.
            gutter.AddChild(Ui.Mono(line.ShortHash, 11f, Nocturne.Accent300).FlexShrink(0).MaxLines(1));
            gutter.AddChild(Text.New(line.AuthorName).FontSize(11f).TextColor(Nocturne.On(0.62))
                .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
            gutter.AddChild(Ui.Mono(line.AuthorDate == DateTimeOffset.MinValue
                    ? "" : line.AuthorDate.LocalDateTime.ToString("yyyy-MM-dd"),
                10.5f, Nocturne.On(0.38)).FlexShrink(0).MaxLines(1));
        }
        else
        {
            gutter.AddChild(Box.New().FlexGrow(1));
        }
        row.AddChild(gutter);

        row.AddChild(Box.New().Width(52f).FlexShrink(0)
            .JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(0, 10, 0, 0))
            .AlignItems(AlignItems.FlexEnd)
            .Children(Ui.Mono(line.LineNumber.ToString(), 10.5f, Nocturne.On(0.28)).MaxLines(1)));

        // Clipped, not ellipsised: a cut at the edge says there is more to the right, where an
        // ellipsis says this is all there is. Blame has no sideways scroll, so a long line is
        // read in the source view — which is one toggle away.
        row.AddChild(Box.New().FlexGrow(1).Overflow(Overflow.Hidden)
            .JustifyContent(JustifyContent.Center)
            .Children(Ui.Code(text.Length == 0 ? " " : text, DiffCodeSize, CodeFont, Nocturne.On(0.85))
                .Spans(spans).MaxLines(1)));

        row.OnClick(_ => OpenBlameCommit(line.CommitHash));
        row.ContextMenu(() => BlameMenuItems(line));
        row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }

    // The two destinations carry their own view's tab icon, so the menu says where each one
    // goes before you read the label: GitDiff is the Commit tab, GitCommit the History tab.
    private IReadOnlyList<ContextMenuItem> BlameMenuItems(GitBlameLine line) =>
    [
        Item(Icons.GitDiff, $"Open commit {line.ShortHash}", () => OpenBlameCommit(line.CommitHash)),
        Item(Icons.GitCommit, "Reveal in history", () => RevealCommitInHistory(line.CommitHash)),
        Item(Icons.Copy, "Copy commit hash", () => CopyToClipboard(line.CommitHash)),
        ContextMenuItem.Separator(),
        Item(Icons.Copy, "Copy line", () => CopyToClipboard(line.Content)),
    ];

    /// <summary>Follows a blame row to the commit it names, in the Commit view — the same
    /// destination double-clicking a row in History reaches.</summary>
    private void OpenBlameCommit(string hash)
    {
        if (!IsBlamableCommit(hash)) return;
        SetCommitSelection(hash);
        PrefetchCommitDetail(hash);   // the detail is usually the slowest part of the switch
        SetView(JunctionView.Commit);
    }

    /// <summary>
    /// Whether a blame row names a commit that can be opened, complaining if it doesn't.
    ///
    /// <para>git writes an all-zero hash for a line that is not committed yet. There is
    /// nothing to open, and selecting it would leave the Commit view resolving a hash git
    /// will not answer for.</para>
    /// </summary>
    private bool IsBlamableCommit(string hash)
    {
        if (!string.IsNullOrEmpty(hash) && !hash.All(c => c == '0')) return true;
        Flash("That line isn't committed yet");
        return false;
    }
}
