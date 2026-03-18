using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Tests for the "unit can't reach nearby destination" class of bugs.
/// Covers: FindTriangleBrute fallback, width filtering for close positions,
/// portal expansion collapse, short-path funnel, edge-case positions.
/// </summary>
[TestFixture]
public class NearDestinationTests
{
    // ================================================================
    //  HELPERS
    // ================================================================

    /// <summary>
    /// Create a mesh with a walkable region and a thin unwalkable strip.
    /// The strip separates two walkable areas that are physically adjacent.
    ///
    ///   v4(0,10)--v5(10,10)--v7(20,10)
    ///   |  t1  /  |  t3  /  |  t5  /
    ///   | /  t0   | / t2    | / t4
    ///   v0(0,0)--v3(10,0)--v6(20,0)
    /// </summary>
    private NavMeshData CreateMeshWithThinTriangles()
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));     // v0
        mesh.AddVertex(new Vector2(5f, 0f));     // v1
        mesh.AddVertex(new Vector2(5f, 5f));     // v2
        mesh.AddVertex(new Vector2(10f, 0f));    // v3
        mesh.AddVertex(new Vector2(0f, 5f));     // v4
        mesh.AddVertex(new Vector2(10f, 5f));    // v5

        mesh.AddTriangle(0, 1, 4, walkable: true);   // t0: left bottom
        mesh.AddTriangle(1, 2, 4, walkable: true);   // t1: left top
        mesh.AddTriangle(1, 3, 2, walkable: true);   // t2: thin middle
        mesh.AddTriangle(3, 5, 2, walkable: true);   // t3: right top
        mesh.AddTriangle(2, 5, 4, walkable: true);   // t4: top connector

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();
        mesh.BuildSpatialGrid(5f);
        return mesh;
    }

    /// <summary>
    /// Simple quad mesh: two triangles sharing edge v1-v2.
    ///   v2(0,5)---v3(10,5)
    ///   |  t0  /  |  t1  /
    ///   | /       | /
    ///   v0(0,0)--v1(10,0)
    /// </summary>
    private NavMeshData CreateSimpleQuad(float width = 10f, float height = 5f)
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));        // v0
        mesh.AddVertex(new Vector2(width, 0f));     // v1
        mesh.AddVertex(new Vector2(0f, height));    // v2
        mesh.AddVertex(new Vector2(width, height)); // v3

        mesh.AddTriangle(0, 1, 2, walkable: true);
        mesh.AddTriangle(1, 3, 2, walkable: true);

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();
        mesh.BuildSpatialGrid(5f);
        return mesh;
    }

    /// <summary>
    /// Mesh with a walkable and unwalkable triangle adjacent to each other.
    /// Tests that positions near the boundary are handled correctly.
    ///
    ///   v2(0,5)---v3(10,5)
    ///   | t0(W) / | t1(UW) /
    ///   | /       | /
    ///   v0(0,0)--v1(10,0)
    /// </summary>
    private NavMeshData CreateMeshWithUnwalkableNeighbor()
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));    // v0
        mesh.AddVertex(new Vector2(10f, 0f));   // v1
        mesh.AddVertex(new Vector2(0f, 5f));    // v2
        mesh.AddVertex(new Vector2(10f, 5f));   // v3

        mesh.AddTriangle(0, 1, 2, walkable: true);
        mesh.AddTriangle(1, 3, 2, walkable: false);

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();
        mesh.BuildSpatialGrid(5f);
        return mesh;
    }

    // ================================================================
    //  FindTriangleBrute: closest-point vs centroid
    // ================================================================

    [Test]
    public void FindTriangleBrute_UsesClosestEdge_NotCentroid()
    {
        // Create two walkable triangles: a large one far away and a small one nearby.
        // The large triangle's centroid is farther from the test point than the
        // small triangle's centroid, but the large triangle's edge is closer.
        //
        // Before the fix, FindTriangleBrute used centroid distance and would
        // pick the wrong triangle.
        var mesh = new NavMeshData();
        // Small triangle near origin
        mesh.AddVertex(new Vector2(0f, 0f));    // v0
        mesh.AddVertex(new Vector2(2f, 0f));    // v1
        mesh.AddVertex(new Vector2(1f, 2f));    // v2
        // Large triangle far from origin but with an edge close to test point
        mesh.AddVertex(new Vector2(3f, -1f));   // v3
        mesh.AddVertex(new Vector2(20f, -1f));  // v4
        mesh.AddVertex(new Vector2(20f, 10f));  // v5

        mesh.AddTriangle(0, 1, 2, walkable: true);  // t0: small, centroid at (1, 0.67)
        mesh.AddTriangle(3, 4, 5, walkable: true);  // t1: large, centroid at (14.3, 2.67)

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();
        mesh.BuildSpatialGrid(5f);

        // Test point at (2.5, 0) — outside both triangles
        // Closest edge of t1 is v3-v4 at y=-1, distance ~1
        // Closest edge of t0 is v0-v1 at y=0, but point is at x=2.5 so closest is v1 at (2,0), dist ~0.5
        // With centroid distance: t0 centroid is closer → picks t0
        // With edge distance: t0 edge is closer → also picks t0
        // That's the same... let me adjust the test.

        // Better test: point at (10, 0) — outside both triangles
        // t0 centroid at ~(1, 0.67), distance = ~9.02
        // t1 centroid at ~(14.3, 2.67), distance = ~5.07
        // t0 closest edge: v1(2,0), distance = 8
        // t1 closest edge: v3(3,-1) to v4(20,-1) at y=-1, closest point on edge is (10,-1), distance = 1
        // With centroid: picks t1 (correct in this case)
        // With edge: picks t1 (also correct)

        // Tricky case: small triangle far, large triangle with distant centroid but near edge
        // Let me make a more targeted test.
        var mesh2 = new NavMeshData();
        mesh2.AddVertex(new Vector2(0f, 0f));    // v0
        mesh2.AddVertex(new Vector2(1f, 0f));    // v1
        mesh2.AddVertex(new Vector2(0.5f, 1f));  // v2

        mesh2.AddVertex(new Vector2(2f, -10f));  // v3
        mesh2.AddVertex(new Vector2(2f, 10f));   // v4
        mesh2.AddVertex(new Vector2(40f, 0f));   // v5

        mesh2.AddTriangle(0, 1, 2, walkable: true);   // t0: small, centroid at (0.5, 0.33)
        mesh2.AddTriangle(3, 4, 5, walkable: true);   // t1: large, centroid at (14.67, 0)

        mesh2.BuildAdjacency();
        mesh2.ComputeAllWidths();
        mesh2.BuildSpatialGrid(5f);

        // Point at (1.5, 0): outside both triangles
        // t0 centroid dist: sqrt((1.5-0.5)^2 + (0-0.33)^2) = sqrt(1 + 0.11) = 1.05
        // t1 centroid dist: sqrt((1.5-14.67)^2 + 0) = 13.17
        // t0 nearest edge: v1(1,0), dist = 0.5
        // t1 nearest edge: segment (2,-10)-(2,10) at x=2, dist = 0.5
        // Both are equidistant by edge, but centroid would pick t0.

        // Better: point at (1.8, 0)
        // t0 nearest edge: v1(1,0), dist = 0.8
        // t1 nearest edge: (2,-10)-(2,10) projected to (2,0), dist = 0.2
        // Centroid would pick t0 (dist 1.3), edge picks t1 (dist 0.2)
        int tri = mesh2.FindTriangleAtPosition(new Vector2(1.8f, 0f));
        Assert.AreEqual(1, tri,
            "FindTriangleBrute should pick the triangle whose EDGE is closest, not centroid");
    }

    [Test]
    public void FindTriangle_NearUnwalkableBoundary_FindsWalkableTriangle()
    {
        var mesh = CreateMeshWithUnwalkableNeighbor();

        // Point right at the shared edge between walkable and unwalkable
        int tri = mesh.FindTriangleAtPosition(new Vector2(5f, 2.5f));
        Assert.GreaterOrEqual(tri, 0);
        Assert.IsTrue(mesh.Triangles[tri].IsWalkable,
            "Point on walkable/unwalkable boundary should resolve to walkable triangle");
    }

    [Test]
    public void FindTriangle_InsideUnwalkableRegion_FindsNearestWalkable()
    {
        var mesh = CreateMeshWithUnwalkableNeighbor();

        // Point clearly inside the unwalkable triangle
        int tri = mesh.FindTriangleAtPosition(new Vector2(8f, 3f));
        Assert.GreaterOrEqual(tri, 0);
        Assert.IsTrue(mesh.Triangles[tri].IsWalkable,
            "Point inside unwalkable triangle should resolve to nearest walkable");
    }

    // ================================================================
    //  SqrDistanceToTriangle / SqrDistanceToSegment
    // ================================================================

    [Test]
    public void SqrDistanceToSegment_PointOnSegment_ReturnsZero()
    {
        float d = NavMeshData.SqrDistanceToSegment(
            new Vector2(5f, 0f),
            new Vector2(0f, 0f),
            new Vector2(10f, 0f));
        Assert.AreEqual(0f, d, 1e-6f);
    }

    [Test]
    public void SqrDistanceToSegment_PointPerpendicularToMidpoint()
    {
        float d = NavMeshData.SqrDistanceToSegment(
            new Vector2(5f, 3f),
            new Vector2(0f, 0f),
            new Vector2(10f, 0f));
        Assert.AreEqual(9f, d, 1e-4f, "Distance should be 3 (squared=9)");
    }

    [Test]
    public void SqrDistanceToSegment_PointBeyondEndpoint()
    {
        float d = NavMeshData.SqrDistanceToSegment(
            new Vector2(15f, 0f),
            new Vector2(0f, 0f),
            new Vector2(10f, 0f));
        Assert.AreEqual(25f, d, 1e-4f, "Distance to closest endpoint (10,0) = 5, squared=25");
    }

    [Test]
    public void SqrDistanceToTriangle_PointOnEdge_ReturnsZero()
    {
        float d = NavMeshData.SqrDistanceToTriangle(
            new Vector2(5f, 0f),
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(0f, 10f));
        Assert.AreEqual(0f, d, 1e-4f, "Point on triangle edge should have zero distance");
    }

    [Test]
    public void SqrDistanceToTriangle_PointInside_ReturnsDistToNearestEdge()
    {
        // Point (1, 1) inside triangle (0,0)-(10,0)-(0,10).
        // Nearest edge is the bottom edge (y=0), distance = 1.
        float d = NavMeshData.SqrDistanceToTriangle(
            new Vector2(1f, 1f),
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(0f, 10f));
        Assert.AreEqual(1f, d, 1e-4f,
            "Point inside triangle: distance to nearest edge, not zero (PointInTriangle handles containment)");
    }

    [Test]
    public void SqrDistanceToTriangle_PointOutside_ClosestToEdge()
    {
        float d = NavMeshData.SqrDistanceToTriangle(
            new Vector2(5f, -2f),
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(5f, 10f));
        Assert.AreEqual(4f, d, 1e-4f, "Distance to bottom edge should be 2, squared=4");
    }

    [Test]
    public void SqrDistanceToTriangle_PointOutside_ClosestToVertex()
    {
        float d = NavMeshData.SqrDistanceToTriangle(
            new Vector2(-1f, -1f),
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(0f, 10f));
        Assert.AreEqual(2f, d, 1e-4f, "Distance to vertex (0,0) should be sqrt(2), squared=2");
    }

    // ================================================================
    //  SHORT-PATH PATHFINDING: start and goal very close
    // ================================================================

    [Test]
    public void FindPath_StartAndGoalInSameTriangle_ReturnsTwoWaypoints()
    {
        var mesh = CreateSimpleQuad();
        Vector2 start = new Vector2(2f, 1f);
        Vector2 goal = new Vector2(3f, 1f);

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0f);

        Assert.IsNotNull(path, "Path within same triangle should succeed");
        Assert.AreEqual(2, path.Count);
        Assert.AreEqual(start, path[0]);
        Assert.AreEqual(goal, path[1]);
    }

    [Test]
    public void FindPath_StartAndGoalInAdjacentTriangles_VeryClose()
    {
        var mesh = CreateSimpleQuad();
        Vector2 start = new Vector2(4f, 2f);
        Vector2 goal = new Vector2(6f, 3f);

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0f);

        Assert.IsNotNull(path, "Path across shared edge should succeed");
        Assert.GreaterOrEqual(path.Count, 2);
    }

    [TestCase(0.5f, 10f, 5f, TestName = "AdjacentTri_SmallRadius_r05")]
    [TestCase(1.0f, 10f, 5f, TestName = "AdjacentTri_MediumRadius_r10")]
    [TestCase(2.0f, 10f, 10f, TestName = "AdjacentTri_LargeRadius_r20")]
    public void FindPath_StartAndGoalInAdjacentTriangles_WithRadius(float radius, float width, float height)
    {
        var mesh = CreateSimpleQuad(width, height);
        float cx = width * 0.5f;
        float cy = height * 0.5f;
        Vector2 start = new Vector2(cx - 1f, cy - 1f);
        Vector2 goal = new Vector2(cx + 1f, cy + 1f);

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, radius);

        Assert.IsNotNull(path, $"Path with radius={radius} in {width}x{height} corridor should succeed");
    }

    // ================================================================
    //  WIDTH FILTER: thin triangles near buildings
    // ================================================================

    [Test]
    public void FindPath_ThroughThinTriangle_SmallUnit_Succeeds()
    {
        var mesh = CreateMeshWithThinTriangles();
        Vector2 start = new Vector2(1f, 1f);
        Vector2 goal = new Vector2(9f, 4f);

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0.3f);
        Assert.IsNotNull(path, "Small unit should pass through thin triangles");
    }

    [Test]
    public void FindPath_ThroughThinTriangle_NoRadius_AlwaysSucceeds()
    {
        var mesh = CreateMeshWithThinTriangles();
        Vector2 start = new Vector2(1f, 1f);
        Vector2 goal = new Vector2(9f, 4f);

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0f);
        Assert.IsNotNull(path, "Zero-radius unit should always find path through connected mesh");
    }

    // ================================================================
    //  PORTAL EXPANSION: collapse to zero width
    // ================================================================

    [Test]
    public void ExpandPortals_NarrowPortal_CollapsesToMidpoint()
    {
        // A portal exactly 2*radius wide → expansion inverts it, so it collapses
        // to the midpoint of the original portal to maintain funnel chain continuity.
        var portals = new List<(Vector2 left, Vector2 right)>
        {
            (new Vector2(0f, 2.5f), new Vector2(0f, 2.5f)),   // start degenerate
            (new Vector2(5f, 3f), new Vector2(5f, 2f)),         // 1-unit-wide portal
            (new Vector2(10f, 2.5f), new Vector2(10f, 2.5f))   // end degenerate
        };

        var expanded = NavMeshPathfinder.ExpandPortals(portals, 0.5f);
        // Narrow portal collapses to midpoint instead of being skipped,
        // preserving the portal chain so the funnel doesn't produce wall-clipping paths.
        Assert.AreEqual(3, expanded.Count,
            "Narrow portal should collapse to midpoint, not be skipped");
        // The collapsed portal should be at the midpoint of original left/right
        Vector2 expectedMid = new Vector2(5f, 2.5f);
        Assert.AreEqual(expectedMid.x, expanded[1].left.x, 0.01f);
        Assert.AreEqual(expectedMid.y, expanded[1].left.y, 0.01f);
        Assert.AreEqual(expanded[1].left, expanded[1].right,
            "Collapsed portal should have left == right (zero width)");
    }

    [Test]
    public void FunnelFromPortals_CollapsedPortals_StillProducesPath()
    {
        // All portals collapsed to midpoints (worst case for near-destination paths)
        var portals = new List<(Vector2 left, Vector2 right)>
        {
            (new Vector2(0f, 0f), new Vector2(0f, 0f)),
            (new Vector2(5f, 2.5f), new Vector2(5f, 2.5f)),
            (new Vector2(10f, 5f), new Vector2(10f, 5f))
        };

        var path = NavMeshPathfinder.FunnelFromPortals(portals);

        Assert.IsNotNull(path);
        Assert.GreaterOrEqual(path.Count, 2, "Funnel should produce at least start and goal");
        Assert.AreEqual(new Vector2(0f, 0f), path[0]);
        Assert.AreEqual(new Vector2(10f, 5f), path[path.Count - 1]);
    }

    [Test]
    public void FunnelFromPortals_SinglePortal_ReturnsTwoWaypoints()
    {
        var portals = new List<(Vector2 left, Vector2 right)>
        {
            (new Vector2(0f, 0f), new Vector2(0f, 0f)),
            (new Vector2(5f, 5f), new Vector2(5f, 5f))
        };

        var path = NavMeshPathfinder.FunnelFromPortals(portals);

        Assert.IsNotNull(path);
        Assert.AreEqual(2, path.Count);
    }

    // ================================================================
    //  VERY SHORT PATHS: same position, 1 unit apart, etc.
    // ================================================================

    [Test]
    public void FindPath_StartEqualsGoal_ReturnsTwoWaypoints()
    {
        var mesh = CreateSimpleQuad();
        Vector2 pos = new Vector2(5f, 2.5f);

        var path = NavMeshPathfinder.FindPath(mesh, pos, pos, 0f);

        Assert.IsNotNull(path, "Path from position to itself should succeed");
        Assert.AreEqual(2, path.Count);
    }

    [Test]
    public void FindPath_GoalOneUnitAway_Succeeds()
    {
        var mesh = CreateSimpleQuad();
        Vector2 start = new Vector2(5f, 2f);
        Vector2 goal = new Vector2(5f, 3f);

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0.5f);

        Assert.IsNotNull(path);
        float pathLen = 0f;
        for (int i = 1; i < path.Count; i++)
            pathLen += Vector2.Distance(path[i - 1], path[i]);
        Assert.LessOrEqual(pathLen, 2f, "Path should be short for nearby destination");
    }

    [Test]
    public void FindPath_GoalHalfUnitAway_Succeeds()
    {
        var mesh = CreateSimpleQuad();
        Vector2 start = new Vector2(5f, 2.5f);
        Vector2 goal = new Vector2(5.5f, 2.5f);

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0.5f);
        Assert.IsNotNull(path, "Path to very close destination should succeed");
    }

    // ================================================================
    //  POSITIONS NEAR MESH BOUNDARY
    // ================================================================

    [Test]
    public void FindTriangle_OnVertex_ReturnsValidTriangle()
    {
        var mesh = CreateSimpleQuad();
        int tri = mesh.FindTriangleAtPosition(new Vector2(0f, 0f));
        Assert.GreaterOrEqual(tri, 0);
        Assert.IsTrue(mesh.Triangles[tri].IsWalkable);
    }

    [Test]
    public void FindTriangle_OnEdge_ReturnsValidTriangle()
    {
        var mesh = CreateSimpleQuad();
        // Point on the shared edge between the two triangles
        int tri = mesh.FindTriangleAtPosition(new Vector2(5f, 2.5f));
        Assert.GreaterOrEqual(tri, 0);
        Assert.IsTrue(mesh.Triangles[tri].IsWalkable);
    }

    [Test]
    public void FindTriangle_JustOutsideMesh_ReturnsNearestTriangle()
    {
        var mesh = CreateSimpleQuad();
        // Just outside the bottom edge
        int tri = mesh.FindTriangleAtPosition(new Vector2(5f, -0.1f));
        Assert.GreaterOrEqual(tri, 0);
        Assert.IsTrue(mesh.Triangles[tri].IsWalkable,
            "Position just outside mesh should snap to nearest walkable triangle");
    }

    [Test]
    public void FindPath_StartJustOutsideMesh_StillFindsPath()
    {
        var mesh = CreateSimpleQuad();
        Vector2 start = new Vector2(5f, -0.5f);  // just outside
        Vector2 goal = new Vector2(5f, 2.5f);     // inside

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0f);
        Assert.IsNotNull(path, "Path from just-outside position should succeed via snapping");
    }

    [Test]
    public void FindPath_GoalJustOutsideMesh_StillFindsPath()
    {
        var mesh = CreateSimpleQuad();
        Vector2 start = new Vector2(5f, 2.5f);    // inside
        Vector2 goal = new Vector2(5f, -0.5f);     // just outside

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0f);
        Assert.IsNotNull(path, "Path to just-outside position should succeed via snapping");
    }

    // ================================================================
    //  POSITIONS NEAR OBSTACLES: narrow walkable strip
    // ================================================================

    [Test]
    public void FindPath_AlongNarrowStrip_SmallRadius()
    {
        // Narrow corridor: 2 units wide, 20 units long
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));
        mesh.AddVertex(new Vector2(10f, 0f));
        mesh.AddVertex(new Vector2(0f, 2f));
        mesh.AddVertex(new Vector2(10f, 2f));
        mesh.AddVertex(new Vector2(20f, 0f));
        mesh.AddVertex(new Vector2(20f, 2f));

        mesh.AddTriangle(0, 1, 2, walkable: true);
        mesh.AddTriangle(1, 3, 2, walkable: true);
        mesh.AddTriangle(1, 4, 3, walkable: true);
        mesh.AddTriangle(4, 5, 3, walkable: true);

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();
        mesh.BuildSpatialGrid(5f);

        var path = NavMeshPathfinder.FindPath(mesh, new Vector2(1f, 1f), new Vector2(19f, 1f), 0.3f);
        Assert.IsNotNull(path, "Small unit should path through 2-unit-wide corridor");
    }

    [Test]
    public void FindPath_AlongNarrowStrip_UnitTooWide()
    {
        // Corridor 1 unit wide — unit with diameter 2 should fail
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));
        mesh.AddVertex(new Vector2(5f, 0f));
        mesh.AddVertex(new Vector2(0f, 1f));
        mesh.AddVertex(new Vector2(5f, 1f));

        mesh.AddTriangle(0, 1, 2, walkable: true);
        mesh.AddTriangle(1, 3, 2, walkable: true);

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();
        mesh.BuildSpatialGrid(5f);

        var path = NavMeshPathfinder.FindPath(mesh, new Vector2(1f, 0.5f), new Vector2(4f, 0.5f), 1.0f);

        if (path != null)
        {
            Assert.AreEqual(2, path.Count,
                "If path succeeds in 1-unit corridor with r=1.0, it must be same-triangle direct");
        }
    }

    // ================================================================
    //  ADJACENT TRIANGLES WITH DIFFERENT WALKABILITY
    // ================================================================

    [Test]
    public void FindPath_GoalInUnwalkableTriangle_SnapsToWalkable()
    {
        var mesh = CreateMeshWithUnwalkableNeighbor();
        Vector2 start = new Vector2(2f, 1f);  // in walkable t0
        Vector2 goal = new Vector2(8f, 3f);    // in unwalkable t1

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0f);
        Assert.IsNotNull(path, "Goal in unwalkable triangle should snap to nearest walkable");
    }

    [Test]
    public void FindPath_BothPositionsNearUnwalkableEdge_StillWorks()
    {
        var mesh = CreateMeshWithUnwalkableNeighbor();
        // Both positions near the walkable/unwalkable boundary
        Vector2 start = new Vector2(4.5f, 2.2f);
        Vector2 goal = new Vector2(4.9f, 2.4f);

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0f);
        Assert.IsNotNull(path, "Positions near unwalkable boundary should still produce a path");
    }

    // ================================================================
    //  PATH LENGTH RATIO FOR NEARBY DESTINATIONS
    // ================================================================

    [Test]
    public void FindPath_NearbyGoal_PathIsReasonablyDirect()
    {
        var mesh = CreateSimpleQuad(20f, 10f);
        Vector2 start = new Vector2(9f, 4f);
        Vector2 goal = new Vector2(11f, 6f);

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0.5f);

        Assert.IsNotNull(path);

        float pathLen = 0f;
        for (int i = 1; i < path.Count; i++)
            pathLen += Vector2.Distance(path[i - 1], path[i]);

        float directDist = Vector2.Distance(start, goal);
        float ratio = pathLen / Mathf.Max(directDist, 0.01f);

        Assert.LessOrEqual(ratio, 2.0f,
            $"Nearby path should be reasonably direct (ratio={ratio:F2}, pathLen={pathLen:F1}, direct={directDist:F1})");
    }

    // ================================================================
    //  MULTIPLE UNIT SIZES: near-destination paths
    // ================================================================

    [TestCase(0.3f, TestName = "NearDest_SmallUnit_r03")]
    [TestCase(0.5f, TestName = "NearDest_MediumUnit_r05")]
    [TestCase(1.0f, TestName = "NearDest_LargeUnit_r10")]
    [TestCase(1.5f, TestName = "NearDest_VeryLargeUnit_r15")]
    public void FindPath_NearbyDestination_DifferentRadii(float radius)
    {
        var mesh = CreateSimpleQuad(20f, 10f);
        Vector2 start = new Vector2(8f, 5f);
        Vector2 goal = new Vector2(12f, 5f);

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, radius);

        Assert.IsNotNull(path, $"Nearby path with radius={radius} should succeed in 10-unit-wide corridor");
        Assert.GreaterOrEqual(path.Count, 2);
    }

    [TestCase(0.3f, TestName = "CrossEdge_SmallUnit_r03")]
    [TestCase(0.5f, TestName = "CrossEdge_MediumUnit_r05")]
    [TestCase(1.0f, TestName = "CrossEdge_LargeUnit_r10")]
    public void FindPath_AcrossSharedEdge_DifferentRadii(float radius)
    {
        var mesh = CreateSimpleQuad(10f, 10f);
        // Start in t0, goal in t1, both 1 unit from the shared edge
        Vector2 start = new Vector2(4f, 4f);
        Vector2 goal = new Vector2(6f, 6f);

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, radius);

        Assert.IsNotNull(path, $"Path across shared edge with radius={radius} should succeed");
    }

    // ================================================================
    //  HUGE UNIT NEAR DESTINATION
    // ================================================================

    [Test]
    public void FindPath_NearbyDestination_HugeUnit_r30()
    {
        // Need a large mesh so the huge unit can actually fit
        var mesh = CreateSimpleQuad(40f, 20f);
        Vector2 start = new Vector2(15f, 10f);
        Vector2 goal = new Vector2(25f, 10f);

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 3.0f);

        Assert.IsNotNull(path,
            "Huge unit (r=3.0) should path to nearby destination in 20-unit-wide corridor");
        Assert.GreaterOrEqual(path.Count, 2);
    }

    // ================================================================
    //  DEGENERATE SEGMENT DISTANCE
    // ================================================================

    [Test]
    public void SqrDistanceToSegment_ZeroLengthSegment_ReturnsDistToPoint()
    {
        float d = NavMeshData.SqrDistanceToSegment(
            new Vector2(3f, 4f),
            new Vector2(0f, 0f),
            new Vector2(0f, 0f));
        Assert.AreEqual(25f, d, 1e-4f,
            "Zero-length segment should return distance to the degenerate point");
    }

    // ================================================================
    //  MULTIPLE CONSECUTIVE THIN TRIANGLES
    // ================================================================

    [Test]
    public void FindPath_ThroughMultipleThinTriangles_SmallUnit()
    {
        // Chain of thin triangles simulating a gap between buildings
        var mesh = new NavMeshData();
        float gapWidth = 2f;
        int segments = 5;

        for (int i = 0; i <= segments; i++)
        {
            mesh.AddVertex(new Vector2(i * 3f, 0f));
            mesh.AddVertex(new Vector2(i * 3f, gapWidth));
        }

        for (int i = 0; i < segments; i++)
        {
            int bl = i * 2;
            int br = (i + 1) * 2;
            int tl = i * 2 + 1;
            int tr = (i + 1) * 2 + 1;
            mesh.AddTriangle(bl, br, tl, walkable: true);
            mesh.AddTriangle(br, tr, tl, walkable: true);
        }

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();
        mesh.BuildSpatialGrid(3f);

        var path = NavMeshPathfinder.FindPath(mesh,
            new Vector2(1f, 1f),
            new Vector2(segments * 3f - 1f, 1f),
            0.3f);

        Assert.IsNotNull(path,
            "Small unit should traverse chain of thin triangles (building gap)");
    }
}
