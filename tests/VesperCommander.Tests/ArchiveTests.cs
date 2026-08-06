using System.IO.Compression;
using System.Text;
using VesperCommander;
using Xunit;

namespace VesperCommander.Tests;

public class ArchiveTests : IDisposable
{
    private readonly string _dir;
    private readonly string _zip;

    public ArchiveTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vcarc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _zip = Path.Combine(_dir, "assets.zip");
        using var zip = ZipFile.Open(_zip, ZipArchiveMode.Create);
        WriteEntry(zip, "readme.txt", "hello archive");
        WriteEntry(zip, "textures/palette.png", "PNGDATA");
        WriteEntry(zip, "textures/hi_res/stone.png", "STONE");
        WriteEntry(zip, "models/crate.obj", "OBJ");
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name);
        using var s = new StreamWriter(e.Open());
        s.Write(content);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    // ── ArchivePath ──────────────────────────────────────────────────────
    [Fact]
    public void TryParse_ArchiveRoot()
    {
        var r = ArchivePath.TryParse(_zip);
        Assert.NotNull(r);
        Assert.Equal(_zip, r!.Value.ArchiveFile);
        Assert.Equal("", r.Value.Internal);
    }

    [Fact]
    public void TryParse_NestedInside()
    {
        var r = ArchivePath.TryParse(Path.Combine(_zip, "textures", "hi_res"));
        Assert.NotNull(r);
        Assert.Equal(_zip, r!.Value.ArchiveFile);
        Assert.Equal(Path.Combine("textures", "hi_res"), r.Value.Internal);
    }

    [Fact]
    public void TryParse_RealPath_IsNull() =>
        Assert.Null(ArchivePath.TryParse(_dir));

    [Fact]
    public void TryParse_ArchiveExtButMissing_IsNull() =>
        Assert.Null(ArchivePath.TryParse(@"C:\nope\ghost.zip\inside"));

    [Fact]
    public void ParentOf_RootIsContainingFolder() =>
        Assert.Equal(_dir, ArchivePath.ParentOf(_zip, ""));

    [Fact]
    public void ParentOf_NestedIsArchiveRoot() =>
        Assert.Equal(_zip, ArchivePath.ParentOf(_zip, "textures"));

    // ── Capabilities matrix ──────────────────────────────────────────────
    [Fact]
    public void Caps_BuiltinZip_ReadWriteNoEncrypt()
    {
        var c = ArchiveVfs.Caps(".zip", sevenZip: false);
        Assert.True(c.Read); Assert.True(c.Write); Assert.False(c.Encrypt);
    }

    [Fact]
    public void Caps_Builtin7z_None()
    {
        var c = ArchiveVfs.Caps(".7z", sevenZip: false);
        Assert.False(c.Read); Assert.False(c.Write);
    }

    [Fact]
    public void Caps_SevenZip_EncryptAndWrite()
    {
        var c = ArchiveVfs.Caps(".7z", sevenZip: true);
        Assert.True(c.Read); Assert.True(c.Write); Assert.True(c.Encrypt);
    }

    [Fact]
    public void Caps_Rar_ReadOnlyEvenWith7z()
    {
        var c = ArchiveVfs.Caps(".rar", sevenZip: true);
        Assert.True(c.Read); Assert.False(c.Write);
    }

    [Fact]
    public void Caps_BuiltinTarGz_ReadOnly()
    {
        var c = ArchiveVfs.Caps(".gz", sevenZip: false);
        Assert.True(c.Read); Assert.False(c.Write);
    }

    // ── Listing ──────────────────────────────────────────────────────────
    [Fact]
    public void ListZip_Root_ShowsDirsThenFiles()
    {
        var l = ArchiveVfs.ListEntries(_zip, "", null, sevenZip: false);
        Assert.Equal(ArchiveStatus.Ok, l.Status);
        Assert.True(l.Entries[0].IsParent);
        var names = l.Entries.Where(e => !e.IsParent).Select(e => e.Name).ToArray();
        Assert.Contains("models", names);
        Assert.Contains("textures", names);
        Assert.Contains("readme.txt", names);
        // Directories precede files.
        var nonParent = l.Entries.Where(e => !e.IsParent).ToList();
        Assert.True(nonParent.FindLastIndex(e => e.IsDir) < nonParent.FindIndex(e => !e.IsDir));
    }

    [Fact]
    public void ListZip_Nested_ShowsChildLevelOnly()
    {
        var l = ArchiveVfs.ListEntries(_zip, "textures", null, sevenZip: false);
        var names = l.Entries.Where(e => !e.IsParent).Select(e => e.Name).ToArray();
        Assert.Contains("hi_res", names);        // subfolder
        Assert.Contains("palette.png", names);   // file at this level
        Assert.DoesNotContain("stone.png", names); // that's one level deeper
    }

    [Fact]
    public void ListZip_MemberHasPackedSize()
    {
        var l = ArchiveVfs.ListEntries(_zip, "", null, sevenZip: false);
        var readme = l.Entries.Single(e => e.Name == "readme.txt");
        Assert.True(readme.PackedSize >= 0);
        Assert.True(readme.Size > 0);
    }

    [Fact]
    public void ListZip_EmptyArchive_ReportsEmpty()
    {
        var empty = Path.Combine(_dir, "empty.zip");
        using (ZipFile.Open(empty, ZipArchiveMode.Create)) { }
        var l = ArchiveVfs.ListEntries(empty, "", null, sevenZip: false);
        Assert.Equal(ArchiveStatus.Empty, l.Status);
    }

    [Fact]
    public void ListZip_Corrupt_ReportsCorrupt()
    {
        var bad = Path.Combine(_dir, "bad.zip");
        File.WriteAllText(bad, "not a real zip");
        var l = ArchiveVfs.ListEntries(bad, "", null, sevenZip: false);
        Assert.Equal(ArchiveStatus.Corrupt, l.Status);
    }

    // ── Open + Create round-trips ────────────────────────────────────────
    [Fact]
    public void OpenEntry_ReadsMemberBytes()
    {
        using var s = ArchiveVfs.OpenEntry(_zip, "readme.txt", null, sevenZip: false);
        Assert.NotNull(s);
        using var r = new StreamReader(s!);
        Assert.Equal("hello archive", r.ReadToEnd());
    }

    [Fact]
    public void CopyStreamed_ExtractsArchiveMember()
    {
        var outDir = Path.Combine(_dir, "out");
        var member = new FileEntry("readme.txt", ArchivePath.Combine(_zip, "readme.txt"), IsDir: false, 0, DateTime.Now);
        var err = FileOps.CopyStreamed(member, outDir, overwrite: true, onBytes: _ => { }, cancelled: () => false);
        Assert.Null(err);
        Assert.Equal("hello archive", File.ReadAllText(Path.Combine(outDir, "readme.txt")));
    }

    [Fact]
    public void CopyStreamed_ExtractsArchiveFolderRecursively()
    {
        var outDir = Path.Combine(_dir, "outf");
        var folder = new FileEntry("textures", ArchivePath.Combine(_zip, "textures"), IsDir: true, 0, DateTime.Now);
        var err = FileOps.CopyStreamed(folder, outDir, overwrite: true, onBytes: _ => { }, cancelled: () => false);
        Assert.Null(err);
        Assert.True(File.Exists(Path.Combine(outDir, "textures", "palette.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "textures", "hi_res", "stone.png")));
    }

    [Fact]
    public async Task Find_DescendsIntoArchivesWhenEnabled()
    {
        var hits = new List<FindEngine.Hit>();
        var opts = new FindEngine.Options("*.png", null, Recursive: true, MatchCase: false,
            Regex: false, Hidden: false, Archives: true, SevenZip: false);
        await FindEngine.Run(_dir, opts, h => { lock (hits) hits.Add(h); }, (_, _) => { }, _ => { },
            System.Threading.CancellationToken.None);
        Assert.Contains(hits, h => h.Name == "palette.png" && ArchivePath.InArchive(h.ParentDir));
    }

    [Fact]
    public async Task Find_ContentSearchInsideArchive()
    {
        var hits = new List<FindEngine.Hit>();
        // "archive" appears in readme.txt's content ("hello archive"); the pattern matches every
        // name, so this exercises content-grep of members — and that a name-matched archive
        // isn't walked twice (the member is found exactly once).
        var opts = new FindEngine.Options("*", "archive", Recursive: true, MatchCase: false,
            Regex: false, Hidden: false, Archives: true, SevenZip: false);
        await FindEngine.Run(_dir, opts, h => { lock (hits) hits.Add(h); }, (_, _) => { }, _ => { },
            System.Threading.CancellationToken.None);
        var readme = hits.Where(h => h.Name == "readme.txt").ToList();
        Assert.Single(readme);
        Assert.True(readme[0].ContentHits >= 1);
        Assert.DoesNotContain(hits, h => h.Name == "palette.png");   // "PNGDATA" has no "archive"
    }

    [Fact]
    public async Task Find_SkipsArchivesWhenDisabled()
    {
        var hits = new List<FindEngine.Hit>();
        var opts = new FindEngine.Options("palette.png", null, Recursive: true, MatchCase: false,
            Regex: false, Hidden: false, Archives: false, SevenZip: false);
        await FindEngine.Run(_dir, opts, h => { lock (hits) hits.Add(h); }, (_, _) => { }, _ => { },
            System.Threading.CancellationToken.None);
        Assert.DoesNotContain(hits, h => h.Name == "palette.png");   // it's only inside the archive
    }

    [Fact]
    public void Nested_ListAndOpenThroughChain()
    {
        // inner.zip (with a nested folder) embedded inside outer.zip.
        var innerPath = Path.Combine(_dir, "inner.zip");
        using (var inner = ZipFile.Open(innerPath, ZipArchiveMode.Create))
            WriteEntry(inner, "deep/hello.txt", "nested content");
        var outerPath = Path.Combine(_dir, "outer.zip");
        using (var outer = ZipFile.Open(outerPath, ZipArchiveMode.Create))
            outer.CreateEntryFromFile(innerPath, "inner.zip");

        // List the root of the nested archive via the outer, and its subfolder.
        var root = ArchiveVfs.ListEntries(outerPath, "inner.zip", null, sevenZip: false);
        Assert.Equal(ArchiveStatus.Ok, root.Status);
        Assert.Contains(root.Entries, e => e.Name == "deep" && e.IsDir);
        var deep = ArchiveVfs.ListEntries(outerPath, @"inner.zip\deep", null, sevenZip: false);
        Assert.Contains(deep.Entries, e => e.Name == "hello.txt");

        // Entry FullPaths stay virtual (outer\inner\…), so navigation is seamless.
        var member = deep.Entries.First(e => e.Name == "hello.txt");
        Assert.Equal(Path.Combine(outerPath, "inner.zip", "deep", "hello.txt"), member.FullPath);

        // Open a member through the whole chain.
        using (var s = ArchiveVfs.OpenEntry(outerPath, @"inner.zip\deep\hello.txt", null, sevenZip: false))
        {
            Assert.NotNull(s);
            using var r = new StreamReader(s!);
            Assert.Equal("nested content", r.ReadToEnd());
        }
        ArchiveVfs.CleanupTemp();
    }

    [Fact]
    public void Create_BuiltinZip_RoundTrips()
    {
        var src = Path.Combine(_dir, "note.txt");
        File.WriteAllText(src, "packed content");
        var outZip = Path.Combine(_dir, "made.zip");
        var err = ArchiveVfs.Create([(src, "note.txt")], outZip, CompressionLevel.Optimal, null, sevenZip: false);
        Assert.Null(err);
        Assert.True(File.Exists(outZip));
        var l = ArchiveVfs.ListEntries(outZip, "", null, sevenZip: false);
        Assert.Contains(l.Entries, e => e.Name == "note.txt");
    }
}
