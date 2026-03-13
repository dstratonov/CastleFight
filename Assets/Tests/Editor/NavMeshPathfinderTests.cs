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
}
