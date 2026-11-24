using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Junction.Git;
using Junction.Theme;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The Files tree's and the Commit file tree's cursor is the list's current item. A key that moves
/// only the cursor — onto a folder, back onto the open file — moves it on the live list and builds
/// nothing: the list makes the row the cursor left and the one it reached. A key past the end,
/// which moves nothing, does nothing. A build made the whole view for each (plan 17 round 19: 53
/// KB and 44 elements a Files folder key, 73 KB and 64 a Commit one, 45–60 KB a key past the
/// end). What the window draws, the scroll, the open file's light, where the keyboard is and
/// what a reader is told are what the build gave.
/// </summary>
[Collection("JunctionDiff")]
public class TreeCursorLiveTests
{
    // ── Files tree ───────────────────────────────────────────────────────

    /// <summary>Onto a folder and back onto the open file: no build, the two rows made again, the
    /// cursor's tint on the folder and the open file's accent kept, then both on the file.</summary>
    [Fact]
    public void Files_AFolderStep_AndBackOntoTheOpenFile_BuildNothing()
    {
        HostedFiles((session, app) =>
        {
            Keys(session, KeyCode.End);   // b.txt, which opens it
            Keys(session, KeyCode.Up);    // a.txt, which opens it
            Assert.Equal("a.txt", Field<string?>(app, "_filesSelPath"));

            int builds = app.Builds, made = app.FileTreeRowsMade;
            Keys(session, KeyCode.Up);    // f39, a folder
            Assert.Equal(builds, app.Builds);
            Assert.Equal(2, app.FileTreeRowsMade - made);
            Assert.Equal("f39", Field<string?>(app, "_filesCursorPath"));
            Assert.Equal(Nocturne.Neutral800, FilesRow(session, "f39")!.BackgroundColor);
            Assert.Equal(Nocturne.Accent900, FilesRow(session, "a.txt")!.BackgroundColor);
            Assert.EndsWith("#c", FilesRow(session, "f39")!.Key);
            Assert.DoesNotContain("#c", FilesRow(session, "a.txt")!.Key);

            made = app.FileTreeRowsMade;
            Keys(session, KeyCode.Down);  // back onto a.txt, the open file
            Assert.Equal(builds, app.Builds);
            Assert.Equal(2, app.FileTreeRowsMade - made);
            Assert.Equal("a.txt", Field<string?>(app, "_filesSelPath"));
            Assert.EndsWith("#s#c", FilesRow(session, "a.txt")!.Key);
            Assert.DoesNotContain("#c", FilesRow(session, "f39")!.Key);
            Assert.Equal(FilesRowIndex(app, "a.txt"), FilesList(session).CurrentIndex);
        });
    }

    /// <summary>Up at the first row and Down at the last move nothing, and do nothing.</summary>
    [Fact]
    public void Files_AKeyPastEitherEnd_BuildsNothing()
    {
        HostedFiles((session, app) =>
        {
            Keys(session, KeyCode.Home);
            int builds = app.Builds, made = app.FileTreeRowsMade;
            Keys(session, KeyCode.Up);
            Keys(session, KeyCode.Up);
            Assert.Equal(builds, app.Builds);
            Assert.Equal(made, app.FileTreeRowsMade);

            Keys(session, KeyCode.End);   // b.txt, which opens it: a build
            builds = app.Builds; made = app.FileTreeRowsMade;
            Keys(session, KeyCode.Down);
            Keys(session, KeyCode.Down);
            Assert.Equal(builds, app.Builds);
            Assert.Equal(made, app.FileTreeRowsMade);
            Assert.Equal("b.txt", Field<string?>(app, "_filesCursorPath"));
        });
    }

