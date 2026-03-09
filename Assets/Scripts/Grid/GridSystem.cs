using UnityEngine;
using System.Collections.Generic;

public enum CellState
{
    Empty,
    Building
}

public class GridSystem : MonoBehaviour
{
    public static GridSystem Instance { get; private set; }

    [SerializeField] private int gridWidth = 100;
    [SerializeField] private int gridHeight = 100;
    [SerializeField] private float cellSize = 2f;
    [SerializeField] private Vector3 gridOrigin = new(-100f, 0f, -100f);
    [SerializeField] private BuildZone[] buildZones;

    private CellState[,] cells;

    public int Width => gridWidth;
    public int Height => gridHeight;
    public float CellSize => cellSize;
    public Vector3 GridOrigin => gridOrigin;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        InitializeGrid();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void InitializeGrid()
    {
        cells = new CellState[gridWidth, gridHeight];
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
                cells[x, y] = CellState.Empty;
    }

    public Vector2Int WorldToCell(Vector3 worldPosition)
    {
        int x = Mathf.RoundToInt((worldPosition.x - gridOrigin.x) / cellSize);
        int z = Mathf.RoundToInt((worldPosition.z - gridOrigin.z) / cellSize);
        return new Vector2Int(x, z);
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        return new Vector3(
            cell.x * cellSize + gridOrigin.x,
            gridOrigin.y,
            cell.y * cellSize + gridOrigin.z
        );
    }

    public Vector3 SnapToGrid(Vector3 worldPosition)
    {
        Vector2Int cell = WorldToCell(worldPosition);
        return CellToWorld(cell);
    }

    public bool IsInBounds(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < gridWidth && cell.y >= 0 && cell.y < gridHeight;
    }

    public CellState GetCellState(Vector2Int cell)
    {
        if (!IsInBounds(cell)) return CellState.Building;
        return cells[cell.x, cell.y];
    }

    public bool IsWalkable(Vector2Int cell)
    {
        if (cells == null || !IsInBounds(cell)) return false;
        return cells[cell.x, cell.y] == CellState.Empty;
    }

    public bool CanPlaceBuilding(Vector3 worldPosition, int teamId)
    {
        if (!IsInBuildZone(worldPosition, teamId)) return false;
        Vector2Int cell = WorldToCell(worldPosition);
        return IsInBounds(cell) && cells[cell.x, cell.y] == CellState.Empty;
    }

    public List<Vector2Int> GetCellsOverlappingBounds(Bounds worldBounds)
    {
        Vector2Int minCell = WorldToCell(worldBounds.min);
        Vector2Int maxCell = WorldToCell(worldBounds.max);

        int x0 = Mathf.Max(0, minCell.x);
        int x1 = Mathf.Min(gridWidth - 1, maxCell.x);
        int z0 = Mathf.Max(0, minCell.y);
        int z1 = Mathf.Min(gridHeight - 1, maxCell.y);

        var result = new List<Vector2Int>((x1 - x0 + 1) * (z1 - z0 + 1));
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

    public void MarkCells(List<Vector2Int> cellList, CellState state)
    {
        foreach (var cell in cellList)
        {
            if (IsInBounds(cell))
                cells[cell.x, cell.y] = state;
        }
        if (GameDebug.Building)
            Debug.Log($"[Grid] Marked {cellList.Count} cells as {state}");
    }

    public void ClearCells(List<Vector2Int> cellList)
    {
        if (GameDebug.Building)
            Debug.Log($"[Grid] Clearing {cellList.Count} cells");
        MarkCells(cellList, CellState.Empty);
    }

    private static readonly Vector2Int[] EightDirections =
    {
        new(0, 1), new(0, -1), new(1, 0), new(-1, 0),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)
    };

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

    public bool HasWalkableNeighbor(Vector2Int cell)
    {
        foreach (var dir in EightDirections)
        {
            if (IsWalkable(cell + dir))
                return true;
        }
        return false;
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
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    private bool IsInBuildZone(Vector3 position, int teamId)
    {
        if (buildZones == null || buildZones.Length == 0) return true;
        foreach (var zone in buildZones)
        {
            if (zone != null && zone.TeamId == teamId && zone.ContainsPoint(position))
                return true;
        }
        return false;
    }

    private void OnDrawGizmos()
    {
        if (cells == null) return;

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                if (cells[x, y] == CellState.Building)
                {
                    Vector3 pos = CellToWorld(new Vector2Int(x, y));
                    pos.y = gridOrigin.y + 0.01f;
                    Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
                    Gizmos.DrawCube(pos, new Vector3(cellSize * 0.9f, 0.1f, cellSize * 0.9f));
                }
            }
        }
    }
}
