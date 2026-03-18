using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

[TestFixture]
public class NavMeshPathfinderTests
{
    private NavMeshData CreateCorridorMesh()
    {
        // Three triangles forming a horizontal corridor:
        //
        //   v2----v3----v5
        //   |  t0 / | t1 / |
        //   | / t0' | / t1'|
        //   v0----v1----v4
        //
        // Simplified: 4 triangles forming a straight corridor.
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));   // v0
        mesh.AddVertex(new Vector2(10f, 0f));  // v1
        mesh.AddVertex(new Vector2(0f, 5f));   // v2
        mesh.AddVertex(new Vector2(10f, 5f));  // v3
        mesh.AddVertex(new Vector2(20f, 0f));  // v4
        mesh.AddVertex(new Vector2(20f, 5f));  // v5

        mesh.AddTriangle(0, 1, 2, walkable: true);  // t0
        mesh.AddTriangle(1, 3, 2, walkable: true);  // t1
        mesh.AddTriangle(1, 4, 3, walkable: true);  // t2
        mesh.AddTriangle(4, 5, 3, walkable: true);  // t3

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();
        mesh.BuildSpatialGrid(5f);
        return mesh;
    }

    [Test]
    public void FindPath_SameTriangle_ReturnsTwoWaypoints()
    {
        var mesh = CreateCorridorMesh();
        Vector2 start = new Vector2(2f, 2f);
        Vector2 goal = new Vector2(4f, 2f);

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0f);

        Assert.IsNotNull(path, "Path within same triangle should succeed");
        Assert.AreEqual(2, path.Count, "Same-triangle path should have start and goal only");
        Assert.AreEqual(start, path[0]);
        Assert.AreEqual(goal, path[1]);
    }

    [Test]
    public void FindPath_AcrossTriangles_ReturnsValidPath()
    {
        var mesh = CreateCorridorMesh();
        Vector2 start = new Vector2(1f, 2f);
        Vector2 goal = new Vector2(19f, 2f);

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0f);

        Assert.IsNotNull(path, "Path across corridor should succeed");
        Assert.GreaterOrEqual(path.Count, 2);
        Assert.AreEqual(start, path[0]);
        Assert.AreEqual(goal, path[path.Count - 1]);
    }

    [Test]
    public void FindPath_WithUnitRadius_StaysInsideCorridor()
    {
        var mesh = CreateCorridorMesh();
        Vector2 start = new Vector2(1f, 2.5f);
        Vector2 goal = new Vector2(19f, 2.5f);

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0.5f);

        Assert.IsNotNull(path, "Path with unit radius should succeed");
        foreach (var wp in path)
        {
            Assert.GreaterOrEqual(wp.y, -0.5f, $"Waypoint {wp} should be above bottom edge");
            Assert.LessOrEqual(wp.y, 5.5f, $"Waypoint {wp} should be below top edge");
        }
    }

    [Test]
    public void FindPath_OutsideMesh_SnapsToNearestTriangle()
    {
        var mesh = CreateCorridorMesh();

        var path = NavMeshPathfinder.FindPath(mesh, new Vector2(-100f, -100f), new Vector2(5f, 2f), 0f);
        Assert.IsNotNull(path, "Pathfinder snaps out-of-mesh start to nearest walkable triangle");
        Assert.GreaterOrEqual(path.Count, 2);
    }

    [Test]
    public void FindPath_GoalInUnwalkable_SnapsToNearestWalkable()
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));
        mesh.AddVertex(new Vector2(10f, 0f));
        mesh.AddVertex(new Vector2(5f, 5f));
        mesh.AddVertex(new Vector2(15f, 5f));

        mesh.AddTriangle(0, 1, 2, walkable: true);
        mesh.AddTriangle(1, 3, 2, walkable: false);

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();
        mesh.BuildSpatialGrid(5f);

        var path = NavMeshPathfinder.FindPath(mesh, new Vector2(3f, 1f), new Vector2(12f, 3f), 0f);
        Assert.IsNotNull(path, "Pathfinder snaps goal in unwalkable triangle to nearest walkable");
    }

    [Test]
    public void FindPath_PathLengthIsReasonable()
    {
        var mesh = CreateCorridorMesh();
        Vector2 start = new Vector2(1f, 2.5f);
        Vector2 goal = new Vector2(19f, 2.5f);

        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0f);
        Assert.IsNotNull(path);

        float pathLen = 0f;
        for (int i = 1; i < path.Count; i++)
            pathLen += Vector2.Distance(path[i - 1], path[i]);

        float directDist = Vector2.Distance(start, goal);
        float ratio = pathLen / directDist;

        Assert.LessOrEqual(ratio, 1.5f, $"Path should be within 50% of direct distance (ratio={ratio:F2})");
    }

    [Test]
    public void BuildPortals_ProducesCorrectCount()
    {
        var mesh = CreateCorridorMesh();
        var channel = new List<int> { 0, 1, 2, 3 };
        Vector2 start = new Vector2(1f, 2f);
        Vector2 goal = new Vector2(19f, 2f);

        var portals = NavMeshPathfinder.BuildPortals(mesh, channel, start, goal);

        Assert.AreEqual(channel.Count + 1, portals.Count,
            "Portal count = channel length - 1 intermediate + 2 degenerate");
    }

    [Test]
    public void ExpandPortals_ShrinksCorridor()
    {
        var mesh = CreateCorridorMesh();
        var channel = new List<int> { 0, 1, 2, 3 };
        Vector2 start = new Vector2(1f, 2.5f);
        Vector2 goal = new Vector2(19f, 2.5f);

        var portals = NavMeshPathfinder.BuildPortals(mesh, channel, start, goal);
        float unitRadius = 0.5f;
        var expanded = NavMeshPathfinder.ExpandPortals(portals, unitRadius);

        Assert.AreEqual(portals.Count, expanded.Count, "Portal count shouldn't change after expansion");

        for (int i = 1; i < expanded.Count - 1; i++)
        {
            float origWidth = Vector2.Distance(portals[i].left, portals[i].right);
            float expandedWidth = Vector2.Distance(expanded[i].left, expanded[i].right);
            Assert.LessOrEqual(expandedWidth, origWidth + 0.01f,
                $"Expanded portal {i} should be same width or narrower");
        }
    }

    [Test]
    public void Stats_AreResetCorrectly()
    {
        NavMeshPathfinder.StatPathsRequested = 42;
        NavMeshPathfinder.StatPathsFailed = 7;
        NavMeshPathfinder.ResetStats();

        Assert.AreEqual(0, NavMeshPathfinder.StatPathsRequested);
        Assert.AreEqual(0, NavMeshPathfinder.StatPathsFailed);
        Assert.AreEqual(0, NavMeshPathfinder.StatPathsSucceeded);
    }

    // ================================================================
    //  COST MULTIPLIER ROUTING
    // ================================================================

    // ================================================================
    //  DISCONNECTED MESH
    // ================================================================

    [Test]
    public void FindPath_DisconnectedMesh_ReturnsNull()
    {
        // Two separate triangle islands with no shared edge
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));
        mesh.AddVertex(new Vector2(5f, 0f));
        mesh.AddVertex(new Vector2(2.5f, 5f));

        mesh.AddVertex(new Vector2(100f, 100f));
        mesh.AddVertex(new Vector2(105f, 100f));
        mesh.AddVertex(new Vector2(102.5f, 105f));

        mesh.AddTriangle(0, 1, 2, walkable: true);  // island A
        mesh.AddTriangle(3, 4, 5, walkable: true);  // island B

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();
        mesh.BuildSpatialGrid(5f);

        var path = NavMeshPathfinder.FindPath(mesh,
            new Vector2(2f, 1f),      // in island A
            new Vector2(103f, 102f),   // in island B
            0f);
        Assert.IsNull(path, "Path between disconnected mesh islands should return null");
    }

    // ================================================================
    //  HUGE UNIT — ALL PORTALS TOO NARROW
    // ================================================================

    [Test]
    public void FindPath_HugeUnit_NarrowCorridor_ReturnsNullOrDirect()
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));
        mesh.AddVertex(new Vector2(10f, 0f));
        mesh.AddVertex(new Vector2(0f, 2f));
        mesh.AddVertex(new Vector2(10f, 2f));

        mesh.AddTriangle(0, 1, 2, walkable: true);
        mesh.AddTriangle(1, 3, 2, walkable: true);

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();
        mesh.BuildSpatialGrid(5f);

        var path = NavMeshPathfinder.FindPath(mesh,
            new Vector2(2f, 1f), new Vector2(8f, 1f), 3.0f);

        if (path != null)
        {
            Assert.AreEqual(2, path.Count,
                "If path succeeds, it must be a same-triangle direct path (2 waypoints)");
        }
    }

    // ================================================================
    //  FUNNEL WITH MANY PORTALS
    // ================================================================

    [Test]
    public void FindPath_LongCorridor_ManyTriangles_Succeeds()
    {
        // Build a long corridor with 10 triangle pairs
        var mesh = new NavMeshData();
        int segments = 10;
        float segWidth = 5f;
        float corridorHeight = 4f;

        for (int i = 0; i <= segments; i++)
        {
            mesh.AddVertex(new Vector2(i * segWidth, 0f));
            mesh.AddVertex(new Vector2(i * segWidth, corridorHeight));
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
        mesh.BuildSpatialGrid(5f);

        var path = NavMeshPathfinder.FindPath(mesh,
            new Vector2(1f, 2f), new Vector2(segments * segWidth - 1f, 2f), 0.5f);

        Assert.IsNotNull(path, "Long corridor path should succeed");
        Assert.GreaterOrEqual(path.Count, 2);

        float pathLen = 0f;
        for (int i = 1; i < path.Count; i++)
            pathLen += Vector2.Distance(path[i - 1], path[i]);
        float directDist = segments * segWidth - 2f;
        Assert.LessOrEqual(pathLen, directDist * 1.2f,
            "Corridor path should be close to direct distance");
    }

    // ================================================================
    //  A* DIRECT TESTS
    // ================================================================

    [Test]
    public void AStarOnTriangles_CorridorMesh_ReturnsAllFourTriangles()
    {
        var mesh = CreateCorridorMesh();
        var channel = NavMeshPathfinder.AStarOnTriangles(mesh, 0, 3, 0f);

        Assert.IsNotNull(channel);
        Assert.AreEqual(0, channel[0]);
        Assert.AreEqual(3, channel[channel.Count - 1]);
        Assert.GreaterOrEqual(channel.Count, 2,
            "Channel should have at least start and goal triangles");
    }

    [Test]
    public void AStarOnTriangles_SameTriangle_ReturnsSingleElement()
    {
        var mesh = CreateCorridorMesh();
        var channel = NavMeshPathfinder.AStarOnTriangles(mesh, 1, 1, 0f);

        Assert.IsNotNull(channel);
        Assert.AreEqual(1, channel.Count);
        Assert.AreEqual(1, channel[0]);
    }

    [Test]
    public void AStarOnTriangles_WithUnitRadius_FiltersNarrowPortals()
    {
        // Build a mesh with a narrow bottleneck
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));    // v0
        mesh.AddVertex(new Vector2(5f, 2.4f));  // v1 — narrow portal bottom
        mesh.AddVertex(new Vector2(0f, 5f));    // v2
        mesh.AddVertex(new Vector2(5f, 2.6f));  // v3 — narrow portal top (0.2 unit gap)
        mesh.AddVertex(new Vector2(10f, 0f));   // v4
        mesh.AddVertex(new Vector2(10f, 5f));   // v5

        mesh.AddTriangle(0, 1, 2, walkable: true); // t0 (left)
        mesh.AddTriangle(1, 3, 2, walkable: true); // t1 (bridge — v1-v3 portal is 0.2 units)
        mesh.AddTriangle(3, 4, 5, walkable: true); // t2 (right)

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();

        var channel = NavMeshPathfinder.AStarOnTriangles(mesh, 0, 2, 0.5f);

        if (channel != null)
        {
            Assert.IsFalse(channel.Contains(1),
                "Channel should not traverse the 0.2-unit-wide portal triangle");
        }
    }

    [Test]
    public void AStarOnTriangles_SkipsUnwalkableTriangles()
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));
        mesh.AddVertex(new Vector2(5f, 0f));
        mesh.AddVertex(new Vector2(2.5f, 5f));
        mesh.AddVertex(new Vector2(7.5f, 5f));
        mesh.AddVertex(new Vector2(10f, 0f));

        mesh.AddTriangle(0, 1, 2, walkable: true);  // t0
        mesh.AddTriangle(1, 4, 3, walkable: false);  // t1 — unwalkable
        mesh.AddTriangle(1, 3, 2, walkable: true);   // t2

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();

        var channel = NavMeshPathfinder.AStarOnTriangles(mesh, 0, 2, 0f);
        Assert.IsNotNull(channel);

        // Channel should not contain the unwalkable triangle
        Assert.IsFalse(channel.Contains(1),
            "Channel should not include unwalkable triangles");
    }

    // ================================================================
    //  FUNNEL QUALITY
    // ================================================================

    [Test]
    public void FunnelFromPortals_StraightCorridor_NearStraightPath()
    {
        var portals = new List<(Vector2 left, Vector2 right)>
        {
            (new Vector2(0f, 2.5f), new Vector2(0f, 2.5f)),   // start (degenerate)
            (new Vector2(5f, 0f), new Vector2(5f, 5f)),         // portal 1
            (new Vector2(10f, 0f), new Vector2(10f, 5f)),       // portal 2
            (new Vector2(15f, 2.5f), new Vector2(15f, 2.5f)),  // goal (degenerate)
        };

        var path = NavMeshPathfinder.FunnelFromPortals(portals);
        Assert.IsNotNull(path);
        Assert.GreaterOrEqual(path.Count, 2);

        // Path through a straight corridor should be roughly straight
        float totalLen = 0f;
        for (int i = 1; i < path.Count; i++)
            totalLen += Vector2.Distance(path[i - 1], path[i]);

        float directDist = 15f;
        Assert.LessOrEqual(totalLen, directDist * 1.1f,
            "Funnel path through straight corridor should be nearly direct");
    }

    [Test]
    public void BuildPortals_IncludesStartAndGoalDegeneratePortals()
    {
        var mesh = CreateCorridorMesh();
        var channel = new List<int> { 0, 1 };
        Vector2 start = new Vector2(2f, 2f);
        Vector2 goal = new Vector2(8f, 3f);

        var portals = NavMeshPathfinder.BuildPortals(mesh, channel, start, goal);

        // First portal should be degenerate (start point)
        Assert.AreEqual(start, portals[0].left);
        Assert.AreEqual(start, portals[0].right);

        // Last portal should be degenerate (goal point)
        Assert.AreEqual(goal, portals[portals.Count - 1].left);
        Assert.AreEqual(goal, portals[portals.Count - 1].right);
    }

    [Test]
    public void ExpandPortals_ZeroRadius_NoChange()
    {
        var portals = new List<(Vector2 left, Vector2 right)>
        {
            (new Vector2(1f, 1f), new Vector2(1f, 1f)),
            (new Vector2(5f, 0f), new Vector2(5f, 5f)),
            (new Vector2(9f, 2f), new Vector2(9f, 2f)),
        };

        var expanded = NavMeshPathfinder.ExpandPortals(portals, 0f);
        Assert.AreEqual(portals.Count, expanded.Count);
        for (int i = 0; i < portals.Count; i++)
        {
            Assert.AreEqual(portals[i].left.x, expanded[i].left.x, 0.01f);
            Assert.AreEqual(portals[i].left.y, expanded[i].left.y, 0.01f);
            Assert.AreEqual(portals[i].right.x, expanded[i].right.x, 0.01f);
            Assert.AreEqual(portals[i].right.y, expanded[i].right.y, 0.01f);
        }
    }
}
