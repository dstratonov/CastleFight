using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

[TestFixture]
public class SpawnPositionFinderTests
{
    // ================================================================
    //  IsPositionClear
    // ================================================================

    [Test]
    public void IsPositionClear_NoNearbyUnits_ReturnsTrue()
    {
        var result = SpawnPositionFinder.IsPositionClear(Vector3.zero, 0.5f, new List<SpawnPositionFinder.NearbyUnit>());
        Assert.IsTrue(result);
    }

    [Test]
    public void IsPositionClear_NullList_ReturnsTrue()
    {
        var result = SpawnPositionFinder.IsPositionClear(Vector3.zero, 0.5f, null);
        Assert.IsTrue(result);
    }

    [Test]
    public void IsPositionClear_OneUnitFarAway_ReturnsTrue()
    {
        var units = new List<SpawnPositionFinder.NearbyUnit>
        {
            new() { Position = new Vector3(10f, 0f, 0f), Radius = 0.5f, IsDead = false }
        };
        var result = SpawnPositionFinder.IsPositionClear(Vector3.zero, 0.5f, units);
        Assert.IsTrue(result);
    }

    [Test]
    public void IsPositionClear_OneUnitOverlapping_ReturnsFalse()
    {
        var units = new List<SpawnPositionFinder.NearbyUnit>
        {
            new() { Position = new Vector3(0.3f, 0f, 0f), Radius = 0.5f, IsDead = false }
        };
        var result = SpawnPositionFinder.IsPositionClear(Vector3.zero, 0.5f, units);
        Assert.IsFalse(result);
    }

    [Test]
    public void IsPositionClear_DeadUnitOverlapping_ReturnsTrue()
    {
        var units = new List<SpawnPositionFinder.NearbyUnit>
        {
            new() { Position = new Vector3(0.3f, 0f, 0f), Radius = 0.5f, IsDead = true }
        };
        var result = SpawnPositionFinder.IsPositionClear(Vector3.zero, 0.5f, units);
        Assert.IsTrue(result);
    }

    [Test]
    public void IsPositionClear_MultipleUnits_OneTooClose_ReturnsFalse()
    {
        var units = new List<SpawnPositionFinder.NearbyUnit>
        {
            new() { Position = new Vector3(10f, 0f, 0f), Radius = 0.5f, IsDead = false },
            new() { Position = new Vector3(0.3f, 0f, 0f), Radius = 0.5f, IsDead = false }
        };
        var result = SpawnPositionFinder.IsPositionClear(Vector3.zero, 0.5f, units);
        Assert.IsFalse(result);
    }

    [Test]
    public void IsPositionClear_ExactThresholdBoundary()
    {
        // combined = 0.5 + 0.5 = 1.0, threshold = 0.8 * 1.0 = 0.8
        // At exactly 0.8 distance: dist < 0.8 is false, so should be clear
        var unitsAtThreshold = new List<SpawnPositionFinder.NearbyUnit>
        {
            new() { Position = new Vector3(0.8f, 0f, 0f), Radius = 0.5f, IsDead = false }
        };
        Assert.IsTrue(SpawnPositionFinder.IsPositionClear(Vector3.zero, 0.5f, unitsAtThreshold));

        // Just inside threshold: 0.79 < 0.8 → blocked
        var unitsInsideThreshold = new List<SpawnPositionFinder.NearbyUnit>
        {
            new() { Position = new Vector3(0.79f, 0f, 0f), Radius = 0.5f, IsDead = false }
        };
        Assert.IsFalse(SpawnPositionFinder.IsPositionClear(Vector3.zero, 0.5f, unitsInsideThreshold));
    }

    // ================================================================
    //  HasClearance
    // ================================================================

    [Test]
    public void HasClearance_AllWalkable_ReturnsTrue()
    {
        bool result = SpawnPositionFinder.HasClearance(
            new Vector2Int(5, 5), 0.5f, 1f,
            isWalkable: _ => true,
            isInBounds: _ => true
        );
        Assert.IsTrue(result);
    }

    [Test]
    public void HasClearance_OneAdjacentUnwalkable_ReturnsFalse()
    {
        var unwalkable = new Vector2Int(5, 6);
        bool result = SpawnPositionFinder.HasClearance(
            new Vector2Int(5, 5), 0.5f, 1f,
            isWalkable: cell => cell != unwalkable,
            isInBounds: _ => true
        );
        Assert.IsFalse(result);
    }

