using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Properties that must hold for any correct diff, whatever algorithm produces it.
/// <para>
/// A minimal edit script is not unique — two correct diffs of the same pair can differ — so
/// tests that pin an exact op sequence can fail on a change that is perfectly right. These
/// pin the invariants instead: the rows must reconstruct both files, the line numbers must
/// stay in order and match the source, and identical input must produce no changes. That is
/// what tells a real regression from a different-but-valid answer.
/// </para>
/// </summary>
public class DiffEnginePropertyTests
{
    private static void AssertReconstructs(string[] a, string[] b)
    {
        var result = DiffEngine.Diff(a, b);

        // Every left-bearing row, in order, is the left file; same for the right.
        var left = result.Lines.Where(l => l.Left != null).Select(l => l.Left!).ToArray();
        var right = result.Lines.Where(l => l.Right != null).Select(l => l.Right!).ToArray();
        Assert.Equal(a, left);
        Assert.Equal(b, right);

        // Line numbers are 1-based, strictly increasing on each side, and name the row's text.
        int lastLeft = 0, lastRight = 0;
        foreach (var line in result.Lines)
        {
            if (line.Left != null)
            {
                Assert.True(line.LeftNo > lastLeft, $"left line numbers went backwards at {line.LeftNo}");
                Assert.Equal(a[line.LeftNo - 1], line.Left);
                lastLeft = line.LeftNo;
            }
            else Assert.Equal(0, line.LeftNo);

            if (line.Right != null)
            {
                Assert.True(line.RightNo > lastRight, $"right line numbers went backwards at {line.RightNo}");
                Assert.Equal(b[line.RightNo - 1], line.Right);
                lastRight = line.RightNo;
            }
            else Assert.Equal(0, line.RightNo);
        }

        Assert.Equal(a.SequenceEqual(b), result.Identical);
    }

    [Fact]
    public void IdenticalFilesReconstructAndReportNoChange()
    {
        var f = new[] { "one", "two", "three" };
        AssertReconstructs(f, f.ToArray());
    }

    [Fact]
    public void AnEmptySideStillReconstructs()
    {
        AssertReconstructs([], ["added"]);
        AssertReconstructs(["removed"], []);
        AssertReconstructs([], []);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1234)]
    public void RandomEditsAlwaysReconstructBothSides(int seed)
    {
        var rng = new Random(seed);
        int n = rng.Next(0, 60);
        var a = Enumerable.Range(0, n).Select(i => $"line {rng.Next(0, 12)} at {i}").ToArray();

        // Derive b by a random walk of keeps, edits, deletes and inserts.
        var b = new List<string>();
        foreach (var line in a)
        {
            switch (rng.Next(0, 4))
            {
                case 0: break;                                   // delete
                case 1: b.Add(line + " CHANGED"); break;         // edit
                case 2: b.Add($"inserted {rng.Next()}"); b.Add(line); break;
                default: b.Add(line); break;                     // keep
            }
        }

        AssertReconstructs(a, b.ToArray());
    }

    [Fact]
    public void ARunOfDeletesFollowedByInsertsPairsIntoChanges()
    {
        // The pairing the side-by-side view depends on: adjacent delete+insert runs line up
        // as Change rows rather than drifting apart into separate blocks.
        var result = DiffEngine.Diff(["same", "old1", "old2", "tail"], ["same", "new1", "new2", "tail"]);

        Assert.Equal(2, result.Changes);
        Assert.Equal(0, result.Adds);
        Assert.Equal(0, result.Dels);
        AssertReconstructs(["same", "old1", "old2", "tail"], ["same", "new1", "new2", "tail"]);
    }

    [Fact]
    public void ALargeComparisonIsNoLongerRefused()
    {
        // The old LCS table needed n·m ints — 40GB at this size — so anything past a few
        // thousand lines was rejected outright.
        var a = Enumerable.Range(0, 15_000).Select(i => $"line {i}").ToArray();
        var b = a.ToArray();
        b[7_500] = "changed line";

        Assert.False(DiffEngine.TooLarge(a, b));
        var result = DiffEngine.Diff(a, b);
        Assert.Equal(1, result.Changes);
        Assert.False(result.Identical);
    }
}
