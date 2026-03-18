using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Regression tests for units moving/facing backwards during direction changes.
///
/// Root cause: smoothedVelocity uses exponential smoothing (rate 10) which takes
/// ~10 frames to converge after a direction change. During the transition,
/// smoothedVelocity points in the OLD direction, causing both backward movement
/// (newPos = oldPos + smoothedVelocity * dt) and backward facing (ApplyRotation
/// uses smoothedVelocity). Industry fix: strip the backward component of
/// smoothedVelocity relative to the desired direction, same as Boids CombineForces.
/// </summary>
[TestFixture]
public class MovementDirectionTests
{
    // ================================================================
    //  TEST 1: SmoothDamp from opposite direction produces backward velocity
    // ================================================================

    [Test]
    public void SmoothDamp_OppositeDirection_ProducesBackwardVelocity()
    {
        Vector3 current = new Vector3(-5f, 0f, 0f);
        Vector3 target = new Vector3(3.5f, 0f, 0f);
        float rate = 10f;
        float dt = 0.016f;

        Vector3 result = MovementLogic.SmoothDamp(current, target, rate, dt);

        float dotWithTarget = Vector3.Dot(result.normalized, target.normalized);
        Assert.Less(dotWithTarget, 0f,
            "SmoothDamp from opposite direction produces velocity pointing backward " +
            "relative to the desired direction. This causes units to physically move " +
            "backwards for ~10 frames during a direction change.");
    }

    // ================================================================
    //  TEST 2: Multiple SmoothDamp frames take too long to converge
    // ================================================================

    [Test]
    public void SmoothDamp_DirectionReversal_TakesMultipleFrames()
    {
        Vector3 velocity = new Vector3(-3.5f, 0f, 0f);
        Vector3 desired = new Vector3(3.5f, 0f, 0f);
        float rate = 10f;
        float dt = 0.016f;

        int framesBackward = 0;
        for (int i = 0; i < 30; i++)
        {
            velocity = MovementLogic.SmoothDamp(velocity, desired, rate, dt);
            if (Vector3.Dot(velocity, desired) < 0f)
                framesBackward++;
        }

        Assert.Greater(framesBackward, 0,
            "After a 180° direction change, smoothedVelocity continues pointing " +
            $"backward for {framesBackward} frames. Each frame, the unit physically " +
            "moves and faces the wrong direction.");
    }

    // ================================================================
    //  TEST 3: PreventBackwardVelocity strips backward component
    // ================================================================

    [Test]
    public void PreventBackwardVelocity_StripsBackwardComponent()
    {
        Vector3 smoothed = new Vector3(-4f, 0f, 0f);
        Vector3 desired = new Vector3(3.5f, 0f, 0f);

        Vector3 corrected = MovementLogic.PreventBackwardVelocity(smoothed, desired);

        float dot = Vector3.Dot(corrected, desired.normalized);
        Assert.GreaterOrEqual(dot, 0f,
            "PreventBackwardVelocity must ensure the corrected velocity " +
            "never opposes the desired direction.");
    }

    [Test]
    public void PreventBackwardVelocity_PreservesLateralComponent()
    {
        Vector3 smoothed = new Vector3(-2f, 0f, 3f);
        Vector3 desired = new Vector3(3.5f, 0f, 0f);

        Vector3 corrected = MovementLogic.PreventBackwardVelocity(smoothed, desired);

        Assert.AreEqual(3f, corrected.z, 0.01f,
            "The lateral (perpendicular) component should be preserved — " +
            "only the backward component along the desired direction is stripped.");
        Assert.GreaterOrEqual(corrected.x, 0f,
            "The forward component must not be negative.");
    }

    [Test]
    public void PreventBackwardVelocity_NoOpWhenForward()
    {
        Vector3 smoothed = new Vector3(2f, 0f, 1f);
        Vector3 desired = new Vector3(3.5f, 0f, 0f);

        Vector3 corrected = MovementLogic.PreventBackwardVelocity(smoothed, desired);

        Assert.AreEqual(smoothed.x, corrected.x, 0.01f);
        Assert.AreEqual(smoothed.z, corrected.z, 0.01f,
            "When velocity already points forward, it should not be modified.");
    }

