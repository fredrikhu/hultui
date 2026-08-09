using System.Linq;
using HultUI.Core.Primitives;
using HultUI.Icons;
using Xunit;

namespace HultUI.IconsTests;

/// <summary>
/// The bundled Phosphor set. An unknown name renders an empty box rather than throwing —
/// deliberately, so one typo cannot crash a menu — which means nothing at runtime would
/// tell you a name is wrong. These tests are what does.
/// </summary>
public class PhosphorTests
{
    [Fact]
    public void TheBundleIsNotEmpty()
    {
        Assert.NotEmpty(Phosphor.Names);
        Assert.True(Phosphor.Has("git-branch"));
    }

    [Fact]
    public void EveryBundledIconRastersToVisiblePixels()
    {
        // A `d` string that fails to parse yields an empty path, which draws nothing and
        // reports no error. Rasterize each and require some coverage.
        foreach (var name in Phosphor.Names)
        {
            var src = Phosphor.Source(name, 24f, Color.White);
            Assert.NotNull(src);
            Assert.True(src!.IsLoaded, name);
            Assert.True(src.IntrinsicSize.Width > 0 && src.IntrinsicSize.Height > 0, name);
        }
    }

    [Fact]
    public void AnUnknownNameIsSurvivable()
    {
        Assert.False(Phosphor.Has("not-a-real-icon"));
        Assert.Null(Phosphor.Source("not-a-real-icon", 16f, Color.White));

        // Draw still returns a correctly sized element, so a bad name costs the glyph
        // and not the layout around it.
        var element = Phosphor.Draw("not-a-real-icon", 16f, Color.White);
        Assert.NotNull(element);
    }

    [Fact]
    public void RastersAreCachedPerNameColourAndSize()
    {
        var a = Phosphor.Source("git-branch", 16f, Color.White);
        var b = Phosphor.Source("git-branch", 16f, Color.White);
        Assert.Same(a, b);

        Assert.NotSame(a, Phosphor.Source("git-branch", 16f, Color.Red));
        Assert.NotSame(a, Phosphor.Source("git-branch", 32f, Color.White));
    }
}

/// <summary>
/// Junction names icons by Phosphor name in string constants. A name that is not in the
/// bundle draws an empty box and reports nothing, so nothing at runtime would catch a
/// typo or a rename of the generated set — this does.
/// </summary>
public class JunctionIconNameTests
{
    public static TheoryData<string, string> Constants()
    {
        var data = new TheoryData<string, string>();
        foreach (var f in typeof(GitClient.Junction.Icons).GetFields(
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
        foreach (var key in GitClient.Junction.Icons.WorkspaceKeys)
            Assert.True(Phosphor.Has(key), key);
    }
}
