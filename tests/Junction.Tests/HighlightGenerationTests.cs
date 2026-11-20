using System.Collections;
using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Rendering;
using HultUI.Headless;
using HultUI.Syntax;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// A working-tree diff's colours are keyed by the data generation, which every working-tree event
/// moves: an editor's save of any file, a new untracked file. The diff on screen was read and walked
/// from line 1 again though its texts had not changed, it drew uncoloured until the walk landed, and
/// the texts kept with a cancelled walk could never be found again (plan 17 round 17: bigrepo's
/// 675 KB file, 13,623 lines and 324 MB a new untracked file, uncoloured for 2.7 s). The texts are
/// still read, and hashed: the same hashes take the last generation's colours, or go on with the walk
/// kept with its texts; other hashes colour the file again.
/// </summary>
[Collection("JunctionDiff")]
public class HighlightGenerationTests
{
    private const int FileLines = 9_000;   // a walk of about 1.5 s here
    private const int ChangedLine = FileLines - 10;
    private const string HugeLine = "    int Changed = 2;";
    private const string SmallLine = "    int Small = 2;";

    /// <summary>A new untracked file, then a save of another file, while a big file's diff is on
    /// screen: its colours go across to the new generation with nothing walked, and every frame
    /// draws them. The colours are let post before frames are pumped, as they do beside git's
    /// quarter of a second (a loaded runner can start the working tree's load first); the walk of
    /// the old code takes the file's whole walk to post and lands with the rows just the same, so
    /// what fails there is the lines walked.</summary>
    [Fact]
    public void AWorkingTreeEventThatLeavesTheTextsAsTheyWere_WalksNothing_AndDrawsNoPlainFrame()
    {
        Hosted((session, app, repo) =>
        {
            Show(session, app, "src/Huge.cs");
            Assert.True(Coloured(session, HugeLine), "the big file's changed line is not coloured");

            foreach (var (name, change) in new (string, Action)[]
                     {
                         ("a new untracked file", () => repo.Write("notes.txt", "new\n")),
                         ("a save of another file", () => repo.Write("src/Small.cs", Small("    int Small = 3;"))),
                     })
            {
                string key = ShownKey(app);
                var colours = Highlights(app)[key];
                int lines = app.HighlightLinesTokenized, reads = app.HighlightFileReads;
                change();

                int plain = Event(session, app, repo, HugeLine);

                Assert.True(plain == 0, $"{name}: {plain} frames drew the diff uncoloured");
                Assert.True(lines == app.HighlightLinesTokenized, $"{name}: {app.HighlightLinesTokenized - lines} lines walked");
                Assert.True(reads + 1 == app.HighlightFileReads, $"{name}: {app.HighlightFileReads - reads} reads");
                Assert.NotEqual(key, ShownKey(app));
                Assert.Same(colours, Highlights(app)[ShownKey(app)]);
                Assert.False(Highlights(app).Contains(key), $"{name}: the last generation's colours are still kept");
            }
        });
    }

    /// <summary>A save of the file on screen colours it again: the hashes differ, and the colours that
    /// land are one fresh walk's of the new texts.</summary>
    [Fact]
    public void AWorkingTreeEventThatChangesTheTextOnScreen_ColoursItAgain()
    {
        Hosted((session, app, repo) =>
        {
            Show(session, app, "src/Small.cs");
            int lines = app.HighlightLinesTokenized;
            repo.Write("src/Small.cs", Small("    string Small = \"two\";"));

            Event(session, app, repo, "    string Small");

            Assert.True(app.HighlightLinesTokenized > lines, "the changed file was not walked again");
            var highlight = Highlights(app)[ShownKey(app)]!;
            var fresh = Fresh(repo, "src/Small.cs", highlight);
            Same(fresh, highlight);
            Assert.True(Coloured(session, "    string Small"), "the changed line is drawn uncoloured");
        });
    }

