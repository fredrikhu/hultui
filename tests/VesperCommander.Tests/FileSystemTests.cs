using VesperCommander;
using Xunit;

namespace VesperCommander.Tests;

public class FileSystemTests : IDisposable
{
    private readonly string _dir;

    public FileSystemTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vctest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(Path.Combine(_dir, "zeta_dir"));
        Directory.CreateDirectory(Path.Combine(_dir, "alpha_dir"));
        File.WriteAllText(Path.Combine(_dir, "banana.txt"), "yy");
        File.WriteAllText(Path.Combine(_dir, "apple.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "cherry.txt"), "zzz");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static string[] FileNames(DirListing l) =>
        l.Entries.Where(e => !e.IsParent && !e.IsDir).Select(e => e.Name).ToArray();

    // ── Preview text ─────────────────────────────────────────────────────

    /// <summary>UTF-16 with a byte-order mark is text, even though every other byte is a
    /// NUL — the file says what it is. Windows writes desktop.ini this way, and the preview
    /// pane used to show nothing for it.</summary>
    [Theory]
    [InlineData("utf-16LE")]
    [InlineData("utf-16BE")]
    [InlineData("utf-8")]
    public void PreviewText_DecodesAByteOrderMarkedFile(string encodingName)
    {
        var enc = System.Text.Encoding.GetEncoding(encodingName);
        var bytes = enc.GetPreamble().Concat(enc.GetBytes("[.ShellClassInfo]\r\nIconResource=x\r\n")).ToArray();
        var path = Path.Combine(_dir, "desktop.ini");
        File.WriteAllBytes(path, bytes);

        Assert.Equal("[.ShellClassInfo]\nIconResource=x", FileSystem.PreviewText(path, bytes.Length));
        Assert.Equal("[.ShellClassInfo]\nIconResource=x", FileSystem.PreviewTextFromBytes(bytes));
    }

    [Fact]
    public void PreviewText_IsEmptyForBinary()
    {
        var bytes = new byte[] { (byte)'a', 0, (byte)'b' };
        var path = Path.Combine(_dir, "b.bin");
        File.WriteAllBytes(path, bytes);

        Assert.Equal("", FileSystem.PreviewText(path, bytes.Length));
        Assert.Equal("", FileSystem.PreviewTextFromBytes(bytes));
    }

    [Fact]
    public void NameAscending_IsAlphabetical()
    {
        var l = FileSystem.List(_dir, sort: SortMode.Name, descending: false);
        Assert.Equal(new[] { "apple.txt", "banana.txt", "cherry.txt" }, FileNames(l));
    }

    [Fact]
    public void NameDescending_ReversesWithinFiles()
    {
        var l = FileSystem.List(_dir, sort: SortMode.Name, descending: true);
        Assert.Equal(new[] { "cherry.txt", "banana.txt", "apple.txt" }, FileNames(l));
    }

    [Fact]
    public void SizeDescending_LargestFirst()
    {
        var l = FileSystem.List(_dir, sort: SortMode.Size, descending: true);
        Assert.Equal(new[] { "cherry.txt", "banana.txt", "apple.txt" }, FileNames(l));   // 3,2,1 bytes
    }

    [Fact]
    public void DirsFirst_GroupingSurvivesDescending()
    {
        var l = FileSystem.List(_dir, sort: SortMode.Name, dirsFirst: true, descending: true);
        var nonParent = l.Entries.Where(e => !e.IsParent).ToList();
        // All directories precede all files regardless of direction.
        int lastDir = nonParent.FindLastIndex(e => e.IsDir);
        int firstFile = nonParent.FindIndex(e => !e.IsDir);
        Assert.True(lastDir < firstFile);
    }

    [Fact]
    public void ParentEntry_LeadsRegardlessOfDirection()
    {
        var asc = FileSystem.List(_dir, sort: SortMode.Name, descending: false);
        var desc = FileSystem.List(_dir, sort: SortMode.Name, descending: true);
        Assert.True(asc.Entries[0].IsParent);
        Assert.True(desc.Entries[0].IsParent);
    }
}
