using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

[TestFixture]
public class CriticalFixTests
{
    [SetUp]
    public void SetUp()
    {
        AttackPositionFinder.ReleaseTargetSlots(1);
        AttackPositionFinder.ReleaseTargetSlots(100);
    }

    // ================================================================
    //  #1: CDTriangulator.ConstraintsFailed exposed
    // ================================================================

    [Test]
    public void CDT_ConstraintsFailed_ZeroOnSuccess()
    {
        var cdt = new CDTriangulator();
        cdt.AddVertex(new Vector2(-10, -10));
        cdt.AddVertex(new Vector2(10, -10));
        cdt.AddVertex(new Vector2(10, 10));
        cdt.AddVertex(new Vector2(-10, 10));

        int v0 = cdt.AddVertex(new Vector2(-5, 0));
        int v1 = cdt.AddVertex(new Vector2(5, 0));
        cdt.Triangulate();
        cdt.InsertConstraint(v0, v1);

        Assert.AreEqual(0, cdt.ConstraintsFailed, "Simple constraint should not fail");
    }

    [Test]
    public void CDT_HorizontalConstraintNearBoundary_Succeeds()
    {
        // Reproduces the game scenario: horizontal building edge near map boundary
        // where the only triangles straddling the constraint involve super-vertices.
        var cdt = new CDTriangulator();

        // Sparse boundary vertices (like NavMeshBuilder's 3*cs spacing)
        cdt.AddVertex(new Vector2(-100, -50));
        cdt.AddVertex(new Vector2(100, -50));
        cdt.AddVertex(new Vector2(100, 50));
        cdt.AddVertex(new Vector2(-100, 50));

        // Building corners near the boundary
        int v0 = cdt.AddVertex(new Vector2(-39, -31));
        int v1 = cdt.AddVertex(new Vector2(-35, -31));
        int v2 = cdt.AddVertex(new Vector2(-35, -25));
        int v3 = cdt.AddVertex(new Vector2(-39, -25));

        cdt.Triangulate();

        cdt.InsertConstraint(v0, v1);
        cdt.InsertConstraint(v1, v2);
        cdt.InsertConstraint(v2, v3);
        cdt.InsertConstraint(v3, v0);

        Assert.AreEqual(0, cdt.ConstraintsFailed,
            "Horizontal constraint near boundary should succeed (super-vertex triangles allowed in walk)");
    }

    [Test]
    public void CDT_VerticalConstraintNearBoundary_Succeeds()
    {
        var cdt = new CDTriangulator();
        cdt.AddVertex(new Vector2(-100, -50));
        cdt.AddVertex(new Vector2(100, -50));
        cdt.AddVertex(new Vector2(100, 50));
        cdt.AddVertex(new Vector2(-100, 50));

        int v0 = cdt.AddVertex(new Vector2(-81, 19));
        int v1 = cdt.AddVertex(new Vector2(-81, 24.3f));
        int v2 = cdt.AddVertex(new Vector2(-75, 24.3f));
        int v3 = cdt.AddVertex(new Vector2(-75, 19));

        cdt.Triangulate();

        cdt.InsertConstraint(v0, v1);
        cdt.InsertConstraint(v1, v2);
        cdt.InsertConstraint(v2, v3);
        cdt.InsertConstraint(v3, v0);

        Assert.AreEqual(0, cdt.ConstraintsFailed,
            "Vertical constraint near boundary should succeed");
    }

