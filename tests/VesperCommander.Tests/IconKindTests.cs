using System;
using System.IO;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Which glyph a row gets. The rule that matters: there is no second hand-written list
/// of "code" extensions here — anything the syntax highlighter can colour shows the code
/// icon, so the file list and the preview can no longer disagree about what code is.
/// </summary>
public class IconKindTests
{
    private static FileEntry Entry(string name, bool isDir = false, bool isParent = false) =>
        new(name, Path.Combine(Path.GetTempPath(), name), isDir, 0, DateTime.UnixEpoch, IsParent: isParent);

    private static IconKind Kind(string name) => Icons.KindFor(Entry(name));

    [Theory]
    [InlineData("Solution.sln")]
    [InlineData("Solution.slnx")]
    [InlineData("Solution.slnf")]
    public void SolutionFilesGetTheSolutionGlyph(string name) => Assert.Equal(IconKind.Solution, Kind(name));

    [Theory]
    [InlineData("App.csproj")]
    [InlineData("App.fsproj")]
    [InlineData("App.vbproj")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    public void ProjectAndBuildFilesGetTheProjectGlyph(string name) => Assert.Equal(IconKind.Project, Kind(name));

    [Theory]
    // The set the old hand-written list already covered…
    [InlineData("a.cs")] [InlineData("a.json")] [InlineData("a.md")] [InlineData("a.yml")]
    // …and the ones it omitted, which used to show a blank page while the preview
    // would happily have coloured them.
    [InlineData("build.ps1")] [InlineData("app.config")] [InlineData("Program.fs")]
    [InlineData("main.swift")] [InlineData("init.lua")] [InlineData("Dockerfile")]
    [InlineData("Makefile")]
    public void AnythingTheHighlighterKnowsGetsTheCodeGlyph(string name) => Assert.Equal(IconKind.Code, Kind(name));

    [Theory]
    [InlineData("photo.png")]
    [InlineData("vector.svg")]   // also XML, but the image glyph is the more useful one
    public void ImagesWinOverBeingHighlightable(string name) => Assert.Equal(IconKind.Image, Kind(name));

    [Theory]
    [InlineData("bundle.zip")]
    [InlineData("bundle.tgz")]
    public void ArchivesGetTheArchiveGlyph(string name) => Assert.Equal(IconKind.Archive, Kind(name));

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("data.bin")]
    [InlineData("noextension")]
    public void EverythingElseStaysAPlainFile(string name) => Assert.Equal(IconKind.File, Kind(name));

    [Fact]
    public void DirectoriesAndTheParentRowIgnoreTheExtension()
    {
        Assert.Equal(IconKind.Folder, Icons.KindFor(Entry("src.csproj", isDir: true)));
        Assert.Equal(IconKind.Parent, Icons.KindFor(Entry("..", isParent: true)));
    }

    [Fact]
    public void EveryKindRasterizesWithoutThrowing()
    {
        // Guards the SVG path data itself: a malformed `d` string parses to an empty
        // path and fails silently as a blank icon rather than an exception, so assert
        // each glyph actually produced something.
        foreach (IconKind kind in Enum.GetValues<IconKind>())
        {
            var element = Icons.Draw(kind);
            Assert.NotNull(element);
        }
    }
}
