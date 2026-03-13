using UnityEngine;
using System.Collections.Generic;

public static class AttackPositionFinder
{
    private static readonly Dictionary<int, Dictionary<Vector2Int, int>> slotRegistry = new();
    private static float cleanupTimer;

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

    public static Vector2Int FindAttackPosition(Unit attacker, Health target, GridSystem grid, ClearanceMap clearance)
    {
        if (target == null || grid == null) return Vector2Int.zero;

        Vector3 targetCenter = BoundsHelper.GetCenter(target.gameObject);
        float targetRadius = BoundsHelper.GetRadius(target.gameObject);
        float attackRange = GetAttackRange(attacker);
        float unitRadius = attacker != null ? attacker.EffectiveRadius : 0.5f;
        bool isRanged = attacker != null && attacker.Data != null && attacker.Data.isRanged;

        Vector2Int targetCell = grid.WorldToCell(targetCenter);
        float maxDist = targetRadius + attackRange + unitRadius + grid.CellSize * 2;
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
            // Target center is unwalkable (building/castle) — seed from walkable border cells.
            // Scan around target to find walkable cells adjacent to unwalkable cells.
            int borderSearchR = Mathf.CeilToInt(targetRadius / grid.CellSize) + 2;
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

            if (open.Count == 0 && GameDebug.Combat)
                Debug.LogWarning($"[AttackPos] No walkable border cells found around {target.name} " +
                    $"targetCell={targetCell} radius={targetRadius:F1} borderSearchR={borderSearchR}");
        }

        Vector3 attackerPos = attacker != null ? attacker.transform.position : targetCenter;
        int attackerId = attacker != null ? attacker.GetInstanceID() : 0;
        int targetId = target.GetInstanceID();

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

        float bestScore = float.MaxValue;
        Vector2Int bestCell = targetCell;
        bool found = false;

        float minDist = Mathf.Max(targetRadius - grid.CellSize, 0f);
        float maxDistFromCenter = targetRadius + attackRange + unitRadius;

        var pfm = PathfindingManager.Instance;
        Vector2 attacker2D = new Vector2(attackerPos.x, attackerPos.z);

        for (int dz = -searchRadius; dz <= searchRadius; dz++)
        {
            for (int dx = -searchRadius; dx <= searchRadius; dx++)
            {
                int idx = (dz + searchRadius) * size + (dx + searchRadius);
                if (cost[idx] >= float.MaxValue) continue;

                Vector2Int worldCell = new Vector2Int(targetCell.x + dx, targetCell.y + dz);
                Vector3 worldPos = grid.CellToWorld(worldCell);
                float distToTarget = Vector3.Distance(worldPos, targetCenter);

                if (distToTarget < minDist || distToTarget > maxDistFromCenter)
                    continue;

                if (clearance != null && !clearance.CanPass(worldCell, unitRadius))
                    continue;

                if (isRanged && !grid.HasLineOfSight(worldCell, targetCell))
                    continue;

                if (IsSlotClaimed(targetId, worldCell, attackerId))
                    continue;

                float travelCost = cost[idx];
                float distToAttacker = Vector3.Distance(worldPos, attackerPos);

                // Penalize positions the attacker can't reach in a straight line.
                // Positions behind buildings get a heavy penalty so we prefer positions
                // on the same side of obstacles as the attacker.
                float pathPenalty = 0f;
                if (pfm != null && distToAttacker > grid.CellSize)
                {
                    Vector2 pos2D = new Vector2(worldPos.x, worldPos.z);
                    if (!pfm.IsSegmentClear(attacker2D, pos2D))
                        pathPenalty = maxDistFromCenter * 3f;
                }

                float score = travelCost * 0.3f + distToAttacker * 0.7f + pathPenalty;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestCell = worldCell;
                    found = true;
                }
            }
        }

        if (!found)
        {
            // Fallback: find the closest reachable walkable cell to the target.
            // Prefer cells the attacker can reach without crossing buildings.
            float fallbackBest = float.MaxValue;
            for (int dz = -searchRadius; dz <= searchRadius; dz++)
            {
                for (int dx = -searchRadius; dx <= searchRadius; dx++)
                {
                    int idx = (dz + searchRadius) * size + (dx + searchRadius);
                    if (cost[idx] >= float.MaxValue) continue;

                    Vector2Int worldCell = new Vector2Int(targetCell.x + dx, targetCell.y + dz);
                    Vector3 worldPos = grid.CellToWorld(worldCell);
                    float distToAttacker = Vector3.Distance(worldPos, attackerPos);

                    float penalty = 0f;
                    if (pfm != null && distToAttacker > grid.CellSize)
                    {
                        Vector2 pos2D = new Vector2(worldPos.x, worldPos.z);
                        if (!pfm.IsSegmentClear(attacker2D, pos2D))
                            penalty = 50f;
                    }

                    float score = distToAttacker + penalty;
                    if (score < fallbackBest)
                    {
                        fallbackBest = score;
                        bestCell = worldCell;
                        found = true;
                    }
                }
            }

            if (GameDebug.Combat)
            {
                if (found)
                    Debug.Log($"[AttackPos] FALLBACK for {attacker?.name} -> {target.name} cell={bestCell}");
                else
                    Debug.LogWarning($"[AttackPos] NO POSITION for {attacker?.name} -> {target.name} " +
                        $"searchR={searchRadius} range={attackRange:F1} unitR={unitRadius:F1}");
            }
        }

        if (found)
        {
            ClaimSlot(targetId, bestCell, attackerId);
            if (GameDebug.Combat)
            {
                Vector3 bestPos = grid.CellToWorld(bestCell);
                float distToTarget2 = Vector3.Distance(bestPos, targetCenter);
                float distToAttacker2 = Vector3.Distance(bestPos, attackerPos);
                int claimedOnTarget = slotRegistry.TryGetValue(targetId, out var s) ? s.Count : 0;
                Debug.Log($"[AttackPos] unit={attacker?.name} -> {target.name} " +
                    $"cell={bestCell} score={bestScore:F1} " +
                    $"distToTarget={distToTarget2:F1} distToAttacker={distToAttacker2:F1} " +
                    $"slotsOnTarget={claimedOnTarget} ranged={isRanged}");
            }
        }

        return bestCell;
    }

    private static float GetAttackRange(Unit unit)
    {
        if (unit == null || unit.Data == null) return 1f;
        float dataRange = unit.Data.attackRange;
        if (!unit.Data.isRanged)
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
            for (int team = 0; team <= 1; team++)
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