    /// <summary>Down over forty folders past the list's bottom and Up back past its top: no key
    /// builds, each draws one frame, the list scrolls the least that shows the cursor's row, and
    /// after every key the frame and the scroll are what a full build for the key draws (the
    /// build's own scroll to the cursor, every row made again).</summary>
    [Fact]
    public void Files_AWalkOverFolders_ScrollsTheLeast_AndDrawsWhatAFullBuildDraws()
    {
        HostedFiles((session, app) =>
        {
            Keys(session, KeyCode.Home);
            var list = FilesList(session);
            Assert.True(list.Layout.AbsoluteContentBox.Height < 39 * 24f, "the walk has to pass the list's edge");
            int builds = app.Builds;
            float moved = 0;
            foreach (var (key, keys) in new[] { (KeyCode.Down, 39), (KeyCode.Up, 39) })
            {
                for (int k = 0; k < keys; k++)
                {
                    float before = list.ScrollOffsetY;
                    session.Window.PressKey(key);
                    int drawn = 0;
                    for (int f = 0; f < 6; f++) if (session.PumpFrame()) drawn++;
                    Assert.True(drawn == 1, $"{key} {k + 1}: {drawn} frames");
                    Assert.Equal(builds, app.Builds);
                    var cursor = Field<string?>(app, "_filesCursorPath")!;
                    AssertLeastScroll(list, FilesRow(session, cursor)!, before, key == KeyCode.Down, cursor);
                    moved += Math.Abs(list.ScrollOffsetY - before);

                    AssertAFullBuildDrawsTheSame(session, list, () => SetField(app, "_pendingFilesTreeScroll", true), app);
                    builds = app.Builds;
                }
            }
            Assert.True(moved > 200f, $"the list scrolled {moved} px in all");
            Assert.Equal("f00", Field<string?>(app, "_filesCursorPath"));
        });
    }

    /// <summary>The keys reach the tree from the search box too, which keeps the keyboard: a
    /// folder step leaves it on the same live field, as a build did.</summary>
    [Fact]
    public void Files_AFolderStepFromTheSearchBox_LeavesTheKeyboardThere()
    {
        HostedFiles((session, app) =>
        {
            Keys(session, KeyCode.Home);
            Keys(session, KeyCode.Slash);
            var field = session.Window.Window.FocusedElement;
            Assert.IsType<TextInputElement>(field);
            int builds = app.Builds;

            Keys(session, KeyCode.Down);
            Keys(session, KeyCode.Down);

            Assert.Equal(builds, app.Builds);
            Assert.Equal("f02", Field<string?>(app, "_filesCursorPath"));
            Assert.Same(field, session.Window.Window.FocusedElement);
            Assert.True(session.Window.Window.IsInTree(field!));
        });
    }

