using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Tracks which grid cells each unit occupies based on its radius.
/// This is a separate layer from walkability — unit cells are NOT obstacles,
/// they just mark "a unit is here." Updated every frame as units move.
/// </summary>
public class UnitGridPresence : MonoBehaviour
{
    public static UnitGridPresence Instance { get; private set; }

    private GridSystem grid;

    // Cell -> list of unit instance IDs occupying it
    // Using int[] per cell for minimal allocation (most cells have 0-2 units)
    private Dictionary<long, List<int>> cellOccupants = new();

    // Unit -> cells it currently occupies (for efficient removal on move)
    private Dictionary<int, List<Vector2Int>> unitCells = new();

    // Reusable buffer for cell computation
    private static readonly List<Vector2Int> cellBuffer = new(16);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void LateUpdate()
    {
        grid = GridSystem.Instance;
        if (grid == null || UnitManager.Instance == null) return;

        UpdateAllUnits();
    }

    private void UpdateAllUnits()
    {
        // Clear all occupancy
        cellOccupants.Clear();
        unitCells.Clear();

        var allUnits = UnitManager.Instance.AllUnits;
        for (int i = 0; i < allUnits.Count; i++)
        {
            var unit = allUnits[i];
            if (unit == null || unit.IsDead) continue;

            int unitId = unit.GetInstanceID();
            int footprint = unit.FootprintSize;
            Vector3 pos = unit.transform.position;

            // Compute which cells this unit covers
            ComputeOccupiedCells(pos, footprint, cellBuffer);

            // Store unit -> cells mapping
            var cells = new List<Vector2Int>(cellBuffer.Count);
            cells.AddRange(cellBuffer);
            unitCells[unitId] = cells;

            // Store cell -> unit mapping
            for (int c = 0; c < cellBuffer.Count; c++)
            {
                long key = CellKey(cellBuffer[c]);
                if (!cellOccupants.TryGetValue(key, out var list))
                {
                    list = new List<int>(4);
                    cellOccupants[key] = list;
                }
                list.Add(unitId);
            }
        }
    }

    /// <summary>
    /// Compute the fixed rectangular footprint of a unit on the grid.
    /// footprintSize is set directly in UnitData (1=small, 2=large, 3=huge).
    /// The rectangle only shifts position as the unit moves, never changes shape.
    /// </summary>
    private void ComputeOccupiedCells(Vector3 worldPos, int footprintSize, List<Vector2Int> result)
    {
        result.Clear();
        Vector2Int center = grid.WorldToCell(worldPos);

        int cellSpan = Mathf.Max(1, footprintSize);
        int halfLow = (cellSpan - 1) / 2;
        int halfHigh = cellSpan / 2;

        for (int dx = -halfLow; dx <= halfHigh; dx++)
        {
            for (int dy = -halfLow; dy <= halfHigh; dy++)
            {
                Vector2Int cell = new Vector2Int(center.x + dx, center.y + dy);
                if (grid.IsInBounds(cell))
                    result.Add(cell);
            }
        }
    }

    /// <summary>
    /// Get how many units occupy a given cell.
    /// </summary>
    public int GetUnitCount(Vector2Int cell)
    {
        long key = CellKey(cell);
        if (cellOccupants.TryGetValue(key, out var list))
            return list.Count;
        return 0;
    }

    /// <summary>
    /// Check if any unit occupies a given cell.
    /// </summary>
    public bool IsOccupied(Vector2Int cell)
    {
        return cellOccupants.ContainsKey(CellKey(cell));
    }

    /// <summary>
    /// Check if a cell is occupied by any unit OTHER than the given one.
    /// </summary>
    public bool IsOccupiedByOther(Vector2Int cell, int excludeUnitId)
    {
        long key = CellKey(cell);
        if (!cellOccupants.TryGetValue(key, out var list)) return false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != excludeUnitId) return true;
        }
        return false;
    }

    /// <summary>
    /// Get the cells a specific unit currently occupies.
    /// </summary>
    public IReadOnlyList<Vector2Int> GetUnitCells(int unitInstanceId)
    {
        if (unitCells.TryGetValue(unitInstanceId, out var cells))
            return cells;
        return null;
    }

    /// <summary>
    /// Get all unit IDs occupying a specific cell.
    /// </summary>
    public IReadOnlyList<int> GetOccupants(Vector2Int cell)
    {
        long key = CellKey(cell);
        if (cellOccupants.TryGetValue(key, out var list))
            return list;
        return null;
    }

    private static long CellKey(Vector2Int cell)
    {
        return ((long)cell.x << 32) | (uint)cell.y;
    }
}
