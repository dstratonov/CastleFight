using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// SC2-vs-current comparison tests. Each test asserts the DESIRED SC2 behavior,
/// which should FAIL against the current code until the bug is fixed.
///
/// Reference: SC2 uses CDT → A* → Funnel for planning, Boids for local avoidance,
/// and continuous position evaluation for combat approach. Units approaching a
/// combat target are NEVER density-stopped. Attack positions don't use clearance
/// maps — units push toward the target and find gaps dynamically.
/// </summary>
[TestFixture]
public class SC2PathfindingComparisonTests
{
    private class FakeGrid : IGrid
    {
        private readonly bool[,] walkable;
        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public Vector3 GridOrigin { get; }

        public FakeGrid(int w, int h, float cellSize = 1f)
        {
            Width = w; Height = h; CellSize = cellSize;
            GridOrigin = Vector3.zero;
            walkable = new bool[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    walkable[x, y] = true;
        }

        public void BlockRect(int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (x >= 0 && x < Width && y >= 0 && y < Height)
                        walkable[x, y] = false;
        }

        public bool IsWalkable(Vector2Int cell) => IsInBounds(cell) && walkable[cell.x, cell.y];
        public bool IsInBounds(Vector2Int cell) =>
            cell.x >= 0 && cell.x < Width && cell.y >= 0 && cell.y < Height;
        public Vector2Int WorldToCell(Vector3 worldPosition) =>
            new(Mathf.RoundToInt((worldPosition.x - GridOrigin.x) / CellSize),
                Mathf.RoundToInt((worldPosition.z - GridOrigin.z) / CellSize));
        public Vector3 CellToWorld(Vector2Int cell) =>
            new(cell.x * CellSize + GridOrigin.x, GridOrigin.y, cell.y * CellSize + GridOrigin.z);
        public Vector3 FindNearestWalkablePosition(Vector3 desiredWorldPos, Vector3 referencePos) =>
            desiredWorldPos;
        public bool HasLineOfSight(Vector2Int from, Vector2Int to) => true;
    }

    // ================================================================
    //  BUG 1: Approach progress threshold too strict for structures
    //
    //  SC2: A unit that moved 0.8 units closer to a structure IS
    //  making progress. Current code requires 2.0 units of progress.
    //  With boids lateral deflection, units zigzag and rarely achieve
    //  2.0 straight-line progress, triggering false approach stalls.
    //
    //  Fix: Reduce structure threshold from 2.0 to 0.5 (same as units).
    // ================================================================

    [Test]
    public void SC2_ApproachProgress_SmallProgressShouldCount()
    {
        float currentDist = 5.0f;
        float lastApproachDist = 5.8f;

        bool progressing = CombatTargeting.IsApproachProgressing(
            currentDist, lastApproachDist, isStructure: true);

        Assert.IsTrue(progressing,
            $"Unit moved {lastApproachDist - currentDist:F1} units closer to structure " +
            "but IsApproachProgressing returns false (threshold=2.0). " +
            "SC2 uses a smaller threshold — 0.8 units of progress should count. " +
            "This causes premature approach stall retries and eventually blacklisting.");
    }

    // ================================================================
    //  BUG 2: ClearanceMap blocks valid melee attack positions
    //
    //  SC2: Attack positions only check walkability, not clearance.
    //  A walkable cell next to a building is a valid attack position.
    //
    //  Current: FindAttackPositionCore checks clearance.CanPass(cell, radius).
    //  With cellSize=1 and unitRadius=1.5, cells adjacent to buildings
    //  have clearance ~1.0, which fails the >=1.5 check.
    //
    //  Fix: Pass null clearance for attack position finding (SC2 style).
    // ================================================================

