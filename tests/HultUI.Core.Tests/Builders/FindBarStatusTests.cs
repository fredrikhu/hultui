using HultUI.Core.Builders;
using Xunit;

namespace HultUI.Core.Tests.Builders;

/// <summary>What <see cref="FindBar"/>'s count says. A broken pattern and a search that stopped
/// at the cap must not read as an ordinary answer.</summary>
public class FindBarStatusTests
{
    private static TextSearch Over(params string[] lines)
    {
        var search = new TextSearch(static () => { });
        search.Bind(lines.Length, 1, (line, _) => line < lines.Length ? lines[line] : null);
        return search;
    }

    [Fact]
    public void NothingTypedSaysNothing() => Assert.Equal("", FindBar.Status(Over("abc")));

    [Fact]
    public void CountsFromOne()
    {
        var search = Over("abc", "abc", "xyz");
        search.SetQuery("abc");
        Assert.Equal("1 of 2", FindBar.Status(search));
    }

    [Fact]
    public void NoMatchesIsSaid()
    {
        var search = Over("abc");
        search.SetQuery("zzz");
        Assert.Equal("no matches", FindBar.Status(search));
    }

    /// <summary>A pattern that does not compile is about what was typed, not about the text.</summary>
    [Fact]
    public void ABrokenPatternSaysWhy()
    {
        var search = Over("abc");
        search.SetRegex(true);
        search.SetQuery("(");
        Assert.NotNull(search.Error);
        Assert.Equal(search.Error, FindBar.Status(search));
    }

    [Fact]
    public void AScanStoppedAtTheCapSaysSo()
    {
        var search = Over(Enumerable.Repeat("x", TextSearch.MaxHits + 10).ToArray());
        search.SetQuery("x");
        Assert.True(search.Capped);
        Assert.Equal($"1 of {TextSearch.MaxHits:N0}+", FindBar.Status(search));
    }
}
