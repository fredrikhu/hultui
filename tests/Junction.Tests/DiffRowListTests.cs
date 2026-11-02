using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Headless;
using Junction.Theme;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// A diff's rows are made as they are asked for, and their colours when they are drawn.
///
/// <para>Selecting a file flattened all of it, a record per line, a word diff per changed pair
/// and the merged spans of both, and again when its colours landed, while about thirteen rows
/// showed: 439 of the 556 KB a key costs on a commit of 415 files. Everything that needs a row
/// the list has not drawn still gets it: a find, a copy, wrapped heights, the review's weave.</para>
/// </summary>
[Collection("JunctionDiff")]
public class DiffRowListTests
{
    private const int NewLines = 5000;

    [Fact]
    public void OpeningALargeFile_MakesTheRowsOnScreen_NotTheFile()
    {
        Hosted((session, app) =>
        {
            int records = app.DiffRowRecords, flattens = app.DiffFlattens;

            Open(session, app, "src/New.cs");
            session.PumpFrames(3);

            Assert.Equal(NewLines + 1, RowCount(app));
            Assert.Equal(flattens + 1, app.DiffFlattens);
            Assert.InRange(app.DiffRowRecords - records, 1, 120);
        });
    }

    /// <summary>The colours landing draw the rows on screen again and make no record.</summary>
    [Fact]
    public void AFilesColoursLanding_FlattensNothing()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/New.cs");
            session.PumpFrames(2);
            int records = app.DiffRowRecords, flattens = app.DiffFlattens;
            Assert.False(Coloured(app, "    public int Value1("));

            session.PumpUntil(() => Coloured(app, "    public int Value1("), TimeSpan.FromSeconds(30));

