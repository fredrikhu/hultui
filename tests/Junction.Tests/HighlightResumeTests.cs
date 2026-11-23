using System.Collections;
using System.Reflection;
using HultUI.Core.Events;
using HultUI.Core.Rendering;
using HultUI.Headless;
using HultUI.Syntax;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// A file whose colours a key cancelled part-way keeps its two texts, and the walk SyntaxHighlighter
/// keeps with them: coming back to it goes on from where the walk stopped and reads nothing. A
/// reader going through bigrepo's 675 KB files at 400 ms a key cancelled every walk, and each visit
/// read both versions again (2.6 MB of large-heap strings) and walked from line 1 (plan 17 round
/// 16). The colours that land must be one uncancelled walk's.
/// </summary>
[Collection("JunctionDiff")]
public class HighlightResumeTests
{
    private const int FileLines = 9_000;   // a walk of about 1.5 s here, of which 0.3 s before the key
    private const int ChangedLine = FileLines - 10;

    [Fact]
    public void AFileLeftPartWay_GoesOnWhenTheReaderComesBack_ReadingNothing()
    {
        Hosted((session, app, repo) =>
        {
            int read = Open(session, app, "src/Huge.cs");
            string key = (string)Field(app, "_highlightShown")!;
            LeavePartWay(session, app, key, read);
            int reads = app.HighlightFileReads, lines = app.HighlightLinesTokenized;

            session.Window.PressKey(KeyCode.Up);   // back to Huge.cs
            session.PumpUntil(() => Highlights(app).Contains(key), TimeSpan.FromSeconds(60));

            Assert.Equal(reads, app.HighlightFileReads);
            var whole = Whole(repo, Highlights(app)[key]!);
            int walked = app.HighlightLinesTokenized - lines;
            Assert.InRange(walked, 1, whole.LinesWalked - 1);
            Same(whole, Highlights(app)[key]!);
            Assert.False(KeptTexts(app).Contains(key), "the texts are kept after the colours landed");
            Assert.Equal(0, app.HighlightTextsKeptBytes);
        });
    }

    /// <summary>A scheme change drops the texts with the walks: those are the old theme's, and the
    /// file is read and walked from line 1 under the new one.</summary>
    [Fact]
    public void ASchemeChange_DropsTheKeptTexts()
    {
        Hosted((session, app, repo) =>
        {
            int read = Open(session, app, "src/Huge.cs");
            string key = (string)Field(app, "_highlightShown")!;
            LeavePartWay(session, app, key, read);
            int reads = app.HighlightFileReads;

            Invoke(app, "RetokenizeCode");
            Assert.Empty(KeptTexts(app));
            Assert.Equal(0, app.HighlightTextsKeptBytes);

            session.Window.PressKey(KeyCode.Up);
            session.PumpUntil(() => Highlights(app).Contains(key), TimeSpan.FromSeconds(60));
            Assert.Equal(reads + 1, app.HighlightFileReads);
            Same(Whole(repo, Highlights(app)[key]!), Highlights(app)[key]!);
        });
    }

    /// <summary>A walk that kept texts gives its memory back once the worker rests: one aggressive
    /// collection, after <see cref="JunctionApp.HighlightIdleCollect"/>, and not again until texts are
    /// kept again. The texts stay, and coming back still goes on with the walk. Without it the window
    /// held 202.7 MB working set after a walk through bigrepo's big files, against 154.0 MB with no
    /// texts kept; with it 134.0 MB (plan 17 round 17).</summary>
    [Fact]
    public void AWalkThatKeptTexts_IsFollowedByOneCollectionWhenTheWorkerRests_AndTheTextsStay()
    {
        Hosted((session, app, repo) =>
        {
            app.HighlightIdleCollect = TimeSpan.FromMilliseconds(200);
            int read = Open(session, app, "src/Huge.cs");
            string key = (string)Field(app, "_highlightShown")!;
            LeavePartWay(session, app, key, read);
            session.PumpUntil(() => app.HighlightIdleCollections > 0, TimeSpan.FromSeconds(10));
            int collections = app.HighlightIdleCollections;
            Rest(session, 600);
            Assert.Equal(collections, app.HighlightIdleCollections);   // once a rest
            Assert.True(KeptTexts(app).Contains(key), "the collection took the kept texts");
            int reads = app.HighlightFileReads, lines = app.HighlightLinesTokenized;
            long bytes = app.HighlightBytesWalked;

            session.Window.PressKey(KeyCode.Up);   // back to Huge.cs
            session.PumpUntil(() => Highlights(app).Contains(key), TimeSpan.FromSeconds(60));
            Assert.Equal(reads, app.HighlightFileReads);
            int walked = app.HighlightLinesTokenized - lines;
            Assert.InRange(walked, 1, Whole(repo, Highlights(app)[key]!).LinesWalked - 1);

            // Landed, nothing kept since the collection: one more only when the rest of the walk was big.
            bool big = walked >= JunctionApp.BigWalkLines || app.HighlightBytesWalked - bytes >= JunctionApp.BigWalkBytes;
            Rest(session, 600);
            Assert.Equal(collections + (big ? 1 : 0), app.HighlightIdleCollections);
        });
    }

