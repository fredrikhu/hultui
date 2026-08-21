using System.IO.Compression;
using System.Reflection;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Copying <i>out of</i> an archive into a folder that needs administrator rights.
///
/// <para>The elevated retry hands a batch of (source, target) paths to a second process, and
/// an archive member has no path that process could open — so archive sources used to be
/// left out of the retry entirely, and the copy simply failed with no offer. They are now
/// extracted to a temp file first, unprivileged: reading the archive never needed rights,
/// only the write did, so the elevated step stays a plain copy between two real paths with
/// no archive backend (and no password) anywhere near the administrator process.</para>
/// </summary>
public class ArchiveElevationStagingTests
{
    /// <summary>Build the private TransferOp the worker uses, aimed at <paramref name="destDir"/>.</summary>
    private static object NewOp(FileEntry entry, string destDir)
    {
        var opType = typeof(CommanderApp).GetNestedType("TransferOp", BindingFlags.NonPublic)!;
        var ctor = opType.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, [typeof(List<FileEntry>)], null)!;
        var op = ctor.Invoke([new List<FileEntry> { entry }]);
        opType.GetField("DestDir")!.SetValue(op, destDir);
        return op;
    }

    private static string? Staging(object op) =>
        (string?)op.GetType().GetField("Staging")!.GetValue(op);

    [Fact]
    public void AnArchiveMemberIsStagedToARealFileTheElevatedWorkerCouldCopy()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = Path.Combine(t.Left, "bundle.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var w = new StreamWriter(z.CreateEntry("inside.txt").Open()))
            w.Write("member contents");
        t.Open();

        var member = new FileEntry("inside.txt", ArchivePath.Combine(zip, "inside.txt"),
            IsDir: false, Size: 15, Modified: DateTime.Now);
        var op = NewOp(member, t.Right);

        var staged = (string?)t.Invoke("StageForElevation", op, member);

        Assert.NotNull(staged);
        Assert.False(ArchivePath.InArchive(staged!), "the retry source must be a real path, not a VFS one");
        Assert.Equal("member contents", File.ReadAllText(staged!));
        Assert.Equal("inside.txt", Path.GetFileName(staged!));
        Assert.NotNull(Staging(op));
    }

    /// <summary>Two members of one batch share the staging folder rather than making one each,
    /// so the whole retry is cleaned up by removing a single directory.</summary>
    [Fact]
    public void EveryStagedMemberOfARunSharesOneFolder()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = Path.Combine(t.Left, "bundle.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(z.CreateEntry("one.txt").Open())) w.Write("1");
            using (var w = new StreamWriter(z.CreateEntry("two.txt").Open())) w.Write("2");
        }
        t.Open();

        var a = new FileEntry("one.txt", ArchivePath.Combine(zip, "one.txt"), false, 1, DateTime.Now);
        var b = new FileEntry("two.txt", ArchivePath.Combine(zip, "two.txt"), false, 1, DateTime.Now);
        var op = NewOp(a, t.Right);

        var stagedA = (string?)t.Invoke("StageForElevation", op, a);
        var stagedB = (string?)t.Invoke("StageForElevation", op, b);

        Assert.NotNull(stagedA);
        Assert.NotNull(stagedB);
        Assert.Equal(Path.GetDirectoryName(stagedA), Path.GetDirectoryName(stagedB));
        Assert.Equal(Staging(op), Path.GetDirectoryName(stagedA));

        // And clearing it takes the whole thing away — no temp copies of the user's files
        // left behind once the retry has run or been declined.
        typeof(CommanderApp).GetMethod("ClearStaging", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [op]);
        Assert.False(Directory.Exists(Path.GetDirectoryName(stagedA)!));
        Assert.Null(Staging(op));
    }

    /// <summary>A member that cannot be read stages nothing, rather than offering the worker
    /// an empty file to copy over the destination as administrator.</summary>
    [Fact]
    public void AMemberThatIsNotThereStagesNothing()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = Path.Combine(t.Left, "bundle.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var w = new StreamWriter(z.CreateEntry("inside.txt").Open()))
            w.Write("x");
        t.Open();

        var missing = new FileEntry("ghost.txt", ArchivePath.Combine(zip, "ghost.txt"), false, 1, DateTime.Now);
        var op = NewOp(missing, t.Right);

        Assert.Null((string?)t.Invoke("StageForElevation", op, missing));
    }
}
