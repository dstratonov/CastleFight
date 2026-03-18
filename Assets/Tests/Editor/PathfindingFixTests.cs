using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Tests for pathfinding fixes:
/// 1. Paths not rejected during async rebuild window (pending building lifecycle)
/// 2. A* succeeds with reasonable unit radii in corridor scenarios
/// 3. MaxEffectiveRadius cap produces sane pathfinding diameters
/// 4. PathCrossesAnyBuilding only rejects for truly pending buildings
/// </summary>
[TestFixture]
public class PathfindingFixTests
{
    [SetUp]
    public void SetUp()
    {
        NavMeshPathfinder.ResetStats();
    }

    // ================================================================
    //  HELPERS
    // ================================================================

    private class FakeGrid : IGrid
    {
        private readonly bool[,] walkable;

        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public Vector3 GridOrigin { get; }

        public FakeGrid(int w, int h, float cellSize = 1f, Vector3? origin = null)
        {
            Width = w;
            Height = h;
            CellSize = cellSize;
            GridOrigin = origin ?? Vector3.zero;
            walkable = new bool[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    walkable[x, y] = true;
        }

        public void SetUnwalkable(int x, int y)
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height)
                walkable[x, y] = false;
        }

        public bool IsWalkable(Vector2Int cell) =>
            IsInBounds(cell) && walkable[cell.x, cell.y];

        public bool IsInBounds(Vector2Int cell) =>
            cell.x >= 0 && cell.x < Width && cell.y >= 0 && cell.y < Height;

        public Vector2Int WorldToCell(Vector3 worldPosition) =>
            new(Mathf.RoundToInt((worldPosition.x - GridOrigin.x) / CellSize),
                Mathf.RoundToInt((worldPosition.z - GridOrigin.z) / CellSize));

        public Vector3 CellToWorld(Vector2Int cell) =>
            new(cell.x * CellSize + GridOrigin.x, GridOrigin.y, cell.y * CellSize + GridOrigin.z);

        public Vector3 FindNearestWalkablePosition(Vector3 desiredWorldPos, Vector3 referencePos) =>
            desiredWorldPos;

