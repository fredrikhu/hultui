using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

public class CollectionWorkspaceTests
{
    [Fact]
    public void AnExistingDirectoryOpens()
    {
        var dir = Directory.CreateTempSubdirectory("meridian-ws-").FullName;
        try
        {
            var ws = CollectionWorkspace.TryOpen(dir);
            Assert.NotNull(ws);
            Assert.Equal(Path.GetFullPath(dir), ws!.Root);
            Assert.Equal(Path.GetFileName(dir), ws.Name);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public void AMissingPathDoesNot()
    {
        Assert.Null(CollectionWorkspace.TryOpen(Path.Combine(Path.GetTempPath(), "meridian-no-such-dir-x9")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankInputIsNotAWorkspace(string? path)
    {
        Assert.Null(CollectionWorkspace.TryOpen(path));
    }

    [Fact]
    public void TheNameIsTheFolderEvenWithATrailingSeparator()
    {
        var dir = Directory.CreateTempSubdirectory("meridian-ws-").FullName;
        try
        {
            var ws = CollectionWorkspace.TryOpen(dir + Path.DirectorySeparatorChar);
            Assert.Equal(Path.GetFileName(dir), ws!.Name);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }
}
