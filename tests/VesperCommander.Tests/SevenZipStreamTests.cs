using System.IO.Compression;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>Runs alone: it points <see cref="SevenZip.ExePath"/>, which every test reads, at 7-Zip.</summary>
[CollectionDefinition(nameof(SevenZipExe), DisableParallelization = true)]
public sealed class SevenZipExe;

/// <summary>Runs where 7-Zip is installed: on PATH, as on a Linux desktop, or where its Windows
/// installer puts it, which is not on PATH.</summary>
public sealed class SevenZipFactAttribute : FactAttribute
{
    public static readonly string? Exe = Find();

    public SevenZipFactAttribute()
    {
        if (Exe is null) Skip = "7-Zip is not installed (the Linux runner needs the 7zip package; see docs/SELF-HOSTED-RUNNERS.md).";
    }

    private static string? Find()
    {
        if (ProgramPath.Resolve("7z") is { } onPath) return onPath;
        if (!OperatingSystem.IsWindows()) return null;
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe");
        return File.Exists(installed) ? installed : null;
    }
}

/// <summary>
/// Reading a member through 7-Zip, which Vesper does for every archive, zips included, once
/// 7-Zip is installed. 7-Zip streams the member to its output, and says it failed only in its
/// exit code (a wrong password, a damaged archive) or, for a member that is not there, only in
/// its messages, exiting 0. The stream ended as if the member were empty either way, so copying
/// one out wrote an empty file and reported success — over the destination, as administrator,
/// in the elevated retry (seen on Fedora as ArchiveElevationStagingTests failing).
/// </summary>
[Collection(nameof(SevenZipExe))]
public sealed class SevenZipStreamTests : IDisposable
{
    private readonly string _saved = SevenZip.ExePath;
    private readonly string _dir = Directory.CreateTempSubdirectory("vesper-7z-").FullName;

    public SevenZipStreamTests() => SevenZip.ExePath = SevenZipFactAttribute.Exe ?? "";

    public void Dispose()
    {
        SevenZip.ExePath = _saved;
        Directory.Delete(_dir, recursive: true);
    }

    private string Zip(params (string Name, string Text)[] members)
    {
        var zip = Path.Combine(_dir, "plain.zip");
        using var z = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (name, text) in members)
        {
            using var w = new StreamWriter(z.CreateEntry(name).Open());
            w.Write(text);
        }
        return zip;
    }

    private static string ReadAll(string archive, string member, string? password)
    {
        using var s = SevenZip.OpenEntry(archive, member, password);
        Assert.NotNull(s);
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    [SevenZipFact]
    public void AMemberReadsAsItsBytes() =>
        Assert.Equal("member contents", ReadAll(Zip(("inside.txt", "member contents")), "inside.txt", null));

    /// <summary>An empty member is empty, not missing: the check must not turn it into an error.</summary>
    [SevenZipFact]
    public void AnEmptyMemberReadsAsEmpty() =>
        Assert.Equal("", ReadAll(Zip(("empty.txt", ""), ("inside.txt", "x")), "empty.txt", null));

    [SevenZipFact]
    public void AMemberThatIsNotThereFailsTheRead()
    {
        var zip = Zip(("inside.txt", "x"));
        var e = Assert.ThrowsAny<IOException>(() => ReadAll(zip, "ghost.txt", null));
        Assert.Contains("not found", e.Message);
    }

    [SevenZipFact]
    public void AWrongPasswordFailsTheRead()
    {
        var source = Path.Combine(_dir, "secret.txt");
        File.WriteAllText(source, "member contents");
        var archive = Path.Combine(_dir, "locked.7z");
        Assert.Null(SevenZip.Create([(source, "secret.txt")], archive, CompressionLevel.Optimal, "right"));

        Assert.Equal("member contents", ReadAll(archive, "secret.txt", "right"));
        var e = Assert.ThrowsAny<IOException>(() => ReadAll(archive, "secret.txt", "wrong"));
        Assert.Equal("Wrong password", e.Message);
    }

    /// <summary>
    /// The read fails only once the destination is open, so writing straight over the file
    /// there emptied it before the failure was known: a wrong password cost the user their file.
    /// It is replaced only once the whole member is in, and nothing is left beside it.
    /// </summary>
    [SevenZipFact]
    public void AFailedReadLeavesTheFileItWouldHaveReplaced()
    {
        var source = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(source, "from the archive");
        var archive = Path.Combine(_dir, "locked.7z");
        Assert.Null(SevenZip.Create([(source, "notes.txt")], archive, CompressionLevel.Optimal, "right"));
        var dest = Directory.CreateDirectory(Path.Combine(_dir, "out")).FullName;
        File.WriteAllText(Path.Combine(dest, "notes.txt"), "the user's own");
        var member = new FileEntry("notes.txt", ArchivePath.Combine(archive, "notes.txt"), IsDir: false, Size: 16, Modified: DateTime.Now);

        var error = FileOps.CopyStreamed(member, dest, overwrite: true, onBytes: _ => { }, cancelled: () => false,
            sevenZip: true, password: "wrong");

        Assert.Equal("Wrong password", error);
        Assert.Equal("the user's own", File.ReadAllText(Path.Combine(dest, "notes.txt")));
        Assert.Equal(["notes.txt"], Directory.GetFiles(dest).Select(Path.GetFileName));

        Assert.Null(FileOps.CopyStreamed(member, dest, overwrite: true, onBytes: _ => { }, cancelled: () => false,
            sevenZip: true, password: "right"));
        Assert.Equal("from the archive", File.ReadAllText(Path.Combine(dest, "notes.txt")));
        Assert.Equal(["notes.txt"], Directory.GetFiles(dest).Select(Path.GetFileName));
    }

    /// <summary>A file the failed copy itself created goes again: an empty file named like the
    /// member would read as a copy that worked.</summary>
    [SevenZipFact]
    public void AFailedReadLeavesNoFileOfItsOwn()
    {
        var zip = Zip(("inside.txt", "x"));
        var dest = Directory.CreateDirectory(Path.Combine(_dir, "out")).FullName;
        var ghost = new FileEntry("ghost.txt", ArchivePath.Combine(zip, "ghost.txt"), IsDir: false, Size: 1, Modified: DateTime.Now);

        Assert.NotNull(FileOps.CopyStreamed(ghost, dest, overwrite: true, onBytes: _ => { }, cancelled: () => false, sevenZip: true));

        Assert.Empty(Directory.GetFiles(dest));
    }

    /// <summary>Through the copy: a member that cannot be read is a failed copy, with a reason,
    /// not a success that leaves an empty file.</summary>
    [SevenZipFact]
    public void CopyingOutAMemberThatIsNotThereFails()
    {
        var zip = Zip(("inside.txt", "x"));
        var dest = Directory.CreateDirectory(Path.Combine(_dir, "out")).FullName;
        var ghost = new FileEntry("ghost.txt", ArchivePath.Combine(zip, "ghost.txt"), IsDir: false, Size: 1, Modified: DateTime.Now);

        var error = FileOps.CopyStreamed(ghost, dest, overwrite: true, onBytes: _ => { }, cancelled: () => false, sevenZip: true);

        Assert.NotNull(error);
    }
}
