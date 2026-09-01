using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Finding a file by name in the Files tab — the counterpart to Ctrl+F, which searches inside
/// the open file. The tree is the browsed commit's, so the filter runs over paths rather than
/// over anything on screen, and a match five folders down has to be shown without the user
/// opening five folders to reach it.
/// </summary>
[Collection("JunctionDiff")]
public class FileSearchTests
{
    [Fact]
    public void SlashFocusesTheSearchField()
    {
        Hosted((session, app) =>
        {
            session.Window.PressKey(KeyCode.Slash);
            session.PumpFrames(3);

            var focused = session.Window.Window.FocusedElement;
            Assert.IsType<TextInputElement>(focused);
            Assert.Equal("files-search", focused!.AutomationId);
        });
    }

    [Fact]
    public void TypingShowsOnlyTheMatchingFilesAndOpensTheFoldersOverThem()
    {
        Hosted((session, app) =>
        {
            Search(session, "widget");

            var rows = VisibleRows(app);
            Assert.Contains("src/deep/widget.cs", rows);
            Assert.DoesNotContain("README.md", rows);
            // The match is two folders down and neither was expanded — a filtered tree opens
            // itself, or the row it found is behind a folder nobody can see is there.
            Assert.Contains("src", rows);
            Assert.Contains("src/deep", rows);
        });
    }

    [Fact]
    public void AWildcardMatchesTheFileNameEndToEnd()
    {
        Hosted((session, app) =>
        {
            Search(session, "*.md");

            var files = VisibleRows(app).Where(p => p.Contains('.')).ToList();
            Assert.Contains("README.md", files);
            Assert.DoesNotContain("src/deep/widget.cs", files);
        });
    }

    [Fact]
    public void EscapeClearsTheQueryAndBringsTheWholeTreeBack()
    {
        Hosted((session, app) =>
        {
            Search(session, "widget");
            Assert.DoesNotContain("README.md", VisibleRows(app));

            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(3);

            Assert.Equal("", Query(app));
            Assert.Contains("README.md", VisibleRows(app));
        });
    }

    /// <summary>The cursor lands on the first match, so Enter opens what the search found
    /// without arrowing down to it first.</summary>
    [Fact]
    public void TheCursorMovesToTheFirstMatch()
    {
        Hosted((session, app) =>
        {
            Search(session, "widget");
            Assert.Equal("src/deep/widget.cs", Cursor(app));
        });
    }

    // ── Reaching the app's state ─────────────────────────────────────────

    private static void Search(HeadlessSession session, string query)
    {
        session.Window.PressKey(KeyCode.Slash);
        session.PumpFrames(3);
        session.Window.TypeText(query);
        session.PumpFrames(3);
    }

    private static string Query(JunctionApp app) =>
        (string)typeof(JunctionApp).GetField("_filesQuery", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;

    private static string? Cursor(JunctionApp app) =>
        (string?)typeof(JunctionApp).GetField("_filesCursorPath", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app);

    /// <summary>The rows the tree is showing, by path — folders included, since a filtered
    /// tree has to open the folders over its matches.</summary>
    private static List<string> VisibleRows(JunctionApp app)
    {
        var flat = typeof(JunctionApp).GetField("_filesFlat", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;
        var rows = new List<string>();
        foreach (var node in (System.Collections.IEnumerable)flat)
            rows.Add((string)node.GetType().GetProperty("Path")!.GetValue(node)!);
        return rows;
    }

    /// <summary>The Files view on a small committed tree with one file worth searching for,
    /// two folders down.</summary>
    private static void Hosted(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("src/deep/widget.cs", "class Widget { }\n");
        repo.Write("src/other.cs", "class Other { }\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "add a tree to search");

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

            session.Window.PressKey(KeyCode.Digit7);   // Files
            session.PumpUntil(() => app!.View == JunctionView.Files, TimeSpan.FromSeconds(20));
            // The tree walk is off-thread; wait for the rows rather than a frame count.
            session.PumpUntil(() => VisibleRows(app!).Count > 0, TimeSpan.FromSeconds(30));
            session.PumpFrames(2);

            body(session, app);
        }
        finally { app?.Dispose(); }
    }
}
