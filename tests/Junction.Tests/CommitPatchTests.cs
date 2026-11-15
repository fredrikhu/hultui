using System.Collections;
using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// A commit's patch is read whole only below <see cref="JunctionApp.WholePatchChars"/>; a longer one a file at a
/// time — the opening file with the detail, each other file on its first visit in the Commit tab. The Commit tab
/// shows one file's diff, and reading and parsing a plan merge's whole patch for it was up to 87 MB allocated a
/// load and 32 MB kept, for every commit History's selection rested on.
///
/// <para>The fixture's last commit has one file of every kind the patch can hold: a modification, a rename
/// with an edit, an addition, a deletion, a binary, a name with pattern characters in it and a mode change.
/// Each file's patch read on its own has to be the one the whole patch has, and the pane has to look the
/// same either way.</para>
/// </summary>
[Collection("JunctionDiff")]
public class CommitPatchTests
{
    private const BindingFlags All = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    private static TempRepo Fixture() => TempRepo.CreateFrom("commit-patches", () =>
    {
        var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("a.txt", "one\ntwo\nthree\n");
        repo.Write("old/b.txt", string.Concat(Enumerable.Range(0, 30).Select(i => $"line {i} of b\n")));
        repo.Write("d.txt", "to be deleted\n");
        repo.WriteBytes("e.bin", [0, 1, 2, 3, 0, 5]);
        repo.Write("f[1].txt", "first\n");
        repo.Write("g.sh", "echo hi\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "the files");

        repo.Write("a.txt", "one\ntwo changed\nthree\n");
        repo.Git("mv", "old/b.txt", "b.txt");
        repo.Write("b.txt", string.Concat(Enumerable.Range(0, 30).Select(i => i == 7 ? "line seven, edited\n" : $"line {i} of b\n")));
        repo.Write("c.cs", string.Concat(Enumerable.Range(0, 40).Select(i => $"class C{i} {{ int F() => {i}; }}\n")));
        repo.Git("rm", "-q", "d.txt");
        repo.WriteBytes("e.bin", [0, 1, 2, 9, 0, 5, 6]);
        repo.Write("f[1].txt", "first, edited\n");
        repo.Git("add", "-A");
        repo.Git("update-index", "--chmod=+x", "g.sh");
        repo.Git("commit", "-m", "one of every kind");
        return repo;
    });

    // ── The repository's two reads ───────────────────────────────────────

    /// <summary>Every file's patch read on its own is the one in the whole patch — a rename only because
    /// the old path is asked for too, the pattern characters only because the paths are literal.</summary>
    [Fact]
    public void EachFilesPatchOnItsOwn_IsTheOneInTheWholePatch()
    {
        using var repo = Fixture();
        var whole = repo.Repo.GetCommitDiff(repo.Head);
        var files = repo.Repo.GetCommitFiles(repo.Head);
        Assert.Equal(7, files.Count);
        Assert.Contains(files, f => f.Kind == FileChangeKind.Renamed && f.OldPath == "old/b.txt");

        foreach (var f in files)
        {
            var alone = repo.Repo.GetCommitFileDiff(repo.Head, f.Path, f.OldPath);
            var expected = whole.Single(d => d.Path == f.Path);
            var actual = Assert.Single(alone);
            Assert.Equal(Describe(expected), Describe(actual));
        }
    }

    /// <summary>Below the limit the patch is read whole, as it always was.</summary>
    [Fact]
    public void AShortPatch_IsReadWhole()
    {
        using var repo = Fixture();
        var files = repo.Repo.GetCommitFiles(repo.Head);
        var (diffs, whole) = repo.Repo.GetCommitDiff(repo.Head, false, 1 << 20, files[0]);

        Assert.True(whole);
        Assert.Equal(repo.Repo.GetCommitDiff(repo.Head).Select(Describe), diffs.Select(Describe));
    }

    /// <summary>Above it, only the first file's patch is kept of what was read, when it ends inside it —
    /// the opening file is usually the first — and an opening file further on is read on its own.</summary>
    [Fact]
    public void ALongPatch_KeepsTheFirstFile_AndReadsAnotherOpeningFileOnItsOwn()
    {
        using var repo = Fixture();
        var files = repo.Repo.GetCommitFiles(repo.Head);
        var all = repo.Repo.GetCommitDiff(repo.Head);
        int firstLength = GitRepository.FirstFilePatchLength(
            GitProcess.Run(repo.Path, "diff-tree", "--no-commit-id", "-p", "-M", "-m", "--first-parent", "--root", repo.Head).Stdout);
        Assert.True(firstLength > 0);

        var (first, whole) = repo.Repo.GetCommitDiff(repo.Head, false, firstLength + 64, files[0]);
        Assert.False(whole);
        Assert.Equal(Describe(all[0]), Describe(Assert.Single(first)));

        var c = files.Single(f => f.Path == "c.cs");
        var (two, _) = repo.Repo.GetCommitDiff(repo.Head, false, firstLength + 64, c);
        Assert.Equal([all[0].Path, "c.cs"], two.Select(d => d.Path));
        Assert.Equal(Describe(all.Single(d => d.Path == "c.cs")), Describe(two[1]));

        // A first file longer than what was read: nothing of it is kept, the opening file is read alone.
        var (alone, _) = repo.Repo.GetCommitDiff(repo.Head, false, firstLength - 1, files[0]);
        Assert.Equal(Describe(all[0]), Describe(Assert.Single(alone)));
    }

    // ── The Commit tab ───────────────────────────────────────────────────

    /// <summary>A commit below the limit loads once, whole, as before: every file's patch with the detail.</summary>
    [Fact]
    public void ASmallCommit_LoadsOnce_Whole()
    {
        Hosted(null, (session, app) =>
        {
            OpenCommit(session, app);
            Assert.Equal(7, DiffCount(app));
            Assert.Null(Patches(app));
            Assert.Equal(0, app.FilePatchLoads);
        });
    }

    /// <summary>A commit above it parses only the file the tab opens on — its lines and no more — and the one
    /// file after it is then read ahead (the first file has none before it).</summary>
    [Fact]
    public void ABigCommit_ParsesOnlyTheOpeningFile()
    {
        Hosted(64, (session, app) =>
        {
            OpenCommit(session, app);
            Assert.NotNull(Patches(app));
            Assert.Equal(5, Diffs(app)["a.txt"] is GitFileDiff a ? a.Hunks.Sum(h => h.Lines.Count) : -1);   // header + 4
            session.PumpFrames(5);
            Assert.Equal(["a.txt", "b.txt"], DiffPaths(app));
            Assert.Equal(1, app.FilePatchLoads);
            Assert.Equal(1, app.FileReadAheads);
        });
    }

    /// <summary>
    /// Once another commit's detail lands, the one the Commit tab showed can be collected. The pane kept the last
    /// diff it drew together with its detail's whole <c>DiffByPath</c>, and held it until the tab's next build: back
    /// in History after a walk through a plan merge's files, its parsed diffs (6–12 MB) stayed for as long as the
    /// tab stayed closed.
    /// </summary>
    [Fact]
    public void AnotherCommitsDetail_LetsTheShownOneGo()
    {
        Hosted(64, (session, app) =>
        {
            OpenCommit(session, app);
            session.PumpFrames(5);
            var shown = Weakly(Diffs(app));

            session.Window.PressKey(KeyCode.Digit2);   // History
            session.PumpFrame();
            string parent = GitProcess.Run((string)Field(app, "_activeRepoPath")!, "rev-parse", "HEAD~1").Stdout.Trim();
            typeof(JunctionApp).GetMethod("SetCommitSelection", All)!.Invoke(app, [parent]);
            typeof(JunctionApp).GetMethod("Rerender", All, Type.EmptyTypes)!.Invoke(app, null);
            session.PumpUntil(() => Property(Detail(app), "Commit") is GitCommit c && c.Hash == parent, TimeSpan.FromSeconds(30));
            session.PumpFrames(3);

            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.False(shown.IsAlive, "the Commit tab's last detail is still held");
        });
    }

    /// <summary>A weak reference made in a frame of its own, so no local keeps the object alive.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference Weakly(object o) => new(o);

    /// <summary>
    /// A walk through the files with the arrow keys: the file beside the open one is read ahead, so each key
    /// draws the new file's diff on its own frame — no "Loading…", no earlier diff — and nothing is read on
    /// a key. Going back reads nothing either.
    /// </summary>
    [Fact]
    public void AKeyWalk_FindsEachFileReadAhead()
    {
        Hosted(64, (session, app) =>
        {
            OpenCommit(session, app);
            session.PumpFrames(5);
            Assert.Equal(1, app.FileReadAheads);   // b.txt

            foreach (var (key, file, text) in new[]
                     {
                         (KeyCode.Down, "b.txt", "line seven, edited"), (KeyCode.Down, "c.cs", "class C0 "),
                         (KeyCode.Down, "d.txt", "to be deleted"), (KeyCode.Up, "c.cs", "class C0 "),
                         (KeyCode.Up, "b.txt", "line seven, edited"),
                     })
            {
                int loads = app.FilePatchLoads;
                session.Window.PressKey(key);   // the key's own frame
                Assert.Equal(file, Field(app, "_selectedFilePath"));
                Assert.Equal(file, PaneLabel(session));
                Assert.False(Shows(session, "Loading…"), $"{file}: the pane waited");
                session.PumpFrame();
                Assert.True(Shows(session, text), $"{file}'s diff is not on screen");
                // What the key itself asked git for: nothing (a read ahead of the next file may follow).
                Assert.Equal(app.FileReadAheads, app.FilePatchLoads);
                Assert.True(app.FilePatchLoads <= loads + 1);
                session.PumpFrames(5);
            }
            Assert.Equal(4, app.FileReadAheads);   // b, c, d and e: one each, never twice
        });
    }

    /// <summary>
    /// A click on a file not read yet (not beside the open one) shows the new selection in the list at
    /// once and keeps the old diff on screen until the new one lands — never a "Loading…" frame between
    /// two diffs — and then shows the new diff and none of the old one.
    /// </summary>
    [Fact]
    public void AClickOnAFarFile_KeepsTheOldDiffUntilTheNewOneLands()
    {
        Hosted(64, (session, app) =>
        {
            OpenCommit(session, app);
            session.PumpFrames(5);
            int onDemand = app.FilePatchLoads - app.FileReadAheads;

            // The click's own build: the file is open, its patch asked for, the old diff still there.
            session.Window.Click(Descendants(Root(session)).First(e => e.Key == "f[1].txt"));
            Assert.Equal("f[1].txt", Field(app, "_selectedFilePath"));
            Assert.Equal("a.txt", PaneLabel(session));
            Assert.True(Shows(session, "two changed"), "the old diff left before the new one came");
            Assert.False(Shows(session, "Loading…"));
            Assert.False(Shows(session, "Select a file"));

            session.PumpUntil(() => PaneLabel(session) == "f[1].txt", TimeSpan.FromSeconds(20));
            session.PumpFrame();
            Assert.True(Descendants(Root(session)).Any(e => e.Key?.StartsWith("f[1].txt#s", StringComparison.Ordinal) == true),
                "the list does not show the selection");
            Assert.True(Shows(session, "first, edited"), "the new diff is not on screen");
            Assert.False(Shows(session, "two changed"), "the old diff stayed after the new one came");
            Assert.Equal(onDemand + 1, app.FilePatchLoads - app.FileReadAheads);   // the click's one read
        });
    }

    /// <summary>The pane looks the same read whole or a file at a time, on a file's first visit and on its
    /// second: the opening file, a rename and a file in colour, pixel for pixel.</summary>
    [Fact]
    public void ThePaneLooksTheSame_ReadWholeOrAFileAtATime()
    {
        string[] files = ["a.txt", "b.txt", "c.cs"];
        var whole = new List<byte[]>();
        Hosted(null, (session, app) =>
        {
            OpenCommit(session, app);
            foreach (var f in files) whole.Add(PaneAt(session, app, f));
        });
        Hosted(64, (session, app) =>
        {
            OpenCommit(session, app);
            for (int i = 0; i < files.Length; i++)
                Assert.True(whole[i].AsSpan().SequenceEqual(PaneAt(session, app, files[i])), $"{files[i]} differs on its first visit");
            int loads = app.FilePatchLoads;
            for (int i = 0; i < files.Length; i++)
                Assert.True(whole[i].AsSpan().SequenceEqual(PaneAt(session, app, files[i])), $"{files[i]} differs on its second visit");
            Assert.Equal(loads, app.FilePatchLoads);   // a second visit reads nothing
        });
    }

    /// <summary>
    /// Ctrl+D while the open file's patch is on its way decides on that file, not on the diff the pane keeps on
    /// screen meanwhile: a binary file opened from a text one is refused once its patch lands, never launched.
    /// (A click builds at once and asks for the patch; headless, the patch is read when the next frame starts,
    /// so a key straight after the click always meets it on its way, however fast git is.)
    /// </summary>
    [Fact]
    public void CtrlD_OnAFileStillLoading_RefusesItWhenItIsBinary()
    {
        Hosted(64, (session, app) =>
        {
            OpenCommit(session, app);
            session.PumpFrames(5);
            ClickRow(session, "e.bin");   // its build asks for e.bin's patch
            Assert.Equal("a.txt", PaneLabel(session));   // the text file is kept on screen

            session.Window.PressKey(KeyCode.D, KeyModifiers.Ctrl);
            Assert.False(Diffs(app).Contains("e.bin"), "the patch landed before the key");
            Assert.False(EditorStarted(session), "the editor was started before the file was known");
            Assert.False(Announces(session, Refused), "refused before the file was known");

            session.PumpUntil(() => PaneLabel(session) == "e.bin", TimeSpan.FromSeconds(20));
            session.PumpFrames(3);
            Assert.True(Announces(session, Refused), "the binary file was not refused");
            Assert.False(EditorStarted(session), "the editor was started for a binary file");
        });
    }

    /// <summary>
    /// The other way: a text file opened from a binary one is opened in the editor once its patch lands; the
    /// binary diff on screen meanwhile does not refuse it.
    /// </summary>
    [Fact]
    public void CtrlD_OnAFileStillLoading_OpensItWhenTheKeptOneIsBinary()
    {
        Hosted(64, (session, app) =>
        {
            OpenCommit(session, app);
            Click(app, "e.bin");
            session.PumpUntil(() => PaneLabel(session) == "e.bin", TimeSpan.FromSeconds(20));
            session.PumpUntil(() => Diffs(app).Contains("d.txt") && Diffs(app).Contains("f[1].txt"), TimeSpan.FromSeconds(20));
            session.PumpFrames(3);
            ClickRow(session, "c.cs");   // not read yet, and not beside e.bin
            Assert.Equal("e.bin", PaneLabel(session));   // the binary file is kept on screen

            session.Window.PressKey(KeyCode.D, KeyModifiers.Ctrl);
            Assert.False(Diffs(app).Contains("c.cs"), "the patch landed before the key");
            Assert.False(Announces(session, Refused), "refused for the binary file on screen");

            session.PumpUntil(() => PaneLabel(session) == "c.cs", TimeSpan.FromSeconds(20));
            session.PumpUntil(() => EditorStarted(session), TimeSpan.FromSeconds(20));
            Assert.False(Announces(session, Refused), "the text file was refused");
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    // Ctrl+D's editor here, and what the app says when it refuses a file and when it starts the editor (or fails to).
    private const string NoEditor = "junction-test-no-such-editor";
    private const string Refused = "there are no two texts";

    /// <summary>Whether Ctrl+D started the editor: the launch's own toast, or the failure to start it that
    /// replaces it.</summary>
    private static bool EditorStarted(HeadlessSession session) =>
        Announces(session, "Opening diff in the editor") || Announces(session, NoEditor);

    /// <summary>A click on a file row of the Commit tab's list: its handler builds at once, so a file not read
    /// yet is asked for and still on its way when this returns.</summary>
    private static void ClickRow(HeadlessSession session, string path) =>
        session.Window.Click(Descendants(Root(session)).First(e => e.Key == path));

    /// <summary>Whether the window says <paramref name="text"/> anywhere (a toast included).</summary>
    private static bool Announces(HeadlessSession session, string text)
    {
        static IEnumerable<AccessibilityNode> Walk(AccessibilityNode n) => n.Children.SelectMany(Walk).Prepend(n);
        return Walk(session.Window.AccessibilityTree).Any(n =>
            n.Name?.Contains(text, StringComparison.Ordinal) == true || n.Value?.Contains(text, StringComparison.Ordinal) == true);
    }

    private static string Describe(GitFileDiff d) =>
        string.Join('\n', new[]
        {
            $"{d.Path}|{d.OldPath}|{d.RenamedFrom}|{d.OldPathHeader}|{d.NewPathHeader}|{d.Binary}",
        }.Concat(d.Hunks.SelectMany(h => h.Lines.Select(l => $"{h.Header}|{l}"))));

    private static object? Field(JunctionApp app, string name) => typeof(JunctionApp).GetField(name, All)!.GetValue(app);

    private static object Detail(JunctionApp app)
    {
        var loader = Field(app, "_commitDetail")!;
        return loader.GetType().GetProperty("Value")!.GetValue(loader)!;
    }

    private static object? Property(object o, string name) => o.GetType().GetProperty(name)!.GetValue(o);

    private static IDictionary Diffs(JunctionApp app) => (IDictionary)Property(Detail(app), "DiffByPath")!;
    private static int DiffCount(JunctionApp app) => Diffs(app).Count;
    private static object? Patches(JunctionApp app) => Property(Detail(app), "Patches");
    private static string[] DiffPaths(JunctionApp app) => Diffs(app).Keys.Cast<string>().Order(StringComparer.Ordinal).ToArray();

    private static int ParsedLines(JunctionApp app) =>
        Diffs(app).Values.Cast<GitFileDiff>().Sum(d => d.Hunks.Sum(h => h.Lines.Count));

    private static IEnumerable<Element> Descendants(Element e)
    {
        yield return e;
        foreach (var c in e.Children)
            foreach (var d in Descendants(c))
                yield return d;
    }

    private static Element Root(HeadlessSession session) =>
        (Element)typeof(HultUI.Window).GetField("_root", All)!.GetValue(session.Window.Window)!;

    /// <summary>The path the diff pane's header shows.</summary>
    private static string? PaneLabel(HeadlessSession session) =>
        Descendants(Root(session)).OfType<TextElement>().SingleOrDefault(t => t.Key == "diff-path")?.Content;

    /// <summary>What a click on a file row does (FileTreeRow's handler).</summary>
    private static void Click(JunctionApp app, string path)
    {
        typeof(JunctionApp).GetField("_commitCursorPath", All)!.SetValue(app, path);
        typeof(JunctionApp).GetField("_selectedFilePath", All)!.SetValue(app, path);
        typeof(JunctionApp).GetMethod("Rerender", All, Type.EmptyTypes)!.Invoke(app, null);
    }

    private static bool Shows(HeadlessSession session, string text) =>
        Descendants(Root(session)).OfType<TextElement>().Any(t => t.Content.Contains(text, StringComparison.Ordinal));

    /// <summary>The Commit tab on the fixture's last commit, its detail loaded.</summary>
    private static void OpenCommit(HeadlessSession session, JunctionApp app)
    {
        session.Window.PressKey(KeyCode.Digit3);
        session.PumpUntil(() => Property(Detail(app), "Commit") != null && Field(app, "_selectedFilePath") is "a.txt",
            TimeSpan.FromSeconds(30));
        session.PumpUntil(() => Shows(session, "two changed"), TimeSpan.FromSeconds(30));
    }

    /// <summary>The diff pane's pixels with <paramref name="path"/> open (as a click opens it) and every
    /// load and colour landed.</summary>
    private static byte[] PaneAt(HeadlessSession session, JunctionApp app, string path)
    {
        typeof(JunctionApp).GetField("_selectedFilePath", All)!.SetValue(app, path);
        typeof(JunctionApp).GetField("_commitCursorPath", All)!.SetValue(app, path);
        typeof(JunctionApp).GetMethod("RerenderView", All)!.Invoke(app, null);
        session.PumpFrame();
        session.PumpUntil(() => Diffs(app).Contains(path) && app.HighlightsPending == 0, TimeSpan.FromSeconds(30));
        session.PumpFrames(3);
        var label = Descendants(Root(session)).OfType<TextElement>().Single(t => t.Key == "diff-path");
        Assert.Equal(path, label.Content);
        var pane = label.Parent!.Parent!;
        var box = pane.WindowBorderBox;
        using var image = session.Window.CapturePixels();
        using var bitmap = SkiaSharp.SKBitmap.FromImage(image);
        float scale = image.Width / 1440f;
        int x0 = (int)(box.X * scale), y0 = (int)(box.Y * scale), w = (int)(box.Width * scale), h = (int)(box.Height * scale);
        var bytes = new byte[w * h * 4];
        int k = 0;
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                var c = bitmap.GetPixel(x, y);
                bytes[k++] = c.Red; bytes[k++] = c.Green; bytes[k++] = c.Blue; bytes[k++] = c.Alpha;
            }
        return bytes;
    }

    private static void Hosted(int? wholePatchChars, Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = Fixture();
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
                // Ctrl+D's editor: a program that is not there, so a launch fails to start rather than open one.
                ExternalTools = new ExternalToolOptions { Editor = EditorPreset.Custom, EditorCommand = NoEditor },
            });
            if (wholePatchChars is { } chars) app.WholePatchChars = chars;
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            body(session, app);
        }
        finally { app?.Dispose(); }
    }
}
