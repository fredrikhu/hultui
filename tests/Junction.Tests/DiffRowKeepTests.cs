using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Headless;
using Junction.Theme;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The diff's rows are flattened once and kept: a build that changes nothing they were made
/// from — a keystroke in the commit summary beside the diff, a toast frame — flattens nothing
/// and makes no row, and each thing the rows do depend on makes them again.
///
/// <para>Every build used to flatten the diff anew (a record per line, a word diff per changed
/// pair, merged spans) and the list made every row on screen again from the new records:
/// Changes, typing beside a 184-row diff, 744 KB of Build and 441 KB of rows a keystroke. The
/// flattened rows now live until the diff, the layout, the highlight or the scheme is another
/// object, and the list is on item rows (<c>.RenderWith((row, selected) => …)</c>), told by
/// <c>InvalidateRows</c> when anything else a row shows moves.</para>
/// </summary>
[Collection("JunctionDiff")]
public class DiffRowKeepTests
{
    [Fact]
    public void TypingInTheSummary_FlattensNothing_AndMakesNoRow()
    {
        Hosted((session, app) =>
        {
            var summary = Get<Ref<TextInputElement>>(app, "_summaryRef").Current!;
            session.Window.Click(summary);
            session.PumpFrames(2);
            var rows = Rows(app);
            int flattens = app.DiffFlattens, made = app.DiffRowsMade, builds = app.Builds;

            foreach (var ch in "Fix it")
            {
                session.Window.TypeText(ch.ToString());
                session.PumpFrame();
            }

            Assert.Equal("Fix it", Get<string>(app, "_commitSummary"));
            Assert.True(app.Builds - builds >= 6, $"{app.Builds - builds} builds for six keystrokes");
            Assert.Equal(flattens, app.DiffFlattens);
            Assert.Equal(made, app.DiffRowsMade);
            Assert.Equal(rows, Rows(app));   // the same row elements, in the same places
        });
    }

    [Fact]
    public void SwitchingToSplit_FlattensAgain()
    {
        Hosted((session, app) =>
        {
            int flattens = app.DiffFlattens, made = app.DiffRowsMade;

            Invoke(app, "SetDiffSplit", true);
            session.PumpFrames(2);

            Assert.Equal(flattens + 1, app.DiffFlattens);
            Assert.True(app.DiffRowsMade - made >= Rows(app).Count, "the rows were not made again");
            // A changed pair is one row now: the old line on the left, the new on the right.
            var pair = Rows(app).First(r => Texts(r).Any(t => t.StartsWith("    int value4 = 4;", StringComparison.Ordinal)));
            Assert.Contains(Texts(pair), t => t.StartsWith("    int value4 = 40;", StringComparison.Ordinal));
        });
    }

    /// <summary>The first build asks for the colours and draws plain; the tokenize landing is a
    /// new highlight, and the rows are flattened again with it.</summary>
    [Fact]
    public void TheHighlightLanding_FlattensAgainInColour()
    {
        Hosted((session, app) =>
        {
            session.PumpUntil(() => Coloured(app), TimeSpan.FromSeconds(20));
            Assert.True(app.DiffFlattens >= 2, $"{app.DiffFlattens} flattenings");
        }, waitForColours: false);
    }

    /// <summary>The word diff's tint is the scheme's, baked into the flattened spans.</summary>
    [Fact]
    public void ASchemeSwitch_FlattensAgainWithItsWordTint()
    {
        Hosted((session, app) =>
        {
            try
            {
                var before = Nocturne.DiffAddHl;
                Assert.Contains(before, Backgrounds(app));

                Nocturne.Use("gruvbox-dark");
                Rebuild(app);
                session.PumpFrames(3);

                Assert.NotEqual(before, Nocturne.DiffAddHl);
                Assert.Contains(Nocturne.DiffAddHl, Backgrounds(app));
                Assert.DoesNotContain(before, Backgrounds(app));
            }
            finally
            {
                Nocturne.Use("nocturne");
            }
        });
    }

