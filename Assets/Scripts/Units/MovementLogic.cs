using UnityEngine;

/// <summary>
/// Pure movement logic extracted from UnitMovement for testability.
/// No MonoBehaviour, no Transform, no network dependencies.
/// </summary>
public static class MovementLogic
{
    public const float StuckThresholdTime = 1.5f;
    public const float NearDestArriveTime = 1.5f;


    public enum StuckTier
    {
        None,
        Tier1_YieldRequest,
        Tier2_Replan,
        Tier3_NearArrive,
        Tier3_FarUnreachable
    }

    /// <summary>
    /// Evaluate stuck tier based on stall time, distance to destination, and unit radius.
    /// SC2-style: combat approach destinations are never marked unreachable.
    /// Units keep retrying with different approach angles via Tier2_Replan.
    /// </summary>
    public static StuckTier EvaluateStuckTier(float stallTime, float distToDest, float unitRadius, bool hasWorldTarget, bool isCombatApproach = false)
    {
        if (stallTime <= 1f) return StuckTier.None;
        if (stallTime <= 2f) return StuckTier.Tier1_YieldRequest;
        if (stallTime <= 3f) return StuckTier.Tier2_Replan;

        if (!hasWorldTarget) return StuckTier.None;

        float nearThreshold = unitRadius * 3f;
        if (nearThreshold < 3f) nearThreshold = 3f;

        if (distToDest < nearThreshold)
            return StuckTier.Tier3_NearArrive;

        if (isCombatApproach)
            return StuckTier.Tier2_Replan;

        return StuckTier.Tier3_FarUnreachable;
    }

    /// <summary>
    /// Frame-rate independent stall detection.
    /// Computes instantaneous speed and compares against a fixed threshold.
    /// </summary>
    public static bool IsStalling(float movedDistance, float deltaTime)
    {
        if (deltaTime < 1e-6f) return false;
        float speed = movedDistance / deltaTime;
        return speed < 0.1f;
    }

    /// <summary>
    /// Update stall time: increase when stalling, decay when moving.
    /// </summary>
    public static float UpdateStallTime(float currentStallTime, bool isStalling, float deltaTime)
    {
        if (isStalling)
            return currentStallTime + deltaTime;
        return Mathf.Max(0f, currentStallTime - deltaTime * 0.5f);
    }

    /// <summary>
    /// Check if near-destination timer should trigger arrival.
    /// </summary>
    public static bool ShouldArriveNearDest(float nearDestTimer, float distToGoal, float effectiveRadius)
    {
        float nearThreshold = Mathf.Max(1.5f, effectiveRadius * 1.5f);
        if (distToGoal >= nearThreshold) return false;
        return nearDestTimer > NearDestArriveTime;
    }

    /// <summary>
    /// Check if unit should advance to next waypoint.
    /// </summary>
    public static bool ShouldAdvanceWaypoint(Vector3 currentPos, Vector3 waypointPos, float effectiveRadius, float baseThreshold)
    {
        float threshold = Mathf.Max(baseThreshold, effectiveRadius * 1.3f);
        return Vector3.Distance(currentPos, waypointPos) < threshold;
    }

    /// <summary>
    /// Check if unit has arrived at final destination.
    /// </summary>
    public static bool HasArrivedAtDestination(Vector3 currentPos, Vector3 destination, float effectiveRadius, float baseThreshold)
    {
        float arrivalDist = Mathf.Max(baseThreshold, effectiveRadius * 1.3f);
        return Vector3.Distance(currentPos, destination) < arrivalDist;
    }


    /// <summary>
    /// Compute castle approach spread offset using deterministic hash.
    /// </summary>
    public static Vector3 ComputeCastleSpreadOffset(int unitInstanceId, float spreadRadius)
    {
        uint hash = (uint)Mathf.Abs(unitInstanceId) * 2654435761u;
        float angle = ((hash & 0xFFFF) / (float)0xFFFF) * Mathf.PI * 2f;
        return new Vector3(Mathf.Cos(angle) * spreadRadius, 0f, Mathf.Sin(angle) * spreadRadius);
    }

    /// <summary>
    /// Decide if duplicate destination should be suppressed.
    /// </summary>
    public static bool IsDuplicateDestination(Vector3 newTarget, Vector3? existingTarget, float threshold)
    {
        if (!existingTarget.HasValue) return false;
        return Vector3.Distance(newTarget, existingTarget.Value) < threshold;
    }
}