    [Test]
    public void HasClearance_OutOfBoundsCell_ReturnsFalse()
    {
        bool result = SpawnPositionFinder.HasClearance(
            new Vector2Int(0, 0), 0.5f, 1f,
            isWalkable: _ => true,
            isInBounds: cell => cell.x >= 0 && cell.y >= 0 && cell.x < 10 && cell.y < 10
        );
        // cellRadius = Ceil(0.5/1) + 1 = 2, so checks (-2,-2) which is out of bounds
        Assert.IsFalse(result);
    }

    [Test]
    public void HasClearance_SmallUnit_ChecksSmallArea()
    {
        // unitRadius=0.5, cellSize=1 → cellRadius = Ceil(0.5) + 1 = 2
        // Checks (2*2+1)^2 = 25 cells
        int checkedCount = 0;
        SpawnPositionFinder.HasClearance(
            new Vector2Int(10, 10), 0.5f, 1f,
            isWalkable: _ => { checkedCount++; return true; },
            isInBounds: _ => true
        );
        Assert.AreEqual(25, checkedCount);
    }

    [Test]
    public void HasClearance_LargeUnit_ChecksLargerArea()
    {
        // unitRadius=2.5, cellSize=1 → cellRadius = Ceil(2.5) + 1 = 4
        // Checks (2*4+1)^2 = 81 cells
        int checkedCount = 0;
        SpawnPositionFinder.HasClearance(
            new Vector2Int(10, 10), 2.5f, 1f,
            isWalkable: _ => { checkedCount++; return true; },
            isInBounds: _ => true
        );
        Assert.AreEqual(81, checkedCount);
    }

    // ================================================================
    //  ComputeFallbackOffset
    // ================================================================

    [Test]
    public void ComputeFallbackOffset_Attempt0_AngleZero()
    {
        float baseSpread = 3f;
        var offset = SpawnPositionFinder.ComputeFallbackOffset(0, baseSpread);
        // angle=0, dist=baseSpread*1.0=3
        Assert.AreEqual(baseSpread, offset.x, 0.01f);
        Assert.AreEqual(0f, offset.y, 0.01f);
        Assert.AreEqual(0f, offset.z, 0.01f);
    }

    [Test]
    public void ComputeFallbackOffset_Attempt1_Angle45()
    {
        float baseSpread = 3f;
        var offset = SpawnPositionFinder.ComputeFallbackOffset(1, baseSpread);
        // angle=45°, dist=3*1.5=4.5
        float expectedDist = baseSpread * 1.5f;
        float angle = 45f * Mathf.Deg2Rad;
        Assert.AreEqual(Mathf.Cos(angle) * expectedDist, offset.x, 0.01f);
        Assert.AreEqual(0f, offset.y, 0.01f);
        Assert.AreEqual(Mathf.Sin(angle) * expectedDist, offset.z, 0.01f);
    }

    [Test]
    public void ComputeFallbackOffset_Attempt4_Angle180()
    {
        float baseSpread = 3f;
        var offset = SpawnPositionFinder.ComputeFallbackOffset(4, baseSpread);
        // angle=180°, dist=3*3.0=9
        float expectedDist = baseSpread * 3f;
        Assert.AreEqual(-expectedDist, offset.x, 0.01f);
        Assert.AreEqual(0f, offset.y, 0.01f);
        Assert.AreEqual(0f, offset.z, 0.02f); // sin(180°) ≈ 0
    }

    [Test]
    public void ComputeFallbackOffset_AllAttemptsWithinExpectedRange()
    {
        float baseSpread = 3f;
        for (int i = 0; i < 8; i++)
        {
            var offset = SpawnPositionFinder.ComputeFallbackOffset(i, baseSpread);
            float expectedDist = baseSpread * (1f + i * 0.5f);
            float actualDist = new Vector3(offset.x, 0f, offset.z).magnitude;
            Assert.AreEqual(expectedDist, actualDist, 0.01f, $"Attempt {i} distance mismatch");
        }
    }

    // ================================================================
    //  ComputeSpawnSpread
    // ================================================================

    [Test]
    public void ComputeSpawnSpread_LargeRadius_ReturnsScaled()
    {
        Assert.AreEqual(6f, SpawnPositionFinder.ComputeSpawnSpread(2.0f), 0.01f);
    }
}
