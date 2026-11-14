using System.Collections;
using System.Reflection;
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

    /// <summary>A commit above it parses only the file the tab opens on: one file, its lines and no more.</summary>
    [Fact]
    public void ABigCommit_ParsesOnlyTheOpeningFile()
    {
        Hosted(64, (session, app) =>
        {
            OpenCommit(session, app);
            Assert.NotNull(Patches(app));
            Assert.Equal(["a.txt"], DiffPaths(app));
            Assert.Equal(5, ParsedLines(app));   // a.txt's hunk: its header and four lines
            Assert.Equal(0, app.FilePatchLoads);
        });
    }

    /// <summary>Another file is read when it is first opened, and not again when it is opened again.</summary>
    [Fact]
    public void AnotherFile_IsReadOnItsFirstVisit_AndNotOnItsSecond()
    {
        Hosted(64, (session, app) =>
        {
            OpenCommit(session, app);

            // The key's own frame: the file is open, its patch on its way (it lands a frame later).
            session.Window.PressKey(KeyCode.Down);   // b.txt
            Assert.Equal("b.txt", Field(app, "_selectedFilePath"));
            Assert.True(Shows(session, "Loading…"), "the pane does not say the file is on its way");
            Assert.False(Shows(session, "Select a file"));
            session.PumpUntil(() => app.FilePatchLoads == 1 && DiffCount(app) == 2, TimeSpan.FromSeconds(20));
            session.PumpFrame();
            Assert.True(Shows(session, "line seven, edited"), "b.txt's diff is not on screen");

            session.Window.PressKey(KeyCode.Down);   // c.cs
            session.PumpUntil(() => app.FilePatchLoads == 2 && Shows(session, "class C0 "), TimeSpan.FromSeconds(20));

            session.Window.PressKey(KeyCode.Up);     // b.txt again: on the key's own frame
            Assert.Equal("b.txt", Field(app, "_selectedFilePath"));
            Assert.False(Shows(session, "Loading…"), "a file already read was asked for again");
            session.PumpFrame();
            Assert.True(Shows(session, "line seven, edited"));
            session.PumpFrames(3);
            Assert.Equal(2, app.FilePatchLoads);
            Assert.Equal(3, DiffCount(app));
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
            for (int i = 0; i < files.Length; i++)
                Assert.True(whole[i].AsSpan().SequenceEqual(PaneAt(session, app, files[i])), $"{files[i]} differs on its second visit");
            Assert.Equal(2, app.FilePatchLoads);
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

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
            });
            if (wholePatchChars is { } chars) app.WholePatchChars = chars;
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            body(session, app);
        }
        finally { app?.Dispose(); }
    }
}