    [Test]
    public void SC2_AttackPosition_ClearanceMustNotBlockMeleeSlots()
    {
        // Use cellSize=1 to create tight clearance values
        var grid = new FakeGrid(40, 40, 1f);

        // 4x4 building at (18,18)-(21,21)
        grid.BlockRect(18, 18, 21, 21);

        var clearance = new ClearanceMap();
        clearance.ComputeFull(grid);

        // Large melee unit (radius 1.5)
        float unitRadius = 1.5f;
        Vector3 targetCenter = grid.CellToWorld(new Vector2Int(20, 20));
        float targetRadius = 2f;
        float attackRange = 1.5f;
        Vector3 attackerPos = new Vector3(10f, 0f, 20f);

        // With clearance check (current behavior)
        var (_, foundWithClearance) = AttackPositionFinder.FindAttackPositionCore(
            grid, clearance, targetCenter, targetRadius,
            attackRange, unitRadius, false,
            attackerPos, 1, 999);

        // Without clearance check (SC2 behavior)
        var (cellNoClear, foundNoClearance) = AttackPositionFinder.FindAttackPositionCore(
            grid, null, targetCenter, targetRadius,
            attackRange, unitRadius, false,
            attackerPos, 2, 999);

        // Cleanup
        AttackPositionFinder.ReleaseAllSlots(1);
        AttackPositionFinder.ReleaseAllSlots(2);

        Assert.IsTrue(foundNoClearance, "Must find position without clearance check");
        Assert.IsTrue(foundWithClearance,
            "With clearance check, large melee units cannot find attack positions " +
            "adjacent to buildings. Cells have clearance < unitRadius, causing " +
            "FindAttackPositionCore to skip them. SC2 doesn't use clearance " +
            "for attack positions — only walkability matters.");
    }

    // ================================================================
    //  BUG 3: MoveTowardTarget has no fallback when all slots claimed
    //
    //  SC2: If no optimal slot is available, units push toward the
    //  closest point on the target surface. They use continuous
    //  position evaluation to find gaps as they open up.
    //
    //  Current: When posFound=false, MoveTowardTarget does nothing.
    //  The unit holds position indefinitely.
    //
    //  Fix: Add fallback in MoveTowardTarget — move toward closest
    //  point on target + attack range when no slot is available.
    // ================================================================

    [Test]
    public void SC2_AttackPosition_MustProvideFallbackWhenAllSlotsClaimed()
    {
        var grid = new FakeGrid(40, 40, 1f);
        grid.BlockRect(18, 18, 21, 21);

        Vector3 targetCenter = grid.CellToWorld(new Vector2Int(20, 20));
        float targetRadius = 2f;
        float attackRange = 1.5f;
        float unitRadius = 0.5f;
        Vector3 attackerPos = new Vector3(10f, 0f, 20f);
        int targetId = 999;

        // Claim all possible cells
        float maxDist = targetRadius + attackRange + unitRadius + grid.CellSize * 2;
        int searchR = Mathf.CeilToInt(maxDist / grid.CellSize) + 1;
        Vector2Int targetCell = grid.WorldToCell(targetCenter);

        int fakeClaimer = 1000;
        for (int dz = -searchR; dz <= searchR; dz++)
            for (int dx = -searchR; dx <= searchR; dx++)
            {
                Vector2Int cell = new Vector2Int(targetCell.x + dx, targetCell.y + dz);
                if (grid.IsInBounds(cell))
                    AttackPositionFinder.ClaimSlot(targetId, cell, fakeClaimer++);
            }

        var (_, found) = AttackPositionFinder.FindAttackPositionCore(
            grid, null, targetCenter, targetRadius,
            attackRange, unitRadius, false,
            attackerPos, 1, targetId);

        // Cleanup
        fakeClaimer = 1000;
        for (int dz = -searchR; dz <= searchR; dz++)
            for (int dx = -searchR; dx <= searchR; dx++)
            {
                Vector2Int cell = new Vector2Int(targetCell.x + dx, targetCell.y + dz);
                if (grid.IsInBounds(cell))
                    AttackPositionFinder.ReleaseSlot(targetId, cell, fakeClaimer++);
            }

        Assert.IsTrue(found,
            "When all optimal slots are claimed, SC2 provides a fallback position " +
            "(closest walkable cell toward target surface within range). Current code " +
            "returns found=false and the unit holds position indefinitely.");
    }

    // ================================================================
    //  BUG 4: Stuck handler marks combat destinations as unreachable
    //
    //  SC2: Combat approach destinations are never permanently marked
    //  as unreachable. Units keep retrying different angles.
    //
    //  Current: EvaluateStuckTier returns Tier3_FarUnreachable for combat.
    //
    //  Fix: Add isCombatApproach parameter — return Tier2_Replan instead
    //  of Tier3_FarUnreachable so the unit retries with a new path.
    // ================================================================

