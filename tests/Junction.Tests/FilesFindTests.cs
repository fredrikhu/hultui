using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Rendering;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Find-in-text inside the Files view (Ctrl+F).
///
/// <para>The awkward part here is that the body is a single read-only TextArea rather than a
/// row per line, so the search's line-and-column answers have to be translated into absolute
/// offsets in the flattened text. Getting that wrong tints the wrong words — visibly, and by a
/// margin that grows with how far down the file you look — which is what these check.</para>
/// </summary>
[Collection("JunctionDiff")]
public class FilesFindTests
{
    [Fact]
    public void CtrlFOpensTheBarAndFocusesIt()
    {
        Hosted((session, app) =>
        {
            Press(session, KeyCode.F, KeyModifiers.Ctrl);

            Assert.True(Open(app));
            Assert.IsType<TextInputElement>(session.Window.Window.FocusedElement);
        });
    }

    [Fact]
    public void TypingFindsEveryMatchInTheOpenFile()
    {
        Hosted((session, app) =>
        {
            Find(session, app, "needle");

            // One per line that carries it, and the line numbers are the file's own —
            // 0-based, so the third line of the file is 2.
            Assert.Equal(3, Search(app).Hits.Count);
            Assert.Equal(new[] { 2, 4, 6 }, Search(app).Hits.Select(h => h.Line).ToArray());
        });
    }

    /// <summary>
    /// The tint lands on the matched word, not near it.
    ///
    /// <para>Asserted by reading the tinted span back out of the flattened text: a translation
    /// that dropped the newline separators, or counted from the wrong line, still produces
    /// plausible-looking spans — they just cover the wrong characters, further off with every
    /// line. Checking the substring is the only assertion that can tell.</para>
    /// </summary>
    [Fact]
    public void EachTintCoversTheMatchedWordItself()
    {
        Hosted((session, app) =>
        {
            Find(session, app, "needle");

            string text = BodyText(app);
            var tinted = BodySpans(app).Where(s => IsFindTint(s.Background)).ToList();
            Assert.Equal(3, tinted.Count);
            foreach (var span in tinted)
                Assert.Equal("needle", text.Substring(span.Start, span.Length));
        });
    }

    [Fact]
    public void TheCurrentMatchIsTintedDifferentlyAndMovesWithEnter()
    {
        Hosted((session, app) =>
        {
            Find(session, app, "needle");

            var first = BodySpans(app).Where(s => IsFindTint(s.Background)).ToList();
            Assert.Equal(Theme.Nocturne.FindCurrentBg, first[0].Background);
            Assert.Equal(Theme.Nocturne.FindHitBg, first[1].Background);

            Press(session, KeyCode.Enter);

            var second = BodySpans(app).Where(s => IsFindTint(s.Background)).ToList();
            Assert.Equal(Theme.Nocturne.FindHitBg, second[0].Background);
            Assert.Equal(Theme.Nocturne.FindCurrentBg, second[1].Background);
        });
    }

    /// <summary>
    /// Every match is actually painted — asserted against the display list, not against the
    /// element's span property.
    ///
    /// <para>The distinction is the whole reason this test exists. The tints were being
    /// appended to the span list after the syntax spans instead of merged in offset order,
    /// and <c>TextAreaElement</c> slices that list per visible line and stops at the first
    /// span starting past the end of the line — so every tint sitting behind that stop was
    /// dropped on the way to the renderer. The spans on the element were perfect the whole
    /// time; two of the three matches simply never reached the screen, which read as search
    /// not highlighting at all.</para>
    /// </summary>
    [Fact]
    public void EveryMatchIsPaintedAndNotJustRecorded()
    {
        Hosted((session, app) =>
        {
            Find(session, app, "needle");

            session.Window.RenderLog!.Clear();
            MarkDirty(app);
            // Waited for rather than counted in frames: the body is rebuilt off the back of
            // async work, so a fixed frame count sometimes captured a repaint that had not
            // drawn it yet. The wait is on the lines being drawn at all — which happens with
            // or without the tint — so it cannot mask the thing being asserted.
            session.PumpUntil(() => DrawnMatchLines(session).Count >= 3, TimeSpan.FromSeconds(20));

            var drawn = DrawnMatchLines(session);
            Assert.All(drawn, line => Assert.Contains("/bg:", line, StringComparison.Ordinal));
        }, record: true);
    }

    /// <summary>Syntax colouring survives a search: a match carries a background only, so the
    /// grammar's foregrounds are all still asked for.</summary>
    [Fact]
    public void SyntaxColourSurvivesASearch()
    {
        Hosted((session, app) =>
        {
            var before = BodySpans(app).Where(s => s.Color != default).ToList();
            Assert.NotEmpty(before);

            Find(session, app, "needle");

            var after = BodySpans(app);
            foreach (var s in before) Assert.Contains(after, a => a.Color == s.Color);
            Assert.Contains(after, s => IsFindTint(s.Background));
        });
    }

    [Fact]
    public void TheRegexToggleChangesWhatTheQueryMeans()
    {
        Hosted((session, app) =>
        {
            Find(session, app, @"needle\s+(one|three)");
            Assert.Empty(Search(app).Hits);       // nothing in the file reads like that literally

            Press(session, KeyCode.R, KeyModifiers.Alt);
            Assert.Equal(2, Search(app).Hits.Count);
            Assert.Equal(new[] { 2, 6 }, Search(app).Hits.Select(h => h.Line).ToArray());
        });
    }