    /// <summary>A big walk that finished keeps nothing, and leaves the memory it allocated committed
    /// all the same: it too is followed by one collection, once the worker rests and the window has
    /// had no input for <see cref="JunctionApp.HighlightIdleCollect"/>, so never while the wheel
    /// turns. A small file's walk is followed by none. Without it bigrepo's Commit open left the
    /// window at 123.1 MB working set, 33.5 MB committed, for as long as it rested; with it 98.0 MB,
    /// 8.4 MB (plan 17 round 18).</summary>
    [Fact]
    public void ABigWalkThatFinished_IsFollowedByOneCollectionOnceTheInputStops_ASmallOneByNone()
    {
        Hosted((session, app, _) =>
        {
            app.HighlightIdleCollect = TimeSpan.FromMilliseconds(400);
            Open(session, app, "src/Huge.cs");
            string key = (string)Field(app, "_highlightShown")!;
            int lines = app.HighlightLinesTokenized;
            session.PumpUntil(() => Highlights(app).Contains(key), TimeSpan.FromSeconds(60));
            Assert.Empty(KeptTexts(app));   // it finished: nothing kept
            Assert.True(app.HighlightLinesTokenized - lines >= JunctionApp.BigWalkLines,
                $"{app.HighlightLinesTokenized - lines} lines walked");

            // The reader scrolls the diff its colours landed in: nothing collects while the wheel turns.
            var until = DateTime.UtcNow.AddMilliseconds(1200);
            while (DateTime.UtcNow < until)
            {
                session.Window.Wheel(1000, 500, 0, -1);
                session.PumpFrame();
                Thread.Sleep(20);
            }
            Assert.Equal(0, app.HighlightIdleCollections);
            session.PumpUntil(() => app.HighlightIdleCollections > 0, TimeSpan.FromSeconds(10));
            Rest(session, 1000);
            Assert.Equal(1, app.HighlightIdleCollections);   // once a rest

            lines = app.HighlightLinesTokenized;
            long bytes = app.HighlightBytesWalked;
            session.Window.PressKey(KeyCode.Down);   // to Small.cs: a walk of a line
            session.PumpUntil(() => app.HighlightLinesTokenized > lines && ShownIsCached(app)
                && Field(app, "_highlightRunning") is null, TimeSpan.FromSeconds(30));
            Assert.InRange(app.HighlightBytesWalked - bytes, 1, JunctionApp.BigWalkBytes - 1);
            Rest(session, 1000);
            Assert.Equal(1, app.HighlightIdleCollections);
        });
    }

    /// <summary>The texts kept are held to their bound in bytes, the file touched longest ago
    /// going first; a pair larger than the bound is not kept at all.</summary>
    [Fact]
    public void TheKeptTexts_AreHeldToTheirBound_TheOldestGoingFirst()
    {
        Hosted((_, app, _) =>
        {
            long limit = JunctionApp.MaxKeptHighlightBytes;
            // A pair of three tenths of the bound: two bytes a character and a line's array slot of eight.
            string text = new('x', (int)(limit * 3 / 40) - 4);
            Keep(app, "a", text);
            Keep(app, "b", text);
            Keep(app, "c", text);
            Assert.Equal(["a", "b", "c"], Order(app));
            Keep(app, "a", text);   // touched again: the newest
            Assert.Equal(["b", "c", "a"], Order(app));

            Keep(app, "d", text);

            Assert.Equal(["c", "a", "d"], Order(app));
            Assert.True(app.HighlightTextsKeptBytes <= limit, $"{app.HighlightTextsKeptBytes:N0} bytes kept");

            Keep(app, "e", new string('x', (int)(limit / 2 / 2)));   // over the bound alone
            Assert.DoesNotContain("e", Order(app));
            Assert.True(app.HighlightTextsKeptBytes <= limit, $"{app.HighlightTextsKeptBytes:N0} bytes kept");
        });

        static void Keep(JunctionApp app, string key, string text)
        {
            var type = typeof(JunctionApp).GetNestedType("HighlightTexts", All)!;
            var texts = Activator.CreateInstance(type, text, true, text, true)!;
            Invoke(app, "KeepHighlightTexts", key, texts);
        }

        static List<string> Order(JunctionApp app) => (List<string>)Field(app, "_highlightTextsOrder")!;
    }

