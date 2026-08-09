namespace Junction.Git;

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
/// An edge from one lane column to another. Upper-half edges run from
/// (FromLane, 0) to (ToLane, centerY); lower-half edges run from
/// (FromLane, centerY) to (ToLane, h). The dot itself sits at
/// (row.Lane, centerY), so any edge meeting the dot has its mid-point
/// at that lane.
/// </summary>
public readonly record struct GraphEdge(int FromLane, int ToLane, int ColorIndex);

/// <summary>
/// Streaming lane assignment over a newest→oldest commit list. Each commit
/// is placed in the leftmost lane that already expects it; any other lane
/// expecting the same hash folds into that lane. Parents are then routed
/// into bottom lanes, possibly reusing existing lanes (merge arc) or
/// opening fresh ones (fork / octopus merge).
/// </summary>
public static class GitGraphLayout
{
    public static Dictionary<string, GitGraphRow> Compute(IReadOnlyList<GitCommit> commits)
    {
        var result = new Dictionary<string, GitGraphRow>(commits.Count);

        // Active lane state: activeLanes[i] is the hash each lane is waiting
        // to encounter next, or null if the lane is temporarily vacant.
        var activeLanes = new List<string?>();
        var laneColors = new List<int>();
        int nextColor = 0;

        for (int i = 0; i < commits.Count; i++)
        {
            var commit = commits[i];
            var hash = commit.Hash;

            // Snapshot top-of-row state for edge construction later.
            var topLanes = activeLanes.ToList();
            var topColors = laneColors.ToList();

            // Choose the dot's lane: reuse any existing lane that expects us;
            // otherwise prefer a vacant slot, then finally append.
            int lane = FindLane(activeLanes, hash);
            if (lane == -1)
            {
                lane = FindVacantLane(activeLanes);
                if (lane == -1)
                {
                    lane = activeLanes.Count;
                    activeLanes.Add(hash);
                    laneColors.Add(nextColor++);
                }
                else
                {
                    activeLanes[lane] = hash;
                    laneColors[lane] = nextColor++;
                }
            }
            int ownColor = laneColors[lane];

            // Any other lanes that also expected this hash converge onto it.
            for (int li = 0; li < activeLanes.Count; li++)
            {
                if (li != lane && activeLanes[li] == hash)
                    activeLanes[li] = null;
            }

            // Route parents. Each parent produces one outgoing edge from the dot.
            var outgoing = new List<GraphEdge>();
            var parents = commit.ParentHashes;

            if (parents.Length == 0)
            {
                activeLanes[lane] = null;
            }
            else
            {
                var firstParent = parents[0];
                int existing = FindLane(activeLanes, firstParent);
                if (existing == -1 || existing == lane)
                {
                    // First parent continues straight down in my lane.
                    activeLanes[lane] = firstParent;
                    outgoing.Add(new GraphEdge(lane, lane, ownColor));
                }
                else
                {
                    // First parent is already carried by a different lane —
                    // close mine and arc across to join it. This is the
                    // branch-merging-back case.
                    activeLanes[lane] = null;
                    outgoing.Add(new GraphEdge(lane, existing, ownColor));
                }

                // Additional parents (regular merge / octopus): either join
                // an existing lane or open a new one.
                for (int p = 1; p < parents.Length; p++)
                {
                    var parent = parents[p];
                    int parentLane = FindLane(activeLanes, parent);
                    if (parentLane == -1)
                    {
                        int slot = FindVacantLane(activeLanes);
                        if (slot == -1)
                        {
                            slot = activeLanes.Count;
                            activeLanes.Add(parent);
                            laneColors.Add(nextColor++);
                        }
                        else
                        {
                            activeLanes[slot] = parent;
                            laneColors[slot] = nextColor++;
                        }
                        outgoing.Add(new GraphEdge(lane, slot, laneColors[slot]));
                    }
                    else
                    {
                        outgoing.Add(new GraphEdge(lane, parentLane, laneColors[parentLane]));
                    }
                }
            }

            // Build the row's edge lists.
            var edgesFromTop = new List<GraphEdge>();
            var edgesToBottom = new List<GraphEdge>();

            for (int ti = 0; ti < topLanes.Count; ti++)
            {
                var h = topLanes[ti];
                if (h == null) continue;

                if (h == hash)
                {
                    // Convergence into the dot.
                    edgesFromTop.Add(new GraphEdge(ti, lane, topColors[ti]));
                }
                else
                {
                    // Pass-through. The lane may have shifted if compaction
                    // or reassignment moved it; draw the diagonal in the
                    // upper half and a straight segment in the lower half.
                    int below = FindLane(activeLanes, h);
                    if (below != -1)
                    {
                        edgesFromTop.Add(new GraphEdge(ti, below, topColors[ti]));
                        edgesToBottom.Add(new GraphEdge(below, below, topColors[ti]));
                    }
                }
            }

            // Outgoing edges from dot to each parent's lane.
            edgesToBottom.AddRange(outgoing);

            // Compact trailing nulls so the active area doesn't grow forever.
            while (activeLanes.Count > 0 && activeLanes[^1] == null)
            {
                activeLanes.RemoveAt(activeLanes.Count - 1);
                laneColors.RemoveAt(laneColors.Count - 1);
            }

            int laneCount = Math.Max(topLanes.Count, activeLanes.Count);
            laneCount = Math.Max(laneCount, lane + 1);
            foreach (var e in edgesFromTop)
                laneCount = Math.Max(laneCount, Math.Max(e.FromLane, e.ToLane) + 1);
            foreach (var e in edgesToBottom)
                laneCount = Math.Max(laneCount, Math.Max(e.FromLane, e.ToLane) + 1);

            result[hash] = new GitGraphRow(
                Lane: lane,
                LaneCount: laneCount,
                IsMerge: parents.Length > 1,
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

    private static int FindVacantLane(List<string?> lanes)
    {
        for (int i = 0; i < lanes.Count; i++)
            if (lanes[i] == null) return i;
        return -1;
    }
}
