using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Commit-graph lane assignment. Rows are drawn independently, so the only thing making
/// the graph read as connected lines is that one row's outgoing lanes agree with the
/// next row's incoming ones. These tests check that agreement rather than exact
/// pixel geometry.
/// </summary>
public class GitGraphLayoutTests
{
    private static GitCommit C(string hash, params string[] parents) =>
        new(hash, hash[..Math.Min(7, hash.Length)], "A", "a@example.com",
            DateTimeOffset.UnixEpoch, "subject", parents, []);

    [Fact]
    public void ALinearHistoryStaysInOneLane()
    {
        var rows = GitGraphLayout.Compute([C("a", "b"), C("b", "c"), C("c")]);

        Assert.Equal(3, rows.Count);
        foreach (var row in rows.Values)
        {
            Assert.Equal(0, row.Lane);
            Assert.Equal(1, row.LaneCount);
            Assert.False(row.IsMerge);
        }
    }

    [Fact]
    public void ARootCommitClosesItsLane()
    {
        var rows = GitGraphLayout.Compute([C("a", "b"), C("b")]);

        // Nothing continues below a parentless commit.
        Assert.Empty(rows["b"].EdgesToNext);
        Assert.NotEmpty(rows["a"].EdgesToNext);
    }

    [Fact]
    public void AMergeOpensASecondLaneAndTheSideBranchArcsBackIntoTheFirst()
    {
        //   m        merge of a and b
        //   a  b     both on top of base
        //   base
        var rows = GitGraphLayout.Compute([C("m", "a", "b"), C("a", "base"), C("b", "base"), C("base")]);

        Assert.True(rows["m"].IsMerge);
        Assert.Equal(2, rows["m"].LaneCount);

        // The merge sends one edge down each parent's lane.
        Assert.Equal(2, rows["m"].EdgesToNext.Count);
        Assert.Contains(rows["m"].EdgesToNext, e => e.FromLane == 0 && e.ToLane == 0);
        Assert.Contains(rows["m"].EdgesToNext, e => e.FromLane == 0 && e.ToLane == 1);

        // b sits in the lane the merge opened, and its parent is already carried by
        // lane 0 — so its edge crosses back rather than continuing straight down.
        Assert.Equal(1, rows["b"].Lane);
        var arc = Assert.Single(rows["b"].EdgesToNext, e => e.FromLane == 1 && e.ToLane == 0);
        Assert.Equal(1, arc.FromLane);

        // …and nothing is left in lane 1 below it.
        Assert.Equal(0, rows["base"].Lane);
    }

    [Fact]
    public void AnOctopusMergeOpensALaneForEveryExtraParent()
    {
        var rows = GitGraphLayout.Compute(
            [C("m", "p1", "p2", "p3"), C("p1"), C("p2"), C("p3")]);

        Assert.True(rows["m"].IsMerge);
        Assert.Equal(3, rows["m"].EdgesToNext.Count);
        Assert.Equal(3, rows["m"].LaneCount);
    }

    [Fact]
    public void AGapLeftByAFinishedLaneIsReusedRatherThanWideningTheGraph()
    {
        // `a` is a root, so lane 0 goes vacant while lane 1 is still carrying `b`. A
        // trailing vacancy would just be trimmed; an interior one has to be filled, or
        // the gutter grows a column for every branch that ever ended.
        var rows = GitGraphLayout.Compute([C("h", "a", "b"), C("a"), C("x", "y"), C("b"), C("y")]);

        Assert.Equal(0, rows["a"].Lane);
        Assert.Equal(0, rows["x"].Lane);
        Assert.Equal(2, rows["x"].LaneCount);
    }

    [Fact]
    public void EveryEdgeStaysInsideTheRowsDeclaredLaneCount()
    {
        // LaneCount is what the renderer sizes the gutter from. An edge outside it is
        // drawn clipped or off-widget, which is how a graph column silently disappears.
        var rows = GitGraphLayout.Compute(
        [
            C("h", "m", "topic2"),
            C("m", "a", "topic1"),
            C("topic2", "a"),
            C("a", "b"),
            C("topic1", "b"),
            C("b", "root"),
            C("root"),
        ]);

        foreach (var (hash, row) in rows)
        {
            Assert.InRange(row.Lane, 0, row.LaneCount - 1);
            foreach (var e in row.EdgesFromPrev.Concat(row.EdgesToNext))
            {
                Assert.InRange(e.FromLane, 0, row.LaneCount - 1);
                Assert.InRange(e.ToLane, 0, row.LaneCount - 1);
                Assert.True(e.ColorIndex >= 0, $"{hash} has a negative colour index");
            }
        }
    }

    [Fact]
    public void OneRowIsProducedPerCommitEvenWhenParentsAreOutsideTheWindow()
    {
        // The log is paged, so the oldest rows reference parents that were never
        // loaded. Those lanes stay open; no row may be dropped.
        var commits = new[] { C("a", "b"), C("b", "missing-1"), C("c", "missing-2") };
        var rows = GitGraphLayout.Compute(commits);

        Assert.Equal(3, rows.Count);
        foreach (var c in commits)
            Assert.True(rows.ContainsKey(c.Hash), c.Hash);
    }
}
