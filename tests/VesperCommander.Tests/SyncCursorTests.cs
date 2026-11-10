using System.IO;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The cursor in Synchronize (F9): what a build of the rows costs, and what a cursor key does (plan
/// 17 round 10 step 7).
/// </summary>
public class SyncCursorTests
{
    private static string Name(int i) => $"f{i:000}.txt";

    /// <summary>Two folders of <paramref name="files"/> files, every third one the same on both sides
    /// (Equal, hidden while "Show equal" is off) and the rest newer on the left, in Sync with the
    /// comparison done.</summary>
    internal static DiffViewTests.Fixture OpenSync(int files)
    {
        var t = new DiffViewTests.Fixture();
        var then = System.DateTime.Now.AddHours(-2);
        for (int i = 0; i < files; i++)
        {
            bool equal = i % 3 == 1;
            t.WriteLeft(Name(i), equal ? "same" : "left, newer");
            t.WriteRight(Name(i), "same");
            File.SetLastWriteTime(Path.Combine(t.Right, Name(i)), then);
            if (equal) File.SetLastWriteTime(Path.Combine(t.Left, Name(i)), then);
        }
        t.Open();
        t.Invoke("OpenSync");
        t.Session.PumpUntil(() => !t.Field<bool>("_syncComparing") && t.Field<System.Collections.IList>("_syncRows").Count == files,
            System.TimeSpan.FromSeconds(20));
        t.Session.PumpFrames(2);
        return t;
    }

    /// <summary>A build of the rows asks for the rows shown once, not once a row: each row looked
    /// itself up in a fresh filtered list (O(rows²) a build).</summary>
    [Fact]
    public void ABuildOfTheRows_AsksForTheRowsShownOnce()
    {
        using var t = OpenSync(60);
        CommanderApp.VisibleSyncRowsCalls = 0;
        t.MarkDirty();
        t.Session.PumpFrames(1);
        Assert.InRange(CommanderApp.VisibleSyncRowsCalls, 1, 2);
    }
}
