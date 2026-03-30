using UnityEngine;

/// <summary>
/// Stateless targeting service. Finds IAttackable targets for a unit by scanning
/// units, buildings, and castles in priority order.
///
/// Unit detection uses aggroRadius (world-space, via SpatialHashGrid).
/// Structure detection uses grid-based attack range (cell rectangle intersection).
/// </summary>
public static class TargetingService
{
    /// <summary>
    /// Find the best target for a unit at the given position.
    /// Returns null if nothing is in range.
    /// </summary>
    public static IAttackable FindTarget(Vector3 position, int teamId, float aggroRadius,
        int attackRangeCells, int footprintSize)
    {
        // Priority 1: enemy units within aggro radius (world-space)
        var unitTarget = FindEnemyUnit(position, teamId, aggroRadius);
        if (unitTarget != null)
            return unitTarget;

        // Priority 2-3: enemy structures within grid attack range
        var grid = GridSystem.Instance;
        if (grid != null)
        {
            var structure = FindEnemyStructure(grid, position, teamId, attackRangeCells, footprintSize);
            if (structure != null)
                return structure;
        }

        // Fallback: enemy castle regardless of range (default objective)
        return GetDefaultTarget(teamId);
    }

    /// <summary>
    /// Get the default target for a unit (the enemy castle). Always returns it regardless of range.
    /// </summary>
    public static IAttackable GetDefaultTarget(int teamId)
    {
        return GameRegistry.GetEnemyCastle(teamId);
    }

    // ================================================================
    //  UNIT SCAN
    // ================================================================

    private static IAttackable FindEnemyUnit(Vector3 position, int teamId, float aggroRadius)
    {
        if (UnitManager.Instance == null) return null;
        return UnitManager.Instance.FindNearestEnemy(position, teamId, aggroRadius);
    }

    // ================================================================
    //  STRUCTURE SCAN (buildings + castle, grid-based)
    // ================================================================

    private static IAttackable FindEnemyStructure(GridSystem grid, Vector3 position, int teamId,
        int attackRangeCells, int footprintSize)
    {
        Vector2Int myCell = grid.WorldToCell(position);
        var (atkMin, atkMax) = AttackRangeHelper.GetAttackRect(myCell, footprintSize, attackRangeCells);

        int enemyTeam = TeamManager.Instance != null
            ? TeamManager.Instance.GetEnemyTeamId(teamId)
            : (teamId == 0 ? 1 : 0);

        // Check enemy buildings
        if (BuildingManager.Instance != null)
        {
            var buildings = BuildingManager.Instance.GetTeamBuildings(enemyTeam);
            float bestDistSq = float.MaxValue;
            Building best = null;

            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b == null) continue;
                IAttackable attackable = b;
                if (attackable.Health == null || attackable.Health.IsDead) continue;

                var (bMin, bMax) = attackable.FootprintBounds;
                bool overlaps = AttackRangeHelper.RectsOverlap(atkMin, atkMax, bMin, bMax);

                if (overlaps)
                {
                    float distSq = (b.transform.position - position).sqrMagnitude;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        best = b;
                    }
                }
            }

            if (best != null)
                return best;
        }

        // Check enemy castle
        Castle castle = GameRegistry.GetEnemyCastle(teamId);
        if (castle != null && castle.Health != null && !castle.Health.IsDead)
        {
            IAttackable castleTarget = castle;
            var (cMin, cMax) = castleTarget.FootprintBounds;
            if (AttackRangeHelper.RectsOverlap(atkMin, atkMax, cMin, cMax))
                return castle;
        }

        return null;
    }
}
