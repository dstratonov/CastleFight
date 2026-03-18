using UnityEngine;

/// <summary>
/// Pure movement logic extracted from UnitMovement for testability.
/// No MonoBehaviour, no Transform, no network dependencies.
/// </summary>
public static class MovementLogic
{
    public const float StuckThresholdTime = 1.5f;
    public const float NearDestArriveTime = 1.5f;
    public const float YieldDuration = 0.4f;

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
    /// Compute yield direction: perpendicular to requester's movement, on the side where the yielder is.
    /// </summary>
    public static Vector3 ComputeYieldDirection(Vector3 yielderPos, Vector3 requesterPos, Vector3 requesterDir)
    {
        Vector3 toMe = yielderPos - requesterPos;
        toMe.y = 0;
        if (toMe.sqrMagnitude < 0.01f) return Vector3.zero;

        Vector3 perp = Vector3.Cross(Vector3.up, requesterDir).normalized;
        float dot = Vector3.Dot(toMe.normalized, perp);
        return dot >= 0 ? perp : -perp;
    }

    /// <summary>
    /// Exponential smooth damp between two velocities.
    /// </summary>
    public static Vector3 SmoothDamp(Vector3 current, Vector3 target, float rate, float deltaTime)
    {
        float t = 1f - Mathf.Exp(-rate * deltaTime);
        return Vector3.Lerp(current, target, t);
    }

    /// <summary>
    /// Strip the backward component of smoothedVelocity relative to desiredVelocity.
    /// Prevents units from physically moving/facing backwards during direction changes
    /// caused by exponential smoothing lag. Preserves the lateral (perpendicular)
    /// component so side-steering from Boids still works.
    /// Same principle as BoidsManager.CombineForces direction preservation.
    /// </summary>
    public static Vector3 PreventBackwardVelocity(Vector3 smoothedVelocity, Vector3 desiredVelocity)
    {
        if (desiredVelocity.sqrMagnitude < 0.01f)
            return smoothedVelocity;

        Vector3 desiredDir = desiredVelocity.normalized;
        float forwardComponent = Vector3.Dot(smoothedVelocity, desiredDir);
        if (forwardComponent < 0f)
            smoothedVelocity -= desiredDir * forwardComponent;

        return smoothedVelocity;
    }

    /// <summary>
    /// Determine the best direction vector for unit rotation.
    /// Prefers actual movement delta (what the unit REALLY did this frame)
    /// over smoothedVelocity (what it INTENDED to do). Falls back to
    /// smoothedVelocity only when displacement is negligible (unit barely moved).
    /// Fixes units facing into walls while sliding along them.
    /// </summary>
    public static Vector3 GetRotationDirection(Vector3 moveDelta, Vector3 smoothedVelocity)
    {
        Vector3 delta = new Vector3(moveDelta.x, 0f, moveDelta.z);
        if (delta.sqrMagnitude > 0.0001f)
            return delta;

        Vector3 vel = new Vector3(smoothedVelocity.x, 0f, smoothedVelocity.z);
        if (vel.sqrMagnitude > 0.01f)
            return vel;

        return Vector3.zero;
    }

    /// <summary>
    /// SC2-style: density stop only applies to marching/move orders.
    /// Units approaching a combat target must never be density-stopped —
    /// they need to push through allies to reach their attack position.
    /// </summary>
    public static bool ShouldCheckDensity(bool isCombatApproach)
    {
        return !isCombatApproach;
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
