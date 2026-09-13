using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.GitLab;
using Junction.Theme;

namespace Junction;

/// <summary>
/// GitLab's todos — design §H: a bell in the title bar with the pending count, and a
/// palette-shaped list of them with a verb tile per row, <i>Done</i> per row and <i>Mark
/// all done</i> in the header. A row opens the merge request it points at, in the local
/// repository that hosts it when there is one and in the browser otherwise. Hidden at zero
/// and when no account is signed in.
/// </summary>
public partial class JunctionApp
{
    private bool _todosOpen;
    private int _todoSel;
    private int _todoGen;
    private string? _todoBusy;

    private readonly KeyedAsyncLoader<string, Hosted<IReadOnlyList<GlTodo>>> _todoLoaders =
        new(Hosted<IReadOnlyList<GlTodo>>.Empty([]), capacity: 8, comparer: StringComparer.OrdinalIgnoreCase);

    internal bool TodosOpen => _todosOpen;

    // ── Data ─────────────────────────────────────────────────────────────────────────

    private Hosted<IReadOnlyList<GlTodo>> TodosFor(string host)
    {
        if (_gitlab.ClientFor(host) is not { } client) return Hosted<IReadOnlyList<GlTodo>>.Empty([]);
        var loader = _todoLoaders.For(host);
        var prev = loader.Value;
        loader.Load($"{host}#{_todoGen}", () =>
        {
            var t = Sync(client.ListPendingTodosAsync());
            return t.Ok ? new Hosted<IReadOnlyList<GlTodo>>(t.Value, DateTimeOffset.UtcNow, null, true) : Keep(prev, t.Failure!);
        }, NoteAuthFailure<IReadOnlyList<GlTodo>>(host));
        return loader.Value;
    }

    /// <summary>Every account's pending todos, newest first.</summary>
    private List<(string Host, GlTodo Todo)> AllTodos()
    {
        var all = new List<(string, GlTodo)>();
        foreach (var account in _gitlab.All)
            foreach (var t in TodosFor(account.Host).Value)
                all.Add((account.Host, t));
        all.Sort((a, b) => Nullable.Compare(b.Item2.CreatedAt, a.Item2.CreatedAt));
        return all;
    }

    internal int TodoCount => AllTodos().Count;

    /// <summary>Reloads the todos — on open, and with every hosted refresh.</summary>
    private void RefreshTodos() => _todoGen++;

    // ── Bell ─────────────────────────────────────────────────────────────────────────

