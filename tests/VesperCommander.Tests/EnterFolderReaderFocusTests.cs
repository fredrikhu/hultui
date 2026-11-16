using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// What a Windows reader is told, through the real UI Automation bridge (<see cref="HeadlessUiaReader"/>),
/// as the keys of the listening run of 2026-11-16 walk out of a folder and into another: Backspace to the
/// parent (four entries), Down to a folder, Enter. The notes of both readers showed one line,
/// "files 800, folder, 3 of 4", and left open whether Enter re-read the old item or read nothing. Each key
/// is one focus event, on the list's current item as it is then, read with its own name and place: Enter's
/// on the new folder's first row, "Parent folder", 1 of N, selected, never the item it left.
/// </summary>
[SupportedOSPlatform("windows")]
public class EnterFolderReaderFocusTests
{
    /// <param name="listingMs">How long each folder's listing takes: -1 lands it before the next frame (a
    /// headless session's own queue); 30 lands it a few frames later on the thread pool, inside the 50 ms a
    /// navigation waits for its rows (<c>LocalMoveMs</c>), as a local folder in a real window does.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(30)]
    public void BackspaceDownEnter_IsOneFocusEventAKey_EnterOnTheNewFoldersParentRow(int listingMs)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var t = Folders();
        using var reader = HeadlessUiaReader.Install(t.Session.Window);
        for (int i = 0; i < 4; i++) t.Session.PumpFrame(TimeSpan.FromMilliseconds(100));
        using var gate = new ManualResetEventSlim(true);
        if (listingMs >= 0) Gate(gate);
        try
        {
            Assert.Equal(["files-1000, folder (2 of 4) selected"], Step(t, reader, gate, KeyCode.Backspace, listingMs));
            Assert.Equal(["files-800, folder (3 of 4) selected"], Step(t, reader, gate, KeyCode.Down, listingMs));

            Assert.Equal(["Parent folder (1 of 81) selected"], Step(t, reader, gate, KeyCode.Enter, listingMs));
            Assert.Equal(Path.Combine(t.Left, "files-800"), t.Dir('L'), ignoreCase: true);
            // The reader's focus is that row, and the row at the old cursor's place reads its new name.
            var focus = reader.GetFocus()!.Value;
            Assert.Equal("Parent folder", reader.Name(focus));
            Assert.True(reader.HasKeyboardFocus(focus));
            Assert.True(VirtualItemId.TryDecode(focus, out var list, out int at));
            Assert.Equal(0, at);
            Assert.Equal("b0001.txt", reader.Name(VirtualItemId.For(list, 2))!.Split(',')[0]);
        }
        finally
        {
            gate.Set();
            FileSystem.EnumerateOverride = null;
        }
    }

    /// <summary>A folder whose rows are not there by <c>LocalMoveMs</c> moves the pane over the waiting
    /// block: the pane holds the focus with no list in it, and a reader is told the pane, named after the
    /// folder it went to, then the parent row when the rows land. Two reads, by design: a share may take
    /// seconds.</summary>
    [Fact]
    public void Enter_IntoASlowFolder_TellsThePaneWhileItWaits_ThenTheParentRow()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var t = Folders();
        using var reader = HeadlessUiaReader.Install(t.Session.Window);
        for (int i = 0; i < 4; i++) t.Session.PumpFrame(TimeSpan.FromMilliseconds(100));
        using var gate = new ManualResetEventSlim(true);
        Gate(gate);
        try
        {
            Step(t, reader, gate, KeyCode.Backspace, 30);
            Step(t, reader, gate, KeyCode.Down, 30);
            Assert.Equal(["Left pane, files-800 (0 of 0) -", "Parent folder (1 of 81) selected"],
                Step(t, reader, gate, KeyCode.Enter, 300));
        }
        finally
        {
            gate.Set();
            FileSystem.EnumerateOverride = null;
        }
    }

    /// <summary>The run's folders, the left pane in files-1000: its parent holds two folders and a file
    /// (four entries with the parent row), files-800 eighty files (81 rows).</summary>
    private static DiffViewTests.Fixture Folders()
    {
        var t = new DiffViewTests.Fixture();
        var big = Path.Combine(t.Left, "files-1000");
        var other = Path.Combine(t.Left, "files-800");
        Directory.CreateDirectory(big);
        Directory.CreateDirectory(other);
        for (int i = 0; i < 120; i++) File.WriteAllText(Path.Combine(big, $"a{i:D4}.txt"), "x");
        for (int i = 0; i < 80; i++) File.WriteAllText(Path.Combine(other, $"b{i:D4}.txt"), "x");
        File.WriteAllText(Path.Combine(t.Left, "readme.txt"), "x");
        t.Open();
        t.PutCursorOn('L', "files-1000");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Rows('L').Count > 100, TimeSpan.FromSeconds(20));
        return t;
    }

    /// <summary>Every listing from now on waits at <paramref name="gate"/>, on the thread pool.</summary>
    private static void Gate(ManualResetEventSlim gate)
    {
        FileSystem.EnumerateOverride = _ => { gate.Wait(); return null!; };
        typeof(HultUI.AsyncLoader<int>).Assembly.GetType("HultUI.AsyncWork")!
            .GetMethod("SetScheduler", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [null]);
    }

    /// <summary>Presses <paramref name="key"/>, lets the listing go after <paramref name="listingMs"/>, and
    /// returns what each focus event of the next 640 ms read: the provider's name, place and state as a
    /// client asks for them when the event comes.</summary>
    private static List<string> Step(DiffViewTests.Fixture t, HeadlessUiaReader reader, ManualResetEventSlim gate, KeyCode key, int listingMs)
    {
        reader.Clear();
        var heard = new List<string>();
        void Read()
        {
            var events = reader.FocusEvents();
            for (int i = heard.Count; i < events.Count; i++)
            {
                var id = events[i]!.Value;
                var (at, of) = reader.Place(id);
                heard.Add($"{reader.Name(id)} ({at} of {of}) {reader.IsSelected(id) switch { true => "selected", false => "not selected", null => "-" }}");
            }
        }
        if (listingMs >= 0) gate.Reset();
        t.Session.Window.PressKey(key);
        for (int frame = 0; frame < 40; frame++)
        {
            if (listingMs >= 0 && frame * 16 >= listingMs) gate.Set();
            t.Session.PumpFrame(TimeSpan.FromMilliseconds(16));
            if (listingMs >= 0) Thread.Sleep(16);
            Read();
        }
        gate.Set();
        return heard;
    }
}
