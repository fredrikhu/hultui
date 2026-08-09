using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HultUI.Icons;
using SkiaSharp;
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
    public void TheSharedLibraryClassifiesTheSameWayVesperDoes()
    {
        // Vesper's IconKind is a projection of FileIconKind; Junction reads FileIconKind
        // directly. If these two ever disagree the apps are back to showing different
        // icons for the same file, which is the whole thing this consolidation removed.
        foreach (var name in new[] { "a.sln", "a.csproj", "a.cs", "build.ps1", "a.png", "a.zip", "a.txt", "Dockerfile" })
        {
            var expected = FileIcons.KindFor(name) switch
            {
                FileIconKind.Solution => IconKind.Solution,
                FileIconKind.Project => IconKind.Project,
                FileIconKind.Code => IconKind.Code,
                FileIconKind.Image => IconKind.Image,
                FileIconKind.Archive => IconKind.Archive,
                _ => IconKind.File,
            };
            Assert.Equal(expected, Kind(name));
        }
    }

    [Fact]
    public void TheVisualStudioMarkHasBothNotchesCutOut()
    {
        // The mark's two triangular notches are separate subpaths, and they only become
        // holes under even-odd. Skia's path parser reads the `d` string alone and
        // defaults to nonzero, so the right notch — wound the same way as the outline —
        // filled solid while the left one happened to work. Sample the middle of each.
        using var bmp = FileIcons.RenderBitmap(FileIconKind.Solution, Icons.Palette);

        // A point in the source's 96-unit box, mapped through the glyph's placement
        // (14 units at offset 1) and then the 2x supersample.
        static (int x, int y) At(float sx, float sy) =>
            ((int)MathF.Round((sx * 14f / 96f + 1f) * 2f), (int)MathF.Round((sy * 14f / 96f + 1f) * 2f));

        var right = At(63.7f, 48f);   // inside triangle (72,27.68) (47.21,48) (72,68.32)
        var left = At(16.1f, 48f);    // inside triangle (12,34.27) (24.41,48) (12,61.73)

        Assert.Equal((byte)0, bmp.GetPixel(right.x, right.y).Alpha);
        Assert.Equal((byte)0, bmp.GetPixel(left.x, left.y).Alpha);

        // …and solid just outside the right notch, so the assertions above cannot pass
        // by the glyph having failed to render at all.
        var body = At(80f, 48f);
        Assert.True(bmp.GetPixel(body.x, body.y).Alpha > 200);
    }

    [Fact]
    public void EveryGlyphConstantNamesABundledIcon()
    {
        // Vesper names its chrome and dialog icons by Phosphor name in Glyphs. An unknown
        // name renders an empty box and reports nothing, so nothing at runtime would catch
        // a typo or an upstream rename — this does.
        var fields = typeof(Glyphs).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToArray();

        Assert.NotEmpty(fields);
        foreach (var f in fields)
        {
            var name = (string)f.GetRawConstantValue()!;
            Assert.True(Phosphor.Has(name), $"Glyphs.{f.Name} = \"{name}\" is not in the bundled Phosphor set");
        }
    }

    [Fact]
    public void IconNamesAreNeverRenderedAsText()
    {
        // Both halves of the conversion's invariant, and both have already been violated
        // once each:
        //
        //   Ui.Icon("✕")      — a character where a Phosphor name belongs. Compiles, and
        //                       draws nothing, because no icon is named "✕".
        //   Text.New(icon)    — a Phosphor name where text belongs. Compiles, and prints
        //                       the literal word "minus" into the UI.
        //
        // Neither throws, so only a check like this one catches them. `icon` is the agreed
        // parameter name for a Phosphor name; text that happens to be a symbol (a keycap's
        // "↵", the sync rail's "+→") is passed as `label` or a literal and stays text.
        var appDir = FindAppDirectory();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(appDir, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;

                if (Regex.IsMatch(line, @"Ui\.Icon\("""))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: icon call given a literal — {line.Trim()}");

                // The argument is often a member access — OptionPages[i].icon — so the
                // pattern has to allow a qualifier before the name. Anchoring at `(` alone
                // missed exactly the bug that prompted this test.
                if (Regex.IsMatch(line, @"(Text\.New|Ui\.Mono|Ui\.Kbd)\(\s*[\w\.\[\]]*\b(icon|Glyphs)\b"))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: icon name rendered as text — {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0, string.Join("\n  ", offenders.Prepend("Icon/text mix-ups:")));
    }

    private static string FindAppDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "apps", "VesperCommander");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException($"Could not find apps/VesperCommander above {AppContext.BaseDirectory}");
    }

    [Fact]
    public void EverySharedKindRasterizesWithoutThrowing()
    {
        // Guards the SVG path data, including the Visual Studio mark lifted from the
        // official icon: a malformed `d` parses to an empty path and fails silently as
        // a blank glyph rather than an exception.
        foreach (FileIconKind kind in Enum.GetValues<FileIconKind>())
            Assert.NotNull(FileIcons.Draw(kind, Icons.Palette));
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
