using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Tests for combat recovery flows using real pure-logic APIs:
/// CombatTargeting, MovementLogic, UnitStateLogic, AttackPositionFinder.
///
/// Validates the SC2-style combat approach, fallback positions,
/// approach stall evaluation, stuck tier behavior during combat,
/// and state transitions around combat entry/exit.
/// </summary>
[TestFixture]
public class CombatRecoveryTests
{
    // ================================================================
    //  TEST 1: SC2-style fallback when all attack slots are claimed
    // ================================================================

    [Test]
    public void FindAttackPosition_AllSlotsClaimed_ReturnsFallback()
    {
        var grid = new FakeGrid(30, 30, 1f);

        Vector3 targetCenter = new Vector3(15f, 0f, 15f);
        float targetRadius = 3f;
        float attackRange = 1.5f;
        float unitRadius = 0.5f;
        Vector3 attackerPos = new Vector3(5f, 0f, 15f);

        int targetId = 999;
        float maxDist = targetRadius + attackRange + unitRadius + grid.CellSize * 2;
        int searchR = Mathf.CeilToInt(maxDist / grid.CellSize) + 1;
        Vector2Int targetCell = grid.WorldToCell(targetCenter);

        int fakeClaimer = 1;
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
            attackerPos, 0, targetId);

        Assert.IsTrue(found, "SC2-style fallback should provide a position even when all optimal slots are claimed");

