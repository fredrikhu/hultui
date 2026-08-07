using HultUI;
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
    // ── Multi-rename tool (Ctrl+M) — batch rename with a live old→new preview ──
    private bool _renameOpen;
    private char _renameSide;
    private List<(string Name, string FullPath)> _renameFiles = new();
    private string _renNameMask = "[N]", _renExtMask = "";
    private string _renFind = "", _renReplace = "";
    private bool _renMatchCase, _renRegex, _renTrimEnds, _renCollapse;
    private RenameEngine.CaseMode _renCase = RenameEngine.CaseMode.AsIs;
    private int _renCounterStart = 1, _renCounterStep = 1, _renCounterDigits = 3;
    private int _renActiveMask;                       // 0 = name, 1 = extension (for token insert)
    private string? _renRegexError;                   // set each build; reddens the Find field
    private RenameEngine.Preview? _renLastValid;      // held while the regex is malformed
    private List<(RenameEngine.Row Row, string Error)>? _renFailures;   // partial-failure mode
    private int _renSucceeded;                         // count applied so far (across retries)
    private readonly Ref<TextInputElement> _renNameRef = new();
    private readonly Ref<VirtualListElement<RenameEngine.Row>> _renVList = new();
    private bool _pendingRenameFocus;

    private RenameEngine.Options RenOptions() => new(
        _renNameMask, _renExtMask, _renFind, _renReplace, _renMatchCase, _renRegex,
        _renCase, _renTrimEnds, _renCollapse, _renCounterStart, _renCounterStep, _renCounterDigits);

    /// <summary>Open the tool on the marked set (or the cursor file), if any and writable.</summary>
    private void OpenRename()
    {
        if (AnyOverlayOpen) return;
        char side = _active;
        if (LockedBlock(side, "rename here") || InArchiveBlock(side)) return;
        var targets = OperationTargets(side).Where(e => !e.IsParent).Select(e => (e.Name, e.FullPath)).ToList();
        if (targets.Count == 0) return;
        _renameSide = side;
        _renameFiles = targets;
        _renNameMask = "[N]"; _renExtMask = "";
        _renFind = _renReplace = "";
        _renMatchCase = _renRegex = _renTrimEnds = _renCollapse = false;
        _renCase = RenameEngine.CaseMode.AsIs;
        _renCounterStart = 1; _renCounterStep = 1; _renCounterDigits = 3;
        _renActiveMask = 0; _renLastValid = null;
        _renFailures = null; _renSucceeded = 0;
        _renameOpen = true; _pendingRenameFocus = true;
        MarkDirty();
    }

    private void HandleRenameKey(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Escape: CloseOverlays(); e.StopPropagation = true; break;
            case KeyCode.Enter:
                if (_renFailures != null) RetryRename(); else CommitRename();
                e.StopPropagation = true; break;
        }
        // Other keys fall through to the focused mask / field.
    }

    // ── Build ────────────────────────────────────────────────────────────
    private Element BuildRename()
    {
        if (_renFailures != null) return BuildRenameFailure();
        var preview = RenameEngine.Build(_renameFiles, RenOptions());
        if (preview.RegexError == null) _renLastValid = preview;
        var shown = preview.RegexError != null ? (_renLastValid ?? preview) : preview;
        bool canRename = preview.RegexError == null && preview.Conflicts == 0 && preview.WillRename > 0;

        _renRegexError = preview.RegexError;
        var card = DialogCard(760);
        card.AddChild(RenameHeader());
        card.AddChild(RenameControls());
        card.AddChild(RenamePreview(shown));
        card.AddChild(RenameSummary(preview));
        card.AddChild(RenameFooter(preview, canRename));
        return Backdrop(card, topAligned: true, topPad: 46f);
    }

    private Element RenameHeader()
    {
        var h = Flex.Row(gap: 12).AlignItems(AlignItems.Center).Padding(new Thickness(10, 14, 10, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        h.AddChild(Ui.HeaderTile("✎"));
        h.AddChild(Text.New($"Multi-rename {_renameFiles.Count} file{(_renameFiles.Count == 1 ? "" : "s")}")
            .FontSize(15f).FontWeight(FontWeight.Medium).TextColor(Nocturne.Text).FlexShrink(0));
        var dir = System.IO.Path.GetDirectoryName(_renameFiles[0].FullPath) ?? "";
        h.AddChild(Ui.Mono(dir, 11f, Nocturne.On(0.55)).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).FlexGrow(1));
        h.AddChild(Ui.Kbd("Ctrl+M"));
        h.AddChild(Ui.IconButton("✕", CloseOverlays, "Close", 28f));
        return h;
    }

    // Controls region — masks + token palette, counter, find/replace, case & trim.
    private Element RenameControls()
    {
        var col = Flex.Column(gap: 10).Padding(new Thickness(14, 14, 12, 14));
        col.AddChild(MaskRow("Name mask", _renNameMask, 1.7f, v => { _renNameMask = v; MarkDirty(); }, _renNameRef, 0, "[N]"));
        col.AddChild(MaskRow("Extension mask", _renExtMask, 0.7f, v => { _renExtMask = v; MarkDirty(); }, null, 1, "[E] · keep"));
        col.AddChild(TokenPalette());
        col.AddChild(Box.New().Height(1).Background(Nocturne.Divider));
        col.AddChild(RenameCounter());
        col.AddChild(RenameFindReplace());
        col.AddChild(RenameCaseTrim());
        return col;
    }

    // §C — counter (dormant at 45% until a [C] appears in a mask).
    private Element RenameCounter()
    {
        bool active = RenameEngine.UsesCounter(RenOptions());
        var row = Flex.Row(gap: 14).AlignItems(AlignItems.Center);
        row.AddChild(Text.New("Counter").FontSize(12f).TextColor(Nocturne.On(0.55)).Width(96).FlexShrink(0));
        row.AddChild(LabeledStepper("start", _renCounterStart, v => _renCounterStart = Math.Max(0, v)));
        row.AddChild(LabeledStepper("step", _renCounterStep, v => { if (v != 0) _renCounterStep = v; }));
        row.AddChild(LabeledStepper("digits", _renCounterDigits, v => _renCounterDigits = Math.Clamp(v, 1, 8)));
        if (active)
        {
            string Fmt(int n) => n.ToString().PadLeft(Math.Max(1, _renCounterDigits), '0');
            row.AddChild(Ui.Mono($"{Fmt(_renCounterStart)} · {Fmt(_renCounterStart + _renCounterStep)} · {Fmt(_renCounterStart + 2 * _renCounterStep)} · …",
                11f, Nocturne.Accent200));
        }
        else
            row.AddChild(Ui.Mono("insert [C] into a mask to enable the counter", 11f, Nocturne.On(0.45)));
        if (!active) row.Opacity(0.45f);
        return row;
    }

    private Element LabeledStepper(string label, int value, Action<int> set)
    {
        var r = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        r.AddChild(Ui.Mono(label, 10.5f, Nocturne.Neutral500));
        r.AddChild(Stepper(value, "", () => { set(value - 1); MarkDirty(); }, () => { set(value + 1); MarkDirty(); }));
        return r;
    }

    // §D — find / replace + case-sensitivity + regex, with inline validity.
    private Element RenameFindReplace()
    {
        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
        row.AddChild(Text.New("Find").FontSize(12f).TextColor(Nocturne.On(0.55)).Width(96).FlexShrink(0));
        row.AddChild(FindField(_renFind, _renRegexError != null, v => { _renFind = v; MarkDirty(); }));
        row.AddChild(Text.New("Replace").FontSize(11f).TextColor(Nocturne.On(0.45)));
        row.AddChild(FindField(_renReplace, false, v => { _renReplace = v; MarkDirty(); }));
        row.AddChild(ToggleIcon("Aa", _renMatchCase, () => { _renMatchCase = !_renMatchCase; MarkDirty(); }));
        row.AddChild(ToggleIcon(".*", _renRegex, () => { _renRegex = !_renRegex; MarkDirty(); }));
        return row;
    }

    private Element FindField(string value, bool bad, Action<string> onChanged)
    {
        var box = Box.New().FlexGrow(1).Height(26).Padding(new Thickness(3, 8, 3, 8)).CornerRadius(4f)
            .Background(Nocturne.Bg).BorderColor(bad ? Nocturne.Alpha(Nocturne.MarkRed, 0.55) : Nocturne.Neutral700).BorderWidth(1f);
        var input = TextInput.New(value).FlexGrow(1).Height(20);
        input.OnTextChanged(onChanged);
        box.AddChild(input);
        return box;
    }

    private Element ToggleIcon(string glyph, bool on, Action toggle)
    {
        var b = Box.New().Width(26).Height(26).CornerRadius(5f).Cursor(CursorType.Pointer).FlexShrink(0)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .BorderColor(on ? Nocturne.Accent600 : Nocturne.Neutral700).BorderWidth(1f)
            .Children(Ui.Mono(glyph, 11f, on ? Nocturne.Accent200 : Nocturne.Neutral400));
        if (on) b.Background(Nocturne.Alpha(Nocturne.Accent500, 0.14));
        b.OnClick(_ => toggle());
        return on ? b : b.HoverBg(Nocturne.Neutral900, Color.Transparent);
    }

    // §E — case (name only) + trim.
    private Element RenameCaseTrim()
    {
        var col = Flex.Column(gap: 8);
        var caseRow = Flex.Row(gap: 10).AlignItems(AlignItems.Center);
        caseRow.AddChild(Text.New("Case").FontSize(12f).TextColor(Nocturne.On(0.55)).Width(96).FlexShrink(0));
        caseRow.AddChild(Seg(
            ("As-is", _renCase == RenameEngine.CaseMode.AsIs, () => SetRenCase(RenameEngine.CaseMode.AsIs)),
            ("UPPER", _renCase == RenameEngine.CaseMode.Upper, () => SetRenCase(RenameEngine.CaseMode.Upper)),
            ("lower", _renCase == RenameEngine.CaseMode.Lower, () => SetRenCase(RenameEngine.CaseMode.Lower)),
            ("Title", _renCase == RenameEngine.CaseMode.Title, () => SetRenCase(RenameEngine.CaseMode.Title)),
            ("First cap", _renCase == RenameEngine.CaseMode.FirstCap, () => SetRenCase(RenameEngine.CaseMode.FirstCap))));
        col.AddChild(caseRow);

        var trimRow = Flex.Row(gap: 20).AlignItems(AlignItems.Center);
        trimRow.AddChild(Box.New().Width(96).FlexShrink(0));
        trimRow.AddChild(CheckItem("Trim leading/trailing spaces", _renTrimEnds, () => { _renTrimEnds = !_renTrimEnds; MarkDirty(); }));
        trimRow.AddChild(CheckItem("Collapse double spaces", _renCollapse, () => { _renCollapse = !_renCollapse; MarkDirty(); }));
        col.AddChild(trimRow);
        return col;
    }

    private void SetRenCase(RenameEngine.CaseMode m) { _renCase = m; MarkDirty(); }

    private Element MaskRow(string label, string value, float grow, Action<string> onChanged,
        Ref<TextInputElement>? refEl, int maskIndex, string placeholder)
    {
        var row = Flex.Row(gap: 10).AlignItems(AlignItems.Center);
        row.AddChild(Text.New(label).FontSize(12f).TextColor(Nocturne.On(0.55)).Width(96).FlexShrink(0));
        var box = Box.New().FlexGrow(grow).Height(30).Padding(new Thickness(4, 8, 4, 8)).CornerRadius(4f)
            .Background(Nocturne.Bg).BorderColor(Nocturne.Neutral700).BorderWidth(1f);
        var input = TextInput.New(value).FlexGrow(1).Height(22);
        input.PlaceholderText(placeholder);
        input.OnTextChanged(onChanged);
        input.Spans(MaskSpans(value));                 // paint-only: tokens Accent200
        input.On<FocusEvent>(_ => _renActiveMask = maskIndex);
        if (refEl != null) input.WithRef(refEl);
        box.AddChild(input);
        row.AddChild(box);
        return row;
    }

    /// <summary>Colour the [tokens] in a mask Accent200 (paint-only). Escaped [[ / ]] and an
    /// unclosed [ stay literal.</summary>
    private static IReadOnlyList<TextSpan>? MaskSpans(string mask)
    {
        List<TextSpan>? spans = null;
        int i = 0;
        while (i < mask.Length)
        {
            if (mask[i] == '[')
            {
                if (i + 1 < mask.Length && mask[i + 1] == '[') { i += 2; continue; }   // escape
                int close = mask.IndexOf(']', i + 1);
                if (close < 0) break;
                (spans ??= new()).Add(new TextSpan(i, close - i + 1, Nocturne.Accent200));
                i = close + 1;
            }
            else i++;
        }
        return spans;
    }

    private Element TokenPalette()
    {
        var row = Flex.Row(gap: 6).AlignItems(AlignItems.Center).FlexWrap(FlexWrap.Wrap);
        row.AddChild(Text.New("Insert").FontSize(11f).TextColor(Nocturne.Neutral500).Width(96).FlexShrink(0));
        foreach (var (tok, hint) in new[] { ("[N]", "name"), ("[N2-5]", "range"), ("[E]", "ext"), ("[C]", "counter"), ("[P]", "parent") })
            row.AddChild(TokenChip(tok, hint));
        return row;
    }

    private Element TokenChip(string token, string hint)
    {
        // A live example from the first file makes the token concrete.
        var (stem, ext) = (_renameFiles.Count > 0 ? _renameFiles[0].Name : "name.ext") is var fn
            ? (System.IO.Path.GetFileNameWithoutExtension(fn), System.IO.Path.GetExtension(fn).TrimStart('.')) : ("", "");
        string parent = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(_renameFiles[0].FullPath) ?? "").Name;
        string example = RenameEngine.Expand(token, stem, ext, parent, _renCounterStart, _renCounterDigits);

        var chip = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(2, 8, 3, 8)).CornerRadius(4f)
            .Background(Nocturne.Alpha(Nocturne.Accent500, 0.07)).BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .Tooltip($"{token} — {hint} · e.g. {example}")
            .Children(Ui.Mono(token, 10.5f, Nocturne.Accent200));
        chip.OnClick(_ =>
        {
            if (_renActiveMask == 1) _renExtMask += token; else _renNameMask += token;
            MarkDirty();
        });
        return chip.HoverBg(Nocturne.Alpha(Nocturne.Accent500, 0.14), Nocturne.Alpha(Nocturne.Accent500, 0.07));
    }

    // ── Preview table ────────────────────────────────────────────────────
    private Element RenamePreview(RenameEngine.Preview p)
    {
        var vl = VirtualListElement<RenameEngine.Row>.New();
        vl.Height(300).Background(Nocturne.Bg)
          .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        vl.ItemHeight(23).Items(p.Rows).RenderWith(RenameRow).WithRef(_renVList);   // fresh list → re-realizes
        return vl;
    }

    private Element RenameRow(RenameEngine.Row r)
    {
        bool conflict = r.IsConflict;
        bool unchanged = r.Status == RenameEngine.Status.Unchanged;
        var grid = Grid.Of("1fr 16px 1.3fr auto", gap: 8).Height(23).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 12, 0, 12));
        if (conflict) grid.Background(Nocturne.Alpha(Nocturne.MarkRed, 0.06));
        var el = grid;
        el.AddChild(Ui.Mono(r.OldName, 11.5f, Nocturne.On(0.45)).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).GridColumn(0));
        el.AddChild(Ui.Icon("→", 11f, Nocturne.Neutral600).GridColumn(1));
        Color newFg = conflict ? Nocturne.MarkRed : unchanged ? Nocturne.On(0.40) : Nocturne.Text;
        string newText = r.Status == RenameEngine.Status.Empty ? "(empty)" : r.NewName;
        var newEl = Ui.Mono(newText, 11.5f, newFg).MaxLines(1).TextOverflow(TextOverflow.Ellipsis);
        // For a clean partial change, tint just the changed run (common prefix/suffix diff);
        // a mostly-different name (>70%) renders plain to avoid noise at N rows.
        if (r.Status == RenameEngine.Status.Ok)
        {
            var (pre, suf) = DiffEngine.IntraSpan(r.OldName, r.NewName);
            int changed = r.NewName.Length - pre - suf;
            if (changed > 0 && changed <= 0.7 * r.NewName.Length)
                newEl.Spans([new TextSpan(pre, changed, Nocturne.Accent200)]);
        }
        el.AddChild(newEl.GridColumn(2));
        var reason = ReasonText(r.Status);
        if (reason.Length > 0)
        {
            var rr = Flex.Row(gap: 4).AlignItems(AlignItems.Center).GridColumn(3);
            if (conflict) rr.AddChild(Ui.Icon("⚠", 10f, Nocturne.MarkRed));
            rr.AddChild(Ui.Mono(reason, 10f, conflict ? Nocturne.MarkRed : Nocturne.Neutral600));
            el.AddChild(rr);
        }
        return el;
    }

    private static string ReasonText(RenameEngine.Status s) => s switch
    {
        RenameEngine.Status.Collision => "duplicate result",
        RenameEngine.Status.Exists => "already exists on disk",
        RenameEngine.Status.Illegal => "illegal character",
        RenameEngine.Status.Empty => "empty name",
        RenameEngine.Status.TooLong => "too long",
        RenameEngine.Status.Unchanged => "unchanged · skipped",
        _ => "",
    };

    private Element RenameSummary(RenameEngine.Preview p)
    {
        var bar = Flex.Row(gap: 6).AlignItems(AlignItems.Center).Padding(new Thickness(6, 14, 6, 14))
            .Background(Nocturne.Surface);
        if (p.RegexError != null)
        {
            bar.AddChild(Ui.Icon("⚠", 11f, Nocturne.MarkRed));
            bar.AddChild(Ui.Mono(p.RegexError, 11f, Nocturne.MarkRed));
            return bar;
        }
        bar.AddChild(Ui.Mono($"{p.WillRename} will rename", 11f, Nocturne.DiffGreen));
        bar.AddChild(Ui.Mono(" · ", 11f, Nocturne.On(0.35)));
        bar.AddChild(Ui.Mono($"{p.Conflicts} conflict{(p.Conflicts == 1 ? "" : "s")}", 11f, p.Conflicts > 0 ? Nocturne.MarkRed : Nocturne.On(0.50)));
        bar.AddChild(Ui.Mono(" · ", 11f, Nocturne.On(0.35)));
        bar.AddChild(Ui.Mono($"{p.Unchanged} unchanged", 11f, Nocturne.On(0.50)));
        // A pointed hint when nothing will rename.
        string? hint = p.WillRename > 0 ? null
            : p.Conflicts > 0 && p.Rows.All(r => r.Status is RenameEngine.Status.Collision or RenameEngine.Status.Ok)
                ? "add [C] or [N] to make results unique"
            : p.Conflicts == 0 ? "nothing to rename — edit the mask, find/replace or case"
            : null;
        if (hint != null)
        {
            bar.AddChild(Box.New().FlexGrow(1));
            bar.AddChild(Ui.Mono(hint, 10.5f, Nocturne.On(0.45)));
        }
        return bar;
    }

    private Element RenameFooter(RenameEngine.Preview p, bool canRename)
    {
        var footer = DialogFooter();
        footer.AddChild(GhostButton("Reset", ResetRename));
        // The reason a disabled Rename can't fire — or, for a single file, the faster path.
        string? why = p.RegexError != null ? "fix the pattern to rename"
            : p.Conflicts > 0 ? $"resolve {p.Conflicts} conflict{(p.Conflicts == 1 ? "" : "s")} to rename"
            : p.WillRename == 0 ? "nothing to rename"
            : _renameFiles.Count == 1 ? "just one file? Shift+F6 renames inline"
            : null;
        if (why != null) footer.AddChild(Ui.Mono(why, 10.5f, Nocturne.On(0.45)));
        footer.AddChild(Box.New().FlexGrow(1));
        footer.AddChild(SecondaryButton("Cancel", CloseOverlays));
        footer.AddChild(RenamePrimary(p.WillRename, canRename));
        return footer;
    }

    private Element RenamePrimary(int n, bool enabled)
    {
        var b = Box.New().Padding(new Thickness(5, 12, 5, 12)).CornerRadius(Nocturne.RadiusSm)
            .BorderColor(enabled ? Nocturne.Accent : Nocturne.Neutral700).BorderWidth(1f)
            .Children(Text.New($"Rename {n} file{(n == 1 ? "" : "s")}").FontSize(12f)
                .TextColor(enabled ? Nocturne.Accent200 : Nocturne.Neutral600));
        if (enabled)
        {
            b.Cursor(CursorType.Pointer).OnClick(_ => CommitRename());
            return b.HoverBg(Nocturne.Alpha(Nocturne.Accent500, 0.10), Color.Transparent);
        }
        return b.Opacity(0.45f);
    }

    private void ResetRename()
    {
        _renNameMask = "[N]"; _renExtMask = "";
        _renFind = _renReplace = "";
        _renMatchCase = _renRegex = _renTrimEnds = _renCollapse = false;
        _renCase = RenameEngine.CaseMode.AsIs;
        _renCounterStart = 1; _renCounterStep = 1; _renCounterDigits = 3;
        _renLastValid = null;
        MarkDirty();
    }

    // ── Commit ───────────────────────────────────────────────────────────
    private void CommitRename()
    {
        var p = RenameEngine.Build(_renameFiles, RenOptions());
        if (p.RegexError != null || p.Conflicts > 0 || p.WillRename == 0) return;   // gated
        ApplyAndSettle(p.Rows);
    }

    private void RetryRename()
    {
        if (_renFailures is not { } fails) return;
        ApplyAndSettle(fails.Select(f => f.Row).ToList());
    }

    /// <summary>Run a set of Ok rows through the engine. Full success closes with a status
    /// line; a partial failure keeps the dialog open in failure mode (banner + failed rows +
    /// Retry). Completed renames are never rolled back.</summary>
    private void ApplyAndSettle(IReadOnlyList<RenameEngine.Row> rows)
    {
        var results = RenameEngine.Apply(rows);
        _renSucceeded += results.Count(r => r.Error == null);
        BumpGen(_renameSide);   // refresh the pane

        var failed = results.Where(r => r.Error != null).Select(r => (r.Row, r.Error!)).ToList();
        if (failed.Count == 0)
        {
            _notice = $"Renamed {_renSucceeded} file{(_renSucceeded == 1 ? "" : "s")}";
            _renFailures = null; _renameOpen = false;
        }
        else _renFailures = failed;   // stay open, show what failed
        MarkDirty();
    }

    // ── Partial-failure view ─────────────────────────────────────────────
    private Element BuildRenameFailure()
    {
        var fails = _renFailures!;
        var card = DialogCard(760);
        card.AddChild(RenameHeader());

        var banner = Flex.Row(gap: 8).AlignItems(AlignItems.Center).Padding(new Thickness(8, 14, 8, 14))
            .Background(Nocturne.Alpha(Nocturne.MarkRed, 0.07))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        banner.AddChild(Ui.Icon("⚠", 12f, Nocturne.MarkRed));
        banner.AddChild(Ui.Mono($"{_renSucceeded} renamed · {fails.Count} failed", 12f, Nocturne.Text));
        banner.AddChild(Ui.Mono("— renamed files keep their new names", 11f, Nocturne.On(0.45)));
        card.AddChild(banner);

        var list = ScrollView.Vertical().MaxHeight(280);
        var col = Flex.Column(gap: 0);
        foreach (var (row, err) in fails)
        {
            var r = Grid.Of("1fr 16px 1.2fr 1.2fr", gap: 8).Height(24).AlignItems(AlignItems.Center)
                .Padding(new Thickness(0, 14, 0, 14));
            r.AddChild(Ui.Mono(row.OldName, 11.5f, Nocturne.On(0.55)).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).GridColumn(0));
            r.AddChild(Ui.Icon("→", 11f, Nocturne.Neutral600).GridColumn(1));
            r.AddChild(Ui.Mono(row.NewName, 11.5f, Nocturne.Text).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).GridColumn(2));
            r.AddChild(Ui.Mono(err, 10f, Nocturne.MarkRed).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).GridColumn(3));
            col.AddChild(r);
        }
        list.AddChild(col);
        card.AddChild(Box.New().Padding(new Thickness(8, 14, 8, 14)).Children(list));

        var footer = DialogFooter();
        footer.AddChild(Ui.Mono($"showing {fails.Count} failed", 10.5f, Nocturne.On(0.45)));
        footer.AddChild(Box.New().FlexGrow(1));
        footer.AddChild(SecondaryButton("Close", CloseOverlays));
        var retry = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(5, 12, 5, 12)).CornerRadius(Nocturne.RadiusSm)
            .BorderColor(Nocturne.Accent).BorderWidth(1f)
            .Children(Text.New($"Retry {fails.Count} failed").FontSize(12f).TextColor(Nocturne.Accent200));
        retry.OnClick(_ => RetryRename());
        footer.AddChild(retry.HoverBg(Nocturne.Alpha(Nocturne.Accent500, 0.10), Color.Transparent));
        card.AddChild(footer);
        return Backdrop(card, topAligned: true, topPad: 46f);
    }
}
