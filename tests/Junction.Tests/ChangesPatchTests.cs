using System.Collections;
using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The Changes view's load reads each side of the working tree whole only below
/// <see cref="JunctionApp.WholeChangesChars"/>; a longer one a file at a time — the file the view shows with the
/// load, each other file when it is first shown, the files beside the shown one ahead of the keys. The load runs at
/// every start and repository switch, Changes shown or not, and it parsed and kept every changed line of the
/// working tree: a deleted folder of 458,215 lines was 256 MB allocated at start and 86.7 MB of heap at rest.
///
/// <para>The fixture's working tree has a staged rename with an edit and a staged modification; and unstaged, a
/// file with two hunks, a deletion, a binary, a name with pattern characters in it, a file git status calls
/// modified and git diff has nothing for ("No textual changes"), and an untracked file. The view has to look and
/// stage the same either way.</para>
/// </summary>
[Collection("JunctionDiff")]
public class ChangesPatchTests
{
    private const BindingFlags All = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    private static readonly string[] ALines = Enumerable.Range(1, 40).Select(i => $"line {i} of a").ToArray();

    private static TempRepo Fixture() => TempRepo.CreateFrom("changes-patches", () =>
    {
        var repo = TempRepo.CreateWithInitialCommit();
        repo.Write(".gitattributes", "* text=auto\n");
        repo.Write("a.txt", string.Concat(ALines.Select(l => l + "\n")));
        repo.Write("old/b.txt", string.Concat(Enumerable.Range(0, 30).Select(i => $"line {i} of b\n")));
        repo.Write("c.cs", string.Concat(Enumerable.Range(0, 40).Select(i => $"class C{i} {{ int F() => {i}; }}\n")));
        repo.Write("d.txt", "to be deleted\n");
        repo.WriteBytes("e.bin", [0, 1, 2, 3, 0, 5]);
        repo.Write("f[1].txt", "first\n");
        repo.Write(Phantom, "a\nb\nc\nd\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "the files");

        // Staged: a rename with an edit, a modification.
        repo.Git("mv", "old/b.txt", "b.txt");
        repo.Write("b.txt", string.Concat(Enumerable.Range(0, 30).Select(i => i == 7 ? "line seven, edited\n" : $"line {i} of b\n")));
        repo.Write("c.cs", string.Concat(Enumerable.Range(0, 40).Select(i => i == 5 ? "class C5 { int F() => 55; }\n" : $"class C{i} {{ int F() => {i}; }}\n")));
        repo.Git("add", "-A");

        // Unstaged: two hunks, a deletion, a binary, pattern characters, an untracked file longer than the limit
        // the hosted tests set.
        repo.Write("a.txt", string.Concat(AWorking().Select(l => l + "\n")));
        File.Delete(Path.Combine(repo.Path, "d.txt"));
        repo.WriteBytes("e.bin", [0, 1, 2, 9, 0, 5, 6]);
        repo.Write("f[1].txt", "first, edited\n");
        repo.Write("u.txt", string.Concat(Enumerable.Range(0, 8).Select(i => $"untracked line {i}\n")));

        // The file status calls modified and diff does not (StatusOnlyChangeTests): committed with LF, checked out
        // as CRLF so the index caches the longer size, then written back with the LF bytes.
        repo.Git("config", "core.eol", "crlf");
        File.Delete(Path.Combine(repo.Path, Phantom));
        repo.Git("checkout", "--", Phantom);
        repo.Write(Phantom, "a\nb\nc\nd\n");
        return repo;
    });

    private const string Phantom = "crlf.txt";

    /// <summary>a.txt in the working tree: two lines added after line 3, one after line 35 (two hunks).</summary>
    private static IEnumerable<string> AWorking()
    {
        for (int i = 0; i < ALines.Length; i++)
        {
            yield return ALines[i];
            if (i == 2) { yield return "first added"; yield return "second added"; }
            if (i == 34) yield return "added after 35";
        }
    }

    private static readonly (string Path, bool Staged)[] Files =
    [
        ("b.txt", true), ("c.cs", true),
        ("a.txt", false), (Phantom, false), ("d.txt", false), ("e.bin", false), ("f[1].txt", false), ("u.txt", false),
    ];

    // ── The repository's two reads ───────────────────────────────────────

    /// <summary>Every file's diff read on its own is the one in the whole diff of its side — the staged rename
    /// only because the old path is asked for too, the pattern characters only because the paths are literal.</summary>
    [Fact]
    public void EachFilesDiffOnItsOwn_IsTheOneInTheWholeDiff()
    {
        using var repo = Fixture();
        foreach (bool staged in new[] { true, false })
        {
            var whole = repo.Repo.GetWorkingDiff(staged);
            var files = staged ? repo.Repo.GetStagedChanges() : repo.Repo.GetUnstagedChanges();
            if (staged) Assert.Contains(files, f => f.Kind == FileChangeKind.Renamed && f.OldPath == "old/b.txt");
            foreach (var f in files.Where(f => f.Kind != FileChangeKind.Untracked))
            {
                var alone = repo.Repo.GetWorkingFileDiff(staged, f.Path, f.OldPath);
                if (f.Path == Phantom)
                {
                    Assert.Empty(alone);   // nothing in the whole diff either
                    Assert.DoesNotContain(whole, d => d.Path == Phantom);
                    continue;
                }
                Assert.Equal(Describe(whole.Single(d => d.Path == f.Path)), Describe(Assert.Single(alone)));
            }
        }
    }

    /// <summary>Below the limit a side is read whole, as it always was; above it, only the files whose diff ended
    /// inside what was read are kept, and git is stopped there.</summary>
    [Fact]
    public void ALongDiff_KeepsTheFilesThatEndedInsideWhatWasRead()
    {
        using var repo = Fixture();
        var all = repo.Repo.GetWorkingDiff(staged: false);
        var (diffs, whole) = repo.Repo.GetWorkingDiff(staged: false, ignoreWhitespace: false, maxChars: 1 << 20);
        Assert.True(whole);
        Assert.Equal(all.Select(Describe), diffs.Select(Describe));

        string patch = GitProcess.Run(repo.Path, "diff").Stdout;
        int second = patch.IndexOf("\ndiff --git ", 1, StringComparison.Ordinal) + 1;
        int third = patch.IndexOf("\ndiff --git ", second, StringComparison.Ordinal) + 1;
        Assert.True(second > 0 && third > second);
        Assert.Equal(third, GitRepository.FilePatchesLength(patch.AsSpan(0, third + 64)));

        var (two, whole2) = repo.Repo.GetWorkingDiff(staged: false, ignoreWhitespace: false, maxChars: third + 64);
        Assert.False(whole2);
        Assert.Equal(all.Take(2).Select(Describe), two.Select(Describe));

        // A first file longer than what was read: nothing is kept.
        var (none, _) = repo.Repo.GetWorkingDiff(staged: false, ignoreWhitespace: false, maxChars: second - 1);
        Assert.Empty(none);
    }

    // ── The start's preload and the Changes view ─────────────────────────

    /// <summary>A working tree below the limit loads as before: every listed file's diff with the load (an empty
    /// one for the file git has no diff for), and nothing read on its own.</summary>
    [Fact]
    public void ASmallWorkingTree_LoadsWhole_AtStart()
    {
        Hosted(null, (session, app) =>
        {
            session.PumpUntil(() => Loaded(app), TimeSpan.FromSeconds(30));
            Assert.Equal(JunctionView.History, app.View);
            Assert.Null(Property(Changes(app), "Patches"));
            Assert.Equal(["b.txt", "c.cs"], Keys(app, "StagedDiffs"));
            Assert.Equal(["a.txt", Phantom, "d.txt", "e.bin", "f[1].txt", "u.txt"], Keys(app, "UnstagedDiffs"));
            Assert.Equal(0, app.ChangePatchLoads);
        });
    }

    /// <summary>
    /// Above the limit the start's preload keeps the lists and the diff of the one file Changes would open on (the
    /// first staged file) and parses no other file's diff, untracked files included; it reads that file on its own.
    /// </summary>
    [Fact]
    public void ABigWorkingTree_ThePreloadKeepsNoDiffOfFilesNotShown()
    {
        Hosted(64, (session, app) =>
        {
            session.PumpUntil(() => Loaded(app), TimeSpan.FromSeconds(30));
            Assert.Equal(JunctionView.History, app.View);
            var data = Changes(app);
            Assert.NotNull(Property(data, "Patches"));
            Assert.Equal(["b.txt", "c.cs"], ((IEnumerable<GitFileChange>)Property(data, "Staged")!).Select(f => f.Path));
            Assert.Equal(6, ((IEnumerable<GitFileChange>)Property(data, "Unstaged")!).Count());
            Assert.Equal(["b.txt"], Keys(app, "StagedDiffs"));
            Assert.Empty(Keys(app, "UnstagedDiffs"));
            Assert.Equal(9, ParsedLines(app));   // b.txt: its hunk header, 3 + 3 lines of context, - and +
            Assert.Equal(1, app.ChangePatchLoads);
        });
    }

    /// <summary>
    /// A walk through the files with the arrow keys: the file beside the shown one is read ahead, so each key draws
    /// the new file's diff on its own frame — no "Loading…" — and nothing is read on a key. The untracked file and
    /// the "No textual changes" row show as they do read whole.
    /// </summary>
    [Fact]
    public void AKeyWalk_FindsEachFileReadAhead()
    {
        Hosted(64, (session, app) =>
        {
            OpenChanges(session, app);
            session.PumpFrames(5);
            Assert.Equal(1, app.ChangeReadAheads);   // c.cs

            foreach (var (file, text) in new[]
                     {
                         ("c.cs", "int F() => 55"), ("a.txt", "second added"), (Phantom, "No textual changes"),
                         ("d.txt", "to be deleted"), ("e.bin", "e.bin"), ("f[1].txt", "first, edited"),
                         ("u.txt", "untracked line 7"),
                     })
            {
                session.Window.PressKey(KeyCode.Down);   // the key's own frame
                Assert.Equal(file, Field(app, "_changeSelPath"));
                Assert.Equal(file, PaneLabel(session));
                Assert.False(Shows(session, "Loading…"), $"{file}: the pane waited");
                session.PumpFrame();
                Assert.True(Shows(session, text), $"{file}'s diff is not on screen");
                // Nothing read on a key: the file the view opened on, and the files read ahead.
                Assert.Equal(1 + app.ChangeReadAheads, app.ChangePatchLoads);
                session.PumpFrames(5);
            }
            Assert.Equal(7, app.ChangeReadAheads);   // each file after b.txt, once
        });
    }

    /// <summary>The pane looks the same read whole or a file at a time, on a file's first visit and on its second
    /// (which reads nothing): the staged rename, a file in colour, two hunks, the "No textual changes" row, a
    /// deletion, a binary and the untracked file, pixel for pixel.</summary>
    [Fact]
    public void ThePaneLooksTheSame_ReadWholeOrAFileAtATime()
    {
        var whole = new List<byte[]>();
        Hosted(null, (session, app) =>
        {
            OpenChanges(session, app);
            foreach (var (path, staged) in Files) whole.Add(PaneAt(session, app, path, staged));
        });
        Hosted(64, (session, app) =>
        {
            OpenChanges(session, app);
            for (int i = 0; i < Files.Length; i++)
                Assert.True(whole[i].AsSpan().SequenceEqual(PaneAt(session, app, Files[i].Path, Files[i].Staged)),
                    $"{Files[i].Path} differs on its first visit");
            int loads = app.ChangePatchLoads;
            for (int i = 0; i < Files.Length; i++)
                Assert.True(whole[i].AsSpan().SequenceEqual(PaneAt(session, app, Files[i].Path, Files[i].Staged)),
                    $"{Files[i].Path} differs on its second visit");
            Assert.Equal(loads, app.ChangePatchLoads);   // a second visit reads nothing
        });
    }

    /// <summary>Staging a line, then a hunk, from the shown diff stages the same lines read whole or a file at a
    /// time: the reload behind each lands with the shown file's diff.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(64)]
    public void StagingALineThenAHunk_StagesTheSameLines(int? wholeChangesChars)
    {
        Hosted(wholeChangesChars, (session, app, repo) =>
        {
            OpenChanges(session, app);
            Show(session, app, "a.txt", staged: false);
            var diff = ShownDiff(app)!;
            Assert.Equal(2, diff.Hunks.Count);
            Stage(app, diff, [diff.Hunks[0].Lines.Single(l => l.Content == "first added")]);
            session.PumpUntil(() => Index(repo).Contains("first added", StringComparison.Ordinal)
                && ShownDiff(app) is { } d && !ReferenceEquals(d, diff) && d.Hunks.Count == 2, TimeSpan.FromSeconds(30));

            var after = ShownDiff(app)!;
            Assert.Equal("a.txt", PaneLabel(session));
            Assert.DoesNotContain(after.Hunks[0].Lines, l => l.Content == "first added" && l.Kind == DiffLineKind.Addition);
            Stage(app, after, after.Hunks[1].Lines.Where(l => l.Kind is DiffLineKind.Addition or DiffLineKind.Deletion).ToList());
            session.PumpUntil(() => Index(repo).Contains("added after 35", StringComparison.Ordinal)
                && ShownDiff(app) is { } d && !ReferenceEquals(d, after) && d.Hunks.Count == 1, TimeSpan.FromSeconds(30));

            var expected = AWorking().Where(l => l != "second added");
            Assert.Equal(string.Concat(expected.Select(l => l + "\n")), Index(repo));
            Assert.Equal(["second added"], ShownDiff(app)!.Hunks.SelectMany(h => h.Lines)
                .Where(l => l.Kind == DiffLineKind.Addition).Select(l => l.Content));
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private static string Describe(GitFileDiff d) =>
        string.Join('\n', new[]
        {
            $"{d.Path}|{d.OldPath}|{d.RenamedFrom}|{d.OldPathHeader}|{d.NewPathHeader}|{d.Binary}",
        }.Concat(d.Hunks.SelectMany(h => h.Lines.Select(l => $"{h.Header}|{l}"))));

    private static object? Field(JunctionApp app, string name) => typeof(JunctionApp).GetField(name, All)!.GetValue(app);

    private static object? Property(object o, string name) => o.GetType().GetProperty(name)!.GetValue(o);

    private static object Loader(JunctionApp app)
    {
        var loaders = Field(app, "_changesLoaders")!;
        return loaders.GetType().GetMethod("For")!.Invoke(loaders, [Field(app, "_activeRepoPath")])!;
    }

    /// <summary>The working tree as the active repository's load last landed with it.</summary>
    private static object Changes(JunctionApp app) => Property(Loader(app), "Value")!;

    private static bool Loaded(JunctionApp app) =>
        ((IEnumerable<GitFileChange>)Property(Changes(app), "Staged")!).Any();

    private static string[] Keys(JunctionApp app, string side) =>
        ((IDictionary)Property(Changes(app), side)!).Keys.Cast<string>().Order(StringComparer.Ordinal).ToArray();

    private static int ParsedLines(JunctionApp app) =>
        new[] { "StagedDiffs", "UnstagedDiffs" }.Sum(side =>
            ((IDictionary)Property(Changes(app), side)!).Values.Cast<GitFileDiff>().Sum(d => d.Hunks.Sum(h => h.Lines.Count)));

    private static string Index(TempRepo repo) => repo.Git("show", ":a.txt").Stdout;

    /// <summary>The diff the Changes view's pane shows.</summary>
    private static GitFileDiff? ShownDiff(JunctionApp app)
    {
        var target = typeof(JunctionApp).GetMethod("CurrentDiffTarget", All)!.Invoke(app, null)!;
        return (GitFileDiff?)target.GetType().GetField("Item2")!.GetValue(target);
    }

    /// <summary>What the action bar's Stage button does with the picked lines.</summary>
    private static void Stage(JunctionApp app, GitFileDiff diff, IReadOnlyCollection<GitDiffLine> lines) =>
        typeof(JunctionApp).GetMethod("ApplyStaging", All)!.Invoke(app, [diff, StageOp.Stage, lines]);

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

    private static bool Shows(HeadlessSession session, string text) =>
        Descendants(Root(session)).OfType<TextElement>().Any(t => t.Content.Contains(text, StringComparison.Ordinal));

    /// <summary>The Changes view, its load landed and its first file (the first staged one) on screen. Waits for the
    /// pane, not for the selection: the preload sets the selection before the view's first frame.</summary>
    private static void OpenChanges(HeadlessSession session, JunctionApp app)
    {
        session.Window.PressKey(KeyCode.Digit4);
        session.PumpUntil(() => PaneLabel(session) == "b.txt" && Shows(session, "line seven, edited"), TimeSpan.FromSeconds(30));
    }

    /// <summary>Selects one file row, as a click on it does, and waits for its diff and colours.</summary>
    private static void Show(HeadlessSession session, JunctionApp app, string path, bool staged)
    {
        var section = typeof(JunctionApp).GetNestedType("ChangeSection", All)!;
        var row = Activator.CreateInstance(typeof(JunctionApp).GetNestedType("ChangeRef", All)!,
            path, Enum.Parse(section, staged ? "Staged" : "Unstaged"), false);
        typeof(JunctionApp).GetMethod("SetChangeSelection", All)!.Invoke(app, [row]);
        typeof(JunctionApp).GetMethod("RerenderView", All)!.Invoke(app, null);
        session.PumpFrame();
        session.PumpUntil(() => PaneLabel(session) == path && !Shows(session, "Loading…") && app.HighlightsPending == 0,
            TimeSpan.FromSeconds(30));
        session.PumpFrames(3);
    }

    /// <summary>The diff pane's pixels with one file shown and every load and colour landed.</summary>
    private static byte[] PaneAt(HeadlessSession session, JunctionApp app, string path, bool staged)
    {
        Show(session, app, path, staged);
        var label = Descendants(Root(session)).OfType<TextElement>().Single(t => t.Key == "diff-path");
        Assert.Equal(path, label.Content);
        var box = label.Parent!.Parent!.WindowBorderBox;
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

    private static void Hosted(int? wholeChangesChars, Action<HeadlessSession, JunctionApp> body) =>
        Hosted(wholeChangesChars, (session, app, _) => body(session, app));

    private static void Hosted(int? wholeChangesChars, Action<HeadlessSession, JunctionApp, TempRepo> body)
    {
        using var repo = Fixture();
        // A copy's first status rewrites its index (every file's times are new): done here, not by the app
        // under its watcher, whose event could land mid-test as a reload.
        repo.Git("status");
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
            if (wholeChangesChars is { } chars) app.WholeChangesChars = chars;
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            // No reload the test does not make itself: a walk or a visit counts every file read, and a reload
            // reads the shown file again and starts the reading ahead over. Under a full run's load a watcher
            // event of the start (2026-11-24) landed mid-walk.
            session.PumpUntil(() => Loaded(app), TimeSpan.FromSeconds(30));
            foreach (var w in ((IDictionary)Field(app, "_watchers")!).Values) ((IDisposable)w!).Dispose();
            session.PumpFrames(3);
            body(session, app, repo);
        }
        finally { app?.Dispose(); }
    }
}
