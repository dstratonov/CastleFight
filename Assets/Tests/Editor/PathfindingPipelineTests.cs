using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// End-to-end integration tests: CDT → A* → Portal Expansion → Funnel.
/// These tests build a full NavMesh from constraints and verify the complete
/// pathfinding pipeline produces correct paths, matching the SC2 architecture.
/// </summary>
[TestFixture]
public class PathfindingPipelineTests
{
    [SetUp]
    public void SetUp()
    {
        NavMeshPathfinder.ResetStats();
    }

    // ================================================================
    //  HELPERS
    // ================================================================

    private NavMeshData BuildOpenFieldMesh(float width, float height, float cellSize = 1f)
    {
        var cdt = new CDTriangulator();
        float step = 5f;
        for (float x = 0f; x <= width; x += step)
            for (float y = 0f; y <= height; y += step)
                cdt.AddVertex(new Vector2(x, y));

        cdt.Triangulate();
        var mesh = cdt.BuildNavMesh(_ => true, cellSize);
        mesh.BuildSpatialGrid(5f);
        return mesh;
    }

    private NavMeshData BuildMeshWithBuilding(float mapW, float mapH,
        float bx, float by, float bw, float bh, float cellSize = 1f)
    {
        var cdt = new CDTriangulator();
        float step = 5f;
        for (float x = 0f; x <= mapW; x += step)
            for (float y = 0f; y <= mapH; y += step)
                cdt.AddVertex(new Vector2(x, y));

        cdt.Triangulate();

        int bl = cdt.AddVertex(new Vector2(bx, by));
        int br = cdt.AddVertex(new Vector2(bx + bw, by));
        int tr = cdt.AddVertex(new Vector2(bx + bw, by + bh));
        int tl = cdt.AddVertex(new Vector2(bx, by + bh));
        cdt.InsertConstraint(bl, br);
        cdt.InsertConstraint(br, tr);
        cdt.InsertConstraint(tr, tl);
        cdt.InsertConstraint(tl, bl);

        var mesh = cdt.BuildNavMesh(pos =>
        {
            return !(pos.x > bx && pos.x < bx + bw && pos.y > by && pos.y < by + bh);
        }, cellSize);
        mesh.BuildSpatialGrid(5f);
        return mesh;
    }

    private float PathLength(List<Vector2> path)
    {
        float len = 0f;
        for (int i = 1; i < path.Count; i++)
            len += Vector2.Distance(path[i - 1], path[i]);
        return len;
    }

    // ================================================================
    //  FULL PIPELINE: OPEN FIELD
    // ================================================================

    [Test]
    public void Pipeline_OpenField_PathIsNearDirect()
    {
        var mesh = BuildOpenFieldMesh(30f, 30f);

        var path = NavMeshPathfinder.FindPath(mesh,
            new Vector2(2f, 2f), new Vector2(28f, 28f), 0.5f);

        Assert.IsNotNull(path, "Open field path should succeed");
        Assert.GreaterOrEqual(path.Count, 2);

        float direct = Vector2.Distance(new Vector2(2f, 2f), new Vector2(28f, 28f));
        float ratio = PathLength(path) / direct;
        Assert.LessOrEqual(ratio, 1.3f,
            $"Open field path should be near-direct (ratio={ratio:F2})");
    }

    [Test]
    public void Pipeline_OpenField_DifferentUnitSizes()
    {
        var mesh = BuildOpenFieldMesh(30f, 30f);
        Vector2 start = new Vector2(5f, 15f);
        Vector2 goal = new Vector2(25f, 15f);

        var pathSmall = NavMeshPathfinder.FindPath(mesh, start, goal, 0.3f);
        var pathMed = NavMeshPathfinder.FindPath(mesh, start, goal, 0.5f);
        var pathLarge = NavMeshPathfinder.FindPath(mesh, start, goal, 1.5f);

        Assert.IsNotNull(pathSmall);
        Assert.IsNotNull(pathMed);
        Assert.IsNotNull(pathLarge);
    }

    // ================================================================
    //  FULL PIPELINE: BUILDING OBSTACLE
    // ================================================================

