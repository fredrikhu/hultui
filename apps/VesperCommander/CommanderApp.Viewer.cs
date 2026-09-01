using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Syntax;
using VesperCommander.Theme;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── File viewer (Shift+F3) — full-shell builtin ──────────────────────
    //
    // The preview pane shows the leading 500 lines of a file, truncated to 200 characters
    // each, because it is a glance beside the file list rather than something to read. This
    // is the reader: the whole file, in one virtualized column, with the diff's syntax
    // colouring, selection and sideways pan.
    //
    // It shares almost everything with the diff on purpose — the same monospace metrics, the
    // same wrap preference, the same row shape — because the two views draw the same thing
    // and the traps that cost real bugs in the diff (measuring a wrapped row against the
    // wrong width, a label that doesn't fill its row) would cost them again here.

    private enum ViewerState { Loading, Ok, Binary, Missing, Unreadable }

    private bool _viewerOpen;
    private char _viewerSide;
    private string _viewerPath = "";
    // Set when the file being read is an archive member rather than something on disk. The
    // preview already reads members, so the reader that replaces it has to as well — the
    // alternative is a file you can glance at but not open. Null for an ordinary file.
    private FileEntry? _viewerMember;
    private string? _viewerPassword;
    private ViewerState _viewerState;
    private string[] _viewerLines = [];
    // The list's items: one entry per line, holding that line's index. Indices rather than
    // the strings themselves, because a row needs to know where it sits — for its line
    // number, for its selection slot, and for its height.
    private int[] _viewerItems = [];

    // Bumped per file opened, so a read or a tokenize that finishes after the user has moved
    // on can tell that it has.
    private int _viewerGen;

    private readonly Ref<ScrollViewElement> _viewerScrollRef = new();

    // Sideways position. Same reasoning as the diff's: a nested horizontal ScrollView would
    // swallow the wheel the vertical one needs, and scrolling the outer view would drag the
    // line-number gutter off screen.
    private readonly HorizontalPan _viewerPan;

    // One selection scope for the file, holding line numbers rather than references to the
    // labels showing those lines — so a copy spans the whole selection whether or not all of
    // it has been realized.
    private readonly TextSelectionGroup _viewerGroup = new();

    // Row tops and the inputs they were measured under. Same index the diff keeps: _viewerTops[i]
    // is the Y of row i and _viewerTops[^1] the whole body's height.
    private float[] _viewerTops = [];
    private float _viewerHeightsWidth = -1f;
    private (bool Wrap, float Pan, float Width) _viewerRowShape = (false, -1f, -1f);

    private IReadOnlyList<TextSpan>[]? _viewerHl;
    private string? _viewerHlKey;

    /// <summary>Whether long lines wrap rather than scrolling sideways. Deliberately the
    /// <i>same</i> preference the diff uses: both views show code in the same font at the
    /// same size, and keeping two settings for it meant answering the same question twice.</summary>
    private bool ViewerWrap => _settings.DiffWrap;

    // The same size as the diff's code text, which is also what the shared monospace metrics
    // (_monoAdvance / _monoLineHeight, see CommanderApp.Diff.cs) were proven at — measuring
    // the viewer at a different size would make those cached figures wrong. Both now come
    // from the one text-font setting, so they cannot drift apart.
    private float ViewerFontSize => DiffFontSize;

    /// <summary>Shift+F3 — open the cursor file in <paramref name="side"/> for reading.</summary>
    private void OpenViewer(char side)
    {
        if (AnyOverlayOpen) return;
        var list = Rows(side);
        int cur = Cursor(side);
        if (cur < 0 || cur >= list.Count || list[cur].IsParent || list[cur].IsDir) return;
        var entry = list[cur];

        _viewerSide = side;
        _viewerPath = entry.FullPath;
        // Inside an archive the entry is read through the VFS rather than opened by path.
        // The password lookup happens here, on the UI thread, so the read does not race the
        // dictionary the password prompt writes into.
        _viewerMember = InArchive(side) ? entry : null;
        _viewerPassword = _viewerMember is null ? null : PasswordFor(entry.FullPath);
        LoadViewer();
        _viewerOpen = true;
        MarkDirty();
    }

    private void CloseViewer()
    {
        _viewerOpen = false;
        _viewerLines = [];
        _viewerItems = [];
        _viewerTops = [];
        // Orphans a read or a tokenize still running, so a slow one cannot land on a view the
        // user has already left.
        _viewerGen++;
        _viewerHlKey = null;
        _viewerHl = null;
        // The panes don't exist yet — this build still shows the viewer. Ask for the focus
        // once they're back; PaneRef still holds the element from before the view opened.
        _pendingPaneFocus = _viewerSide;
        MarkDirty();
    }

    /// <summary>
    /// Reads the file off the UI thread and applies it when it lands.
    /// <para>
    /// A 4MB file is several tens of megabytes of string once decoded and split, and none of
    /// that should be spent with the window frozen — the view opens on "Loading…" and stays
    /// responsive, so Esc still closes it.
    /// </para>
    /// </summary>
    private void LoadViewer()
    {
        _viewerLines = [];
        _viewerItems = [];
        _viewerTops = [];
        _viewerHl = null;
        _viewerHlKey = null;
        _viewerPan.Reset();
        _viewerPan.ContentWidth = 0;
        _viewerState = ViewerState.Loading;
        // The matches index into the file being replaced. The query itself is kept, so
        // opening the next file searches it for the same thing.
        _viewerFind.Search.Unbind();

        string path = _viewerPath;
        var member = _viewerMember;
        string? password = _viewerPassword;
        int generation = ++_viewerGen;

        Task.Run(() =>
        {
            var (state, lines) = member is null ? ReadViewerFile(path) : ReadViewerMember(member, password);
            HultUI.Application.InvokeOnMainThread(() =>
            {
                if (_viewerGen != generation) return;   // superseded, or the view was closed
                ApplyViewer(state, lines);
                MarkDirty();
            });
        });
    }

    /// <summary>The diff's reader, unchanged: it keeps the 4MB ceiling and the NUL scan that
    /// tells text from binary, and it is the one that decided how the compared files were
    /// split — so the two views agree about what a line is.</summary>
    private static (ViewerState, string[]?) ReadViewerFile(string path)
    {
        if (!File.Exists(path)) return (ViewerState.Missing, null);
        var (lines, binary) = LoadForDiff(path);
        return binary || lines is null ? (ViewerState.Binary, null) : (ViewerState.Ok, lines);
    }

    /// <summary>
    /// The same, for an entry inside an archive. The three ways this can fail are kept apart
    /// on purpose: too large, unreadable and genuinely binary each get their own message,
    /// because "can't show this as text" sends someone looking at a file that is fine when
    /// what actually happened is that the archive wanted a password.
    /// </summary>
    private (ViewerState, string[]?) ReadViewerMember(FileEntry member, string? password)
    {
        if (member.Size > ViewMaxBytes) return (ViewerState.Binary, null);
        // A zero-length member reads as an empty file, not as one that could not be read —
        // ReadMemberBytes declines to open it, so there is nothing to ask it for.
        if (member.Size == 0) return (ViewerState.Ok, [""]);

        var bytes = ReadMemberBytes(member, ViewMaxBytes, password);
        if (bytes is null) return (ViewerState.Unreadable, null);
        var (lines, binary) = LinesFromBytes(bytes);
        return binary || lines is null ? (ViewerState.Binary, null) : (ViewerState.Ok, lines);
    }

    private void ApplyViewer(ViewerState state, string[]? lines)
    {
        _viewerState = state;
        if (state != ViewerState.Ok || lines is null) return;

        _viewerLines = lines;
        _viewerItems = new int[lines.Length];
        for (int i = 0; i < lines.Length; i++) _viewerItems[i] = i;
        // Selection is bound to this file: rebinding a different key drops a selection whose
        // offsets mean nothing in the new one.
        _viewerGroup.Bind((_viewerPath, _viewerGen), lines.Length,
            i => i >= 0 && i < _viewerLines.Length ? _viewerLines[i] : null);
        _viewerPan.ContentWidth = HorizontalPan.MeasureWidest(lines, ViewerFontSize, TextFont);
        StartViewerHighlight(lines);
        BindViewerSearch();
    }

    /// <summary>Tokenize the whole file off the UI thread, exactly as the diff does — a
    /// TextMate grammar carries state across lines, so a per-visible-line tokenize would
    /// mis-colour anything inside a block comment or a multi-line string.</summary>
    private void StartViewerHighlight(string[] lines)
    {
        _viewerHl = null;
        _viewerHlKey = null;
        if (!SyntaxHighlighter.CanHighlight(_viewerPath)) return;   // no grammar for this extension

        string path = _viewerPath;
        var key = _viewerHlKey = $"{path} {_viewerGen}";
        Task.Run(() =>
        {
            var spans = Tokenize(path, lines);
            HultUI.Application.InvokeOnMainThread(() =>
            {
                if (_viewerHlKey != key) return;   // superseded
                _viewerHl = spans;
                MarkDirty();
            });
        });
    }

    /// <summary>Syntax spans for a 0-based line, or null while the tokenize is still running
    /// (or when the file has no grammar).</summary>
    private IReadOnlyList<TextSpan>? ViewerSpansFor(int line) =>
        _viewerHl is { } hl && line >= 0 && line < hl.Length ? hl[line] : null;

    // ── Geometry ─────────────────────────────────────────────────────────

    // Memoized per digit count, and deliberately the one place the figure is decided: the
    // grid track and the wrap width are the same number seen from two sides, and if they ever
    // disagreed the rows would be measured against a width the text does not get.
    private int _viewerGutterW = (int)DiffGutterW;
    private int _viewerGutterDigits = -1;

    /// <summary>
    /// Width of the gutter, from how many digits the largest line number needs. A fixed width
    /// is fine for the diff, whose files are capped well below six figures; this view will
    /// happily open a 200,000-line log, and the numbers have to fit.
    /// <para>
    /// An <c>int</c>, because the row's grid track is built by interpolating this into a
    /// string and the grid parses its numbers as invariant. A fractional width picks up the
    /// thread's decimal separator instead — on a machine whose locale writes <c>43,5</c> the
    /// template becomes <c>"43,5px 1fr"</c>, which the parser rejects outright, so the view
    /// throws for anyone whose files run past four digits and no one else.
    /// </para>
    /// </summary>
    private int ViewerGutterW()
    {
        int digits = Math.Max(2, _viewerLines.Length.ToString().Length);
        if (digits == _viewerGutterDigits) return _viewerGutterW;

        // 12px covers the gutter's own right padding plus a little air before the code.
        float measured = TextMeasure.MeasureWidth(new string('0', digits), 10.5f, Fonts.Mono);
        if (measured <= 0) return (int)DiffGutterW;   // no measure context yet — don't cache a guess
        _viewerGutterDigits = digits;
        return _viewerGutterW = (int)MathF.Ceiling(Math.Max(DiffGutterW, measured + 12f));
    }

    /// <summary>Width of the code column. Measured from the laid-out body when there is one
    /// and from the window before that — the first Build after opening runs before any
    /// layout, and it still has to decide how tall a wrapped row is.</summary>
    private float ViewerCodeWidth()
    {
        float body = _viewerScrollRef.Current is { Layout.AbsoluteContentBox.Width: > 0 } sv
            ? sv.Layout.AbsoluteContentBox.Width
            : _window.LayoutWidth;
        return Math.Max(0, body - ViewerGutterW());
    }

    /// <summary>Measures every row once, at the current column width. Unwrapped every row is
    /// one line tall and the whole thing is arithmetic; wrapped, each is as tall as its line
    /// laid out into the column. Called when the file, the wrap mode or the width changes —
    /// not per frame, and never per row per frame.</summary>
    private void RebuildViewerHeights()
    {
        float width = ViewerCodeWidth();
        _viewerHeightsWidth = width;
        var tops = new float[_viewerLines.Length + 1];
        float y = 0f;
        for (int i = 0; i < _viewerLines.Length; i++)
        {
            tops[i] = y;
            y += ViewerRowHeight(i, width);
        }
        tops[_viewerLines.Length] = y;
        _viewerTops = tops;
    }

    private float ViewerRowHeight(int index, float width)
    {
        if (!ViewerWrap) return DiffLineH;
        // The label fills its cell exactly — no padding — so the column width is the wrap
        // width. Measuring against anything wider gives a row too short for its own text,
        // and the list forces the height it is given, so the overflow is simply clipped.
        var style = TextMeasure.StyleFor(ViewerFontSize, TextFont);
        // Shared with the diff: the monospace fast path answers "certainly one line" with a
        // multiplication and falls back to real measurement for anything it cannot settle.
        float text = WrappedHeight(_viewerLines[index], style, width);
        return Math.Max(DiffLineH, text);   // a blank line still owns a row
    }

    private void ViewerScroll(float delta, float? to = null)
    {
        if (_viewerState != ViewerState.Ok || _viewerScrollRef.Current is not { } sv) return;
        sv.ScrollOffsetY = to ?? sv.ScrollOffsetY + delta;
    }

    /// <summary>A page is the viewport less one line, so the line you were reading stays on
    /// screen across a PgUp/PgDn.</summary>
    private float ViewerPage() => _viewerScrollRef.Current is { } sv
        ? Math.Max(DiffLineH, sv.Layout.AbsoluteContentBox.Height - DiffLineH)
        : DiffLineH;

    /// <summary>Tell the pan how much of a line the code column shows. Wrapped, nothing
    /// scrolls sideways, so the two modes cannot both be on.</summary>
    private void UpdateViewerPan() =>
        _viewerPan.ViewportWidth = ViewerWrap ? float.MaxValue : ViewerCodeWidth();

    /// <summary>Flips the wrap preference — the diff's, shared. Saving it here is what makes
    /// the two views agree the next time either is opened.</summary>
    private void ToggleViewerWrap()
    {
        _settings.DiffWrap = !_settings.DiffWrap;
        SettingsStore.Save(_settings);
        _viewerPan.Reset();   // the other mode's offset means nothing in this one
        MarkDirty();
    }

    private void HandleViewerKey(KeyDownEvent e)
    {
        // The find field, when it has the keyboard. Its own keys have to reach it — a letter
        // is a letter and Home is the start of the query — so the bar takes only the handful
        // it owns, and everything that moves the body is skipped entirely while typing.
        bool typing = e.Target is TextInputElement;

        if (HandleFindBarKey(e, _viewerFind, CloseViewer)) { e.StopPropagation = true; return; }
        // Nothing else claimed Esc, so it leaves the view.
        if (e.Key == KeyCode.Escape) { CloseViewer(); e.StopPropagation = true; return; }
        // A function key is never text, so it closes the view whether or not the field has
        // the keyboard.
        if (e.Key == KeyCode.F3) { CloseViewer(); e.StopPropagation = true; return; }

        if (typing) return;

        switch (e.Key)
        {
            // n / p walk the matches, the same pair the diff uses to walk its changes.
            case KeyCode.N when _viewerFind.Open: FindNav(_viewerFind, 1); e.StopPropagation = true; break;
            case KeyCode.P when _viewerFind.Open: FindNav(_viewerFind, -1); e.StopPropagation = true; break;
            // The body is read-only, so the arrows scroll it rather than moving a cursor.
            case KeyCode.Up: ViewerScroll(-DiffLineH); e.StopPropagation = true; break;
            case KeyCode.Down: ViewerScroll(DiffLineH); e.StopPropagation = true; break;
            case KeyCode.PageUp: ViewerScroll(-ViewerPage()); e.StopPropagation = true; break;
            case KeyCode.PageDown: ViewerScroll(ViewerPage()); e.StopPropagation = true; break;
            case KeyCode.Home: ViewerScroll(0, to: 0); e.StopPropagation = true; break;
            case KeyCode.End: ViewerScroll(0, to: float.MaxValue); e.StopPropagation = true; break;
            // Sideways, for lines wider than the column. Ctrl jumps to either margin.
            case KeyCode.Left or KeyCode.Right:
                _viewerPan.HandleKey(e); e.StopPropagation = true; break;
            case KeyCode.W: ToggleViewerWrap(); e.StopPropagation = true; break;
        }
    }

    // ── Rendering ────────────────────────────────────────────────────────

    private Element BuildViewer()
    {
        var col = Flex.Column(gap: 0).FlexGrow(1).Background(Nocturne.Bg);
        col.AddChild(BuildViewerHeader());
        if (_viewerFind.Open)
        {
            var bar = BuildFindBar(_viewerFind, "Find in file…");
            // Keyed so the bar reconciles as itself across rebuilds — a keyless element that
            // comes and goes takes the retained caret and focus of whatever was in its slot.
            bar.Key = "viewer-find";
            col.AddChild(bar);
        }
        col.AddChild(_viewerState switch
        {
            ViewerState.Ok => BuildViewerBody(),
            ViewerState.Loading => DiffMessage(Glyphs.TextContent, "Loading…",
                System.IO.Path.GetFileName(_viewerPath)),
            ViewerState.Binary => DiffMessage(Glyphs.BinaryFile, "Can't show this as text",
                "The file is binary or larger than 4 MB."),
            // Distinct from binary on purpose: the member may be perfectly ordinary text, and
            // saying otherwise sends people looking for a problem with the file itself.
            ViewerState.Unreadable => DiffMessage(Glyphs.Key, "Couldn't read this archive member",
                "It may need a password, or the archive may be damaged."),
            _ => DiffMessage(Glyphs.Prohibit, "File not found", _viewerPath),
        });
        if (_viewerState == ViewerState.Ok && _viewerPan.CanPan)
            col.AddChild(_viewerPan.Scrollbar(Nocturne.Surface, Nocturne.Neutral700, Nocturne.Divider));
        col.AddChild(BuildViewerFooter());
        return col;
    }

    private Element BuildViewerHeader()
    {
        var h = Flex.Row(gap: 10).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 14, 8, 14))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        var back = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(2, 8, 2, 8)).CornerRadius(5f)
            .Children(Ui.Icon(Glyphs.Back, 16f, Nocturne.Neutral300)).Tooltip("Back · Esc");
        back.OnClick(_ => CloseViewer());
        h.AddChild(back.HoverBg(Nocturne.Neutral900, Color.Transparent));

        h.AddChild(Ui.Icon(Glyphs.TextContent, 13f, Nocturne.Accent));
        h.AddChild(Text.New(System.IO.Path.GetFileName(_viewerPath)).FontSize(13f)
            .FontWeight(FontWeight.Medium).TextColor(Nocturne.Text).MaxLines(1).FlexShrink(0));
        // The path gives way before the name or the counts do.
        h.AddChild(Ui.Mono(_viewerPath, 10.5f, Nocturne.On(0.40))
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis).FlexGrow(1));
        if (_viewerState == ViewerState.Ok)
            h.AddChild(Ui.Mono($"{_viewerLines.Length:N0} lines", 10.5f, Nocturne.On(0.50)).FlexShrink(0));
        return h;
    }

    private Element BuildViewerBody()
    {
        UpdateViewerPan();

        // What a row looks like depends on three things the list cannot see: the wrap mode,
        // how far the view is panned sideways, and the column width the text wraps into. A
        // realized row is reused as-is across a patch, so when any of them moves the rows have
        // to be thrown away and rebuilt — and the heights remeasured, since two of the three
        // change how tall a row is.
        var shape = (ViewerWrap, _viewerPan.Offset, ViewerCodeWidth());
        if (_viewerTops.Length != _viewerLines.Length + 1 || _viewerRowShape != shape)
        {
            if (_viewerRowShape.Item1 != shape.Item1 || _viewerHeightsWidth != shape.Item3
                || _viewerTops.Length != _viewerLines.Length + 1)
                RebuildViewerHeights();
            if (_viewerScrollRef.Current is VirtualListElement<int> mounted)
                mounted.InvalidateRows();
            _viewerRowShape = shape;
        }

        var list = VirtualListElement<int>.New()
            .ItemHeight(DiffLineH)
            .RowHeights((_, row) => row + 1 < _viewerTops.Length ? _viewerTops[row + 1] - _viewerTops[row] : DiffLineH)
            .Items(_viewerItems)
            .RenderWith(BuildViewerRow)
            .FlexGrow(1)
            .Background(Nocturne.Bg);
        // Keyed by file so opening another one starts at the top instead of inheriting the
        // previous one's offset, and so toggling wrap reconciles rather than rebuilding.
        list.Key = $"view:{_viewerPath}|{_viewerGen}";
        list.WithRef(_viewerScrollRef);
        // Focusable so that closing the find bar has somewhere in the tree to put the
        // keyboard. Out of the Tab cycle: the view has one thing to focus, so traversal
        // would be a no-op that looks like a broken Tab.
        list.IsFocusable = true;
        list.TabIndex = -1;
        // A trackpad's sideways swipe arrives as DeltaX; a mouse reports Shift+wheel as
        // DeltaY. The list's own handler sees both first, but its content is exactly as wide
        // as its viewport, so it clamps them to nothing and leaves them to us.
        list.On<ScrollEvent>(ev => _viewerPan.HandleScroll(ev));
        return list;
    }

    /// <summary>One row of the virtualized body. Called only for rows near the viewport.</summary>
    private Element BuildViewerRow(int index)
    {
        string text = index >= 0 && index < _viewerLines.Length ? _viewerLines[index] : "";

        // The row track is auto, so wrapped it grows to whatever the line needs and the gutter
        // beside it shares that band. A minimum keeps a blank line a row tall.
        var row = Grid.Of($"{ViewerGutterW()}px 1fr", gap: 0).AlignItems(AlignItems.Stretch);
        if (ViewerWrap) row.MinHeight(DiffLineH); else row.Height(DiffLineH);

        // Wrapped, the number belongs beside the *first* of the line's rows rather than
        // floating in the middle of a five-row block. See DiffGutter for why this is
        // JustifyContent and not AlignItems: a Box lays out as a column.
        row.AddChild(Box.New().AlignItems(AlignItems.FlexEnd)
            .JustifyContent(ViewerWrap ? JustifyContent.FlexStart : JustifyContent.Center)
            .Padding(new Thickness(ViewerWrap ? 3 : 0, 6, 0, 0))
            .Children(Ui.Mono((index + 1).ToString(), 10.5f, Nocturne.Neutral700))
            .GridColumn(0));
        row.AddChild(ViewerTextCell(text, index).GridColumn(1));
        return row;
    }

    private Element ViewerTextCell(string text, int index)
    {
        // No padding, and OverflowX.Hidden so the panned text clips at the cell's edge rather
        // than spilling back over the gutter. Padding here would cost twice, as it did in the
        // diff: the rows would be measured against a width the text does not actually get, and
        // a press in the margin would land on the box rather than the label — so selecting a
        // line by its leading whitespace, the natural place to start a drag, would do nothing.
        var box = Box.New().JustifyContent(JustifyContent.FlexStart).OverflowX(Overflow.Hidden);

        // FlexGrow on the vertical axis — the main axis of the column box it sits in — so the
        // label is as tall as its row. Without it the label keeps its own text height and the
        // rest of the row belongs to the box, which is a strip that swallows presses.
        var mono = Ui.Code(text, ViewerFontSize, TextFont, Nocturne.On(0.78)).FlexGrow(1);
        if (!ViewerWrap) mono.MaxLines(1).TextOverflow(TextOverflow.Clip);
        // Syntax colouring with any search hits tinted over it — see ViewerLineSpans.
        if (ViewerLineSpans(index, text) is { Count: > 0 } spans) mono.Spans(spans);
        // One group for the whole file, so a drag runs down its lines. The window owns the
        // group drag and the Ctrl+C that copies it.
        mono.Selectable().SelectionGroup(_viewerGroup, index);

        box.AddChild((ViewerWrap ? mono : _viewerPan.Cell(mono)).FlexGrow(1));
        return box;
    }

    private Element BuildViewerFooter()
    {
        var footer = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(5, 12, 5, 12))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        footer.AddChild(Ui.Mono("read-only", 10f, Nocturne.On(0.40)));
        footer.AddChild(Box.New().FlexGrow(1));
        if (_viewerState == ViewerState.Ok)
        {
            footer.AddChild(Ui.Kbd("↑↓"));
            footer.AddChild(Ui.Kbd("PgUp"));
            footer.AddChild(Ui.Kbd("PgDn"));
            if (_viewerPan.CanPan) footer.AddChild(Ui.Kbd("←→"));
            footer.AddChild(Ui.Mono("scroll", 10f, Nocturne.On(0.45)));
            footer.AddChild(Ui.Kbd("w"));
            footer.AddChild(Ui.Mono(ViewerWrap ? "no wrap" : "wrap", 10f, Nocturne.On(0.45)));
            if (_viewerFind.Open)
            {
                footer.AddChild(Ui.Kbd("n")); footer.AddChild(Ui.Kbd("p"));
                footer.AddChild(Ui.Mono("match", 10f, Nocturne.On(0.45)));
            }
            else
            {
                footer.AddChild(Ui.Kbd("Ctrl+F"));
                footer.AddChild(Ui.Mono("find", 10f, Nocturne.On(0.45)));
            }
        }
        footer.AddChild(Ui.Kbd("Esc")); footer.AddChild(Ui.Mono("back", 10f, Nocturne.On(0.45)));
        return footer;
    }
}
