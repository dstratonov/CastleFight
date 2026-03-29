using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Single source of truth for footprint cell computation.
/// All systems (A*, movement, presence, attack range) must use this.
/// </summary>
public static class FootprintHelper
{
    /// <summary>
    /// Compute half-extents from footprint size.
    /// 1→(0,0), 2→(0,1), 3→(1,1), 4→(1,2)
    /// </summary>
    public static void GetHalfExtents(int footprintSize, out int halfLow, out int halfHigh)
    {
        int cellSpan = Mathf.Max(1, footprintSize);
        halfLow = (cellSpan - 1) / 2;
        halfHigh = cellSpan / 2;
    }

    /// <summary>
    /// Get the (min, max) cell bounds of a footprint centered on the given cell.
    /// </summary>
    public static (Vector2Int min, Vector2Int max) GetRect(Vector2Int center, int footprintSize)
    {
        GetHalfExtents(footprintSize, out int halfLow, out int halfHigh);
        return (
            new Vector2Int(center.x - halfLow, center.y - halfLow),
            new Vector2Int(center.x + halfHigh, center.y + halfHigh)
        );
    }

    /// <summary>
    /// Enumerate all cells in a footprint centered on the given cell.
    /// </summary>
    public static void GetCells(Vector2Int center, int footprintSize, List<Vector2Int> result)
    {
        result.Clear();
        GetHalfExtents(footprintSize, out int halfLow, out int halfHigh);
        for (int dx = -halfLow; dx <= halfHigh; dx++)
            for (int dy = -halfLow; dy <= halfHigh; dy++)
                result.Add(new Vector2Int(center.x + dx, center.y + dy));
    }

    /// <summary>
    /// Check if the full NxN footprint at center is walkable on the grid.
    /// This is the ONE place that checks footprint walkability.
    /// </summary>
    public static bool IsWalkable(IGrid grid, Vector2Int center, int footprintSize)
    {
        GetHalfExtents(footprintSize, out int halfLow, out int halfHigh);
        for (int dx = -halfLow; dx <= halfHigh; dx++)
        {
            for (int dy = -halfLow; dy <= halfHigh; dy++)
            {
                Vector2Int cell = new Vector2Int(center.x + dx, center.y + dy);
                if (!grid.IsInBounds(cell) || !grid.IsWalkable(cell))
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Find the nearest cell where the full footprint is walkable.
    /// Searches in a spiral pattern outward from center.
    /// </summary>
    public static Vector2Int FindNearestWalkable(IGrid grid, Vector2Int center, int footprintSize, int maxRadius = 15)
    {
        if (IsWalkable(grid, center, footprintSize))
            return center;

        for (int r = 1; r <= maxRadius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    Vector2Int c = new Vector2Int(center.x + dx, center.y + dy);
                    if (IsWalkable(grid, c, footprintSize))
                        return c;
                }
            }
        }
        return center;
    }
}