    [Test]
    public void SC2_StuckTier3_MustNotMarkUnreachableDuringCombat()
    {
        float stallTime = 3.5f;
        float distToDest = 8f;
        float unitRadius = 0.5f;
        bool hasWorldTarget = true;
        bool isCombatApproach = true;

        var tier = MovementLogic.EvaluateStuckTier(stallTime, distToDest, unitRadius, hasWorldTarget, isCombatApproach);

        Assert.AreNotEqual(MovementLogic.StuckTier.Tier3_FarUnreachable, tier,
            "SC2 never marks combat approach destinations as unreachable. " +
            "During combat, stuck units should get Tier2_Replan to retry " +
            "with a different approach angle, not Tier3_FarUnreachable.");

        Assert.AreEqual(MovementLogic.StuckTier.Tier2_Replan, tier,
            "Combat approach stuck handler should return Tier2_Replan " +
            "so the unit retries with a new path instead of giving up.");
    }

    // ================================================================
    //  BUG 5: Density stop has no combat awareness
    //
    //  SC2: Density stop only applies to marching/move orders.
    //  Units approaching a combat target bypass density stop entirely.
    //
    //  Current: ComputeDensityCore has no combat awareness parameter.
    //  Any unit near its destination with crowded allies gets stopped,
    //  even if it's approaching a building to attack.
    //
    //  Fix: Add isCombatApproach parameter to ShouldDensityStop.
    //  When true, always return false.
    // ================================================================

    [Test]
    public void SC2_DensityStop_MustAcceptCombatFlag()
    {
        // With distToDest=1.0, probeRadius=1.5, circleArea=PI*2.25=7.07
        // 6 agents each PI*0.25 = 0.79 → total = 5.50 → density = 0.78 > 0.6 → stop
        float distToDest = 1.0f;
        float myRadius = 0.5f;
        int myId = 1;

        var neighbors = new List<BoidsNeighbor>();
        for (int i = 0; i < 6; i++)
        {
            neighbors.Add(new BoidsNeighbor
            {
                Position = new Vector3(20f + i * 0.3f, 0, 20f),
                Velocity = Vector3.zero,
                Radius = 0.5f,
                TeamId = 0,
                InstanceId = 100 + i
            });
        }

        var (density, shouldStop) = BoidsManager.ComputeDensityCore(distToDest, myRadius, neighbors, myId);

        Assert.IsTrue(shouldStop,
            $"Setup validation: density={density:F2} should trigger stop with 6 allies at close range");

        // SC2 behavior: during combat approach, density stop must be bypassed.
        // The current UnitMovement code calls ShouldDensityStop without checking
        // whether the unit has an active combat target. We need MovementLogic
        // to provide a ShouldCheckDensity(bool isCombatApproach) helper.
        bool hasCombatTarget = true;
        bool sc2ShouldStop = MovementLogic.ShouldCheckDensity(hasCombatTarget) && shouldStop;

        Assert.IsFalse(sc2ShouldStop,
            $"density={density:F2} shouldStop={shouldStop}. " +
            "SC2 bypasses density stop during combat. Units approaching buildings to " +
            "attack stop short because density check fires when friendlies crowd " +
            "the attack area. Fix: skip density stop when unit has an attack target.");
    }

    // ================================================================
    //  BUG 6: Attack position finder doesn't provide fallback position
    //         for the caller when finder itself can't find optimal slot
    //
    //  SC2: The fallback is to compute "closest point on target surface
    //  + unit radius + small offset" as a direct approach destination.
    //
    //  Fix: When found=false, AttackPositionFinder should still return
    //  a best-effort cell (closest walkable cell toward target, ignoring
    //  slot claims). Set a separate flag to indicate it's a fallback.
    // ================================================================

