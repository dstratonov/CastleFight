using UnityEngine;
using System.Collections.Generic;

public enum CellState
{
    Empty,
    Building,
    ReservedByUnit,
    OccupiedByUnit
}

public struct CellData
{
    public CellState State;
    public GameObject Occupant;
}

public class GridSystem : MonoBehaviour
{
    public static GridSystem Instance { get; private set; }

    [SerializeField] private int gridWidth = 100;
    [SerializeField] private int gridHeight = 100;
    [SerializeField] private float cellSize = 2f;
    [SerializeField] private Vector3 gridOrigin = new(-100f, 0f, -100f);
    [SerializeField] private BuildZone[] buildZones;

    private CellData[,] cells;

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
        cells = new CellData[gridWidth, gridHeight];
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                cells[x, y] = new CellData { State = CellState.Empty, Occupant = null };
            }
        }
    }

    // --- Coordinate Conversion ---

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

    // --- Bounds Checking ---

    public bool IsInBounds(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < gridWidth && cell.y >= 0 && cell.y < gridHeight;
    }

    // --- Cell State Queries ---

    public CellData GetCell(Vector2Int cell)
    {
        if (!IsInBounds(cell))
            return new CellData { State = CellState.Building, Occupant = null };
        return cells[cell.x, cell.y];
    }

    public CellState GetCellState(Vector2Int cell)
    {
        return GetCell(cell).State;
    }

    public bool IsWalkable(Vector2Int cell)
    {
        if (!IsInBounds(cell)) return false;
        return cells[cell.x, cell.y].State == CellState.Empty;
    }

    public bool IsWalkableOrOccupiedBy(Vector2Int cell, GameObject unit)
    {
        if (!IsInBounds(cell)) return false;
        var data = cells[cell.x, cell.y];
        if (data.State == CellState.Empty) return true;
        return (data.State == CellState.OccupiedByUnit || data.State == CellState.ReservedByUnit)
               && data.Occupant == unit;
    }

    // --- Building Placement ---

    public bool CanPlaceBuilding(Vector3 worldPosition, int teamId)
    {
        Vector2Int cell = WorldToCell(worldPosition);
        if (!IsInBounds(cell)) return false;
        if (cells[cell.x, cell.y].State != CellState.Empty) return false;
        if (!IsInBuildZone(worldPosition, teamId)) return false;
        return true;
    }

    public void PlaceBuilding(Vector2Int cell, GameObject building)
    {
        if (!IsInBounds(cell)) return;
        cells[cell.x, cell.y] = new CellData { State = CellState.Building, Occupant = building };
    }

    public void RemoveBuilding(Vector2Int cell)
    {
        if (!IsInBounds(cell)) return;
        cells[cell.x, cell.y] = new CellData { State = CellState.Empty, Occupant = null };
    }

    // --- Unit Occupancy ---

    public bool TryOccupyCell(Vector2Int cell, GameObject unit)
    {
        if (!IsInBounds(cell)) return false;
        if (cells[cell.x, cell.y].State != CellState.Empty) return false;
        cells[cell.x, cell.y] = new CellData { State = CellState.OccupiedByUnit, Occupant = unit };
        return true;
    }

    public bool TryReserveCell(Vector2Int cell, GameObject unit)
    {
        if (!IsInBounds(cell)) return false;
        if (cells[cell.x, cell.y].State != CellState.Empty) return false;
        cells[cell.x, cell.y] = new CellData { State = CellState.ReservedByUnit, Occupant = unit };
        return true;
    }

    public void SetCellOccupied(Vector2Int cell, GameObject unit)
    {
        if (!IsInBounds(cell)) return;
        cells[cell.x, cell.y] = new CellData { State = CellState.OccupiedByUnit, Occupant = unit };
    }

    public void ReleaseCell(Vector2Int cell, GameObject unit)
    {
        if (!IsInBounds(cell)) return;
        if (cells[cell.x, cell.y].Occupant == unit)
            cells[cell.x, cell.y] = new CellData { State = CellState.Empty, Occupant = null };
    }

    // --- Neighbor Queries ---

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

    public bool HasWalkableNeighbor(Vector2Int cell)
    {
        foreach (var dir in EightDirections)
        {
            if (IsWalkable(cell + dir))
                return true;
        }
        return false;
    }

    // --- Build Zone ---

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

    // --- Debug ---

    private void OnDrawGizmos()
    {
        if (cells == null) return;

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                Vector3 pos = CellToWorld(new Vector2Int(x, y));
                pos.y = gridOrigin.y + 0.01f;

                switch (cells[x, y].State)
                {
                    case CellState.Building:
                        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
                        Gizmos.DrawCube(pos, new Vector3(cellSize * 0.9f, 0.1f, cellSize * 0.9f));
                        break;
                    case CellState.OccupiedByUnit:
                        Gizmos.color = new Color(0f, 0f, 1f, 0.3f);
                        Gizmos.DrawCube(pos, new Vector3(cellSize * 0.9f, 0.1f, cellSize * 0.9f));
                        break;
                    case CellState.ReservedByUnit:
                        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
                        Gizmos.DrawCube(pos, new Vector3(cellSize * 0.9f, 0.1f, cellSize * 0.9f));
                        break;
                }
            }
        }
    }
}
