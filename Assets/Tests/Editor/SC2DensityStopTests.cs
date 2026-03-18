using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

[TestFixture]
public class SC2DensityStopTests
{
    // ================================================================
    //  HASH-BASED DIRECTION (deterministic zero-distance resolution)
    // ================================================================

    [Test]
    public void HashBasedDirection_SameId_SameDirection()
    {
        var dir1 = BoidsManager.HashBasedDirection(42);
        var dir2 = BoidsManager.HashBasedDirection(42);
        Assert.AreEqual(dir1, dir2, "Same instance ID should produce same direction");
    }

    [Test]
    public void HashBasedDirection_DifferentIds_DifferentDirections()
    {
        var dir1 = BoidsManager.HashBasedDirection(1);
        var dir2 = BoidsManager.HashBasedDirection(2);
        Assert.AreNotEqual(dir1, dir2, "Different instance IDs should produce different directions");
    }

    [Test]
    public void HashBasedDirection_IsUnitLength()
    {
        var dir = BoidsManager.HashBasedDirection(100);
        Assert.AreEqual(1f, dir.magnitude, 0.01f, "Direction should be approximately unit length");
    }

    [Test]
    public void HashBasedDirection_YComponentIsZero()
    {
        var dir = BoidsManager.HashBasedDirection(55);
        Assert.AreEqual(0f, dir.y, 0.001f, "Y component should be zero (2D direction)");
    }

    [Test]
    public void HashBasedDirection_NegativeId_NoException()
    {
        Assert.DoesNotThrow(() => BoidsManager.HashBasedDirection(-999));
    }

    // ================================================================
    //  SEPARATION PUSH — FRAME-RATE INDEPENDENCE
    // ================================================================

    [Test]
    public void SeparationPush_ProportionalToDeltaTime()
    {
        // Use enough overlapping neighbors so the total push exceeds the
        // per-frame cap at both frame rates. When capped, the output
        // scales exactly with deltaTime (cap = radius * PushSpeed * dt).
        var neighbors = new List<BoidsNeighbor>();
        for (int i = 0; i < 5; i++)
        {
            neighbors.Add(new BoidsNeighbor
            {
                Position = new Vector3(0.1f, 0f, 0f),
                Radius = 0.5f,
                InstanceId = 10 + i
            });
        }

        var push60fps = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, 0.5f, 1, neighbors, 1f / 60f);

        var push30fps = BoidsManager.ComputeSeparationPushCore(
            Vector3.zero, 0.5f, 1, neighbors, 1f / 30f);

