using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Grid-based A* pathfinder. Replaces the NavMesh CDT approach.
/// Operates directly on the IGrid walkability grid using 8-directional movement.
/// Much simpler and more reliable than the CDT+Funnel pipeline.
///
/// Features:
/// - 8-directional movement with diagonal cost √2
/// - Diagonal movement requires both adjacent cardinal cells to be walkable (no corner-cutting)
/// - Path smoothing via line-of-sight checks (removes unnecessary waypoints)
/// - Pooled data structures to minimize GC
/// - Iteration cap for safety
/// </summary>
public static class GridAStar
{
    // Statistics
    public static int StatPathsRequested;
    public static int StatPathsSucceeded;
    public static int StatPathsFailed;
    public static int StatTotalNodes;
    public static int StatThrottled;

    public static void ResetStats()
    {
        StatPathsRequested = 0;
        StatPathsSucceeded = 0;
        StatPathsFailed = 0;
        StatTotalNodes = 0;
        StatThrottled = 0;
    }

    private static readonly Vector2Int[] Dirs =
    {
        new(0, 1), new(0, -1), new(1, 0), new(-1, 0),   // cardinal
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)   // diagonal
    };

    private static readonly float[] DirCosts =
    {
        1f, 1f, 1f, 1f,
        1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f
    };

    // Pooled open set and score maps — reset per call, never shrink
    private static readonly SortedSet<(float f, int idx)> s_open =
        new(Comparer<(float f, int idx)>.Create(
            (a, b) => a.f != b.f ? a.f.CompareTo(b.f) : a.idx.CompareTo(b.idx)));

    private static float[] s_gScore = Array.Empty<float>();
    private static int[] s_parent = Array.Empty<int>();
    private static bool[] s_closed = Array.Empty<bool>();

    /// <summary>
    /// Find a path from startWorld to goalWorld on the grid.
    /// footprintSize is the NxN cell footprint of the unit (1=small, 2=large, 3=huge).
    /// A* ensures the full footprint is walkable at every position (no obstacle overlap).
    /// If debugCellPath is non-null, the raw A* cell path is stored for visualization.
    /// </summary>
    public static List<Vector3> FindPath(IGrid grid, Vector3 startWorld, Vector3 goalWorld,
        List<Vector2Int> debugCellPath = null, int footprintSize = 1)
    {
        StatPathsRequested++;

        FootprintHelper.GetHalfExtents(footprintSize, out int halfLow, out int halfHigh);

        Vector2Int startCell = grid.WorldToCell(startWorld);
        Vector2Int goalCell = grid.WorldToCell(goalWorld);

        startCell = ClampToGrid(startCell, grid);
        goalCell = ClampToGrid(goalCell, grid);

        // If start/goal footprint overlaps obstacle, find nearest valid cell
        if (!FootprintHelper.IsWalkable(grid, startCell, footprintSize))
            startCell = FootprintHelper.FindNearestWalkable(grid, startCell, footprintSize, 10);
        if (!FootprintHelper.IsWalkable(grid, goalCell, footprintSize))
            goalCell = FootprintHelper.FindNearestWalkable(grid, goalCell, footprintSize, 10);

        if (startCell == goalCell)
        {
            StatPathsSucceeded++;
            return new List<Vector3> { startWorld, grid.CellToWorld(goalCell) };
        }

        // A* on grid cells with footprint checking
        int w = grid.Width, h = grid.Height;
        int totalCells = w * h;

        if (s_gScore.Length < totalCells)
        {
            s_gScore = new float[totalCells];
            s_parent = new int[totalCells];
            s_closed = new bool[totalCells];
        }

        Array.Fill(s_gScore, float.MaxValue, 0, totalCells);
        Array.Fill(s_parent, -1, 0, totalCells);
        Array.Fill(s_closed, false, 0, totalCells);
        s_open.Clear();

        int startIdx = startCell.y * w + startCell.x;
        int goalIdx = goalCell.y * w + goalCell.x;

        s_gScore[startIdx] = 0f;
        s_open.Add((Heuristic(startCell, goalCell), startIdx));

        int maxIter = Mathf.Min(totalCells, 10000);
        int iter = 0;

        while (s_open.Count > 0 && iter++ < maxIter)
        {
            var (_, currentIdx) = s_open.Min;
            s_open.Remove(s_open.Min);

            if (currentIdx == goalIdx)
            {
                StatPathsSucceeded++;
                StatTotalNodes += iter;
                var cellPath = ReconstructPath(startIdx, goalIdx, w);
                if (debugCellPath != null)
                {
                    debugCellPath.Clear();
                    debugCellPath.AddRange(cellPath);
                }
                var waypoints = new List<Vector3>(cellPath.Count + 1);
                waypoints.Add(startWorld);
                for (int i = 1; i < cellPath.Count; i++)
                    waypoints.Add(grid.CellToWorld(cellPath[i]));
                return waypoints;
            }

            if (s_closed[currentIdx]) continue;
            s_closed[currentIdx] = true;

            int cx = currentIdx % w;
            int cy = currentIdx / w;
            float currentG = s_gScore[currentIdx];

            for (int d = 0; d < 8; d++)
            {
                int nx = cx + Dirs[d].x;
                int ny = cy + Dirs[d].y;

                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;

                Vector2Int nCell = new Vector2Int(nx, ny);

                // Check entire footprint at this cell is walkable (no obstacle overlap)
                if (!FootprintHelper.IsWalkable(grid, nCell, footprintSize)) continue;

                // Diagonal: require both adjacent cardinal footprints to be walkable
                if (d >= 4)
                {
                    Vector2Int adj1 = new Vector2Int(cx + Dirs[d].x, cy);
                    Vector2Int adj2 = new Vector2Int(cx, cy + Dirs[d].y);
                    if (!FootprintHelper.IsWalkable(grid, adj1, footprintSize)) continue;
                    if (!FootprintHelper.IsWalkable(grid, adj2, footprintSize)) continue;
                }

                int nIdx = ny * w + nx;
                if (s_closed[nIdx]) continue;

                float tentativeG = currentG + DirCosts[d];
                if (tentativeG < s_gScore[nIdx])
                {
                    s_gScore[nIdx] = tentativeG;
                    s_parent[nIdx] = currentIdx;
                    s_open.Add((tentativeG + Heuristic(nCell, goalCell), nIdx));
                }
            }
        }

        // A* failed — return straight line as fallback
        StatPathsFailed++;
        StatTotalNodes += iter;
        return new List<Vector3> { startWorld, goalWorld };
    }

    private static float Heuristic(Vector2Int a, Vector2Int b)
    {
        // Octile distance: optimal for 8-directional movement
        int dx = Mathf.Abs(a.x - b.x);
        int dy = Mathf.Abs(a.y - b.y);
        return Mathf.Max(dx, dy) + 0.41421356f * Mathf.Min(dx, dy);
    }

    private static List<Vector2Int> ReconstructPath(int startIdx, int goalIdx, int w)
    {
        var path = new List<Vector2Int>();
        int current = goalIdx;
        while (current != startIdx && current >= 0)
        {
            path.Add(new Vector2Int(current % w, current / w));
            current = s_parent[current];
        }
        path.Add(new Vector2Int(startIdx % w, startIdx / w));
        path.Reverse();
        return path;
    }

    /// <summary>
    /// Smooth the cell path by removing intermediate waypoints that have
    /// line-of-sight to each other. This produces natural-looking paths
    /// instead of grid-aligned zigzags.
    /// </summary>
    private static List<Vector3> SmoothPath(IGrid grid, List<Vector2Int> cellPath,
        Vector3 startWorld, Vector3 goalWorld)
    {
        if (cellPath.Count <= 2)
        {
            var result = new List<Vector3> { startWorld };
            if (cellPath.Count > 0)
                result.Add(grid.CellToWorld(cellPath[cellPath.Count - 1]));
            else
                result.Add(goalWorld);
            return result;
        }

        // Greedy line-of-sight smoothing
        var smoothed = new List<Vector3> { startWorld };
        int anchor = 0;

        while (anchor < cellPath.Count - 1)
        {
            // Find the farthest cell visible from anchor
            int farthest = anchor + 1;
            for (int probe = cellPath.Count - 1; probe > anchor + 1; probe--)
            {
                if (grid.HasLineOfSight(cellPath[anchor], cellPath[probe]))
                {
                    farthest = probe;
                    break;
                }
            }

            smoothed.Add(grid.CellToWorld(cellPath[farthest]));
            anchor = farthest;
        }

        // Replace last waypoint with exact goal position
        if (smoothed.Count > 1)
            smoothed[smoothed.Count - 1] = goalWorld;

        return smoothed;
    }

    private static Vector2Int ClampToGrid(Vector2Int cell, IGrid grid)
    {
        return new Vector2Int(
            Mathf.Clamp(cell.x, 0, grid.Width - 1),
            Mathf.Clamp(cell.y, 0, grid.Height - 1));
    }

    /// <summary>
    /// Find the nearest walkable cell to the given cell, searching in a spiral pattern.
    /// </summary>
    public static Vector2Int FindNearestWalkableCell(IGrid grid, Vector2Int center, int maxRadius)
    {
        if (grid.IsInBounds(center) && grid.IsWalkable(center))
            return center;

        for (int r = 1; r <= maxRadius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    Vector2Int c = new Vector2Int(center.x + dx, center.y + dy);
                    if (grid.IsInBounds(c) && grid.IsWalkable(c))
                        return c;
                }
            }
        }
        return center; // fallback: return original
    }

    /// <summary>Delegates to FootprintHelper — kept for backward compatibility.</summary>
    public static bool IsFootprintWalkable(IGrid grid, Vector2Int center, int halfLow, int halfHigh)
    {
        int footprintSize = halfLow + halfHigh + 1;
        return FootprintHelper.IsWalkable(grid, center, footprintSize);
    }
}