            Assert.Equal(flattens, app.DiffFlattens);
            Assert.Equal(records, app.DiffRowRecords);
        });
    }

    [Fact]
    public void ScrollingFar_MakesTheRowsItReaches()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/New.cs");
            session.PumpFrames(2);
            int records = app.DiffRowRecords;

            List(app).ScrollBy(0, 4000 * 22f);
            session.PumpFrames(3);

            Assert.InRange(app.DiffRowRecords - records, 1, 120);
            Assert.Contains(Rows(app).SelectMany(Texts), t => t.StartsWith("    public int Value4", StringComparison.Ordinal));
        });
    }

    /// <summary>A row scrolled away is let go; back on screen it draws what it drew.</summary>
    [Fact]
    public void ARowScrolledAwayAndBack_FindsItsSpans()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Pairs.cs");
            session.PumpUntil(() => Coloured(app, "    int value1 = 1;"), TimeSpan.FromSeconds(30));
            var before = Label(app, "    int value1 = 1;").Spans;
            int spans = ((ICollection)Member(DiffRows(app), "_spans")).Count;

            List(app).ScrollBy(0, 200 * 22f);
            session.PumpFrames(3);
            List(app).ScrollBy(0, -200 * 22f);
            session.PumpFrames(3);

            Assert.Same(before, Label(app, "    int value1 = 1;").Spans);
            Assert.True(((ICollection)Member(DiffRows(app), "_spans")).Count > spans);
        });
    }

    /// <summary>A removed line's word diff needs the line it is paired with, a screen below it.</summary>
    [Fact]
    public void AWordDiff_ShowsWhenItsPartnerIsOffScreen()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Pairs.cs");
            session.PumpFrames(3);

            Assert.DoesNotContain(Rows(app).SelectMany(Texts), t => t.StartsWith("    int value1 = 10;", StringComparison.Ordinal));
            Assert.Contains(Label(app, "    int value1 = 1;").Spans!, s => s.Background == Nocturne.DiffDelHl);
        });
    }

    /// <summary>The same word diff on the other side, and in split, both from the one pair.</summary>
    [Fact]
    public void AWordDiff_ShowsOnBothHalvesOfASplitRow()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Pairs.cs");
            Invoke(app, "SetDiffSplit", true);
            session.PumpFrames(3);

            Assert.Contains(Label(app, "    int value1 = 1;").Spans!, s => s.Background == Nocturne.DiffDelHl);
            Assert.Contains(Label(app, "    int value1 = 10;").Spans!, s => s.Background == Nocturne.DiffAddHl);
        });
    }

    [Fact]
    public void Find_SeesRowsNoOneDrew()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/New.cs");
            session.PumpFrames(2);
            int flattens = app.DiffFlattens;
            var search = Get<FindBar>(app, "_diffFind").Search;

            search.SetQuery("Value4999(");
            session.PumpFrames(2);

            var hit = Assert.Single(search.Hits);
            Assert.Equal(5000, hit.Line);   // the hunk's header is row 0, line 1 row 1
            Assert.Equal(flattens, app.DiffFlattens);
        });
    }

    [Fact]
    public void SelectingAll_CopiesRowsNoOneDrew()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/New.cs");
            session.PumpFrames(2);
            var group = Get<TextSelectionGroup>(app, "_diffGroupUnified");

            group.SelectAll();

            var text = group.SelectedText.ReplaceLineEndings("\n");
            Assert.Contains("    public int Value0(", text);
            Assert.Contains("    public int Value4999(", text);
            Assert.Equal(NewLines, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        });
    }

    /// <summary>Wrapped, the list needs every row's height, and only then has them all made.</summary>
    [Fact]
    public void WrappingTheDiff_MeasuresEveryRow()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/New.cs");
            session.PumpFrames(2);

            Invoke(app, "ToggleDiffWrap");
            session.PumpFrames(4);

            Assert.Equal(RowCount(app), Get<float[]>(app, "_diffHeights").Length);
        });
    }

    /// <summary>Rows in a hunk with runs of different lengths, and a second hunk: the rows and
    /// the pairs are the ones flattening made.</summary>
    [Fact]
    public void RowsInSplit_PairARunsLinesInOrder()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Runs.cs");
            Invoke(app, "SetDiffSplit", true);
            session.PumpFrames(3);

            var rows = Rows(app).Select(Texts).ToList();
            string Row(string code) => string.Join("|", rows.First(r => r.Any(t => t.Contains(code, StringComparison.Ordinal)))
                .Where(t => t.Contains(" line ", StringComparison.Ordinal) || t == " ").Select(t => t.Trim()));

            Assert.Equal("old line 11|new line 11", Row("old line 11"));
            Assert.Equal("old line 12|new line 12", Row("old line 12"));
            Assert.Equal("old line 13|", Row("old line 13"));           // more removed than added
            // Two headers, three context lines above and below each change.
            Assert.Equal(2 + 2 * 6 + 3 + 3, RowCount(app));

            List(app).ScrollBy(0, 12 * 22f);
            session.PumpFrames(3);
            rows = Rows(app).Select(Texts).ToList();
            Assert.Equal("old line 81|new line 81", Row("old line 81"));
            Assert.Equal("|new line 82", Row("new line 82"));           // more added than removed
            Assert.Equal("|new line 83", Row("new line 83"));
        });
    }

    /// <summary>A reader's threads ask for a long list's rows while the UI thread does: there is
    /// one row for an index, whoever asked first.</summary>
    [Fact]
    public void ARowMadeOnAnotherThread_IsTheRow()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/New.cs");
            session.PumpFrames(2);
            var list = (IEnumerable)DiffRows(app);
            var item = list.GetType().GetProperty("Item")!;
            var seen = new ConcurrentDictionary<int, object>();

            Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 8 }, t =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    var row = item.GetValue(list, [i * 5])!;
                    Assert.Same(seen.GetOrAdd(i, row), row);
                }
            });
        });
    }

    /// <summary>The list's content is as wide as the line that reaches furthest once its tabs
    /// are expanded, which is not the one with the most characters.</summary>
    [Fact]
    public void TheWidestLine_IsWhatItsTabsExpandTo()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Plain.cs");
            session.PumpFrames(3);
            float plain = Get<HorizontalPan>(app, "_diffPan").ContentWidth;   // 100 characters
            int tab = Get<JunctionSettingsData>(app, "_settings").Appearance.TabWidth;

            Open(session, app, "src/Tabbed.cs");
            session.PumpFrames(3);

            float tabbed = Get<HorizontalPan>(app, "_diffPan").ContentWidth;  // 3 tabs and 95, beside a plain 99
            Assert.Equal(plain * (3 * tab + 95) / 100f, tabbed, 1f);
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private const BindingFlags All = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    private static T Get<T>(JunctionApp app, string field) =>
        (T)typeof(JunctionApp).GetField(field, All)!.GetValue(app)!;

    private static object Member(object target, string name) =>
        target.GetType().GetField(name, All)!.GetValue(target)!;

    private static object DiffRows(JunctionApp app) => Get<object>(app, "_diffRows");

    private static int RowCount(JunctionApp app) => ((IReadOnlyCollection<object>)DiffRows(app)).Count;

    private static void Invoke(JunctionApp app, string method, params object?[] args) =>
        typeof(JunctionApp).GetMethod(method, All)!.Invoke(app, args);

    private static ScrollViewElement List(JunctionApp app) => (ScrollViewElement)Get<Ref<Element>>(app, "_diffBodyRef").Current!;

    private static List<Element> Rows(JunctionApp app) => List(app).Children.Skip(1).ToList();

    private static List<string> Texts(Element row) => Descendants(row).OfType<TextElement>().Select(t => t.Content).ToList();

    private static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var d in Descendants(child))
                yield return d;
    }

    private static TextElement Label(JunctionApp app, string code) =>
        Rows(app).SelectMany(Descendants).OfType<TextElement>().First(t => t.Content == code);

    private static bool Coloured(JunctionApp app, string code) =>
        Rows(app).SelectMany(Descendants).OfType<TextElement>()
            .Any(t => t.Content.StartsWith(code, StringComparison.Ordinal) && t.Spans is { Count: > 0 } s && s.Any(x => x.Color.A > 0));

    /// <summary>The Commit tab, open on <paramref name="path"/>, its first build done.</summary>
    private static void Open(HeadlessSession session, JunctionApp app, string path)
    {
        typeof(JunctionApp).GetField("_selectedFilePath", All)!.SetValue(app, path);
        typeof(JunctionApp).GetField("_commitCursorPath", All)!.SetValue(app, path);
        Invoke(app, "RerenderView");
        session.PumpFrame();
    }

    private static string Lines(int from, int to, Func<int, string> line) =>
        string.Concat(Enumerable.Range(from, to - from + 1).Select(i => line(i) + "\n"));

    private static void Hosted(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        string Same(int i) => $"    // same line {i}";
        string Runs(string side, params int[] changed) =>
            Lines(1, 100, i => changed.Contains(i) ? $"    {side} line {i}" : Same(i));

        repo.Write("src/Aaa.cs", "class Aaa { }\n");
        repo.Write("src/Pairs.cs", Lines(1, 300, i => $"    int value{i} = {i};"));
        // Removed 11, 12, 13 and added 11, 12: and removed 81, added 81, 82, 83.
        repo.Write("src/Runs.cs", Lines(1, 100, i => i is 11 or 12 or 13 or 81 ? $"    old line {i}" : Same(i)));
        repo.Write("src/Plain.cs", "class Plain\n{\n}\n");
        repo.Write("src/Tabbed.cs", "class Tabbed\n{\n}\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "the sources");

        repo.Write("src/Aaa.cs", "class Aaa { int x; }\n");
        repo.Write("src/Pairs.cs", Lines(1, 300, i => $"    int value{i} = {i}0;"));
        repo.Write("src/Runs.cs", string.Concat(Enumerable.Range(1, 100).Select(i => i switch
        {
            11 or 12 => $"    new line {i}\n",
            13 => "",
            81 => $"    new line 81\n    new line 82\n    new line 83\n",
            _ => Same(i) + "\n",
        })));
        repo.Write("src/Plain.cs", "class Plain\n{\n" + new string('p', 100) + "\n}\n");
        repo.Write("src/Tabbed.cs", "class Tabbed\n{\n" + new string('q', 99) + "\n\t\t\t" + new string('t', 95) + "\n}\n");
        repo.Write("src/New.cs", Lines(0, NewLines - 1, i => $"    public int Value{i}() => {i};"));
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "change them");

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
            session.Window.PressKey(KeyCode.Digit2);
            session.PumpUntil(() => Get<string?>(app, "_selectedCommitHash") != null, TimeSpan.FromSeconds(30));
            session.Window.PressKey(KeyCode.Digit3);
            session.PumpUntil(() => Get<string?>(app, "_selectedFilePath") is { Length: > 0 }, TimeSpan.FromSeconds(30));
            session.PumpUntil(() => app.HighlightsPending == 0, TimeSpan.FromSeconds(30));
            body(session, app);
        }
        finally { app?.Dispose(); }
    }
}