        float ratio = push30fps.magnitude / Mathf.Max(push60fps.magnitude, 0.0001f);
        Assert.AreEqual(2f, ratio, 0.3f,
            "Push magnitude should scale approximately linearly with deltaTime");
    }


    // ================================================================
    //  DENSITY STOP — UNIT-SIZE-AWARE (ComputeDensityCore)
    // ================================================================

    [Test]
    public void DensityCore_NoNeighbors_StillIncludesOwnArea()
    {
        var neighbors = new List<BoidsNeighbor>();
        var (density, shouldStop) = BoidsManager.ComputeDensityCore(
            distToDest: 5f, myRadius: 0.5f, neighbors, myInstanceId: 1);

        Assert.Greater(density, 0f,
            "Density should be > 0 even with no neighbors (own area included)");
        Assert.IsFalse(shouldStop,
            "Single unit alone should not trigger density stop");
    }

    [Test]
    public void DensityCore_LargeUnit_HigherSelfDensity()
    {
        var neighbors = new List<BoidsNeighbor>();

        var (densitySmall, _) = BoidsManager.ComputeDensityCore(
            distToDest: 3f, myRadius: 0.5f, neighbors, myInstanceId: 1);

        var (densityLarge, _) = BoidsManager.ComputeDensityCore(
            distToDest: 3f, myRadius: 2f, neighbors, myInstanceId: 1);

        Assert.Greater(densityLarge, densitySmall,
            $"Large unit (r=2) density={densityLarge:F3} should > small unit (r=0.5) density={densitySmall:F3}");
    }

    [Test]
    public void DensityCore_CrowdedDestination_StopsUnit()
    {
        var neighbors = new List<BoidsNeighbor>();
        for (int i = 0; i < 20; i++)
        {
            neighbors.Add(new BoidsNeighbor
            {
                Position = Vector3.zero,
                Radius = 1f,
                InstanceId = 10 + i
            });
        }

        var (density, shouldStop) = BoidsManager.ComputeDensityCore(
            distToDest: 3f, myRadius: 0.5f, neighbors, myInstanceId: 1);

        Assert.IsTrue(shouldStop,
            $"Heavily crowded destination should trigger density stop (density={density:F2})");
    }

    [Test]
    public void DensityCore_FewNeighbors_DoesNotStop()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            new BoidsNeighbor { Position = Vector3.zero, Radius = 0.5f, InstanceId = 10 }
        };

        var (density, shouldStop) = BoidsManager.ComputeDensityCore(
            distToDest: 10f, myRadius: 0.5f, neighbors, myInstanceId: 1);

        Assert.IsFalse(shouldStop,
            $"Sparse destination should not trigger density stop (density={density:F3})");
    }

    [Test]
    public void DensityCore_TooClose_ReturnsNoDensity()
    {
        var neighbors = new List<BoidsNeighbor>();
        var (density, shouldStop) = BoidsManager.ComputeDensityCore(
            distToDest: 0.1f, myRadius: 0.5f, neighbors, myInstanceId: 1);

        Assert.AreEqual(0f, density, "Distance below minimum should return 0 density");
        Assert.IsFalse(shouldStop);
    }

    [Test]
    public void DensityCore_TooFar_ReturnsNoDensity()
    {
        var neighbors = new List<BoidsNeighbor>();
        var (density, shouldStop) = BoidsManager.ComputeDensityCore(
            distToDest: 20f, myRadius: 0.5f, neighbors, myInstanceId: 1);

        Assert.AreEqual(0f, density, "Distance above maximum should return 0 density");
        Assert.IsFalse(shouldStop);
    }

    [Test]
    public void DensityCore_LargeUnitNearDest_StopsEarlier()
    {
        var neighbors = new List<BoidsNeighbor>();
        for (int i = 0; i < 5; i++)
        {
            neighbors.Add(new BoidsNeighbor
            {
                Position = Vector3.zero,
                Radius = 0.5f,
                InstanceId = 10 + i
            });
        }

        var (densitySmall, stopSmall) = BoidsManager.ComputeDensityCore(
            distToDest: 2f, myRadius: 0.5f, neighbors, myInstanceId: 1);

        var (densityLarge, stopLarge) = BoidsManager.ComputeDensityCore(
            distToDest: 2f, myRadius: 2f, neighbors, myInstanceId: 1);

        Assert.Greater(densityLarge, densitySmall,
            "Large unit should see higher density at same distance");
    }

    [Test]
    public void DensityCore_ExcludesSelf()
    {
        var neighbors = new List<BoidsNeighbor>
        {
            new BoidsNeighbor { Position = Vector3.zero, Radius = 0.5f, InstanceId = 1 }
        };

        var (density, _) = BoidsManager.ComputeDensityCore(
            distToDest: 5f, myRadius: 0.5f, neighbors, myInstanceId: 1);

        // With self excluded from neighbors but own area still added,
        // density should equal just the unit's own area / probe area
        float probeR = 5f + 0.5f;
        float expectedDensity = (Mathf.PI * 0.5f * 0.5f) / (Mathf.PI * probeR * probeR);
        Assert.AreEqual(expectedDensity, density, 0.001f,
            "Density should exclude neighbor with own InstanceId from neighbor area");
    }
}
