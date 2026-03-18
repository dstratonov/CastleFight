using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Pure spawn position logic extracted from Spawner for testability.
/// No MonoBehaviour, no UnitManager, no GridSystem.Instance.
/// </summary>
public static class SpawnPositionFinder
{
    public struct NearbyUnit
    {
        public Vector3 Position;
        public float Radius;
        public bool IsDead;
    }

    /// <summary>
    /// Check if a spawn position is clear of nearby units.
    /// </summary>
    public static bool IsPositionClear(Vector3 position, float unitRadius, List<NearbyUnit> nearbyUnits)
    {
        if (nearbyUnits == null) return true;

        foreach (var other in nearbyUnits)
        {
            if (other.IsDead) continue;
            float combinedRadius = unitRadius + other.Radius;
            float dist = Vector3.Distance(position, other.Position);
            if (dist < combinedRadius * 0.8f)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Check if a cell has sufficient clearance for a unit of the given radius.
    /// </summary>
    public static bool HasClearance(Vector2Int cell, float unitRadius, float cellSize, System.Func<Vector2Int, bool> isWalkable, System.Func<Vector2Int, bool> isInBounds)
    {
        int cellRadius = Mathf.CeilToInt(unitRadius / cellSize) + 1;
        for (int dx = -cellRadius; dx <= cellRadius; dx++)
        {
            for (int dz = -cellRadius; dz <= cellRadius; dz++)
            {
                Vector2Int adj = new(cell.x + dx, cell.y + dz);
                if (!isInBounds(adj) || !isWalkable(adj))
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Compute fallback position using angular sweep around base position.
    /// Returns the angle and distance for a given attempt index.
    /// </summary>
    public static Vector3 ComputeFallbackOffset(int attemptIndex, float baseSpread)
    {
        float angle = attemptIndex * 45f * Mathf.Deg2Rad;
        float dist = baseSpread * (1f + attemptIndex * 0.5f);
        return new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
    }

    /// <summary>
    /// Compute spawn spread based on unit radius.
    /// </summary>
    public static float ComputeSpawnSpread(float unitRadius)
    {
        return Mathf.Max(3f, unitRadius * 3f);
    }
}