    /// <summary>The cursor set outside the keys — a tree row opened into blame — is the list's
    /// current item after the build that follows.</summary>
    [Fact]
    public void Files_ACursorSetForBlame_IsShownByTheBuild()
    {
        HostedFiles((session, app) =>
        {
            Keys(session, KeyCode.End);   // b.txt, on screen
            Keys(session, KeyCode.Up);    // a.txt, which opens it
            Assert.EndsWith("#s#c", FilesRow(session, "a.txt")!.Key);
            typeof(JunctionApp).GetMethod("BlameTreeFile", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(app, ["b.txt"]);
            session.PumpUntil(() => FilesRow(session, "b.txt") is { } r && r.Key!.EndsWith("#s#c", StringComparison.Ordinal),
                TimeSpan.FromSeconds(20));
            Assert.Equal(FilesRowIndex(app, "b.txt"), FilesList(session).CurrentIndex);
            Assert.DoesNotContain("#c", FilesRow(session, "a.txt")!.Key);
        });
    }

    // ── Commit file tree ─────────────────────────────────────────────────

    /// <summary>Onto a folder the open file keeps its accent (an item's state, not the cursor's)
    /// and the folder takes the cursor's tint; back onto the open file both are on it. Neither key
    /// builds; each makes the two rows.</summary>
    [Fact]
    public void Commit_AFolderStep_AndBackOntoTheOpenFile_BuildNothing()
    {
        HostedCommit((session, app) =>
        {
            Assert.Equal("d00/x.txt", Field<string?>(app, "_selectedFilePath"));
            int builds = app.Builds, made = app.CommitTreeRowsMade;

            Keys(session, KeyCode.Down);  // d01, a folder
            Assert.Equal(builds, app.Builds);
            Assert.Equal(2, app.CommitTreeRowsMade - made);
            Assert.Equal("d01", Field<string?>(app, "_commitCursorPath"));
            Assert.Equal(Nocturne.Accent900, CommitRow(session, "d00/x.txt")!.BackgroundColor);
            Assert.Equal(Nocturne.Neutral800, CommitRow(session, "d01")!.BackgroundColor);

            made = app.CommitTreeRowsMade;
            Keys(session, KeyCode.Up);    // back onto d00/x.txt, the open file
            Assert.Equal(builds, app.Builds);
            Assert.Equal(2, app.CommitTreeRowsMade - made);
            Assert.EndsWith("#s#c", CommitRow(session, "d00/x.txt")!.Key);
            Assert.DoesNotContain("#c", CommitRow(session, "d01")!.Key);
            Assert.Equal(CommitRowIndex(app, "d00/x.txt") + 1, CommitList(session).CurrentIndex);

            Keys(session, KeyCode.Down);
            Keys(session, KeyCode.Down);  // d01/x.txt, which opens it: a build
            Assert.Equal("d01/x.txt", Field<string?>(app, "_selectedFilePath"));
            Assert.True(app.Builds > builds);
        });
    }

    [Fact]
    public void Commit_AKeyPastEitherEnd_BuildsNothing()
    {
        HostedCommit((session, app) =>
        {
            Keys(session, KeyCode.Home);
            int builds = app.Builds, made = app.CommitTreeRowsMade;
            Keys(session, KeyCode.Up);
            Keys(session, KeyCode.Up);
            Assert.Equal(builds, app.Builds);
            Assert.Equal(made, app.CommitTreeRowsMade);

            Keys(session, KeyCode.End);   // the last file, which opens it: a build
            builds = app.Builds; made = app.CommitTreeRowsMade;
            Keys(session, KeyCode.Down);
            Keys(session, KeyCode.Down);
            Assert.Equal(builds, app.Builds);
            Assert.Equal(made, app.CommitTreeRowsMade);
        });
    }

    /// <summary>Down through thirty folders and their files past the list's bottom, and Up back:
    /// every key draws one frame and scrolls the least that shows the cursor's row; a folder key
    /// builds nothing; after every key the frame and the scroll are what a full build for it draws
    /// (the build's own scroll to the cursor, every row made again).</summary>
    [Fact]
    public void Commit_AWalk_ScrollsTheLeast_AndDrawsWhatAFullBuildDraws()
    {
        HostedCommit((session, app) =>
        {
            var rows = (List<TreeNode<GitFileChange>>)FieldObj(app, "_commitFileRows")!;
            Assert.Equal(60, rows.Count);
            Assert.True(rows[2].IsFolder && !rows[3].IsFolder, "a folder and its file a pair");
            var list = CommitList(session);
            float moved = 0;
            foreach (var (key, keys) in new[] { (KeyCode.Down, 58), (KeyCode.Up, 58) })
            {
                for (int k = 0; k < keys; k++)
                {
                    float before = list.ScrollOffsetY;
                    int builds = app.Builds;
                    session.Window.PressKey(key);
                    int drawn = 0;
                    for (int f = 0; f < 6; f++) if (session.PumpFrame()) drawn++;
                    Assert.True(drawn == 1, $"{key} {k + 1}: {drawn} frames");
                    var cursor = Field<string?>(app, "_commitCursorPath")!;
                    if (rows[CommitRowIndex(app, cursor)].IsFolder) Assert.Equal(builds, app.Builds);
                    AssertLeastScroll(list, CommitRow(session, cursor)!, before, key == KeyCode.Down, cursor);
                    moved += Math.Abs(list.ScrollOffsetY - before);

                    AssertAFullBuildDrawsTheSame(session, list, () => SetField(app, "_pendingScrollToSelected", true), app);
                }
            }
            Assert.True(moved > 200f, $"the list scrolled {moved} px in all");
        });
    }

    /// <summary>The cursor set outside the keys — a review's step to a thread in another file sets
    /// it with the open file and builds — is the list's current item after that build.</summary>
    [Fact]
    public void Commit_ACursorSetWithTheOpenFile_IsShownByTheBuild()
    {
        HostedCommit((session, app) =>
        {
            SetField(app, "_selectedFilePath", "d03/x.txt");
            SetField(app, "_commitCursorPath", "d03/x.txt");
            typeof(JunctionApp).GetMethod("Rerender", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);
            session.PumpFrames(3);
            Assert.Equal(CommitRowIndex(app, "d03/x.txt") + 1, CommitList(session).CurrentIndex);
            Assert.EndsWith("#s#c", CommitRow(session, "d03/x.txt")!.Key);
            Assert.DoesNotContain("#", CommitRow(session, "d00/x.txt")!.Key);
        });
    }

    // ── What a reader is told ────────────────────────────────────────────

    /// <summary>The rows are composite, so a reader is told of no current item, before the change
    /// or after: a folder step raises no focus event, and the bridge tells the two rows' swap as
    /// the build's patch did, in the key's frame — the same events in both trees, measured at the
    /// base and after.</summary>
    [Fact]
    public void AFolderStep_TellsAReaderWhatABuildTold()
    {
        if (!OperatingSystem.IsWindows()) return;
        // Files: folders from the top. Commit: from the open file onto a folder, back, and again.
        HostedFiles((session, app) =>
        {
            Keys(session, KeyCode.Home);
            AssertReaderHearsTheRowSwap(session, app, [KeyCode.Down, KeyCode.Down, KeyCode.Down]);
        });
        HostedCommit((session, app) => AssertReaderHearsTheRowSwap(session, app, [KeyCode.Down, KeyCode.Up, KeyCode.Down]));
    }

    private static void AssertReaderHearsTheRowSwap(HeadlessSession session, JunctionApp app, KeyCode[] keys)
    {
        using var reader = HeadlessUiaReader.Install(session.Window);
        for (int i = 0; i < 4; i++) session.PumpFrame(TimeSpan.FromMilliseconds(100));
        foreach (var key in keys)
        {
            reader.Clear();
            int builds = app.Builds;
            session.Window.PressKey(key);
            session.PumpFrame(TimeSpan.FromMilliseconds(16));
            var inFrame = reader.Raised.ToArray();
            for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(100));
            Assert.Empty(reader.FocusEvents());
            Assert.Equal(inFrame, reader.Raised.ToArray());
            Assert.Equal(
                new[] { (HeadlessUiaEventKind.Structure, 3), (HeadlessUiaEventKind.Structure, 4) },
                reader.Raised.Select(e => (e.Kind, e.Id)).ToArray());
            // Last: what is heard above is the base's too, where this key built the view.
            Assert.Equal(builds, app.Builds);
        }
    }

