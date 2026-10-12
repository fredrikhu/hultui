using System.Collections;
using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// A syntax highlight that lands rebuilds the window only when the diff on screen is waiting
/// for it.
///
/// <para>Selecting a commit in History prefetches the colours of the file its diff will open on
/// (<see cref="CommitPrefetchTests"/>), so they land while the log is still on screen — and the
/// landing used to rebuild the whole window, which shows no diff there. A reader moving through
/// the log paid two whole-window builds a key instead of one (Junction, Down every 500 ms: 2.00
/// builds and ~470 KB a key). These pin both sides: no build for a result nothing on screen
/// reads, and the colours still arrive on their own for a diff that is on screen, and show at
/// once when the Commit tab opens on a result that landed earlier.</para>
/// </summary>
[Collection("JunctionDiff")]
public class HighlightRebuildTests
{
    /// <summary>One key, one build: the commit's detail and the highlight of its opening file
    /// both land while History is on screen, and neither rebuilds the window.</summary>
    [Fact]
    public void AMoveInHistory_BuildsOnce_WhenTheNextCommitsHighlightLands()
    {
        Hosted((session, app) =>
        {
            int warmed = Highlights(app).Count;
            int before = app.Builds;

            session.Window.PressKey(KeyCode.Down);
            session.PumpUntil(() => Highlights(app).Count > warmed && Pending(app) == 0, TimeSpan.FromSeconds(20));
            session.PumpFrames(3);

            Assert.Equal(JunctionView.History, View(app));
            Assert.Equal(1, app.Builds - before);
        });
    }

    /// <summary>The result that landed without a build is the one the Commit tab draws with, on
    /// its first frame.</summary>
    [Fact]
    public void ColoursThatLandedInHistory_ShowWhenTheCommitTabOpens()
    {
        Hosted((session, app) =>
        {
            int warmed = Highlights(app).Count;
            session.Window.PressKey(KeyCode.Down);   // "add the sources": opens on src/Program.cs
            session.PumpUntil(() => Highlights(app).Count > warmed && Pending(app) == 0, TimeSpan.FromSeconds(20));
            session.PumpFrames(2);

            session.Window.PressKey(KeyCode.Digit3);
            session.PumpFrame();

            Assert.Equal(JunctionView.Commit, View(app));
            Assert.True(Coloured(session, "class Program"), "the opening file's diff was drawn without its colours");
        });
    }

    /// <summary>The other side: a file opened in the Commit tab whose colours are not cached yet
    /// is drawn plain, and its colours arrive by themselves when the tokenize lands — a rebuild
    /// the diff on screen is owed.</summary>
    [Fact]
    public void AFileOpenedInTheCommitTab_GetsItsColoursWhenTheyLand()
    {
        Hosted((session, app) =>
        {
            int warmed = Highlights(app).Count;
            session.Window.PressKey(KeyCode.Down);   // "add the sources"
            session.PumpUntil(() => Highlights(app).Count > warmed && Pending(app) == 0, TimeSpan.FromSeconds(20));
            session.Window.PressKey(KeyCode.Digit3);
            session.PumpFrames(2);
            Assert.Equal("src/Program.cs", Field(app, "_selectedFilePath"));

            // Up the file list to src/Helper.cs, which nothing has tokenized.
            session.Window.PressKey(KeyCode.Up);
            session.PumpFrame();
            Assert.Equal("src/Helper.cs", Field(app, "_selectedFilePath"));

            session.PumpUntil(() => Coloured(session, "class Helper"), TimeSpan.FromSeconds(20));
        });
    }

    /// <summary>What the diff asked for is forgotten by the next build that shows no diff: a
    /// tokenize the Commit tab started, landing after the reader went back to History, is cached
    /// and builds nothing.</summary>
    [Fact]
    public void AHighlightLandingAfterItsDiffLeftTheScreen_BuildsNothing()
    {
        Hosted((session, app) =>
        {
            int warmed = Highlights(app).Count;
            session.Window.PressKey(KeyCode.Down);   // "add the sources"
            session.PumpUntil(() => Highlights(app).Count > warmed && Pending(app) == 0, TimeSpan.FromSeconds(20));
            session.Window.PressKey(KeyCode.Digit3);
            session.PumpFrames(2);
            warmed = Highlights(app).Count;

            // Open src/Helper.cs and leave for History before anything is pumped: the tokenize
            // starts, and its result can only reach the UI thread after the switch.
            session.Window.PressKey(KeyCode.Up);
            Assert.Equal("src/Helper.cs", Field(app, "_selectedFilePath"));
            Assert.Equal(1, Pending(app));
            session.Window.PressKey(KeyCode.Digit2);
            int before = app.Builds;

            session.PumpUntil(() => Highlights(app).Count > warmed && Pending(app) == 0, TimeSpan.FromSeconds(20));
            session.PumpFrames(3);

            Assert.Equal(JunctionView.History, View(app));
            Assert.Equal(0, app.Builds - before);
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private static object? Field(JunctionApp app, string name) =>
        typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app);

    private static IDictionary Highlights(JunctionApp app) => (IDictionary)Field(app, "_highlights")!;

    private static int Pending(JunctionApp app) => ((IEnumerable<string>)Field(app, "_highlightsPending")!).Count();

    private static JunctionView View(JunctionApp app) =>
        (JunctionView)typeof(JunctionApp).GetProperty("View", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static Element Root(HeadlessSession session) =>
        (Element)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window)!;

    /// <summary>Whether the diff draws the line starting with <paramref name="code"/> with
    /// syntax colours.</summary>
    private static bool Coloured(HeadlessSession session, string code) =>
        Descendants(Root(session)).OfType<TextElement>()
            .Any(t => t.Content.StartsWith(code, StringComparison.Ordinal) && t.Spans is { Count: > 0 } spans
                && spans.Any(s => s.Color.A > 0));

    private static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var d in Descendants(child))
                yield return d;
    }

    /// <summary>History open on the newest of three commits, its detail loaded and its opening
    /// file's colours landed: the state someone is in while reading the log.</summary>
    private static void Hosted(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("src/Program.cs", "class Program { static void Main() { } }\n");
        repo.Write("src/Helper.cs", "class Helper { }\n");
        repo.Write("README.md", "# demo\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "add the sources");
        repo.Write("src/Program.cs", "class Program { static void Main() { System.Console.WriteLine(); } }\n");
        repo.Git("commit", "-am", "print something");

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

            session.Window.PressKey(KeyCode.Digit2);
            session.PumpUntil(() => Field(app, "_selectedCommitHash") != null, TimeSpan.FromSeconds(30));
            session.PumpUntil(() => Highlights(app).Count > 0 && Pending(app) == 0, TimeSpan.FromSeconds(30));
            session.PumpFrames(3);
            body(session, app);
        }
        finally { app?.Dispose(); }
    }
}