    /// <summary>A big file left part-way keeps its texts; a working-tree event that leaves them as they
    /// were carries them to the new generation, and coming back reads the file once, to hash it, and goes
    /// on with the walk. Nothing is kept once the colours land: the texts the old generation kept went
    /// with it.</summary>
    [Fact]
    public void TheTextsOfAWalkLeftPartWay_FollowTheGeneration_AndTheWalkGoesOn()
    {
        Hosted((session, app, repo) =>
        {
            Show(session, app, "src/Small.cs");
            string huge = LeavePartWay(session, app);
            int lines = app.HighlightLinesTokenized, reads = app.HighlightFileReads;

            repo.Write("notes.txt", "new\n");
            Event(session, app, repo, SmallLine);
            Assert.True(lines == app.HighlightLinesTokenized, $"{app.HighlightLinesTokenized - lines} lines walked for Small.cs");
            Assert.True(KeptTexts(app).Contains(huge), "the big file's texts went at the event");

            Invoke(app, "SelectChange", "src/Huge.cs", false);
            session.PumpUntil(() => ShownIsCached(app) && app.HighlightsPending == 0, TimeSpan.FromSeconds(60));

            Assert.NotEqual(huge, ShownKey(app));
            Assert.Equal(reads + 2, app.HighlightFileReads);   // Small.cs and Huge.cs, each read to be hashed
            var highlight = Highlights(app)[ShownKey(app)]!;
            var whole = Fresh(repo, "src/Huge.cs", highlight);
            int walked = app.HighlightLinesTokenized - lines;
            Assert.InRange(walked, 1, whole.LinesWalked - 1);
            Same(whole, highlight);
            Assert.Empty(KeptTexts(app));
            Assert.Equal(0, app.HighlightTextsKeptBytes);
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private const BindingFlags All = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static string Huge(string changed) => string.Concat(Enumerable.Range(1, FileLines).Select(i =>
        (i == ChangedLine ? changed
            : i == ChangedLine - 3 ? "    /* opens"
            : i == ChangedLine + 2 ? "       closes */"
            : $"    public int Value{i}(string name) => name.Length + {i}; // the quick brown fox") + "\n"));

    private static string Small(string changed) => "class Small\n{\n    int Kept = 0;\n" + changed + "\n}\n";

    /// <summary>The file's diff on screen, its colours landed.</summary>
    private static void Show(HeadlessSession session, JunctionApp app, string path)
    {
        Invoke(app, "SelectChange", path, false);
        session.PumpUntil(() => ShownIsCached(app) && app.HighlightsPending == 0, Wait);
        session.PumpFrames(2);
    }

    /// <summary>Huge.cs's walk under way, then Small.cs selected, uncoloured, which cancels it; until a
    /// walk is kept with its texts (a busy machine may still be reading at the key). Huge.cs's key.</summary>
    private static string LeavePartWay(HeadlessSession session, JunctionApp app)
    {
        for (int tries = 0; ; tries++)
        {
            int read = app.HighlightsRead;
            Highlights(app).Clear();   // the view opened on Huge.cs, and may have coloured it
            Invoke(app, "SelectChange", "src/Huge.cs", false);
            session.PumpFrame();
            string key = ShownKey(app);
            Assert.True(SpinWait.SpinUntil(() => app.HighlightsRead > read, Wait), "the tokenize never got past its rest");
            Thread.Sleep(300);   // no frame runs, so nothing cancels it

            Highlights(app).Clear();   // a file whose colours are known leaves the walk going
            Invoke(app, "SelectChange", "src/Small.cs", false);
            session.PumpUntil(() => app.HighlightsPending == 0 && ShownIsCached(app) && Field(app, "_highlightRunning") is null, Wait);
            Assert.False(Highlights(app).Contains(key), "the whole file was coloured before the key");
            if (KeptTexts(app).Contains(key) && WalkKept(app, key)) return key;
            Assert.True(tries < 5, "no walk was kept in six visits");
        }
    }

    /// <summary>A working-tree event in the repository, as the watcher posts it, with the diff's colours
    /// let post first; then frames until the working tree at the new generation has landed and nothing is
    /// pending. The frames that drew <paramref name="line"/> uncoloured.</summary>
    private static int Event(HeadlessSession session, JunctionApp app, TempRepo repo, string line)
    {
        int gen = DataGen(app, repo.Path), posted = app.HighlightsPosted;
        Invoke(app, "OnWorkingTreeChange", repo.Path);
        Assert.True(SpinWait.SpinUntil(() => app.HighlightsPosted > posted, Wait), "no colours were asked for");
        int plain = 0;
        var until = DateTime.UtcNow + Wait;
        while (!(Landed() && app.HighlightsPending == 0 && ShownIsCached(app)))
        {
            Assert.True(DateTime.UtcNow < until, "the working tree never landed");
            if (session.PumpFrame(TimeSpan.FromMilliseconds(16)) && Spans(session, line) is not null && !Coloured(session, line)) plain++;
            Thread.Sleep(2);
        }
        session.PumpFrames(2);
        if (!Coloured(session, line)) plain++;
        return plain;

        bool Landed() => DataGen(app, repo.Path) > gen
            && LoadedKey(app, "_changesLoaders", repo.Path) is { } k && k.StartsWith($"{repo.Path}|{DataGen(app, repo.Path)}|", StringComparison.Ordinal);
    }

    /// <summary>Whether SyntaxHighlighter keeps a walk with the file's kept texts: it does once the
    /// walk has done a line.</summary>
    private static bool WalkKept(JunctionApp app, string key)
    {
        var texts = KeptTexts(app)[key]!;
        string held = (string)texts.GetType().GetProperty("New")!.GetValue(texts)!;
        var walks = typeof(SyntaxHighlighter).GetField("DiffWalks", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        object?[] args = [held, null];
        return (bool)walks.GetType().GetMethod("TryGetValue")!.Invoke(walks, args)!;
    }

    /// <summary>What one walk of the index's and the working tree's texts gives for the lines
    /// <paramref name="highlight"/> shows, on a clock that stands still, as Junction's does headless.</summary>
    private static TokenizedDiff Fresh(TempRepo repo, string path, object highlight)
    {
        var shown = highlight.GetType().GetProperty("Shown")!.GetValue(highlight)!;
        var oldShown = (LineRange[])shown.GetType().GetProperty("Old")!.GetValue(shown)!;
        var newShown = (LineRange[])shown.GetType().GetProperty("New")!.GetValue(shown)!;
        return SyntaxHighlighter.TokenizeDiff(path, repo.Repo.GetIndexFileText(path), oldShown,
            repo.Read(path), newShown, SyntaxHighlighter.DefaultMaxLines, default, new StandingClock());
    }

    private static void Same(TokenizedDiff whole, object highlight)
    {
        var old = (IReadOnlyList<TextSpan>[])highlight.GetType().GetProperty("OldLines")!.GetValue(highlight)!;
        var @new = (IReadOnlyList<TextSpan>[])highlight.GetType().GetProperty("NewLines")!.GetValue(highlight)!;
        Assert.Equal(whole.Old!.Length, old.Length);
        Assert.Equal(whole.New!.Length, @new.Length);
        for (int i = 0; i < old.Length; i++) Assert.Equal(whole.Old[i], old[i]);
        for (int i = 0; i < @new.Length; i++) Assert.Equal(whole.New[i], @new[i]);
    }

    private sealed class StandingClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }

    private static object? Field(JunctionApp app, string name) => typeof(JunctionApp).GetField(name, All)!.GetValue(app);

    private static IDictionary Highlights(JunctionApp app) => (IDictionary)Field(app, "_highlights")!;

    private static IDictionary KeptTexts(JunctionApp app) => (IDictionary)Field(app, "_highlightTexts")!;

    private static string ShownKey(JunctionApp app) => (string)Field(app, "_highlightShown")!;

    private static bool ShownIsCached(JunctionApp app) =>
        Field(app, "_highlightShown") is string key && Highlights(app).Contains(key);

    private static void Invoke(JunctionApp app, string method, params object?[] args) =>
        typeof(JunctionApp).GetMethod(method, All)!.Invoke(app, args);

    private static int DataGen(JunctionApp app, string path) =>
        ((Dictionary<string, int>)Field(app, "_dataGen")!).GetValueOrDefault(path);

    /// <summary>The key a repository's loader in <paramref name="field"/> holds its value for.</summary>
    private static string? LoadedKey(JunctionApp app, string field, string path)
    {
        var keyed = Field(app, field)!;
        var loader = keyed.GetType().GetMethod("For")!.Invoke(keyed, [path])!;
        return loader.GetType().GetField("_loadedKey", All)!.GetValue(loader) as string;
    }

    private static Element Root(HeadlessSession session) =>
        (Element)typeof(HultUI.Window).GetField("_root", All)!.GetValue(session.Window.Window)!;

    private static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var d in Descendants(child))
                yield return d;
    }

    private static IReadOnlyList<TextSpan>? Spans(HeadlessSession session, string code) =>
        Descendants(Root(session)).OfType<TextElement>()
            .FirstOrDefault(t => t.Content.StartsWith(code, StringComparison.Ordinal))?.Spans;

    private static bool Coloured(HeadlessSession session, string code) =>
        Spans(session, code) is { Count: > 0 } spans && spans.Any(s => s.Color.A > 0);

    /// <summary>Two modified C# files in the working tree, Huge.cs (a walk of about 1.5 s, its changed
    /// line inside a block comment opened above the hunk) and Small.cs; the Changes view on screen, the
    /// watchers quiet (disposed, still listed, so a build does not start them again): the test's own
    /// calls are the events.</summary>
    private static void Hosted(Action<HeadlessSession, JunctionApp, TempRepo> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("src/Huge.cs", Huge("    int Changed = 1;"));
        repo.Write("src/Small.cs", Small("    int Small = 1;"));
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "the sources");
        repo.Write("src/Huge.cs", Huge(HugeLine));
        repo.Write("src/Small.cs", Small(SmallLine));

        using var session = HeadlessSession.Create(1440, 900);
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
            session.PumpFrame();
            session.Window.PressKey(KeyCode.Digit4);
            session.PumpUntil(() => LoadedKey(app, "_changesLoaders", repo.Path) is { } k
                && k.StartsWith($"{repo.Path}|{DataGen(app, repo.Path)}|", StringComparison.Ordinal), Wait);
            foreach (var w in ((IDictionary)Field(app, "_watchers")!).Values) ((IDisposable)w!).Dispose();
            session.PumpUntil(() => app.HighlightsPending == 0, Wait);
            Assert.Equal(JunctionView.Changes, app.View);
            body(session, app, repo);
        }
        finally { app?.Dispose(); }
    }
}