    // ── Checks ───────────────────────────────────────────────────────────

    /// <summary>The cursor's row is wholly in the list's view, and the scroll moved only if it had
    /// to, by the least: the row's far edge on the view's edge.</summary>
    private static void AssertLeastScroll(ScrollViewElement list, Element row, float before, bool down, string what)
    {
        var view = list.Layout.AbsoluteContentBox;
        var box = row.Layout.AbsoluteBorderBox;
        float top = box.Y - list.ScrollOffsetY, bottom = top + box.Height;
        Assert.True(top >= view.Y - 0.5f && bottom <= view.Bottom + 0.5f,
            $"{what}'s row at {top:0.#}-{bottom:0.#}, outside {view.Y:0.#}-{view.Bottom:0.#}");
        if (list.ScrollOffsetY == before) return;
        Assert.True(down ? list.ScrollOffsetY > before : list.ScrollOffsetY < before, $"{what}: scrolled the wrong way");
        if (down) Assert.Equal(view.Bottom, bottom, 0.5f);
        else Assert.Equal(view.Y, top, 0.5f);
    }

    /// <summary>The frame as the key left it, then a full build for it as the app made one before:
    /// the build's after-hook asked to scroll the cursor's row in, and every row made again from the
    /// template. Byte-identical, and the same scroll.</summary>
    private static void AssertAFullBuildDrawsTheSame(HeadlessSession session, ScrollViewElement list, Action askScroll, JunctionApp app)
    {
        byte[] keyed = Pixels(session);
        float offset = list.ScrollOffsetY;
        askScroll();
        if (list is VirtualListElement<JunctionApp.FileTreeRowItem> files) files.InvalidateRows();
        else ((VirtualListElement<JunctionApp.CommitTreeRowItem>)list).InvalidateRows();
        Rebuild(app);
        session.PumpFrames(3);
        Assert.Equal(offset, list.ScrollOffsetY);
        Assert.True(keyed.AsSpan().SequenceEqual(Pixels(session)), "the key's frame is not what a full build draws");
    }

