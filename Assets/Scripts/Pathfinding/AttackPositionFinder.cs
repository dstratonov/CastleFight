using UnityEngine;
using System.Collections.Generic;

public static class AttackPositionFinder
{
    private static readonly Dictionary<int, Dictionary<Vector2Int, int>> slotRegistry = new();
    private static float cleanupTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        slotRegistry.Clear();
        cleanupTimer = 0f;
    }

    private static readonly Vector2Int[] Dirs =
    {
        new(0, 1), new(0, -1), new(1, 0), new(-1, 0),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)
    };

    private static readonly float[] DirCosts =
    {
        1f, 1f, 1f, 1f,
        1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f
    };

    /// <summary>
    /// Returns (cell, found). Callers must check 'found' — cell is only valid when found is true.
    /// </summary>
    public static (Vector2Int cell, bool found) FindAttackPosition(Unit attacker, Health target, IGrid grid, ClearanceMap clearance)
    {
        Debug.Assert(target != null, "[AttackPositionFinder] FindAttackPosition: target is null");
        Debug.Assert(grid != null, "[AttackPositionFinder] FindAttackPosition: grid is null");
        Debug.Assert(attacker != null, "[AttackPositionFinder] FindAttackPosition: attacker is null");
        if (target == null || grid == null) return (Vector2Int.zero, false);

        Vector3 targetCenter = BoundsHelper.GetCenter(target.gameObject);
        Bounds targetBounds = BoundsHelper.GetPhysicalBounds(target.gameObject);
        float targetRadius = BoundsHelper.GetRadius(target.gameObject);
        Vector3 targetExtents = new Vector3(targetBounds.extents.x, 0f, targetBounds.extents.z);
        float attackRange = GetAttackRange(attacker);
        Debug.Assert(attacker.Data != null, $"[AttackPositionFinder] {attacker.name} FindAttackPosition: attacker.Data is null");
        float unitRadius = attacker.EffectiveRadius;
        bool isRanged = attacker.Data.isRanged;
        Vector3 attackerPos = attacker.transform.position;
        int attackerId = attacker.GetInstanceID();
        int targetId = target.GetInstanceID();

        var result = FindAttackPositionCore(
            grid, clearance,
            targetCenter, targetRadius,
            attackRange, unitRadius, isRanged,
            attackerPos, attackerId, targetId,
            targetExtents);

        if (result.found && GameDebug.Combat)
        {
            Vector3 bestPos = grid.CellToWorld(result.cell);
            float distToTarget2 = Vector3.Distance(bestPos, targetCenter);
            float distToAttacker2 = Vector3.Distance(bestPos, attackerPos);
            int claimedOnTarget = slotRegistry.TryGetValue(targetId, out var s) ? s.Count : 0;
            Debug.Log($"[AttackPos] unit={attacker?.name} -> {target.name} " +
                $"cell={result.cell} distToTarget={distToTarget2:F1} " +
                $"distToAttacker={distToAttacker2:F1} slotsOnTarget={claimedOnTarget} ranged={isRanged}");
        }
        else if (!result.found && GameDebug.Combat)
        {
            Debug.LogWarning($"[AttackPos] NO POSITION for {attacker?.name} -> {target?.name} " +
                $"range={attackRange:F1} unitR={unitRadius:F1}");
        }

        return result;
    }

    /// <summary>
    /// 2D distance from a point to the surface of an axis-aligned bounding box.
    /// Returns 0 when the point is inside the box. This matches the distance
    /// that Bounds.ClosestPoint produces, ensuring attack positions are placed
    /// where UnitCombat.DistanceToTarget will confirm they are in range.
    /// </summary>
    public static float DistToSurface(Vector3 pos, Vector3 boundsCenter, Vector3 boundsExtents)
    {
        float dx = Mathf.Max(0f, Mathf.Abs(pos.x - boundsCenter.x) - boundsExtents.x);
        float dz = Mathf.Max(0f, Mathf.Abs(pos.z - boundsCenter.z) - boundsExtents.z);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Pure-logic core of attack position finding. Decoupled from Unity objects
    /// for unit testing. Runs Dijkstra from the target, then scores candidate cells.
    /// targetExtents: XZ half-sizes of the target AABB. When zero, computed from
    /// targetRadius as a square (backwards compatible with point/circle targets).
    /// </summary>
    public static (Vector2Int cell, bool found) FindAttackPositionCore(
        IGrid grid, ClearanceMap clearance,
        Vector3 targetCenter, float targetRadius,
        float attackRange, float unitRadius, bool isRanged,
        Vector3 attackerPos, int attackerId, int targetId,
        Vector3 targetExtents = default)
    {
        Debug.Assert(grid != null, "[AttackPositionFinder] FindAttackPositionCore: grid is null");
        if (grid == null) return (Vector2Int.zero, false);

        if (targetExtents.x <= 0f && targetExtents.z <= 0f)
            targetExtents = new Vector3(targetRadius, 0f, targetRadius);

        Vector2Int targetCell = grid.WorldToCell(targetCenter);
        float maxExtent = Mathf.Max(targetExtents.x, targetExtents.z);
        float maxDist = maxExtent + attackRange + unitRadius + grid.CellSize * 2;
        int searchRadius = Mathf.CeilToInt(maxDist / grid.CellSize) + 1;

        int size = (searchRadius * 2 + 1);
        int totalCells = size * size;
        float[] cost = new float[totalCells];
        for (int i = 0; i < totalCells; i++)
            cost[i] = float.MaxValue;

        var open = new SortedSet<(float cost, int idx)>(Comparer<(float, int)>.Create(
            (a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2.CompareTo(b.Item2)));

        bool centerWalkable = grid.IsInBounds(targetCell) && grid.IsWalkable(targetCell);
        if (centerWalkable)
        {
            int centerLocal = searchRadius * size + searchRadius;
            cost[centerLocal] = 0f;
            open.Add((0f, centerLocal));
        }
        else
        {
            int borderSearchR = Mathf.CeilToInt(maxExtent / grid.CellSize) + 2;
            borderSearchR = Mathf.Min(borderSearchR, searchRadius);
            for (int dz = -borderSearchR; dz <= borderSearchR; dz++)
            {
                for (int dx = -borderSearchR; dx <= borderSearchR; dx++)
                {
                    Vector2Int wc = new Vector2Int(targetCell.x + dx, targetCell.y + dz);
                    if (!grid.IsInBounds(wc) || !grid.IsWalkable(wc)) continue;

                    bool adjacentToTarget = false;
                    for (int nd = 0; nd < 4; nd++)
                    {
                        Vector2Int adj = new Vector2Int(wc.x + Dirs[nd].x, wc.y + Dirs[nd].y);
                        if (grid.IsInBounds(adj) && !grid.IsWalkable(adj))
                        { adjacentToTarget = true; break; }
                    }
                    if (!adjacentToTarget) continue;

                    if (Mathf.Abs(dx) > searchRadius || Mathf.Abs(dz) > searchRadius) continue;
                    int idx = (dz + searchRadius) * size + (dx + searchRadius);
                    float seedCost = Vector3.Distance(grid.CellToWorld(wc), targetCenter);
                    if (seedCost < cost[idx])
                    {
                        cost[idx] = seedCost;
                        open.Add((seedCost, idx));
                    }
                }
            }
        }

        // Dijkstra expansion
        while (open.Count > 0)
        {
            var (currentCost, currentIdx) = open.Min;
            open.Remove(open.Min);

            if (currentCost > cost[currentIdx])
                continue;

            int cx = currentIdx % size - searchRadius;
            int cz = currentIdx / size - searchRadius;

            for (int d = 0; d < 8; d++)
            {
                int nx = cx + Dirs[d].x;
                int nz = cz + Dirs[d].y;

                if (Mathf.Abs(nx) > searchRadius || Mathf.Abs(nz) > searchRadius)
                    continue;

                Vector2Int worldCell = new Vector2Int(targetCell.x + nx, targetCell.y + nz);
                if (!grid.IsInBounds(worldCell) || !grid.IsWalkable(worldCell))
                    continue;

                if (d >= 4)
                {
                    var adj1 = new Vector2Int(targetCell.x + cx + Dirs[d].x, targetCell.y + cz);
                    var adj2 = new Vector2Int(targetCell.x + cx, targetCell.y + cz + Dirs[d].y);
                    if (!grid.IsWalkable(adj1) || !grid.IsWalkable(adj2))
                        continue;
                }

                float stepCost = DirCosts[d] * grid.CellSize;
                int nIdx = (nz + searchRadius) * size + (nx + searchRadius);
                float newCost = currentCost + stepCost;

                if (newCost < cost[nIdx])
                {
                    cost[nIdx] = newCost;
                    open.Add((newCost, nIdx));
                }
            }
        }

        // Score candidate cells using AABB surface distance.
        // This matches UnitCombat.DistanceToTarget (which uses Bounds.ClosestPoint),
        // guaranteeing that returned positions are within actual attack range.
        float bestScore = float.MaxValue;
        Vector2Int bestCell = targetCell;
        bool found = false;

        float maxSurfaceDist = attackRange + unitRadius;
        float minSurfaceDist;
        if (isRanged)
            minSurfaceDist = attackRange * 0.4f;
        else
            minSurfaceDist = 0f;

        float idealRangedSurfaceDist = attackRange * 0.85f;

        for (int dz = -searchRadius; dz <= searchRadius; dz++)
        {
            for (int dx = -searchRadius; dx <= searchRadius; dx++)
            {
                int idx = (dz + searchRadius) * size + (dx + searchRadius);
                if (cost[idx] >= float.MaxValue) continue;

                Vector2Int worldCell = new Vector2Int(targetCell.x + dx, targetCell.y + dz);
                Vector3 worldPos = grid.CellToWorld(worldCell);
                float surfaceDist = DistToSurface(worldPos, targetCenter, targetExtents);

                if (surfaceDist < minSurfaceDist || surfaceDist > maxSurfaceDist)
                    continue;

                if (clearance != null && !clearance.CanPass(worldCell, unitRadius))
                    continue;

                if (isRanged && !grid.HasLineOfSight(worldCell, targetCell))
                    continue;

                if (IsSlotClaimed(targetId, worldCell, attackerId))
                    continue;

                float travelCost = cost[idx];
                float distToAttacker = Vector3.Distance(worldPos, attackerPos);

                float score;
                if (isRanged)
                {
                    float rangeDelta = Mathf.Abs(surfaceDist - idealRangedSurfaceDist);
                    float rangePreference = rangeDelta / Mathf.Max(attackRange, 1f);
                    score = travelCost * 0.25f + distToAttacker * 0.45f + rangePreference * 0.3f;
                }
                else
                {
                    score = travelCost * 0.3f + distToAttacker * 0.7f;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestCell = worldCell;
                    found = true;
                }
            }
        }

        // Fallback: all optimal slots claimed — find closest walkable cell within
        // range ignoring slot claims.
        if (!found)
        {
            float fallbackBest = float.MaxValue;
            for (int dz = -searchRadius; dz <= searchRadius; dz++)
            {
                for (int dx = -searchRadius; dx <= searchRadius; dx++)
                {
                    int idx = (dz + searchRadius) * size + (dx + searchRadius);
                    if (cost[idx] >= float.MaxValue) continue;

                    Vector2Int worldCell = new Vector2Int(targetCell.x + dx, targetCell.y + dz);
                    Vector3 worldPos = grid.CellToWorld(worldCell);
                    float surfaceDist = DistToSurface(worldPos, targetCenter, targetExtents);

                    if (surfaceDist < minSurfaceDist || surfaceDist > maxSurfaceDist)
                        continue;

                    if (clearance != null && !clearance.CanPass(worldCell, unitRadius))
                        continue;

                    if (isRanged && !grid.HasLineOfSight(worldCell, targetCell))
                        continue;

                    float distToAttacker = Vector3.Distance(worldPos, attackerPos);
                    if (distToAttacker < fallbackBest)
                    {
                        fallbackBest = distToAttacker;
                        bestCell = worldCell;
                        found = true;
                    }
                }
            }
        }

        if (found)
            ClaimSlot(targetId, bestCell, attackerId);

        return (bestCell, found);
    }

    /// <summary>
    /// Compute the effective attack range for a unit. Clamps melee to [0.3, 2]
    /// and ranged to [1, 8].
    /// </summary>
    public static float GetAttackRange(Unit unit)
    {
        Debug.Assert(unit != null, "[AttackPositionFinder] GetAttackRange: unit is null");
        Debug.Assert(unit.Data != null, $"[AttackPositionFinder] {unit.name} GetAttackRange: unit.Data is null");
        return GetAttackRangeFromData(unit.Data.attackRange, unit.Data.isRanged);
    }

    /// <summary>
    /// Pure-logic version of GetAttackRange for unit testing.
    /// </summary>
    public static float GetAttackRangeFromData(float dataRange, bool isRanged)
    {
        if (!isRanged)
            return Mathf.Clamp(dataRange, 0.3f, 2f);
        return Mathf.Clamp(dataRange, 1f, 8f);
    }

    public static int MaxMeleeSlots(float targetRadius, float attackerDiameter)
    {
        if (attackerDiameter <= 0f) return 8;
        return Mathf.Max(1, Mathf.FloorToInt(2f * Mathf.PI * targetRadius / attackerDiameter));
    }

    public static void ClaimSlot(int targetId, Vector2Int cell, int attackerId)
    {
        if (!slotRegistry.TryGetValue(targetId, out var slots))
        {
            slots = new Dictionary<Vector2Int, int>();
            slotRegistry[targetId] = slots;
        }
        slots[cell] = attackerId;
    }

    public static void ReleaseSlot(int targetId, Vector2Int cell, int attackerId)
    {
        if (!slotRegistry.TryGetValue(targetId, out var slots)) return;
        if (slots.TryGetValue(cell, out int owner) && owner == attackerId)
            slots.Remove(cell);
        if (slots.Count == 0)
            slotRegistry.Remove(targetId);
    }

    public static void ReleaseAllSlots(int attackerId)
    {
        var toClean = new List<int>();
        foreach (var kvp in slotRegistry)
        {
            var cellsToRemove = new List<Vector2Int>();
            foreach (var slot in kvp.Value)
            {
                if (slot.Value == attackerId)
                    cellsToRemove.Add(slot.Key);
            }
            foreach (var c in cellsToRemove)
                kvp.Value.Remove(c);
            if (kvp.Value.Count == 0)
                toClean.Add(kvp.Key);
        }
        foreach (var id in toClean)
            slotRegistry.Remove(id);
    }

    public static void ReleaseTargetSlots(int targetId)
    {
        slotRegistry.Remove(targetId);
    }

    /// <summary>
    /// Returns (targetCount, totalSlots, maxSlotsPerTarget) for diagnostics.
    /// </summary>
    public static (int targets, int totalSlots, int maxPerTarget) GetSlotStats()
    {
        int totalSlots = 0;
        int maxPerTarget = 0;
        foreach (var kvp in slotRegistry)
        {
            totalSlots += kvp.Value.Count;
            if (kvp.Value.Count > maxPerTarget)
                maxPerTarget = kvp.Value.Count;
        }
        return (slotRegistry.Count, totalSlots, maxPerTarget);
    }

    private static bool IsSlotClaimed(int targetId, Vector2Int cell, int myAttackerId)
    {
        if (!slotRegistry.TryGetValue(targetId, out var slots)) return false;
        if (!slots.TryGetValue(cell, out int ownerId)) return false;
        return ownerId != myAttackerId;
    }

    /// <summary>
    /// Removes empty target entries and slots held by instance IDs that no longer exist.
    /// </summary>
    public static void CleanupStaleSlots()
    {
        cleanupTimer -= Time.deltaTime;
        if (cleanupTimer > 0f) return;
        cleanupTimer = 5f;

        var livingIds = new HashSet<int>();
        if (UnitManager.Instance != null)
        {
            int teamCount = TeamManager.TeamCount;
            for (int team = 0; team < teamCount; team++)
            {
                var units = UnitManager.Instance.GetTeamUnits(team);
                if (units == null) continue;
                foreach (var u in units)
                {
                    if (u != null && !u.IsDead)
                        livingIds.Add(u.GetInstanceID());
                }
            }
        }

        var emptyTargets = new List<int>();
        foreach (var kvp in slotRegistry)
        {
            var deadSlots = new List<Vector2Int>();
            foreach (var slot in kvp.Value)
            {
                if (!livingIds.Contains(slot.Value))
                    deadSlots.Add(slot.Key);
            }
            foreach (var cell in deadSlots)
                kvp.Value.Remove(cell);

            if (kvp.Value.Count == 0)
                emptyTargets.Add(kvp.Key);
        }
        foreach (var id in emptyTargets)
            slotRegistry.Remove(id);
    }
}
