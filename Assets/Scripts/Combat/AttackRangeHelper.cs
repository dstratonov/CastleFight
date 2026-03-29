using UnityEngine;

/// <summary>
/// Grid-based attack range. The unit's footprint is expanded by attackRangeCells
/// in each direction to form the attack rectangle. A target is "in range" when
/// its footprint rectangle intersects this attack rectangle.
/// All footprint calculations go through FootprintHelper.
/// </summary>
public static class AttackRangeHelper
{
    /// <summary>
    /// Compute the attack rectangle: unit footprint expanded by attackRangeCells.
    /// </summary>
    public static (Vector2Int min, Vector2Int max) GetAttackRect(
        Vector2Int unitCell, int footprintSize, int attackRangeCells)
    {
        var (fpMin, fpMax) = FootprintHelper.GetRect(unitCell, footprintSize);
        return (
            new Vector2Int(fpMin.x - attackRangeCells, fpMin.y - attackRangeCells),
            new Vector2Int(fpMax.x + attackRangeCells, fpMax.y + attackRangeCells)
        );
    }

    /// <summary>
    /// Check if two axis-aligned rectangles overlap (inclusive bounds).
    /// </summary>
    public static bool RectsOverlap(Vector2Int minA, Vector2Int maxA, Vector2Int minB, Vector2Int maxB)
    {
        return minA.x <= maxB.x && maxA.x >= minB.x
            && minA.y <= maxB.y && maxA.y >= minB.y;
    }

    /// <summary>
    /// Check if a target is within the attacker's attack range using grid rect intersection.
    /// </summary>
    public static bool IsTargetInRange(
        GridSystem grid, Vector3 attackerPos, int attackerFootprint, int attackRangeCells,
        IAttackable target)
    {
        Vector2Int attackerCell = grid.WorldToCell(attackerPos);
        var (atkMin, atkMax) = GetAttackRect(attackerCell, attackerFootprint, attackRangeCells);

        var (tMin, tMax) = FootprintHelper.GetRect(target.CurrentCell, target.FootprintSize);

        return RectsOverlap(atkMin, atkMax, tMin, tMax);
    }

    /// <summary>
    /// Find the best walkable cell for the attacker where its attack rectangle
    /// overlaps the target's footprint. Picks the cell closest to the attacker.
    /// </summary>
    public static Vector2Int? FindAttackCell(
        GridSystem grid, Vector3 attackerPos, int attackerFootprint, int attackRangeCells,
        IAttackable target)
    {
        Vector2Int attackerCell = grid.WorldToCell(attackerPos);
        var (tMin, tMax) = FootprintHelper.GetRect(target.CurrentCell, target.FootprintSize);

        // Search area: expand target rect by attacker's footprint + attack range
        FootprintHelper.GetHalfExtents(attackerFootprint, out int halfLow, out int halfHigh);
        int expand = attackRangeCells + Mathf.Max(halfLow, halfHigh) + 1;

        Vector2Int searchMin = new Vector2Int(tMin.x - expand, tMin.y - expand);
        Vector2Int searchMax = new Vector2Int(tMax.x + expand, tMax.y + expand);

        float bestDistSq = float.MaxValue;
        Vector2Int? bestCell = null;

        for (int x = searchMin.x; x <= searchMax.x; x++)
        {
            for (int y = searchMin.y; y <= searchMax.y; y++)
            {
                Vector2Int candidate = new Vector2Int(x, y);

                if (!FootprintHelper.IsWalkable(grid, candidate, attackerFootprint))
                    continue;

                var (atkMin, atkMax) = GetAttackRect(candidate, attackerFootprint, attackRangeCells);
                if (!RectsOverlap(atkMin, atkMax, tMin, tMax))
                    continue;

                float distSq = (candidate - attackerCell).sqrMagnitude;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestCell = candidate;
                }
            }
        }

        return bestCell;
    }
}