    [Test]
    public void CDT_MultipleAxisAlignedConstraints_NearBoundary_AllSucceed()
    {
        // Simulates the NavMeshBuilder pattern: sparse grid + multiple buildings
        var cdt = new CDTriangulator();

        // Map corners
        cdt.AddVertex(new Vector2(-100, -50));
        cdt.AddVertex(new Vector2(100, -50));
        cdt.AddVertex(new Vector2(100, 50));
        cdt.AddVertex(new Vector2(-100, 50));

        // Boundary spacing vertices (like NavMeshBuilder cs*3)
        for (float x = -94; x < 100; x += 6)
        {
            cdt.AddVertex(new Vector2(x, -50));
            cdt.AddVertex(new Vector2(x, 50));
        }
        for (float z = -44; z < 50; z += 6)
        {
            cdt.AddVertex(new Vector2(-100, z));
            cdt.AddVertex(new Vector2(100, z));
        }

        // Three buildings at different positions
        void AddBuilding(float bx, float bz, float bw, float bh)
        {
            cdt.AddVertex(new Vector2(bx, bz));
            cdt.AddVertex(new Vector2(bx + bw, bz));
            cdt.AddVertex(new Vector2(bx + bw, bz + bh));
            cdt.AddVertex(new Vector2(bx, bz + bh));
        }
        AddBuilding(-39, -31, 4, 6);
        AddBuilding(-89, -9, 6, 18);
        AddBuilding(-81, 19, 6, 5.3f);

        cdt.Triangulate();

        void InsertBuildingConstraints(float bx, float bz, float bw, float bh)
        {
            int bl = cdt.AddVertex(new Vector2(bx, bz));
            int br = cdt.AddVertex(new Vector2(bx + bw, bz));
            int tr = cdt.AddVertex(new Vector2(bx + bw, bz + bh));
            int tl = cdt.AddVertex(new Vector2(bx, bz + bh));
            cdt.InsertConstraint(bl, br);
            cdt.InsertConstraint(br, tr);
            cdt.InsertConstraint(tr, tl);
            cdt.InsertConstraint(tl, bl);
        }
        InsertBuildingConstraints(-39, -31, 4, 6);
        InsertBuildingConstraints(-89, -9, 6, 18);
        InsertBuildingConstraints(-81, 19, 6, 5.3f);

        Assert.AreEqual(0, cdt.ConstraintsFailed,
            $"All building constraints should succeed, but {cdt.ConstraintsFailed} failed");
    }

    [Test]
    public void CDT_ExactGameFailureCoordinates_AllConstraintsSucceed()
    {
        // Reproduces the exact failing coordinates from play-mode logs:
        // Building 1: (-19,-19) to (-15,-13) — 4x6  (v102→v103 failure)
        // Building 2: (-89,-9) to (-83,9)   — 6x18 (v109→v130, v134→v135 failures)
        // Both had constraint walk+flip failures on axis-aligned edges.
        var cdt = new CDTriangulator();

        cdt.AddVertex(new Vector2(-100, -50));
        cdt.AddVertex(new Vector2(100, -50));
        cdt.AddVertex(new Vector2(100, 50));
        cdt.AddVertex(new Vector2(-100, 50));

        for (float x = -94; x < 100; x += 6)
        {
            cdt.AddVertex(new Vector2(x, -50));
            cdt.AddVertex(new Vector2(x, 50));
        }
        for (float z = -44; z < 50; z += 6)
        {
            cdt.AddVertex(new Vector2(-100, z));
            cdt.AddVertex(new Vector2(100, z));
        }

        for (float x = -94; x < 100; x += 12)
            for (float z = -44; z < 50; z += 12)
                cdt.AddVertex(new Vector2(x, z));

        void AddBuilding(float bx, float bz, float bw, float bh)
        {
            cdt.AddVertex(new Vector2(bx, bz));
            cdt.AddVertex(new Vector2(bx + bw, bz));
            cdt.AddVertex(new Vector2(bx + bw, bz + bh));
            cdt.AddVertex(new Vector2(bx, bz + bh));
        }
        AddBuilding(-19, -19, 4, 6);
        AddBuilding(-89, -9, 6, 18);

        cdt.Triangulate();

        void InsertBuildingConstraints(float bx, float bz, float bw, float bh)
        {
            int bl = cdt.AddVertex(new Vector2(bx, bz));
            int br = cdt.AddVertex(new Vector2(bx + bw, bz));
            int tr = cdt.AddVertex(new Vector2(bx + bw, bz + bh));
            int tl = cdt.AddVertex(new Vector2(bx, bz + bh));
            cdt.InsertConstraint(bl, br);
            cdt.InsertConstraint(br, tr);
            cdt.InsertConstraint(tr, tl);
            cdt.InsertConstraint(tl, bl);
        }
        InsertBuildingConstraints(-19, -19, 4, 6);
        InsertBuildingConstraints(-89, -9, 6, 18);

        Assert.AreEqual(0, cdt.ConstraintsFailed,
            $"Exact game failure coordinates should succeed with flip fallback, but {cdt.ConstraintsFailed} failed");
    }