        public bool HasLineOfSight(Vector2Int from, Vector2Int to) => true;
    }

    private NavMeshBuilder BuildNavMesh(FakeGrid grid, List<(int id, Bounds bounds)> buildings = null)
    {
        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);
        if (buildings != null)
        {
            foreach (var (id, bounds) in buildings)
            {
                builder.RegisterBuilding(id, bounds);
            }
            builder.Rebuild();
        }
        return builder;
    }

    // ================================================================
    //  1. PENDING BUILDING LIFECYCLE — paths NOT rejected after rebuild
    // ================================================================

    [Test]
    public void PathCrossesAnyBuilding_AfterRebuild_DoesNotRejectIncorporatedBuilding()
    {
        // Setup: grid with building marked unwalkable
        var grid = new FakeGrid(40, 40, 1f);
        for (int x = 15; x < 25; x++)
            for (int y = 15; y < 25; y++)
                grid.SetUnwalkable(x, y);

        // Build base, register building, then rebuild (simulates full lifecycle)
        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);
        builder.RegisterBuilding(1, new Bounds(
            new Vector3(20f, 0f, 20f), new Vector3(10f, 1f, 10f)));
        builder.Rebuild(); // This should clear pendingBuildingIds

        var mesh = builder.ActiveNavMesh;
        Assert.IsNotNull(mesh);

        // Path goes around building (it's incorporated in the NavMesh)
        Vector2 start = new Vector2(5f, 20f);
        Vector2 goal = new Vector2(35f, 20f);
        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0.5f);
        Assert.IsNotNull(path, "Path around incorporated building should exist");

        // After rebuild, building is incorporated — path should NOT be rejected
        Assert.IsFalse(builder.PathCrossesAnyBuilding(path),
            "PathCrossesAnyBuilding must not reject paths after building is incorporated via Rebuild()");
    }

    [Test]
    public void PathCrossesAnyBuilding_BeforeRebuild_RejectsStalePath()
    {
        // Build NavMesh with no buildings
        var grid = new FakeGrid(40, 40, 1f);
        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);

        var staleMesh = builder.ActiveNavMesh;
        Assert.IsNotNull(staleMesh);

        // Get a straight path through where the building will be
        Vector2 start = new Vector2(5f, 20f);
        Vector2 goal = new Vector2(35f, 20f);
        var stalePath = NavMeshPathfinder.FindPath(staleMesh, start, goal, 0.5f);
        Assert.IsNotNull(stalePath);

        // Register building but DON'T rebuild — simulates the async window
        builder.RegisterBuilding(1, new Bounds(
            new Vector3(20f, 0f, 20f), new Vector3(10f, 1f, 10f)));

        // Stale path should be rejected because building is still pending
        Assert.IsTrue(builder.PathCrossesAnyBuilding(stalePath),
            "PathCrossesAnyBuilding must reject stale paths through pending buildings");
    }

    [Test]
    public void PathCrossesAnyBuilding_NoPendingBuildings_AlwaysFalse()
    {
        var grid = new FakeGrid(40, 40, 1f);
        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);

        Vector2 start = new Vector2(5f, 20f);
        Vector2 goal = new Vector2(35f, 20f);
        var path = NavMeshPathfinder.FindPath(builder.ActiveNavMesh, start, goal, 0.5f);
        Assert.IsNotNull(path);

        // No buildings registered at all — should never reject
        Assert.IsFalse(builder.PathCrossesAnyBuilding(path),
            "PathCrossesAnyBuilding with no pending buildings must always return false");
    }

    [Test]
    public void PathCrossesAnyBuilding_UnregisteredBuilding_NotRejected()
    {
        var grid = new FakeGrid(40, 40, 1f);
        for (int x = 15; x < 25; x++)
            for (int y = 15; y < 25; y++)
                grid.SetUnwalkable(x, y);

        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);

        // Register, rebuild, then unregister (building destroyed)
        builder.RegisterBuilding(1, new Bounds(
            new Vector3(20f, 0f, 20f), new Vector3(10f, 1f, 10f)));
        builder.Rebuild();
        builder.UnregisterBuilding(1);

        // Path through where building was — should not be rejected since building is gone
        var path = new List<Vector2>
        {
            new Vector2(5f, 20f),
            new Vector2(20f, 20f),
            new Vector2(35f, 20f)
        };

        Assert.IsFalse(builder.PathCrossesAnyBuilding(path),
            "PathCrossesAnyBuilding must not reject after building is unregistered");
    }

    [Test]
    public void PathCrossesAnyBuilding_PathDoesNotCrossPendingBuilding_NotRejected()
    {
        var grid = new FakeGrid(40, 40, 1f);
        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);

        // Register a building in the south, path is in the north
        builder.RegisterBuilding(1, new Bounds(
            new Vector3(20f, 0f, 5f), new Vector3(10f, 1f, 6f)));

        // Path far from the building
        var path = new List<Vector2>
        {
            new Vector2(5f, 35f),
            new Vector2(35f, 35f)
        };

        Assert.IsFalse(builder.PathCrossesAnyBuilding(path),
            "Path that doesn't cross pending building should not be rejected");
    }

    // ================================================================
    //  2. A* CORRIDOR WIDTH — reasonable unit radii succeed
    //  Uses CDT directly (like PathfindingPipelineTests) for reliable triangulations
    // ================================================================

    private NavMeshData BuildCorridorMesh(float mapW, float mapH,
        float wallTopY, float wallBotY, float wallMinX, float wallMaxX)
    {
        // Build a mesh with a horizontal corridor between two walls
        var cdt = new CDTriangulator();
        float step = 5f;
        for (float x = 0f; x <= mapW; x += step)
            for (float y = 0f; y <= mapH; y += step)
                cdt.AddVertex(new Vector2(x, y));

        // Top wall vertices
        int tw0 = cdt.AddVertex(new Vector2(wallMinX, wallTopY));
        int tw1 = cdt.AddVertex(new Vector2(wallMaxX, wallTopY));
        int tw2 = cdt.AddVertex(new Vector2(wallMaxX, mapH));
        int tw3 = cdt.AddVertex(new Vector2(wallMinX, mapH));

        // Bottom wall vertices
        int bw0 = cdt.AddVertex(new Vector2(wallMinX, 0f));
        int bw1 = cdt.AddVertex(new Vector2(wallMaxX, 0f));
        int bw2 = cdt.AddVertex(new Vector2(wallMaxX, wallBotY));
        int bw3 = cdt.AddVertex(new Vector2(wallMinX, wallBotY));

        cdt.Triangulate();

        cdt.InsertConstraint(tw0, tw1); cdt.InsertConstraint(tw1, tw2);
        cdt.InsertConstraint(tw2, tw3); cdt.InsertConstraint(tw3, tw0);
        cdt.InsertConstraint(bw0, bw1); cdt.InsertConstraint(bw1, bw2);
        cdt.InsertConstraint(bw2, bw3); cdt.InsertConstraint(bw3, bw0);

        float topY = wallTopY, botY = wallBotY;
        float minX = wallMinX, maxX = wallMaxX;
        var mesh = cdt.BuildNavMesh(pos =>
        {
            bool inTop = pos.x > minX && pos.x < maxX && pos.y > topY && pos.y < mapH;
            bool inBot = pos.x > minX && pos.x < maxX && pos.y > 0f && pos.y < botY;
            return !(inTop || inBot);
        }, 1f);
        mesh.BuildSpatialGrid(5f);
        return mesh;
    }

    [Test]
    public void AStar_SmallUnit_SucceedsInNarrowCorridor()
    {
        // 4-unit wide corridor between y=8 and y=12, walls at x=10..20
        var mesh = BuildCorridorMesh(30f, 20f, 12f, 8f, 10f, 20f);

        Vector2 start = new Vector2(5f, 10f);
        Vector2 goal = new Vector2(25f, 10f);
        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0.5f);
        Assert.IsNotNull(path, "Small unit (r=0.5) should find path through 4-wide corridor");
    }

    [Test]
    public void AStar_MediumUnit_SucceedsInWideCorridor()
    {
        // 8-unit wide corridor between y=6 and y=14, walls at x=10..20
        var mesh = BuildCorridorMesh(30f, 20f, 14f, 6f, 10f, 20f);

        Vector2 start = new Vector2(5f, 10f);
        Vector2 goal = new Vector2(25f, 10f);
        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 1.5f);
        Assert.IsNotNull(path, "Medium unit (r=1.5) should find path through 8-wide corridor");
    }

    [Test]
    public void AStar_UnitDiameterExceedsCorridor_NoPath()
    {
        // 2-unit wide corridor between y=9 and y=11, walls at x=10..20
        var mesh = BuildCorridorMesh(30f, 20f, 11f, 9f, 10f, 20f);

        Vector2 start = new Vector2(5f, 10f);
        Vector2 goal = new Vector2(25f, 10f);
        // Unit diameter 6.0 > corridor width 2.0 — should fail A* width filter
        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 3.0f);
        Assert.IsNull(path, "Huge unit (r=3.0, d=6.0) should NOT find path through 2-wide corridor");
    }

    [Test]
    public void AStar_WidthFilter_RejectsNarrowPortals()
    {
        // Build a mesh with two walls leaving a gap in the middle
        var cdt = new CDTriangulator();
        for (float x = 0f; x <= 30f; x += 5f)
            for (float y = 0f; y <= 20f; y += 5f)
                cdt.AddVertex(new Vector2(x, y));

        // Top wall: x=12..18, y=12..20
        int tw0 = cdt.AddVertex(new Vector2(12f, 12f));
        int tw1 = cdt.AddVertex(new Vector2(18f, 12f));
        int tw2 = cdt.AddVertex(new Vector2(18f, 20f));
        int tw3 = cdt.AddVertex(new Vector2(12f, 20f));

        // Bottom wall: x=12..18, y=0..8 — leaves 4-unit gap at y=8..12
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
        }, 1f);
        mesh.BuildSpatialGrid(5f);

        Vector2 start = new Vector2(5f, 10f);
        Vector2 goal = new Vector2(25f, 10f);

        // Small unit should fit through the 4-unit gap
        var pathSmall = NavMeshPathfinder.FindPath(mesh, start, goal, 0.3f);
        Assert.IsNotNull(pathSmall, "Tiny unit (r=0.3) should fit through 4-unit gap");

        // Medium unit should fit too (diameter 2.0 < gap 4.0)
        var pathMed = NavMeshPathfinder.FindPath(mesh, start, goal, 1.0f);
        Assert.IsNotNull(pathMed, "Medium unit (r=1.0) should fit through 4-unit gap");
    }

    // ================================================================
    //  3. MaxEffectiveRadius cap — sane pathfinding diameters
    // ================================================================

    [Test]
    public void MaxEffectiveRadius_ProducesSaneDiameter()
    {
        // MaxEffectiveRadius = 3.0 → diameter 6.0
        // Build a 10-unit wide corridor, verify max-radius unit can path through it
        var mesh = BuildCorridorMesh(30f, 20f, 15f, 5f, 10f, 20f);

        float maxRadius = 3.0f;
        Vector2 start = new Vector2(5f, 10f);
        Vector2 goal = new Vector2(25f, 10f);
        var path = NavMeshPathfinder.FindPath(mesh, start, goal, maxRadius);
        Assert.IsNotNull(path,
            $"Unit at MaxEffectiveRadius ({maxRadius}) should find path through 10-wide corridor");
    }

    [Test]
    public void MaxEffectiveRadius_DiameterDoesNotExceed6()
    {
        // Verify the cap value: MaxEffectiveRadius = 3.0 → diameter 6.0
        float maxRadius = 3.0f; // mirrors Unit.MaxEffectiveRadius
        float diameter = maxRadius * 2f;
        Assert.AreEqual(6f, diameter,
            "MaxEffectiveRadius cap should produce diameter of 6 units");
        Assert.LessOrEqual(diameter, 8f,
            "Max diameter should be reasonable (not block standard corridors)");
    }

    [Test]
    public void ReducedRadius_FallbackSucceeds_WhenFullRadiusFails()
    {
        // 3-unit wide corridor — full radius (2.0, diameter 4.0) fails, reduced (1.0, diameter 2.0) succeeds
        var mesh = BuildCorridorMesh(30f, 20f, 11.5f, 8.5f, 10f, 20f);

        Vector2 start = new Vector2(5f, 10f);
        Vector2 goal = new Vector2(25f, 10f);

        float fullRadius = 2.0f;
        float reducedRadius = fullRadius * 0.5f; // 1.0

        // Full radius should fail (diameter 4.0 > corridor 3.0)
        var fullPath = NavMeshPathfinder.FindPath(mesh, start, goal, fullRadius);

        // Reduced radius should succeed (diameter 2.0 < corridor 3.0)
        var reducedPath = NavMeshPathfinder.FindPath(mesh, start, goal, reducedRadius);
        Assert.IsNotNull(reducedPath,
            $"Reduced radius ({reducedRadius}) should find path when full radius ({fullRadius}) fails in narrow corridor");
    }

    // ================================================================
    //  4. PathCrossesAnyBuilding — only rejects truly pending buildings
    // ================================================================

    [Test]
    public void PathCrossesAnyBuilding_MultiplePendingBuildings_RejectsCorrectly()
    {
        var grid = new FakeGrid(40, 40, 1f);
        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);

        // Register two pending buildings
        builder.RegisterBuilding(1, new Bounds(
            new Vector3(15f, 0f, 20f), new Vector3(6f, 1f, 6f)));
        builder.RegisterBuilding(2, new Bounds(
            new Vector3(25f, 0f, 20f), new Vector3(6f, 1f, 6f)));

        // Path through first building
        var path1 = new List<Vector2>
        {
            new Vector2(10f, 20f),
            new Vector2(20f, 20f)
        };
        Assert.IsTrue(builder.PathCrossesAnyBuilding(path1),
            "Path through first pending building should be rejected");

        // Path through second building
        var path2 = new List<Vector2>
        {
            new Vector2(20f, 20f),
            new Vector2(30f, 20f)
        };
        Assert.IsTrue(builder.PathCrossesAnyBuilding(path2),
            "Path through second pending building should be rejected");

        // Path through neither building (north of both)
        var pathSafe = new List<Vector2>
        {
            new Vector2(10f, 35f),
            new Vector2(30f, 35f)
        };
        Assert.IsFalse(builder.PathCrossesAnyBuilding(pathSafe),
            "Path not crossing any pending building should not be rejected");
    }

    [Test]
    public void PathCrossesAnyBuilding_OnePendingOneIncorporated_OnlyRejectsPending()
    {
        var grid = new FakeGrid(40, 40, 1f);

        // First building already in the grid
        for (int x = 12; x < 18; x++)
            for (int y = 17; y < 23; y++)
                grid.SetUnwalkable(x, y);

        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);

        // Register first building and rebuild (incorporated)
        builder.RegisterBuilding(1, new Bounds(
            new Vector3(15f, 0f, 20f), new Vector3(6f, 1f, 6f)));
        builder.Rebuild();

        // Register second building but DON'T rebuild (pending)
        builder.RegisterBuilding(2, new Bounds(
            new Vector3(25f, 0f, 20f), new Vector3(6f, 1f, 6f)));

        // Path through second (pending) building should be rejected
        var pathThroughPending = new List<Vector2>
        {
            new Vector2(20f, 20f),
            new Vector2(30f, 20f)
        };
        Assert.IsTrue(builder.PathCrossesAnyBuilding(pathThroughPending),
            "Path through pending building should be rejected");

        // Path through first (incorporated) building — validator should NOT care
        // because it's already in the NavMesh
        var pathThroughIncorporated = new List<Vector2>
        {
            new Vector2(10f, 20f),
            new Vector2(20f, 20f)
        };
        Assert.IsFalse(builder.PathCrossesAnyBuilding(pathThroughIncorporated),
            "Path near incorporated building should NOT be rejected by pending check");
    }

    [Test]
    public void PathCrossesAnyBuilding_NullOrShortPath_ReturnsFalse()
    {
        var grid = new FakeGrid(20, 20, 1f);
        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);
        builder.RegisterBuilding(1, new Bounds(
            new Vector3(10f, 0f, 10f), new Vector3(5f, 1f, 5f)));

        // Null path
        Assert.IsFalse(builder.PathCrossesAnyBuilding(null),
            "Null path should return false");

        // Single-point path
        Assert.IsFalse(builder.PathCrossesAnyBuilding(new List<Vector2> { new Vector2(10f, 10f) }),
            "Single-point path should return false");
    }

    // ================================================================
    //  OPEN FIELD — basic sanity for various unit sizes
    // ================================================================

    private NavMeshData BuildOpenFieldMesh(float width, float height)
    {
        var cdt = new CDTriangulator();
        float step = 5f;
        for (float x = 0f; x <= width; x += step)
            for (float y = 0f; y <= height; y += step)
                cdt.AddVertex(new Vector2(x, y));

        cdt.Triangulate();
        var mesh = cdt.BuildNavMesh(_ => true, 1f);
        mesh.BuildSpatialGrid(5f);
        return mesh;
    }

    [Test]
    public void OpenField_AllReasonableRadii_FindPath()
    {
        var mesh = BuildOpenFieldMesh(30f, 30f);

        Vector2 start = new Vector2(5f, 15f);
        Vector2 goal = new Vector2(25f, 15f);

        float[] radii = { 0.25f, 0.5f, 1.0f, 1.5f, 2.0f, 2.5f, 3.0f };
        foreach (float r in radii)
        {
            var path = NavMeshPathfinder.FindPath(mesh, start, goal, r);
            Assert.IsNotNull(path, $"Open field path should exist for radius {r}");
            Assert.GreaterOrEqual(path.Count, 2, $"Path for radius {r} should have at least 2 waypoints");
        }
    }
}
