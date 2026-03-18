using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class MovementLogicTests
{
    // ================================================================
    //  EvaluateStuckTier
    // ================================================================

    [Test]
    public void EvaluateStuckTier_LowStallTime_None()
    {
        Assert.AreEqual(MovementLogic.StuckTier.None,
            MovementLogic.EvaluateStuckTier(0.5f, 10f, 1f, true));
    }

    [Test]
    public void EvaluateStuckTier_AtBoundary1s_None()
    {
        Assert.AreEqual(MovementLogic.StuckTier.None,
            MovementLogic.EvaluateStuckTier(1f, 10f, 1f, true));
    }

    [Test]
    public void EvaluateStuckTier_Tier1_YieldRequest()
    {
        Assert.AreEqual(MovementLogic.StuckTier.Tier1_YieldRequest,
            MovementLogic.EvaluateStuckTier(1.5f, 10f, 1f, true));
    }

    [Test]
    public void EvaluateStuckTier_AtBoundary2s_Tier1()
    {
        Assert.AreEqual(MovementLogic.StuckTier.Tier1_YieldRequest,
            MovementLogic.EvaluateStuckTier(2f, 10f, 1f, true));
    }

    [Test]
    public void EvaluateStuckTier_Tier2_Replan()
    {
        Assert.AreEqual(MovementLogic.StuckTier.Tier2_Replan,
            MovementLogic.EvaluateStuckTier(2.5f, 10f, 1f, true));
    }

    [Test]
    public void EvaluateStuckTier_AtBoundary3s_Tier2()
    {
        Assert.AreEqual(MovementLogic.StuckTier.Tier2_Replan,
            MovementLogic.EvaluateStuckTier(3f, 10f, 1f, true));
    }

    [Test]
    public void EvaluateStuckTier_NoWorldTarget_Above3s_None()
    {
        Assert.AreEqual(MovementLogic.StuckTier.None,
            MovementLogic.EvaluateStuckTier(5f, 10f, 1f, false));
    }

    [Test]
    public void EvaluateStuckTier_Tier3_NearArrive()
    {
        Assert.AreEqual(MovementLogic.StuckTier.Tier3_NearArrive,
            MovementLogic.EvaluateStuckTier(4f, 2f, 1f, true));
    }

    [Test]
    public void EvaluateStuckTier_Tier3_FarUnreachable()
    {
        Assert.AreEqual(MovementLogic.StuckTier.Tier3_FarUnreachable,
            MovementLogic.EvaluateStuckTier(4f, 20f, 1f, true));
    }

    [Test]
    public void EvaluateStuckTier_NearThreshold_MinClamp()
    {
        // unitRadius=0.5 -> nearThreshold = max(0.5*3, 3) = 3
        Assert.AreEqual(MovementLogic.StuckTier.Tier3_NearArrive,
            MovementLogic.EvaluateStuckTier(4f, 2.9f, 0.5f, true));
        Assert.AreEqual(MovementLogic.StuckTier.Tier3_FarUnreachable,
            MovementLogic.EvaluateStuckTier(4f, 3.1f, 0.5f, true));
    }

    [Test]
    public void EvaluateStuckTier_LargeRadius_ExpandsNearThreshold()
    {
        // unitRadius=2 -> nearThreshold = max(2*3, 3) = 6
        Assert.AreEqual(MovementLogic.StuckTier.Tier3_NearArrive,
            MovementLogic.EvaluateStuckTier(4f, 5f, 2f, true));
        Assert.AreEqual(MovementLogic.StuckTier.Tier3_FarUnreachable,
            MovementLogic.EvaluateStuckTier(4f, 7f, 2f, true));
    }

    // ================================================================
    //  IsStalling
    // ================================================================

    [Test]
    public void IsStalling_ZeroMovement_ReturnsTrue()
    {
        Assert.IsTrue(MovementLogic.IsStalling(0f, 0.016f));
    }

    [Test]
    public void IsStalling_BelowThreshold_ReturnsTrue()
    {
        // speed = 0.001 / 0.016 = 0.0625 < 0.1 → stalling
        Assert.IsTrue(MovementLogic.IsStalling(0.001f, 0.016f));
    }

    [Test]
    public void IsStalling_AboveThreshold_ReturnsFalse()
    {
        // speed = 0.01 / 0.016 = 0.625 >= 0.1 → not stalling
        Assert.IsFalse(MovementLogic.IsStalling(0.01f, 0.016f));
    }

    // ================================================================
    //  UpdateStallTime
    // ================================================================

    [Test]
    public void UpdateStallTime_Stalling_Increases()
    {
        float result = MovementLogic.UpdateStallTime(1f, true, 0.5f);
        Assert.AreEqual(1.5f, result, 0.001f);
    }

    [Test]
    public void UpdateStallTime_Moving_Decays()
    {
        float result = MovementLogic.UpdateStallTime(2f, false, 1f);
        Assert.AreEqual(1.5f, result, 0.001f);
    }

    [Test]
    public void UpdateStallTime_Decay_NeverBelowZero()
    {
        float result = MovementLogic.UpdateStallTime(0.1f, false, 10f);
        Assert.AreEqual(0f, result, 0.001f);
    }

    // ================================================================
    //  ShouldArriveNearDest
    // ================================================================

    [Test]
    public void ShouldArriveNearDest_CloseAndTimerExceeded_ReturnsTrue()
    {
        Assert.IsTrue(MovementLogic.ShouldArriveNearDest(2f, 1f, 1f));
    }

    [Test]
    public void ShouldArriveNearDest_FarAway_ReturnsFalse()
    {
        Assert.IsFalse(MovementLogic.ShouldArriveNearDest(5f, 10f, 1f));
    }

    [Test]
    public void ShouldArriveNearDest_TimerNotExceeded_ReturnsFalse()
    {
        Assert.IsFalse(MovementLogic.ShouldArriveNearDest(0.5f, 1f, 1f));
    }

    [Test]
    public void ShouldArriveNearDest_LargeRadius_ExpandsThreshold()
    {
        // effectiveRadius=3 -> nearThreshold = max(1.5, 3*1.5) = 4.5
        Assert.IsTrue(MovementLogic.ShouldArriveNearDest(2f, 4f, 3f));
        Assert.IsFalse(MovementLogic.ShouldArriveNearDest(2f, 5f, 3f));
    }

    [Test]
    public void ShouldArriveNearDest_AtExactThresholdDist_ReturnsFalse()
    {
        // effectiveRadius=1 -> nearThreshold = max(1.5, 1.5) = 1.5
        Assert.IsFalse(MovementLogic.ShouldArriveNearDest(2f, 1.5f, 1f));
    }

    // ================================================================
    //  ShouldAdvanceWaypoint
    // ================================================================

    [Test]
    public void ShouldAdvanceWaypoint_Close_ReturnsTrue()
    {
        Vector3 pos = Vector3.zero;
        Vector3 wp = new Vector3(0.3f, 0, 0);
        Assert.IsTrue(MovementLogic.ShouldAdvanceWaypoint(pos, wp, 0.5f, 0.5f));
    }

    [Test]
    public void ShouldAdvanceWaypoint_Far_ReturnsFalse()
    {
        Vector3 pos = Vector3.zero;
        Vector3 wp = new Vector3(5f, 0, 0);
        Assert.IsFalse(MovementLogic.ShouldAdvanceWaypoint(pos, wp, 0.5f, 0.5f));
    }

    [Test]
    public void ShouldAdvanceWaypoint_LargeRadius_ExpandsThreshold()
    {
        Vector3 pos = Vector3.zero;
        Vector3 wp = new Vector3(2.5f, 0, 0);
        // effectiveRadius=2 -> threshold = max(0.5, 2*1.3) = 2.6
        Assert.IsTrue(MovementLogic.ShouldAdvanceWaypoint(pos, wp, 2f, 0.5f));
    }

    // ================================================================
    //  HasArrivedAtDestination
    // ================================================================

    [Test]
    public void HasArrivedAtDestination_AtDest_ReturnsTrue()
    {
        Assert.IsTrue(MovementLogic.HasArrivedAtDestination(Vector3.zero, new Vector3(0.1f, 0, 0), 0.5f, 0.5f));
    }

    [Test]
    public void HasArrivedAtDestination_Far_ReturnsFalse()
    {
        Assert.IsFalse(MovementLogic.HasArrivedAtDestination(Vector3.zero, new Vector3(5f, 0, 0), 0.5f, 0.5f));
    }

    [Test]
    public void HasArrivedAtDestination_LargeRadius_ExpandsArrivalDist()
    {
        // effectiveRadius=3 -> arrivalDist = max(0.5, 3*1.3) = 3.9
        Assert.IsTrue(MovementLogic.HasArrivedAtDestination(Vector3.zero, new Vector3(3.5f, 0, 0), 3f, 0.5f));
    }

    // ================================================================
    //  ComputeYieldDirection
    // ================================================================

    [Test]
    public void ComputeYieldDirection_YielderOnRight_ReturnsRightPerp()
    {
        Vector3 yielder = new Vector3(1, 0, 1);
        Vector3 requester = Vector3.zero;
        Vector3 requesterDir = Vector3.forward;

        Vector3 result = MovementLogic.ComputeYieldDirection(yielder, requester, requesterDir);
        // Cross(up, forward) = right (+x)
        Assert.Greater(result.x, 0f, "Should yield to the right");
        Assert.AreEqual(0f, result.y, 0.001f);
    }

    [Test]
    public void ComputeYieldDirection_YielderOnLeft_ReturnsLeftPerp()
    {
        Vector3 yielder = new Vector3(-1, 0, 1);
        Vector3 requester = Vector3.zero;
        Vector3 requesterDir = Vector3.forward;

        Vector3 result = MovementLogic.ComputeYieldDirection(yielder, requester, requesterDir);
        Assert.Less(result.x, 0f, "Should yield to the left");
    }

    [Test]
    public void ComputeYieldDirection_SamePosition_ReturnsZero()
    {
        Vector3 result = MovementLogic.ComputeYieldDirection(Vector3.zero, Vector3.zero, Vector3.forward);
        Assert.AreEqual(Vector3.zero, result);
    }

    [Test]
    public void ComputeYieldDirection_ResultIsNormalized()
    {
        Vector3 yielder = new Vector3(3, 0, 5);
        Vector3 result = MovementLogic.ComputeYieldDirection(yielder, Vector3.zero, Vector3.forward);
        if (result.sqrMagnitude > 0.01f)
            Assert.AreEqual(1f, result.magnitude, 0.01f);
    }

    // ================================================================
    //  SmoothDamp
    // ================================================================

    [Test]
    public void SmoothDamp_ConvergesTowardTarget()
    {
        Vector3 current = Vector3.zero;
        Vector3 target = Vector3.right * 10f;
        Vector3 result = MovementLogic.SmoothDamp(current, target, 5f, 0.016f);

        Assert.Greater(result.x, 0f, "Should move toward target");
        Assert.Less(result.x, 10f, "Should not overshoot");
    }

    [Test]
    public void SmoothDamp_HighRate_ConvergesFaster()
    {
        Vector3 current = Vector3.zero;
        Vector3 target = Vector3.right;
        Vector3 slow = MovementLogic.SmoothDamp(current, target, 1f, 0.1f);
        Vector3 fast = MovementLogic.SmoothDamp(current, target, 20f, 0.1f);
        Assert.Greater(fast.x, slow.x);
    }

    // ================================================================
    //  ComputeCastleSpreadOffset
    // ================================================================

    [Test]
    public void ComputeCastleSpreadOffset_Deterministic()
    {
        Vector3 a = MovementLogic.ComputeCastleSpreadOffset(42, 3f);
        Vector3 b = MovementLogic.ComputeCastleSpreadOffset(42, 3f);
        Assert.AreEqual(a.x, b.x, 0.0001f);
        Assert.AreEqual(a.z, b.z, 0.0001f);
    }

    [Test]
    public void ComputeCastleSpreadOffset_DifferentIds_DifferentOffsets()
    {
        Vector3 a = MovementLogic.ComputeCastleSpreadOffset(1, 3f);
        Vector3 b = MovementLogic.ComputeCastleSpreadOffset(2, 3f);
        bool different = Mathf.Abs(a.x - b.x) > 0.01f || Mathf.Abs(a.z - b.z) > 0.01f;
        Assert.IsTrue(different, "Different IDs should produce different offsets");
    }

    [Test]
    public void ComputeCastleSpreadOffset_WithinRadiusBounds()
    {
        float radius = 5f;
        for (int id = 0; id < 100; id++)
        {
            Vector3 offset = MovementLogic.ComputeCastleSpreadOffset(id, radius);
            float dist = new Vector2(offset.x, offset.z).magnitude;
            Assert.AreEqual(0f, offset.y, 0.001f, "Y should always be 0");
            Assert.LessOrEqual(dist, radius + 0.01f, $"Offset for id={id} exceeds radius");
        }
    }

    [Test]
    public void ComputeCastleSpreadOffset_RadiusScales()
    {
        Vector3 small = MovementLogic.ComputeCastleSpreadOffset(99, 1f);
        Vector3 large = MovementLogic.ComputeCastleSpreadOffset(99, 5f);
        float smallDist = new Vector2(small.x, small.z).magnitude;
        float largeDist = new Vector2(large.x, large.z).magnitude;
        Assert.AreEqual(largeDist / smallDist, 5f, 0.01f);
    }

    // ================================================================
    //  IsDuplicateDestination
    // ================================================================

    [Test]
    public void IsDuplicateDestination_Close_ReturnsTrue()
    {
        Vector3 a = new Vector3(1, 0, 0);
        Vector3 b = new Vector3(1.1f, 0, 0);
        Assert.IsTrue(MovementLogic.IsDuplicateDestination(a, b, 0.5f));
    }

    [Test]
    public void IsDuplicateDestination_Far_ReturnsFalse()
    {
        Vector3 a = new Vector3(0, 0, 0);
        Vector3 b = new Vector3(5, 0, 0);
        Assert.IsFalse(MovementLogic.IsDuplicateDestination(a, b, 0.5f));
    }

    // ================================================================
    //  PreventBackwardVelocity
    // ================================================================

    [Test]
    public void PreventBackwardVelocity_BackwardStripped()
    {
        Vector3 desired = Vector3.forward;
        Vector3 smoothed = -Vector3.forward; // entirely backward
        Vector3 result = MovementLogic.PreventBackwardVelocity(smoothed, desired);
        float forwardDot = Vector3.Dot(result, desired.normalized);
        Assert.GreaterOrEqual(forwardDot, 0f, "Backward component should be removed");
    }

    [Test]
    public void PreventBackwardVelocity_LateralPreserved()
    {
        Vector3 desired = Vector3.forward;
        Vector3 smoothed = Vector3.right; // purely lateral
        Vector3 result = MovementLogic.PreventBackwardVelocity(smoothed, desired);
        Assert.AreEqual(smoothed.x, result.x, 0.001f, "Lateral component should be preserved");
        Assert.AreEqual(smoothed.z, result.z, 0.001f);
    }

    [Test]
    public void PreventBackwardVelocity_ForwardPreserved()
    {
        Vector3 desired = Vector3.forward * 3f;
        Vector3 smoothed = Vector3.forward * 2f;
        Vector3 result = MovementLogic.PreventBackwardVelocity(smoothed, desired);
        Assert.AreEqual(smoothed.x, result.x, 0.001f);
        Assert.AreEqual(smoothed.z, result.z, 0.001f);
    }

    [Test]
    public void PreventBackwardVelocity_NearZeroDesired_ReturnsUnchanged()
    {
        Vector3 desired = Vector3.zero;
        Vector3 smoothed = new Vector3(1f, 0f, -2f);
        Vector3 result = MovementLogic.PreventBackwardVelocity(smoothed, desired);
        Assert.AreEqual(smoothed.x, result.x, 0.001f);
        Assert.AreEqual(smoothed.z, result.z, 0.001f);
    }

    [Test]
    public void PreventBackwardVelocity_DiagonalBackward_StripsOnlyBackward()
    {
        Vector3 desired = Vector3.forward;
        // smoothed has forward=-1 and lateral=+2
        Vector3 smoothed = new Vector3(2f, 0f, -1f);
        Vector3 result = MovementLogic.PreventBackwardVelocity(smoothed, desired);
        float forwardDot = Vector3.Dot(result, desired.normalized);
        Assert.GreaterOrEqual(forwardDot, -0.001f, "Forward component should be non-negative after stripping");
        Assert.AreEqual(2f, result.x, 0.001f, "Lateral X should be preserved");
    }

    // ================================================================
    //  GetRotationDirection
    // ================================================================

    [Test]
    public void GetRotationDirection_PrefersMoveDelta()
    {
        Vector3 moveDelta = new Vector3(1f, 0.5f, 0f);
        Vector3 smoothedVel = new Vector3(0f, 0f, 5f);
        Vector3 result = MovementLogic.GetRotationDirection(moveDelta, smoothedVel);
        Assert.AreEqual(1f, result.x, 0.001f, "Should use moveDelta X");
        Assert.AreEqual(0f, result.y, 0.001f, "Y should be stripped");
        Assert.AreEqual(0f, result.z, 0.001f, "Should use moveDelta Z (was 0)");
    }

    [Test]
    public void GetRotationDirection_FallsBackToSmoothedVelocity()
    {
        Vector3 moveDelta = new Vector3(0f, 0f, 0f); // negligible
        Vector3 smoothedVel = new Vector3(3f, 1f, 0f);
        Vector3 result = MovementLogic.GetRotationDirection(moveDelta, smoothedVel);
        Assert.AreEqual(3f, result.x, 0.001f, "Should fall back to smoothedVelocity X");
        Assert.AreEqual(0f, result.y, 0.001f, "Y should be stripped");
    }

    [Test]
    public void GetRotationDirection_BothNegligible_ReturnsZero()
    {
        Vector3 moveDelta = new Vector3(0.001f, 0f, 0f);
        Vector3 smoothedVel = new Vector3(0.01f, 0f, 0f);
        Vector3 result = MovementLogic.GetRotationDirection(moveDelta, smoothedVel);
        Assert.AreEqual(Vector3.zero, result);
    }

    [Test]
    public void GetRotationDirection_StripsYFromMoveDelta()
    {
        Vector3 moveDelta = new Vector3(0f, 5f, 1f); // Y=5 should be ignored, Z=1 makes it valid
        Vector3 result = MovementLogic.GetRotationDirection(moveDelta, Vector3.zero);
        Assert.AreEqual(0f, result.y, 0.001f, "Y should always be 0");
        Assert.AreEqual(1f, result.z, 0.001f);
    }

    [Test]
    public void GetRotationDirection_StripsYFromSmoothedVelocity()
    {
        Vector3 moveDelta = Vector3.zero;
        Vector3 smoothedVel = new Vector3(0f, 10f, 2f); // Y=10 ignored, Z=2 makes it valid
        Vector3 result = MovementLogic.GetRotationDirection(moveDelta, smoothedVel);
        Assert.AreEqual(0f, result.y, 0.001f, "Y should always be 0");
        Assert.AreEqual(2f, result.z, 0.001f);
    }

    // ================================================================
    //  ShouldCheckDensity
    // ================================================================

    [Test]
    public void ShouldCheckDensity_NotCombatApproach_ReturnsTrue()
    {
        Assert.IsTrue(MovementLogic.ShouldCheckDensity(false));
    }

    [Test]
    public void ShouldCheckDensity_CombatApproach_ReturnsFalse()
    {
        Assert.IsFalse(MovementLogic.ShouldCheckDensity(true));
    }

    // ================================================================
    //  EvaluateStuckTier — CombatApproach mode
    // ================================================================

    [Test]
    public void EvaluateStuckTier_CombatApproach_FarAway_ReplansInsteadOfUnreachable()
    {
        // Without combat approach: far distance → Tier3_FarUnreachable
        Assert.AreEqual(MovementLogic.StuckTier.Tier3_FarUnreachable,
            MovementLogic.EvaluateStuckTier(4f, 20f, 1f, true, false));

        // With combat approach: far distance → Tier2_Replan (never gives up)
        Assert.AreEqual(MovementLogic.StuckTier.Tier2_Replan,
            MovementLogic.EvaluateStuckTier(4f, 20f, 1f, true, true));
    }
}