    [Test]
    public void PreventBackwardVelocity_HandlesZeroDesired()
    {
        Vector3 smoothed = new Vector3(-2f, 0f, 3f);
        Vector3 desired = Vector3.zero;

        Vector3 corrected = MovementLogic.PreventBackwardVelocity(smoothed, desired);

        Assert.AreEqual(smoothed, corrected,
            "When desired velocity is zero, smoothed velocity should pass through unchanged.");
    }

    // ================================================================
    //  TEST 4: GetRotationDirection prefers moveDelta over smoothedVelocity
    // ================================================================

    [Test]
    public void GetRotationDirection_PrefersMoveDelta_WhenSignificant()
    {
        Vector3 moveDelta = new Vector3(0.05f, 0f, 0f);
        Vector3 smoothedVelocity = new Vector3(0f, 0f, 3.5f);

        Vector3 dir = MovementLogic.GetRotationDirection(moveDelta, smoothedVelocity);

        Assert.Greater(Mathf.Abs(dir.x), 0f,
            "When moveDelta has meaningful magnitude, it should be used for rotation " +
            "so the unit faces where it actually moves, not where smoothedVelocity says.");
        float dotWithDelta = Vector3.Dot(dir.normalized, moveDelta.normalized);
        Assert.Greater(dotWithDelta, 0.9f,
            "Rotation direction should closely match actual movement delta.");
    }

    [Test]
    public void GetRotationDirection_FallsBackToSmoothed_WhenDeltaTooSmall()
    {
        Vector3 moveDelta = new Vector3(0.001f, 0f, 0f);
        Vector3 smoothedVelocity = new Vector3(0f, 0f, 3.5f);

        Vector3 dir = MovementLogic.GetRotationDirection(moveDelta, smoothedVelocity);

        float dotWithSmoothed = Vector3.Dot(dir.normalized, smoothedVelocity.normalized);
        Assert.Greater(dotWithSmoothed, 0.9f,
            "When moveDelta is near-zero (unit barely moved), fall back to smoothedVelocity.");
    }

    [Test]
    public void GetRotationDirection_ReturnsZero_WhenBothSmall()
    {
        Vector3 moveDelta = Vector3.zero;
        Vector3 smoothedVelocity = new Vector3(0.001f, 0f, 0f);

        Vector3 dir = MovementLogic.GetRotationDirection(moveDelta, smoothedVelocity);

        Assert.Less(dir.sqrMagnitude, 0.01f,
            "When both moveDelta and smoothedVelocity are negligible, return zero.");
    }

    // ================================================================
    //  TEST 5: Full pipeline — direction change should not cause backward movement
    // ================================================================

    [Test]
    public void FullPipeline_DirectionChange_NeverMovesBackward()
    {
        Vector3 velocity = new Vector3(-3.5f, 0f, 0f);
        Vector3 desired = new Vector3(3.5f, 0f, 0f);
        float rate = 10f;
        float dt = 0.016f;

        for (int i = 0; i < 30; i++)
        {
            velocity = MovementLogic.SmoothDamp(velocity, desired, rate, dt);
            velocity = MovementLogic.PreventBackwardVelocity(velocity, desired);

            float dot = Vector3.Dot(velocity, desired.normalized);
            Assert.GreaterOrEqual(dot, -0.001f,
                $"Frame {i}: velocity must never oppose desired direction after " +
                "backward prevention. Got dot=" + dot);
        }
    }

    // ================================================================
    //  TEST 6: Diagonal direction change preserves lateral movement
    // ================================================================

    [Test]
    public void FullPipeline_DiagonalChange_PreservesLateral()
    {
        Vector3 velocity = new Vector3(-2f, 0f, 2f);
        Vector3 desired = new Vector3(3.5f, 0f, 0f);
        float rate = 10f;
        float dt = 0.016f;

        velocity = MovementLogic.SmoothDamp(velocity, desired, rate, dt);
        velocity = MovementLogic.PreventBackwardVelocity(velocity, desired);

        Assert.GreaterOrEqual(Vector3.Dot(velocity, desired.normalized), -0.001f,
            "Forward component must not be negative.");
        Assert.Greater(Mathf.Abs(velocity.z), 0.1f,
            "Lateral component should be preserved during backward stripping.");
    }
}
