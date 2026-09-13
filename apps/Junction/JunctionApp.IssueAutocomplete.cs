using System.Text.RegularExpressions;
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
/// Issues in the commit composer: typing <c>#</c> offers the project's open issues, picking
/// one inserts <c>#1042 </c>, and a closing keyword ahead of a reference gets one line
/// saying what will happen when the commit reaches the default branch. Only for a
/// GitLab-backed repository with an account; otherwise the composer is exactly as it was.
/// </summary>
public partial class JunctionApp
{
    private readonly KeyedAsyncLoader<string, Hosted<IReadOnlyList<GlIssue>>> _issueLoaders =
        new(Hosted<IReadOnlyList<GlIssue>>.Empty([]), capacity: 8, comparer: PathComparer);

    /// <summary>Which composer field the last keystroke was in, and where the caret was.</summary>
    private bool _issueInSummary = true;
    private int _issueCaret = -1;
    private int _issueSel;

    private static readonly Regex IssueTrigger = new(@"(^|\s)#(\w*)$", RegexOptions.Compiled);
    private static readonly Regex ClosingRef = new(
        @"\b(close[sd]?|closing|fix(?:e[sd])?|fixing|resolve[sd]?|resolving|implement(?:s|ed|ing)?)\s+#(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private Hosted<IReadOnlyList<GlIssue>> IssuesFor(string path)
    {
        if (GitLabRemoteFor(path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client)
            return Hosted<IReadOnlyList<GlIssue>>.Empty([]);
        var loader = _issueLoaders.For(path);
        var prev = loader.Value;
        loader.Load($"{path}#{HostedGen(path)}", () =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return prev;
            var issues = Sync(client.ListIssuesAsync(id.Value, new IssueQuery(State: "opened"), maxPages: 2));
            return issues.Ok
                ? new Hosted<IReadOnlyList<GlIssue>>(issues.Value, DateTimeOffset.UtcNow, null, true)
                : Keep(prev, issues.Failure!);
        });
        return loader.Value;
    }

    /// <summary>The text before the caret ends in a <c>#</c> token → (token start, query).</summary>
    private static (int Start, string Query)? IssueTokenAt(string text, int caret)
    {
        if (caret < 0 || caret > text.Length) caret = text.Length;
        var m = IssueTrigger.Match(text[..caret]);
        // The caret the field reports can lag the character just typed by a change; the
        // end of the text is where typing happens, so it is tried too.
        if (!m.Success && caret != text.Length) m = IssueTrigger.Match(text);
        if (!m.Success) return null;
        return (m.Groups[2].Index - 1, m.Groups[2].Value);
    }

    /// <summary>Records where typing happened so the suggestion list can read the token.</summary>
    private void NoteComposerTyping(bool summary, int caret)
    {
        _issueInSummary = summary;
        _issueCaret = caret;
        _issueSel = 0;
    }

    private List<GlIssue> IssueSuggestions(out (int Start, string Query) token)
    {
        token = default;
        if (!GitLabActive(_activeRepoPath)) return [];
        var text = _issueInSummary ? _commitSummary : _commitDescription;
        if (IssueTokenAt(text, _issueCaret) is not { } t) return [];
        token = t;
        var q = t.Query;
        var open = IssuesFor(_activeRepoPath).Value;
        return open
            .Where(i => q.Length == 0 || i.Iid.ToString().StartsWith(q, StringComparison.Ordinal)
                        || i.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Iid.ToString().StartsWith(q, StringComparison.Ordinal) ? 0 : 1)
            .ThenByDescending(i => i.UpdatedAt)
            .Take(5)
            .ToList();
    }

    /// <summary>The list above the composer, or null when there is nothing to offer.</summary>
    private Element? BuildIssueSuggestions()
    {
        var items = IssueSuggestions(out _);
        if (items.Count == 0) return null;
        if (_issueSel >= items.Count) _issueSel = items.Count - 1;
        var remote = GitLabRemoteFor(_activeRepoPath)!;

        var box = Flex.Column(gap: 0).Padding(new Thickness(4))
            .Background(Nocturne.Surface).BorderColor(Nocturne.Neutral700).BorderWidth(1f).CornerRadius(Nocturne.RadiusMd);
        box.AutomationId = "issue-suggestions";
        var head = Flex.Row(gap: 5).AlignItems(AlignItems.Center).Padding(new Thickness(4, 8, 5, 8));
        head.AddChild(Ui.Icon(Icons.GitLabLogo, 11f, Nocturne.On(0.42)));
        head.AddChild(Text.New($"OPEN ISSUES · {remote.ProjectPath}").FontSize(10f).TextColor(Nocturne.On(0.42)));
        box.AddChild(head);
        for (int i = 0; i < items.Count; i++)
        {
            var issue = items[i];
            bool on = i == _issueSel;
            var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
                .Padding(new Thickness(6, 8, 6, 8)).CornerRadius(6f).Cursor(CursorType.Pointer)
                .Background(on ? Nocturne.Neutral900 : Color.Transparent);
            row.AddChild(Ui.Mono($"#{issue.Iid}", 12f, Nocturne.Accent300).Width(44).FlexShrink(0));
            row.AddChild(Text.New(issue.Title).FontSize(12f).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
            row.Key = "issue-" + issue.Iid + (on ? "#s" : "");
            row.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
            row.AccessibleName = $"#{issue.Iid} {issue.Title}";
            row.On<ClickEvent>(e => { e.StopPropagation = true; InsertIssue(issue); });
            if (!on) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
            box.AddChild(row);
        }
        return box;
    }

    /// <summary>Replaces the token being typed with <c>#iid </c>, in whichever field it is in.</summary>
    private void InsertIssue(GlIssue issue)
    {
        var text = _issueInSummary ? _commitSummary : _commitDescription;
        if (IssueTokenAt(text, _issueCaret) is not { } t) return;
        var insert = $"#{issue.Iid} ";
        int caret = Math.Max(t.Start + 1 + t.Query.Length, Math.Min(Math.Max(_issueCaret, 0), text.Length));
        var updated = text[..t.Start] + insert + text[caret..];
        int newCaret = t.Start + insert.Length;
        if (_issueInSummary)
        {
            _commitSummary = updated;
            if (_summaryRef.Current is { } s) { s.Text = updated; s.CursorPosition = newCaret; }
        }
        else
        {
            _commitDescription = updated;
            if (_descRef.Current is { } d) { d.Text = updated; d.CursorPosition = newCaret; }
        }
        _issueCaret = -1;   // token consumed; the list closes
        MarkDirty();
    }

    /// <summary>↑ ↓ ↵ Esc while the list is open. True when the key was taken.</summary>
    private bool HandleIssueSuggestionKey(KeyDownEvent e)
    {
        var items = IssueSuggestions(out _);
        if (items.Count == 0) return false;
        switch (e.Key)
        {
            case KeyCode.Down: _issueSel = Math.Min(_issueSel + 1, items.Count - 1); MarkDirty(); return true;
            case KeyCode.Up: _issueSel = Math.Max(_issueSel - 1, 0); MarkDirty(); return true;
            case KeyCode.Enter when !e.Modifiers.HasFlag(KeyModifiers.Ctrl) && !e.Modifiers.HasFlag(KeyModifiers.Super):
                InsertIssue(items[Math.Clamp(_issueSel, 0, items.Count - 1)]); return true;
            case KeyCode.Escape: _issueCaret = -1; MarkDirty(); return true;
        }
        return false;
    }

    /// <summary>The line under the description: <i>Closes #1042 ("…") will close the issue
    /// when this reaches main.</i> Null when nothing in the message closes anything.</summary>
    private Element? BuildClosingHint()
    {
        if (!GitLabActive(_activeRepoPath)) return null;
        var m = ClosingRef.Match(_commitSummary);
        if (!m.Success) m = ClosingRef.Match(_commitDescription);
        if (!m.Success) return null;
        var iid = m.Groups[2].Value;
        var title = IssuesFor(_activeRepoPath).Value.FirstOrDefault(i => i.Iid.ToString() == iid)?.Title;
        var target = ProjectFor(_activeRepoPath).Value?.Project.DefaultBranch ?? "main";
        var text = title is null
            ? $"{m.Value} will close the issue when this reaches {target}."
            : $"{m.Value} (\"{title}\") will close the issue when this reaches {target}.";
        var row = Flex.Row(gap: 5).AlignItems(AlignItems.Center);
        row.AddChild(Ui.Icon(Icons.GitLabLogo, 12f, Nocturne.Accent300).FlexShrink(0));
        row.AddChild(Text.New(text).FontSize(10.5f).TextColor(Nocturne.Accent300).FlexShrink(1)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(2));
        row.AutomationId = "closing-hint";
        return row;
    }
}
