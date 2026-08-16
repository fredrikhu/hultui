using System.IO.Compression;
using System.Reflection;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Packing reports itself through the copy progress overlay, and can be stopped. It used to be
/// a background task behind a transient "Packing…" notice: a large pack looked frozen, and there
/// was nothing to press to call it off.
/// </summary>
public class PackOverlayTests
{
    [Fact]
    public void CommittingAPackRaisesTheProgressOverlayRatherThanJustANotice()
    {
        using var t = new Harness();
        t.WriteFiles(6);
        t.Open();

        t.CommitPack("a0.txt", "a1.txt");

        // Set before the worker starts, so this is a fact about the commit and not a race.
        Assert.True(t.ProgressOpen, "the pack ran with no overlay to report into");
        Assert.True(t.OpIsPack, "the progress overlay is showing a copy, not a pack");
        t.WaitForNotice();
        Assert.StartsWith("Packed", t.Notice);
        Assert.False(t.ProgressOpen, "the overlay stayed up after the pack finished");
    }

    /// <summary>The overlay's queue names the archive members, and the rows finish as the pack
    /// works through them.</summary>
    [Fact]
    public void TheQueueIsTheEntriesGoingIntoTheArchive()
    {
        using var t = new Harness();
        t.WriteFiles(3);
        t.Open();

        t.CommitPack("a0.txt", "a1.txt", "a2.txt");
        Assert.Equal(new[] { "a0.txt", "a1.txt", "a2.txt" }, t.QueueNames());
        Assert.Equal("bundle.zip", t.PackTarget);

        // The op is gone once the pack finishes — what the queue was reporting on has to be
        // read off the archive it produced.
        t.WaitForNotice();
        using var zip = ZipFile.OpenRead(Path.Combine(t.Right, "bundle.zip"));
        Assert.Equal(new[] { "a0.txt", "a1.txt", "a2.txt" }, zip.Entries.Select(e => e.FullName).Order());
    }

    /// <summary>Cancel stops the run and takes the part-written archive with it. A half-packed
    /// zip lists and extracts as though it were the whole thing, so leaving one behind is worse
    /// than leaving nothing.</summary>
    [Fact]
    public void CancellingDeletesThePartWrittenArchive()
    {
        using var t = new Harness();
        // Enough entries that the worker is still going while the test reaches into it — it
        // stats every one of them before writing the first.
        t.WriteFiles(400);
        t.Open();

        t.CommitPack(t.Names(400));
        t.PauseOp();                 // hold the worker at its next checkpoint
        t.CancelOp();                // and then call it off from there

        t.WaitForNotice();
        Assert.Equal("Pack cancelled", t.Notice);
        Assert.False(File.Exists(Path.Combine(t.Right, "bundle.zip")),
            "a cancelled pack left its half-written archive behind");
    }

    /// <summary>Cancelling "one archive per item" part-way keeps the archives that already
    /// finished. Only the one being written is incomplete; the rest are archives the user watched
    /// succeed, and a cancel is a request to stop, not to undo.</summary>
    [Fact]
    public void CancellingPartWayKeepsTheArchivesThatAlreadyFinished()
    {
        using var t = new Harness();
        t.WriteFiles(60);
        t.Open();

        t.CommitPack(eachOwn: true, t.Names(60));
        // Let a few archives land before stopping the run.
        t.PumpUntilDone(3);
        t.PauseOp();
        t.CancelOp();
        t.WaitForNotice();

        Assert.Equal("Pack cancelled", t.Notice);
        var kept = Directory.GetFiles(t.Right, "*.zip").Select(Path.GetFileName).ToList();
        Assert.NotEmpty(kept);
        // Every archive still there is a complete one that can be opened and read back.
        foreach (var name in kept)
        {
            using var zip = ZipFile.OpenRead(Path.Combine(t.Right, name!));
            Assert.Single(zip.Entries);
        }
    }

    /// <summary>One progress op at a time. A copy sent to the background is still running with
    /// the overlay hidden, so the dialog's own "is anything open" check doesn't see it — and a
    /// pack starting there would take the op out from under it.</summary>
    [Fact]
    public void PackingIsRefusedWhileATransferIsAlreadyRunning()
    {
        using var t = new Harness();
        t.WriteFiles(3);
        t.Open();

        t.Mark("a0.txt");
        t.Press(KeyCode.F5, KeyModifiers.Alt);
        t.SetField("_op", t.NewBareOp());        // as a backgrounded copy would have left it
        t.Press(KeyCode.Enter);

        Assert.True(t.PackDialogOpen, "the pack went ahead over a running transfer");
        Assert.Contains("already running", t.PackError);
        Assert.False(File.Exists(Path.Combine(t.Right, "bundle.zip")));
    }

