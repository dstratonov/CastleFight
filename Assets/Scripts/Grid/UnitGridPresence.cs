using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Tracks which grid cells each unit occupies. ALL units are grid obstacles —
/// their cells are marked in GridSystem.unitObstacles so A* routes around them.
/// When a unit's cells change (it moves), affected paths are invalidated.
/// </summary>
public class UnitGridPresence : MonoBehaviour
{
    public static UnitGridPresence Instance { get; private set; }

    private GridSystem grid;

    // Cell -> list of unit instance IDs occupying it
    private Dictionary<long, List<int>> cellOccupants = new();

    // Unit -> cells it currently occupies
    private Dictionary<int, List<Vector2Int>> unitCells = new();

    // Unit -> team (for team-aware queries)
    private Dictionary<int, int> unitTeams = new();

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
        var allUnits = UnitManager.Instance.AllUnits;

        // Detect cell changes and update grid obstacles
        var newCellOccupants = new Dictionary<long, List<int>>();

        for (int i = 0; i < allUnits.Count; i++)
        {
            var unit = allUnits[i];
            if (unit == null || unit.IsDead) continue;

            int unitId = unit.GetInstanceID();
            int footprint = unit.FootprintSize;

            ComputeOccupiedCells(unit.transform.position, footprint, cellBuffer);

            // Check if cells changed from last frame
            bool changed = false;
            if (!unitCells.TryGetValue(unitId, out var oldCells))
            {
                changed = true;
            }
            else if (oldCells.Count != cellBuffer.Count)
            {
                changed = true;
            }
            else
            {
                for (int c = 0; c < cellBuffer.Count; c++)
                {
                    if (oldCells[c] != cellBuffer[c]) { changed = true; break; }
                }
            }

            int teamId = unit.TeamId;

            if (changed)
            {
                // Unmark old cells (use stored team in case team changed)
                int oldTeam = unitTeams.TryGetValue(unitId, out int t) ? t : teamId;
                if (oldCells != null)
                    grid.UnmarkUnitObstacle(oldCells, oldTeam);

                // Mark new cells
                var newCells = new List<Vector2Int>(cellBuffer);
                grid.MarkUnitObstacle(newCells, teamId);

                unitCells[unitId] = newCells;
            }

            unitTeams[unitId] = teamId;

            // Update cell -> unit mapping
            for (int c = 0; c < cellBuffer.Count; c++)
            {
                long key = CellKey(cellBuffer[c]);
                if (!newCellOccupants.TryGetValue(key, out var list))
                {
                    list = new List<int>(4);
                    newCellOccupants[key] = list;
                }
                list.Add(unitId);
            }
        }

        // Clean up units that no longer exist
        var deadUnits = new List<int>();
        foreach (var kvp in unitCells)
        {
            bool alive = false;
            for (int i = 0; i < allUnits.Count; i++)
            {
                if (allUnits[i] != null && allUnits[i].GetInstanceID() == kvp.Key)
                { alive = true; break; }
            }
            if (!alive)
            {
                int deadTeam = unitTeams.TryGetValue(kvp.Key, out int dt) ? dt : 0;
                grid.UnmarkUnitObstacle(kvp.Value, deadTeam);
                deadUnits.Add(kvp.Key);
            }
        }
        foreach (var id in deadUnits)
        {
            unitCells.Remove(id);
            unitTeams.Remove(id);
        }

        cellOccupants = newCellOccupants;
    }

    /// <summary>
    /// Temporarily unmark a unit's cells so its own A* doesn't self-block.
    /// Call RemarkUnit after path computation.
    /// </summary>
    public void UnmarkUnit(int unitId)
    {
        if (!unitCells.TryGetValue(unitId, out var cells)) return;
        int teamId = unitTeams.TryGetValue(unitId, out int t) ? t : 0;
        grid?.UnmarkUnitObstacle(cells, teamId);
    }

    /// <summary>
    /// Re-mark a unit's cells after path computation.
    /// </summary>
    public void RemarkUnit(int unitId)
    {
        if (!unitCells.TryGetValue(unitId, out var cells)) return;
        int teamId = unitTeams.TryGetValue(unitId, out int t) ? t : 0;
        grid?.MarkUnitObstacle(cells, teamId);
    }

    private void ComputeOccupiedCells(Vector3 worldPos, int footprintSize, List<Vector2Int> result)
    {
        Vector2Int center = grid.WorldToCell(worldPos);
        FootprintHelper.GetCells(center, footprintSize, result);
        // Remove out-of-bounds cells
        for (int i = result.Count - 1; i >= 0; i--)
        {
            if (!grid.IsInBounds(result[i]))
                result.RemoveAt(i);
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
    /// Check if a cell is occupied by a friendly unit (same team, not self).
    /// </summary>
    public bool IsOccupiedByFriendly(Vector2Int cell, int excludeUnitId, int teamId)
    {
        long key = CellKey(cell);
        if (!cellOccupants.TryGetValue(key, out var list)) return false;
        for (int i = 0; i < list.Count; i++)
        {
            int occupantId = list[i];
            if (occupantId == excludeUnitId) continue;
            if (unitTeams.TryGetValue(occupantId, out int otherTeam) && otherTeam == teamId)
                return true;
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