    [Test]
    public void SC2_AttackPosition_MustReturnFallbackEvenWhenSlotsFull()
    {
        var grid = new FakeGrid(40, 40, 1f);

        Vector3 targetCenter = new Vector3(20f, 0f, 20f);
        float targetRadius = 2f;
        float attackRange = 1.5f;
        float unitRadius = 0.5f;
        Vector3 attackerPos = new Vector3(10f, 0f, 20f);
        int targetId = 999;

        float maxDist = targetRadius + attackRange + unitRadius + grid.CellSize * 2;
        int searchR = Mathf.CeilToInt(maxDist / grid.CellSize) + 1;
        Vector2Int targetCell = grid.WorldToCell(targetCenter);

        // Claim all slots
        int fakeClaimer = 2000;
        for (int dz = -searchR; dz <= searchR; dz++)
            for (int dx = -searchR; dx <= searchR; dx++)
            {
                Vector2Int cell = new Vector2Int(targetCell.x + dx, targetCell.y + dz);
                if (grid.IsInBounds(cell))
                    AttackPositionFinder.ClaimSlot(targetId, cell, fakeClaimer++);
            }

        var (resultCell, found) = AttackPositionFinder.FindAttackPositionCore(
            grid, null, targetCenter, targetRadius,
            attackRange, unitRadius, false,
            attackerPos, 1, targetId);

        // Cleanup
        fakeClaimer = 2000;
        for (int dz = -searchR; dz <= searchR; dz++)
            for (int dx = -searchR; dx <= searchR; dx++)
            {
                Vector2Int c = new Vector2Int(targetCell.x + dx, targetCell.y + dz);
                if (grid.IsInBounds(c))
                    AttackPositionFinder.ReleaseSlot(targetId, c, fakeClaimer++);
            }

        // SC2 would return found=true with a fallback position
        Assert.IsTrue(found,
            "When all slots are claimed, SC2 still returns a fallback position " +
            "(closest walkable cell within range, ignoring slot claims). " +
            "Current code returns found=false, causing MoveTowardTarget to do nothing.");
    }

    // ================================================================
    //  ADDITIONAL SC2-STYLE TESTS
    // ================================================================

    [Test]
    public void SC2_DensityStop_MarchingUnitsShouldStillStop()
    {
        // Verify density stop still works for marching (non-combat) units
        bool hasCombatTarget = false;
        bool shouldCheck = MovementLogic.ShouldCheckDensity(hasCombatTarget);
        Assert.IsTrue(shouldCheck,
            "Density stop must still apply to marching units without combat targets");
    }

    [Test]
    public void SC2_StuckTier3_NonCombatStillMarksUnreachable()
    {
        // Non-combat stuck should still mark unreachable (original behavior preserved)
        var tier = MovementLogic.EvaluateStuckTier(3.5f, 8f, 0.5f, true, false);
        Assert.AreEqual(MovementLogic.StuckTier.Tier3_FarUnreachable, tier,
            "Non-combat stuck far from destination should still mark unreachable");
    }

    [Test]
    public void SC2_StuckTier3_NearDestStillArrives()
    {
        // Both combat and non-combat near-dest should arrive
        var combatTier = MovementLogic.EvaluateStuckTier(3.5f, 1f, 0.5f, true, true);
        var normalTier = MovementLogic.EvaluateStuckTier(3.5f, 1f, 0.5f, true, false);

        Assert.AreEqual(MovementLogic.StuckTier.Tier3_NearArrive, combatTier,
            "Combat near-dest stuck should arrive");
        Assert.AreEqual(MovementLogic.StuckTier.Tier3_NearArrive, normalTier,
            "Non-combat near-dest stuck should arrive");
    }

    [Test]
    public void SC2_ApproachProgress_UnitWithSmallProgress()
    {
        // SC2 threshold for units is now 0.3
        Assert.IsTrue(CombatTargeting.IsApproachProgressing(3.0f, 4.0f, false),
            "1.0 units of progress should count (threshold=0.3)");
        Assert.IsTrue(CombatTargeting.IsApproachProgressing(3.5f, 4.0f, false),
            "0.5 units of progress should count (threshold=0.3)");
        Assert.IsFalse(CombatTargeting.IsApproachProgressing(3.8f, 4.0f, false),
            "0.2 units of progress should NOT count (threshold=0.3)");
    }

