using HultUI.Core.Layout;
using Xunit;

namespace HultUI.Core.Tests.Layout;

public class GridTemplateTests
{
    [Fact]
    public void Parse_FixedPx()
    {
        var tracks = GridTemplate.Parse("100px 200px");
        Assert.Equal(2, tracks.Length);
        Assert.Equal(GridTrackKind.Fixed, tracks[0].Kind);
        Assert.Equal(100, tracks[0].Value);
        Assert.Equal(200, tracks[1].Value);
    }

    [Fact]
    public void Parse_BareNumberIsPixels()
    {
        var tracks = GridTemplate.Parse("100 200");
        Assert.Equal(GridTrackKind.Fixed, tracks[0].Kind);
        Assert.Equal(100, tracks[0].Value);
        Assert.Equal(200, tracks[1].Value);
    }

    [Fact]
    public void Parse_Percent()
    {
        var tracks = GridTemplate.Parse("25% 75%");
        Assert.Equal(GridTrackKind.Percent, tracks[0].Kind);
        Assert.Equal(25, tracks[0].Value);
        Assert.Equal(75, tracks[1].Value);
    }

    [Fact]
    public void Parse_Auto()
    {
        var tracks = GridTemplate.Parse("auto auto");
        Assert.All(tracks, t => Assert.Equal(GridTrackKind.Auto, t.Kind));
    }

    [Fact]
    public void Parse_Fr()
    {
        var tracks = GridTemplate.Parse("1fr 2fr");
        Assert.Equal(GridTrackKind.Fr, tracks[0].Kind);
        Assert.Equal(1, tracks[0].Value);
        Assert.Equal(2, tracks[1].Value);
    }

    [Fact]
    public void Parse_BareFrIsOneFr()
    {
        var tracks = GridTemplate.Parse("fr fr");
        Assert.All(tracks, t =>
        {
            Assert.Equal(GridTrackKind.Fr, t.Kind);
            Assert.Equal(1, t.Value);
        });
    }

    [Fact]
    public void Parse_MixedTemplate()
    {
        var tracks = GridTemplate.Parse("200px auto 1fr 2fr 50%");
        Assert.Equal(5, tracks.Length);
        Assert.Equal(GridTrackKind.Fixed, tracks[0].Kind);
        Assert.Equal(GridTrackKind.Auto, tracks[1].Kind);
        Assert.Equal(GridTrackKind.Fr, tracks[2].Kind);
        Assert.Equal(GridTrackKind.Fr, tracks[3].Kind);
        Assert.Equal(2, tracks[3].Value);
        Assert.Equal(GridTrackKind.Percent, tracks[4].Kind);
    }

    [Fact]
    public void Parse_TolerantOfExtraWhitespace()
    {
        var tracks = GridTemplate.Parse("  100px   auto   1fr  ");
        Assert.Equal(3, tracks.Length);
    }

    [Fact]
    public void Parse_CaseInsensitive()
    {
        var tracks = GridTemplate.Parse("100PX AUTO 1FR");
        Assert.Equal(GridTrackKind.Fixed, tracks[0].Kind);
        Assert.Equal(GridTrackKind.Auto, tracks[1].Kind);
        Assert.Equal(GridTrackKind.Fr, tracks[2].Kind);
    }

    [Fact]
    public void Parse_FractionalValues()
    {
        var tracks = GridTemplate.Parse("12.5px 0.5fr");
        Assert.Equal(12.5f, tracks[0].Value);
        Assert.Equal(0.5f, tracks[1].Value);
    }

    [Fact]
    public void Parse_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => GridTemplate.Parse(""));
        Assert.Throws<ArgumentException>(() => GridTemplate.Parse("   "));
    }

    [Fact]
    public void Parse_GarbageToken_Throws()
    {
        var ex = Assert.Throws<FormatException>(() => GridTemplate.Parse("100px floomp"));
        Assert.Contains("floomp", ex.Message);
    }
}
