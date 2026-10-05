using System.IO.Compression;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Copying an archive member over a file that is already there. It was written straight over,
/// so the file was emptied before the member had been read: a read that failed, or a copy the
/// user cancelled, cost them the file. The member is now extracted beside it first and the
/// file overwritten only once the member is complete. A 7-Zip read that fails is in
/// <see cref="SevenZipStreamTests"/>.
/// </summary>
public sealed class ArchiveCopyOverTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vesper-over-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private (FileEntry Member, string Dest) Setup()
    {
        var zip = Path.Combine(_dir, "bundle.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var w = new StreamWriter(z.CreateEntry("notes.txt").Open()))
            w.Write("from the archive");
        var dest = Directory.CreateDirectory(Path.Combine(_dir, "out")).FullName;
        File.WriteAllText(Path.Combine(dest, "notes.txt"), "the user's own");
        return (new FileEntry("notes.txt", ArchivePath.Combine(zip, "notes.txt"), IsDir: false, Size: 16, Modified: DateTime.Now), dest);
    }

    /// <summary>Cancelling deleted the destination outright: the half-written file was taken for
    /// the copy's own, and it was the user's.</summary>
    [Fact]
    public void ACancelledCopyLeavesTheFileThere()
    {
        var (member, dest) = Setup();

        Assert.Equal("cancelled", FileOps.CopyStreamed(member, dest, overwrite: true, onBytes: _ => { }, cancelled: () => true));

        Assert.Equal("the user's own", File.ReadAllText(Path.Combine(dest, "notes.txt")));
        Assert.Equal(["notes.txt"], Directory.GetFiles(dest).Select(Path.GetFileName));
    }

    [Fact]
    public void ACompletedCopyReplacesItAndLeavesNothingBeside()
    {
        var (member, dest) = Setup();

        Assert.Null(FileOps.CopyStreamed(member, dest, overwrite: true, onBytes: _ => { }, cancelled: () => false));

        Assert.Equal("from the archive", File.ReadAllText(Path.Combine(dest, "notes.txt")));
        Assert.Equal(["notes.txt"], Directory.GetFiles(dest).Select(Path.GetFileName));
    }

    /// <summary>Without overwrite the file there is a refusal, as it was, and stays as it is.</summary>
    [Fact]
    public void WithoutOverwriteTheFileIsLeftAndTheCopyRefused()
    {
        var (member, dest) = Setup();

        Assert.NotNull(FileOps.CopyStreamed(member, dest, overwrite: false, onBytes: _ => { }, cancelled: () => false));

        Assert.Equal("the user's own", File.ReadAllText(Path.Combine(dest, "notes.txt")));
        Assert.Equal(["notes.txt"], Directory.GetFiles(dest).Select(Path.GetFileName));
    }
}
