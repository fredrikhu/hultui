using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using VesperCommander.Theme;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── Find files (Alt+F7) ──────────────────────────────────────────────
    private bool _findOpen;
    private char _findSide;
    private string _findPattern = "";
    private string _findContains = "";
    private bool _findRecursive, _findMatchCase, _findRegex, _findHidden;
    private readonly List<FindEngine.Hit> _findResults = new();   // main-thread only
    private readonly System.Collections.Concurrent.ConcurrentQueue<FindEngine.Hit> _findIncoming = new();
    private long _lastFindRepaint;
    private int _findSel;
    private int _findScanned, _findApproxTotal, _findDenied;
    private bool _findSearching;
    private System.Threading.CancellationTokenSource? _findCts;
    private bool _pendingFindFocus;
    private readonly Ref<TextInputElement> _findPatternRef = new();
    private readonly Ref<VirtualListElement<FindEngine.Hit>> _findVList = new();

    private const int FindResultCap = 2000;

    private void OpenFind(char side)
    {
        if (AnyOverlayOpen) return;
        _findSide = side;
        _findOpen = true;
        _findPattern = ""; _findContains = "";
        _findRecursive = false;
        _findResults.Clear();
        _findSel = 0; _findScanned = 0; _findApproxTotal = 0; _findDenied = 0;
        _findSearching = false;
        _pendingFindFocus = true;
        MarkDirty();
    }

    private void CloseFind()
    {
        StopFind();
        _findOpen = false;
        PaneRef(_findSide).Current?.Focus();
        MarkDirty();
    }

    private void StopFind()
    {
        _findCts?.Cancel();
        _findCts = null;
        _findSearching = false;
    }

    /// <summary>(Re)start the search from the current inputs. Cancels any running search.
    /// The walk runs off-thread; hits queue lock-free and a throttled repaint drains them,
    /// so a big recursive search never floods the UI thread. Empty query ⇒ idle.</summary>
    private void RunFind()
    {
        StopFind();
        _findResults.Clear();
        _findIncoming.Clear();
        _findSel = 0; _findScanned = 0; _findApproxTotal = 0; _findDenied = 0;
        if (_findPattern.Length == 0 && _findContains.Length == 0) { MarkDirty(); return; }

        _findSearching = true;
        var cts = new System.Threading.CancellationTokenSource();
        _findCts = cts;
        var opts = new FindEngine.Options(
            _findPattern, _findContains.Length > 0 ? _findContains : null,
            _findRecursive, _findMatchCase, _findRegex, _findHidden);
        string root = Dir(_findSide);

        // Callbacks run on the walker thread. We do NOT marshal + MarkDirty per hit (that
        // rebuilds the whole result list every time and saturates the UI thread on a large
        // recursive search). Instead hits enqueue lock-free and FindRepaint coalesces the
        // UI refresh to ≤ ~12/s; DrainFindResults moves them onto the list during Build.
        var task = FindEngine.Run(root, opts,
            onHit: h => { if (!cts.IsCancellationRequested) { _findIncoming.Enqueue(h); FindRepaint(); } },
            onProgress: (s, t) => { if (!cts.IsCancellationRequested) { _findScanned = s; _findApproxTotal = t; FindRepaint(); } },
            onDenied: _ => { if (!cts.IsCancellationRequested) { System.Threading.Interlocked.Increment(ref _findDenied); FindRepaint(); } },
            cts.Token);
        task.ContinueWith(_ => Application.InvokeOnMainThread(() =>
        {
            if (_findCts == cts) { _findSearching = false; MarkDirty(); }   // final refresh drains the tail
        }));
    }

    /// <summary>Coalesced repaint request from the walker thread — at most one queued
    /// MarkDirty per ~80 ms, regardless of how fast hits arrive.</summary>
    private void FindRepaint()
    {
        var now = Environment.TickCount64;
        if (now - _lastFindRepaint < 80) return;
        _lastFindRepaint = now;
        Application.InvokeOnMainThread(MarkDirty);
    }

    /// <summary>Move queued hits onto the (main-thread-only) results list, honoring the cap.
    /// Called from Build so the list reflects the walk without per-hit marshalling.</summary>
    private void DrainFindResults()
    {
        while (_findResults.Count < FindResultCap && _findIncoming.TryDequeue(out var h))
            _findResults.Add(h);
    }

    private void JumpFindResult(char side)
    {
        if (_findSel < 0 || _findSel >= _findResults.Count) return;
        var h = _findResults[_findSel];
        _findOpen = false; StopFind();
        _active = side;
        // Land the cursor on the hit (or on the folder itself if the hit is a folder).
        NavigateTo(side, h.IsDir ? h.FullPath : h.ParentDir, focusChildName: h.IsDir ? null : h.Name);
        PaneRef(side).Current?.Focus();
    }

    private void HandleFindKey(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Escape:
                if (_findSearching) StopFind(); else CloseFind();
                e.StopPropagation = true; break;
            case KeyCode.Up:
                _findSel = Math.Max(0, _findSel - 1); MarkDirty(); e.StopPropagation = true; break;
            case KeyCode.Down:
                _findSel = Math.Min(Math.Max(0, _findResults.Count - 1), _findSel + 1); MarkDirty(); e.StopPropagation = true; break;
            case KeyCode.Enter:
                if (_findResults.Count > 0)
                {
                    // Ctrl+Enter opens the hit in the other pane.
                    char target = (e.Modifiers.HasFlag(KeyModifiers.Ctrl) || e.Modifiers.HasFlag(KeyModifiers.Super))
                        ? (_findSide == 'L' ? 'R' : 'L') : _findSide;
                    JumpFindResult(target);
                }
                else if (!_findRecursive)
                {
                    _findRecursive = true; RunFind();   // escalate a fruitless shallow search
                }
                e.StopPropagation = true; break;
            // Text keys fall through to the focused pattern field (OnTextChanged re-runs).
        }
    }

    // ── Find overlay ─────────────────────────────────────────────────────
    private Element BuildFind()
    {
        DrainFindResults();   // pull queued hits onto the list before rendering
        var card = Card(560, "Find files", Dir(_findSide));

        // Pattern + optional "containing text" inputs.
        card.AddChild(FindInputRow("⌕", _findPattern, "*.rs, name fragment, or /regex/…",
            _findPatternRef, v => { _findPattern = v; RunFind(); }));
        card.AddChild(FindInputRow("≡", _findContains, "Containing text (optional)…",
            null, v => { _findContains = v; RunFind(); }));

        // Scope + option toggles.
        var controls = Flex.Row(gap: 14).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 14, 10, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        controls.AddChild(Seg(
            ("This folder", !_findRecursive, () => { _findRecursive = false; RunFind(); }),
            ("Recursive", _findRecursive, () => { _findRecursive = true; RunFind(); })));
        controls.AddChild(FindCheck("Match case", _findMatchCase, () => { _findMatchCase = !_findMatchCase; RunFind(); }));
        controls.AddChild(FindCheck("Regex", _findRegex, () => { _findRegex = !_findRegex; RunFind(); }));
        controls.AddChild(FindCheck("Hidden", _findHidden, () => { _findHidden = !_findHidden; RunFind(); }));
        card.AddChild(controls);

        card.AddChild(BuildFindBody());
        card.AddChild(BuildFindFooter());
        return Backdrop(card, topAligned: true, topPad: 70f);
    }

    private Element FindInputRow(string glyph, string value, string placeholder,
        Ref<TextInputElement>? refBox, Action<string> onChange)
    {
        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 14, 8, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        row.AddChild(Ui.Icon(glyph, 14f, Nocturne.On(0.45)));
        // Explicit height: a flex-only TextInput collapses its content box and paints
        // neither text nor placeholder (known HultUI gotcha). 30 clears the 13.5px field
        // font's ascenders/descenders — 22 clipped them top and bottom.
        var input = TextInput.New(value).FlexGrow(1).Height(30);
        input.PlaceholderText(placeholder);
        input.OnTextChanged(v => onChange(v));
        if (refBox != null) input.WithRef(refBox);
        row.AddChild(input);
        return row;
    }

    private Element FindCheck(string label, bool on, Action toggle)
    {
        var row = Flex.Row(gap: 6).AlignItems(AlignItems.Center).Cursor(CursorType.Pointer);
        var box = Box.New().Width(14).Height(14).CornerRadius(3f)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .BorderColor(on ? Nocturne.Accent : Nocturne.Neutral600).BorderWidth(1.5f)
            .Background(on ? Nocturne.Accent : Color.Transparent);
        if (on) box.AddChild(Ui.Icon("✓", 9f, Nocturne.Accent100));
        row.AddChild(box);
        row.AddChild(Text.New(label).FontSize(11.5f).TextColor(Nocturne.Neutral300));
        row.OnClick(_ => toggle());
        return row;
    }

    private Element BuildFindBody()
    {
        // Idle: nothing typed yet.
        if (_findPattern.Length == 0 && _findContains.Length == 0 && !_findSearching)
            return Box.New().Padding(new Thickness(22, 14, 22, 14))
                .AlignItems(AlignItems.Center)
                .Children(Text.New("Type to filter this folder · ↵ searches recursively")
                    .FontSize(11.5f).TextColor(Nocturne.On(0.40)));

        // No matches (settled, nothing found).
        if (_findResults.Count == 0 && !_findSearching)
        {
            var empty = Flex.Column(gap: 10).Padding(new Thickness(22, 14, 22, 14)).AlignItems(AlignItems.Center);
            empty.AddChild(Text.New($"No matches for \"{_findPattern}\" here")
                .FontSize(12f).TextColor(Nocturne.On(0.55)));
            if (!_findRecursive)
            {
                var btn = Box.New().Cursor(CursorType.Pointer)
                    .Padding(new Thickness(5, 12, 5, 12)).CornerRadius(Nocturne.RadiusSm)
                    .BorderColor(Nocturne.Accent700).BorderWidth(1f);
                var lbl = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
                lbl.AddChild(Text.New("Search recursively").FontSize(11.5f).TextColor(Nocturne.Accent200));
                lbl.AddChild(Ui.Kbd("↵"));
                btn.AddChild(lbl);
                btn.OnClick(_ => { _findRecursive = true; RunFind(); });
                empty.AddChild(btn);
            }
            return empty;
        }

        // Results. Small sets render inline (grow-to-content); large sets virtualize so a
        // recursive search with thousands of hits only realizes the visible rows.
        const int InlineMax = 60;
        if (_findResults.Count <= InlineMax)
        {
            var scroll = ScrollView.Vertical().MaxHeight(360).Padding(new Thickness(6, 6, 6, 6));
            var col = Flex.Column(gap: 0);
            for (int i = 0; i < _findResults.Count; i++)
                col.AddChild(FindResultRow(_findResults[i], i == _findSel));
            scroll.AddChild(col);
            return scroll;
        }

        // Fresh snapshot each build so the VirtualList re-realizes visible rows against
        // current state (the reference-equality gotcha).
        return VirtualListElement<FindEngine.Hit>.New()
            .ItemHeight((int)RowHeight)
            .Items(_findResults.ToList())
            .RenderWith(h => FindResultRow(h, _findResults.IndexOf(h) == _findSel))
            .WithRef(_findVList)
            .Height(360)
            .Padding(new Thickness(6, 6, 6, 6));
    }

    private Element FindResultRow(FindEngine.Hit h, bool selected)
    {
        // Reserve the 1px border on every row (transparent when unselected) so selecting
        // one doesn't nudge its contents inward.
        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center).Height(RowHeight)
            .Padding(new Thickness(0, 10, 0, 10)).CornerRadius(Nocturne.RadiusSm)
            .BorderWidth(1f).BorderColor(selected ? Nocturne.Accent600 : Color.Transparent)
            .Cursor(CursorType.Pointer);
        if (selected)
            row.Background(Nocturne.Alpha(Nocturne.Accent500, 0.13));

        var fe = new FileEntry(h.Name, h.FullPath, h.IsDir, h.Size, h.Modified);
        row.AddChild(Icons.Draw(Icons.KindFor(fe)));

        var name = Flex.Row(gap: 6).AlignItems(AlignItems.Center).FlexGrow(1).Overflow(Overflow.Hidden);
        name.AddChild(Ui.Mono(h.Name, 12f, h.IsDir ? Nocturne.DirText : Nocturne.Text)
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        string parent = RelativeParent(h.ParentDir);
        if (parent.Length > 0)
            name.AddChild(Ui.Mono("· " + parent, 10.5f, Nocturne.On(0.42)).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        if (h.ContentHits > 0)
            name.AddChild(Ui.Mono($"· {h.ContentHits} hit{(h.ContentHits == 1 ? "" : "s")}", 10.5f, Nocturne.Accent300));
        row.AddChild(name);

        if (!h.IsDir)
            row.AddChild(Ui.Mono(FileSystem.ByteCount(h.Size), 10.5f, Nocturne.On(0.45)));

        int idx = _findResults.IndexOf(h);
        row.OnClick(_ => { _findSel = idx; JumpFindResult(_findSide); });
        return row;
    }

    /// <summary>Parent folder shown relative to the search root when possible (e.g. "src\vfs").</summary>
    private string RelativeParent(string parentDir)
    {
        string root = Dir(_findSide);
        if (PathEquals(parentDir, root)) return "";
        if (parentDir.StartsWith(root, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return parentDir[root.Length..].TrimStart(System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar);
        return parentDir;
    }

    private Element BuildFindFooter()
    {
        var footer = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 12, 6, 12))
            .Background(Nocturne.Surface);

        if (_findSearching)
        {
            footer.AddChild(Ui.Icon("◜", 12f, Nocturne.Accent));   // spinner stand-in
            footer.AddChild(Ui.Mono($"Searching… {_findScanned} of ~{_findApproxTotal} folders · {_findResults.Count} found",
                10.5f, Nocturne.On(0.55)));
            footer.AddChild(Box.New().FlexGrow(1));
            footer.AddChild(Ui.Kbd("Esc")); footer.AddChild(Ui.Mono("stop", 10.5f, Nocturne.On(0.45)));
        }
        else if (_findResults.Count > 0)
        {
            footer.AddChild(Ui.Mono($"{_findResults.Count} match{(_findResults.Count == 1 ? "" : "es")}"
                + (_findResults.Count >= FindResultCap ? " (capped)" : ""), 10.5f, Nocturne.On(0.55)));
            footer.AddChild(Box.New().FlexGrow(1));
            footer.AddChild(Ui.Kbd("↵")); footer.AddChild(Ui.Mono("jump", 10.5f, Nocturne.On(0.45)));
            footer.AddChild(Ui.Kbd("Ctrl+↵")); footer.AddChild(Ui.Mono("other pane", 10.5f, Nocturne.On(0.45)));
            footer.AddChild(Ui.Kbd("Esc"));
        }
        else
        {
            footer.AddChild(Ui.Kbd("Esc")); footer.AddChild(Ui.Mono("close", 10.5f, Nocturne.On(0.45)));
            footer.AddChild(Box.New().FlexGrow(1));
        }

        if (_findDenied > 0)
        {
            var banner = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
            banner.AddChild(Ui.Icon("⚠", 11f, Nocturne.MarkRed));
            banner.AddChild(Ui.Mono($"{_findDenied} folder{(_findDenied == 1 ? "" : "s")} skipped — access denied",
                10f, Nocturne.MarkRed));
            footer.AddChild(banner);
        }
        return footer;
    }
}