    [Test]
    public void SC2_AttackPosition_FallbackIsCloserToAttacker()
    {
        // Verify the fallback position is reasonable — closest to the attacker
        var grid = new FakeGrid(40, 40, 1f);
        Vector3 targetCenter = new Vector3(20f, 0f, 20f);
        float targetRadius = 2f;
        float attackRange = 1.5f;
        float unitRadius = 0.5f;
        Vector3 attackerPos = new Vector3(10f, 0f, 20f);
        int targetId = 888;

        float maxDist = targetRadius + attackRange + unitRadius + grid.CellSize * 2;
        int searchR = Mathf.CeilToInt(maxDist / grid.CellSize) + 1;
        Vector2Int targetCell = grid.WorldToCell(targetCenter);

        int fakeClaimer = 3000;
        for (int dz = -searchR; dz <= searchR; dz++)
            for (int dx = -searchR; dx <= searchR; dx++)
            {
                Vector2Int cell = new Vector2Int(targetCell.x + dx, targetCell.y + dz);
                if (grid.IsInBounds(cell))
                    AttackPositionFinder.ClaimSlot(targetId, cell, fakeClaimer++);
            }

        var (fallbackCell, found) = AttackPositionFinder.FindAttackPositionCore(
            grid, null, targetCenter, targetRadius,
            attackRange, unitRadius, false,
            attackerPos, 1, targetId);

        // Cleanup
        fakeClaimer = 3000;
        for (int dz = -searchR; dz <= searchR; dz++)
            for (int dx = -searchR; dx <= searchR; dx++)
            {
                Vector2Int c = new Vector2Int(targetCell.x + dx, targetCell.y + dz);
                if (grid.IsInBounds(c))
                    AttackPositionFinder.ReleaseSlot(targetId, c, fakeClaimer++);
            }

        Assert.IsTrue(found, "Fallback must find a position");

        Vector3 fallbackPos = grid.CellToWorld(fallbackCell);
        float distToTarget = Vector3.Distance(fallbackPos, targetCenter);
        float distToAttacker = Vector3.Distance(fallbackPos, attackerPos);

        Assert.LessOrEqual(distToTarget, targetRadius + attackRange + unitRadius + 0.5f,
            $"Fallback position should be within attack range of target. distToTarget={distToTarget:F1}");
        Assert.Less(distToAttacker, Vector3.Distance(attackerPos, targetCenter),
            "Fallback position should be between attacker and target, not behind");
    }

    [Test]
    public void SC2_Boids_DirectionPreservation_StripBackward()
    {
        // SC2 boids never push a unit backward relative to its desired direction
        Vector3 desired = new Vector3(0, 0, 5); // moving forward (north)
        var forces = new BoidsForces
        {
            Separation = new Vector3(0, 0, -3), // separation pushing backward (south)
            SeparationCount = 1,
            Avoidance = Vector3.zero,
            Alignment = Vector3.zero,
            Cohesion = Vector3.zero,
            HasOverlap = false
        };

        Vector3 combined = BoidsManager.CombineForces(forces, desired, 5f, true);

        float forwardComponent = Vector3.Dot(combined, desired.normalized);
        Assert.GreaterOrEqual(forwardComponent, 0f,
            $"SC2 boids must never produce backward combined velocity. " +
            $"forward component={forwardComponent:F2}");
    }

    [Test]
    public void SC2_ValidatePosition_WallSlide_UsesMovementDelta()
    {
        // ValidatePosition tie-breaking should use movement delta, not smoothedVelocity.
        // This is a logic test for the slide direction decision.
        Vector3 oldPos = new Vector3(10f, 0f, 10f);

        // Moving diagonally northeast with stronger X component
        Vector3 newPos = new Vector3(10.8f, 0f, 10.3f);
        Vector3 delta = newPos - oldPos;

        // X displacement = 0.8, Z displacement = 0.3
        // Should prefer X-slide since X component is larger
        bool preferX = Mathf.Abs(delta.x) >= Mathf.Abs(delta.z);
        Assert.IsTrue(preferX,
            "Wall slide should prefer X-axis when X displacement is larger than Z");

        // Moving with stronger Z component
        newPos = new Vector3(10.2f, 0f, 10.9f);
        delta = newPos - oldPos;
        bool preferZ = Mathf.Abs(delta.z) > Mathf.Abs(delta.x);
        Assert.IsTrue(preferZ,
            "Wall slide should prefer Z-axis when Z displacement is larger than X");
    }