    [Test]
    public void CDT_DenseBuildings_ConstraintsSucceedWithFlipFallback()
    {
        // Stress test: many buildings close together to force walk failures
        // that require the edge-flip fallback.
        var cdt = new CDTriangulator();

        cdt.AddVertex(new Vector2(-50, -50));
        cdt.AddVertex(new Vector2(50, -50));
        cdt.AddVertex(new Vector2(50, 50));
        cdt.AddVertex(new Vector2(-50, 50));

        for (float x = -44; x < 50; x += 6)
        {
            cdt.AddVertex(new Vector2(x, -50));
            cdt.AddVertex(new Vector2(x, 50));
        }
        for (float z = -44; z < 50; z += 6)
        {
            cdt.AddVertex(new Vector2(-50, z));
            cdt.AddVertex(new Vector2(50, z));
        }

        var buildings = new (float x, float z, float w, float h)[]
        {
            (-40, -40, 4, 6),
            (-40, -30, 4, 6),
            (-40, -16, 6, 8),
            (-30, -40, 6, 6),
            (-30, -28, 4, 4),
            (10, 10, 6, 6),
            (10, 20, 4, 8),
            (20, 10, 8, 4),
            (-10, 30, 4, 6),
            (-10, 38, 4, 6),
        };

        foreach (var (bx, bz, bw, bh) in buildings)
        {
            cdt.AddVertex(new Vector2(bx, bz));
            cdt.AddVertex(new Vector2(bx + bw, bz));
            cdt.AddVertex(new Vector2(bx + bw, bz + bh));
            cdt.AddVertex(new Vector2(bx, bz + bh));
        }

        cdt.Triangulate();

        foreach (var (bx, bz, bw, bh) in buildings)
        {
            int bl = cdt.AddVertex(new Vector2(bx, bz));
            int br = cdt.AddVertex(new Vector2(bx + bw, bz));
            int tr = cdt.AddVertex(new Vector2(bx + bw, bz + bh));
            int tl = cdt.AddVertex(new Vector2(bx, bz + bh));
            cdt.InsertConstraint(bl, br);
            cdt.InsertConstraint(br, tr);
            cdt.InsertConstraint(tr, tl);
            cdt.InsertConstraint(tl, bl);
        }

        Assert.AreEqual(0, cdt.ConstraintsFailed,
            $"Dense buildings should all succeed with flip fallback, but {cdt.ConstraintsFailed} failed");
    }

    [Test]
    public void CDT_FlipFallback_ForcedByDestroyedTopology()
    {
        // Minimal test that forces the walk to fail by inserting constraints
        // that destroy the local topology before the next constraint is processed.
        var cdt = new CDTriangulator();

        cdt.AddVertex(new Vector2(0, 0));
        cdt.AddVertex(new Vector2(20, 0));
        cdt.AddVertex(new Vector2(20, 20));
        cdt.AddVertex(new Vector2(0, 20));

        // Two buildings sharing the y=10 line — sequential constraint
        // insertion for building A modifies triangles that building B needs.
        cdt.AddVertex(new Vector2(2, 8));
        cdt.AddVertex(new Vector2(6, 8));
        cdt.AddVertex(new Vector2(6, 12));
        cdt.AddVertex(new Vector2(2, 12));

        cdt.AddVertex(new Vector2(8, 8));
        cdt.AddVertex(new Vector2(12, 8));
        cdt.AddVertex(new Vector2(12, 12));
        cdt.AddVertex(new Vector2(8, 12));

        cdt.Triangulate();

        // Insert building A constraints
        int a0 = cdt.AddVertex(new Vector2(2, 8));
        int a1 = cdt.AddVertex(new Vector2(6, 8));
        int a2 = cdt.AddVertex(new Vector2(6, 12));
        int a3 = cdt.AddVertex(new Vector2(2, 12));
        cdt.InsertConstraint(a0, a1);
        cdt.InsertConstraint(a1, a2);
        cdt.InsertConstraint(a2, a3);
        cdt.InsertConstraint(a3, a0);

        // Insert building B constraints — topology is now modified
        int b0 = cdt.AddVertex(new Vector2(8, 8));
        int b1 = cdt.AddVertex(new Vector2(12, 8));
        int b2 = cdt.AddVertex(new Vector2(12, 12));
        int b3 = cdt.AddVertex(new Vector2(8, 12));
        cdt.InsertConstraint(b0, b1);
        cdt.InsertConstraint(b1, b2);
        cdt.InsertConstraint(b2, b3);
        cdt.InsertConstraint(b3, b0);

        Assert.AreEqual(0, cdt.ConstraintsFailed,
            $"Adjacent buildings should succeed, but {cdt.ConstraintsFailed} failed");
    }