    // ── harness ──────────────────────────────────────────────────────────
    private sealed class Harness : IDisposable
    {
        private readonly DiffViewTests.Fixture _t = new();

        public string Right => _t.Right;
        public void Open() => _t.Open();
        public void Dispose() => _t.Dispose();
        public void SetField(string name, object? value) => _t.SetField(name, value);
        public void Press(KeyCode k, KeyModifiers m = KeyModifiers.None) => _t.Press(k, m);
        public void Mark(params string[] names) => _t.Mark('L', names);

        public void WriteFiles(int count)
        {
            for (int i = 0; i < count; i++) _t.WriteLeft($"a{i}.txt", $"contents of {i}");
        }

        public string[] Names(int count) => [.. Enumerable.Range(0, count).Select(i => $"a{i}.txt")];

        /// <summary>Mark the items, Alt+F5, name the archive, Enter — the keystrokes a user
        /// makes. The name is set after opening because opening seeds the field.</summary>
        public void CommitPack(params string[] items) => CommitPack(false, items);

        public void CommitPack(bool eachOwn, params string[] items)
        {
            _t.Mark('L', items);
            _t.Press(KeyCode.F5, KeyModifiers.Alt);
            Assert.True(PackDialogOpen, "Alt+F5 did not open the Pack dialog");
            _t.SetField("_packEachOwn", eachOwn);
            _t.SetField("_packName", "bundle.zip");
            _t.Press(KeyCode.Enter);
        }

        /// <summary>Wait until the pack has finished at least <paramref name="count"/> entries,
        /// so a cancel lands part-way through the run rather than before any of it.</summary>
        public void PumpUntilDone(int count) =>
            _t.Session.PumpUntil(
                () => Op is { } op && (int)OpField(op, "Done") >= count,
                TimeSpan.FromSeconds(20));

        public bool PackDialogOpen => _t.Field<bool>("_packOpen");
        public string PackError => _t.Field<string?>("_packError") ?? "";
        public bool ProgressOpen => _t.Field<bool>("_progressOpen");
        public string Notice => _t.Field<string?>("_notice") ?? "";

        public void WaitForNotice() =>
            _t.Session.PumpUntil(() => Notice.StartsWith("Packed") || Notice.StartsWith("Pack "),
                                 TimeSpan.FromSeconds(30));

        // TransferOp is private to CommanderApp, so the op is reached reflectively rather than
        // by widening it just for the tests.
        private object? Op => typeof(CommanderApp)
            .GetField("_op", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(App);

        private object App => typeof(DiffViewTests.Fixture)
            .GetField("_app", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_t)!;

        private static object OpField(object op, string name) =>
            op.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(op)!;

        public bool OpIsPack => Op is { } op && (bool)OpField(op, "IsPack");
        public string PackTarget => Op is { } op ? (string)OpField(op, "PackTarget") : "";

        public IEnumerable<string> QueueNames()
        {
            var op = Op ?? throw new Xunit.Sdk.XunitException("no pack is running");
            var items = (System.Collections.IEnumerable)OpField(op, "Items");
            return items.Cast<FileEntry>().Select(e => e.Name).ToList();
        }

        private void SetOpFlag(string name, bool value)
        {
            var op = Op ?? throw new Xunit.Sdk.XunitException("no pack is running");
            op.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)!.SetValue(op, value);
        }

        /// <summary>Hold the worker where it next looks — the same flag the Pause button sets.
        /// Pausing first is what makes the cancel deterministic: the run cannot finish while it
        /// is waiting to be let go.</summary>
        public void PauseOp() => SetOpFlag("Paused", true);

        public void CancelOp()
        {
            SetOpFlag("Cancelled", true);
            SetOpFlag("Paused", false);
        }

        /// <summary>An op with nothing behind it, standing in for a copy that was sent to the
        /// background.</summary>
        public object NewBareOp()
        {
            var type = typeof(CommanderApp).GetNestedType("TransferOp", BindingFlags.NonPublic)!;
            return Activator.CreateInstance(type, nonPublic: true)!;
        }
    }
}