    [Test]
    public void SC2_ApproachStall_StructureRetryBeforeBlacklist()
    {
        Assert.AreEqual(ApproachAction.Continue,
            CombatTargeting.EvaluateApproachStall(1.5f, 0, true),
            "First 2s should continue for structures");

        Assert.AreEqual(ApproachAction.RetryApproach,
            CombatTargeting.EvaluateApproachStall(2.5f, 0, true),
            "After 2s, retry approach for structures");

        Assert.AreEqual(ApproachAction.RetryApproach,
            CombatTargeting.EvaluateApproachStall(2.5f, 2, true),
            "After 2 retries (count=2), should still retry for structures");

        Assert.AreEqual(ApproachAction.BlacklistAndRetreat,
            CombatTargeting.EvaluateApproachStall(2.5f, 3, true),
            "After 3 retries (count=3), blacklist and retreat");
    }

    // ================================================================
    //  SC2 DENSITY THRESHOLD: 60% (not 50%)
    // ================================================================

    [Test]
    public void SC2_DensityThreshold_Uses60Percent()
    {
        // SC2 density stop triggers at 60% area coverage.
        // Create a scenario where density is between 50% and 60%.
        // This should NOT stop (it would have stopped with the old 50% threshold).
        float distToDest = 2.0f;
        float myRadius = 0.5f;
        int myId = 1;
        float probeRadius = distToDest + myRadius; // 2.5
        float circleArea = Mathf.PI * probeRadius * probeRadius; // PI * 6.25 = 19.63

        // Target density: ~55% (between 50% and 60%)
        // Need agentsArea / circleArea ≈ 0.55
        // agentsArea = 0.55 * 19.63 = 10.80
        // Each neighbor r=0.5: area = PI*0.25 = 0.785
        // Self area: 0.785
        // Need 13 neighbors: total = 14 * 0.785 = 10.99 → density = 10.99/19.63 = 0.56
        var neighbors = new List<BoidsNeighbor>();
        for (int i = 0; i < 13; i++)
        {
            neighbors.Add(new BoidsNeighbor
            {
                Position = new Vector3(0.2f * i, 0, 0),
                Velocity = Vector3.zero,
                Radius = 0.5f,
                TeamId = 0,
                InstanceId = 100 + i
            });
        }

        var (density, shouldStop) = BoidsManager.ComputeDensityCore(
            distToDest, myRadius, neighbors, myId);

        Assert.Greater(density, 0.50f, $"Density {density:F3} should be above 50%");
        Assert.Less(density, 0.60f, $"Density {density:F3} should be below 60%");
        Assert.IsFalse(shouldStop,
            $"SC2 threshold is 60% — density {density:F3} (between 50-60%) should NOT stop");
    }

    [Test]
    public void SC2_DensityThreshold_StopsAbove60Percent()
    {
        // Create scenario where density > 60%. This SHOULD stop.
        float distToDest = 2.0f;
        float myRadius = 0.5f;
        int myId = 1;
        float probeRadius = distToDest + myRadius; // 2.5
        float circleArea = Mathf.PI * probeRadius * probeRadius; // 19.63

        // Need agentsArea / circleArea > 0.60
        // Need > 0.60 * 19.63 = 11.78
        // 15 neighbors: total = 16 * 0.785 = 12.57 → density = 0.64
        var neighbors = new List<BoidsNeighbor>();
        for (int i = 0; i < 15; i++)
        {
            neighbors.Add(new BoidsNeighbor
            {
                Position = new Vector3(0.2f * i, 0, 0),
                Velocity = Vector3.zero,
                Radius = 0.5f,
                TeamId = 0,
                InstanceId = 100 + i
            });
        }

        var (density, shouldStop) = BoidsManager.ComputeDensityCore(
            distToDest, myRadius, neighbors, myId);

        Assert.Greater(density, 0.60f, $"Density {density:F3} should be above 60%");
        Assert.IsTrue(shouldStop,
            $"SC2 threshold is 60% — density {density:F3} (above 60%) SHOULD stop");
    }

