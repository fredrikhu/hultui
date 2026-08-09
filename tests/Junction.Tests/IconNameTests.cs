using System.Text.RegularExpressions;
using HultUI.Icons;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Junction names icons by Phosphor name in string constants. A name that is not in the
/// bundle draws an empty box and reports nothing, so nothing at runtime would catch a
/// typo or a rename of the generated set — this does.
/// </summary>
public class IconNameTests
{
    public static TheoryData<string, string> Constants()
    {
        var data = new TheoryData<string, string>();
        foreach (var f in typeof(Icons).GetFields(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (f.IsLiteral && f.FieldType == typeof(string) && f.GetRawConstantValue() is string v)
                data.Add(f.Name, v);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Constants))]
    public void EveryIconConstantNamesABundledIcon(string constant, string iconName)
    {
        Assert.True(Phosphor.Has(iconName), $"Icons.{constant} = \"{iconName}\" is not in the bundled Phosphor set");
    }

    [Fact]
    public void TheWorkspacePickerKeysAllResolve()
    {
        // These are stored in user settings and mapped to glyphs at render time, so a
        // missing one shows an empty square in the picker rather than failing loudly.
        foreach (var key in Icons.WorkspaceKeys)
            Assert.True(Phosphor.Has(key), key);
    }

    [Fact]
    public void IconNamesAreNeverRenderedAsText()
    {
        // Junction is clean today; this keeps it that way. Once icons are named strings,
        // two mistakes compile and neither throws:
        //
        //   Ui.Icon("✕")            — a character where a name belongs → draws nothing
        //   Text.New(Icons.Branch)  — a name where text belongs → prints "git-branch"
        //
        // Both happened during Vesper's conversion. The qualifier in the pattern matters:
        // the argument is usually a member access, and a version anchored at the paren
        // passed against the real defect.
        //
        // Junction keeps genuine text that happens to be a symbol — "—" for a missing
        // value, "·" between metadata, "↵" in a keycap hint. Those are literals, not
        // icon names, so they do not match.
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(TestPaths.AppDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;

                if (Regex.IsMatch(line, @"Ui\.Icon\("""))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: icon call given a literal — {line.Trim()}");

                if (Regex.IsMatch(line, @"(Text\.New|Ui\.Mono|Kbd)\(\s*[\w\.\[\]]*\bIcons\.\w"))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: icon name rendered as text — {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0, string.Join("\n  ", offenders.Prepend("Icon/text mix-ups:")));
    }
}

/// <summary>
/// The nominal-to-box size conversion. Junction's icon sizes were chosen when icons were
/// text glyphs, and a vector fills more of its box than a glyph fills its em — so reusing
/// the numbers unchanged drew everything oversized.
/// </summary>
public class IconSizeTests
{
    [Fact]
    public void NominalSizesShrinkToTheirVisualEquivalent()
    {
        // Measured: the Unicode stand-ins inked ~64% of the font size, Phosphor paths ink
        // ~72% of the box. Under, but close.
        Assert.True(Ui.IconBox(16f) < 16f);
        Assert.InRange(Ui.IconBox(16f) / 16f, 0.85f, 0.92f);
    }

    [Fact]
    public void TheRelativeSizeHierarchyIsPreserved()
    {
        // One ratio applied uniformly, so the design's relationships — a small badge
        // against a larger button glyph — survive the correction.
        float badge = Ui.IconBox(9f);
        float row = Ui.IconBox(12f);
        float button = Ui.IconBox(15f);
        Assert.True(badge < row && row < button);
        Assert.Equal(12f / 9f, row / badge, 1);
    }

    [Fact]
    public void SizesLandOnHalfPixels()
    {
        // Rounded to halves so rasters land on sensible pixel counts instead of
        // producing a slightly different bitmap per call site.
        foreach (var n in new[] { 9f, 10f, 11f, 12f, 13f, 13.5f, 14f, 15f, 16f, 17f, 26f })
            Assert.Equal(0f, Ui.IconBox(n) * 2f % 1f);
    }
}
