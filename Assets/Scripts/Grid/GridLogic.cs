using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Pure grid math logic extracted from GridSystem for testability.
/// No MonoBehaviour, no Instance, no visual dependencies.
/// </summary>
public class GridLogic
{
    private readonly int width;
    private readonly int height;
    private readonly float cellSize;
    private readonly Vector3 origin;
    private CellState[,] cells;

    private static readonly Vector2Int[] EightDirections =
    {
        new(0, 1), new(0, -1), new(1, 0), new(-1, 0),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)
    };

    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;
    public Vector3 Origin => origin;

    public GridLogic(int width, int height, float cellSize, Vector3 origin)
    {
        this.width = width;
        this.height = height;
        this.cellSize = cellSize;
        this.origin = origin;
        cells = new CellState[width, height];
    }

    public Vector2Int WorldToCell(Vector3 worldPosition)
    {
        int x = Mathf.RoundToInt((worldPosition.x - origin.x) / cellSize);
        int z = Mathf.RoundToInt((worldPosition.z - origin.z) / cellSize);
        return new Vector2Int(x, z);
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        return new Vector3(
            cell.x * cellSize + origin.x,
            origin.y,
            cell.y * cellSize + origin.z
        );
    }

    public Vector3 SnapToGrid(Vector3 worldPosition)
    {
        Vector2Int cell = WorldToCell(worldPosition);
        return CellToWorld(cell);
    }

    public bool IsInBounds(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
    }

    public CellState GetCellState(Vector2Int cell)
    {
        if (!IsInBounds(cell)) return CellState.Building;
        return cells[cell.x, cell.y];
    }

    public bool IsWalkable(Vector2Int cell)
    {
        if (!IsInBounds(cell)) return false;
        return cells[cell.x, cell.y] == CellState.Empty;
    }

    public void SetCell(Vector2Int cell, CellState state)
    {
        if (IsInBounds(cell))
            cells[cell.x, cell.y] = state;
    }

    public List<Vector2Int> GetWalkableNeighbors(Vector2Int cell)
    {
        var result = new List<Vector2Int>(8);
        foreach (var dir in EightDirections)
        {
            Vector2Int neighbor = cell + dir;
            if (IsWalkable(neighbor))
                result.Add(neighbor);
        }
        return result;
    }

    public List<Vector2Int> GetAdjacentCells(Vector2Int cell)
    {
        var result = new List<Vector2Int>(8);
        foreach (var dir in EightDirections)
        {
            Vector2Int neighbor = cell + dir;
            if (IsInBounds(neighbor))
                result.Add(neighbor);
        }
        return result;
    }

    public bool HasWalkableNeighbor(Vector2Int cell)
    {
        foreach (var dir in EightDirections)
        {
            if (IsWalkable(cell + dir))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns cells whose centers fall within the given world bounds.
    /// Uses Floor/Ceil to avoid inflation from RoundToInt.
    /// </summary>
    public List<Vector2Int> GetCellsOverlappingBounds(Bounds worldBounds)
    {
        int x0 = Mathf.Max(0, Mathf.CeilToInt((worldBounds.min.x - origin.x) / cellSize));
        int x1 = Mathf.Min(width - 1, Mathf.FloorToInt((worldBounds.max.x - origin.x) / cellSize));
        int z0 = Mathf.Max(0, Mathf.CeilToInt((worldBounds.min.z - origin.z) / cellSize));
        int z1 = Mathf.Min(height - 1, Mathf.FloorToInt((worldBounds.max.z - origin.z) / cellSize));

        var result = new List<Vector2Int>(Mathf.Max(0, (x1 - x0 + 1) * (z1 - z0 + 1)));
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
                result.Add(new Vector2Int(x, z));
        return result;
    }

    public bool AreCellsEmpty(List<Vector2Int> cellList)
    {
        foreach (var cell in cellList)
        {
            if (!IsInBounds(cell)) return false;
            if (cells[cell.x, cell.y] != CellState.Empty) return false;
        }
        return true;
    }

    public bool HasLineOfSight(Vector2Int from, Vector2Int to)
    {
        int x0 = from.x, y0 = from.y;
        int x1 = to.x, y1 = to.y;
        int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            if (x0 == x1 && y0 == y1) return true;

            var cell = new Vector2Int(x0, y0);
            if (cell != from && cell != to && !IsWalkable(cell))
                return false;

            int e2 = 2 * err;
            bool stepX = e2 > -dy;
            bool stepY = e2 < dx;

            if (stepX && stepY)
            {
                var adjX = new Vector2Int(x0 + sx, y0);
                var adjY = new Vector2Int(x0, y0 + sy);
                if (!IsWalkable(adjX) || !IsWalkable(adjY))
                    return false;
            }

            if (stepX) { err -= dy; x0 += sx; }
            if (stepY) { err += dx; y0 += sy; }
        }
    }

    public Vector3 FindNearestWalkablePosition(Vector3 desiredWorldPos, Vector3 referencePos)
    {
        Vector2Int center = WorldToCell(desiredWorldPos);
        if (IsInBounds(center) && IsWalkable(center))
            return desiredWorldPos;

        for (int radius = 1; radius <= 15; radius++)
        {
            Vector2Int best = center;
            float bestDist = float.MaxValue;
            bool found = false;

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (Mathf.Abs(dx) != radius && Mathf.Abs(dz) != radius) continue;
                    Vector2Int cell = new(center.x + dx, center.y + dz);
                    if (!IsInBounds(cell) || !IsWalkable(cell)) continue;

                    float dist = (referencePos - CellToWorld(cell)).sqrMagnitude;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = cell;
                        found = true;
                    }
                }
            }

            if (found)
                return CellToWorld(best);
        }

        return desiredWorldPos;
    }

    /// <summary>
    /// Wall-slide position validation. If newPos is walkable, return it.
    /// Otherwise try sliding along X or Z axis. Falls back to oldPos.
    /// </summary>
    public Vector3 ValidatePosition(Vector3 oldPos, Vector3 newPos, Vector3 velocityHint)
    {
        Vector2Int newCell = WorldToCell(newPos);
        if (IsInBounds(newCell) && IsWalkable(newCell))
            return newPos;

        Vector3 slideX = new Vector3(newPos.x, newPos.y, oldPos.z);
        Vector2Int cellX = WorldToCell(slideX);
        bool xOk = IsInBounds(cellX) && IsWalkable(cellX);

        Vector3 slideZ = new Vector3(oldPos.x, newPos.y, newPos.z);
        Vector2Int cellZ = WorldToCell(slideZ);
        bool zOk = IsInBounds(cellZ) && IsWalkable(cellZ);

        if (xOk && zOk)
            return Mathf.Abs(velocityHint.x) >= Mathf.Abs(velocityHint.z) ? slideX : slideZ;
        if (xOk) return slideX;
        if (zOk) return slideZ;

        return oldPos;
    }
}
