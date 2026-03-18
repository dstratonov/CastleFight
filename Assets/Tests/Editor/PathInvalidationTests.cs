using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

[TestFixture]
public class PathInvalidationTests
{
    // ================================================================
    //  ComputePathBounds
    // ================================================================

    [Test]
    public void PathBounds_NullOrEmpty_ReturnsZeroBounds()
    {
        Bounds bNull = PathInvalidation.ComputePathBounds(null);
        Assert.AreEqual(Vector3.zero, bNull.center);
        Assert.AreEqual(Vector3.zero, bNull.size);

        Bounds bEmpty = PathInvalidation.ComputePathBounds(new List<Vector3>());
        Assert.AreEqual(Vector3.zero, bEmpty.center);
        Assert.AreEqual(Vector3.zero, bEmpty.size);
    }

    [Test]
    public void PathBounds_SingleWaypoint_PointBounds()
    {
        var wp = new List<Vector3> { new Vector3(5f, 0f, 10f) };
        Bounds b = PathInvalidation.ComputePathBounds(wp);
        Assert.AreEqual(new Vector3(5f, 0f, 10f), b.center);
        Assert.AreEqual(Vector3.zero, b.size);
    }

    [Test]
    public void PathBounds_ComputesCorrectAABB()
    {
        var wp = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(10f, 0f, 5f),
            new Vector3(3f, 0f, 8f),
            new Vector3(7f, 0f, 2f),
        };

        Bounds b = PathInvalidation.ComputePathBounds(wp);