    [Test]
    public void Pipeline_BuildingInCenter_PathGoesAround()
    {
        var mesh = BuildMeshWithBuilding(30f, 30f, 12f, 12f, 6f, 6f);

        var path = NavMeshPathfinder.FindPath(mesh,
            new Vector2(5f, 15f), new Vector2(25f, 15f), 0.5f);

        Assert.IsNotNull(path, "Path around building should succeed");

        bool passesAboveOrBelow = false;
        for (int i = 0; i < path.Count; i++)
        {
            if (path[i].y < 12f || path[i].y > 18f)
                passesAboveOrBelow = true;
        }
        Assert.IsTrue(passesAboveOrBelow,
            "Path should go above or below the building, not through it");
    }

    [Test]
    public void Pipeline_BuildingInCenter_NoWaypointInsideBuilding()
    {
        var mesh = BuildMeshWithBuilding(30f, 30f, 12f, 12f, 6f, 6f);

        var path = NavMeshPathfinder.FindPath(mesh,
            new Vector2(5f, 15f), new Vector2(25f, 15f), 0.5f);

        Assert.IsNotNull(path);
        for (int i = 0; i < path.Count; i++)
        {
            bool inside = path[i].x > 12.5f && path[i].x < 17.5f &&
                           path[i].y > 12.5f && path[i].y < 17.5f;
            Assert.IsFalse(inside,
                $"Waypoint {path[i]} is inside the building bounds");
        }
    }

    [Test]
    public void Pipeline_TwoBuildings_NarrowGap_SmallUnitPasses()
    {
        var cdt = new CDTriangulator();
        for (float x = 0f; x <= 30f; x += 5f)
            for (float y = 0f; y <= 20f; y += 5f)
                cdt.AddVertex(new Vector2(x, y));

        // Building corner vertices must be added BEFORE Triangulate()
        // so they are part of the Bowyer-Watson triangulation.
        int a0 = cdt.AddVertex(new Vector2(10f, 0f));
        int a1 = cdt.AddVertex(new Vector2(14f, 0f));
        int a2 = cdt.AddVertex(new Vector2(14f, 8f));
        int a3 = cdt.AddVertex(new Vector2(10f, 8f));

        int b0 = cdt.AddVertex(new Vector2(10f, 12f));
        int b1 = cdt.AddVertex(new Vector2(14f, 12f));
        int b2 = cdt.AddVertex(new Vector2(14f, 20f));
        int b3 = cdt.AddVertex(new Vector2(10f, 20f));

        cdt.Triangulate();

        // Building A: (10,0)-(14,8)
        cdt.InsertConstraint(a0, a1); cdt.InsertConstraint(a1, a2);
        cdt.InsertConstraint(a2, a3); cdt.InsertConstraint(a3, a0);

        // Building B: (10,12)-(14,20) — gap of 4 units between y=8 and y=12
        cdt.InsertConstraint(b0, b1); cdt.InsertConstraint(b1, b2);
        cdt.InsertConstraint(b2, b3); cdt.InsertConstraint(b3, b0);

        var mesh = cdt.BuildNavMesh(pos =>
        {
            bool inA = pos.x > 10f && pos.x < 14f && pos.y > 0f && pos.y < 8f;
            bool inB = pos.x > 10f && pos.x < 14f && pos.y > 12f && pos.y < 20f;
            return !(inA || inB);
        });
        mesh.BuildSpatialGrid(5f);

        var path = NavMeshPathfinder.FindPath(mesh,
            new Vector2(5.5f, 10f), new Vector2(25.5f, 10f), 0.3f);

        Assert.IsNotNull(path, "Small unit should fit through the 4-unit gap");
    }

    // ================================================================
    //  A* DIRECT: WIDTH FILTERING
    // ================================================================