    [Fact]
    public void ANewDiff_FlattensAgain()
    {
        Hosted((session, app) =>
        {
            File.AppendAllText(Path.Combine(Get<string>(app, "_activeRepoPath"), "src", "Notes.cs"), "// appended after the class\n");
            Invoke(app, "RefreshAll");
            session.PumpUntil(() => Rows(app).Any(r => Texts(r).Contains("// appended after the class")),
                TimeSpan.FromSeconds(20));
        });
    }

    /// <summary>A commit's colours are keyed by its content, so reloading its diff (here with
    /// whitespace ignored) keeps the same highlight: only the diff is a new object, and that
    /// alone has to flatten again.</summary>
    [Fact]
    public void AReloadedCommitDiff_FlattensAgain_WithTheSameHighlight()
    {
        Hosted((session, app) =>
        {
            int Value20Rows() => Rows(app).Count(r => Texts(r).Any(t => t.TrimStart().StartsWith("int value20 = 20;", StringComparison.Ordinal)));
            Assert.Equal(2, Value20Rows());   // the re-indented line: removed and added
            int flattens = app.DiffFlattens;

            Invoke(app, "ToggleIgnoreWhitespace");
            session.PumpUntil(() => Value20Rows() == 0, TimeSpan.FromSeconds(20));   // no change left: no hunk
            Assert.True(app.DiffFlattens > flattens);
        }, commitView: true);
    }

    /// <summary>A line picked for staging draws ticked and tinted: state the list does not see.</summary>
    [Fact]
    public void PickingALineToStage_MakesTheRowsAgain()
    {
        Hosted((session, app) =>
        {
            var row = RowWith(app, "    int value4 = 40;");
            int flattens = app.DiffFlattens;

            session.Window.Click(row);
            session.PumpFrames(2);

            Assert.Equal(flattens, app.DiffFlattens);
            Assert.Equal(Nocturne.Alpha(Nocturne.Accent, 0.22), ((BoxElement)RowWith(app, "    int value4 = 40;")).BackgroundColor);
        });
    }

    [Fact]
    public void FindTintsTheKeptRows()
    {
        Hosted((session, app) =>
        {
            int flattens = app.DiffFlattens;
            var search = Get<HultUI.Core.Builders.FindBar>(app, "_diffFind").Search;

            search.SetQuery("gamma");
            session.PumpFrames(2);

            Assert.Equal(flattens, app.DiffFlattens);
            Assert.True(search.HasHits);
            Assert.Contains(Nocturne.FindCurrentBg, Backgrounds(app));
            Assert.Contains(Nocturne.FindHitBg, Backgrounds(app));
        });
    }

