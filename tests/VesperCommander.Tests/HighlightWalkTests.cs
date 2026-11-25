using System.Threading;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The colour walks of the viewer (Shift+F3) and the comparison (F3): a walk whose view closes
/// stops within a line, and a big walk's memory is given back at rest in one collection, as
/// Junction's HighlightResumeTests pin for its own walks.
/// </summary>
public class HighlightWalkTests
{
    private const int FileLines = 9_000;   // a walk of about a second here

    // Code a walk takes a while over.
    private static string Huge() => string.Concat(Enumerable.Range(1, FileLines).Select(i =>
        $"    public int Value{i}(string name) => name.Length + {i}; // the quick brown fox\n"));

    /// <summary>Closing the viewer stops its colour walk. Without the cancel the walk went on to
    /// the end of the file for colours nobody would see: an 8,000-line C# file closed 150 ms
    /// after it opened walked on for 31-36 MB and half a second of a core (plan 17 round 19).</summary>
    [Fact]
    public void ClosingTheViewer_StopsItsColourWalk()
    {
        using var t = new ViewerTests.Fixture();
        t.Write("Huge.cs", Huge());
        t.Open();
        var app = t.App;

        t.ViewFile("Huge.cs");   // the text landed, so its walk has started
        t.Press(KeyCode.Escape);
        t.Session.PumpUntil(() => app.HighlightWalksEnded == 1, TimeSpan.FromSeconds(30));

        Assert.Equal(1, app.HighlightWalksCancelled);
        Assert.Equal(0, app.HighlightLinesWalked);   // stopped, not finished
    }

    /// <summary>The same for the comparison: closing it stops the walk of both files.</summary>
    [Fact]
    public void ClosingTheComparison_StopsItsColourWalk()
    {
        using var t = new ViewerTests.Fixture();
        t.Write("Huge.cs", Huge());
        t.WriteOther("Huge.cs", Huge().Replace("Value17(", "Changed17("));   // one line changed
        t.Open();
        var app = t.App;

        t.DiffFile("Huge.cs");   // the comparison landed, so its walk has started
        t.Press(KeyCode.Escape);
        t.Session.PumpUntil(() => app.HighlightWalksEnded == 1, TimeSpan.FromSeconds(30));

        Assert.Equal(1, app.HighlightWalksCancelled);
        Assert.Equal(0, app.HighlightLinesWalked);
    }

    /// <summary>A big walk is followed by one collection, once no walk runs and the window has
    /// had no input for <see cref="CommanderApp.HighlightIdleCollect"/>, so never while the
    /// wheel turns; a small file's walk is followed by none. Without it the window rested at
    /// 142.5 MB working set after an 8,000-line file's colours, 30.7 MB committed, for as long as
    /// it rested; with it 121.3 MB, 9.5 MB committed (plan 17 round 19).</summary>
    [Fact]
    public void ABigWalk_IsFollowedByOneCollectionOnceTheInputStops_ASmallOneByNone()
    {
        using var t = new ViewerTests.Fixture();
        t.Write("Huge.cs", Huge());
        t.Write("Small.cs", "class Small { int x = 1; }\n");
        t.Open();
        var app = t.App;
        app.HighlightIdleCollect = TimeSpan.FromMilliseconds(400);

        t.ViewFile("Huge.cs");
        t.WaitForHighlight();   // landed in the same step as the walk's end
        Assert.True(app.HighlightLinesWalked >= CommanderApp.BigWalkLines, $"{app.HighlightLinesWalked} lines walked");

        // The reader scrolls the file its colours landed in: nothing collects while the wheel turns.
        var until = DateTime.UtcNow.AddMilliseconds(1200);
        while (DateTime.UtcNow < until)
        {
            t.Session.Window.Wheel(600, 400, 0, -1);
            t.Session.PumpFrame();
            Thread.Sleep(20);
        }
        Assert.Equal(0, app.HighlightIdleCollections);
        t.Session.PumpUntil(() => app.HighlightIdleCollections > 0, TimeSpan.FromSeconds(10));
        Rest(t.Session, 1000);
        Assert.Equal(1, app.HighlightIdleCollections);   // once a rest

        t.Press(KeyCode.Escape);
        long bytes = app.HighlightBytesWalked;
        t.ViewFile("Small.cs");   // a walk of a line
        t.WaitForHighlight();
        Assert.InRange(app.HighlightBytesWalked - bytes, 1, CommanderApp.BigWalkBytes - 1);
        Rest(t.Session, 1000);
        Assert.Equal(1, app.HighlightIdleCollections);
    }

    private static void Rest(HeadlessSession session, int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { session.PumpFrame(); Thread.Sleep(5); }
    }
}