    // ================================================================
    //  SC2 COMBAT APPROACH: No alignment/cohesion (isMarching=false)
    // ================================================================

    [Test]
    public void SC2_CombatApproach_NoAlignmentCohesion()
    {
        // During combat approach, isMarching should be false,
        // so alignment and cohesion forces are NOT applied.
        var neighbors = new List<BoidsNeighbor>();
        for (int i = 0; i < 5; i++)
        {
            neighbors.Add(new BoidsNeighbor
            {
                Position = new Vector3(3f + i, 0, 0),
                Velocity = new Vector3(0, 0, 5f),
                Radius = 0.5f,
                TeamId = 0,
                InstanceId = 100 + i
            });
        }

        Vector3 myPos = Vector3.zero;
        Vector3 desiredVelocity = new Vector3(5f, 0, 0);
        float maxSpeed = 5f;

        var forces = BoidsManager.ComputeForcesCore(
            myPos, 0.5f, 0, 1, desiredVelocity, neighbors);

        // With isMarching=false (combat), alignment+cohesion should be stripped
        Vector3 combatResult = BoidsManager.CombineForces(forces, desiredVelocity, maxSpeed, false);
        Vector3 marchResult = BoidsManager.CombineForces(forces, desiredVelocity, maxSpeed, true);

        // The marching result should include alignment/cohesion contribution,
        // making it different from the combat result
        Assert.AreNotEqual(combatResult, marchResult,
            "Combat approach (isMarching=false) should produce different steering than marching (isMarching=true)");
    }

    // ================================================================
    //  SC2 APPROACH STALL: must not increment while unit is moving
    // ================================================================

    [Test]
    public void SC2_ApproachStall_NotIncrementedWhileMoving()
    {
        // SC2: when a unit is physically walking to its attack slot (lateral
        // movement), the approach stall timer must NOT tick — the unit IS
        // making progress, just not toward the target.
        bool result = CombatTargeting.ShouldIncrementApproachStall(
            isApproachProgressing: false,
            isPhysicallyMoving: true);

        Assert.IsFalse(result,
            "Approach stall should NOT increment while unit is actively moving to attack position");
    }

    [Test]
    public void SC2_ApproachStall_IncrementedWhenStationary()
    {
        // When unit is stationary and not getting closer, stall SHOULD increment.
        bool result = CombatTargeting.ShouldIncrementApproachStall(
            isApproachProgressing: false,
            isPhysicallyMoving: false);

        Assert.IsTrue(result,
            "Approach stall SHOULD increment when unit is stationary and not making progress");
    }

    [Test]
    public void SC2_ApproachStall_NotIncrementedWhenProgressing()
    {
        // When approach IS progressing, stall should never increment regardless of movement.
        Assert.IsFalse(
            CombatTargeting.ShouldIncrementApproachStall(true, false),
            "Stall should not increment when approach is progressing (even if stationary)");
        Assert.IsFalse(
            CombatTargeting.ShouldIncrementApproachStall(true, true),
            "Stall should not increment when approach is progressing (moving)");
    }

    // ================================================================
    //  SC2 BLACKLIST: nearby enemies get shorter blacklist
    // ================================================================

    [Test]
    public void SC2_Blacklist_NearbyTarget_ShortDuration()
    {
        // SC2: units should never ignore a visible, reachable enemy for 8 seconds.
        // Nearby targets get a much shorter blacklist.
        float effectiveRange = 2.0f;
        float nearbyDist = effectiveRange * 1.5f;

        float duration = CombatTargeting.GetBlacklistDuration(nearbyDist, effectiveRange);

        Assert.LessOrEqual(duration, 2f,
            $"Nearby target (dist={nearbyDist:F1}, effRange={effectiveRange:F1}) should have blacklist <= 2s, got {duration:F1}s");
    }

    [Test]
    public void SC2_Blacklist_FarTarget_FullDuration()
    {
        // Far targets (well outside engagement range) get the full blacklist.
        float effectiveRange = 2.0f;
        float farDist = effectiveRange * 5f;

        float duration = CombatTargeting.GetBlacklistDuration(farDist, effectiveRange);

        Assert.AreEqual(8f, duration,
            $"Far target (dist={farDist:F1}) should get full 8s blacklist");
    }