    // ================================================================
    //  #2: GetPortalEdge — test via BuildPortals
    // ================================================================

    [Test]
    public void BuildPortals_AdjacentTriangles_ProducesValidPortals()
    {
        var mesh = BuildSimpleMesh(20f, 20f);
        int tri0 = mesh.FindTriangleAtPosition(new Vector2(5f, 5f));
        Assert.GreaterOrEqual(tri0, 0);

        // Find a neighbor
        int neighbor = mesh.Triangles[tri0].N0;
        if (neighbor < 0) neighbor = mesh.Triangles[tri0].N1;
        if (neighbor < 0) neighbor = mesh.Triangles[tri0].N2;
        if (neighbor < 0)
        {
            Assert.Inconclusive("No neighbor found — mesh may have only one triangle");
            return;
        }

        var channel = new List<int> { tri0, neighbor };
        var portals = NavMeshPathfinder.BuildPortals(mesh, channel,
            mesh.GetCentroid(tri0), mesh.GetCentroid(neighbor));

        Assert.AreEqual(3, portals.Count, "Should have start + portal + goal");
        // Portal between tri0 and neighbor should not be centroids
        var mid = portals[1];
        Assert.AreNotEqual(mid.left, mid.right, "Portal should not be degenerate");
    }

    // ================================================================
    //  #5: AttackPositionFinder — no relaxed fallback
    // ================================================================

    [Test]
    public void AttackPosition_NoValidPosition_ReturnsFalse()
    {
        // Create a grid where all cells around target are unwalkable
        var grid = new TestGrid(10, 10, 1f, Vector3.zero);
        for (int x = 0; x < 10; x++)
            for (int y = 0; y < 10; y++)
                grid.SetWalkable(new Vector2Int(x, y), false);

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            new Vector3(5f, 0f, 5f), 1f,
            2f, 0.5f, false,
            Vector3.zero, 1, 100);

