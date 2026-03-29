using UnityEngine;
using System.Collections.Generic;

public enum CellState
{
    Empty,
    Building
}

public class GridSystem : MonoBehaviour, IGrid
{
    public static GridSystem Instance { get; private set; }

    [SerializeField] private int gridWidth = 100;
    [SerializeField] private int gridHeight = 100;
    [SerializeField] private float cellSize = 2f;
    [SerializeField] private Vector3 gridOrigin = new(-100f, 0f, -100f);
    [SerializeField] private BuildZone[] buildZones;

    private CellState[,] cells;

    // Per-team unit obstacle layers. Units only block pathfinding for same-team units.
    // Enemy units are not obstacles — the combat system handles engagement.
    private int[,] unitObstaclesTeam0;
    private int[,] unitObstaclesTeam1;

    /// <summary>
    /// Controls which unit obstacles IsWalkable checks.
    ///  -1 = no unit obstacles (default, building placement)
    ///   0 = team 0 only (soft lock marching)
    ///   1 = team 1 only (soft lock marching)
    ///  -2 = ALL unit obstacles (hard lock, must navigate around everyone)
    /// </summary>
    public int WalkableTeamContext { get; set; } = -1;

    private ClearanceMap clearanceMap;
    public ClearanceMap ClearanceMap => clearanceMap;

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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    private GridLogic logic;
    public GridLogic Logic => logic;

    private void InitializeGrid()
    {
        cells = new CellState[gridWidth, gridHeight];
        unitObstaclesTeam0 = new int[gridWidth, gridHeight];
        unitObstaclesTeam1 = new int[gridWidth, gridHeight];
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
                cells[x, y] = CellState.Empty;
        logic = new GridLogic(gridWidth, gridHeight, cellSize, gridOrigin);
    }

    public void BuildClearanceMap()
    {
        clearanceMap = new ClearanceMap();
        clearanceMap.ComputeFull(this);
    }

    public void UpdateClearanceRegion(Vector2Int min, Vector2Int max)
    {
        clearanceMap?.UpdateRegion(min, max, this);
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
        if (cells[cell.x, cell.y] != CellState.Empty) return false;

        if (WalkableTeamContext == -2)
        {
            // Hard lock: check ALL unit obstacles
            if (unitObstaclesTeam0[cell.x, cell.y] > 0) return false;
            if (unitObstaclesTeam1[cell.x, cell.y] > 0) return false;
        }
        else if (WalkableTeamContext >= 0)
        {
            // Soft lock: check same-team only
            var teamObstacles = WalkableTeamContext == 0 ? unitObstaclesTeam0 : unitObstaclesTeam1;
            if (teamObstacles[cell.x, cell.y] > 0) return false;
        }

        return true;
    }

    /// <summary>
    /// Mark cells as blocked by a unit on the given team.
    /// </summary>
    public void MarkUnitObstacle(List<Vector2Int> cellList, int teamId)
    {
        var arr = teamId == 0 ? unitObstaclesTeam0 : unitObstaclesTeam1;
        foreach (var cell in cellList)
        {
            if (IsInBounds(cell))
                arr[cell.x, cell.y]++;
        }
    }

    /// <summary>
    /// Unmark cells previously blocked by a unit on the given team.
    /// </summary>
    public void UnmarkUnitObstacle(List<Vector2Int> cellList, int teamId)
    {
        var arr = teamId == 0 ? unitObstaclesTeam0 : unitObstaclesTeam1;
        foreach (var cell in cellList)
        {
            if (IsInBounds(cell))
                arr[cell.x, cell.y] = Mathf.Max(0, arr[cell.x, cell.y] - 1);
        }
    }

    public bool CanPlaceBuilding(Vector3 worldPosition, int teamId)
    {
        if (!IsInBuildZone(worldPosition, teamId)) return false;
        Vector2Int cell = WorldToCell(worldPosition);
        if (!IsInBounds(cell) || cells[cell.x, cell.y] != CellState.Empty)
            return false;
        return true;
    }

    /// <summary>
    /// Full footprint validation: checks that ALL cells a building would occupy are empty.
    /// </summary>
    public bool CanPlaceBuildingFootprint(Vector3 worldPosition, int teamId, Vector3 buildingSize)
    {
        if (!CanPlaceBuilding(worldPosition, teamId)) return false;

        Bounds footprint = new Bounds(worldPosition, buildingSize);
        var footprintCells = GetCellsOverlappingBounds(footprint);
        return AreCellsEmpty(footprintCells);
    }

    public List<Vector2Int> GetCellsOverlappingBounds(Bounds worldBounds)
    {
        return logic.GetCellsOverlappingBounds(worldBounds);
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
        if (cellList.Count == 0) return;
        Vector2Int min = cellList[0], max = cellList[0];
        foreach (var cell in cellList)
        {
            if (IsInBounds(cell))
                cells[cell.x, cell.y] = state;
            min = Vector2Int.Min(min, cell);
            max = Vector2Int.Max(max, cell);
        }
        if (clearanceMap != null)
            UpdateClearanceRegion(min, max);
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
