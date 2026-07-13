using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Junction;

public partial class JunctionApp
{
    // ── Diff pane (header + body), shared by Commit / Changes / fullscreen ──

    private Element BuildDiffPane(string path, GitFileDiff? diff)
    {
        var col = Flex.Column(gap: 0).FlexGrow(1);

        var header = Flex.Row(gap: 10).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(8, 12, 8, 12))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Ui.Mono(path.Length > 0 ? path : "—", 12f, Nocturne.Text)
            .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        header.AddChild(DiffModeToggle());
        header.AddChild(Ui.IconButton(Icons.ArrowsOut, () => { _diffFull = true; MarkDirty(); }, "Fullscreen diff"));
        col.AddChild(header);

        col.AddChild(BuildDiffScroll(diff));
        return col;
    }

    private Element DiffModeToggle()
    {
        var seg = Flex.Row(gap: 2).AlignItems(AlignItems.Center)
            .Padding(new Thickness(2))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusRow);
        seg.AddChild(DiffModeButton("Unified", Icons.Rows, !_diffSplit, () => { _diffSplit = false; MarkDirty(); }));
        seg.AddChild(DiffModeButton("Split", Icons.Columns, _diffSplit, () => { _diffSplit = true; MarkDirty(); }));
        return seg;
    }

    private Element DiffModeButton(string label, string glyph, bool active, Action onClick)
    {
        var fg = active ? Nocturne.Accent : Nocturne.On(0.58);
        var b = Flex.Row(gap: 5).AlignItems(AlignItems.Center)
            .Padding(new Thickness(4, 9, 4, 9)).CornerRadius(5f).Cursor(CursorType.Pointer);
        if (active) b.Background(Nocturne.Bg).BorderColor(Nocturne.Accent).BorderWidth(1f);
        b.AddChild(Ui.Icon(glyph, 12f, fg));
        b.AddChild(Text.New(label).FontSize(11.5f).FontWeight(FontWeight.Medium).TextColor(fg));
        b.OnClick(e => { e.StopPropagation = true; onClick(); });
        return b;
    }

    private const int DiffRowHeight = 22;

    private enum DiffRowType : byte { Hunk, Unified, Split }

    private sealed record DiffRow
    {
        public DiffRowType Type { get; init; }
        public string Hunk { get; init; } = "";
        public GitDiffLine? Line { get; init; }   // unified
        public GitDiffLine? Left { get; init; }   // split
        public GitDiffLine? Right { get; init; }
    }

    // Virtualized so large diffs keep constant row height and stay cheap — a
    // plain ScrollView over a tall column compresses its rows to fit.
    private Element BuildDiffScroll(GitFileDiff? diff)
    {
        if (diff == null || diff.Hunks.Count == 0)
            return Box.New().FlexGrow(1)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New(diff == null ? "Select a file" : "No textual changes")
                    .FontSize(13f).TextColor(Nocturne.On(0.45)));

        var rows = _diffSplit ? FlattenSplit(diff) : FlattenUnified(diff);
        var list = VirtualListElement<DiffRow>.New()
            .ItemHeight(DiffRowHeight)
            .Items(rows)
            .RenderWith(RenderDiffRow)
            .FlexGrow(1)
            .Background(Nocturne.Bg);
        // Focusable so clicking the diff lets arrow / page / home / end scroll it.
        list.IsFocusable = true;
        list.TabIndex = 0;
        list.On<KeyDownEvent>(e => DiffScrollKey(e, list));
        return list;
    }

    private void DiffScrollKey(KeyDownEvent e, VirtualListElement<DiffRow> list)
    {
        if (e.Key == KeyCode.Escape && _diffFull)
        {
            _diffFull = false;
            MarkDirty();
            e.StopPropagation = true;
            return;
        }

        var page = Math.Max(DiffRowHeight, list.Layout.AbsoluteContentBox.Height - DiffRowHeight);
        var handled = true;
        switch (e.Key)
        {
            case KeyCode.Down: list.ScrollOffsetY += DiffRowHeight; break;
            case KeyCode.Up: list.ScrollOffsetY -= DiffRowHeight; break;
            case KeyCode.PageDown: list.ScrollOffsetY += page; break;
            case KeyCode.PageUp: list.ScrollOffsetY -= page; break;
            case KeyCode.Home: list.ScrollOffsetY = 0; break;
            case KeyCode.End: list.ScrollOffsetY = 1_000_000f; break;   // clamped to content
            default: handled = false; break;
        }
        if (handled) e.StopPropagation = true;
    }

    private static List<DiffRow> FlattenUnified(GitFileDiff diff)
    {
        var rows = new List<DiffRow>();
        foreach (var hunk in diff.Hunks)
        {
            rows.Add(new DiffRow { Type = DiffRowType.Hunk, Hunk = hunk.Header });
            foreach (var line in hunk.Lines.Where(l => l.Kind != DiffLineKind.HunkHeader))
                rows.Add(new DiffRow { Type = DiffRowType.Unified, Line = line });
        }
        return rows;
    }

    private static List<DiffRow> FlattenSplit(GitFileDiff diff)
    {
        var rows = new List<DiffRow>();
        foreach (var hunk in diff.Hunks)
        {
            rows.Add(new DiffRow { Type = DiffRowType.Hunk, Hunk = hunk.Header });
            var lines = hunk.Lines.Where(l => l.Kind != DiffLineKind.HunkHeader).ToList();
            foreach (var (left, right) in SplitPairs(lines))
                rows.Add(new DiffRow { Type = DiffRowType.Split, Left = left, Right = right });
        }
        return rows;
    }

    private static Element RenderDiffRow(DiffRow r) => r.Type switch
    {
        DiffRowType.Hunk => HunkHeaderRow(r.Hunk),
        DiffRowType.Split => SplitRow(r.Left, r.Right),
        _ => UnifiedRow(r.Line!),
    };

    private static Element HunkHeaderRow(string header) =>
        Box.New()
            .Padding(new Thickness(5, 14, 5, 14))
            .Background(Nocturne.Alpha(Nocturne.Accent, 0.06))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0))
            .Children(Ui.Mono(header, 11.5f, Nocturne.Accent300).MaxLines(1).OverflowMode(TextOverflow.Ellipsis));

    private static Element UnifiedRow(GitDiffLine line)
    {
        var (bg, code, sign, signColor) = line.Kind switch
        {
            DiffLineKind.Addition => (Nocturne.DiffAddBg, Nocturne.DiffAddText, "+", Nocturne.DiffAddSign),
            DiffLineKind.Deletion => (Nocturne.DiffDelBg, Nocturne.DiffDelText, "−", Nocturne.DiffDelSign),
            _ => (Color.Transparent, Nocturne.On(0.80), " ", Color.Transparent),
        };

        return Flex.Row(gap: 0).AlignItems(AlignItems.Center).Background(bg)
            .Children(
                NumCell(line.OldLineNumber, 46f),
                NumCell(line.NewLineNumber, 46f),
                Box.New().Width(16).JustifyContent(JustifyContent.Center)
                    .Children(Ui.Mono(sign, 12f, signColor)),
                Ui.Mono(line.Content.Length == 0 ? " " : line.Content, 12f, code)
                    .FlexGrow(1).Padding(new Thickness(2, 14, 2, 0))
                    .MaxLines(1).OverflowMode(TextOverflow.Ellipsis));
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

    private static Element SplitRow(GitDiffLine? left, GitDiffLine? right) =>
        Flex.Row(gap: 0).AlignItems(AlignItems.Stretch)
            .Children(
                SplitHalf(left, isLeft: true),
                SplitHalf(right, isLeft: false));

    private static Element SplitHalf(GitDiffLine? line, bool isLeft)
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

        var half = Flex.Row(gap: 0).AlignItems(AlignItems.Center).FlexGrow(1).FlexBasis(Dimension.Px(0))
            .Background(bg);
        if (isLeft) half.BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0));
        half.AddChild(NumCell(num, 44f));
        half.AddChild(Ui.Mono(text, 12f, code).FlexGrow(1).FlexShrink(1)
            .Padding(new Thickness(2, 10, 2, 0)).MaxLines(1).OverflowMode(TextOverflow.Ellipsis));
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
}
