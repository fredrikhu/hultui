using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Core.Tests.Rendering;

public class PathDisplayTests
{
    // Deterministic, OS-independent width: 7px per character. Backslash separator is
    // passed explicitly so the Windows-style fixtures behave the same on the Linux CI.
    private static float W(string s) => s.Length * 7f;

    private const string Path = @"C:\Users\me\projects\hultui\src\app";

    [Fact]
    public void FitsBudget_ReturnedUnchanged()
    {
        Assert.Equal(Path, PathDisplay.Collapse(Path, W(Path), W, '\\'));
        Assert.Equal(Path, PathDisplay.Collapse(Path, W(Path) + 50f, W, '\\'));
    }

    [Fact]
    public void TightBudget_KeepsRootAndTail_WithinBudget()
    {
        var collapsed = PathDisplay.Collapse(Path, 120f, W, '\\');

        Assert.NotEqual(Path, collapsed);
        Assert.StartsWith(@"C:\…", collapsed);   // root preserved, middle dropped
        Assert.EndsWith("app", collapsed);        // last segment always kept when it fits
        Assert.True(W(collapsed) <= 120f);        // never exceeds the budget
    }

    [Fact]
    public void ProgressivelyTighterBudget_DropsMoreLeadingSegments()
    {
        var loose = PathDisplay.Collapse(Path, 170f, W, '\\');
        var tight = PathDisplay.Collapse(Path, 110f, W, '\\');

        // A tighter budget keeps fewer trailing segments, so the tight result is shorter.
        Assert.True(tight.Length <= loose.Length);
        Assert.EndsWith("app", loose);
        Assert.EndsWith("app", tight);
    }

    [Fact]
    public void BudgetTooSmallForAnyTail_FallsBackToRootEllipsis()
    {
        // Enough room for "C:\…" (4 chars = 28px) but not "C:\…\app" (8 chars = 56px).
        var result = PathDisplay.Collapse(Path, 40f, W, '\\');
        Assert.Equal(@"C:\…", result);
    }

    [Fact]
    public void TooFewSegments_NotCollapsedEvenWhenOverBudget()
    {
        // Root + one segment: nothing meaningful to drop, so return as-is.
        Assert.Equal(@"C:\file", PathDisplay.Collapse(@"C:\file", 1f, W, '\\'));
    }

    [Fact]
    public void EmptyOrNull_ReturnedAsIs()
    {
        Assert.Equal("", PathDisplay.Collapse("", 10f, W, '\\'));
    }

    [Fact]
    public void NoMeasureContext_FullPathReturned()
    {
        // The ambient overload with no installed context measures width as zero, so a
        // path never "exceeds" the budget and is returned intact rather than mangled.
        TextMeasure.Context = null;
        var result = PathDisplay.Collapse(Path, 50f, fontSize: 13f, separator: '\\');
        Assert.Equal(Path, result);
    }
}
