using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Junction.Git;
using Junction.Theme;

namespace Junction;

public partial class JunctionApp
{
    // ── Diff pane (header + body), shared by Commit / Changes / fullscreen ──

    // Stable tokens that let a drag select across the diff's per-line code labels
    // (Ctrl+C copies the span). Unified is one column; split keeps its two columns
    // separate so a drag down one side doesn't rope in the other.
    // The selection is held as row numbers into the flattened diff, not as references to the
    // labels showing those rows — the list recycles a label as soon as it scrolls out of
    // view, which used to end the drag and truncate the copy to whatever was on screen.
    private readonly TextSelectionGroup _diffGroupUnified = new();
    private readonly TextSelectionGroup _diffGroupLeft = new();
    private readonly TextSelectionGroup _diffGroupRight = new();

    // ── Horizontal position ──────────────────────────────────────────────
    // The list cannot provide this itself: VirtualListElement pins its content width to the
    // viewport, so its own ScrollOffsetX can never leave zero. HorizontalPan moves the code
    // by margin inside a clipping cell instead, which virtualization does not obstruct and
    // which leaves the line-number and staging columns where they are.
    private readonly HorizontalPan _diffPan;
    // The comparison the line width was measured for, so it is measured once per file.
    private string _diffLongestFor = "";

    private readonly Ref<Element> _diffBodyRef = new();

    // Find in the diff. Two lanes in split mode, one in unified — see BindDiffSearch.
    private readonly FindBarState _diffFind;

    private Element BuildDiffPane(string path, GitFileDiff? diff, DiffInteract? interact = null)
    {
        var col = Flex.Column(gap: 0).FlexGrow(1);

        var header = Flex.Row(gap: 10).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(8, 12, 8, 12))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Ui.Mono(path.Length > 0 ? path : "—", 12f, Nocturne.Text)
            .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        header.AddChild(DiffModeToggle());
        header.AddChild(IgnoreWhitespaceToggle());
        header.AddChild(Ui.IconButton(Icons.FileCode, OpenSelectedDiffExternally, "Open in VS Code (Ctrl D)").FlexShrink(0));
        header.AddChild(Ui.IconButton(Icons.ArrowsOut, () => { _diffFull = true; MarkDirty(); }, "Fullscreen diff").FlexShrink(0));
        col.AddChild(header);
        if (_diffFind.Open)
        {
            var bar = BuildFindBar(_diffFind, "Find in diff…");
            // Keyed so it reconciles as itself: a keyless element that comes and goes
            // inherits the retained caret and focus of whatever last held its slot.
            bar.Key = "diff-find";
            col.AddChild(bar);
        }

