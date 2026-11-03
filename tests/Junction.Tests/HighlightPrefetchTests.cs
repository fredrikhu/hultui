using System.Collections;
using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Rendering;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// A diff's colours are tokenized as far as the diff can show, and only while they are wanted.
///
/// <para>Every commit a reader passed in History had both whole files of its opening file
/// tokenized on a worker: 9,826 KB a key on all threads against 60 on the UI thread, most of
/// the walk's GC pause, and a core kept busy, for a diff nobody had opened. Each tokenize also
/// waited its turn behind the ones before it, for files long gone.</para>
/// </summary>
[Collection("JunctionDiff")]
public class HighlightPrefetchTests
{
    private const int FileLines = 3000;
    private const int FirstChange = 50;

    [Fact]
    public void ATokenize_WalksToTheLastLineTheDiffShows_AndTheLinesBothSidesShareOnce()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Big.cs");

            session.PumpUntil(() => Coloured(session, app, "    int Changed = 2;"), TimeSpan.FromSeconds(20));

            // Three lines of context after the change. Not 2 × 3,000.
            Assert.InRange(app.HighlightLinesTokenized, FirstChange, FirstChange + 12);
        });
    }

    [Fact]
    public void AFileThatOnlyGainedLines_HasNoOldSide()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Grown.cs");

            session.PumpUntil(() => Coloured(session, app, "class Grown2"), TimeSpan.FromSeconds(20));

            var highlight = Single(app);
            Assert.Null(highlight.GetType().GetProperty("OldLines")!.GetValue(highlight));
        });
    }

    /// <summary>All old side, no new one: the scene compare found these plain.</summary>
    [Fact]
    public void ADeletedFile_HasItsColours()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Gone.cs");

            session.PumpUntil(() => Coloured(session, app, "class Gone"), TimeSpan.FromSeconds(20));

            var highlight = Single(app);
            Assert.Null(highlight.GetType().GetProperty("NewLines")!.GetValue(highlight));
        });
    }

    /// <summary>A file renamed and changed in the commit: its old side is read under the old
    /// name, and the lines it lost have their colours. Read at the new name it found nothing, and
    /// they stayed plain.</summary>
    [Fact]
    public void ARenamedFile_HasItsDeletedLinesColoured()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Renamed.cs");

            session.PumpUntil(() => Coloured(session, app, "    string Label = \"new\";"), TimeSpan.FromSeconds(20));

            Assert.True(Coloured(session, app, "    string Name = \"old\";"), "the renamed file's deleted line is plain");
        });
    }

    /// <summary>A session's clock runs on with its frames while the worker tokenizes: the colours
    /// reach the end of the file however many seconds of it go by.</summary>
    [Fact]
    public void ATokenizeTheSessionsClockOutruns_ColoursToTheEnd()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Grown.cs");
            while (app.HighlightsPending > 0)
            {
                session.PumpFrame(TimeSpan.FromSeconds(5));
                Thread.Sleep(1);
            }

            var highlight = Single(app);
            var lines = (IReadOnlyList<TextSpan>[])highlight.GetType().GetProperty("NewLines")!.GetValue(highlight)!;
            Assert.NotEmpty(lines[^1]);
        });
    }

    /// <summary>A line inside a block comment opened far above the hunk takes the comment's
    /// colour, on both sides of the change.</summary>
    [Fact]
    public void ALineInACommentOpenedAboveTheHunk_HasTheCommentsColour()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Comment.cs");

            session.PumpUntil(() => Coloured(session, app, "   the new line"), TimeSpan.FromSeconds(20));

            var removed = Spans(session, "   the old line");
            var added = Spans(session, "   the new line");
            Assert.NotNull(removed);
            Assert.NotNull(added);
            Assert.Equal(added![0].Color, removed![0].Color);
        });
    }

    /// <summary>Lines a later diff of the same commit shows, that the first one did not: here the
    /// reader turns "ignore whitespace" off, and a change far down the file comes back. They get
    /// their colour; they do not stay plain because the commit's key has an entry.</summary>
    [Fact]
    public void ADiffShowingMoreLines_GetsThemColoured()
    {
        Hosted((session, app) =>
        {
            Invoke(app, "ToggleIgnoreWhitespace");
            session.PumpFrames(3);
            Open(session, app, "src/Spaced.cs");
            session.PumpUntil(() => Coloured(session, app, "    int First = 2;"), TimeSpan.FromSeconds(20));
            int before = app.HighlightsRead;

            Invoke(app, "ToggleIgnoreWhitespace");

            session.PumpUntil(() => Coloured(session, app, "       int Indented = 0;"), TimeSpan.FromSeconds(20));
            Assert.True(app.HighlightsRead > before);
        });
    }

    /// <summary>The same lines from another diff object (a reload) are the same colours.</summary>
    [Fact]
    public void AReloadedDiffOfTheSameLines_TokenizesNothing()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Big.cs");
            session.PumpUntil(() => Coloured(session, app, "    int Changed = 2;"), TimeSpan.FromSeconds(20));
            int read = app.HighlightsRead;

            Invoke(app, "RefreshAll");
            session.PumpFrames(30);

            Assert.Equal(read, app.HighlightsRead);
            Assert.True(Coloured(session, app, "    int Changed = 2;"));
        });
    }

    /// <summary>Keys faster than the rest read no file at all but the one the reader stops on:
    /// that is what a key held down in the log is.</summary>
    [Fact]
    public void ACommitPassedInTheLog_IsNotRead()
    {
        Hosted((session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit2);
            session.PumpUntil(() => Field(app, "_selectedCommitHash") != null, TimeSpan.FromSeconds(30));
            session.PumpUntil(() => app.HighlightsPending == 0 && Highlights(app).Count > 0, TimeSpan.FromSeconds(30));
            int read = app.HighlightsRead;
            app.HighlightRest = TimeSpan.FromSeconds(1);   // a key a frame, however busy the machine

            for (int i = 0; i < 4; i++)
            {
                session.Window.PressKey(i % 2 == 0 ? KeyCode.Down : KeyCode.Up);
                session.PumpFrames(2);
            }
            Assert.Equal(read, app.HighlightsRead);

            session.Window.PressKey(KeyCode.Down);
            session.PumpUntil(() => app.HighlightsRead > read, TimeSpan.FromSeconds(20));
            session.PumpUntil(() => app.HighlightsPending == 0, TimeSpan.FromSeconds(20));
            Assert.Equal(read + 1, app.HighlightsRead);
        });
    }

    /// <summary>A newer selection ends the rest of the one before at once, with the detail of
    /// the new commit still on its way: it may be slow, and nothing would stop the old one.</summary>
    [Fact]
    public void ASelectionMoved_DropsThePrefetchBeforeItsDetailLands()
    {
        Hosted((session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit2);
            session.PumpUntil(() => Field(app, "_selectedCommitHash") != null, TimeSpan.FromSeconds(30));
            session.PumpUntil(() => app.HighlightsPending == 0 && Highlights(app).Count > 0, TimeSpan.FromSeconds(30));
            app.HighlightRest = TimeSpan.FromSeconds(5);

            session.Window.PressKey(KeyCode.Down);
            session.PumpUntil(() => app.HighlightsPending == 1, TimeSpan.FromSeconds(20));

            session.Window.PressKey(KeyCode.Up);

            Assert.Equal(0, app.HighlightsPending);
        });
    }

    /// <summary>A key to another file cancels the tokenize of the one it left.</summary>
    [Fact]
    public void AKeyToAnotherFile_CancelsTheOneItLeft()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Grown.cs");   // walks 3,002 lines
            Assert.Equal(1, app.HighlightsPending);

            session.Window.PressKey(KeyCode.Up);

            Assert.NotEqual("src/Grown.cs", Field(app, "_selectedFilePath"));
            Assert.Equal(1, app.HighlightsPending);   // the one the key reached
        });
    }

    /// <summary>One that finished before the key that replaced it, its result still on the way
    /// to the UI thread, is kept: going back to that file costs nothing.</summary>
    [Fact]
    public void AResultThatFinishedBeforeTheKey_IsKept()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Big.cs");
            Thread.Sleep(1500);   // no frame runs: the result waits to be posted through
            var big = (string)Field(app, "_highlightShown")!;

            session.Window.PressKey(KeyCode.Down);
            session.PumpUntil(() => app.HighlightsPending == 0, TimeSpan.FromSeconds(20));

            Assert.True(Highlights(app).Contains(big));
        });
    }

    /// <summary>A file reached while another's tokenize is still going may be one more step of a
    /// walk: it waits for the reader to stay, and reads nothing until then.</summary>
    [Fact]
    public void AFileReachedDuringAnothersTokenize_WaitsForTheReaderToStay()
    {
        Hosted((session, app) =>
        {
            app.HighlightRest = TimeSpan.FromSeconds(1);
            Open(session, app, "src/Grown.cs");
            Thread.Sleep(300);   // no frame runs: it stays the running one, past its read
            int read = app.HighlightsRead;

            session.Window.PressKey(KeyCode.Up);
            session.PumpFrames(5);
            Assert.Equal(read, app.HighlightsRead);

            session.PumpUntil(() => app.HighlightsPending == 0, TimeSpan.FromSeconds(20));
            Assert.Equal(read + 1, app.HighlightsRead);
            Assert.True(ShownIsCached(app), "the file the reader stopped on has no colours");
        });
    }

    /// <summary>The reader opens the file a prefetch is resting on: the screen is waiting now.</summary>
    [Fact]
    public void TheCommitTabOpeningOnARestingPrefetch_StartsItAtOnce()
    {
        Hosted((session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit2);
            session.PumpUntil(() => Field(app, "_selectedCommitHash") != null, TimeSpan.FromSeconds(30));
            session.PumpUntil(() => app.HighlightsPending == 0 && Highlights(app).Count > 0, TimeSpan.FromSeconds(30));
            app.HighlightRest = TimeSpan.FromSeconds(30);
            int read = app.HighlightsRead;

            session.Window.PressKey(KeyCode.Down);
            session.PumpUntil(() => app.HighlightsPending == 1, TimeSpan.FromSeconds(20));
            Assert.Equal(read, app.HighlightsRead);

            session.Window.PressKey(KeyCode.Digit3);

            session.PumpUntil(() => app.HighlightsRead > read, TimeSpan.FromSeconds(10));
        });
    }

    /// <summary>However fast the files go by, one tokenize runs and one waits, the last asked
    /// for, and that is the one whose colours land.</summary>
    [Fact]
    public void AKeyHeldDownOverFiles_QueuesNothing_AndTheLastFileGetsItsColours()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Big.cs");

            for (int i = 0; i < 12; i++)
            {
                session.Window.PressKey(i % 8 < 4 ? KeyCode.Up : KeyCode.Down);
                session.PumpFrame();
                Assert.InRange(app.HighlightsPending, 0, 2);
            }
            var last = (string)Field(app, "_selectedFilePath")!;

            session.PumpUntil(() => app.HighlightsPending == 0, TimeSpan.FromSeconds(20));
            session.PumpFrames(2);

            Assert.Equal(last, Field(app, "_selectedFilePath"));
            Assert.True(ShownIsCached(app), "the file on screen has no colours");
        });
    }

    /// <summary>A prefetch does not take the worker from the diff on screen: it stays plain for
    /// good otherwise, since nothing asks again.</summary>
    [Fact]
    public void APrefetch_DoesNotCancelWhatTheDiffOnScreenWaitsFor()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Comment.cs");
            Assert.Equal(1, app.HighlightsPending);

            var detail = Field(app, "_commitDetail")!;
            var value = detail.GetType().GetProperty("Value")!.GetValue(detail)!;
            Pick(app, "src/Grown.cs");
            Invoke(app, "PrefetchHighlight", value);
            Pick(app, "src/Comment.cs");

            session.PumpUntil(() => app.HighlightsPending == 0, TimeSpan.FromSeconds(20));
            Assert.True(ShownIsCached(app), "the diff on screen was left without its colours");
        });
    }

    /// <summary>Nor does a prefetch take the place of what the diff on screen waits for next.</summary>
    [Fact]
    public void APrefetch_DoesNotReplaceWhatTheDiffOnScreenWaitsForNext()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Grown.cs");
            session.Window.PressKey(KeyCode.Up);   // running Grown, cancelled; waiting, the file above
            var shown = (string)Field(app, "_selectedFilePath")!;

            var detail = Field(app, "_commitDetail")!;
            var value = detail.GetType().GetProperty("Value")!.GetValue(detail)!;
            Pick(app, "src/Spaced.cs");
            Invoke(app, "PrefetchHighlight", value);
            Pick(app, shown);

            session.PumpUntil(() => app.HighlightsPending == 0, TimeSpan.FromSeconds(20));
            Assert.True(ShownIsCached(app), "the diff on screen was left without its colours");
        });
    }

    /// <summary>A tokenize already finished, its result on the way to the UI thread, when the
    /// scheme changes: it has the old palette and is not kept.</summary>
    [Fact]
    public void AResultLandingAfterASchemeChange_IsNotKept()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Big.cs");
            Thread.Sleep(1500);   // no frame runs: the result waits to be posted through

            Invoke(app, "RetokenizeCode");
            session.PumpFrames(2);

            Assert.Empty(Highlights(app));
        });
    }

    /// <summary>The scheme changes during a tokenize: what it made has the old palette and is
    /// not kept, and the diff gets the new one.</summary>
    [Fact]
    public void ASchemeChangeDuringATokenize_KeepsNoneOfIt()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/Big.cs");
            Assert.Equal(1, app.HighlightsPending);

            Invoke(app, "RetokenizeCode");
            Assert.Equal(0, app.HighlightsPending);
            Invoke(app, "RerenderView");
            session.PumpFrame();

            session.PumpUntil(() => Coloured(session, app, "    int Changed = 2;"), TimeSpan.FromSeconds(20));
        });
    }

    /// <summary>A file no grammar parses is never read.</summary>
    [Fact]
    public void AFileWithNoGrammar_ReadsNothing()
    {
        Hosted((session, app) =>
        {
            Open(session, app, "src/notes.unknowntext");
            int read = app.HighlightsRead;
            session.PumpFrames(5);

            Assert.Equal(0, app.HighlightsPending);
            Assert.Equal(read, app.HighlightsRead);
            Assert.Empty(Highlights(app));
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private const BindingFlags All = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    private static object? Field(JunctionApp app, string name) => typeof(JunctionApp).GetField(name, All)!.GetValue(app);

    private static IDictionary Highlights(JunctionApp app) => (IDictionary)Field(app, "_highlights")!;

    private static object Single(JunctionApp app)
    {
        var e = Highlights(app).Values.GetEnumerator();
        Assert.True(e.MoveNext());
        return e.Current!;
    }

    private static bool ShownIsCached(JunctionApp app) =>
        Field(app, "_highlightShown") is string key && Highlights(app).Contains(key);

    private static void Invoke(JunctionApp app, string method, params object?[] args) =>
        typeof(JunctionApp).GetMethod(method, All)!.Invoke(app, args);

    private static void Pick(JunctionApp app, string path)
    {
        typeof(JunctionApp).GetField("_selectedFilePath", All)!.SetValue(app, path);
        typeof(JunctionApp).GetField("_commitCursorPath", All)!.SetValue(app, path);
    }

    /// <summary>The Commit tab, open on <paramref name="path"/>, with nothing tokenized yet but
    /// that file's colours on their way.</summary>
    private static void Open(HeadlessSession session, JunctionApp app, string path)
    {
        session.Window.PressKey(KeyCode.Digit3);
        session.PumpUntil(() => Detail(app) > 0, TimeSpan.FromSeconds(30));
        // What PickInitialFile prefetched is not this test's: let it go.
        session.PumpUntil(() => app.HighlightsPending == 0, TimeSpan.FromSeconds(30));
        Highlights(app).Clear();
        typeof(JunctionApp).GetProperty(nameof(JunctionApp.HighlightLinesTokenized), All)!.SetValue(app, 0);
        Pick(app, path);
        Invoke(app, "RerenderView");
        session.PumpFrame();
    }

    private static int Detail(JunctionApp app)
    {
        var loader = Field(app, "_commitDetail")!;
        var value = loader.GetType().GetProperty("Value")!.GetValue(loader)!;
        return ((ICollection)value.GetType().GetProperty("Files")!.GetValue(value)!).Count;
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

    private static bool Coloured(HeadlessSession session, JunctionApp app, string code) =>
        Spans(session, code) is { Count: > 0 } spans && spans.Any(s => s.Color.A > 0);

    private static string Lines(int count, Func<int, string> line) =>
        string.Concat(Enumerable.Range(1, count).Select(i => line(i) + "\n"));

    private static void Hosted(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        string Big(string changed) => Lines(FileLines, i => i == FirstChange ? changed : $"    int Value{i} = {i};");
        string Grown(int classes) => Lines(FileLines, i => $"    int Value{i} = {i};") + Lines(classes, i => $"class Grown{i} {{ }}");
        string Comment(string line) => "/* opens\n" + Lines(400, i => $"   comment {i}") + line + "\n   closes */\nclass Comment { }\n";
        string Spaced(string first, string indent) =>
            first + "\n" + Lines(800, i => $"    int Value{i} = {i};") + indent + "int Indented = 0;\n" + Lines(20, i => $"    int After{i} = {i};");
        string Renamed(string line) => "class Shop\n{\n" + Lines(40, i => $"    int Kept{i} = {i};") + line + "\n}\n";

        repo.Write("src/Big.cs", Big("    int Changed = 1;"));
        repo.Write("src/Grown.cs", Grown(1));
        repo.Write("src/Comment.cs", Comment("   the old line"));
        repo.Write("src/Spaced.cs", Spaced("    int First = 1;", "    "));
        repo.Write("src/Gone.cs", "class Gone\n{\n    /* nothing */ int x = 1;\n}\n");
        repo.Write("src/Other1.cs", "class Other1 { }\n");
        repo.Write("src/Other2.cs", "class Other2 { }\n");
        repo.Write("src/notes.unknowntext", "plain\n");
        repo.Write("src/Before.cs", Renamed("    string Name = \"old\";"));
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "the sources");

        repo.Write("src/Big.cs", Big("    int Changed = 2;"));
        repo.Write("src/Grown.cs", Grown(2));
        repo.Write("src/Comment.cs", Comment("   the new line"));
        repo.Write("src/Spaced.cs", Spaced("    int First = 2;", "       "));
        repo.Write("src/Other1.cs", "class Other1 { int x; }\n");
        repo.Write("src/Other2.cs", "class Other2 { int x; }\n");
        repo.Write("src/notes.unknowntext", "still plain\n");
        repo.Git("rm", "-q", "src/Gone.cs");
        repo.Git("mv", "src/Before.cs", "src/Renamed.cs");
        repo.Write("src/Renamed.cs", Renamed("    string Label = \"new\";"));
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
            body(session, app);
        }
        finally { app?.Dispose(); }
    }
}
