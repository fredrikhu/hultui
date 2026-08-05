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
    private RenameEngine.Preview? _renLastValid;      // held while the regex is malformed
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
        _renameOpen = true; _pendingRenameFocus = true;
        MarkDirty();
    }

    private void HandleRenameKey(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Escape: CloseOverlays(); e.StopPropagation = true; break;
            case KeyCode.Enter: CommitRename(); e.StopPropagation = true; break;
        }
        // Other keys fall through to the focused mask / field.
    }

    // ── Build ────────────────────────────────────────────────────────────
    private Element BuildRename()
    {
        var preview = RenameEngine.Build(_renameFiles, RenOptions());
        if (preview.RegexError == null) _renLastValid = preview;
        var shown = preview.RegexError != null ? (_renLastValid ?? preview) : preview;
        bool canRename = preview.RegexError == null && preview.Conflicts == 0 && preview.WillRename > 0;

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
        var h = Flex.Row(gap: 8).AlignItems(AlignItems.Center).Padding(new Thickness(10, 14, 10, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        h.AddChild(Ui.Icon("✎", 14f, Nocturne.Accent400));
        h.AddChild(Text.New($"Multi-rename {_renameFiles.Count} file{(_renameFiles.Count == 1 ? "" : "s")}")
            .FontSize(13f).FontWeight(FontWeight.Medium).TextColor(Nocturne.Text));
        var dir = System.IO.Path.GetDirectoryName(_renameFiles[0].FullPath) ?? "";
        h.AddChild(Ui.Mono(dir, 11f, Nocturne.On(0.55)).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).FlexGrow(1));
        h.AddChild(Ui.Kbd("Ctrl+M"));
        var close = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(1, 5, 1, 5)).CornerRadius(4f)
            .Children(Ui.Icon("✕", 12f, Nocturne.Neutral600));
        close.OnClick(_ => CloseOverlays());
        h.AddChild(close.HoverBg(Nocturne.Neutral900, Color.Transparent));
        return h;
    }

    // Controls region — masks + token palette (counter / find-replace / case land in P2).
    private Element RenameControls()
    {
        var col = Flex.Column(gap: 10).Padding(new Thickness(14, 16, 12, 16));
        col.AddChild(MaskRow("Name mask", _renNameMask, 1.7f, v => { _renNameMask = v; MarkDirty(); }, _renNameRef, 0, "[N]"));
        col.AddChild(MaskRow("Extension mask", _renExtMask, 0.7f, v => { _renExtMask = v; MarkDirty(); }, null, 1, "[E] · keep"));
        col.AddChild(TokenPalette());
        return col;
    }

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
        el.AddChild(Ui.Mono(newText, 11.5f, newFg).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).GridColumn(2));
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
        return bar;
    }

    private Element RenameFooter(RenameEngine.Preview p, bool canRename)
    {
        var footer = DialogFooter();
        footer.AddChild(GhostButton("Reset", ResetRename));
        // The reason a disabled Rename can't fire.
        string? why = p.RegexError != null ? "fix the pattern to rename"
            : p.Conflicts > 0 ? $"resolve {p.Conflicts} conflict{(p.Conflicts == 1 ? "" : "s")} to rename"
            : p.WillRename == 0 ? "nothing to rename"
            : null;
        if (why != null) footer.AddChild(Ui.Mono(why, 10.5f, Nocturne.On(0.45)));
        footer.AddChild(Box.New().FlexGrow(1));
        footer.AddChild(GhostButton("Cancel", CloseOverlays));
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
        RenameEngine.Apply(p.Rows);   // partial-failure handling arrives in P4
        _renameOpen = false;
        BumpGen(_renameSide);          // refresh the pane
        MarkDirty();
    }
}