        // Interactive staging: an action bar appears while lines are selected, plus
        // a one-line hint when nothing is selected yet (so the affordance is discoverable).
        if (interact != null && diff != null)
        {
            if (_stageSel.Count > 0)
                col.AddChild(BuildStageActionBar(diff, interact));
            else
                col.AddChild(BuildStageHint(interact));
        }
        else if (diff != null && View == JunctionView.Changes && !_changeSelConflicted
                 && _settings.Git.IgnoreWhitespace)
        {
            // Explain the missing affordance rather than just dropping it.
            col.AddChild(Flex.Row(gap: 6).FlexShrink(0).AlignItems(AlignItems.Center)
                .Padding(new Thickness(5, 12, 5, 12))
                .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0))
                .Children(
                    Ui.Mono("W", 11f, Nocturne.Accent300),
                    Text.New("Ignoring whitespace — line staging is off (this view no longer matches the file exactly)")
                        .FontSize(10.5f).TextColor(Nocturne.On(0.45))));
        }

        // Images have no textual diff (git reports "Binary files … differ"), so they
        // get a before/after view instead of the empty "No textual changes" pane.
        if (IsImagePath(path) && BuildImageDiffBody(path) is { } imagePane)
        {
            col.AddChild(imagePane);
            return col;
        }

        col.AddChild(BuildDiffScroll(diff, interact));
        return col;
    }

    private Element BuildStageHint(DiffInteract it) =>
        Flex.Row(gap: 6).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(5, 12, 5, 12))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0))
            .Children(
                Ui.Icon(Icons.CheckSquare, 11f, Nocturne.On(0.4)),
                Text.New(it.Staged
                        ? "Click a line or hunk header to unstage part of this file"
                        : "Click a line or hunk header to stage or discard part of this file")
                    .FontSize(10.5f).TextColor(Nocturne.On(0.4)));

    private Element DiffModeToggle()
    {
        // FlexShrink(0) so the toggle keeps its size as the diff pane narrows (the
        // header path ellipsizes to absorb the shrink instead of the buttons
        // overlapping); compact windows drop the labels to icon-only.
        var seg = Flex.Row(gap: 2).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(2))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusRow);
        seg.AddChild(DiffModeButton("Unified", Icons.Rows, !_diffSplit, () => { _diffSplit = false; Persist(); MarkDirty(); }));
        seg.AddChild(DiffModeButton("Split", Icons.Columns, _diffSplit, () => { _diffSplit = true; Persist(); MarkDirty(); }));
        seg.AddChild(DiffModeButton("Wrap", Icons.Rows, _diffWrap, ToggleDiffWrap));
        return seg;
    }

    // "Ignore whitespace" (git --ignore-all-space). Lit while active; the tooltip
    // names the staging trade-off, since a -w diff can't produce appliable patches.
    private Element IgnoreWhitespaceToggle()
    {
        bool on = _settings.Git.IgnoreWhitespace;
        var b = Flex.Row(gap: 0).Width(30).Height(30).FlexShrink(0)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .CornerRadius(Nocturne.RadiusSm)
            .BorderColor(on ? Nocturne.Accent : Nocturne.Divider).BorderWidth(1f)
            .Cursor(CursorType.Pointer)
            .Tooltip(on
                ? "Ignoring whitespace — line staging is off while active"
                : "Ignore whitespace changes");
        if (on) b.Background(Nocturne.Alpha(Nocturne.Accent, 0.14));
        b.AddChild(Ui.Mono("W", 12f, on ? Nocturne.Accent300 : Nocturne.On(0.55)));
        b.Key = "ws-toggle" + (on ? "#on" : "");
        b.OnClick(e => { e.StopPropagation = true; ToggleIgnoreWhitespace(); });
        if (!on) b.HoverBg(Nocturne.Alpha(Nocturne.Text, 0.07), Color.Transparent);
        return b;
    }

    private void ToggleIgnoreWhitespace()
    {
        _settings.Git.IgnoreWhitespace = !_settings.Git.IgnoreWhitespace;
        _stageSel.Clear();     // a -w diff's lines aren't stageable; drop any picks
        Persist();
        RefreshAll();          // diff loaders re-key on the flag and reload
    }

    private Element DiffModeButton(string label, string glyph, bool active, Action onClick)
    {
        var fg = active ? Nocturne.Accent : Nocturne.On(0.58);
        var b = Flex.Row(gap: 5).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(4, 9, 4, 9)).CornerRadius(5f).Cursor(CursorType.Pointer)
            .Tooltip(label);
        if (active) b.Background(Nocturne.Bg).BorderColor(Nocturne.Accent).BorderWidth(1f);
        b.AddChild(Ui.Icon(glyph, 12f, fg));
        if (!_compactChrome)
            b.AddChild(Text.New(label).FontSize(11.5f).FontWeight(FontWeight.Medium).TextColor(fg));
        b.OnClick(e => { e.StopPropagation = true; onClick(); });
        return b;
    }

    private const int DiffRowHeight = 22;
    private const float DiffCodeSize = 12f;

    /// <summary>Whether a line too wide for its column wraps rather than scrolling sideways.
    /// The two are alternatives: wrapping means every line is visible in full, so there is
    /// nothing left to scroll to. Session state, like the split/unified choice beside it.</summary>
    private bool _diffWrap;

    private void ToggleDiffWrap()
    {
        _diffWrap = !_diffWrap;
        _diffPan.Reset();   // the other mode's sideways offset means nothing in this one
        Persist();
        MarkDirty();
    }

    // Row heights for the wrapped body, and the shape they were measured under. Wrapped rows
    // have no common height, so the list is given a per-row height instead of one number.
    private float[] _diffHeights = [];
    private float _diffCodeWidth;   // the code column, which is what wrapped text wraps into

    /// <summary>The code label for a diff line, wrapped or clipped to one line and panned.
    /// Wrapping and sideways scrolling are alternatives, so a wrapped line takes neither the
    /// one-line clip nor the panning cell.</summary>
    private Element DiffCodeCell(TextElement label)
    {
        if (_diffWrap) return label.FlexGrow(1);
        return _diffPan.Cell(label.MaxLines(1).OverflowMode(TextOverflow.Clip).FlexGrow(1));
    }
    private (bool Wrap, bool Split, float Width, int Rows) _diffHeightShape = (false, false, -1f, -1);

    /// <summary>
    /// The height of every row at the current width, measured once per change of shape rather
    /// than per frame. Unwrapped every row is <see cref="DiffRowHeight"/> and the list is told
    /// so directly; this only runs when wrapping.
    /// </summary>
    private void RebuildDiffHeights(List<DiffRow> rows, float codeWidth)
    {
        var heights = new float[rows.Count];
        var style = TextMeasure.StyleFor(DiffCodeSize, Fonts.Mono);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Type == DiffRowType.Hunk) { heights[i] = DiffRowHeight; continue; }
            float text = r.Type == DiffRowType.Split
                ? Math.Max(WrappedHeight(r.Left?.Content, style, codeWidth),
                           WrappedHeight(r.Right?.Content, style, codeWidth))
                : WrappedHeight(r.Line?.Content, style, codeWidth);
            // The label carries 2px of padding above and below it.
            heights[i] = Math.Max(DiffRowHeight, text + 4f);
        }
        _diffHeights = heights;
    }

    // Same shortcut Vesper's comparison uses: the code font is monospace, so a short enough
    // line certainly fits on one line and needs no shaping to find that out. Anything else,
    // and anything outside printable ASCII, is measured properly — a height that disagrees
    // with what the row lays out to is a clipped row, not a slow one.
    private float _diffMonoAdvance = -1f;
    private float _diffMonoLineHeight;

    private float WrappedHeight(string? text, ResolvedInheritedStyle style, float width)
    {
        text ??= "";
        EnsureDiffMonoMetrics(style);
        if (_diffMonoAdvance > 0f && width > 0f && IsPlainAscii(text)
            && (text.Length + 1) * _diffMonoAdvance <= width)
            return _diffMonoLineHeight;
        return TextMeasure.MeasureWrapped(text, style, width).Height;
    }

    private static bool IsPlainAscii(string text)
    {
        foreach (var c in text) if (c < ' ' || c > '~') return false;
        return true;
    }

    private void EnsureDiffMonoMetrics(ResolvedInheritedStyle style)
    {
        if (_diffMonoAdvance >= 0f) return;
        float advance = TextMeasure.MeasureWidth("M", style);
        if (advance <= 0f) { _diffMonoAdvance = 0f; return; }
        foreach (var c in " iMW0(_~lI'")
            if (Math.Abs(TextMeasure.MeasureWidth(c.ToString(), style) - advance) > 0.01f)
            { _diffMonoAdvance = 0f; return; }
        if (Math.Abs(TextMeasure.MeasureWidth("MM", style) - advance * 2f) > 0.01f)
        { _diffMonoAdvance = 0f; return; }
        _diffMonoLineHeight = TextMeasure.MeasureWrapped("M", style, advance * 4f).Height;
        if (_diffMonoLineHeight <= 0f) { _diffMonoAdvance = 0f; return; }
        _diffMonoAdvance = advance;
    }

    private enum DiffRowType : byte { Hunk, Unified, Split }

    private sealed record DiffRow
    {
        public DiffRowType Type { get; init; }
        /// <summary>Position in the flattened list — what a cross-line selection is anchored to.</summary>
        public int Index { get; init; }
        public string Hunk { get; init; } = "";
        public GitDiffHunk? HunkRef { get; init; }   // set on Hunk rows, for hunk-level staging
        public GitDiffLine? Line { get; init; }   // unified
        public GitDiffLine? Left { get; init; }   // split
        public GitDiffLine? Right { get; init; }
        // Syntax-highlight spans for the code cell(s), line-relative offsets.
        public IReadOnlyList<HultUI.Core.Rendering.TextSpan>? Spans { get; init; }        // unified
        public IReadOnlyList<HultUI.Core.Rendering.TextSpan>? LeftSpans { get; init; }    // split
        public IReadOnlyList<HultUI.Core.Rendering.TextSpan>? RightSpans { get; init; }
    }

    // Virtualized so large diffs keep constant row height and stay cheap — a
    // plain ScrollView over a tall column compresses its rows to fit.
    private Element BuildDiffScroll(GitFileDiff? diff, DiffInteract? interact = null)
    {
        if (diff == null || diff.Hunks.Count == 0)
            return Box.New().FlexGrow(1)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New(diff == null ? "Select a file" : "No textual changes")
                    .FontSize(13f).TextColor(Nocturne.On(0.45)));

        if (interact != null) SyncStageSelection(interact);

        var hl = HighlightForCurrent(diff.Path);
        var rows = _diffSplit ? FlattenSplit(diff, hl) : FlattenUnified(diff, hl);
        for (int i = 0; i < rows.Count; i++) rows[i] = rows[i] with { Index = i };
        BindDiffSelection(diff, rows);
        BindDiffSearch(diff, rows);

        // Decided before the list is built, and that ordering is load-bearing: WithRef points
        // the Ref at the element being *built*, which has no layout yet, and only PatchFrom
        // repoints it to the live one. Ask afterwards and DiffCodeWidth reads zero, falls
        // back to the window — several times this pane's width — and concludes there is
        // nothing to scroll, on every build forever.
        UpdateDiffPan(diff, rows, interact != null);
        _diffBarBuilt = _diffPan.CanPan;

        // What a row looks like depends on three things the list cannot see: whether lines
        // wrap, which layout is showing, and how wide the code column is. When any of them
        // moves the rows have to be measured again and rebuilt, because a realized row is
        // otherwise reused exactly as it was.
        float codeWidth = _diffCodeWidth;
        var shape = (_diffWrap, _diffSplit, codeWidth, rows.Count);
        if (_diffHeightShape != shape)
        {
            if (_diffWrap) RebuildDiffHeights(rows, codeWidth); else _diffHeights = [];
            // Not on the first build: there is nothing realized yet to throw away, and doing
            // it mid-build disturbs the rows the very first frame lays out.
            if (_diffHeightShape.Rows >= 0 && _diffBodyRef.Current is VirtualListElement<DiffRow> mounted)
                mounted.InvalidateRows();
            _diffHeightShape = shape;
        }

        var list = VirtualListElement<DiffRow>.New()
            .ItemHeight(DiffRowHeight)
            .Items(rows)
            .RenderWith(r => RenderDiffRow(r, interact))
            .FlexGrow(1)
            .Background(Nocturne.Bg);
        // Unwrapped every row is the same height and the list does its own arithmetic; only
        // wrapping needs the measured index.
        if (_diffWrap)
            list.RowHeights((_, i) => i < _diffHeights.Length ? _diffHeights[i] : DiffRowHeight);
        // Key by file so each file gets its own scroll position instead of one shared
        // offset — switching files reconciles to a fresh list that starts at the top.
        list.Key = "diff:" + diff.Path;
        // Focusable so clicking the diff lets arrow / page / home / end scroll it.
        // KeyboardScroll handles those (and leaves chorded keys to the app, so
        // Ctrl+↑/↓ commit stepping still works while the diff has focus).
        list.IsFocusable = true;
        list.TabIndex = 0;
        list.KeyboardScroll();
        list.On<KeyDownEvent>(DiffPaneKey);
        list.WithRef(_diffBodyRef);
        // A trackpad's sideways swipe arrives as DeltaX, a mouse's as Shift+DeltaY. The
        // list's own handler sees them first and clamps them to nothing, since its content
        // is exactly as wide as its viewport.
        list.On<ScrollEvent>(ev => _diffPan.HandleScroll(ev));

        if (!_diffBarBuilt) return list;

        var col = Flex.Column(gap: 0).FlexGrow(1);
        col.AddChild(list);
        col.AddChild(_diffPan.Scrollbar(Nocturne.Surface, Nocturne.Neutral700, Nocturne.Divider));
        return col;
    }

    // Whether the last build included the scrollbar.
    private bool _diffBarBuilt;

    /// <summary>
    /// Ask for one more build when the scrollbar's presence was decided on a guess.
    ///
    /// <para>Whether there is anything to scroll to depends on how wide the diff pane is,
    /// and the first build after opening a file runs before any layout — so
    /// <see cref="DiffCodeWidth"/> falls back to the window, which here is several times
    /// the pane. Once the body has a real width the answer can flip, and nothing else would
    /// mark the tree dirty to act on it. Rebuilds at most once: after it, the two agree.</para>
    /// </summary>
    private void SettleDiffScrollbar()
    {
        if (_diffBodyRef.Current is null) return;
        // Read after the frame has been laid out. OnAfterBuild is too early — the list has
        // no box for this frame yet, and on the first build it is a brand new element with
        // no prior layout at all, so the width DiffCodeWidth needs is simply not knowable
        // here. Same reason ScrollRowIntoView defers.
        _window.RequestPostFrameCallback(() =>
        {
            if (_diffBodyRef.Current is not { Layout.AbsoluteContentBox.Width: > 0 }) return;
            if (_diffBarBuilt == _diffPan.CanPan) return;
            MarkDirty();   // settles: after the rebuild the two agree
        });
    }

    /// <summary>Tell the pan how wide the content and its window are. The width of one code
    /// column is what the fixed number and sign columns leave over, halved in split mode —
    /// taken from the laid-out body when there is one, and from the window before that, since
    /// the first build after opening a file runs before any layout and still has to decide
    /// whether there is a scrollbar. Line widths are measured once per comparison; the rows
    /// are the same objects across a rebuild and measuring is a shaping call.</summary>
    private void UpdateDiffPan(GitFileDiff diff, List<DiffRow> rows, bool interactive)
    {
        float body = _diffBodyRef.Current is { Layout.AbsoluteContentBox.Width: > 0 } b
            ? b.Layout.AbsoluteContentBox.Width
            : _window.LayoutWidth;
        // Split: two 44px number columns. Unified: two 46px number columns, a 16px sign,
        // and — while staging — a 20px checkbox.
        float fixedCols = _diffSplit ? 44f * 2 : 46f * 2 + 16f + (interactive ? 20f : 0f);
        float code = body - fixedCols;
        _diffCodeWidth = Math.Max(0, _diffSplit ? code / 2 : code);
        // Wrapped, every line is visible in full, so there is nothing left to scroll to. An
        // unbounded viewport is what says so: it leaves no offset to pan by, so CanPan goes
        // false and the scrollbar disappears with it. The real column width is kept
        // separately, because that is still what the text wraps into.
        _diffPan.ViewportWidth = _diffWrap ? float.MaxValue : _diffCodeWidth;
        if (_diffWrap) { _diffPan.Reset(); return; }

        var key = diff.Path + "#" + (_diffSplit ? "s" : "u") + "#" + rows.Count;
        if (_diffLongestFor == key) return;
        _diffLongestFor = key;
        _diffPan.Reset();
        _diffPan.ContentWidth = HorizontalPan.MeasureWidest(
            rows.SelectMany(r => new[] { r.Line?.Content, r.Left?.Content, r.Right?.Content }),
            12f, Fonts.Mono);
    }

    // Vertical scrolling is the list's own (KeyboardScroll); sideways is not, because the
    // list cannot do it — see _diffScrollX.
    private void DiffPaneKey(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Escape when _diffFull:
                _diffFull = false;
                MarkDirty();
                e.StopPropagation = true;
                break;
            case KeyCode.W:
                ToggleDiffWrap();
                e.StopPropagation = true;
                break;
            default:
                if (_diffPan.HandleKey(e)) e.StopPropagation = true;
                break;
        }
    }


    private static List<DiffRow> FlattenUnified(GitFileDiff diff, FileHighlight? hl)
    {
        var rows = new List<DiffRow>();
        foreach (var hunk in diff.Hunks)
        {
            rows.Add(new DiffRow { Type = DiffRowType.Hunk, Hunk = hunk.Header, HunkRef = hunk });
            var lines = hunk.Lines.Where(l => l.Kind != DiffLineKind.HunkHeader).ToList();
            var intra = IntralineByLine(lines);
            foreach (var line in lines)
                rows.Add(new DiffRow { Type = DiffRowType.Unified, Line = line, Spans = DiffSpans(hl, line, intra) });
        }
        return rows;
    }

    private static List<DiffRow> FlattenSplit(GitFileDiff diff, FileHighlight? hl)
    {
        var rows = new List<DiffRow>();
        foreach (var hunk in diff.Hunks)
        {
            rows.Add(new DiffRow { Type = DiffRowType.Hunk, Hunk = hunk.Header, HunkRef = hunk });
            var lines = hunk.Lines.Where(l => l.Kind != DiffLineKind.HunkHeader).ToList();
            var intra = IntralineByLine(lines);
            foreach (var (left, right) in SplitPairs(lines))
                rows.Add(new DiffRow
                {
                    Type = DiffRowType.Split, Left = left, Right = right,
                    LeftSpans = left == null ? null : DiffSpans(hl, left, intra),
                    RightSpans = right == null ? null : DiffSpans(hl, right, intra),
                });
        }
        return rows;
    }

    // ── Intra-line (word-level) change emphasis ──────────────────────────
    //
    // Deletion/addition lines that SplitPairs puts side by side get a word
    // diff; the changed ranges render as brighter background spans on top of
    // the syntax colors. Keyed by line identity: identical lines can repeat
    // within a hunk, and each occurrence pairs with a different partner.

    private static Dictionary<GitDiffLine, IReadOnlyList<(int Start, int End)>> IntralineByLine(
        List<GitDiffLine> lines)
    {
        var byLine = new Dictionary<GitDiffLine, IReadOnlyList<(int Start, int End)>>(
            ReferenceEqualityComparer.Instance);
        foreach (var (left, right) in SplitPairs(lines))
        {
            if (left is not { Kind: DiffLineKind.Deletion } || right is not { Kind: DiffLineKind.Addition })
                continue;
            var (old, @new) = IntralineDiff.Compute(left.Content, right.Content);
            if (old != null) byLine[left] = old;
            if (@new != null) byLine[right] = @new;
        }
        return byLine;
    }

    private static IReadOnlyList<HultUI.Core.Rendering.TextSpan>? DiffSpans(FileHighlight? hl, GitDiffLine line,
        Dictionary<GitDiffLine, IReadOnlyList<(int Start, int End)>> intra)
    {
        var syntax = SpansFor(hl, line);
        if (!intra.TryGetValue(line, out var ranges)) return syntax;
        var bg = line.Kind == DiffLineKind.Addition ? Nocturne.DiffAddHl : Nocturne.DiffDelHl;
        return MergeHighlight(syntax, ranges, bg);
    }

    // Overlays background ranges onto (possibly absent) syntax spans, keeping
    // the result sorted and non-overlapping as TextSpan requires. Segments are
    // cut at every syntax-span or range boundary; a segment with no syntax
    // color gets a transparent foreground (= element text color).
    private static IReadOnlyList<HultUI.Core.Rendering.TextSpan> MergeHighlight(
        IReadOnlyList<HultUI.Core.Rendering.TextSpan>? syntax,
        IReadOnlyList<(int Start, int End)> ranges, Color bg)
    {
        var cuts = new SortedSet<int>();
        foreach (var (s, e) in ranges) { cuts.Add(s); cuts.Add(e); }
        if (syntax != null)
            foreach (var s in syntax) { cuts.Add(s.Start); cuts.Add(s.End); }

        var result = new List<HultUI.Core.Rendering.TextSpan>();
        int prev = -1;
        foreach (var cut in cuts)
        {
            if (prev >= 0 && cut > prev)
            {
                var fg = SyntaxColorAt(syntax, prev);
                bool highlighted = RangeContains(ranges, prev);
                if (fg.A > 0 || highlighted)
                    result.Add(new HultUI.Core.Rendering.TextSpan(
                        prev, cut - prev, fg, highlighted ? bg : default));
            }
            prev = cut;
        }
        return result;
    }

    private static Color SyntaxColorAt(IReadOnlyList<HultUI.Core.Rendering.TextSpan>? syntax, int pos)
    {
        if (syntax != null)
            foreach (var s in syntax)
                if (pos >= s.Start && pos < s.End)
                    return s.Color;
        return Color.Transparent;
    }

    private static bool RangeContains(IReadOnlyList<(int Start, int End)> ranges, int pos)
    {
        foreach (var (s, e) in ranges)
            if (pos >= s && pos < e)
                return true;
        return false;
    }

    private Element RenderDiffRow(DiffRow r, DiffInteract? interact) => r.Type switch
    {
        DiffRowType.Hunk => HunkHeaderRow(r.Hunk, r.HunkRef, interact),
        DiffRowType.Split => SplitRow(r.Left, r.Right,
            FoundIn(r.LeftSpans, r.Index, 0, r.Left?.Content),
            FoundIn(r.RightSpans, r.Index, 1, r.Right?.Content), r.Index),
        _ => UnifiedRow(r.Line!, FoundIn(r.Spans, r.Index, 0, r.Line?.Content), interact, r.Index),
    };

    /// <summary>A code cell's syntax spans with any search matches tinted over them. A match
    /// takes a background and never a foreground, so it stacks on the add/delete tint and the
    /// intra-line emphasis instead of replacing either.</summary>
    private IReadOnlyList<HultUI.Core.Rendering.TextSpan>? FoundIn(
        IReadOnlyList<HultUI.Core.Rendering.TextSpan>? spans, int row, int lane, string? content) =>
        _diffFind.Search.Decorate(spans, row, lane, content?.Length ?? 0,
            Nocturne.FindHitBg, Nocturne.FindCurrentBg);

    /// <summary>
    /// Points the search at the rows on display. Guarded by a key because this runs inside
    /// Build, which reflattens the diff on every repaint — rebinding unguarded would rescan
    /// the whole file every frame.
    /// <para>Split mode is two lanes over one set of row numbers, so a match knows which
    /// column it is in; unified is one. Rows carrying no text of that lane — a hunk header, or
    /// the blank half opposite an add — return null and are skipped.</para>
    /// </summary>
    private void BindDiffSearch(GitFileDiff diff, List<DiffRow> rows)
    {
        var key = (diff, _diffSplit, rows.Count);
        if (Equals(_diffFind.BoundKey, key)) return;
        _diffFind.BoundKey = key;
        _diffFind.Search.Bind(rows.Count, _diffSplit ? 2 : 1, (row, lane) =>
        {
            if (row < 0 || row >= rows.Count) return null;
            var r = rows[row];
            return r.Type switch
            {
                DiffRowType.Split => (lane == 0 ? r.Left : r.Right)?.Content,
                DiffRowType.Unified => r.Line?.Content,
                _ => null,
            };
        });
    }

    /// <summary>Brings the current match on screen. Row tops are summed rather than stored:
    /// unwrapped every row is the same height, and wrapped this runs once per keystroke
    /// rather than once per row per frame.</summary>
    private void RevealDiffHit()
    {
        if (_diffBodyRef.Current is not ScrollViewElement sv) return;
        if (_diffFind.Search.Current is not { } hit) return;

        float top = 0f, height = DiffRowHeight;
        if (_diffWrap && _diffHeights.Length > 0)
        {
            for (int i = 0; i < hit.Line && i < _diffHeights.Length; i++) top += _diffHeights[i];
            if (hit.Line < _diffHeights.Length) height = _diffHeights[hit.Line];
        }
        else top = hit.Line * DiffRowHeight;

        sv.ScrollRangeIntoView(top, height);
    }

    /// <summary>Points the selection groups at the rows currently on display. A different
    /// file or layout rebinds under a new key, which drops a selection that no longer maps.</summary>
    private void BindDiffSelection(GitFileDiff diff, List<DiffRow> rows)
    {
        // null where a row carries no text of that side: a hunk header, or the blank half
        // opposite an add/delete. Those contribute nothing to a copy.
        var key = (diff.Path, _diffSplit, rows.Count);
        _diffGroupUnified.Bind(key, rows.Count, i => At(i)?.Line?.Content);
        _diffGroupLeft.Bind(key, rows.Count, i => At(i)?.Left?.Content);
        _diffGroupRight.Bind(key, rows.Count, i => At(i)?.Right?.Content);

        DiffRow? At(int i) => i >= 0 && i < rows.Count ? rows[i] : null;
    }

    private Element HunkHeaderRow(string header, GitDiffHunk? hunk, DiffInteract? interact)
    {
        var box = Box.New()
            .Padding(new Thickness(5, 14, 5, 14))
            .Background(Nocturne.Alpha(Nocturne.Accent, 0.06))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0))
            .Children(Ui.Mono(header, 11.5f, Nocturne.Accent300).MaxLines(1).OverflowMode(TextOverflow.Ellipsis));

        // Interactive: clicking the hunk header toggles all its changed lines.
        if (interact != null && hunk != null && HunkChangedLines(hunk).Any())
        {
            box.Cursor(CursorType.Pointer).Tooltip("Select all lines in this hunk");
            box.OnClick(_ => ToggleStageHunk(hunk));
        }
        return box;
    }

    private Element UnifiedRow(GitDiffLine line, IReadOnlyList<HultUI.Core.Rendering.TextSpan>? spans,
        DiffInteract? interact, int rowIndex)
    {
        var (bg, code, sign, signColor) = line.Kind switch
        {
            DiffLineKind.Addition => (Nocturne.DiffAddBg, Nocturne.DiffAddText, "+", Nocturne.DiffAddSign),
            DiffLineKind.Deletion => (Nocturne.DiffDelBg, Nocturne.DiffDelText, "−", Nocturne.DiffDelSign),
            _ => (Color.Transparent, Nocturne.On(0.80), " ", Color.Transparent),
        };

        bool selectable = interact != null && line.Kind is DiffLineKind.Addition or DiffLineKind.Deletion;
        bool selected = selectable && _stageSel.Contains(line);
        if (selected) bg = Nocturne.Alpha(Nocturne.Accent, 0.22);

        // Stretch, so the code label can fill the row's height. Centred, the label was only
        // as tall as its own text and the pixel or two above and below it belonged to the
        // row — a strip between the lines where a press began no selection at all.
        // Stretching alone is not enough: the label also has to grow inside its cell, or the
        // cell fills the row and the label still does not fill the cell.
        var row = Flex.Row(gap: 0).AlignItems(AlignItems.Stretch).Background(bg);

        // Interactive mode gets a leading checkbox column (transparent on context
        // rows) so the sign/number columns stay aligned across every row.
        if (interact != null)
        {
            var mark = Box.New().Width(20).FlexShrink(0).JustifyContent(JustifyContent.Center);
            if (selectable)
                mark.AddChild(Ui.Icon(selected ? Icons.CheckSquare : Icons.Square, 12f,
                    selected ? Nocturne.Accent : Nocturne.On(0.34)));
            row.AddChild(mark);
        }

        row.AddChild(NumCell(line.OldLineNumber, 46f));
        row.AddChild(NumCell(line.NewLineNumber, 46f));
        row.AddChild(Box.New().Width(16).JustifyContent(JustifyContent.Center)
            .Children(Ui.Mono(sign, 12f, signColor)));
        // Clip rather than ellipsise: the line is scrollable now, so cutting it at the edge
        // says "there is more that way", where an ellipsis says "this is all there is".
        row.AddChild(DiffCodeCell(
            Ui.Mono(line.Content.Length == 0 ? " " : line.Content, DiffCodeSize, code)
                .Selectable().SelectionGroup(_diffGroupUnified, rowIndex)
                .Spans(spans)
                .Padding(new Thickness(2, 14, 2, 0))));

        if (selectable)
        {
            row.Cursor(CursorType.Pointer);
            row.OnClick(_ => ToggleStageLine(line));
        }
        return row;
    }

    private static IEnumerable<(GitDiffLine? Left, GitDiffLine? Right)> SplitPairs(List<GitDiffLine> lines)
    {
        var i = 0;
        while (i < lines.Count)
        {
            var cur = lines[i];
            if (cur.Kind == DiffLineKind.Context)
            {
                yield return (cur, cur);
                i++;
                continue;
            }
            var dels = new List<GitDiffLine>();
            var adds = new List<GitDiffLine>();
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Deletion) dels.Add(lines[i++]);
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Addition) adds.Add(lines[i++]);
            var n = Math.Max(dels.Count, adds.Count);
            for (var k = 0; k < n; k++)
                yield return (k < dels.Count ? dels[k] : null, k < adds.Count ? adds[k] : null);
        }
    }

    private Element SplitRow(GitDiffLine? left, GitDiffLine? right,
        IReadOnlyList<HultUI.Core.Rendering.TextSpan>? leftSpans,
        IReadOnlyList<HultUI.Core.Rendering.TextSpan>? rightSpans, int rowIndex) =>
        Flex.Row(gap: 0).AlignItems(AlignItems.Stretch)
            .Children(
                SplitHalf(left, isLeft: true, leftSpans, rowIndex),
                SplitHalf(right, isLeft: false, rightSpans, rowIndex));

    private Element SplitHalf(GitDiffLine? line, bool isLeft,
        IReadOnlyList<HultUI.Core.Rendering.TextSpan>? spans, int rowIndex)
    {
        var (bg, code) = line?.Kind switch
        {
            DiffLineKind.Addition => (Nocturne.DiffAddBg, Nocturne.DiffAddText),
            DiffLineKind.Deletion => (Nocturne.DiffDelBg, Nocturne.DiffDelText),
            DiffLineKind.Context => (Color.Transparent, Nocturne.On(0.80)),
            _ => (Nocturne.DiffBlankBg, Color.Transparent),
        };
        var num = isLeft ? line?.OldLineNumber : line?.NewLineNumber;
        var text = line == null ? " " : (line.Content.Length == 0 ? " " : line.Content);

        // Stretch for the same reason as the unified row: a centred label leaves a strip of
        // the row above and below it that swallows presses meant to start a selection.
        var half = Flex.Row(gap: 0).AlignItems(AlignItems.Stretch).FlexGrow(1).FlexBasis(Dimension.Px(0))
            .Background(bg);
        if (isLeft) half.BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0));
        half.AddChild(NumCell(num, 44f));
        // Both halves share one offset, so the two files stay aligned as they move.
        half.AddChild(DiffCodeCell(
            Ui.Mono(text, DiffCodeSize, code).Selectable()
                .SelectionGroup(isLeft ? _diffGroupLeft : _diffGroupRight, rowIndex)
                .Spans(spans)
                .Padding(new Thickness(2, 10, 2, 0))));
        return half;
    }

    private static Element NumCell(int? n, float width) =>
        Box.New().Width(width).FlexShrink(0)
            .JustifyContent(JustifyContent.FlexEnd)
            .Padding(new Thickness(0, 8, 0, 0))
            .Children(Ui.Mono(n?.ToString() ?? "", 11f, Nocturne.On(0.30)));

    // ── Fullscreen diff overlay ──────────────────────────────────────────

    private Element BuildFullscreenDiff()
    {
        var (path, diff) = CurrentDiffTarget();

        var overlay = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(Nocturne.Bg)
            .ZIndex(20);
        overlay.Position.Type = PositionType.Absolute;
        overlay.Position.Top = Dimension.Px(0);
        overlay.Position.Left = Dimension.Px(0);

        var col = Flex.Column(gap: 0).FlexGrow(1);

        var bar = Flex.Row(gap: 12).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(11, 16, 11, 16))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        bar.AddChild(Ui.Icon(Icons.FileCode, 16f, Nocturne.Accent300));
        bar.AddChild(Ui.Mono(path.Length > 0 ? path : "—", 13f, Nocturne.Text)
            .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        bar.AddChild(DiffModeToggle());
        bar.AddChild(Ui.SecondaryButton("Exit fullscreen (Esc)", Icons.ArrowsIn,
            () => { _diffFull = false; MarkDirty(); }, 12f));
        col.AddChild(bar);

        col.AddChild(BuildDiffScroll(diff));
        overlay.AddChild(col);
        return overlay;
    }

    /// <summary>The (path, diff) the fullscreen overlay should show for the active view.</summary>
    private (string Path, GitFileDiff? Diff) CurrentDiffTarget()
    {
        if (View == JunctionView.Commit && _selectedFilePath != null)
        {
            _commitDetail.Value.DiffByPath.TryGetValue(_selectedFilePath, out var d);
            return (_selectedFilePath, d);
        }
        if (View == JunctionView.Changes && _changeSelPath != null)
        {
            var dict = _changeSelStaged ? _changes.Value.StagedDiffs : _changes.Value.UnstagedDiffs;
            dict.TryGetValue(_changeSelPath, out var d);
            return (_changeSelPath, d);
        }
        return (_selectedFilePath ?? "", null);
    }

    private enum ExternalDiffMode { Commit, Staged, Unstaged }

    // Ctrl+D in the Commit / Changes views: open the selected file's diff in VS Code
    // (`code --wait --diff <left> <right>`). Reuses ExternalDiffLauncher's PATH/.cmd
    // resolution and its temp-file plumbing; --wait keeps the launched process alive
    // until the diff tab closes so its temp files survive, then are cleaned on exit.
    private async void OpenSelectedDiffExternally()
    {
        if (ActiveRepo is not { } repo) return;

        string path;
        ExternalDiffMode mode;
        string? commitHash = null;
        string? baseHash = null;
        if (View == JunctionView.Commit && !string.IsNullOrEmpty(_selectedFilePath) && _detailRange is { } r)
        {
            path = _selectedFilePath!;
            mode = ExternalDiffMode.Commit;
            commitHash = r.HeadHash;
            baseHash = r.BaseHash;
        }
        else if (View == JunctionView.Commit && !string.IsNullOrEmpty(_selectedFilePath) && _detailHash is { } h)
        {
            path = _selectedFilePath!;
            mode = ExternalDiffMode.Commit;
            commitHash = h;
        }
        else if (View == JunctionView.Changes && !string.IsNullOrEmpty(_changeSelPath))
        {
            path = _changeSelPath!;
            mode = _changeSelStaged ? ExternalDiffMode.Staged : ExternalDiffMode.Unstaged;
        }
        else
        {
            Flash("Select a file to diff first");
            return;
        }

        Flash("Opening diff in VS Code…");
        var (ok, err) = await Task.Run(() => LaunchVsCodeDiff(repo, path, mode, commitHash, baseHash));
        if (!ok) Flash(string.IsNullOrEmpty(err) ? "Could not open VS Code" : err);
    }

    // Runs off the UI thread (git show + temp-file writes + process start).
    private static (bool ok, string err) LaunchVsCodeDiff(
        GitRepository repo, string path, ExternalDiffMode mode, string? commitHash, string? baseHash = null)
    {
        string leftLabel, rightLabel, left, right;
        switch (mode)
        {
            case ExternalDiffMode.Commit:
                var hash = commitHash!;
                leftLabel = baseHash == null ? "parent" : Short7(baseHash);   // range compare: oldest selected
                rightLabel = Short7(hash);
                left = repo.GetFileText(baseHash ?? hash + "^", path) ?? "";   // "" for an added file / root commit
                right = repo.GetFileText(hash, path) ?? "";
                break;
            case ExternalDiffMode.Staged:
                leftLabel = "HEAD"; rightLabel = "index";
                left = repo.GetFileText("HEAD", path) ?? "";
                right = repo.GetIndexFileText(path) ?? "";
                break;
            default: // Unstaged
                leftLabel = "index"; rightLabel = "working";
                left = repo.GetIndexFileText(path) ?? repo.GetFileText("HEAD", path) ?? "";
                right = repo.GetWorkingFileText(path) ?? "";
                break;
        }

        var request = new ExternalDiffRequest
        {
            RepoPath = repo.Path,
            RelativePath = path,
            LeftLabel = leftLabel,
            LeftContent = left,
            RightLabel = rightLabel,
            RightContent = right,
        };
        var opts = new ExternalToolOptions
        {
            ExternalDiffToolPath = "code",
            ExternalDiffToolArgs = "--wait --diff {left} {right}",
        };
        bool ok = ExternalDiffLauncher.TryLaunch(opts, request, out var err);
        return (ok, err);
    }
}