    /// <summary>What a kept pair counts: two bytes a character and a line's array slot of eight on
    /// each side, and with both versions the nine a line of the new one that the walk keeps for the
    /// old one's to take; a version alone keeps no such line.</summary>
    [Fact]
    public void AKeptPair_CountsWhatTheWalkKeepsALine()
    {
        string old = "a\nbb\n", @new = "a\nbb\nccc\n";   // 3 and 4 lines, the empty one after the last break counted

        Assert.Equal(2 * 5 + 8 * 3 + 2 * 9 + (8 + 9) * 4, Bytes(old, @new));
        Assert.Equal(2 * 9 + 8 * 4, Bytes(null, @new));
        Assert.Equal(2 * 5 + 8 * 3, Bytes(old, null));

        static long Bytes(string? old, string? @new)
        {
            var type = typeof(JunctionApp).GetNestedType("HighlightTexts", All)!;
            var texts = Activator.CreateInstance(type, old, old is not null, @new, @new is not null)!;
            return (long)type.GetProperty("Bytes")!.GetValue(texts)!;
        }
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private const BindingFlags All = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    /// <summary>Frames for <paramref name="ms"/> of the real clock, which the idle collection waits on.</summary>
    private static void Rest(HeadlessSession session, int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { session.PumpFrame(); Thread.Sleep(5); }
    }

    /// <summary>The reader stays on the file a moment, then a key takes them to the next one,
    /// which cancels its walk; until a walk is kept with its texts (a busy machine may still be
    /// reading at the key, and then only the texts are kept), and the next file's colours have
    /// landed.</summary>
    private static void LeavePartWay(HeadlessSession session, JunctionApp app, string key, int read)
    {
        for (int tries = 0; ; tries++)
        {
            Assert.True(SpinWait.SpinUntil(() => app.HighlightsRead > read, TimeSpan.FromSeconds(30)),
                "the tokenize never got past its rest");
            Thread.Sleep(300);   // no frame runs, so nothing cancels it

            // To Small.cs, uncoloured: a key to a file whose colours are known leaves the walk going.
            Highlights(app).Clear();
            session.Window.PressKey(KeyCode.Down);
            session.PumpUntil(() => app.HighlightsPending == 0 && ShownIsCached(app) && Field(app, "_highlightRunning") is null,
                TimeSpan.FromSeconds(30));
            Assert.False(Highlights(app).Contains(key), "the whole file was coloured before the key");
            Assert.True(KeptTexts(app).Contains(key), "the texts of the file left part-way are not kept");
            if (WalkKept(app, key)) return;

            Assert.True(tries < 5, "no walk was kept in six visits");
            read = app.HighlightsRead;
            session.Window.PressKey(KeyCode.Up);
            session.PumpFrame();
        }
    }

    /// <summary>Whether SyntaxHighlighter keeps a walk with the file's kept texts: it does once the
    /// walk has done a line.</summary>
    private static bool WalkKept(JunctionApp app, string key)
    {
        var texts = KeptTexts(app)[key]!;
        string held = (string)texts.GetType().GetProperty("New")!.GetValue(texts)!;
        var walks = typeof(SyntaxHighlighter).GetField("DiffWalks", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        object?[] args = [held, null];
        return (bool)walks.GetType().GetMethod("TryGetValue")!.Invoke(walks, args)!;
    }

    /// <summary>What one walk of fresh copies gives for the lines <paramref name="highlight"/>
    /// shows, on a clock that stands still, as Junction's does in a headless session.</summary>
    private static TokenizedDiff Whole(TempRepo repo, object highlight)
    {
        var shown = highlight.GetType().GetProperty("Shown")!.GetValue(highlight)!;
        var oldShown = (LineRange[])shown.GetType().GetProperty("Old")!.GetValue(shown)!;
        var newShown = (LineRange[])shown.GetType().GetProperty("New")!.GetValue(shown)!;
        return SyntaxHighlighter.TokenizeDiff("src/Huge.cs", repo.Repo.GetFileText("HEAD^", "src/Huge.cs"), oldShown,
            repo.Repo.GetFileText("HEAD", "src/Huge.cs"), newShown, SyntaxHighlighter.DefaultMaxLines, default,
            new StandingClock());
    }

    private static void Same(TokenizedDiff whole, object highlight)
    {
        var old = (IReadOnlyList<TextSpan>[])highlight.GetType().GetProperty("OldLines")!.GetValue(highlight)!;
        var @new = (IReadOnlyList<TextSpan>[])highlight.GetType().GetProperty("NewLines")!.GetValue(highlight)!;
        Assert.Equal(whole.Old!.Length, old.Length);
        Assert.Equal(whole.New!.Length, @new.Length);
        for (int i = 0; i < old.Length; i++) Assert.Equal(whole.Old[i], old[i]);
        for (int i = 0; i < @new.Length; i++) Assert.Equal(whole.New[i], @new[i]);
        Assert.NotEmpty(@new[ChangedLine - 1]);
    }

    private sealed class StandingClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }

    private static object? Field(JunctionApp app, string name) => typeof(JunctionApp).GetField(name, All)!.GetValue(app);

    private static IDictionary Highlights(JunctionApp app) => (IDictionary)Field(app, "_highlights")!;

    private static IDictionary KeptTexts(JunctionApp app) => (IDictionary)Field(app, "_highlightTexts")!;

    private static bool ShownIsCached(JunctionApp app) =>
        Field(app, "_highlightShown") is string key && Highlights(app).Contains(key);

    private static void Invoke(JunctionApp app, string method, params object?[] args) =>
        typeof(JunctionApp).GetMethod(method, All)!.Invoke(app, args);

    /// <summary>The Commit tab, open on <paramref name="path"/>, its colours on their way; what
    /// <see cref="JunctionApp.HighlightsRead"/> was before they were asked for.</summary>
    private static int Open(HeadlessSession session, JunctionApp app, string path)
    {
        session.Window.PressKey(KeyCode.Digit3);
        session.PumpUntil(() => Detail(app) > 0, TimeSpan.FromSeconds(30));
        // What PickInitialFile asked for (A.cs) is not this test's: let it go.
        session.PumpUntil(() => app.HighlightsPending == 0, TimeSpan.FromSeconds(30));
        Highlights(app).Clear();
        int read = app.HighlightsRead;
        typeof(JunctionApp).GetField("_selectedFilePath", All)!.SetValue(app, path);
        typeof(JunctionApp).GetField("_commitCursorPath", All)!.SetValue(app, path);
        Invoke(app, "RerenderView");
        session.PumpFrame();
        return read;
    }

    private static int Detail(JunctionApp app)
    {
        var loader = Field(app, "_commitDetail")!;
        var value = loader.GetType().GetProperty("Value")!.GetValue(loader)!;
        return ((ICollection)value.GetType().GetProperty("Files")!.GetValue(value)!).Count;
    }

    private static void Hosted(Action<HeadlessSession, JunctionApp, TempRepo> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        // Code a walk takes a while over, with a block comment open across the change: its colour
        // needs the lines above it.
        string Huge(string changed) => string.Concat(Enumerable.Range(1, FileLines).Select(i =>
            (i == ChangedLine ? changed
                : i == ChangedLine - 3 ? "    /* opens"
                : i == ChangedLine + 2 ? "       closes */"
                : $"    public int Value{i}(string name) => name.Length + {i}; // the quick brown fox") + "\n"));
        repo.Write("src/A.cs", "class A { int x = 1; }\n");
        repo.Write("src/Huge.cs", Huge("    int Changed = 1;"));
        repo.Write("src/Small.cs", "class Small { int x = 1; }\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "the sources");
        repo.Write("src/A.cs", "class A { int x = 2; }\n");
        repo.Write("src/Huge.cs", Huge("    int Changed = 2;"));
        repo.Write("src/Small.cs", "class Small { int x = 2; }\n");
        repo.Git("commit", "-am", "change them");

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
            body(session, app, repo);
        }
        finally { app?.Dispose(); }
    }
}
