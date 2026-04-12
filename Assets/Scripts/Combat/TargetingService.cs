using UnityEngine;

/// <summary>
/// Stateless targeting service. Finds IAttackable targets for a unit by scanning
/// units, buildings, and castles in priority order.
///
/// All detection uses aggroRadius (world-space distance).
/// Unit scan uses SpatialHashGrid. Structure scan uses BoundsHelper closest-point distance.
/// </summary>
public static class TargetingService
{
    /// <summary>
    /// Find the best target for a unit at the given position.
    /// Returns null if nothing is in range.
    /// </summary>
    public static IAttackable FindTarget(Vector3 position, int teamId, float aggroRadius)
    {
        // Priority 1: enemy units within aggro radius (world-space)
        var unitTarget = FindEnemyUnit(position, teamId, aggroRadius);
        if (unitTarget != null)
            return unitTarget;

        // Priority 2-3: enemy structures within aggro radius (world-space distance)
        // Uses aggroRadius so units notice buildings before walking past them,
        // not just when already in attack range.
        var structure = FindEnemyStructure(position, teamId, aggroRadius);
        if (structure != null)
            return structure;

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
    //  STRUCTURE SCAN (buildings + castle, world-space distance)
    // ================================================================

    private static IAttackable FindEnemyStructure(Vector3 position, int teamId, float aggroRadius)
    {
        // Check enemy buildings within aggro radius
        if (BuildingManager.Instance != null)
        {
            var building = BuildingManager.Instance.FindNearestEnemyBuilding(position, teamId, aggroRadius);
            if (building != null)
                return building;
        }

        // Check enemy castle within aggro radius
        Castle castle = GameRegistry.GetEnemyCastle(teamId);
        if (castle != null && castle.Health != null && !castle.Health.IsDead)
        {
            float distSq = BoundsHelper.ClosestPointDistanceSq(position, castle.gameObject);
            if (distSq <= aggroRadius * aggroRadius)
                return castle;
        }

        return null;
    }
}