        Assert.IsFalse(result.found, "Should return found=false when no walkable cells");
    }

    [Test]
    public void AttackPosition_ValidPosition_ReturnsTrue()
    {
        var grid = new TestGrid(10, 10, 1f, Vector3.zero);
        // All walkable by default

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            new Vector3(5f, 0f, 5f), 1f,
            2f, 0.5f, false,
            new Vector3(0f, 0f, 5f), 1, 100);

        Assert.IsTrue(result.found, "Should find a valid position in open field");
    }

    // ================================================================
    //  #9: Funnel cap reduced to *3
    // ================================================================

    [Test]
    public void Funnel_StraightCorridor_CompletesWithinCap()
    {
        // Build a straight corridor of portals
        var portals = new List<(Vector2 left, Vector2 right)>();
        portals.Add((new Vector2(0, 0), new Vector2(0, 0)));
        for (int i = 1; i <= 10; i++)
            portals.Add((new Vector2(i, -1), new Vector2(i, 1)));
        portals.Add((new Vector2(11, 0), new Vector2(11, 0)));

        var path = NavMeshPathfinder.FunnelFromPortals(portals);

        Assert.IsNotNull(path);
        Assert.GreaterOrEqual(path.Count, 2, "Should produce at least start and goal");
    }

    // ================================================================
    //  #16: NavMeshData spiral search
    // ================================================================

    [Test]
    public void FindTriangleAtPosition_NearbyPosition_FoundWithoutBrute()
    {
        var mesh = BuildSimpleMesh(20f, 20f);
        // Position inside the mesh should be found
        int tri = mesh.FindTriangleAtPosition(new Vector2(5f, 5f));
        Assert.GreaterOrEqual(tri, 0, "Should find triangle for position inside mesh");
    }

    // ================================================================
    //  #29: ValidateMesh — brokenAdjacency
    // ================================================================

    [Test]
    public void ValidateMesh_Healthy_ReturnsTrue()
    {
        var mesh = BuildSimpleMesh(10f, 10f);
        Assert.IsTrue(mesh.ValidateMesh(), "Clean mesh should be healthy");
    }

    [Test]
    public void ValidateMesh_BrokenAdjacency_ReturnsFalse()
    {
        // Create two adjacent triangles, then corrupt adjacency so one doesn't point back
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));   // v0
        mesh.AddVertex(new Vector2(10f, 0f));  // v1
        mesh.AddVertex(new Vector2(0f, 10f));  // v2
        mesh.AddVertex(new Vector2(10f, 10f)); // v3

        mesh.AddTriangle(0, 1, 2, walkable: true);  // t0
        mesh.AddTriangle(1, 3, 2, walkable: true);  // t1

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();

        // Corrupt: t0 says neighbor is t1 on edge 1, but t1 does not point back to t0
        mesh.Triangles[1].N0 = -1;
        mesh.Triangles[1].N1 = -1;
        mesh.Triangles[1].N2 = -1;

        Assert.IsFalse(mesh.ValidateMesh(), "Mesh with broken adjacency should fail validation");
    }

    // ================================================================
    //  HELPERS
    // ================================================================

    private static NavMeshData BuildSimpleMesh(float width, float height)
    {
        var cdt = new CDTriangulator();
        cdt.AddVertex(new Vector2(0, 0));
        cdt.AddVertex(new Vector2(width, 0));
        cdt.AddVertex(new Vector2(width, height));
        cdt.AddVertex(new Vector2(0, height));

        // Add interior points for better triangulation
        float spacing = Mathf.Min(width, height) / 3f;
        for (float x = spacing; x < width; x += spacing)
            for (float y = spacing; y < height; y += spacing)
                cdt.AddVertex(new Vector2(x, y));

        cdt.Triangulate();
        var mesh = cdt.BuildNavMesh(pos => true, 1f, Vector2.zero);
        mesh.BuildSpatialGrid(2f);
        return mesh;
    }
}

/// <summary>
/// Simple test grid implementation for unit testing.
/// </summary>
public class TestGrid : IGrid
{
    private readonly bool[,] walkable;
    private readonly int width, height;
    private readonly float cellSize;
    private readonly Vector3 origin;

    public TestGrid(int w, int h, float cs, Vector3 org)
    {
        width = w;
        height = h;
        cellSize = cs;
        origin = org;
        walkable = new bool[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                walkable[x, y] = true;
    }

    public void SetWalkable(Vector2Int cell, bool val)
    {
        if (cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height)
            walkable[cell.x, cell.y] = val;
    }

    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;
    public Vector3 GridOrigin => origin;

    public bool IsWalkable(Vector2Int cell)
    {
        if (!IsInBounds(cell)) return false;
        return walkable[cell.x, cell.y];
    }

    public bool IsInBounds(Vector2Int cell) =>
        cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;

    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        int x = Mathf.RoundToInt((worldPos.x - origin.x) / cellSize);
        int y = Mathf.RoundToInt((worldPos.z - origin.z) / cellSize);
        return new Vector2Int(x, y);
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        return new Vector3(origin.x + cell.x * cellSize, origin.y, origin.z + cell.y * cellSize);
    }

    public bool HasLineOfSight(Vector2Int from, Vector2Int to) => true;

    public Vector3 FindNearestWalkablePosition(Vector3 pos, Vector3 fallback)
    {
        return pos;
    }
}
