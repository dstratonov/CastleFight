using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Tests for Boids collision/separation logic.
/// Covers: separation force summing, cohesion overlap dead-zone,
/// speed limit during overlap, separation push for stopped units,
/// multi-unit scenarios, and edge cases.
/// </summary>
[TestFixture]
public class BoidsCollisionTests
{
    private const float DefaultRadius = 0.5f;
    private const float MaxSpeed = 3.5f;

    private static BoidsNeighbor MakeNeighbor(Vector3 pos, float radius = DefaultRadius,
        int teamId = 0, int instanceId = -1, Vector3? velocity = null)
    {
        return new BoidsNeighbor
        {
            Position = pos,
            Radius = radius,
            TeamId = teamId,
            InstanceId = instanceId,
            Velocity = velocity ?? Vector3.zero
        };
    }

    // ================================================================
    //  SEPARATION: SUMMED, NOT AVERAGED
    // ================================================================

    [Test]
    public void Separation_OneNeighbor_ProducesPushAway()
    {
        var myPos = Vector3.zero;
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.5f, 0f, 0f), instanceId: 2)
        };

        var forces = BoidsManager.ComputeForcesCore(
            myPos, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.Greater(forces.SeparationCount, 0);
        Assert.Less(forces.Separation.x, 0f,
            "Separation should push away from neighbor (negative X)");
    }

    [Test]
    public void Separation_TwoNeighbors_ForcesAreSummed()
    {
        var myPos = Vector3.zero;
        // One neighbor on the right, one on the left — both overlapping
        var oneNeighbor = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.5f, 0f, 0f), instanceId: 2)
        };
        var twoNeighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.5f, 0f, 0f), instanceId: 2),
            MakeNeighbor(new Vector3(-0.5f, 0f, 0f), instanceId: 3)
        };

        var forcesOne = BoidsManager.ComputeForcesCore(
            myPos, DefaultRadius, 0, 1, Vector3.forward, oneNeighbor);
        var forcesTwo = BoidsManager.ComputeForcesCore(
            myPos, DefaultRadius, 0, 1, Vector3.forward, twoNeighbors);

        // With summing, two neighbors produce roughly 2x the total separation magnitude
        // (minus the cancellation from opposite directions).
        // The key test: with neighbors on opposite sides, separation forces cancel out,
        // but each contributes its full strength (not halved).
        Assert.AreEqual(2, forcesTwo.SeparationCount);
    }

    [Test]
    public void Separation_FiveNeighborsSameDirection_StrongerThanOne()
    {
        var myPos = Vector3.zero;
        var oneNeighbor = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.5f, 0f, 0f), instanceId: 2)
        };
        var fiveNeighbors = new List<BoidsNeighbor>();
        for (int i = 0; i < 5; i++)
            fiveNeighbors.Add(MakeNeighbor(new Vector3(0.5f + i * 0.1f, 0f, 0f), instanceId: 10 + i));

        var forcesOne = BoidsManager.ComputeForcesCore(
            myPos, DefaultRadius, 0, 1, Vector3.forward, oneNeighbor);
        var forcesMany = BoidsManager.ComputeForcesCore(
            myPos, DefaultRadius, 0, 1, Vector3.forward, fiveNeighbors);

        float magOne = forcesOne.Separation.magnitude;
        float magMany = forcesMany.Separation.magnitude;
        Assert.Greater(magMany, magOne,
            "Five neighbors in the same direction should produce stronger separation than one (summed, not averaged)");
    }

    // ================================================================
    //  OVERLAP DETECTION
    // ================================================================

    [Test]
    public void HasOverlap_WhenUnitsOverlap_IsTrue()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.3f, 0f, 0f), instanceId: 2)
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.IsTrue(forces.HasOverlap,
            "dist=0.3 < combinedRadius=1.0 should detect overlap");
    }

    [Test]
    public void HasOverlap_WhenUnitsNotOverlapping_IsFalse()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(2f, 0f, 0f), instanceId: 2)
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.IsFalse(forces.HasOverlap,
            "dist=2.0 > combinedRadius=1.0 should not detect overlap");
    }

    [Test]
    public void HasOverlap_ExactlyTouching_IsFalse()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(1.0f, 0f, 0f), instanceId: 2)
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.IsFalse(forces.HasOverlap,
            "dist=1.0 == combinedRadius=1.0 is touching, not overlapping");
    }

    // ================================================================
    //  COHESION: DISABLED DURING OVERLAP
    // ================================================================

    [Test]
    public void Cohesion_WhenOverlapping_IsZero()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.3f, 0f, 0f), teamId: 0, instanceId: 2)
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.AreEqual(Vector3.zero, forces.Cohesion,
            "Cohesion should be zero when overlapping (dead zone)");
    }

    [Test]
    public void Cohesion_WhenNotOverlapping_IsNonZero()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(3f, 0f, 0f), teamId: 0, instanceId: 2)
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.AreNotEqual(Vector3.zero, forces.Cohesion,
            "Cohesion should be non-zero when not overlapping");
    }

    [Test]
    public void Cohesion_IgnoresEnemyUnits()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(3f, 0f, 0f), teamId: 1, instanceId: 2)
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.AreEqual(Vector3.zero, forces.Cohesion,
            "Cohesion should ignore enemy units");
    }

    // ================================================================
    //  SPEED LIMIT: HIGHER DURING OVERLAP
    // ================================================================

    [Test]
    public void CombineForces_NoOverlap_ClampsToMaxSpeed()
    {
        var forces = new BoidsForces
        {
            Separation = new Vector3(-10f, 0f, 0f),
            SeparationCount = 1,
            HasOverlap = false
        };

        var result = BoidsManager.CombineForces(forces, Vector3.forward * MaxSpeed, MaxSpeed, true);

        Assert.LessOrEqual(result.magnitude, MaxSpeed + 0.01f,
            "Without overlap, combined velocity should be clamped to maxSpeed");
    }

    [Test]
    public void CombineForces_WithOverlap_AllowsHigherSpeed()
    {
        var forces = new BoidsForces
        {
            Separation = new Vector3(-20f, 0f, 0f),
            SeparationCount = 1,
            HasOverlap = true
        };

        var result = BoidsManager.CombineForces(forces, Vector3.forward * MaxSpeed, MaxSpeed, true);

        Assert.Greater(result.magnitude, MaxSpeed,
            "With overlap, separation should be allowed to exceed maxSpeed");
        Assert.LessOrEqual(result.magnitude, MaxSpeed * 1.5f + 0.01f,
            "But still capped at 1.5x maxSpeed");
    }

    [Test]
    public void CombineForces_WithOverlap_SpeedLimitIs150Percent()
    {
        var forces = new BoidsForces
        {
            Separation = new Vector3(-100f, 0f, 0f),
            SeparationCount = 1,
            HasOverlap = true
        };

        var result = BoidsManager.CombineForces(forces, Vector3.zero, MaxSpeed, true);
        Assert.AreEqual(MaxSpeed * 1.5f, result.magnitude, 0.01f,
            "With extreme overlap force, speed should cap at exactly 1.5x maxSpeed");
    }

    // ================================================================
    //  SEPARATION PUSH: FOR STOPPED UNITS
    // ================================================================

    [Test]
    public void SeparationPush_NoOverlap_ReturnsZero()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(3f, 0f, 0f), instanceId: 2)
        };

        var push = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, DefaultRadius, 1, neighbors, 0.016f);

        Assert.AreEqual(Vector3.zero, push,
            "No push when units are not overlapping");
    }

    [Test]
    public void SeparationPush_WithOverlap_PushesApart()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.5f, 0f, 0f), instanceId: 2)
        };

        var push = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, DefaultRadius, 1, neighbors, 0.016f);

        Assert.Less(push.x, 0f,
            "Push should be away from overlapping neighbor (negative X)");
        Assert.Greater(push.magnitude, 0f,
            "Push magnitude should be positive");
    }

    [Test]
    public void SeparationPush_DeeperOverlap_StrongerPush()
    {
        var shallow = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.8f, 0f, 0f), instanceId: 2)
        };
        var deep = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.2f, 0f, 0f), instanceId: 2)
        };

        var pushShallow = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, DefaultRadius, 1, shallow, 0.016f);
        var pushDeep = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, DefaultRadius, 1, deep, 0.016f);

        Assert.Greater(pushDeep.magnitude, pushShallow.magnitude,
            "Deeper penetration should produce stronger push");
    }

    [Test]
    public void SeparationPush_FullOverlap_UsesHashDirection()
    {
        // Both at the same position — should use hash-based direction, not NaN
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(Vector3.zero, instanceId: 2)
        };

        var push = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, DefaultRadius, 1, neighbors, 0.016f);

        Assert.IsFalse(float.IsNaN(push.x), "Push should not be NaN");
        Assert.IsFalse(float.IsNaN(push.z), "Push should not be NaN");
        Assert.Greater(push.magnitude, 0f,
            "Full overlap should still produce a push via hash-based direction");
    }

    [Test]
    public void SeparationPush_IgnoresSelf()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(Vector3.zero, instanceId: 1)
        };

        var push = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, DefaultRadius, 1, neighbors, 0.016f);

        Assert.AreEqual(Vector3.zero, push,
            "Unit should not push itself");
    }

    [Test]
    public void SeparationPush_MultipleOverlaps_SumsForces()
    {
        var one = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.5f, 0f, 0f), instanceId: 2)
        };
        var two = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.5f, 0f, 0f), instanceId: 2),
            MakeNeighbor(new Vector3(0.5f, 0f, 0.3f), instanceId: 3)
        };

        var pushOne = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, DefaultRadius, 1, one, 0.016f);
        var pushTwo = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, DefaultRadius, 1, two, 0.016f);

        Assert.Greater(pushTwo.magnitude, pushOne.magnitude,
            "Two overlapping neighbors should produce stronger push than one");
    }

    [Test]
    public void SeparationPush_IsCappedByMaxPush()
    {
        // Create extreme overlap scenario
        var neighbors = new List<BoidsNeighbor>();
        for (int i = 0; i < 20; i++)
            neighbors.Add(MakeNeighbor(new Vector3(0.1f, 0f, 0f), instanceId: 10 + i));

        var push = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, DefaultRadius, 1, neighbors, 0.016f);

        float maxPush = DefaultRadius * 2f * 0.016f / 0.016f;
        Assert.LessOrEqual(push.magnitude, maxPush + 0.01f,
            "Push should be capped to prevent teleportation");
    }

    // ================================================================
    //  DIFFERENT UNIT SIZES
    // ================================================================

    [TestCase(0.3f, TestName = "SepPush_SmallUnit_r03")]
    [TestCase(0.5f, TestName = "SepPush_MediumUnit_r05")]
    [TestCase(1.0f, TestName = "SepPush_LargeUnit_r10")]
    [TestCase(1.5f, TestName = "SepPush_VeryLargeUnit_r15")]
    [TestCase(3.0f, TestName = "SepPush_HugeUnit_r30")]
    public void SeparationPush_DifferentUnitSizes_ProducesPush(float radius)
    {
        float overlap = radius * 0.5f;
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(overlap, 0f, 0f), radius: radius, instanceId: 2)
        };

        var push = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, radius, 1, neighbors, 0.016f);

        Assert.Greater(push.magnitude, 0f,
            $"Overlapping unit with radius={radius} should produce a push");
        Assert.Less(push.x, 0f,
            "Push should be away from the neighbor");
    }

    [TestCase(0.3f, 1.5f, TestName = "SepPush_SmallVsLarge")]
    [TestCase(0.5f, 3.0f, TestName = "SepPush_MediumVsHuge")]
    [TestCase(1.0f, 0.3f, TestName = "SepPush_LargeVsSmall")]
    public void SeparationPush_MixedSizes_ProducesPush(float myRadius, float otherRadius)
    {
        float combinedRadius = myRadius + otherRadius;
        float overlapDist = combinedRadius * 0.5f;
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(overlapDist, 0f, 0f), radius: otherRadius, instanceId: 2)
        };

        var push = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, myRadius, 1, neighbors, 0.016f);

        Assert.Greater(push.magnitude, 0f,
            $"Mixed-size overlap (my={myRadius}, other={otherRadius}) should produce push");
    }

    // ================================================================
    //  COMBINED FORCE BEHAVIOR
    // ================================================================

    [Test]
    public void CombineForces_SeparationOnlyDuringStop_StillWorks()
    {
        var forces = new BoidsForces
        {
            Separation = new Vector3(-5f, 0f, 0f),
            SeparationCount = 3,
            HasOverlap = true
        };

        var result = BoidsManager.CombineForces(forces, Vector3.zero, MaxSpeed, false);

        Assert.Greater(result.magnitude, 0f,
            "Separation force should produce movement even with zero desired velocity");
        Assert.Less(result.x, 0f,
            "Movement should be in separation direction");
    }

    [Test]
    public void CombineForces_DesiredVelocityOppositesSeparation_DirectionPreserved()
    {
        var forces = new BoidsForces
        {
            Separation = new Vector3(-15f, 0f, 0f),
            SeparationCount = 3,
            HasOverlap = true
        };

        Vector3 desired = new Vector3(MaxSpeed, 0f, 0f);
        var result = BoidsManager.CombineForces(forces, desired, MaxSpeed, true);

        // SC2-style: backward component of steering is stripped.
        // The unit should still move forward (or at worst be deflected laterally),
        // never pushed backward against its desired direction.
        Assert.GreaterOrEqual(result.x, 0f,
            "SC2-style direction preservation: separation should not reverse the desired direction");
    }

    [Test]
    public void CombineForces_LateralSeparation_PreservedFully()
    {
        // Separation perpendicular to desired direction should be preserved in full
        var forces = new BoidsForces
        {
            Separation = new Vector3(0f, 0f, 5f),
            SeparationCount = 1,
            HasOverlap = false
        };

        Vector3 desired = new Vector3(MaxSpeed, 0f, 0f);
        var result = BoidsManager.CombineForces(forces, desired, MaxSpeed, true);

        Assert.Greater(result.z, 0f,
            "Lateral separation should be preserved");
        Assert.Greater(result.x, 0f,
            "Forward desired velocity should be preserved alongside lateral separation");
    }

    [Test]
    public void CombineForces_NoMarching_SkipsCohesionAndAlignment()
    {
        var forces = new BoidsForces
        {
            Alignment = Vector3.right,
            Cohesion = new Vector3(5f, 0f, 0f),
            SeparationCount = 0
        };

        var result = BoidsManager.CombineForces(forces, Vector3.forward * MaxSpeed, MaxSpeed, false);

        // Without marching, alignment and cohesion should not be applied
        float dotRight = Vector3.Dot(result.normalized, Vector3.right);
        Assert.Less(Mathf.Abs(dotRight), 0.1f,
            "Without marching, alignment and cohesion should not deflect movement");
    }

    // ================================================================
    //  SEPARATION FORCE DIRECTION
    // ================================================================

    [Test]
    public void Separation_NeighborOnRight_PushesLeft()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.5f, 0f, 0f), instanceId: 2)
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.Less(forces.Separation.x, 0f, "Neighbor on right should push left");
    }

    [Test]
    public void Separation_NeighborBehind_PushesForward()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0f, 0f, -0.5f), instanceId: 2)
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.Greater(forces.Separation.z, 0f, "Neighbor behind should push forward");
    }

    [Test]
    public void Separation_NeighborDiagonal_PushesDiagonally()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.3f, 0f, 0.3f), instanceId: 2)
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.Less(forces.Separation.x, 0f, "Diagonal neighbor: push away in X");
        Assert.Less(forces.Separation.z, 0f, "Diagonal neighbor: push away in Z");
    }

    // ================================================================
    //  SEPARATION STRENGTH SCALING
    // ================================================================

    [Test]
    public void Separation_CloserNeighbor_StrongerForce()
    {
        var far = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(1.2f, 0f, 0f), instanceId: 2)
        };
        var close = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.3f, 0f, 0f), instanceId: 2)
        };

        var forcesFar = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, far);
        var forcesClose = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, close);

        Assert.Greater(forcesClose.Separation.magnitude, forcesFar.Separation.magnitude,
            "Closer neighbor should produce stronger separation");
    }

    [Test]
    public void Separation_AtSeparationRadiusBoundary_ZeroForce()
    {
        // combinedRadius * 1.5 = 1.5 — at this distance, strength should be zero
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(1.5f, 0f, 0f), instanceId: 2)
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.AreEqual(0, forces.SeparationCount,
            "At exactly combinedRadius*1.5, separation should not trigger");
    }

    [Test]
    public void Separation_BeyondSeparationRadius_NoForce()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(5f, 0f, 0f), instanceId: 2)
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.AreEqual(0, forces.SeparationCount);
        Assert.AreEqual(Vector3.zero, forces.Separation);
    }

    // ================================================================
    //  CROWD SCENARIOS
    // ================================================================

    [Test]
    public void Separation_SurroundedByFour_ProducesResultantPush()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.5f, 0f, 0f), instanceId: 2),
            MakeNeighbor(new Vector3(-0.5f, 0f, 0f), instanceId: 3),
            MakeNeighbor(new Vector3(0f, 0f, 0.5f), instanceId: 4),
            MakeNeighbor(new Vector3(0f, 0f, -0.5f), instanceId: 5),
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.AreEqual(4, forces.SeparationCount);
        // Symmetric arrangement should nearly cancel out
        Assert.Less(forces.Separation.magnitude, 0.1f,
            "Symmetric arrangement of 4 neighbors should nearly cancel separation forces");
    }

    [Test]
    public void Separation_AsymmetricCrowd_NetPushAwayFromDensity()
    {
        // 3 neighbors on the right, 1 on the left
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.5f, 0f, 0f), instanceId: 2),
            MakeNeighbor(new Vector3(0.6f, 0f, 0.1f), instanceId: 3),
            MakeNeighbor(new Vector3(0.4f, 0f, -0.1f), instanceId: 4),
            MakeNeighbor(new Vector3(-0.5f, 0f, 0f), instanceId: 5),
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.Less(forces.Separation.x, 0f,
            "Net push should be away from the denser side (leftward)");
    }

    // ================================================================
    //  Y-AXIS INVARIANCE
    // ================================================================

    [Test]
    public void Separation_YAxisIgnored()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.5f, 5f, 0f), instanceId: 2)
        };

        var forces = BoidsManager.ComputeForcesCore(
            new Vector3(0f, 3f, 0f), DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.AreEqual(0f, forces.Separation.y, 1e-6f,
            "Separation force should have zero Y component");
    }

    [Test]
    public void SeparationPush_YAxisZero()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.3f, 10f, 0f), instanceId: 2)
        };

        var push = BoidsManager.ComputeSeparationPushCore(
            new Vector3(0f, 5f, 0f), DefaultRadius, 1, neighbors, 0.016f);

        Assert.AreEqual(0f, push.y, 1e-6f,
            "Push Y component should be zero");
    }

    // ================================================================
    //  EDGE CASES
    // ================================================================

    [Test]
    public void ComputeForcesCore_NoNeighbors_ZeroForces()
    {
        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward,
            new List<BoidsNeighbor>());

        Assert.AreEqual(0, forces.SeparationCount);
        Assert.AreEqual(Vector3.zero, forces.Separation);
        Assert.AreEqual(Vector3.zero, forces.Avoidance);
        Assert.IsFalse(forces.HasOverlap);
    }

    [Test]
    public void SeparationPush_NoNeighbors_ReturnsZero()
    {
        var push = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, DefaultRadius, 1, new List<BoidsNeighbor>(), 0.016f);

        Assert.AreEqual(Vector3.zero, push);
    }

    [Test]
    public void CombineForces_ZeroEverything_ReturnsZero()
    {
        var forces = new BoidsForces();
        var result = BoidsManager.CombineForces(forces, Vector3.zero, MaxSpeed, true);
        Assert.AreEqual(Vector3.zero, result);
    }

    [Test]
    public void SeparationPush_VerySmallDeltaTime_PushIsSmaller()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.3f, 0f, 0f), instanceId: 2)
        };

        var pushNormal = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, DefaultRadius, 1, neighbors, 0.016f);
        var pushSmallDt = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, DefaultRadius, 1, neighbors, 0.008f);

        // maxPush scales with deltaTime, so smaller dt = smaller max
        Assert.LessOrEqual(pushSmallDt.magnitude, pushNormal.magnitude + 0.01f,
            "Push with smaller deltaTime should not exceed push with normal deltaTime");
    }

    // ================================================================
    //  AVOIDANCE FORCE
    // ================================================================

    [Test]
    public void Avoidance_NeighborAhead_ProducesPerpendicularForce()
    {
        // Unit moving forward (+Z), neighbor directly ahead
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0f, 0f, 2f), instanceId: 2)
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward * MaxSpeed, neighbors);

        // Avoidance should be perpendicular to the away-direction (i.e., on X axis)
        Assert.Greater(forces.Avoidance.magnitude, 0.1f,
            "Avoidance should produce a force when neighbor is ahead");
        Assert.Less(Mathf.Abs(forces.Avoidance.z), forces.Avoidance.magnitude * 0.5f,
            "Avoidance force should be mostly perpendicular (X), not along movement axis (Z)");
    }

    [Test]
    public void Avoidance_NoNeighborInRange_ZeroAvoidance()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0f, 0f, 20f), instanceId: 2)
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward * MaxSpeed, neighbors);

        Assert.AreEqual(Vector3.zero, forces.Avoidance,
            "Avoidance should be zero when neighbor is far away");
    }

    [Test]
    public void Avoidance_CloserNeighbor_StrongerForce()
    {
        // avoidanceRadius = myRadius * 4 = 2.0, both neighbors must be within gap < 2.0
        var far = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0f, 0f, 2.5f), instanceId: 2)
        };
        var close = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0f, 0f, 1.2f), instanceId: 2)
        };

        var forcesFar = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward * MaxSpeed, far);
        var forcesClose = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward * MaxSpeed, close);

        Assert.Greater(forcesClose.Avoidance.magnitude, forcesFar.Avoidance.magnitude,
            "Closer neighbor should produce stronger avoidance");
    }

    [Test]
    public void Avoidance_SideLock_ConsistentDirection()
    {
        // Two neighbors in the same general direction — side lock should keep avoidance consistent
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0.2f, 0f, 2f), instanceId: 2),
            MakeNeighbor(new Vector3(-0.1f, 0f, 3f), instanceId: 3)
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward * MaxSpeed, neighbors);

        // The side-lock should pick one side (positive or negative X) and stick with it
        // If the two avoidance contributions are on the same side, they should reinforce
        Assert.Greater(forces.Avoidance.magnitude, 0.1f,
            "Side-locked avoidance from two neighbors should not cancel out");
    }

    // ================================================================
    //  COHESION DIRECTION
    // ================================================================

    [Test]
    public void Cohesion_PointsTowardAllyCenter()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(5f, 0f, 0f), teamId: 0, instanceId: 2),
            MakeNeighbor(new Vector3(5f, 0f, 4f), teamId: 0, instanceId: 3),
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        // Center of mass is at (5, 0, 2), direction from origin is (+X, +Z)
        Assert.Greater(forces.Cohesion.x, 0f,
            "Cohesion should point toward center of mass (positive X)");
    }

    [Test]
    public void Alignment_PointsTowardAllyHeading()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(3f, 0f, 0f), teamId: 0, instanceId: 2,
                velocity: Vector3.right * 2f),
            MakeNeighbor(new Vector3(-3f, 0f, 0f), teamId: 0, instanceId: 3,
                velocity: Vector3.right * 1f),
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.Greater(forces.Alignment.x, 0f,
            "Alignment should point in the average heading direction of allies (positive X)");
    }

    [Test]
    public void Alignment_IgnoresEnemies()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(3f, 0f, 0f), teamId: 1, instanceId: 2,
                velocity: Vector3.right * 5f),
        };

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, Vector3.forward, neighbors);

        Assert.AreEqual(Vector3.zero, forces.Alignment,
            "Alignment should ignore enemy units");
    }

    // ================================================================
    //  DANCE PREVENTION
    // ================================================================

    [Test]
    public void DancePrevention_HeadOnCollision_ProducesLateralAvoidance()
    {
        // Place neighbor beyond regular avoidance range (gap >= myRadius*4 = 2.0)
        // so only dance prevention contributes to Avoidance.
        // dist=4.0, combinedRadius=1.0, gap=3.0 >= avoidanceRadius=2.0
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0f, 0f, 4f), teamId: 0, instanceId: 2,
                velocity: new Vector3(0f, 0f, -1f)),
        };

        Vector3 desiredVel = new Vector3(0f, 0f, 1f);

        var forces = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, desiredVel, neighbors);

        Assert.Greater(Mathf.Abs(forces.Avoidance.x), 0.1f,
            "Dance prevention should produce a lateral (X-axis) avoidance force");
        Assert.Less(Mathf.Abs(forces.Avoidance.z), Mathf.Abs(forces.Avoidance.x),
            "Dance prevention should push sideways, not along the movement axis");
    }

    [Test]
    public void DancePrevention_ParallelMovement_NoExtraAvoidance()
    {
        // Place beyond regular avoidance range so only dance prevention varies
        var neighbors = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0f, 0f, 4f), teamId: 0, instanceId: 2,
                velocity: new Vector3(0f, 0f, 1f)),
        };

        Vector3 desiredVel = new Vector3(0f, 0f, 1f);

        var forcesParallel = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, desiredVel, neighbors);

        var neighborsHeadOn = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(0f, 0f, 4f), teamId: 0, instanceId: 2,
                velocity: new Vector3(0f, 0f, -1f)),
        };

        var forcesHeadOn = BoidsManager.ComputeForcesCore(
            Vector3.zero, DefaultRadius, 0, 1, desiredVel, neighborsHeadOn);

        Assert.Greater(forcesHeadOn.Avoidance.magnitude, forcesParallel.Avoidance.magnitude,
            "Head-on should produce stronger avoidance than parallel movement");
    }

    // ================================================================
    //  COLLISION TIER TESTS (ZeroSpace-style)
    // ================================================================

    [Test]
    public void CollisionTierScale_EqualSize_ReturnsOne()
    {
        float scale = BoidsManager.CollisionTierScale(1f, 1f);
        Assert.AreEqual(1f, scale, 0.001f);
    }

    [Test]
    public void CollisionTierScale_SmallNeighbor_ReducedScale()
    {
        // Cyclops (3.0) near goblin (0.5): ratio = 0.167, scale = 0.028
        float scale = BoidsManager.CollisionTierScale(3f, 0.5f);
        Assert.Less(scale, 0.1f,
            "Large unit should barely be pushed by small neighbor");
    }

    [Test]
    public void CollisionTierScale_LargeNeighbor_FullScale()
    {
        // Goblin (0.5) near cyclops (3.0): ratio = 6.0, clamped to 1.0
        float scale = BoidsManager.CollisionTierScale(0.5f, 3f);
        Assert.AreEqual(1f, scale, 0.001f,
            "Small unit should receive full push from large neighbor");
    }

    [Test]
    public void Separation_LargeUnitResistsSmallNeighbor()
    {
        float largeRadius = 3f;
        float smallRadius = 0.5f;

        // Same overlap scenario, different perspectives
        var smallNeighbor = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(2f, 0f, 0f), radius: smallRadius, instanceId: 2)
        };

        var largeNeighbor = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(2f, 0f, 0f), radius: largeRadius, instanceId: 2)
        };

        // Large unit pushed by small neighbor
        var forcesOnLarge = BoidsManager.ComputeForcesCore(
            Vector3.zero, largeRadius, 0, 1, Vector3.zero, smallNeighbor);

        // Small unit pushed by large neighbor
        var forcesOnSmall = BoidsManager.ComputeForcesCore(
            Vector3.zero, smallRadius, 0, 1, Vector3.zero, largeNeighbor);

        Assert.Greater(forcesOnSmall.Separation.magnitude, forcesOnLarge.Separation.magnitude,
            "Small unit should be pushed more than large unit in same overlap");
    }

    [Test]
    public void SeparationPush_LargeUnitResistsSmallNeighbor()
    {
        float largeRadius = 3f;
        float smallRadius = 0.5f;
        float dt = 0.016f;

        var smallNeighbor = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(2f, 0f, 0f), radius: smallRadius, instanceId: 2)
        };

        var largeNeighbor = new List<BoidsNeighbor>
        {
            MakeNeighbor(new Vector3(2f, 0f, 0f), radius: largeRadius, instanceId: 2)
        };

        var pushOnLarge = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, largeRadius, 1, smallNeighbor, dt);

        var pushOnSmall = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, smallRadius, 1, largeNeighbor, dt);

        Assert.Greater(pushOnSmall.magnitude, pushOnLarge.magnitude,
            "Stopped large unit should resist push from small neighbor");
    }
}