    /// <summary>The title bar's bell with its badge; nothing at all when there is nothing
    /// pending or nobody is signed in.</summary>
    private Element? TodosBell()
    {
        if (_gitlab.All.Count == 0) return null;
        int count = TodoCount;
        if (count == 0) return null;
        var bell = Flex.Row(gap: 5).Height(26).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 8, 0, 8))
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusMd).Cursor(CursorType.Pointer).FlexShrink(0)
            .Tooltip(count == 1 ? "1 thing to do on GitLab" : $"{count} things to do on GitLab");
        bell.AddChild(Ui.Icon(Icons.Bell, 13f, Nocturne.On(0.7)));
        var badge = Box.New().Padding(new Thickness(0, 5, 0, 5)).CornerRadius(999f).Background(Nocturne.Accent800)
            .Children(Ui.Mono(count.ToString(), 10f, Nocturne.Accent100));
        bell.AddChild(badge);
        bell.AutomationId = "todo-bell";
        bell.AccessibleName = $"To do: {count}";
        bell.OnClick(e => { e.StopPropagation = true; OpenTodos(); });
        bell.HoverBg(Nocturne.Alpha(Nocturne.Accent, 0.08), Nocturne.Bg);
        return bell;
    }

    // ── Panel ────────────────────────────────────────────────────────────────────────

    internal void OpenTodos()
    {
        _todosOpen = true;
        _todoSel = 0;
        _paletteOpen = false;
        _popover = null;
        RefreshTodos();
        Rerender();
    }

    private void CloseTodos()
    {
        _todosOpen = false;
        MarkDirty();
    }

    private Element BuildTodosPanel()
    {
        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(new Color(0, 0, 0, 140))
            .JustifyContent(JustifyContent.FlexStart).AlignItems(AlignItems.Center)
            .Padding(new Thickness(96, 0, 0, 0))
            .ZIndex(42);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; CloseTodos(); });

        var card = Flex.Column(gap: 0)
            .Width(600).MaxHeight(520)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg);
        card.On<ClickEvent>(e => e.StopPropagation = true);
        card.AutomationId = "todos";

        var todos = AllTodos();
        _todoSel = Math.Clamp(_todoSel, 0, Math.Max(0, todos.Count - 1));

        var header = Flex.Row(gap: 10).AlignItems(AlignItems.Center).Height(56).FlexShrink(0)
            .Padding(new Thickness(0, 16, 0, 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Box.New().Width(26).Height(26).CornerRadius(Nocturne.RadiusRow).Background(Nocturne.Accent800)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Ui.Icon(Icons.Bell, 14f, Nocturne.Accent100)));
        header.AddChild(Text.New($"To do · {todos.Count}").FontSize(14f).FontWeight(FontWeight.Medium).FlexGrow(1));
        if (_todoBusy is { } busy)
            header.AddChild(DisabledButton(busy, Icons.CircleNotch));
        else if (todos.Count > 0)
        {
            var all = Ui.GhostButton("Mark all done", Icons.Check, MarkAllTodosDone, 12f);
            all.AccessibleName = "Mark all done";
            header.AddChild(all);
        }
        var close = Ui.IconButton(Icons.X, CloseTodos, "Close (Esc)", 28f);
        close.AccessibleName = "Close todos";
        header.AddChild(close);
        card.AddChild(header);

        var list = ScrollView.New().FlexGrow(1).Padding(new Thickness(6));
        if (todos.Count == 0)
            list.AddChild(Box.New().Padding(new Thickness(24)).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New("Nothing to do — GitLab has no pending todos for you.").FontSize(13f).TextColor(Nocturne.On(0.45))));
        for (int i = 0; i < todos.Count; i++)
            list.AddChild(TodoRow(todos[i].Host, todos[i].Todo, i));
        card.AddChild(list);

        var footer = Flex.Row(gap: 16).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 14, 8, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        footer.AddChild(Hint("↑ ↓", "navigate"));
        footer.AddChild(Hint("↵", "open"));
        footer.AddChild(Hint("d", "done"));
        footer.AddChild(Hint("Esc", "close"));
        card.AddChild(footer);

        backdrop.AddChild(card);
        return backdrop;
    }

    private static (string Glyph, string Verb) TodoVerb(GlTodo t) => t.ActionName switch
    {
        "review_requested" => (Icons.Eyes, "review requested"),
        "mentioned" or "directly_addressed" => (Icons.At, "mentioned"),
        "build_failed" => (Icons.XCircle, "pipeline failed"),
        "assigned" => (Icons.UserPlus, "assigned"),
        "approval_required" => (Icons.SealCheck, "approval required"),
        "unmergeable" => (Icons.Prohibit, "unmergeable"),
        "marked" => (Icons.Bell, "marked"),
        var other => (Icons.Bell, other.Replace('_', ' ')),
    };

    private Element TodoRow(string host, GlTodo t, int index)
    {
        bool selected = index == _todoSel;
        var normalBg = selected ? Nocturne.Alpha(Nocturne.Accent, 0.14) : Color.Transparent;
        var row = Flex.Row(gap: 11).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 10, 8, 10)).CornerRadius(Nocturne.RadiusMd)
            .Background(normalBg).Cursor(CursorType.Pointer);
        row.Key = $"todo:{host}:{t.Id}" + (selected ? "#s" : "");
        row.AutomationId = "todo-row";

        var (glyph, verb) = TodoVerb(t);
        row.AddChild(Box.New().Width(28).Height(28).FlexShrink(0).CornerRadius(Nocturne.RadiusRow)
            .Background(Nocturne.Accent800).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Ui.Icon(glyph, 14f, Nocturne.Accent100)));

        var text = Flex.Column(gap: 2).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0));
        var top = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        top.AddChild(Text.New(verb.ToUpperInvariant()).FontSize(9.5f).LetterSpacing(0.6f).TextColor(Nocturne.Accent300));
        if (t.Target?.Iid is { } iid)
            top.AddChild(Ui.Mono($"{(t.TargetType == "Issue" ? "#" : "!")}{iid}", 10.5f, Nocturne.On(0.55)));
        text.AddChild(top);
        text.AddChild(Text.New(t.Target?.Title ?? t.Body ?? "").FontSize(13f).MaxLines(1).OverflowMode(TextOverflow.Ellipsis));
        var project = t.Project?.PathWithNamespace ?? t.Project?.Name ?? host;
        text.AddChild(Text.New($"{project} · {RelativeTime(t.CreatedAt)}").FontSize(11f).TextColor(Nocturne.On(0.45))
            .MaxLines(1).OverflowMode(TextOverflow.Ellipsis));
        row.AddChild(text);

        var done = Ui.GhostButton("Done", null, () => MarkTodoDone(host, t), 11.5f).FlexShrink(0);
        done.AccessibleName = $"Done !{t.Target?.Iid}";
        done.On<ClickEvent>(e => e.StopPropagation = true);
        row.AddChild(done);

        row.OnClick(_ => OpenTodoTarget(host, t));
        row.HoverBg(Nocturne.Neutral900, normalBg);
        return row;
    }

    /// <summary>Goes where the todo points: the request's detail in the local repository
    /// that hosts the project, or the browser when no repository here does.</summary>
    private void OpenTodoTarget(string host, GlTodo t)
    {
        CloseTodos();
        var projectPath = t.Project?.PathWithNamespace;
        string? repo = null;
        if (projectPath != null)
            foreach (var path in _settings.RepositoryPaths)
                if (GitLabRemoteFor(path) is { } r && string.Equals(r.Host, host, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
                { repo = path; break; }
        if (repo != null && t.TargetType == "MergeRequest" && t.Target?.Iid is { } iid)
        {
            if (!PathEquals(repo, _activeRepoPath)) SelectRepo(repo);
            OpenMergeRequest(repo, iid);
            return;
        }
        OpenUrl(t.TargetUrl ?? t.Target?.WebUrl);
    }

    private async void MarkTodoDone(string host, GlTodo t)
    {
        if (_todoBusy != null || _gitlab.ClientFor(host) is not { } client) return;
        _todoBusy = "Marking…";
        MarkDirty();
        var r = await Task.Run(() => Sync(client.MarkTodoDoneAsync(t.Id)));
        _todoBusy = null;
        if (!r.Ok) { Flash("Could not mark it done: " + r.Failure!.UserMessage(host)); MarkDirty(); return; }
        RefreshTodos();
        Rerender();
    }

    private async void MarkAllTodosDone()
    {
        if (_todoBusy != null) return;
        _todoBusy = "Marking all…";
        MarkDirty();
        string? failed = null;
        foreach (var account in _gitlab.All)
        {
            if (_gitlab.ClientFor(account.Host) is not { } client) continue;
            var r = await Task.Run(() => Sync(client.MarkAllTodosDoneAsync()));
            if (!r.Ok) failed = r.Failure!.UserMessage(account.Host);
        }
        _todoBusy = null;
        if (failed != null) Flash("Could not mark all done: " + failed);
        else Flash("All todos marked done");
        RefreshTodos();
        Rerender();
    }

    // ── Keys ─────────────────────────────────────────────────────────────────────────

    private bool HandleTodosKey(KeyDownEvent e)
    {
        var todos = AllTodos();
        switch (e.Key)
        {
            case KeyCode.Escape: CloseTodos(); return true;
            case KeyCode.Up: if (_todoSel > 0) { _todoSel--; MarkDirty(); } return true;
            case KeyCode.Down: if (_todoSel < todos.Count - 1) { _todoSel++; MarkDirty(); } return true;
            case KeyCode.Enter:
                if (_todoSel >= 0 && _todoSel < todos.Count) OpenTodoTarget(todos[_todoSel].Host, todos[_todoSel].Todo);
                return true;
            case KeyCode.D:
                if (_todoSel >= 0 && _todoSel < todos.Count) MarkTodoDone(todos[_todoSel].Host, todos[_todoSel].Todo);
                return true;
        }
        return false;
    }
}
