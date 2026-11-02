using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using VesperCommander.Theme;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── Diff view (F3) — full-shell builtin ──────────────────────────────
    private enum DiffViewState { Comparing, Ok, Identical, Binary, TooLarge, Missing }

    private bool _diffOpen;
    private char _diffSide;
    private string _diffLeftPath = "", _diffRightPath = "";
    private DiffViewState _diffState;
    private DiffEngine.Result? _diffResult;
    private List<DiffRow> _diffRows = new();
    private List<int> _diffChangeRows = new();
    private int _diffChangeIdx;
    // Whether the user has jumped between changes yet. The current-change ring is drawn only
    // after that, so opening a comparison doesn't look like it has a row selected.
    private bool _diffNavUsed;
    private bool _pendingDiffScroll;
    private readonly Ref<ScrollViewElement> _diffScrollRef = new();
    // The row holding the change n/p last landed on, so its position can be read rather
    // than computed when rows are not all the same height.
    private readonly Ref<Element> _diffChangeRowRef = new();

    // Sideways position, shared by both sides so they stay aligned. See HorizontalPan for
    // why this is not a horizontal ScrollView: a nested one would swallow the wheel the
    // outer vertical scroll needs, and scrolling the outer view would drag the line-number
    // gutters off screen and push the right-hand side out of view entirely.
    private readonly HorizontalPan _diffPan;

    // Selection scopes, one per side. They live for the app's lifetime so a rebuild mid-drag
    // doesn't break the group the window is tracking, and they hold the selection as line
    // numbers into _diffRows rather than as references to the labels showing those lines —
    // so a copy spans the whole selection whether or not it is all on screen.
    private readonly TextSelectionGroup _diffGroupLeft = new();
    private readonly TextSelectionGroup _diffGroupRight = new();

    // Find in the comparison. Two lanes over one set of row numbers — lane 0 is the left
    // column, lane 1 the right — so a match knows which side it is on.
    private readonly FindBar _diffFind;

    /// <summary>Points both side groups at the current comparison. Rebinding a different
    /// model key drops a stale selection, since offsets into the old file mean nothing here.</summary>
    private void BindDiffSelection()
    {
        var key = (_diffLeftPath, _diffRightPath, _diffGen);
        _diffGroupLeft.Bind(key, _diffRows.Count, i => LineTextAt(i, leftSide: true));
        _diffGroupRight.Bind(key, _diffRows.Count, i => LineTextAt(i, leftSide: false));
        // The same model the two groups use, searched across both columns at once. Bound here
        // rather than in Build: the rows change only when a comparison lands, and rebinding
        // per frame would rescan both files on every repaint.
        _diffFind.Search.Bind(_diffRows.Count, lanes: 2,
            (row, lane) => LineTextAt(row, leftSide: lane == 0));
    }

    // null where the model has no line — a collapsed run, or the blank half opposite an
    // add/delete. Those take no highlight and contribute nothing to a copy; a genuinely
    // empty line returns "" and copies as the blank line it is.
    private string? LineTextAt(int row, bool leftSide)
    {
        if (row < 0 || row >= _diffRows.Count) return null;
        if (_diffRows[row].Line is not { } line) return null;
        return leftSide ? line.Left : line.Right;
    }

    // Bumped per comparison, so a syntax tokenize that finishes after the user has moved on
    // can tell that it has.
    private int _diffGen;

    private sealed record DiffRow(DiffEngine.Line? Line, int? CollapseCount)
    {
        /// <summary>Position in <c>_diffRows</c> — the row's identity for selection and for
        /// the height index, neither of which can rely on where the element sits.</summary>
        public int Index { get; init; }
    }

    // Row heights and their running total, for the virtualized body. Wrapped rows differ in
    // height, so the list cannot multiply its way to a row's position; this is the index it
    // asks instead. _diffTops[i] is the Y of row i, _diffTops[^1] the whole body's height.
    private float[] _diffTops = [];
    private float _diffHeightsWidth = -1f;   // code-column width the heights were measured at
    // Wrap mode, pan offset and column width as the last build saw them — the inputs that
    // change what a row looks like without changing which rows there are.
    private (bool Wrap, float Pan, float Width) _diffRowShape = (false, -1f, -1f);

    /// <summary>
    /// Measures every row once, at the current column width. Wrapped, a row is as tall as
    /// whichever side wrapped further; unwrapped, every row is one line and the whole thing is
    /// arithmetic. Called when the comparison, the wrap mode, or the width changes — not per
    /// frame, and never per row per frame.
    /// </summary>
    private void RebuildDiffHeights()
    {
        float width = DiffCodeWidth();
        _diffHeightsWidth = width;
        var tops = new float[_diffRows.Count + 1];
        float y = 0f;
        for (int i = 0; i < _diffRows.Count; i++)
        {
            tops[i] = y;
            y += DiffRowHeight(i, width);
        }
        tops[_diffRows.Count] = y;
        _diffTops = tops;
    }

    /// <summary>
    /// Drops everything measured in the old text face: the monospace advance and line height
    /// the wrap shortcut leans on, and the per-row heights of both the diff and the viewer.
    /// Called when the font setting changes — none of these caches is keyed on the face,
    /// because until now it could not change.
    /// </summary>
    private void InvalidateTextMetrics()
    {
        _monoAdvance = -1f;
        _monoLineHeight = 0f;
        _diffHeightsWidth = -1f;
        _diffTops = [];
        _diffRowShape = (false, -1f, -1f);
        _viewerHeightsWidth = -1f;
        _viewerTops = [];
    }

    private float DiffRowHeight(int index, float width)
    {
        if (!DiffWrap || _diffRows[index].Line is not { } line) return DiffLineH;

        var style = TextMeasure.StyleFor(DiffFontSize, TextFont);
        // The label now fills its cell exactly, so the column width is the wrap width.
        float text = Math.Max(
            WrappedHeight(line.Left ?? "", style, width),
            WrappedHeight(line.Right ?? "", style, width));
        // A blank line still owns a row.
        return Math.Max(DiffLineH, text);
    }

    // ── Monospace fast path for row heights ──────────────────────────────
    //
    // Measuring every row through the text pipeline means shaping every line of both files
    // before the first one can be drawn — the single largest cost of opening a wrapped diff.
    // Almost every line of code, though, is plain ASCII and short enough to fit its column,
    // and in a monospace face that question is arithmetic: length × advance ≤ width. One
    // multiplication instead of a shaper call.
    //
    // It answers only the case it can answer *exactly* — a line that certainly fits, and so
    // is certainly one line tall. Everything else falls back to real measurement, because a
    // height that disagrees with what the row lays out to is not a slow row, it is a clipped
    // one: the list forces the height it is given.
    private float _monoAdvance = -1f;   // 0 = proven not uniform, -1 = not yet proven
    private float _monoLineHeight;

    private float WrappedHeight(string text, ResolvedInheritedStyle style, float width)
    {
        if (FitsOnOneLine(text, width)) return _monoLineHeight;
        return TextMeasure.MeasureWrapped(text, style, width).Height;
    }

    private bool FitsOnOneLine(string text, float width)
    {
        EnsureMonoMetrics();
        if (_monoAdvance <= 0f || width <= 0f) return false;

        foreach (var c in text)
        {
            // Outside the range the advance was proven over — a tab, a combining mark, or
            // anything needing a fallback face — so the arithmetic says nothing.
            if (c < ' ' || c > '~') return false;
        }
        // A margin of one character, so a line that lands within rounding distance of the
        // column edge takes the measured path rather than being guessed at.
        return (text.Length + 1) * _monoAdvance <= width;
    }

    /// <summary>
    /// Proves the diff font really is monospace before anything relies on it: every printable
    /// ASCII character must advance by the same width, and a pair must be exactly twice one,
    /// which rules out kerning. Anything else disables the fast path for good. Prove, never
    /// assume — the font is configurable, and a proportional one would silently mis-size rows.
    /// </summary>
    private void EnsureMonoMetrics()
    {
        if (_monoAdvance >= 0f) return;

        var style = TextMeasure.StyleFor(DiffFontSize, TextFont);
        float advance = TextMeasure.MeasureWidth("M", style);
        if (advance <= 0f) { _monoAdvance = 0f; return; }   // no measure context yet

        foreach (var c in " iMW0(_~lI'")
        {
            if (Math.Abs(TextMeasure.MeasureWidth(c.ToString(), style) - advance) > 0.01f)
            {
                _monoAdvance = 0f;
                return;
            }
        }
        if (Math.Abs(TextMeasure.MeasureWidth("MM", style) - advance * 2f) > 0.01f)
        {
            _monoAdvance = 0f;
            return;
        }

        _monoLineHeight = TextMeasure.MeasureWrapped("M", style, advance * 4f).Height;
        if (_monoLineHeight <= 0f) { _monoAdvance = 0f; return; }
        _monoAdvance = advance;
    }

    /// <summary>Total height of the body, from the row index.</summary>
    private float DiffContentHeight => _diffTops.Length > 0 ? _diffTops[^1] : 0f;

    /// <summary>F3 — compare the cursor file in <paramref name="side"/> with its partner in
    /// the other pane. Honors the DiffTool setting (VS Code launches externally;
    /// Builtin/Junction use this view).</summary>
    private void OpenDiff(char side)
    {
        if (AnyOverlayOpen) return;
        var list = Rows(side);
        int cur = Cursor(side);
        if (cur < 0 || cur >= list.Count || list[cur].IsParent || list[cur].IsDir) return;
        var e = list[cur];
        char other = side == 'L' ? 'R' : 'L';

        _diffLeftPath = e.FullPath;
        _diffRightPath = DiffPartner(other, e);
        _diffSide = side;

        if (_settings.DiffTool == DiffToolKind.VSCode && File.Exists(_diffRightPath))
        {
            FileOps.LaunchDiff("code", _diffLeftPath, _diffRightPath);
            return;
        }

        ComputeDiff();
        RememberPaneScroll();   // the panes are about to be replaced by the view
        _diffOpen = true;
        MarkDirty();
    }

    /// <summary>The file F3 compares <paramref name="e"/> against. The same-named file in the
    /// other pane's folder wins — two copies of one file is the common case — and otherwise
    /// the two cursors pair up, which is what lets F3 compare unrelated names across two
    /// folders instead of reporting the other side missing. A same-named hit that resolves
    /// back to the file we started from is no comparison at all, so it defers to the cursor
    /// too. With nothing to pair against, the same-named path is returned anyway, so the
    /// missing-file report names the path the user expected.</summary>
    private string DiffPartner(char other, FileEntry e)
    {
        var sameName = System.IO.Path.Combine(Dir(other), e.Name);
        if (File.Exists(sameName) && !PathEquals(sameName, e.FullPath)) return sameName;

        var list = Rows(other);
        int cur = Cursor(other);
        if (cur >= 0 && cur < list.Count && list[cur] is { IsParent: false, IsDir: false } partner
            && !PathEquals(partner.FullPath, e.FullPath))
            return partner.FullPath;

        return sameName;
    }

    /// <summary>
    /// The file F3 would compare the active pane's cursor against right now, or null when F3
    /// would do nothing (the cursor is on a folder or on "..").
    ///
    /// <para>Routed through <see cref="DiffPartner"/> rather than reproducing its rule, so the
    /// row the inactive pane rings is the row F3 actually opens. Pairing prefers the
    /// same-named file over the other pane's cursor, so "the other cursor" is only sometimes
    /// the answer — marking that instead would be wrong exactly when it matters most, with two
    /// copies of one file open side by side.</para>
    ///
    /// <para>One <c>File.Exists</c> per pane build, not per row: the panes already stat every
    /// entry they list, and paying it once keeps the indicator and the action from ever
    /// disagreeing.</para>
    /// </summary>
    private string? DiffTargetPath()
    {
        char side = _active;
        var list = Rows(side);
        int cur = Cursor(side);
        if (cur < 0 || cur >= list.Count) return null;
        var e = list[cur];
        if (e.IsParent || e.IsDir) return null;
        return DiffPartner(side == 'L' ? 'R' : 'L', e);
    }

    private void CloseDiff()
    {
        _diffOpen = false;
        _diffResult = null;
        // Bumping the generation orphans any comparison still running, so a slow one cannot
        // land on a view the user has already left.
        _diffGen++;
        // The panes don't exist yet — this build still shows the diff. Ask for the focus
        // once they're back; PaneRef still holds the element from before the view opened.
        _pendingPaneFocus = _diffSide;
        MarkDirty();
    }

    /// <summary>
    /// Reads both files and compares them off the UI thread, then applies the result.
    /// <para>
    /// Reading and diffing are both unbounded work — a 4MB file to parse, and a comparison
    /// whose cost depends on how different the two sides are, not their size. Doing either on
    /// the UI thread froze the window for as long as it took, with no way to back out. Now the
    /// view opens on "Comparing…" and stays responsive, so Esc still closes it.
    /// </para>
    /// <para>
    /// A generation stamp taken before the work starts is checked when it finishes: opening
    /// another comparison, or closing this one, leaves the older result to be discarded rather
    /// than letting it land on top of what the user is now looking at.
    /// </para>
    /// </summary>
    private void ComputeDiff()
    {
        _diffResult = null;
        _diffRows = new();
        _diffChangeRows = new();
        _diffChangeIdx = 0;
        _diffNavUsed = false;
        _diffTops = [];
        _diffPan.Reset();
        _diffPan.ContentWidth = 0;
        _diffState = DiffViewState.Comparing;

        string leftPath = _diffLeftPath, rightPath = _diffRightPath;
        int generation = ++_diffGen;
        var gate = DiffCompareGate;

        Task.Run(async () =>
        {
            if (gate is not null) await gate().ConfigureAwait(false);
            var outcome = CompareFiles(leftPath, rightPath);
            HultUI.Application.InvokeOnMainThread(() =>
            {
                if (_diffGen != generation) return;   // superseded, or the view was closed
                ApplyDiff(outcome);
                MarkDirty();
            });
        });
    }

    /// <summary>Awaited on the worker before the comparison reads either file, so a test can
    /// hold the view on "Comparing…" for as long as it needs and then let it finish. Without
    /// it, how long that state lasts is a race against the thread pool: on a 2-core CI runner
    /// a 400-line pair finished inside the one frame after F3. Null in every shipped path.</summary>
    internal Func<Task>? DiffCompareGate;

    private readonly record struct DiffOutcome(
        DiffViewState State, DiffEngine.Result? Result, string[]? Left, string[]? Right);

    /// <summary>The whole comparison, off the UI thread. Touches no view state.</summary>
    private static DiffOutcome CompareFiles(string leftPath, string rightPath)
    {
        if (!File.Exists(leftPath) || !File.Exists(rightPath))
            return new DiffOutcome(DiffViewState.Missing, null, null, null);

        var (la, lwhy) = LoadForDiff(leftPath);
        var (ra, rwhy) = LoadForDiff(rightPath);
        if (lwhy == TextLoad.TooLarge || rwhy == TextLoad.TooLarge)
            return new DiffOutcome(DiffViewState.TooLarge, null, null, null);
        if (la is null || ra is null)
            return new DiffOutcome(DiffViewState.Binary, null, null, null);
        if (DiffEngine.TooLarge(la, ra))
            return new DiffOutcome(DiffViewState.TooLarge, null, null, null);

        var result = DiffEngine.Diff(la, ra);
        return new DiffOutcome(
            result.Identical ? DiffViewState.Identical : DiffViewState.Ok, result, la, ra);
    }

    private void ApplyDiff(DiffOutcome outcome)
    {
        _diffState = outcome.State;
        _diffResult = outcome.Result;
        if (outcome.State != DiffViewState.Ok || outcome.Left is null || outcome.Right is null) return;

        BuildDiffRowModel();
        _diffPan.ContentWidth = HorizontalPan.MeasureWidest(
            outcome.Left.Concat(outcome.Right), DiffFontSize, TextFont);
        StartDiffHighlight(outcome.Left, outcome.Right);
    }

    /// <summary>The largest file either the diff or the viewer will read. Past this the text
    /// pipeline is not the bottleneck — decoding and splitting are — and the wait stops being
    /// worth it.</summary>
    internal const long ViewMaxBytes = 4L * 1024 * 1024;

    /// <summary>Why a file could not be read as text. Kept apart because they send the reader
    /// to different places: binary means look at the file, too large means look at the size,
    /// and one message for both had people looking for a problem with a 282-byte ini file.</summary>
    private enum TextLoad { Ok, Binary, TooLarge }

    private static (string[]? lines, TextLoad why) LoadForDiff(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (fi.Length > ViewMaxBytes) return (null, TextLoad.TooLarge);
            return LinesFromBytes(File.ReadAllBytes(path));
        }
        catch { return (null, TextLoad.Binary); }
    }

    /// <summary>Decodes a whole file's bytes into lines, or reports it binary. Shared with the
    /// viewer, which reads archive members through the VFS and so has bytes rather than a
    /// path — splitting them the same way here is what makes the two views agree about what a
    /// line is.</summary>
    private static (string[]? lines, TextLoad why) LinesFromBytes(byte[] bytes)
    {
        var text = FileSystem.DecodeText(bytes);
        if (text is null) return (null, TextLoad.Binary);
        return (text.Replace("\r\n", "\n").Split('\n'), TextLoad.Ok);
    }

    /// <summary>Flatten the diff into rendered rows, collapsing long unchanged runs to
    /// 3 lines of context each side of a "⋯ N ⋯" marker, and record the first row of each
    /// change block for n/p navigation.</summary>
    private void BuildDiffRowModel()
    {
        var lines = _diffResult!.Lines;
        var rows = new List<DiffRow>();
        const int ctx = 3, thresh = 8;
        int i = 0;
        while (i < lines.Count)
        {
            if (lines[i].Op == DiffEngine.Op.Equal)
            {
                int start = i;
                while (i < lines.Count && lines[i].Op == DiffEngine.Op.Equal) i++;
                int run = i - start;
                if (run > thresh)
                {
                    for (int k = 0; k < ctx; k++) rows.Add(new DiffRow(lines[start + k], null));
                    rows.Add(new DiffRow(null, run - 2 * ctx));
                    for (int k = run - ctx; k < run; k++) rows.Add(new DiffRow(lines[start + k], null));
                }
                else for (int k = 0; k < run; k++) rows.Add(new DiffRow(lines[start + k], null));
            }
            else rows.Add(new DiffRow(lines[i++], null));
        }

        var changeRows = new List<int>();
        for (int r = 0; r < rows.Count; r++)
        {
            bool changed = rows[r].Line is { Op: not DiffEngine.Op.Equal };
            bool prevChanged = r > 0 && rows[r - 1].Line is { Op: not DiffEngine.Op.Equal };
            if (changed && !prevChanged) changeRows.Add(r);
            // Each row carries its own position: the virtualized body hands the template a
            // row, not an index, and both selection and height lookups need one.
            rows[r] = rows[r] with { Index = r };
        }
        _diffRows = rows;
        _diffTops = [];              // remeasured on the next build, at the current width
        BindDiffSelection();
        _diffChangeRows = changeRows;
    }

    private void DiffNav(int dir)
    {
        if (_diffChangeRows.Count == 0) return;
        _diffNavUsed = true;
        _diffChangeIdx = Math.Clamp(_diffChangeIdx + dir, 0, _diffChangeRows.Count - 1);
        _pendingDiffScroll = true;
        MarkDirty();
    }

    /// <summary>Scroll the diff body by <paramref name="delta"/> px (or to <paramref
    /// name="to"/> when given). The offset survives reconciliation — ScrollViewElement
    /// deliberately doesn't copy it from the incoming build — so it can be set directly,
    /// and the setter clamps to the content and repaints. Only the Ok state builds a
    /// scroll body; without that guard the Ref would still point at the detached
    /// ScrollView left over from a previous comparison.</summary>
    private void DiffScroll(float delta, float? to = null)
    {
        if (_diffState != DiffViewState.Ok || _diffScrollRef.Current is not { } sv) return;
        sv.ScrollOffsetY = to ?? sv.ScrollOffsetY + delta;
    }

    /// <summary>A page is the viewport less one line, so the line you were reading stays
    /// on screen across a PgUp/PgDn.</summary>
    private float DiffPage() => _diffScrollRef.Current is { } sv
        ? Math.Max(DiffLineH, sv.Layout.AbsoluteContentBox.Height - DiffLineH)
        : DiffLineH;

    /// <summary>Width of one code column — the two split whatever the fixed tracks leave
    /// over. Measured from the laid-out body when there is one, and from the window before
    /// that: the first Build after opening a comparison runs before any layout, and it still
    /// has to decide whether there is a scrollbar to show.</summary>
    private float DiffCodeWidth()
    {
        float body = _diffScrollRef.Current is { Layout.AbsoluteContentBox.Width: > 0 } sv
            ? sv.Layout.AbsoluteContentBox.Width
            : _window.LayoutWidth;
        return Math.Max(0, (body - DiffGutterW * 2 - DiffMarkerW) / 2);
    }

    /// <summary>How far right the view can scroll: the longest line, less what one code
    /// column shows of it. Nowhere, when lines wrap instead.</summary>
    /// <summary>Tell the pan how much of a line one code column shows. Wrapped, nothing
    /// scrolls sideways, so the two modes cannot both be on.</summary>
    private void UpdateDiffPan() => _diffPan.ViewportWidth = DiffWrap ? float.MaxValue : DiffCodeWidth();

    /// <summary>Whether a line too wide for its column wraps rather than scrolling sideways.
    /// The two are alternatives: wrapping means every line is visible in full, so there is
    /// nothing to scroll to.</summary>
    private bool DiffWrap => _settings.DiffWrap;

    private void ToggleDiffWrap()
    {
        _settings.DiffWrap = !_settings.DiffWrap;
        SettingsStore.Save(_settings);
        _diffPan.Reset();         // the other mode's offset means nothing in this one
        _pendingDiffScroll = true; // keep the current change in view across the switch
        MarkDirty();
    }

    /// <summary>
    /// Where the current change block starts, in the body's own content space.
    ///
    /// <para>Read straight out of the row index, which knows every row's position whether or
    /// not it has been realized — so this answers for a change a thousand rows below the
    /// viewport, and answers before layout. It used to have to ask the row element for its
    /// laid-out Y, which meant returning null until the row had been through a layout pass.</para>
    /// </summary>
    private float? DiffChangeTop(ScrollViewElement sv)
    {
        int row = _diffChangeRows[_diffChangeIdx];
        if (row < 0 || row >= _diffTops.Length - 1) return null;
        return _diffTops[row];
    }

    /// <summary>Brings the current match on screen. Read from the row index, which knows
    /// every row's position whether or not it has been realized.</summary>
    private void RevealDiffHit()
    {
        if (_diffScrollRef.Current is not { } sv) return;
        if (_diffFind.Search.Current is not { } hit) return;
        if (hit.Line < 0 || hit.Line + 1 >= _diffTops.Length) return;
        sv.ScrollRangeIntoView(_diffTops[hit.Line], _diffTops[hit.Line + 1] - _diffTops[hit.Line]);
    }

    private void HandleDiffKey(KeyDownEvent e)
    {
        // The find field, when it has the keyboard. The view's keys are routed ahead of it, so
        // the bar takes the handful it owns and everything else is skipped while typing —
        // otherwise "w" typed into a query would toggle wrapping.
        bool typing = e.Target is TextInputElement;

        if (_diffFind.HandleKey(e)) { e.StopPropagation = true; return; }
        if (typing) return;

        // The key that opened the comparison closes it, and the copy key copies the missing
        // file across — whatever they are bound to now.
        if (Pressed(Cmd.Diff, e)) { CloseDiff(); e.StopPropagation = true; return; }
        if (Pressed(Cmd.Copy, e))
        {
            if (_diffState == DiffViewState.Missing) { CloseDiff(); StartCopy(); }
            e.StopPropagation = true; return;
        }

        switch (e.Key)
        {
            case KeyCode.Escape: CloseDiff(); e.StopPropagation = true; break;
            // n / p walk the changes. While a search is up the bar has already taken them
            // (FindBar.LetterKeys): stepping through its matches is what they plainly mean then.
            case KeyCode.N: DiffNav(1); e.StopPropagation = true; break;
            case KeyCode.P: DiffNav(-1); e.StopPropagation = true; break;
            // F8 walks the changes the way most comparison tools do; Shift reverses it.
            case KeyCode.F8:
                DiffNav(e.Modifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                e.StopPropagation = true; break;
            // The body is read-only, so the arrows scroll it rather than moving a cursor;
            // n / p remain the way to jump between changes.
            case KeyCode.Up: DiffScroll(-DiffLineH); e.StopPropagation = true; break;
            case KeyCode.Down: DiffScroll(DiffLineH); e.StopPropagation = true; break;
            case KeyCode.PageUp: DiffScroll(-DiffPage()); e.StopPropagation = true; break;
            case KeyCode.PageDown: DiffScroll(DiffPage()); e.StopPropagation = true; break;
            case KeyCode.Home: DiffScroll(0, to: 0); e.StopPropagation = true; break;
            case KeyCode.End: DiffScroll(0, to: float.MaxValue); e.StopPropagation = true; break;
            // Sideways, for lines wider than the pane. Ctrl jumps to either margin.
            case KeyCode.Left or KeyCode.Right:
                _diffPan.HandleKey(e); e.StopPropagation = true; break;
            case KeyCode.W: ToggleDiffWrap(); e.StopPropagation = true; break;
        }
    }

    // ── Diff rendering ───────────────────────────────────────────────────

    /// <summary>The height of one line row. Derived from the text size rather than fixed at
    /// 20, so raising the font does not clip the glyphs into their own row: the eight units
    /// around them are the padding the design chose at 11.5.</summary>
    private float DiffLineH => MathF.Round(DiffFontSize) + 8f;

    /// <summary>The size and the face every body of file text is drawn in — the diff and the
    /// viewer both. Settings; the defaults are what they always were.</summary>
    private float DiffFontSize => _settings.TextFontSize;
    private string TextFont =>
        string.IsNullOrWhiteSpace(_settings.TextFont) ? Fonts.Mono : _settings.TextFont;
    // The fixed tracks of a line row. Kept as constants because the horizontal extent has
    // to work out how much width is left for the two code columns.
    private const float DiffGutterW = 34f;
    private const float DiffMarkerW = 18f;

    private Element BuildDiff()
    {
        var col = Flex.Column(gap: 0).FlexGrow(1).Background(Nocturne.Bg);
        col.AddChild(BuildDiffHeader());
        if (_diffFind.IsOpen)
        {
            var bar = _diffFind.Build();
            // Keyed so it reconciles as itself: a keyless element that comes and goes takes
            // the retained caret and focus of whatever last held its slot.
            bar.Key = "diff-find";
            col.AddChild(bar);
        }
        col.AddChild(BuildDiffSubHeader());
        col.AddChild(_diffState switch
        {
            DiffViewState.Ok => BuildDiffBody(),
            DiffViewState.Identical => DiffMessage(Glyphs.Check, "Files are identical",
                $"{(_diffResult?.Lines.Count ?? 0)} lines · same content"),
            DiffViewState.Binary => DiffMessage(Glyphs.BinaryFile, "Can't show a text diff",
                "One side is binary."),
            // Distinct from binary on purpose: these files are perfectly readable text, and
            // saying otherwise sent people looking for a problem with the files themselves.
            DiffViewState.TooLarge => DiffMessage(Glyphs.BinaryFile, "Too large to compare",
                $"Comparing needs both sides under 4 MB and {DiffEngine.MaxLines:N0} lines."),
            DiffViewState.Comparing => DiffMessage(Glyphs.Check, "Comparing…",
                System.IO.Path.GetFileName(_diffLeftPath)),
            _ => DiffMissingBody(),
        });
        if (_diffState == DiffViewState.Ok && _diffPan.CanPan)
            col.AddChild(_diffPan.Scrollbar(Nocturne.Surface, Nocturne.Neutral700, Nocturne.Divider));
        col.AddChild(BuildDiffFooter());
        return col;
    }

    private Element BuildDiffHeader()
    {
        var h = Flex.Row(gap: 10).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 14, 8, 14))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        var back = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(2, 8, 2, 8)).CornerRadius(5f)
            .Children(Ui.Icon(Glyphs.Back, 16f, Nocturne.Neutral300)).Tooltip("Back · Esc");
        back.OnClick(_ => CloseDiff());
        h.AddChild(back.HoverBg(Nocturne.Neutral900, Color.Transparent));

        h.AddChild(Ui.Icon(Glyphs.Swap, 13f, Nocturne.Accent));
        h.AddChild(Text.New(System.IO.Path.GetFileName(_diffLeftPath)).FontSize(13f)
            .FontWeight(FontWeight.Medium).TextColor(Nocturne.Text));

        if (_diffResult is { } r && _diffState == DiffViewState.Ok)
        {
            h.AddChild(DiffPill($"+{r.Adds}", Nocturne.DiffGreen));
            h.AddChild(DiffPill($"−{r.Dels}", Nocturne.MarkRed));
            h.AddChild(DiffPill($"~{r.Changes}", Nocturne.Accent300));
        }
        h.AddChild(Box.New().FlexGrow(1));

        if (_diffChangeRows.Count > 0)
        {
            h.AddChild(Ui.Mono($"change {_diffChangeIdx + 1} / {_diffChangeRows.Count}", 10.5f, Nocturne.On(0.50)));
            h.AddChild(DiffNavBtn(Glyphs.ArrowUp, "p", () => DiffNav(-1)));
            h.AddChild(DiffNavBtn(Glyphs.ArrowDown, "n", () => DiffNav(1)));
        }
        return h;
    }

    private Element DiffPill(string text, Color c) =>
        Box.New().Padding(new Thickness(1, 7, 2, 7)).CornerRadius(9f)
            .Background(Nocturne.Alpha(c, 0.16))
            .Children(Ui.Mono(text, 10.5f, c));

    private Element DiffNavBtn(string icon, string tip, Action onClick)
    {
        var b = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(2, 7, 2, 7)).CornerRadius(4f)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .Children(Ui.Icon(icon, 12f, Nocturne.Neutral300)).Tooltip($"{tip}");
        b.OnClick(_ => onClick());
        return b.HoverBg(Nocturne.Neutral900, Color.Transparent);
    }

    private Element BuildDiffSubHeader()
    {
        var row = Grid.Of("1fr 1fr", gap: 0).FlexShrink(0)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        row.AddChild(DiffPathCell("LEFT", _diffLeftPath).GridColumn(0));
        row.AddChild(DiffPathCell("RIGHT", _diffRightPath).GridColumn(1));
        return row;
    }

    private Element DiffPathCell(string tag, string path)
    {
        var cell = Flex.Row(gap: 8).AlignItems(AlignItems.Center).Padding(new Thickness(5, 12, 5, 12));
        // Same treatment as the timestamp below, and for the same reason: the path between
        // them is the only thing that should give way. Without it a narrow pane squeezed
        // the tag until it folded, showing "LEF/T" stacked over two lines.
        cell.AddChild(Ui.Mono(tag, 9.5f, Nocturne.On(0.40)).MaxLines(1).FlexShrink(0));
        cell.AddChild(Ui.Mono(path, 11f, Nocturne.On(0.60)).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).FlexGrow(1));
        if (File.Exists(path))
        {
            try
            {
                var fi = new FileInfo(path);
                // Fixed size, never wrapped: the path beside it grows and ellipsises, so it
                // is the one that should give way. Without this the timestamp folded onto a
                // second line and spilled over the first row of the diff.
                cell.AddChild(Ui.Mono($"{FileSystem.ByteCount(fi.Length)} · {fi.LastWriteTime:yyyy-MM-dd HH:mm}",
                    10f, Nocturne.On(0.40)).MaxLines(1).FlexShrink(0));
            }
            catch { }
        }
        return cell;
    }

    private Element BuildDiffBody()
    {
        UpdateDiffPan();

        // What a row looks like depends on three things the list cannot see: the wrap mode,
        // how far the view is panned sideways, and the column width the text wraps into. A
        // realized row is reused as-is across a patch, so when any of them moves the rows have
        // to be thrown away and rebuilt — and the heights remeasured, since two of the three
        // change how tall a row is.
        var shape = (DiffWrap, _diffPan.Offset, DiffCodeWidth());
        // And what else a row shows: the change F8 last jumped to (ringed), find's query,
        // options and match count (tinted), and the syntax colouring once it lands. The rows
        // are on item rows, so a rebuild keeps every row whose DiffRow is unchanged; when any of
        // these changes the rows are made again. The rest comes with something already handled:
        // another comparison is another list (keyed by its generation), new rows and the text
        // face reset the heights above, and another colour scheme is another Theme object,
        // which makes a list's rows again by itself. The current match is tinted on its own row
        // only, so a step to the next one makes the row it left and the row it reached.
        var look = new DiffRowLook(CurrentDiffChangeRow(), DiffFindShape(), _diffHlLeft, _diffHlRight);
        bool remake = look != _diffRowLook;
        _diffRowLook = look;
        if (_diffTops.Length != _diffRows.Count + 1 || _diffRowShape != shape)
        {
            if (_diffRowShape.Item1 != shape.Item1 || _diffHeightsWidth != shape.Item3
                || _diffTops.Length != _diffRows.Count + 1)
                RebuildDiffHeights();
            remake = true;
            _diffRowShape = shape;
        }
        var hit = CurrentHit(_diffFind.Search);
        if (_diffScrollRef.Current is VirtualListElement<DiffRow> mounted)
            RemakeForFind(mounted, remake, _diffHit, hit);
        _diffHit = hit;

        var list = VirtualListElement<DiffRow>.New()
            .ItemHeight(DiffLineH)
            .RowHeights((_, row) => row + 1 < _diffTops.Length ? _diffTops[row + 1] - _diffTops[row] : DiffLineH)
            .Items(_diffRows)
            // No list selection: `selected` is always false, and a row is its DiffRow and the
            // look above.
            .RenderWith((r, _) => BuildDiffRow(r))
            // Named for assistive technology by what the row says. Unset, each item was read out
            // as the DiffRow record's ToString() — "DiffRow { Line = Line { Op = Change, … } }".
            .AccessibleItemLabel(_diffLabelFn ??= DiffRowLabel)
            .FlexGrow(1)
            .Background(Nocturne.Bg);
        // Keyed by comparison so each file pair starts at the top instead of inheriting the
        // previous one's offset, and so toggling wrap reconciles rather than rebuilding.
        list.Key = $"diff:{_diffLeftPath}|{_diffRightPath}|{_diffGen}";
        list.WithRef(_diffScrollRef);
        // The list's content is exactly as wide as its viewport, so it cannot scroll sideways
        // itself: it is told who does, and the wheel's sideways motion (a trackpad swipe, or
        // Shift+wheel) and the middle-click autoscroll's both go to the pan.
        list.ScrollSidewaysWith(_diffPan);
        return list;
    }

    // The rows' labels, made the first time assistive technology asks for each and kept with
    // the comparison's rows: the tree asks for every item's label each time it is built, and
    // a label made per ask would be a string per row per keystroke.
    private (List<DiffRow>? Rows, string?[] Labels) _diffLabels = (null, []);
    private Func<DiffRow, string?>? _diffLabelFn;

    private string DiffRowLabel(DiffRow r)
    {
        if (!ReferenceEquals(_diffLabels.Rows, _diffRows)) _diffLabels = (_diffRows, new string?[_diffRows.Count]);
        var labels = _diffLabels.Labels;
        return (uint)r.Index < (uint)labels.Length ? labels[r.Index] ??= DiffRowText(r) : DiffRowText(r);
    }

    /// <summary>What a reader hears for a row: what happened to the line, where, and its text.</summary>
    private static string DiffRowText(DiffRow r)
    {
        if (r.CollapseCount is int n) return $"{n} unchanged line{(n == 1 ? "" : "s")}";
        if (r.Line is not { } l) return "";
        return l.Op switch
        {
            DiffEngine.Op.Del => $"removed, line {l.LeftNo}: {Spoken(l.Left)}",
            DiffEngine.Op.Add => $"added, line {l.RightNo}: {Spoken(l.Right)}",
            DiffEngine.Op.Change => $"changed, left line {l.LeftNo}: {Spoken(l.Left)}; right line {l.RightNo}: {Spoken(l.Right)}",
            _ => l.LeftNo == l.RightNo
                ? $"line {l.LeftNo}: {Spoken(l.Right)}"
                : $"line {l.LeftNo} left, {l.RightNo} right: {Spoken(l.Right)}",
        };
    }

    /// <summary>A line's text as a reader says it: an empty or all-blank line is "blank".</summary>
    internal static string Spoken(string? text) => string.IsNullOrWhiteSpace(text) ? "blank" : text;

    /// <summary>What a comparison's rows show besides their own <see cref="DiffRow"/>, as the rows
    /// on screen were made with it (see <see cref="BuildDiffBody"/>).</summary>
    private readonly record struct DiffRowLook(
        int CurrentChange, (string Query, bool MatchCase, bool Regex, int Hits) Find,
        IReadOnlyList<TextSpan>[]? HighlightLeft, IReadOnlyList<TextSpan>[]? HighlightRight);

    private DiffRowLook _diffRowLook;
    private (int Index, int Line) _diffHit = (-1, -1);

    /// <summary>The row F8 last jumped to, which is ringed, or -1. Only once the user has jumped:
    /// on opening, the index already points at the first change.</summary>
    private int CurrentDiffChangeRow() =>
        _diffNavUsed && _diffChangeRows.Count > 0 && _diffChangeIdx < _diffChangeRows.Count
            ? _diffChangeRows[_diffChangeIdx] : -1;

    /// <summary>What find tints on every row: the query and its options and how many matches
    /// there are (the matches themselves only move with a new query, option or binding, and a
    /// new binding comes with new rows). The current match is not in it: it is tinted on one row
    /// (see <see cref="RemakeForFind"/>).</summary>
    private (string, bool, bool, int) DiffFindShape()
    {
        var s = _diffFind.Search;
        return (s.Query, s.MatchCase, s.UseRegex, s.Hits.Count);
    }

    /// <summary>One row of the virtualized body. Called only for rows near the viewport.</summary>
    private Element BuildDiffRow(DiffRow r)
    {
        bool current = r.Index == CurrentDiffChangeRow();
        var built = r.CollapseCount is int n ? DiffCollapseRow(n) : DiffLineRow(r.Line!, current, r.Index);
        if (current) built.WithRef(_diffChangeRowRef);
        return built;
    }

    private Element DiffCollapseRow(int n) =>
        Box.New().Height(DiffLineH).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Background(Nocturne.Alpha(Nocturne.Text, 0.02))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 1, 0))
            .Children(Ui.Mono($"⋯ {n} unchanged line{(n == 1 ? "" : "s")} ⋯", 10f, Nocturne.On(0.35)));

    private Element DiffLineRow(DiffEngine.Line line, bool current, int rowIndex)
    {
        var (leftBg, rightBg, marker, markColor) = line.Op switch
        {
            DiffEngine.Op.Change => (Nocturne.DiffChangeBg, Nocturne.DiffChangeBg, "~", Nocturne.Accent300),
            DiffEngine.Op.Del => (Nocturne.DiffDelBg, HatchBg, "−", Nocturne.MarkRed),
            DiffEngine.Op.Add => (HatchBg, Nocturne.DiffAddBg, "+", Nocturne.DiffGreen),
            _ => (Color.Transparent, Color.Transparent, " ", Color.Transparent),
        };

        // Wrapping needs no alignment work of its own: the row track is auto, so it grows to
        // whichever side wrapped taller and every cell in it — both gutters included — shares
        // that band. A minimum keeps a blank line a row tall.
        var row = Grid.Of($"{DiffGutterW}px 1fr {DiffMarkerW}px {DiffGutterW}px 1fr", gap: 0)
            .AlignItems(AlignItems.Stretch);
        if (DiffWrap) row.MinHeight(DiffLineH); else row.Height(DiffLineH);
        // Only once the user has actually jumped to a change. On opening, the index still
        // points at the first one, and ringing it made the top row read as a selected row in
        // a list — an interaction the view doesn't have.
        // Offset -1 puts the ring exactly where the border was, in the row's outermost pixel:
        // rows here touch, so a ring outside would be painted over by the next row. As a
        // border it grew the row 2px, which in wrap mode (MinHeight, no fixed height) pushed
        // every row below it down.
        if (current) row.Outline(1f, Nocturne.Accent600, offset: -1f);

        // On a changed line, mark only the differing middle — the common prefix/suffix stay
        // plain, so the eye lands on what actually changed.
        (int start, int len)? hiL = null, hiR = null;
        if (line.Op is DiffEngine.Op.Change && line.Left != null && line.Right != null)
        {
            var (pre, suf) = DiffEngine.IntraSpan(line.Left, line.Right);
            hiL = (pre, Math.Max(0, line.Left.Length - suf - pre));
            hiR = (pre, Math.Max(0, line.Right.Length - suf - pre));
        }

        // Syntax, then the intra-line change marker, then any search matches on top. All
        // three ride as backgrounds except the syntax colour, so they stack rather than
        // replace one another.
        var spansL = _diffFind.Search.Decorate(
            MergeDiffSpans(SyntaxFor(leftSide: true, line.LeftNo), hiL, line.Left?.Length ?? 0),
            rowIndex, lane: 0, line.Left?.Length ?? 0, Nocturne.FindHitBg, Nocturne.FindCurrentBg);
        var spansR = _diffFind.Search.Decorate(
            MergeDiffSpans(SyntaxFor(leftSide: false, line.RightNo), hiR, line.Right?.Length ?? 0),
            rowIndex, lane: 1, line.Right?.Length ?? 0, Nocturne.FindHitBg, Nocturne.FindCurrentBg);

        row.AddChild(DiffGutter(line.LeftNo).GridColumn(0));
        row.AddChild(DiffTextCell(line.Left, leftBg, line.Op is DiffEngine.Op.Del ? Nocturne.MarkRed : Nocturne.On(0.78), _diffGroupLeft, rowIndex, spansL).GridColumn(1));
        row.AddChild(Box.New().AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Children(Ui.Mono(marker, 11f, markColor)).GridColumn(2));
        row.AddChild(DiffGutter(line.RightNo).GridColumn(3));
        row.AddChild(DiffTextCell(line.Right, rightBg, line.Op is DiffEngine.Op.Add ? Nocturne.DiffGreenText : Nocturne.On(0.78), _diffGroupRight, rowIndex, spansR).GridColumn(4));
        return row;
    }

    private static Color HatchBg => Nocturne.Alpha(Nocturne.Text, 0.02);

    // A Box lays out as a *column*, so JustifyContent is the vertical axis and AlignItems
    // the horizontal one — the opposite of the row-shaped cells everywhere else in this
    // file. Both cells below want "vertically centred, horizontally not centred", which is
    // JustifyContent.Center; using AlignItems.Center for it centred the text instead.
    // Wrapped, the number belongs beside the *first* of the line's rows rather than floating
    // in the middle of a five-row block, so it sits at the top instead of centred.
    private Element DiffGutter(int no) =>
        Box.New().AlignItems(AlignItems.FlexEnd)
            .JustifyContent(DiffWrap ? JustifyContent.FlexStart : JustifyContent.Center)
            .Padding(new Thickness(DiffWrap ? 3 : 0, 6, 0, 0))
            .Children(Ui.Mono(no > 0 ? no.ToString() : "", 10.5f, Nocturne.Neutral700));

    private Element DiffTextCell(string? text, Color bg, Color fg, TextSelectionGroup selectionGroup,
        int rowIndex, IReadOnlyList<TextSpan>? spans = null)
    {
        // AlignItems is left at its Stretch default so the line fills the cell and clips at
        // the cell's own edge rather than at the width of the text. OverflowX.Hidden is what
        // makes the horizontal offset below safe: without it the shifted text would spill
        // left over the line-number gutter. Wrapped, the cell grows instead of clipping, so
        // the content sits at the top rather than centred in a row it no longer defines.
        var box = Box.New().Background(bg)
            .JustifyContent(JustifyContent.FlexStart)
            // No padding. It used to sit here, on the box that also paints the row's colour,
            // which cost twice: the line's tinted band stopped short of the text by 8px on
            // each side so added and removed rows looked unevenly sized, and a click in that
            // margin landed on the box rather than the label, so selecting a line by its
            // leading whitespace — the natural place to start a drag — did nothing at all.
            .OverflowX(Overflow.Hidden);
        // Wrapped there is no offset to apply, and the cell already clips.

        // Grows on the vertical axis — the main axis of the column boxes it sits in — so the
        // label is as tall as its row. Without it the label keeps its own text height and the
        // remainder of the row belongs to the box, which is a strip that swallows presses.
        var mono = Ui.Code(text ?? "", DiffFontSize, TextFont, fg).FlexGrow(1);
        if (!DiffWrap) mono.MaxLines(1).TextOverflow(TextOverflow.Clip);
        if (spans is { Count: > 0 }) mono.Spans(spans);

        // Horizontal scroll. A negative left margin moves the line left and — because the
        // stretch that sizes it subtracts the horizontal margin — widens it by the same
        // amount, so the text that scrolls in from the right is actually laid out.

        // Selectable, with one group per side: a drag runs down the lines of one file
        // rather than zig-zagging between the two. The window owns the group drag and the
        // Ctrl+C that copies it.
        mono.Selectable().SelectionGroup(selectionGroup, rowIndex);

        // Filling the cell rather than sitting centred in it. Centring left a few pixels of
        // slack above and below that belonged to the box, not the line — so a press near the
        // top or bottom edge of a row selected nothing, the same dead strip the horizontal
        // padding used to leave at each end.
        box.AddChild((DiffWrap ? mono : _diffPan.Cell(mono)).FlexGrow(1));
        return box;
    }

    private Element DiffMessage(string icon, string title, string sub)
    {
        var box = Flex.Column(gap: 8).FlexGrow(1).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        box.AddChild(Ui.Icon(icon, 26f, Nocturne.Accent300));
        box.AddChild(Text.New(title).FontSize(13f).TextColor(Nocturne.Text));
        box.AddChild(Ui.Mono(sub, 11f, Nocturne.On(0.45)));
        return box;
    }

    private Element DiffMissingBody()
    {
        bool rightMissing = !File.Exists(_diffRightPath);
        var box = Flex.Column(gap: 10).FlexGrow(1).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        box.AddChild(Ui.Icon(Glyphs.Prohibit, 24f, Nocturne.MarkRed));
        box.AddChild(Text.New(rightMissing ? "Right file missing" : "Left file missing")
            .FontSize(13f).TextColor(Nocturne.Text));
        box.AddChild(Ui.Mono((rightMissing ? _diffRightPath : _diffLeftPath) + " — not found", 10.5f, Nocturne.On(0.45)));
        if (rightMissing)
        {
            var btn = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(5, 12, 5, 12))
                .CornerRadius(Nocturne.RadiusSm).BorderColor(Nocturne.Accent700).BorderWidth(1f);
            var lbl = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
            lbl.AddChild(Text.New("Copy left → right").FontSize(11.5f).TextColor(Nocturne.Accent200));
            if (KeyText(Cmd.Copy) is { Length: > 0 } copyKeys) lbl.AddChild(Ui.Kbd(copyKeys));
            btn.AddChild(lbl);
            btn.OnClick(_ => { CloseDiff(); StartCopy(); });
            box.AddChild(btn);
        }
        return box;
    }

    private Element BuildDiffFooter()
    {
        var footer = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(5, 12, 5, 12))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        footer.AddChild(Ui.Mono("read-only", 10f, Nocturne.On(0.40)));
        footer.AddChild(Box.New().FlexGrow(1));
        if (_diffState == DiffViewState.Ok)
        {
            footer.AddChild(Ui.Kbd("↑↓"));
            if (_diffPan.CanPan) footer.AddChild(Ui.Kbd("←→"));
            footer.AddChild(Ui.Mono("scroll", 10f, Nocturne.On(0.45)));
            footer.AddChild(Ui.Kbd("w"));
            footer.AddChild(Ui.Mono(DiffWrap ? "no wrap" : "wrap", 10f, Nocturne.On(0.45)));
            if (!_diffFind.IsOpen)
            {
                footer.AddChild(Ui.Kbd("Ctrl+F"));
                footer.AddChild(Ui.Mono("find", 10f, Nocturne.On(0.45)));
            }
        }
        if (_diffChangeRows.Count > 0)
        {
            footer.AddChild(Ui.Kbd("F8")); footer.AddChild(Ui.Kbd("n")); footer.AddChild(Ui.Kbd("p"));
            footer.AddChild(Ui.Mono("change", 10f, Nocturne.On(0.45)));
        }
        footer.AddChild(Ui.Kbd("Esc")); footer.AddChild(Ui.Mono("back", 10f, Nocturne.On(0.45)));
        return footer;
    }
}