        fakeClaimer = 1;
        for (int dz = -searchR; dz <= searchR; dz++)
            for (int dx = -searchR; dx <= searchR; dx++)
            {
                Vector2Int cell = new Vector2Int(targetCell.x + dx, targetCell.y + dz);
                if (grid.IsInBounds(cell))
                    AttackPositionFinder.ReleaseSlot(targetId, cell, fakeClaimer++);
            }
    }

    // ================================================================
    //  Combat approach: stuck tier stays at Replan, never marks unreachable
    // ================================================================

    [Test]
    public void CombatApproach_StuckTier3_StaysAtReplan_NeverUnreachable()
    {
        // SC2-style: combat approach units keep retrying via Replan,
        // they are never marked unreachable even at high stall times
        float stallTime = 5f;
        float distToDest = 20f;
        float unitRadius = 0.5f;

        var tier = MovementLogic.EvaluateStuckTier(stallTime, distToDest, unitRadius,
            hasWorldTarget: true, isCombatApproach: true);

        Assert.AreEqual(MovementLogic.StuckTier.Tier2_Replan, tier,
            "Combat approach must never reach Tier3_FarUnreachable — keeps replanning");
    }

    [Test]
    public void NonCombatApproach_StuckTier3_MarkedUnreachable()
    {
        // Contrast: non-combat units DO get marked unreachable
        float stallTime = 5f;
        float distToDest = 20f;
        float unitRadius = 0.5f;

        var tier = MovementLogic.EvaluateStuckTier(stallTime, distToDest, unitRadius,
            hasWorldTarget: true, isCombatApproach: false);

        Assert.AreEqual(MovementLogic.StuckTier.Tier3_FarUnreachable, tier,
            "Non-combat units far from dest should be marked unreachable");
    }

    // ================================================================
    //  Approach stall evaluation: retry vs blacklist vs continue
    // ================================================================

    [Test]
    public void ApproachStall_StructureTarget_RetryFasterThanUnit()
    {
        // Structure targets get shorter retry time (2s vs 3s)
        var actionStruct = CombatTargeting.EvaluateApproachStall(
            stallTimer: 2.5f, stuckRetryCount: 0, isStructureTarget: true);
        var actionUnit = CombatTargeting.EvaluateApproachStall(
            stallTimer: 2.5f, stuckRetryCount: 0, isStructureTarget: false);

        Assert.AreEqual(ApproachAction.RetryApproach, actionStruct,
            "Structure target stall at 2.5s should trigger retry (threshold=2s)");
        Assert.AreEqual(ApproachAction.Continue, actionUnit,
            "Unit target stall at 2.5s should continue (threshold=3s)");
    }

    [Test]
    public void ApproachStall_MaxRetries_BlacklistsTarget()
    {
        var action = CombatTargeting.EvaluateApproachStall(
            stallTimer: 4f, stuckRetryCount: 3, isStructureTarget: false);

        Assert.AreEqual(ApproachAction.BlacklistAndRetreat, action,
            "After 3 retries, should blacklist and retreat");
    }

    [Test]
    public void ApproachStall_UnderThreshold_Continues()
    {
        var action = CombatTargeting.EvaluateApproachStall(
            stallTimer: 1.5f, stuckRetryCount: 0, isStructureTarget: false);

        Assert.AreEqual(ApproachAction.Continue, action,
            "Under retry threshold should continue approaching");
    }

    // ================================================================
    //  Approach progress detection
    // ================================================================

    [Test]
    public void ApproachProgress_Decreasing_IsProgressing()
    {
        bool progressing = CombatTargeting.IsApproachProgressing(
            currentDist: 5f, lastApproachDist: 6f, isStructure: false);
        Assert.IsTrue(progressing,
            "Distance decreased by 1.0 (> 0.3 threshold) — should be progressing");
    }

    [Test]
    public void ApproachProgress_BarelyDecreasing_NotProgressing()
    {
        bool progressing = CombatTargeting.IsApproachProgressing(
            currentDist: 5.8f, lastApproachDist: 6f, isStructure: false);
        Assert.IsFalse(progressing,
            "Distance decreased by 0.2 (< 0.3 threshold) — not enough progress");
    }

    [Test]
    public void ApproachProgress_StructureUsesLargerThreshold()
    {
        // Structure threshold is 0.5, unit threshold is 0.3
        bool progressUnit = CombatTargeting.IsApproachProgressing(
            currentDist: 5.6f, lastApproachDist: 6f, isStructure: false);
        bool progressStruct = CombatTargeting.IsApproachProgressing(
            currentDist: 5.6f, lastApproachDist: 6f, isStructure: true);

        Assert.IsTrue(progressUnit,
            "Unit: 0.4 decrease > 0.3 threshold — progressing");
        Assert.IsFalse(progressStruct,
            "Structure: 0.4 decrease < 0.5 threshold — not progressing");
    }

    // ================================================================
    //  Approach stall increment: only when physically stopped
    // ================================================================

    [Test]
    public void ApproachStall_PhysicallyMoving_DoesNotIncrement()
    {
        bool shouldIncrement = CombatTargeting.ShouldIncrementApproachStall(
            isApproachProgressing: false, isPhysicallyMoving: true);
        Assert.IsFalse(shouldIncrement,
            "Lateral movement toward attack slot should not count as stall");
    }

    [Test]
    public void ApproachStall_StoppedAndNotProgressing_Increments()
    {
        bool shouldIncrement = CombatTargeting.ShouldIncrementApproachStall(
            isApproachProgressing: false, isPhysicallyMoving: false);
        Assert.IsTrue(shouldIncrement,
            "Physically stopped and not progressing — stall should increment");
    }

    [Test]
    public void ApproachStall_Progressing_NeverIncrements()
    {
        bool shouldIncrement = CombatTargeting.ShouldIncrementApproachStall(
            isApproachProgressing: true, isPhysicallyMoving: false);
        Assert.IsFalse(shouldIncrement,
            "Making approach progress — stall should never increment");
    }

    // ================================================================
    //  Blacklist duration scales with distance (SC2-style)
    // ================================================================

    [Test]
    public void BlacklistDuration_NearbyTarget_ShortDuration()
    {
        float effectiveRange = 2f;
        float duration = CombatTargeting.GetBlacklistDuration(3f, effectiveRange);
        Assert.AreEqual(2f, duration,
            "Nearby target (dist < 3x range) should have short blacklist");
    }

    [Test]
    public void BlacklistDuration_FarTarget_LongDuration()
    {
        float effectiveRange = 2f;
        float duration = CombatTargeting.GetBlacklistDuration(15f, effectiveRange);
        Assert.AreEqual(8f, duration,
            "Far target should have full blacklist duration");
    }

    // ================================================================
    //  State transitions around combat entry/exit
    // ================================================================

    [Test]
    public void StateTransition_DyingDuringCombat_OverridesFighting()
    {
        var state = UnitStateLogic.ComputeNextState(
            UnitState.Fighting, isDead: true, isMoving: false, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Dying, state,
            "Death during combat must override Fighting state");
    }

    [Test]
    public void StateTransition_FightingStaysSticky_EvenIfMoving()
    {
        var state = UnitStateLogic.ComputeNextState(
            UnitState.Fighting, isDead: false, isMoving: true, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Fighting, state,
            "Fighting is a sticky state — movement flags don't exit it");
    }

    [Test]
    public void StateTransition_AfterCombat_IdleToMoving()
    {
        // After combat ends, unit transitions from Idle to Moving when given a new destination
        var state = UnitStateLogic.ComputeNextState(
            UnitState.Idle, isDead: false, isMoving: true, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Moving, state,
            "After combat ends and movement resumes, unit should go to Moving");
    }

    // ================================================================
    //  Combat density: density stop bypassed during combat approach
    // ================================================================

    [Test]
    public void DensityStop_CombatApproach_Bypassed()
    {
        bool shouldCheck = MovementLogic.ShouldCheckDensity(isCombatApproach: true);
        Assert.IsFalse(shouldCheck,
            "Combat approach units must bypass density stop to reach their target");
    }

    [Test]
    public void DensityStop_NonCombat_Checked()
    {
        bool shouldCheck = MovementLogic.ShouldCheckDensity(isCombatApproach: false);
        Assert.IsTrue(shouldCheck,
            "Non-combat marching units should respect density stop");
    }

    // ================================================================
    //  Duplicate destination suppression
    // ================================================================

    [Test]
    public void DuplicateDestination_SamePosition_Suppressed()
    {
        Vector3 target = new Vector3(40f, 0f, 20f);
        bool isDup = MovementLogic.IsDuplicateDestination(target, target, 0.5f);
        Assert.IsTrue(isDup,
            "Same position should be detected as duplicate to prevent path thrashing");
    }

    [Test]
    public void DuplicateDestination_NoExistingTarget_NotDuplicate()
    {
        bool isDup = MovementLogic.IsDuplicateDestination(
            new Vector3(40f, 0f, 20f), null, 0.5f);
        Assert.IsFalse(isDup,
            "No existing target — should not suppress");
    }

    [Test]
    public void DuplicateDestination_FarPosition_NotDuplicate()
    {
        bool isDup = MovementLogic.IsDuplicateDestination(
            new Vector3(40f, 0f, 20f), new Vector3(10f, 0f, 5f), 0.5f);
        Assert.IsFalse(isDup,
            "Far position should not be duplicate");
    }

    // ================================================================
    //  Near-destination arrival during combat stall
    // ================================================================

    [Test]
    public void NearDestArrival_CloseAndStalled_ShouldArrive()
    {
        bool arrive = MovementLogic.ShouldArriveNearDest(
            nearDestTimer: 2f, distToGoal: 1f, effectiveRadius: 0.5f);
        Assert.IsTrue(arrive,
            "Near destination with timer exceeded — should force arrival");
    }

    [Test]
    public void NearDestArrival_CloseButTimerNotExpired_ShouldNotArrive()
    {
        bool arrive = MovementLogic.ShouldArriveNearDest(
            nearDestTimer: 0.5f, distToGoal: 1f, effectiveRadius: 0.5f);
        Assert.IsFalse(arrive,
            "Near destination but timer not expired — keep trying");
    }

    [Test]
    public void NearDestArrival_FarAway_ShouldNotArrive()
    {
        bool arrive = MovementLogic.ShouldArriveNearDest(
            nearDestTimer: 10f, distToGoal: 10f, effectiveRadius: 0.5f);
        Assert.IsFalse(arrive,
            "Too far from destination — should not force arrival");
    }

    // ================================================================
    //  In-range tolerance for stuck combat units
    // ================================================================

    [Test]
    public void InRangeWithTolerance_PathDoneAndStuck_GrantsCombat()
    {
        float effectiveRange = 2f;
        float distance = 2.8f; // outside normal range but within tolerance
        bool inRange = CombatTargeting.IsInRangeWithTolerance(
            distance, effectiveRange, wasInRange: false,
            pathDone: true, unitRadius: 0.5f, stuckRetryCount: 2);
        Assert.IsTrue(inRange,
            "Stuck unit with path done should get tolerance to enter combat");
    }

    [Test]
    public void InRangeWithTolerance_PathNotDone_NoTolerance()
    {
        float effectiveRange = 2f;
        float distance = 2.8f;
        bool inRange = CombatTargeting.IsInRangeWithTolerance(
            distance, effectiveRange, wasInRange: false,
            pathDone: false, unitRadius: 0.5f, stuckRetryCount: 2);
        Assert.IsFalse(inRange,
            "Path not done — no tolerance, unit should keep pathing");
    }

    // ================================================================
    //  HELPERS
    // ================================================================

    private class FakeGrid : IGrid
    {
        private readonly bool[,] walkable;

        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public Vector3 GridOrigin { get; }

        public FakeGrid(int w, int h, float cellSize = 1f)
        {
            Width = w;
            Height = h;
            CellSize = cellSize;
            GridOrigin = Vector3.zero;
            walkable = new bool[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    walkable[x, y] = true;
        }

        public bool IsWalkable(Vector2Int cell) =>
            IsInBounds(cell) && walkable[cell.x, cell.y];

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
}
