using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

/// <summary>The freedesktop.org trash, pointed at a temp folder: it runs the same on every OS
/// (the Windows Recycle Bin is the shell's, and a test would fill the real one).</summary>
public class TrashTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("meridian-trash-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Make(string relative, string content = "x")
    {
        var path = Path.Combine(_dir, "collection", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void AFileMovesToFilesWithATrashinfoSayingWhereItCameFrom()
    {
        var trash = new FreedesktopTrash(Path.Combine(_dir, "Trash"));
        var path = Make("get charge.req", "GET /x\n");

        Assert.Equal("Trash", trash.Name);
        Assert.Null(trash.Delete(path));

        Assert.False(File.Exists(path));
        Assert.Equal("GET /x\n", File.ReadAllText(Path.Combine(_dir, "Trash", "files", "get charge.req")));
        var info = File.ReadAllText(Path.Combine(_dir, "Trash", "info", "get charge.req.trashinfo"));
        Assert.StartsWith("[Trash Info]\nPath=", info);
        Assert.Contains("get%20charge.req\n", info);   // escaped as a URL path
        Assert.Matches(@"\nDeletionDate=\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\n$", info);
    }

    [Fact]
    public void ASecondFileOfTheSameNameGetsANameOfItsOwn_AndAFolderGoesWhole()
    {
        var trash = new FreedesktopTrash(Path.Combine(_dir, "Trash"));
        Assert.Null(trash.Delete(Make("a/health.req", "one")));
        Assert.Null(trash.Delete(Make("b/health.req", "two")));
        Make("orders/_folder.req");
        Make("orders/list.req");
        Assert.Null(trash.Delete(Path.Combine(_dir, "collection", "orders")));

        var files = Path.Combine(_dir, "Trash", "files");
        Assert.Equal("one", File.ReadAllText(Path.Combine(files, "health.req")));
        Assert.Equal("two", File.ReadAllText(Path.Combine(files, "health.2.req")));
        Assert.True(File.Exists(Path.Combine(_dir, "Trash", "info", "health.2.req.trashinfo")));
        Assert.True(File.Exists(Path.Combine(files, "orders", "_folder.req")));
        Assert.True(File.Exists(Path.Combine(_dir, "Trash", "info", "orders.trashinfo")));
        Assert.False(Directory.Exists(Path.Combine(_dir, "collection", "orders")));
    }

    [Fact]
    public void APathThatIsGoneIsRefusedAndLeavesNoTrashinfo()
    {
        var trash = new FreedesktopTrash(Path.Combine(_dir, "Trash"));
        Assert.NotNull(trash.Delete(Path.Combine(_dir, "collection", "nothing.req")));
        Assert.False(Directory.Exists(Path.Combine(_dir, "Trash", "info"))
            && Directory.EnumerateFiles(Path.Combine(_dir, "Trash", "info")).Any());
    }

    [Fact]
    public void WithoutABinADeleteIsPermanent()
    {
        var trash = new NoTrash();
        var path = Make("x.req");
        Assert.Null(trash.Name);
        Assert.Null(trash.Delete(path));
        Assert.False(File.Exists(path));
    }
}