        Assert.AreEqual(0f, b.min.x, 0.001f, "min.x");
        Assert.AreEqual(0f, b.min.z, 0.001f, "min.z");
        Assert.AreEqual(10f, b.max.x, 0.001f, "max.x");
        Assert.AreEqual(8f, b.max.z, 0.001f, "max.z");
    }

    [Test]
    public void PathBounds_WithMargin_ExpandsBounds()
    {
        var wp = new List<Vector3>
        {
            new Vector3(5f, 0f, 5f),
            new Vector3(15f, 0f, 15f),
        };

        Bounds noMargin = PathInvalidation.ComputePathBounds(wp, 0f);
        Bounds withMargin = PathInvalidation.ComputePathBounds(wp, 2f);

        Assert.AreEqual(noMargin.min.x - 2f, withMargin.min.x, 0.001f);
        Assert.AreEqual(noMargin.min.z - 2f, withMargin.min.z, 0.001f);
        Assert.AreEqual(noMargin.max.x + 2f, withMargin.max.x, 0.001f);
        Assert.AreEqual(noMargin.max.z + 2f, withMargin.max.z, 0.001f);
    }

    // ================================================================
    //  PathIntersectsRegion
    // ================================================================

    [Test]
    public void PathIntersectsRegion_Overlapping_ReturnsTrue()
    {
        var wp = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(20f, 0f, 0f),
        };
        Bounds building = new Bounds(new Vector3(10f, 0f, 0f), new Vector3(4f, 4f, 4f));

        Assert.IsTrue(PathInvalidation.PathIntersectsRegion(wp, building));
    }

    [Test]
    public void PathIntersectsRegion_NonOverlapping_ReturnsFalse()
    {
        var wp = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(20f, 0f, 0f),
        };
        Bounds building = new Bounds(new Vector3(10f, 0f, 50f), new Vector3(4f, 4f, 4f));

        Assert.IsFalse(PathInvalidation.PathIntersectsRegion(wp, building));
    }

    [Test]
    public void PathIntersectsRegion_PathAboveBuilding_ReturnsFalse()
    {
        var wp = new List<Vector3>
        {
            new Vector3(0f, 0f, 10f),
            new Vector3(20f, 0f, 10f),
        };
        Bounds building = new Bounds(new Vector3(10f, 0f, -10f), new Vector3(4f, 4f, 4f));

        Assert.IsFalse(PathInvalidation.PathIntersectsRegion(wp, building));
    }

    [Test]
    public void PathIntersectsRegion_EdgeTouching_ReturnsTrue()
    {
        var wp = new List<Vector3>
        {
            new Vector3(0f, 0f, 2f),
            new Vector3(20f, 0f, 2f),
        };
        // Building from z=0 to z=4 (center=2, size=4)
        Bounds building = new Bounds(new Vector3(10f, 0f, 2f), new Vector3(4f, 4f, 4f));

        Assert.IsTrue(PathInvalidation.PathIntersectsRegion(wp, building));
    }

    [Test]
    public void PathIntersectsRegion_EmptyPath_ReturnsFalse()
    {
        Bounds building = new Bounds(new Vector3(10f, 0f, 0f), new Vector3(4f, 4f, 4f));

        Assert.IsFalse(PathInvalidation.PathIntersectsRegion(null, building));
        Assert.IsFalse(PathInvalidation.PathIntersectsRegion(new List<Vector3>(), building));
    }

    [Test]
    public void PathIntersectsRegion_WithMargin_CatchesNearMiss()
    {
        var wp = new List<Vector3>
        {
            new Vector3(0f, 0f, 5f),
            new Vector3(20f, 0f, 5f),
        };
        // Building at z=0, size=4 → extends z=-2 to z=2. Path at z=5 → gap of 3.
        Bounds building = new Bounds(new Vector3(10f, 0f, 0f), new Vector3(4f, 4f, 4f));

        Assert.IsFalse(PathInvalidation.PathIntersectsRegion(wp, building, 0f),
            "Without margin, path should not intersect");
        Assert.IsTrue(PathInvalidation.PathIntersectsRegion(wp, building, 4f),
            "With 4-unit margin, path should intersect (gap=3 < margin=4)");
    }

    [Test]
    public void PathIntersectsRegion_DiagonalPath_Overlapping()
    {
        var wp = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(20f, 0f, 20f),
        };
        Bounds building = new Bounds(new Vector3(10f, 0f, 10f), new Vector3(4f, 4f, 4f));

        Assert.IsTrue(PathInvalidation.PathIntersectsRegion(wp, building));
    }

    // ================================================================
    //  UnionBounds
    // ================================================================

    [Test]
    public void UnionBounds_TwoBuildings_EncapsulatesBoth()
    {
        Bounds a = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(4f, 4f, 4f));
        Bounds b = new Bounds(new Vector3(20f, 0f, 0f), new Vector3(4f, 4f, 4f));

        Bounds union = PathInvalidation.UnionBounds(a, b);

        Assert.LessOrEqual(union.min.x, a.min.x, "Union should contain A's min.x");
        Assert.GreaterOrEqual(union.max.x, b.max.x, "Union should contain B's max.x");
        Assert.LessOrEqual(union.min.z, Mathf.Min(a.min.z, b.min.z), "Union should contain both min.z");
    }

    [Test]
    public void UnionBounds_OverlappingBuildings_CorrectResult()
    {
        Bounds a = new Bounds(new Vector3(5f, 0f, 5f), new Vector3(6f, 4f, 6f));
        Bounds b = new Bounds(new Vector3(8f, 0f, 8f), new Vector3(6f, 4f, 6f));

        Bounds union = PathInvalidation.UnionBounds(a, b);

        // A: min=(2,_,2) max=(8,_,8)
        // B: min=(5,_,5) max=(11,_,11)
        // Union: min=(2,_,2) max=(11,_,11)
        Assert.AreEqual(2f, union.min.x, 0.001f);
        Assert.AreEqual(2f, union.min.z, 0.001f);
        Assert.AreEqual(11f, union.max.x, 0.001f);
        Assert.AreEqual(11f, union.max.z, 0.001f);
    }

    // ================================================================
    //  Realistic scenario
    // ================================================================

    [Test]
    public void Scenario_PathFarFromBuilding_NotInvalidated()
    {
        // Unit walking along the top of the map
        var wp = new List<Vector3>
        {
            new Vector3(-50f, 0f, 80f),
            new Vector3(-30f, 0f, 80f),
            new Vector3(-10f, 0f, 80f),
        };

        // Building placed at the bottom of the map
        Bounds building = new Bounds(new Vector3(0f, 0f, -50f), new Vector3(6f, 6f, 6f));

        Assert.IsFalse(PathInvalidation.PathIntersectsRegion(wp, building, 1f),
            "Path far from building should not be invalidated");
    }

    [Test]
    public void Scenario_PathThroughBuilding_Invalidated()
    {
        // Unit walking straight through where the building is placed
        var wp = new List<Vector3>
        {
            new Vector3(-10f, 0f, 0f),
            new Vector3(0f, 0f, 0f),
            new Vector3(10f, 0f, 0f),
        };

        Bounds building = new Bounds(new Vector3(0f, 0f, 0f), new Vector3(6f, 6f, 6f));

        Assert.IsTrue(PathInvalidation.PathIntersectsRegion(wp, building, 0.5f),
            "Path through building footprint should be invalidated");
    }
}
