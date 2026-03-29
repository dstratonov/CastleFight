using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Grid-based attack range. The unit's footprint is expanded by attackRangeCells
/// in each direction to form the attack rectangle. A target is "in range" when
/// its footprint cells intersect this rectangle.
/// </summary>
public static class AttackRangeHelper
{
    /// <summary>
    /// Compute the attack rectangle: unit footprint expanded by attackRangeCells.
    /// Returns (min, max) cell bounds inclusive.
    /// </summary>
    public static (Vector2Int min, Vector2Int max) GetAttackRect(
        Vector2Int unitCell, int footprintSize, int attackRangeCells)
    {
        int halfLow = (footprintSize - 1) / 2;
        int halfHigh = footprintSize / 2;

        Vector2Int min = new Vector2Int(
            unitCell.x - halfLow - attackRangeCells,
            unitCell.y - halfLow - attackRangeCells);
        Vector2Int max = new Vector2Int(
            unitCell.x + halfHigh + attackRangeCells,
            unitCell.y + halfHigh + attackRangeCells);

        return (min, max);
    }

    /// <summary>
    /// Compute the footprint rectangle for an entity at the given cell.
    /// Returns (min, max) cell bounds inclusive.
    /// </summary>
    public static (Vector2Int min, Vector2Int max) GetFootprintRect(
        Vector2Int cell, int footprintSize)
    {
        int halfLow = (footprintSize - 1) / 2;
        int halfHigh = footprintSize / 2;

        Vector2Int min = new Vector2Int(cell.x - halfLow, cell.y - halfLow);
        Vector2Int max = new Vector2Int(cell.x + halfHigh, cell.y + halfHigh);

        return (min, max);
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
    /// Check if a target's footprint is within the attacker's attack range on the grid.
    /// </summary>
    public static bool IsTargetInRange(
        GridSystem grid, Vector3 attackerPos, int attackerFootprint, int attackRangeCells,
        GameObject targetObj)
    {
        Vector2Int attackerCell = grid.WorldToCell(attackerPos);
        var (atkMin, atkMax) = GetAttackRect(attackerCell, attackerFootprint, attackRangeCells);

        // Get target's footprint cells from bounds
        Bounds targetBounds = BoundsHelper.GetPhysicalBounds(targetObj);
        var targetCells = grid.GetCellsOverlappingBounds(targetBounds);

        // Check if any target cell falls within the attack rect
        foreach (var cell in targetCells)
        {
            if (cell.x >= atkMin.x && cell.x <= atkMax.x &&
                cell.y >= atkMin.y && cell.y <= atkMax.y)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Find the best walkable cell for the attacker where its attack rectangle
    /// overlaps the target's footprint. Picks the cell closest to the attacker.
    /// </summary>
    public static Vector2Int? FindAttackCell(
        GridSystem grid, Vector3 attackerPos, int attackerFootprint, int attackRangeCells,
        GameObject targetObj)
    {
        Vector2Int attackerCell = grid.WorldToCell(attackerPos);

        // Get target footprint bounds on grid
        Bounds targetBounds = BoundsHelper.GetPhysicalBounds(targetObj);
        var targetCells = grid.GetCellsOverlappingBounds(targetBounds);
        if (targetCells.Count == 0) return null;

        // Compute target's bounding rect on grid
        Vector2Int tMin = targetCells[0], tMax = targetCells[0];
        foreach (var c in targetCells)
        {
            tMin = Vector2Int.Min(tMin, c);
            tMax = Vector2Int.Max(tMax, c);
        }

        // The attacker needs to stand at a cell where its attack rect overlaps the target rect.
        // Expand search area: any cell within (attackRangeCells + footprint) of the target rect.
        int halfLow = (attackerFootprint - 1) / 2;
        int halfHigh = attackerFootprint / 2;
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

                // Check full footprint fits
                if (!GridAStar.IsFootprintWalkable(grid, candidate, halfLow, halfHigh))
                    continue;

                // Check attack rect overlaps target rect
                var (atkMin, atkMax) = GetAttackRect(candidate, attackerFootprint, attackRangeCells);
                if (!RectsOverlap(atkMin, atkMax, tMin, tMax))
                    continue;

                // Pick closest to attacker
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