    private static byte[] Pixels(HeadlessSession session)
    {
        using var image = session.Window.CapturePixels();
        using var pixels = image.PeekPixels();
        return pixels.GetPixelSpan().ToArray();
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    /// <summary>Forty closed folders f00–f39 (a file in each), then a.txt (open) and b.txt, at
    /// 1400 × 900: more rows than the list shows.</summary>
    private static void HostedFiles(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateEmpty();
        for (int i = 0; i < 40; i++) repo.Write($"f{i:00}/x.txt", $"{i}\n");
        repo.Write("a.txt", "a\n");
        repo.Write("b.txt", "b\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "a tree");
        Host(repo, session => session.Window.PressKey(KeyCode.Digit7), (session, app) =>
            app.View == JunctionView.Files && Field<List<TreeNode<string>>>(app, "_filesFlat").Count == 42
            && Field<string?>(app, "_filesSelPath") == "a.txt"
            && LayoutProbe.Descendants<VirtualListElement<JunctionApp.FileTreeRowItem>>(Root(session), _ => true).Any(), body);
    }

    /// <summary>A commit of thirty folders d00–d29 with a changed file in each: sixty rows, a
    /// folder and its file in turn, d00/x.txt open.</summary>
    private static void HostedCommit(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        for (int i = 0; i < 30; i++) repo.Write($"d{i:00}/x.txt", $"{i}\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "thirty folders");
        Host(repo, session => session.Window.PressKey(KeyCode.Digit3), (session, app) =>
            app.View == JunctionView.Commit && ((List<TreeNode<GitFileChange>>)FieldObj(app, "_commitFileRows")!).Count == 60
            && CommitRow(session, "d00/x.txt") is { } r && r.Layout.AbsoluteBorderBox.Height > 0, body);
    }

    private static void Host(TempRepo repo, Action<HeadlessSession> open, Func<HeadlessSession, JunctionApp, bool> ready,
        Action<HeadlessSession, JunctionApp> body)
    {
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
            open(session);
            session.PumpUntil(() => ready(session, app!), TimeSpan.FromSeconds(30));
            DiffScrollTests.Quiet(session, app);
            session.PumpFrames(3);
            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    private static void Keys(HeadlessSession session, KeyCode key)
    {
        session.Window.PressKey(key);
        session.PumpFrames(3);
    }

    private static void Rebuild(JunctionApp app)
    {
        for (var t = typeof(JunctionApp); t != null; t = t.BaseType)
            if (t.GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance) is { } m)
            { m.Invoke(app, null); return; }
        throw new Xunit.Sdk.XunitException("no MarkDirty to call");
    }

    private static object? FieldObj(JunctionApp app, string name) =>
        typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app);

    private static T Field<T>(JunctionApp app, string name) => (T)FieldObj(app, name)!;

    private static void SetField(JunctionApp app, string name, object? value) =>
        typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, value);

    private static int FilesRowIndex(JunctionApp app, string path) =>
        Field<List<TreeNode<string>>>(app, "_filesFlat").FindIndex(n => n.Path == path);

    private static int CommitRowIndex(JunctionApp app, string path) =>
        ((List<TreeNode<GitFileChange>>)FieldObj(app, "_commitFileRows")!).FindIndex(n => n.Path == path);

    private static VirtualListElement<JunctionApp.FileTreeRowItem> FilesList(HeadlessSession session) =>
        LayoutProbe.Descendants<VirtualListElement<JunctionApp.FileTreeRowItem>>(Root(session), _ => true).Single();

    private static VirtualListElement<JunctionApp.CommitTreeRowItem> CommitList(HeadlessSession session) =>
        LayoutProbe.Descendants<VirtualListElement<JunctionApp.CommitTreeRowItem>>(Root(session), _ => true).Single();

    /// <summary>The realized Files row of a path (its key is d: or f:, the path, then its states).</summary>
    private static BoxElement? FilesRow(HeadlessSession session, string path) =>
        FilesList(session).Children.Skip(1).OfType<BoxElement>()
            .FirstOrDefault(r => r.Key is { } k && k[2..].Split('#')[0] == path);

    /// <summary>The realized Commit row of a path (a folder's key is d: and its path, a file's its
    /// path, then their states).</summary>
    private static BoxElement? CommitRow(HeadlessSession session, string path) =>
        LayoutProbe.Descendants<VirtualListElement<JunctionApp.CommitTreeRowItem>>(Root(session), _ => true).SingleOrDefault() is { } list
            ? list.Children.Skip(1).OfType<BoxElement>()
                .FirstOrDefault(r => r.Key is { } k && (k.StartsWith("d:", StringComparison.Ordinal) ? k[2..] : k).Split('#')[0] == path)
            : null;

    private static Element Root(HeadlessSession session) =>
        (Element)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window)!;
}