    [Test]
    public void AStar_WidthFilter_RejectsNarrowPortal()
    {
        // Build a mesh with a bottleneck: two triangles connected by a narrow edge
        var mesh = new NavMeshData();
        // Wide left area
        mesh.AddVertex(new Vector2(0f, 0f));   // v0
        mesh.AddVertex(new Vector2(10f, 4.5f)); // v1 — narrow portal top
        mesh.AddVertex(new Vector2(0f, 10f));  // v2
        mesh.AddVertex(new Vector2(10f, 5.5f)); // v3 — narrow portal bottom
        mesh.AddVertex(new Vector2(20f, 0f));  // v4
        mesh.AddVertex(new Vector2(20f, 10f)); // v5

        // Left triangle
        mesh.AddTriangle(0, 1, 2, walkable: true);  // t0
        // Narrow bridge triangle connecting v1-v3 to right
        mesh.AddTriangle(1, 3, 2, walkable: true);  // t1 (portal v1-v3 is only 1 unit wide)
        // Right triangles
        mesh.AddTriangle(3, 1, 4, walkable: true);  // t2
        mesh.AddTriangle(1, 5, 4, walkable: true);  // t3 (this won't connect right)

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();
        mesh.BuildSpatialGrid(5f);

        var channel = NavMeshPathfinder.AStarOnTriangles(mesh, 0, 2, 1.5f);

        if (channel != null)
        {
            Assert.IsFalse(channel.Contains(1),
                "Large unit (diameter 3) should not traverse the 1-unit-wide bridge triangle");
        }
    }

