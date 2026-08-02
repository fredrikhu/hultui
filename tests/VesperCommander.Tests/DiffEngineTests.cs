using VesperCommander;
using Xunit;

namespace VesperCommander.Tests;

public class DiffEngineTests
{
    private static string[] L(params string[] lines) => lines;

    [Fact]
    public void IdenticalFiles_ReportIdentical()
    {
        var r = DiffEngine.Diff(L("a", "b", "c"), L("a", "b", "c"));
        Assert.True(r.Identical);
        Assert.Equal(0, r.Adds + r.Dels + r.Changes);
        Assert.All(r.Lines, l => Assert.Equal(DiffEngine.Op.Equal, l.Op));
    }

    [Fact]
    public void ChangedLine_PairsIntoOneChangeRow()
    {
        var r = DiffEngine.Diff(L("a", "b", "c"), L("a", "B", "c"));
        Assert.Equal(1, r.Changes);
        Assert.Equal(0, r.Adds);
        Assert.Equal(0, r.Dels);
        var chg = Assert.Single(r.Lines, l => l.Op == DiffEngine.Op.Change);
        Assert.Equal("b", chg.Left);
        Assert.Equal("B", chg.Right);
        Assert.Equal(2, chg.LeftNo);
        Assert.Equal(2, chg.RightNo);
    }

    [Fact]
    public void PureInsertion_IsAdd_WithNoLeft()
    {
        var r = DiffEngine.Diff(L("a", "c"), L("a", "b", "c"));
        Assert.Equal(1, r.Adds);
        Assert.Equal(0, r.Dels);
        Assert.Equal(0, r.Changes);
        var add = Assert.Single(r.Lines, l => l.Op == DiffEngine.Op.Add);
        Assert.Null(add.Left);
        Assert.Equal("b", add.Right);
        Assert.Equal(0, add.LeftNo);
    }

    [Fact]
    public void PureDeletion_IsDel_WithNoRight()
    {
        var r = DiffEngine.Diff(L("a", "b", "c"), L("a", "c"));
        Assert.Equal(1, r.Dels);
        Assert.Equal(0, r.Adds);
        var del = Assert.Single(r.Lines, l => l.Op == DiffEngine.Op.Del);
        Assert.Equal("b", del.Left);
        Assert.Null(del.Right);
        Assert.Equal(0, del.RightNo);
    }

    [Fact]
    public void EqualRuns_KeepBothLineNumbers()
    {
        var r = DiffEngine.Diff(L("x", "y"), L("x", "y"));
        Assert.Collection(r.Lines,
            l => { Assert.Equal(1, l.LeftNo); Assert.Equal(1, l.RightNo); },
            l => { Assert.Equal(2, l.LeftNo); Assert.Equal(2, l.RightNo); });
    }

    [Fact]
    public void IntraSpan_FindsCommonPrefixAndSuffix()
    {
        var (prefix, suffix) = DiffEngine.IntraSpan("the quick fox", "the slow fox");
        Assert.Equal(4, prefix);   // "the "
        Assert.Equal(4, suffix);   // " fox"
    }

    [Fact]
    public void TooLarge_TrueOnlyForHugePairs()
    {
        Assert.False(DiffEngine.TooLarge(new string[10], new string[10]));
        Assert.True(DiffEngine.TooLarge(new string[5000], new string[5000]));
    }
}
