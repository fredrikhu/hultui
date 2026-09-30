using HultUI.Core.Builders;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// An OS drag as the Windows drop target now drives it: the data is known from the moment the
/// drag enters, each position is answered with whether the element there accepts it — the
/// operating system shows a no-entry cursor otherwise — and a drag that leaves the window is
/// a leave, not a drop.
/// </summary>
public class OsDragFeedbackTests
{
    private static (HeadlessSession Session, List<string> Seen) Zone()
    {
        var session = HeadlessSession.Create(400, 300);
        var seen = new List<string>();
        var zone = Box.New().Width(200).Height(100)
            .OnDragOver(e =>
            {
                seen.Add($"over files={e.DragData.Files.Count}");
                e.Accepted = e.DragData.HasFiles;
            })
            .OnDrop(e => seen.Add("drop " + string.Join(",", e.DragData.Files)));
        zone.On<DragLeaveEvent>(_ => seen.Add("leave"));
        session.Window.SetRoot(Box.New().FlexGrow(1).Children(zone));
        session.PumpFrame();
        return (session, seen);
    }

    [Fact]
    public void TheFiles_AreKnownWhileTheDragHovers_AndTheAnswerComesBack()
    {
        var (session, seen) = Zone();
        using var _ = session;

        Assert.True(session.Window.DragFilesOver(new Point(50, 50), [@"C:\tmp\a.txt"]));
        Assert.Equal("over files=1", seen[0]);

        // Outside the zone nobody accepts, and the window says so.
        Assert.False(session.Window.DragTo(new Point(300, 250)));

        Assert.True(session.Window.DragTo(new Point(60, 50)));
        Assert.True(session.Window.DropHere());
        Assert.Contains(@"drop C:\tmp\a.txt", seen);
    }

    [Fact]
    public void ADragThatLeavesTheWindow_IsNotADrop()
    {
        var (session, seen) = Zone();
        using var _ = session;

        session.Window.DragFilesOver(new Point(50, 50), [@"C:\tmp\a.txt"]);
        session.Window.CancelDrag();

        Assert.Equal(["over files=1", "leave"], seen);
        // The next drag starts clean.
        Assert.False(session.Window.DragFilesOver(new Point(300, 250), []));
    }

    [Fact]
    public void ADropNobodyAccepts_IsReportedAsNotTaken()
    {
        var (session, seen) = Zone();
        using var _ = session;

        session.Window.DragFilesOver(new Point(300, 250), [@"C:\tmp\a.txt"]);

        Assert.False(session.Window.DropHere());
        Assert.DoesNotContain(seen, s => s.StartsWith("drop"));
    }
}
