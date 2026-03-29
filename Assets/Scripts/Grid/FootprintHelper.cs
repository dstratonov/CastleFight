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
    /// Check if the full footprint at center is walkable on the grid.
    /// </summary>
    public static bool IsWalkable(IGrid grid, Vector2Int center, int footprintSize)
    {
        GetHalfExtents(footprintSize, out int halfLow, out int halfHigh);
        return GridAStar.IsFootprintWalkable(grid, center, halfLow, halfHigh);
    }

    /// <summary>
    /// Find the nearest cell where the full footprint is walkable.
    /// </summary>
    public static Vector2Int FindNearestWalkable(IGrid grid, Vector2Int center, int footprintSize, int maxRadius = 15)
    {
        GetHalfExtents(footprintSize, out int halfLow, out int halfHigh);
        return GridAStar.FindNearestWalkableCellForFootprint(grid, center, maxRadius, halfLow, halfHigh);
    }
}
