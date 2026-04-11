namespace GitClient.Git;

/// <summary>
/// Per-commit position inside the rendered graph. Lane indices are stable
/// across rows so edges line up column-for-column between neighbouring rows.
/// </summary>
public readonly record struct GitGraphRow(
    int Lane,
    int LaneCount,
    bool IsMerge,
    IReadOnlyList<GraphEdge> EdgesFromPrev,
    IReadOnlyList<GraphEdge> EdgesToNext
);

/// <summary>
/// An edge between two rows. <see cref="FromLane"/> is the lane at the top
/// of the row area, <see cref="ToLane"/> the lane at the bottom.
/// </summary>
public readonly record struct GraphEdge(int FromLane, int ToLane, int ColorIndex);

/// <summary>
/// Computes lane assignments and edges for a linear list of commits.
///
/// Display order runs newest → oldest (same order as `git log`). For each
/// commit we pick a lane, then replace/extend the active-lane list based on
/// the commit's parents. Edges entering the row are produced by comparing
/// the lane state before and after the row.
/// </summary>
public static class GitGraphLayout
{
    public static Dictionary<string, GitGraphRow> Compute(IReadOnlyList<GitCommit> commits)
    {
        var result = new Dictionary<string, GitGraphRow>(commits.Count);

        // activeLanes[i] is the hash of the commit we're expecting to see in lane i.
        // null means the lane is vacant and reusable.
        var activeLanes = new List<string?>();

        // Assign a stable colour to each lane the first time it's created so a
        // branch keeps its colour as it flows down the graph.
        var laneColors = new List<int>();
        int nextColor = 0;

        for (int i = 0; i < commits.Count; i++)
        {
            var commit = commits[i];

            // Snapshot the top edge of this row: what lanes arrive into this row
            // and which hashes they still carry. Each entry owned by another
            // commit becomes an edge heading downward.
            var topLanes = activeLanes.ToList();
            var topColors = laneColors.ToList();

            // Pick a lane for this commit. Reuse the leftmost lane that already
            // expects this commit's hash; otherwise open a fresh lane on the
            // right.
            int lane = FindLane(activeLanes, commit.Hash);
            if (lane == -1)
            {
                lane = activeLanes.Count;
                activeLanes.Add(commit.Hash);
                laneColors.Add(nextColor++);
            }

            int ownColor = laneColors[lane];

            // Replace the current lane with the first parent (if any), and
            // add any additional parents as new lanes on the right. Merge
            // commits therefore fan out to the right.
            var parents = commit.ParentHashes;
            if (parents.Length == 0)
            {
                activeLanes[lane] = null;
            }
            else
            {
                // First parent continues in the same lane with the same colour.
                var firstParent = parents[0];
                int existing = FindLane(activeLanes, firstParent);
                if (existing != -1 && existing != lane)
                {
                    // First parent already has a lane — close ours, the edge
                    // will fold into the existing lane below.
                    activeLanes[lane] = null;
                }
                else
                {
                    activeLanes[lane] = firstParent;
                }

                for (int p = 1; p < parents.Length; p++)
                {
                    var parent = parents[p];
                    int parentLane = FindLane(activeLanes, parent);
                    if (parentLane == -1)
                    {
                        activeLanes.Add(parent);
                        laneColors.Add(ownColor);
                    }
                }
            }

            // Compact trailing nulls so the graph doesn't grow forever.
            while (activeLanes.Count > 0 && activeLanes[^1] == null)
            {
                activeLanes.RemoveAt(activeLanes.Count - 1);
                laneColors.RemoveAt(laneColors.Count - 1);
            }

            var bottomLanes = activeLanes.ToList();
            var bottomColors = laneColors.ToList();

            // Edges from top to dot: every top lane that carries a hash has
            // an incoming edge. Lanes whose hash equals this commit's hash
            // converge onto this commit's lane.
            var edgesFromTop = new List<GraphEdge>();
            for (int ti = 0; ti < topLanes.Count; ti++)
            {
                if (topLanes[ti] == null) continue;
                int targetLane = topLanes[ti] == commit.Hash ? lane : ti;
                edgesFromTop.Add(new GraphEdge(ti, targetLane, topColors[ti]));
            }

            // Edges from dot to bottom: every bottom lane that carries a hash
            // has an outgoing edge. Lanes that were opened by this commit
            // originate from this commit's lane; existing lanes continue
            // straight down.
            var edgesToBottom = new List<GraphEdge>();
            for (int bi = 0; bi < bottomLanes.Count; bi++)
            {
                if (bottomLanes[bi] == null) continue;

                // Did this lane carry the same hash before the row? Then it's
                // a pass-through (straight vertical edge).
                bool wasPresent = bi < topLanes.Count && topLanes[bi] == bottomLanes[bi];
                int originLane = wasPresent ? bi : lane;
                edgesToBottom.Add(new GraphEdge(originLane, bi, bottomColors[bi]));
            }

            int laneCount = Math.Max(topLanes.Count, bottomLanes.Count);
            result[commit.Hash] = new GitGraphRow(
                Lane: lane,
                LaneCount: laneCount,
                IsMerge: commit.ParentHashes.Length > 1,
                EdgesFromPrev: edgesFromTop,
                EdgesToNext: edgesToBottom
            );
        }

        return result;
    }

    private static int FindLane(List<string?> lanes, string hash)
    {
        for (int i = 0; i < lanes.Count; i++)
            if (lanes[i] == hash) return i;
        return -1;
    }
}
