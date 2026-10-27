using System.IO;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// A footer's free space comes from <c>DriveInfo.AvailableFreeSpace</c>: a round trip to the disk,
/// or across the network for a share, on the UI thread. The footer asked on every build of itself:
/// once for a mark or a type-to-find key, twice for a folder change, twice for each transfer
/// repaint. It asks when the answer can have moved — another folder, a refresh, a file operation
/// completing — and never shows a stale one after a copy, a move or a delete.
/// </summary>
public class FreeSpaceTests
{
    [Fact]
    public void TheFooterAsksWhenTheAnswerCanHaveMoved_NotForEachKey()
    {
        using var t = new DiffViewTests.Fixture();
        Directory.CreateDirectory(Path.Combine(t.Left, "docs"));
        for (int i = 0; i < 20; i++) t.WriteLeft($"f{i:00}.txt", new string('x', 100 + i));
        t.Open();
        int asked = 0, free = 1;
        t.App.FreeLabelSource = _ => { asked++; return $"{free} GB free"; };
        t.Field<Settings>("_settings").Recycle = false;   // a delete here never reaches the Recycle Bin

        // A refresh asks again, for the pane refreshed.
        t.Press(KeyCode.R, KeyModifiers.Ctrl);
        t.Session.PumpFrames(3);
        Assert.Contains("1 GB free", Footer(t, 'L'));
        Assert.Equal(1, asked);

        // Keys that build the footer — a mark, a sweep, a filter — do not ask.
        free = 2;
        t.PutCursorOn('L', "f00.txt");
        foreach (var key in new[] { KeyCode.Insert, KeyCode.Insert, KeyCode.Space, KeyCode.F, KeyCode.Digit1, KeyCode.Escape,
                     KeyCode.Down, KeyCode.Tab, KeyCode.Down, KeyCode.Tab })
            t.Press(key);
        t.Session.PumpFrames(2);
        Assert.Equal(1, asked);
        Assert.Contains("1 GB free", Footer(t, 'L'));
        t.MarkSet('L').Clear();
        t.Press(KeyCode.Up);
        t.Session.PumpFrames(2);

        // A copy, a move and a delete: once each completes, both footers show the volume's answer.
        t.PutCursorOn('L', "f03.txt");
        t.Press(KeyCode.F5);
        t.Press(KeyCode.Enter);
        RunToCompletion(t);
        Assert.True(File.Exists(Path.Combine(t.Right, "f03.txt")));
        Assert.Contains("2 GB free", Footer(t, 'L'));
        Assert.Contains("2 GB free", Footer(t, 'R'));

        free = 3;
        t.PutCursorOn('L', "f04.txt");
        t.Press(KeyCode.F6);
        t.Press(KeyCode.Enter);
        RunToCompletion(t);
        Assert.True(File.Exists(Path.Combine(t.Right, "f04.txt")));
        Assert.Contains("3 GB free", Footer(t, 'L'));
        Assert.Contains("3 GB free", Footer(t, 'R'));

        free = 4;
        t.PutCursorOn('L', "f05.txt");
        var entry = t.Rows('L')[t.Cursor('L')];
        t.Invoke("DoDelete", 'L', new List<FileEntry> { entry });
        t.Session.PumpUntil(() => !t.Rows('L').Any(e => e.Name == "f05.txt"), System.TimeSpan.FromSeconds(10));
        t.Session.PumpFrames(2);
        Assert.False(File.Exists(Path.Combine(t.Left, "f05.txt")));
        Assert.Contains("4 GB free", Footer(t, 'L'));

        // Another folder asks for its own.
        free = 5;
        t.Invoke("NavigateTo", 'L', Path.Combine(t.Left, "docs"));
        t.Session.PumpUntil(() => t.Dir('L').EndsWith("docs"));
        t.Session.PumpFrames(3);
        Assert.Contains("5 GB free", Footer(t, 'L'));
    }

    private static void RunToCompletion(DiffViewTests.Fixture t)
    {
        t.Session.PumpUntil(() => t.Field<object?>("_op") == null, System.TimeSpan.FromSeconds(30));
        t.Session.PumpFrames(3);
    }

    /// <summary>The text a pane's footer shows.</summary>
    private static string Footer(DiffViewTests.Fixture t, char side)
    {
        var parts = t.Field<System.Array>("_paneParts").GetValue(side == 'L' ? 0 : 1)!;
        var foot = (Component)parts.GetType().GetField("Foot")!.GetValue(parts)!;
        return string.Join(" ", HultUI.Headless.LayoutProbe.Descendants<TextElement>(foot.Mount()).Select(x => x.Content));
    }
}
