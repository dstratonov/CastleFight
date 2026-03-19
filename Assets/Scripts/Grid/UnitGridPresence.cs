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
            float radius = unit.EffectiveRadius;
            Vector3 pos = unit.transform.position;

            // Compute which cells this unit covers
            ComputeOccupiedCells(pos, radius, cellBuffer);

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
    /// The rectangle size is constant for a given radius — it never changes
    /// shape as the unit moves, only shifts to the nearest cell center.
    /// Uses the unit's diameter to determine how many cells it spans.
    ///
    /// Examples (cellSize=2):
    ///   radius 0.5 → diameter 1.0 → 1x1 cell  (small unit: goblin, rat)
    ///   radius 1.0 → diameter 2.0 → 1x1 cell  (medium unit: knight, wolf)
    ///   radius 1.5 → diameter 3.0 → 2x2 cells (large unit: cyclops, griffin)
    ///   radius 2.5 → diameter 5.0 → 3x3 cells (huge unit: dragon, hydra)
    ///   radius 4.0 → diameter 8.0 → 4x4 cells (massive unit)
    /// </summary>
    private void ComputeOccupiedCells(Vector3 worldPos, float radius, List<Vector2Int> result)
    {
        result.Clear();
        Vector2Int center = grid.WorldToCell(worldPos);
        float cs = grid.CellSize;

        // How many cells the unit's diameter spans (always at least 1)
        int cellSpan = Mathf.Max(1, Mathf.CeilToInt(radius * 2f / cs));

        // For odd spans (1, 3, 5): symmetric around center
        // For even spans (2, 4): offset so center cell is bottom-left of the 2x2 block
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
    /// Get the cell span (NxN size) for a unit with the given radius.
    /// Useful for external systems that need to know footprint size.
    /// </summary>
    public int GetCellSpan(float radius)
    {
        if (grid == null) return 1;
        return Mathf.Max(1, Mathf.CeilToInt(radius * 2f / grid.CellSize));
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
