using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Find-in-text inside the diff pane (Ctrl+F).
///
/// <para>The search model is HultUI's <see cref="TextSearch"/>, tested on its own; what is
/// asserted here is the wiring — that the pane's rows are what gets searched, that split mode
/// searches both columns and unified searches the one, and that the bar's keys reach it while
/// its field holds the keyboard.</para>
/// </summary>
[Collection("JunctionDiff")]
public class DiffFindTests
{
    [Fact]
    public void CtrlFOpensTheBarAndFocusesIt()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Press(session, KeyCode.F, KeyModifiers.Ctrl);

            Assert.True(Open(app));
            Assert.IsType<TextInputElement>(session.Window.Window.FocusedElement);
        });
    }

    /// <summary>Ctrl+F on a bar that is already open is "search for something else": the
    /// query is selected whole, so typing replaces it instead of tacking onto its end.</summary>
    [Fact]
    public void CtrlFOnAnOpenBarSelectsTheQuery()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Find(session, app, "EDIT");

            Press(session, KeyCode.F, KeyModifiers.Ctrl);

            var field = Assert.IsType<TextInputElement>(session.Window.Window.FocusedElement);
            Assert.Equal("EDIT", field.SelectedText);

            Find(session, app, "one");
            Assert.Equal("one", Search(app).Query);
        }, content: "one\ntwo\nthree\n");
    }

    /// <summary>
    /// Ctrl+F with text selected in the diff searches for it: the query is the selection's
    /// first line — a drag across rows contributes the row it started on — and the current
    /// match is the selection itself rather than the first occurrence in the file.
    /// </summary>
    [Fact]
    public void CtrlFSeedsTheQueryFromTheSelectedText()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            // Locate the "two" row through the search, then put the bar away again.
            Find(session, app, "two");
            var row = Search(app).Current!.Value.Line;
            Press(session, KeyCode.Escape);
            Assert.False(Open(app));

            var group = (TextSelectionGroup)typeof(JunctionApp)
                .GetField("_diffGroupUnified", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
            group.Anchor(row, 0);
            group.ExtendTo(row + 1, 2);   // "two\nth"
            Assert.Equal("two\nth", group.SelectedText);

            Press(session, KeyCode.F, KeyModifiers.Ctrl);

            Assert.True(Open(app));
            Assert.Equal("two", Search(app).Query);
            Assert.Equal(row, Search(app).Current!.Value.Line);
            // Selected whole in the field, the same as a query left from before: typing
            // replaces it.
            var field = Assert.IsType<TextInputElement>(session.Window.Window.FocusedElement);
            Assert.Equal("two", field.Text);
            Assert.Equal("two", field.SelectedText);
        }, content: "one\ntwo\nthree\n");
    }

    /// <summary>The seed lands on the selection, not the first match: with "two" on two
    /// rows, selecting the lower one opens the bar on that one.</summary>
    [Fact]
    public void TheSeededMatchIsTheSelectionNotTheFirstOccurrence()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Find(session, app, "two");
            Assert.Equal(2, Search(app).Hits.Count);
            var lower = Search(app).Hits[1].Line;
            Press(session, KeyCode.Escape);

            var group = (TextSelectionGroup)typeof(JunctionApp)
                .GetField("_diffGroupUnified", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
            group.Anchor(lower, 0);
            group.ExtendTo(lower, 3);

            Press(session, KeyCode.F, KeyModifiers.Ctrl);

            Assert.Equal("two", Search(app).Query);
            Assert.Equal(1, Search(app).Index);
            Assert.Equal(lower, Search(app).Current!.Value.Line);
        }, content: "two\ntwo\nthree\n");   // all three within the hunk's context lines
    }

    [Fact]
    public void TypingFindsMatchesInTheDiff()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Find(session, app, "EDIT");

            Assert.NotEmpty(Search(app).Hits);
            Assert.All(Search(app).Hits, h => Assert.Equal(4, h.Length));
        }, content: "one\ntwo\nthree\n");
    }

    /// <summary>
    /// Unified is one column, split is two. The lane count has to follow the layout, or a
    /// search in split mode would only ever look down the left-hand side.
    /// </summary>
    [Fact]
    public void SplitModeSearchesBothColumns()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            // Unified first: every match is on the single lane.
            Find(session, app, "EDIT");
            Assert.All(Search(app).Hits, h => Assert.Equal(0, h.Lane));

            SetSplit(app, true);
            session.PumpFrames(3);

            Assert.NotEmpty(Search(app).Hits);
            Assert.Contains(Search(app).Hits, h => h.Lane == 1);
        }, content: "one\ntwo\nthree\n");
    }

    [Fact]
    public void EnterWalksTheMatchesAndWraps()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Find(session, app, "line");
            int count = Search(app).Hits.Count;
            Assert.True(count >= 3, $"expected several matches to walk, got {count}");
            Assert.Equal(0, Search(app).Index);

            Press(session, KeyCode.Enter);
            Assert.Equal(1, Search(app).Index);

            for (int i = 1; i < count; i++) Press(session, KeyCode.Enter);
            Assert.Equal(0, Search(app).Index);   // all the way round

            Press(session, KeyCode.Enter, KeyModifiers.Shift);
            Assert.Equal(count - 1, Search(app).Index);
        }, content: string.Join("\n", Enumerable.Range(0, 40).Select(i => $"line {i}")) + "\n");
    }

    [Fact]
    public void TheRegexToggleChangesWhatTheQueryMeans()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Find(session, app, @"\d+");
            Assert.Empty(Search(app).Hits);       // no literal backslash-d in the file

            Press(session, KeyCode.R, KeyModifiers.Alt);
            Assert.NotEmpty(Search(app).Hits);
        }, content: string.Join("\n", Enumerable.Range(0, 20).Select(i => $"line {i}")) + "\n");
    }

    [Fact]
    public void AnUnparseablePatternIsReportedRatherThanReadAsNoMatches()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Find(session, app, "([unclosed");
            Press(session, KeyCode.R, KeyModifiers.Alt);

            Assert.Equal("bad pattern", Search(app).Error);
        }, content: "one\ntwo\n");
    }

    [Fact]
    public void EscapeClosesTheBar()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Find(session, app, "EDIT");
            Assert.True(Open(app));

            Press(session, KeyCode.Escape);
            Assert.False(Open(app));
            Assert.Empty(Search(app).Hits);
        }, content: "one\ntwo\n");
    }

    /// <summary>The diff's own shortcuts stay out of the query. Junction routes the find bar
    /// before the typing guard, so "w" must reach the field rather than toggling wrap.</summary>
    [Fact]
    public void WDoesNotToggleWrapWhileTypingInTheField()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            bool wrap = Wrap(app);
            Press(session, KeyCode.F, KeyModifiers.Ctrl);
            session.Window.PressKey(KeyCode.W);
            session.PumpFrames(2);

            Assert.Equal(wrap, Wrap(app));
        }, content: "one\ntwo\n");
    }

    /// <summary>
    /// A match far down the diff is scrolled to.
    ///
    /// <para>Needs a diff that is actually long, which an appended line is not — git reports
    /// one hunk of a few lines whatever the file's size. This rewrites every line, so the diff
    /// is hundreds of rows and the last match is well below the viewport.</para>
    /// </summary>
    [Fact]
    public void WalkingToAMatchScrollsItIntoView()
    {
        HostedWithLongDiff((session, app) =>
        {
            Find(session, app, "needle");
            Assert.NotEmpty(Search(app).Hits);

            // Typing already reveals the first match, and in this diff that is itself a long
            // way down: git lists all 300 deletions before the additions the needle is on.
            float first = Body(app).ScrollOffsetY;
            Assert.True(first > 0, "the first match is hundreds of rows down and the body never moved");

            // And walking to the last one goes further still.
            for (int i = 1; i < Search(app).Hits.Count; i++) Press(session, KeyCode.Enter);

            Assert.True(Body(app).ScrollOffsetY > first,
                $"walked from the first match to the last ({Search(app).Hits.Count} of them)"
                + $" and the body stayed at {first:0}px");
        });
    }

    // ── Reaching the app's state ─────────────────────────────────────────

    private static object State(JunctionApp app) =>
        typeof(JunctionApp).GetField("_diffFind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;

    private static object Member(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(owner)!;

    private static bool Open(JunctionApp app) => (bool)Member(State(app), "Open");
    private static TextSearch Search(JunctionApp app) => (TextSearch)Member(State(app), "Search");

    private static bool Wrap(JunctionApp app) =>
        (bool)typeof(JunctionApp).GetField("_diffWrap", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;

    /// <summary>Switches the pane to split. The field alone is not enough: nothing rebuilds
    /// until the app is marked dirty, so the search would still be bound to the unified
    /// rows — and would look like split mode had simply been ignored.</summary>
    private static void SetSplit(JunctionApp app, bool split)
    {
        typeof(JunctionApp).GetField("_diffSplit", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(app, split);
        typeof(JunctionApp).GetMethod("MarkDirty",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.Invoke(app, null);
    }

    private static ScrollViewElement Body(JunctionApp app) =>
        (ScrollViewElement)((HultUI.Core.Reactivity.Ref<Element>)typeof(JunctionApp)
            .GetField("_diffBodyRef", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!).Current!;

    /// <summary>A repository whose working tree differs from HEAD on every line, so the diff
    /// is long enough to scroll. DiffScrollTests.Hosted always appends a single line, which
    /// git renders as one short hunk however big the file is.</summary>
    private static void HostedWithLongDiff(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("big.txt", Lines("old line {0}", 300));
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "add a long file");
        // Every line rewritten, and a needle on each, so there are matches to walk.
        repo.Write("big.txt", Lines("new needle {0}", 300));

        using var session = HeadlessSession.Create(1400, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [repo.Path],
                ActiveRepoPath = repo.Path,
                Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
                ActiveWorkspaceId = "w1",
            });
            session.Application.SetRoot(app.Mount());
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Digit4);   // Changes
            session.PumpUntil(() => BodyOrNull(app!) is { } l && l.Children.Count > 1,
                TimeSpan.FromSeconds(30));
            session.PumpFrames(4);
            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    private static ScrollViewElement? BodyOrNull(JunctionApp app) =>
        ((HultUI.Core.Reactivity.Ref<Element>)typeof(JunctionApp)
            .GetField("_diffBodyRef", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!).Current as ScrollViewElement;

    private static string Lines(string format, int count)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < count; i++) sb.Append(string.Format(format, i)).Append('\n');
        return sb.ToString();
    }

    private static void Press(HeadlessSession session, KeyCode key, KeyModifiers mods = KeyModifiers.None)
    {
        session.Window.PressKey(key, mods);
        session.PumpFrames(2);
    }

    /// <summary>Opens the bar and types, the way a user does — real text input to the focused
    /// field, so the app runs whatever it runs on a keystroke.</summary>
    private static void Find(HeadlessSession session, JunctionApp app, string query)
    {
        if (!Open(app)) Press(session, KeyCode.F, KeyModifiers.Ctrl);
        session.Window.TypeText(query);
        session.PumpFrames(2);
    }
}