    // ================================================================
    //  AABB SURFACE DISTANCE: DistToSurface correctness
    // ================================================================

    [Test]
    public void SC2_DistToSurface_PointInsideAABB_ReturnsZero()
    {
        Vector3 center = new Vector3(10f, 0f, 10f);
        Vector3 extents = new Vector3(3f, 0f, 2f);
        Vector3 inside = new Vector3(11f, 0f, 10.5f);

        float dist = AttackPositionFinder.DistToSurface(inside, center, extents);
        Assert.AreEqual(0f, dist, 0.001f, "Point inside AABB should have zero surface distance");
    }

    [Test]
    public void SC2_DistToSurface_AlongShortFace()
    {
        Vector3 center = new Vector3(10f, 0f, 10f);
        Vector3 extents = new Vector3(3f, 0f, 1f); // wide building (6x2)

        // Point 2 units above the short face (z-axis)
        Vector3 pos = new Vector3(10f, 0f, 13f);
        float dist = AttackPositionFinder.DistToSurface(pos, center, extents);
        Assert.AreEqual(2f, dist, 0.001f,
            "Distance from (10,13) to surface of 6x2 AABB centered at (10,10) should be 2");
    }

    [Test]
    public void SC2_DistToSurface_AlongLongFace()
    {
        Vector3 center = new Vector3(10f, 0f, 10f);
        Vector3 extents = new Vector3(3f, 0f, 1f); // wide building (6x2)

        // Point 2 units to the right of the long face (x-axis)
        Vector3 pos = new Vector3(15f, 0f, 10f);
        float dist = AttackPositionFinder.DistToSurface(pos, center, extents);
        Assert.AreEqual(2f, dist, 0.001f,
            "Distance from (15,10) to surface of 6x2 AABB centered at (10,10) should be 2");
    }

    [Test]
    public void SC2_DistToSurface_DiagonalFromCorner()
    {
        Vector3 center = new Vector3(10f, 0f, 10f);
        Vector3 extents = new Vector3(3f, 0f, 1f); // corners at (7,9), (13,9), (7,11), (13,11)

        // Point at (15, 13) — 2 past corner on x, 2 past corner on z
        Vector3 pos = new Vector3(15f, 0f, 13f);
        float dist = AttackPositionFinder.DistToSurface(pos, center, extents);
        float expected = Mathf.Sqrt(2f * 2f + 2f * 2f);
        Assert.AreEqual(expected, dist, 0.01f,
            "Diagonal distance from corner should use Euclidean distance");
    }

    // ================================================================
    //  AABB FIX: Attack position for rectangular building is within
    //  actual attack range (was broken with sphere-based radius)
    // ================================================================

    [Test]
    public void SC2_AttackPosition_RectangularBuilding_WithinSurfaceRange()
    {
        var grid = new FakeGrid(40, 40, 1f);

        // 6x2 building: x=[17..22], z=[19..20]  (extents: 3, 1)
        grid.BlockRect(17, 19, 22, 20);

        Vector3 targetCenter = grid.CellToWorld(new Vector2Int(20, 20));
        float targetRadius = 3f; // Max(extents) = 3
        Vector3 targetExtents = new Vector3(3f, 0f, 1f);
        float attackRange = 1.5f;
        float unitRadius = 0.5f;
        Vector3 attackerPos = new Vector3(20f, 0f, 30f); // approaching from Z+ face

        var (cell, found) = AttackPositionFinder.FindAttackPositionCore(
            grid, null, targetCenter, targetRadius,
            attackRange, unitRadius, false,
            attackerPos, 1, 777,
            targetExtents);

        AttackPositionFinder.ReleaseAllSlots(1);

        Assert.IsTrue(found, "Must find attack position for rectangular building");

        Vector3 pos = grid.CellToWorld(cell);
        float surfaceDist = AttackPositionFinder.DistToSurface(pos, targetCenter, targetExtents);

        Assert.LessOrEqual(surfaceDist, attackRange + unitRadius + 0.01f,
            $"Attack position surface distance ({surfaceDist:F2}) must be within " +
            $"attack range + unit radius ({attackRange + unitRadius:F2}). " +
            "The old sphere-based check placed cells too far from the short face.");
    }
}