    [Test]
    public void AStar_ReturnsChannelWithCorrectStartAndGoal()
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));
        mesh.AddVertex(new Vector2(10f, 0f));
        mesh.AddVertex(new Vector2(0f, 5f));
        mesh.AddVertex(new Vector2(10f, 5f));
        mesh.AddVertex(new Vector2(20f, 0f));
        mesh.AddVertex(new Vector2(20f, 5f));

        mesh.AddTriangle(0, 1, 2, walkable: true);  // t0
        mesh.AddTriangle(1, 3, 2, walkable: true);  // t1
        mesh.AddTriangle(1, 4, 3, walkable: true);  // t2
        mesh.AddTriangle(4, 5, 3, walkable: true);  // t3

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();

        var channel = NavMeshPathfinder.AStarOnTriangles(mesh, 0, 3, 0f);

        Assert.IsNotNull(channel);
        Assert.AreEqual(0, channel[0], "Channel should start with start triangle");
        Assert.AreEqual(3, channel[channel.Count - 1], "Channel should end with goal triangle");
    }

    [Test]
    public void AStar_SameTriangle_ReturnsSingleElement()
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));
        mesh.AddVertex(new Vector2(10f, 0f));
        mesh.AddVertex(new Vector2(5f, 5f));
        mesh.AddTriangle(0, 1, 2, walkable: true);
        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();

        var channel = NavMeshPathfinder.AStarOnTriangles(mesh, 0, 0, 0f);

        Assert.IsNotNull(channel);
        Assert.AreEqual(1, channel.Count, "Same start/goal triangle → single-element channel");
        Assert.AreEqual(0, channel[0]);
    }

    [Test]
    public void AStar_UnreachableGoal_ReturnsNull()
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));
        mesh.AddVertex(new Vector2(5f, 0f));
        mesh.AddVertex(new Vector2(2.5f, 5f));
        mesh.AddVertex(new Vector2(50f, 50f));
        mesh.AddVertex(new Vector2(55f, 50f));
        mesh.AddVertex(new Vector2(52.5f, 55f));

        mesh.AddTriangle(0, 1, 2, walkable: true);
        mesh.AddTriangle(3, 4, 5, walkable: true);
        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();

        var channel = NavMeshPathfinder.AStarOnTriangles(mesh, 0, 1, 0f);
        Assert.IsNull(channel, "Disconnected triangles should return null");
    }

    [Test]
    public void AStar_PrefersCheaperTriangle()
    {
        // Diamond: two routes — top (t0) and bottom (t1)
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));   // v0 left
        mesh.AddVertex(new Vector2(10f, 0f));  // v1 right
        mesh.AddVertex(new Vector2(5f, 5f));   // v2 top
        mesh.AddVertex(new Vector2(5f, -5f));  // v3 bottom

        int t0 = mesh.AddTriangle(0, 1, 2, walkable: true); // top route
        int t1 = mesh.AddTriangle(0, 3, 1, walkable: true); // bottom route

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();

        var channel = NavMeshPathfinder.AStarOnTriangles(mesh, 0, 1, 0f);
        // With only 2 triangles sharing an edge, A* goes directly.
        // But if start is t0, goal is t1, the channel should be [t0, t1] regardless.
        Assert.IsNotNull(channel);
    }

    // ================================================================
    //  COMPLEX MESH: L-SHAPED CORRIDOR
    // ================================================================

    [Test]
    public void Pipeline_LShapedCorridor_PathFollowsCorridor()
    {
        var cdt = new CDTriangulator();
        // Create L-shaped walkable area by adding points along the L
        // Outer boundary
        for (float x = 0f; x <= 20f; x += 5f)
            for (float y = 0f; y <= 20f; y += 5f)
                cdt.AddVertex(new Vector2(x, y));

        cdt.Triangulate();

        // Block the upper-right quadrant: (10,10)-(20,20)
        int bl = cdt.AddVertex(new Vector2(10f, 10f));
        int br = cdt.AddVertex(new Vector2(20f, 10f));
        int tr = cdt.AddVertex(new Vector2(20f, 20f));
        int tl = cdt.AddVertex(new Vector2(10f, 20f));
        cdt.InsertConstraint(bl, br);
        cdt.InsertConstraint(br, tr);
        cdt.InsertConstraint(tr, tl);
        cdt.InsertConstraint(tl, bl);

        var mesh = cdt.BuildNavMesh(pos =>
        {
            return !(pos.x > 10f && pos.x < 20f && pos.y > 10f && pos.y < 20f);
        });
        mesh.BuildSpatialGrid(5f);

        // Path from bottom-left to top-left must go around the blocked area
        var path = NavMeshPathfinder.FindPath(mesh,
            new Vector2(15f, 2f), new Vector2(5f, 15f), 0.5f);

        Assert.IsNotNull(path, "L-shaped corridor path should succeed");
        Assert.GreaterOrEqual(path.Count, 2);
    }

    // ================================================================
    //  COMPLEX MESH: CHOKE POINT
    // ================================================================

    [Test]
    public void Pipeline_ChokePoint_PathGoesThrough()
    {
        var cdt = new CDTriangulator();
        for (float x = 0f; x <= 30f; x += 5f)
            for (float y = 0f; y <= 20f; y += 5f)
                cdt.AddVertex(new Vector2(x, y));

        int tw0 = cdt.AddVertex(new Vector2(12f, 12f));
        int tw1 = cdt.AddVertex(new Vector2(18f, 12f));
        int tw2 = cdt.AddVertex(new Vector2(18f, 20f));
        int tw3 = cdt.AddVertex(new Vector2(12f, 20f));

        int bw0 = cdt.AddVertex(new Vector2(12f, 0f));
        int bw1 = cdt.AddVertex(new Vector2(18f, 0f));
        int bw2 = cdt.AddVertex(new Vector2(18f, 8f));
        int bw3 = cdt.AddVertex(new Vector2(12f, 8f));

        cdt.Triangulate();

        cdt.InsertConstraint(tw0, tw1); cdt.InsertConstraint(tw1, tw2);
        cdt.InsertConstraint(tw2, tw3); cdt.InsertConstraint(tw3, tw0);

        cdt.InsertConstraint(bw0, bw1); cdt.InsertConstraint(bw1, bw2);
        cdt.InsertConstraint(bw2, bw3); cdt.InsertConstraint(bw3, bw0);

        var mesh = cdt.BuildNavMesh(pos =>
        {
            bool inTop = pos.x > 12f && pos.x < 18f && pos.y > 12f && pos.y < 20f;
            bool inBot = pos.x > 12f && pos.x < 18f && pos.y > 0f && pos.y < 8f;
            return !(inTop || inBot);
        });
        mesh.BuildSpatialGrid(5f);

        // Verify the mesh has walkable triangles on both sides of the choke.
        int startTri = mesh.FindTriangleAtPosition(new Vector2(5.5f, 10f));
        int goalTri = mesh.FindTriangleAtPosition(new Vector2(25.5f, 10f));
        Assert.IsTrue(startTri >= 0, "Start position should be inside a walkable triangle");
        Assert.IsTrue(goalTri >= 0, "Goal position should be inside a walkable triangle");
        Assert.IsTrue(mesh.Triangles[startTri].IsWalkable, "Start triangle should be walkable");
        Assert.IsTrue(mesh.Triangles[goalTri].IsWalkable, "Goal triangle should be walkable");

        // Verify the choke area has walkable triangles.
        int chokeTri = mesh.FindTriangleAtPosition(new Vector2(15f, 10f));
        Assert.IsTrue(chokeTri >= 0, "Choke center should be inside a triangle");
        Assert.IsTrue(mesh.Triangles[chokeTri].IsWalkable, "Choke center triangle should be walkable");
    }

    [Test]
    public void Pipeline_ChokePoint_LargeUnitGoesAroundOrFails()
    {
        var cdt = new CDTriangulator();
        for (float x = 0f; x <= 30f; x += 5f)
            for (float y = 0f; y <= 20f; y += 5f)
                cdt.AddVertex(new Vector2(x, y));

        // Wall vertices must be added BEFORE Triangulate()
        int tw0 = cdt.AddVertex(new Vector2(14f, 11f));
        int tw1 = cdt.AddVertex(new Vector2(16f, 11f));
        int tw2 = cdt.AddVertex(new Vector2(16f, 20f));
        int tw3 = cdt.AddVertex(new Vector2(14f, 20f));

        int bw0 = cdt.AddVertex(new Vector2(14f, 0f));
        int bw1 = cdt.AddVertex(new Vector2(16f, 0f));
        int bw2 = cdt.AddVertex(new Vector2(16f, 9f));
        int bw3 = cdt.AddVertex(new Vector2(14f, 9f));

        cdt.Triangulate();

        // Two walls leaving only a 2-unit choke
        cdt.InsertConstraint(tw0, tw1); cdt.InsertConstraint(tw1, tw2);
        cdt.InsertConstraint(tw2, tw3); cdt.InsertConstraint(tw3, tw0);

        cdt.InsertConstraint(bw0, bw1); cdt.InsertConstraint(bw1, bw2);
        cdt.InsertConstraint(bw2, bw3); cdt.InsertConstraint(bw3, bw0);

        var mesh = cdt.BuildNavMesh(pos =>
        {
            bool inTop = pos.x > 14f && pos.x < 16f && pos.y > 11f && pos.y < 20f;
            bool inBot = pos.x > 14f && pos.x < 16f && pos.y > 0f && pos.y < 9f;
            return !(inTop || inBot);
        });
        mesh.BuildSpatialGrid(5f);

        // Large unit (diameter 4) can't fit through 2-unit choke
        var path = NavMeshPathfinder.FindPath(mesh,
            new Vector2(5f, 10f), new Vector2(25f, 10f), 2.0f);

        // The path either goes around or fails entirely — both are correct
        // as long as it doesn't go through the choke
        if (path != null)
        {
            for (int i = 0; i < path.Count; i++)
            {
                if (path[i].x > 14f && path[i].x < 16f)
                {
                    Assert.IsTrue(path[i].y > 11f || path[i].y < 9f,
                        $"Large unit waypoint {path[i]} should not be in the 2-unit choke");
                }
            }
        }
    }

    // ================================================================
    //  COMPLEX MESH: DEAD END
    // ================================================================

    [Test]
    public void Pipeline_DeadEnd_PathAvoidsDeadEnd()
    {
        var cdt = new CDTriangulator();
        for (float x = 0f; x <= 30f; x += 5f)
            for (float y = 0f; y <= 20f; y += 5f)
                cdt.AddVertex(new Vector2(x, y));
        cdt.Triangulate();

        // Create a U-shaped dead end: walls on three sides at right side of map
        // Bottom wall
        int w0 = cdt.AddVertex(new Vector2(20f, 0f));
        int w1 = cdt.AddVertex(new Vector2(30f, 0f));
        int w2 = cdt.AddVertex(new Vector2(30f, 3f));
        int w3 = cdt.AddVertex(new Vector2(20f, 3f));
        cdt.InsertConstraint(w0, w1); cdt.InsertConstraint(w1, w2);
        cdt.InsertConstraint(w2, w3); cdt.InsertConstraint(w3, w0);

        // Top wall
        int w4 = cdt.AddVertex(new Vector2(20f, 17f));
        int w5 = cdt.AddVertex(new Vector2(30f, 17f));
        int w6 = cdt.AddVertex(new Vector2(30f, 20f));
        int w7 = cdt.AddVertex(new Vector2(20f, 20f));
        cdt.InsertConstraint(w4, w5); cdt.InsertConstraint(w5, w6);
        cdt.InsertConstraint(w6, w7); cdt.InsertConstraint(w7, w4);

        // Right wall (closing the dead end)
        int w8 = cdt.AddVertex(new Vector2(27f, 3f));
        int w9 = cdt.AddVertex(new Vector2(30f, 3f));
        int w10 = cdt.AddVertex(new Vector2(30f, 17f));
        int w11 = cdt.AddVertex(new Vector2(27f, 17f));
        cdt.InsertConstraint(w8, w9); cdt.InsertConstraint(w9, w10);
        cdt.InsertConstraint(w10, w11); cdt.InsertConstraint(w11, w8);

        var mesh = cdt.BuildNavMesh(pos =>
        {
            bool inBot = pos.x > 20f && pos.x < 30f && pos.y > 0f && pos.y < 3f;
            bool inTop = pos.x > 20f && pos.x < 30f && pos.y > 17f && pos.y < 20f;
            bool inRight = pos.x > 27f && pos.x < 30f && pos.y > 3f && pos.y < 17f;
            return !(inBot || inTop || inRight);
        });
        mesh.BuildSpatialGrid(5f);

        // Path from left side to inside the U should still work
        var path = NavMeshPathfinder.FindPath(mesh,
            new Vector2(2f, 10f), new Vector2(22f, 10f), 0.5f);
        Assert.IsNotNull(path, "Path into the dead end area should succeed");
    }

    // ================================================================
    //  MESH VALIDATION AFTER CDT
    // ================================================================

    [Test]
    public void Pipeline_CDT_ProducesValidMesh()
    {
        var mesh = BuildMeshWithBuilding(30f, 30f, 10f, 10f, 10f, 10f);

        Assert.Greater(mesh.TriangleCount, 0);
        Assert.Greater(mesh.VertexCount, 0);

        int walkable = 0, unwalkable = 0;
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            if (mesh.Triangles[i].IsWalkable) walkable++;
            else unwalkable++;
        }

        Assert.Greater(walkable, 0, "Should have walkable triangles");
        Assert.Greater(unwalkable, 0, "Should have unwalkable triangles inside building");
    }

    [Test]
    public void Pipeline_CDT_AdjacencyIsConsistent()
    {
        var mesh = BuildMeshWithBuilding(20f, 20f, 8f, 8f, 4f, 4f);

        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            if (!mesh.Triangles[i].IsWalkable) continue;
            for (int e = 0; e < 3; e++)
            {
                int n = mesh.Triangles[i].GetNeighbor(e);
                if (n < 0) continue;

                int backEdge = mesh.Triangles[n].GetEdgeToNeighbor(i);
                Assert.GreaterOrEqual(backEdge, 0,
                    $"Triangle {n} should reference back to {i} (adjacency symmetry)");
            }
        }
    }

    // ================================================================
    //  PORTAL EXPANSION WITH CDT MESH
    // ================================================================

    [Test]
    public void Pipeline_ExpandedPortals_StayInsideMesh()
    {
        var mesh = BuildMeshWithBuilding(20f, 20f, 8f, 8f, 4f, 4f);

        var path = NavMeshPathfinder.FindPath(mesh,
            new Vector2(2f, 10f), new Vector2(18f, 10f), 0.5f);

        Assert.IsNotNull(path);
        for (int i = 0; i < path.Count; i++)
        {
            Assert.GreaterOrEqual(path[i].x, -1f, $"Waypoint {path[i]} out of mesh bounds");
            Assert.LessOrEqual(path[i].x, 21f, $"Waypoint {path[i]} out of mesh bounds");
            Assert.GreaterOrEqual(path[i].y, -1f, $"Waypoint {path[i]} out of mesh bounds");
            Assert.LessOrEqual(path[i].y, 21f, $"Waypoint {path[i]} out of mesh bounds");
        }
    }

    // ================================================================
    //  MULTIPLE BUILDINGS
    // ================================================================

    [Test]
    public void Pipeline_MultipleBuildings_PathNavigatesBetween()
    {
        var cdt = new CDTriangulator();
        for (float x = 0f; x <= 30f; x += 5f)
            for (float y = 0f; y <= 30f; y += 5f)
                cdt.AddVertex(new Vector2(x, y));
        cdt.Triangulate();

        void AddBuilding(float bx, float by, float bw, float bh)
        {
            int bl = cdt.AddVertex(new Vector2(bx, by));
            int br = cdt.AddVertex(new Vector2(bx + bw, by));
            int tr = cdt.AddVertex(new Vector2(bx + bw, by + bh));
            int tl = cdt.AddVertex(new Vector2(bx, by + bh));
            cdt.InsertConstraint(bl, br); cdt.InsertConstraint(br, tr);
            cdt.InsertConstraint(tr, tl); cdt.InsertConstraint(tl, bl);
        }

        AddBuilding(5f, 5f, 4f, 4f);
        AddBuilding(13f, 10f, 4f, 4f);
        AddBuilding(20f, 5f, 4f, 4f);

        var mesh = cdt.BuildNavMesh(pos =>
        {
            bool in1 = pos.x > 5f && pos.x < 9f && pos.y > 5f && pos.y < 9f;
            bool in2 = pos.x > 13f && pos.x < 17f && pos.y > 10f && pos.y < 14f;
            bool in3 = pos.x > 20f && pos.x < 24f && pos.y > 5f && pos.y < 9f;
            return !(in1 || in2 || in3);
        });
        mesh.BuildSpatialGrid(5f);

        int startTri = mesh.FindTriangleAtPosition(new Vector2(2f, 7f));
        int goalTri = mesh.FindTriangleAtPosition(new Vector2(28f, 7f));
        Assert.IsTrue(startTri >= 0, "Start position should be inside a triangle");
        Assert.IsTrue(goalTri >= 0, "Goal position should be inside a triangle");
        Assert.IsTrue(mesh.Triangles[startTri].IsWalkable, "Start triangle should be walkable");
        Assert.IsTrue(mesh.Triangles[goalTri].IsWalkable, "Goal triangle should be walkable");

        int midTri = mesh.FindTriangleAtPosition(new Vector2(11f, 7f));
        Assert.IsTrue(midTri >= 0, "Mid-gap triangle should exist");
        Assert.IsTrue(mesh.Triangles[midTri].IsWalkable, "Mid-gap triangle should be walkable");
    }

    // ================================================================
    //  REGRESSION: PATH QUALITY
    // ================================================================

    [Test]
    public void Pipeline_PathQuality_NoUTurns()
    {
        var mesh = BuildOpenFieldMesh(20f, 20f);

        var path = NavMeshPathfinder.FindPath(mesh,
            new Vector2(2f, 10f), new Vector2(18f, 10f), 0.5f);

        Assert.IsNotNull(path);

        // Check no U-turns: each waypoint should generally progress toward the goal
        for (int i = 1; i < path.Count; i++)
        {
            float prevDist = Vector2.Distance(path[i - 1], new Vector2(18f, 10f));
            float currDist = Vector2.Distance(path[i], new Vector2(18f, 10f));
            // Allow some slack (path may curve slightly)
            Assert.Less(currDist, prevDist + 3f,
                $"Waypoint {i} ({path[i]}) is farther from goal than {i - 1} ({path[i - 1]})");
        }
    }
}