    /// <summary>The code face, size and tab width are settings the rows draw with.</summary>
    [Fact]
    public void ATabWidthOrCodeFaceChange_MakesTheRowsAgain()
    {
        Hosted((session, app) =>
        {
            var settings = Get<JunctionSettingsData>(app, "_settings");
            int width = settings.Appearance.TabWidth;
            Assert.Contains(new string(' ', width) + "int tabbed = 2;", AllTexts(app));

            settings.Appearance.TabWidth = width + 3;
            Invoke(app, "InvalidateCodeMetrics");
            Rebuild(app);
            session.PumpFrames(2);
            Assert.Contains(new string(' ', width + 3) + "int tabbed = 2;", AllTexts(app));

            // Not the default face (Consolas on Windows), or nothing changes.
            Invoke(app, "UseCodeFont", "Courier New");
            session.PumpFrames(2);
            var label = Rows(app).SelectMany(r => Descendants(r)).OfType<TextElement>()
                .First(t => t.Content.StartsWith("    int value4 = 40;", StringComparison.Ordinal));
            Assert.Equal("Courier New", label.Inherited.FontFamily);
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private static T Get<T>(JunctionApp app, string field) =>
        (T)typeof(JunctionApp).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static void Invoke(JunctionApp app, string name, params object?[] args) =>
        typeof(JunctionApp).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, args);

    private static void Rebuild(JunctionApp app)
    {
        for (var t = typeof(JunctionApp); t != null; t = t.BaseType)
            if (t.GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance) is { } m)
            { m.Invoke(app, null); return; }
        throw new Xunit.Sdk.XunitException("no MarkDirty to call");
    }

    private static ScrollViewElement? ListOrNull(JunctionApp app) =>
        Get<Ref<Element>>(app, "_diffBodyRef").Current as ScrollViewElement;

    /// <summary>The realized diff rows, top to bottom (the list's first child is its spacer).</summary>
    private static List<Element> Rows(JunctionApp app) =>
        (ListOrNull(app) ?? throw new Xunit.Sdk.XunitException("the diff list is not on screen")).Children.Skip(1).ToList();

    private static List<string> Texts(Element row) =>
        Descendants(row).OfType<TextElement>().Select(t => t.Content).ToList();

    private static List<string> AllTexts(JunctionApp app) => Rows(app).SelectMany(Texts).ToList();

    private static Element RowWith(JunctionApp app, string code) =>
        Rows(app).First(r => Texts(r).Any(t => t.StartsWith(code, StringComparison.Ordinal)));

    /// <summary>Every span background the rows on screen draw.</summary>
    private static List<Color> Backgrounds(JunctionApp app) =>
        Rows(app).SelectMany(Descendants).OfType<TextElement>()
            .SelectMany(t => t.Spans ?? []).Select(s => s.Background).Where(c => c.A > 0).ToList();

    private static bool Coloured(JunctionApp app) =>
        ListOrNull(app) is { } list && list.Children.Skip(1).SelectMany(Descendants).OfType<TextElement>()
            .Any(t => t.Content.StartsWith("class Notes", StringComparison.Ordinal) && t.Spans is { Count: > 0 } s && s.Any(x => x.Color.A > 0));

    private static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var d in Descendants(child))
                yield return d;
    }

    private static string Notes(Func<int, string> line, string tabbed) =>
        "class Notes\n{\n" + tabbed + "\n" + string.Concat(Enumerable.Range(4, 30).Select(i => line(i) + "\n")) + "}\n";

    /// <summary>Changes (or, with <paramref name="commitView"/>, the Commit tab on the commit
    /// that makes the change), on a C# file with a line holding a tab and seven more changed
    /// word-wise, and one re-indented further down; its diff on screen and (by default)
    /// coloured.</summary>
    private static void Hosted(Action<HeadlessSession, JunctionApp> body, bool waitForColours = true, bool commitView = false)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("src/Notes.cs", Notes(i => $"    int value{i} = {i}; // alpha beta gamma {i}", "\tint tabbed = 1;"));
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "add notes");
        repo.Write("src/Notes.cs", Notes(i => i switch
        {
            <= 10 => $"    int value{i} = {i * 10}; // alpha BETA gamma {i}",
            20 => $"        int value{i} = {i}; // alpha beta gamma {i}",
            _ => $"    int value{i} = {i}; // alpha beta gamma {i}",
        }, "\tint tabbed = 2;"));
        if (commitView) repo.Git("commit", "-am", "change notes");

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

            if (commitView)
            {
                session.Window.PressKey(KeyCode.Digit2);   // History selects the newest commit
                session.PumpUntil(() => Get<string?>(app!, "_selectedCommitHash") != null, TimeSpan.FromSeconds(30));
                session.Window.PressKey(KeyCode.Digit3);   // Commit
            }
            else
                session.Window.PressKey(KeyCode.Digit4);   // Changes
            session.PumpUntil(() => ListOrNull(app!) is { } l && l.Children.Count > 1, TimeSpan.FromSeconds(30));
            if (waitForColours) session.PumpUntil(() => Coloured(app!), TimeSpan.FromSeconds(20));
            session.PumpFrames(3);

            body(session, app);
        }
        finally { app?.Dispose(); }
    }
}
