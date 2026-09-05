using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// "Who has this file open?" The test process is the one program guaranteed to be around,
/// so it holds the file itself — a stream opened without sharing — and expects to be named.
/// Windows answers through the Restart Manager and Linux through /proc; macOS shells out to
/// lsof, which a CI box may not have, so the platform tests stop at "an answer or a null".
/// </summary>
public class FileHoldersTests
{
    private static bool CanAsk => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    private static string TempFile(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "vcholders_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "held.txt");
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void TheProcessHoldingAFileIsNamed()
    {
        if (!CanAsk) return;
        var path = TempFile(out var dir);
        try
        {
            using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            var holders = FileHolders.Find([path]);

            Assert.NotNull(holders);
            var me = Assert.Single(holders!, h => h.Pid == Environment.ProcessId);
            Assert.False(string.IsNullOrWhiteSpace(me.Name));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void AFileNobodyHoldsAnswersEmptyNotNull()
    {
        if (!CanAsk) return;
        var path = TempFile(out var dir);
        try
        {
            var holders = FileHolders.Find([path]);
            Assert.NotNull(holders);
            Assert.DoesNotContain(holders!, h => h.Pid == Environment.ProcessId);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>"Why can't I delete this folder" is answered by any open file inside it.</summary>
    [Fact]
    public void AFolderIsAskedThroughTheFilesInIt()
    {
        if (!CanAsk) return;
        var path = TempFile(out var dir);
        try
        {
            using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            var holders = FileHolders.Find([dir]);

            Assert.NotNull(holders);
            Assert.Contains(holders!, h => h.Pid == Environment.ProcessId);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void AMissingFileIsNotAnError()
    {
        var holders = FileHolders.Find([Path.Combine(Path.GetTempPath(), "vc-no-such-" + Guid.NewGuid().ToString("N"))]);
        Assert.NotNull(holders);
        Assert.Empty(holders!);
    }

    // ── The error tag ────────────────────────────────────────────────────
    /// <summary>On Windows a rename of a file something has open fails with a sharing
    /// violation, and that is the signal to go and ask. Elsewhere the rename simply works.</summary>
    [Fact]
    public void ARenameOfAHeldFileIsTaggedInUse()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = TempFile(out var dir);
        try
        {
            using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            var err = FileOps.Rename(FileOps.EntryFromPath(path), "other.txt");
            Assert.True(FileOps.IsInUse(err), err);
            Assert.False(FileOps.IsDenied(err));
            Assert.DoesNotContain("[inuse]", FileOps.Clean(err));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ── Through the app ──────────────────────────────────────────────────
    [Fact]
    public void CtrlShiftLOnAFileNobodyHoldsSaysSo()
    {
        if (!CanAsk) return;
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("free.txt", "x");
        t.Open();
        t.PutCursorOn('L', "free.txt");

        t.Press(KeyCode.L, KeyModifiers.Ctrl | KeyModifiers.Shift);

        t.Session.PumpUntil(() => (t.Field<string?>("_notice") ?? "").StartsWith("Nothing has"), TimeSpan.FromSeconds(30));
        Assert.Equal("Nothing has free.txt open", t.Field<string?>("_notice"));
        Assert.Null(t.Field<object?>("_confirm"));
    }

    [Fact]
    public void CtrlShiftLOnAHeldFileOpensTheDialogNamingTheHolder()
    {
        if (!CanAsk) return;
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("held.txt", "x");
        t.Open();
        using var hold = new FileStream(Path.Combine(t.Left, "held.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
        t.PutCursorOn('L', "held.txt");

        t.Press(KeyCode.L, KeyModifiers.Ctrl | KeyModifiers.Shift);

        t.Session.PumpUntil(() => t.Field<object?>("_confirm") != null, TimeSpan.FromSeconds(30));
        var confirm = t.Field<object>("_confirm");
        string title = (string)confirm.GetType().GetProperty("Title")!.GetValue(confirm)!;
        string message = (string)confirm.GetType().GetProperty("Message")!.GetValue(confirm)!;
        Assert.StartsWith("held.txt is open in", title);
        Assert.Contains($"PID {Environment.ProcessId}", message);
        // An information dialog: no Cancel, and Enter closes it.
        Assert.Null(confirm.GetType().GetProperty("CancelLabel")!.GetValue(confirm));
        t.Press(KeyCode.Enter);
        Assert.Null(t.Field<object?>("_confirm"));
    }

    /// <summary>The moment the question is actually asked: a delete that failed for being
    /// in use names the program, instead of failing quietly.</summary>
    [Fact]
    public void AFailedDeleteNamesWhatHoldsTheFile()
    {
        if (!OperatingSystem.IsWindows()) return;   // elsewhere an open file deletes fine
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("held.txt", "x");
        t.Open();
        var settings = t.Field<Settings>("_settings");
        settings.ConfirmDelete = false;
        settings.Recycle = false;
        using var hold = new FileStream(Path.Combine(t.Left, "held.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
        t.PutCursorOn('L', "held.txt");

        t.Press(KeyCode.F8);

        t.Session.PumpUntil(() => t.Field<object?>("_confirm") != null, TimeSpan.FromSeconds(30));
        var confirm = t.Field<object>("_confirm");
        Assert.Contains($"PID {Environment.ProcessId}",
            (string)confirm.GetType().GetProperty("Message")!.GetValue(confirm)!);
        Assert.True(File.Exists(Path.Combine(t.Left, "held.txt")));
    }

    [Fact]
    public void AFailedRenameNamesTheHolderInThePrompt()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("held.txt", "x");
        t.Open();
        using var hold = new FileStream(Path.Combine(t.Left, "held.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
        t.PutCursorOn('L', "held.txt");

        t.Press(KeyCode.F6, KeyModifiers.Shift);
        t.SetField("_namePromptValue", "other.txt");
        t.Press(KeyCode.Enter);

        Assert.NotNull(t.Field<object?>("_namePrompt"));   // still open, with the answer in it
        var err = t.Field<string?>("_namePromptError") ?? "";
        Assert.StartsWith("Open in", err);
        Assert.Contains($"PID {Environment.ProcessId}", err);
    }

    [Fact]
    public void AnArchiveMemberIsAnsweredWithoutALookup()
    {
        using var t = new DiffViewTests.Fixture();
        using (var zip = System.IO.Compression.ZipFile.Open(Path.Combine(t.Left, "a.zip"), System.IO.Compression.ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("in.txt").Open())) w.Write("x");
        t.Open();
        t.PutCursorOn('L', "a.zip");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "in.txt"), TimeSpan.FromSeconds(10));
        t.PutCursorOn('L', "in.txt");

        t.Press(KeyCode.L, KeyModifiers.Ctrl | KeyModifiers.Shift);

        Assert.Contains("Archive members", t.Field<string?>("_notice") ?? "");
    }
}