    [Fact]
    public void EscapeClosesTheBarAndDropsTheTinting()
    {
        Hosted((session, app) =>
        {
            Find(session, app, "needle");
            Assert.Contains(BodySpans(app), s => IsFindTint(s.Background));

            Press(session, KeyCode.Escape);

            Assert.False(Open(app));
            Assert.DoesNotContain(BodySpans(app), s => IsFindTint(s.Background));
        });
    }

    /// <summary>A match below the fold is scrolled to. The TextArea scrolls by line, which is
    /// what the search already answers in.</summary>
    [Fact]
    public void WalkingToAMatchScrollsItIntoView()
    {
        Hosted((session, app) =>
        {
            Find(session, app, "far-below");
            Assert.NotEmpty(Search(app).Hits);

            // The reveal lands on a rebuild after the search resolves, so this waits for the
            // scroll rather than assuming a frame count — it went red about one full run in
            // four. The wait can only ever turn a slow pass into a pass; a body that never
            // scrolls still fails, with the message intact.
            try { session.PumpUntil(() => ScrollY(app) > 0, TimeSpan.FromSeconds(15)); }
            catch (TimeoutException) { /* fall through to the assertion below */ }

            Assert.True(ScrollY(app) > 0,
                "the only match is hundreds of lines down and the body never moved");
        }, longFile: true);
    }

    // ── Reaching the app's state ─────────────────────────────────────────

    private static object State(JunctionApp app) =>
        typeof(JunctionApp).GetField("_filesFind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;

    private static object Member(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(owner)!;

    private static bool Open(JunctionApp app) => (bool)Member(State(app), "Open");
    private static TextSearch Search(JunctionApp app) => (TextSearch)Member(State(app), "Search");

    private static TextAreaElement Body(JunctionApp app) =>
        ((HultUI.Core.Reactivity.Ref<TextAreaElement>)typeof(JunctionApp)
            .GetField("_fileBodyRef", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!).Current
        ?? throw new Xunit.Sdk.XunitException("the file body is not on screen");

    /// <summary>The body lines carrying a match, as they were handed to the renderer.</summary>
    private static List<string> DrawnMatchLines(HeadlessSession session) =>
        session.Window.RenderLog!.Named("DrawText")
            .Select(op => op.ToString())
            .Where(t => t.Contains("// needle", StringComparison.Ordinal))
            .ToList();

    private static void MarkDirty(JunctionApp app) =>
        typeof(JunctionApp).GetMethod("MarkDirty",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.Invoke(app, null);

    private static string BodyText(JunctionApp app) => Body(app).Text;

    private static IReadOnlyList<TextSpan> BodySpans(JunctionApp app) => Body(app).Spans ?? [];

    private static float ScrollY(JunctionApp app) =>
        (float)typeof(TextAreaElement).GetProperty("ScrollOffsetY",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Body(app))!;

    private static bool IsFindTint(HultUI.Core.Primitives.Color c) =>
        c == Theme.Nocturne.FindHitBg || c == Theme.Nocturne.FindCurrentBg;

    private static void Press(HeadlessSession session, KeyCode key, KeyModifiers mods = KeyModifiers.None)
    {
        session.Window.PressKey(key, mods);
        session.PumpFrames(2);
    }

    private static void Find(HeadlessSession session, JunctionApp app, string query)
    {
        if (!Open(app)) Press(session, KeyCode.F, KeyModifiers.Ctrl);
        session.Window.TypeText(query);
        session.PumpFrames(2);
    }

    /// <summary>The Files view on a committed source file, with the cursor already on it —
    /// the view browses the tree at the selected commit, so the file has to be in one.</summary>
    private static void Hosted(Action<HeadlessSession, JunctionApp> body, bool longFile = false,
        bool record = false)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        // A .cs file so the grammar colours it: one of these tests is about a match not
        // discarding the syntax colour under it.
        repo.Write("sample.cs", longFile
            ? Lines("// old line {0}", 400) + "// the far-below marker\n"
            : "class Alpha\n{\n    // needle one\n    void M() { }\n    // needle two\n"
              + "    int x;\n    // needle three\n}\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "add a sample file");

        using var session = record
            ? HeadlessSession.Create(1400, 900, options: HeadlessOptions.DeterministicRecording())
            : HeadlessSession.Create(1400, 900);
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

            session.Window.PressKey(KeyCode.Digit7);   // Files
            session.PumpUntil(() => app!.View == JunctionView.Files, TimeSpan.FromSeconds(20));

            SelectFile(app, "sample.cs");
            // The tree walk and the file read are both off-thread; wait for the body rather
            // than a frame count, or every assertion below races an empty pane.
            session.PumpUntil(() => BodyOrNull(app) is { } b && b.Text.Length > 0,
                TimeSpan.FromSeconds(30));
            session.PumpFrames(2);

            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    private static TextAreaElement? BodyOrNull(JunctionApp app) =>
        ((HultUI.Core.Reactivity.Ref<TextAreaElement>)typeof(JunctionApp)
            .GetField("_fileBodyRef", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!).Current;

    private static void SelectFile(JunctionApp app, string path)
    {
        typeof(JunctionApp).GetField("_filesSelPath", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(app, path);
        typeof(JunctionApp).GetMethod("MarkDirty",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.Invoke(app, null);
    }

    private static string Lines(string format, int count)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < count; i++) sb.Append(string.Format(format, i)).Append('\n');
        return sb.ToString();
    }
}
