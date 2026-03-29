using UnityEngine;

/// <summary>
/// Stateless targeting service. Finds IAttackable targets for a unit by scanning
/// units, buildings, and castles in priority order.
/// </summary>
public static class TargetingService
{
    /// <summary>
    /// Find the best target for a unit at the given position.
    /// Returns null if nothing is in range.
    /// </summary>
    public static IAttackable FindTarget(Vector3 position, int teamId, float aggroRadius, float attackRange, float unitRadius)
    {
        // Priority 1: enemy units within aggro radius
        var unitTarget = FindEnemyUnit(position, teamId, aggroRadius);
        if (unitTarget != null)
            return unitTarget;

        // Priority 2: enemy buildings within attack range
        float structureRange = attackRange + unitRadius;
        var building = FindEnemyBuilding(position, teamId, structureRange);
        if (building != null)
            return building;

        // Priority 3: enemy castle within attack range
        var castle = FindEnemyCastle(position, teamId, structureRange);
        if (castle != null)
            return castle;

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
    //  BUILDING SCAN
    // ================================================================

    private static IAttackable FindEnemyBuilding(Vector3 position, int teamId, float maxRange)
    {
        if (BuildingManager.Instance == null) return null;

        int enemyTeam = TeamManager.Instance != null
            ? TeamManager.Instance.GetEnemyTeamId(teamId)
            : (teamId == 0 ? 1 : 0);

        var buildings = BuildingManager.Instance.GetTeamBuildings(enemyTeam);
        float bestDistSq = float.MaxValue;
        Building best = null;

        for (int i = 0; i < buildings.Count; i++)
        {
            var b = buildings[i];
            if (b == null) continue;
            IAttackable attackable = b;
            if (attackable.Health == null || attackable.Health.IsDead) continue;

            // Distance to closest edge, not center
            Vector3 closest = BoundsHelper.ClosestPoint(b.gameObject, position);
            float distSq = (closest - position).sqrMagnitude;

            if (distSq <= maxRange * maxRange && distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = b;
            }
        }

        return best;
    }

    // ================================================================
    //  CASTLE SCAN
    // ================================================================

    private static IAttackable FindEnemyCastle(Vector3 position, int teamId, float maxRange)
    {
        Castle castle = GameRegistry.GetEnemyCastle(teamId);
        if (castle == null || castle.Health == null || castle.Health.IsDead) return null;

        // Distance to closest edge, not center
        Vector3 closest = BoundsHelper.ClosestPoint(castle.gameObject, position);
        float dist = Vector3.Distance(position, closest);
        if (dist > maxRange) return null;

        return castle;
    }
}
