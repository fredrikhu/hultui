using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Headless;
using HultUI.Headless.Recording;
using HultUI.Rendering.Skia;
using HultUI.Testing;
using Junction.Git;
using Junction.Theme;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The commit graph beside History's rows is painted on every frame for every row on screen —
/// hovering a row repaints them all. Its lane palette was a property that built a new
/// <c>Color[6]</c> on every read, three reads per colour, and its edge lists were walked with
/// <c>foreach</c> over an interface, which boxes an enumerator: 1.97 KB a frame over a screen of
/// History in this repository (plan 17, round 2), more on a repository with many lanes.
/// </summary>
public class HistoryGraphPaintTests
{
    private const int Frames = 100;

    [Fact]
    public void TheGraphGutter_AllocatesNothingPerFrame()
    {
        Hosted((session, graphs) =>
        {
            using var surface = new SkiaRasterRenderSurface();
            surface.Resize(1440, 900, 1f);
            var ctx = surface.RenderContext;

            void Frame()
            {
                ctx.BeginFrame(1440, 900, 1f, Color.Transparent);
                foreach (var graph in graphs)
                    graph.Render(ctx);
                ctx.EndFrame();
            }

            Frame();
            Frame();
            double perFrame = Allocations.PerCall(Frames, Frame);

            Assert.True(perFrame < 1, $"painting {graphs.Count} graph cells allocated {perFrame:0.#} B per frame");
        });
    }

    /// <summary>The palette is kept between frames, so it has to follow a theme switch: every
    /// edge and dot is drawn in the current theme's lane colours, before and after.</summary>
    [Fact]
    public void TheGraphFollowsTheTheme()
    {
        Hosted((session, graphs) =>
        {
            try
            {
                var nocturne = LaneColours();
                var first = GraphColours(graphs);
                Assert.Subset(nocturne, first);

                Nocturne.Use("gruvbox-dark");
                var gruvbox = LaneColours();
                Assert.Empty(gruvbox.Intersect(nocturne));
                var second = GraphColours(graphs);
                Assert.Subset(gruvbox, second);

                Nocturne.Use("nocturne");
                Assert.Subset(nocturne, GraphColours(graphs));
            }
            finally
            {
                Nocturne.Use("nocturne");
            }
        });
    }

    /// <summary>The lane colours of the theme in force, formatted as the render log does.</summary>
    private static HashSet<string> LaneColours() =>
    [
        Fmt(Nocturne.Accent400), Fmt(Nocturne.Neutral300), Fmt(Nocturne.Accent2_400),
        Fmt(Nocturne.Accent300), Fmt(Nocturne.Neutral400), Fmt(Nocturne.Accent500),
    ];

    /// <summary>Every edge and dot colour the graph cells draw (not the rings in the ground
    /// colour). Checks there are edges among them: a graph of dots only would not test the
    /// edge loops.</summary>
    private static HashSet<string> GraphColours(List<CanvasElement> graphs)
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(1440, 900, 1f);
        var recording = new RecordingRenderContext(surface.RenderContext);
        recording.BeginFrame(1440, 900, 1f, Color.Transparent);
        foreach (var graph in graphs)
            graph.Render(recording);
        recording.EndFrame();

        var ops = recording.Log.Ops;
        var beziers = ops.Where(o => o.Name == "Canvas.DrawBezier").ToList();
        var dots = ops.Where(o => o.Name == "Canvas.DrawCircle" && o.Args.Contains(" r=5.5 ")).ToList();
        Assert.True(beziers.Count >= graphs.Count, $"{beziers.Count} edges over {graphs.Count} rows:\n{recording.Log}");
        Assert.Equal(graphs.Count, dots.Count);

        var colours = new HashSet<string>();
        foreach (var op in beziers)
            colours.Add(op.Args.Split(' ')[^2]);              // "(x,y)->(x,y) #RRGGBB width=2"
        foreach (var op in dots)
            colours.Add(op.Args.Split(' ')[^1]);              // "(x,y) r=5.5 #RRGGBB"
        return colours;
    }

    private static string Fmt(Color c) =>
        c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";

    /// <summary>History over a repository with two merged branches and one still open, so
    /// rows carry edges in several lanes; hands over the graph cells on screen.</summary>
    private static void Hosted(Action<HeadlessSession, List<CanvasElement>> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        void Commit(string file, string message)
        {
            repo.Write(file, message + "\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", message);
        }

        repo.Git("checkout", "-b", "feature");
        Commit("f.txt", "feature one");
        Commit("f.txt", "feature two");
        repo.Git("checkout", "main");
        Commit("m.txt", "main one");
        repo.Git("checkout", "-b", "topic");
        Commit("t.txt", "topic one");
        repo.Git("checkout", "main");
        Commit("m.txt", "main two");
        repo.Git("merge", "--no-ff", "feature", "-m", "merge feature");
        repo.Git("checkout", "-b", "open");
        Commit("o.txt", "still open");
        repo.Git("checkout", "main");
        repo.Git("merge", "--no-ff", "topic", "-m", "merge topic");
        Commit("m.txt", "main three");

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
            Assert.Equal(JunctionView.History, app.View);

            List<CanvasElement> graphs = [];
            try
            {
                session.PumpUntil(() => (graphs = Graphs(session)).Count >= 10, TimeSpan.FromSeconds(20));
            }
            catch (TimeoutException)
            {
                throw new Xunit.Sdk.XunitException(
                    $"History showed {graphs.Count} graph cells.\n" + session.Window.DumpAccessibilityTree());
            }

            body(session, graphs);
        }
        finally { app?.Dispose(); }
    }

    /// <summary>The graph cells on screen: canvases whose draw callback closes over a graph row.</summary>
    private static List<CanvasElement> Graphs(HeadlessSession session) =>
        LayoutProbe.Descendants<CanvasElement>(Root(session), c =>
            c.Layout.AbsoluteBorderBox.Height > 0
            && c.Draw?.Target is { } target
            && target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(f => f.FieldType == typeof(GitGraphRow)));

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window);
}
