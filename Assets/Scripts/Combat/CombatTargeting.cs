using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Pure combat targeting logic extracted from UnitCombat for testability.
/// Operates on data structs, not Unity objects.
/// </summary>
public static class CombatTargeting
{
    public struct TargetCandidate
    {
        public int Id;
        public float DistanceSq;
        public int EngageCount;
        public bool IsStructure;
        public bool IsBlacklisted;
        public bool IsDead;
    }

    public const int MaxEngagersPerUnit = 4;

    /// <summary>
    /// Compute effective attack range from unit data.
    /// </summary>
    public static float GetAttackRange(float dataRange, bool isRanged)
    {
        if (!isRanged)
            return Mathf.Clamp(dataRange, 0.3f, 2f);
        return Mathf.Clamp(dataRange, 1f, 8f);
    }

    /// <summary>
    /// Compute aggro scan range from attack range and unit radius.
    /// </summary>
    public static float GetAggroRange(float attackRange, float unitRadius)
    {
        return Mathf.Clamp(attackRange + 4f + unitRadius, 5f, 12f);
    }

    /// <summary>
    /// Check if a target is in attack range, with hysteresis for disengage.
    /// </summary>
    public static bool IsInRange(float distance, float effectiveRange, bool wasInRange)
    {
        float disengageRange = wasInRange
            ? effectiveRange + effectiveRange * 0.15f
            : effectiveRange;
        return distance <= disengageRange;
    }

    /// <summary>
    /// Extended in-range check with arrival tolerance (when path is done and unit can't get closer).
    /// Tolerance grows with retries but is capped to stay within GetMaxAttackDistance so
    /// TryAttack never rejects a unit that was just granted combat.
    /// </summary>
    public static bool IsInRangeWithTolerance(float distance, float effectiveRange, bool wasInRange,
        bool pathDone, float unitRadius, int stuckRetryCount)
    {
        if (IsInRange(distance, effectiveRange, wasInRange))
            return true;

        if (!pathDone) return false;

        float disengageRange = wasInRange
            ? effectiveRange + effectiveRange * 0.15f
            : effectiveRange;
        float maxTolerance = 0.15f * effectiveRange + 0.5f;
        float arrivalTolerance = Mathf.Min(0.5f + stuckRetryCount * 0.5f, maxTolerance);
        return distance <= disengageRange + arrivalTolerance;
    }

    /// <summary>
    /// Select the best target from a sorted list of candidates.
    /// Prefers targets under the engage cap; falls back to closest.
    /// </summary>
    public static int SelectBestTarget(List<TargetCandidate> candidates)
    {
        if (candidates == null || candidates.Count == 0) return -1;

        foreach (var c in candidates)
        {
            if (c.IsDead || c.IsBlacklisted) continue;
            if (c.EngageCount < MaxEngagersPerUnit)
                return c.Id;
        }

        // Fallback: return first non-dead, non-blacklisted
        foreach (var c in candidates)
        {
            if (!c.IsDead && !c.IsBlacklisted)
                return c.Id;
        }

        return -1;
    }

    /// <summary>
    /// Determine if approach has stalled and what action to take.
    /// </summary>
    public static ApproachAction EvaluateApproachStall(float stallTimer, int stuckRetryCount,
        bool isStructureTarget, float retryTime = -1f)
    {
        if (retryTime < 0f)
            retryTime = isStructureTarget ? 2f : 3f;

        if (stallTimer <= retryTime)
            return ApproachAction.Continue;

        if (stuckRetryCount >= 3) // will be incremented to 4 before check
            return ApproachAction.BlacklistAndRetreat;

        return ApproachAction.RetryApproach;
    }

    /// <summary>
    /// Determine if the unit should switch from a building target to a nearby unit.
    /// </summary>
    public static bool ShouldPrioritySwitchFromBuilding(bool currentTargetIsStructure, bool hasNearbyEnemyUnit)
    {
        return currentTargetIsStructure && hasNearbyEnemyUnit;
    }

    /// <summary>
    /// Calculate max attack distance for TryAttack validation.
    /// Covers the hysteresis disengage range (effectiveRange * 1.15) plus a
    /// small fixed margin. Consistent with IsInRangeWithTolerance so units
    /// that enter combat via tolerance can always land their attacks.
    /// </summary>
    public static float GetMaxAttackDistance(float attackRange, float unitRadius, bool isRanged)
    {
        float effectiveRange = attackRange + unitRadius;
        float maxDist = effectiveRange * 1.15f;
        maxDist += isRanged ? 1.0f : 0.5f;
        return maxDist;
    }

    /// <summary>
    /// Check whether approach is making progress toward target.
    /// </summary>
    public static bool IsApproachProgressing(float currentDist, float lastApproachDist, bool isStructure)
    {
        float threshold = isStructure ? 0.5f : 0.3f;
        return currentDist < lastApproachDist - threshold;
    }

    /// <summary>
    /// SC2-style: approach stall should only count when the unit is physically
    /// stationary. A unit walking laterally to reach an attack slot is making
    /// movement progress even if its distance to the target isn't decreasing.
    /// </summary>
    public static bool ShouldIncrementApproachStall(bool isApproachProgressing, bool isPhysicallyMoving)
    {
        if (isApproachProgressing) return false;
        return !isPhysicallyMoving;
    }

    /// <summary>
    /// SC2-style: blacklist duration scales with distance to target.
    /// Nearby enemies get a very short blacklist; far enemies get the full duration.
    /// Units should never ignore a visible, reachable enemy for long.
    /// </summary>
    public static float GetBlacklistDuration(float distanceToTarget, float effectiveRange)
    {
        if (distanceToTarget < effectiveRange * 3f)
            return 2f;
        return 8f;
    }
}

public enum ApproachAction
{
    Continue,
    RetryApproach,
    BlacklistAndRetreat
}
