using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Regression tests for the "paths go through buildings" bug.
/// Verifies that after NavMesh rebuild, no path segment crosses through
/// a building's footprint. Tests at multiple levels:
/// - NavMeshBuilder full pipeline (grid + building bounds)
/// - CDT-only pipeline (direct constraints)
/// - Segment-rect intersection geometry
/// </summary>
[TestFixture]
public class PathThroughBuildingTests
{
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

        public bool IsWalkable(Vector2Int cell)
        {
            if (!IsInBounds(cell)) return false;
            return walkable[cell.x, cell.y];
        }

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

    /// <summary>
    /// Liang-Barsky line clipping: returns true if any part of the segment
    /// (p1→p2) lies inside the rectangle (including touching edges/corners).
    /// </summary>
    private static bool SegmentCrossesRect(Vector2 p1, Vector2 p2, Rect rect)
    {
        float dx = p2.x - p1.x;
        float dy = p2.y - p1.y;

        float tMin = 0f, tMax = 1f;

        float[] p = { -dx, dx, -dy, dy };
        float[] q = { p1.x - rect.xMin, rect.xMax - p1.x, p1.y - rect.yMin, rect.yMax - p1.y };

        for (int i = 0; i < 4; i++)
        {
            if (Mathf.Abs(p[i]) < 1e-10f)
            {
                if (q[i] < 0f) return false;
            }
            else
            {
                float t = q[i] / p[i];
                if (p[i] < 0f) tMin = Mathf.Max(tMin, t);
                else tMax = Mathf.Min(tMax, t);
            }
        }

        return tMin <= tMax;
    }

    private void AssertPathDoesNotCrossRect(List<Vector2> path, Rect buildingRect, string context)
    {
        for (int i = 0; i < path.Count - 1; i++)
        {
            Assert.IsFalse(SegmentCrossesRect(path[i], path[i + 1], buildingRect),
                $"[{context}] Path segment [{i}] ({path[i]:F1}) → ({path[i + 1]:F1}) " +
                $"crosses through building rect {buildingRect}");
        }
    }

    // ================================================================
    //  NAVMESHBUILDER PIPELINE TESTS
    //  Uses the real NavMeshBuilder with grid + building bounds
    // ================================================================

    [Test]
    public void NavMeshBuilder_PathDoesNotCrossBuilding_CenterObstacle()
    {
        var grid = new FakeGrid(40, 40, 1f);
        for (int x = 15; x < 25; x++)
            for (int y = 15; y < 25; y++)
                grid.SetUnwalkable(x, y);

        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);
        builder.RegisterBuilding(1, new Bounds(
            new Vector3(20f, 0f, 20f), new Vector3(10f, 1f, 10f)));
        builder.Rebuild();

        var mesh = builder.ActiveNavMesh;
        Assert.IsNotNull(mesh, "NavMesh should build successfully");

        Vector2 start = new Vector2(5f, 20f);
        Vector2 goal = new Vector2(35f, 20f);
        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0.5f);
        Assert.IsNotNull(path, "Path around building should exist");

        Rect buildingInterior = new Rect(15.5f, 15.5f, 9f, 9f);
        AssertPathDoesNotCrossRect(path, buildingInterior, "center-obstacle");
    }

    [Test]
    public void NavMeshBuilder_PathDoesNotCrossBuilding_WallShape()
    {
        // Long narrow building like a castle wall (matches the observed bug)
        var grid = new FakeGrid(50, 30, 1f);
        for (int x = 15; x < 35; x++)
            for (int y = 12; y < 18; y++)
                grid.SetUnwalkable(x, y);

        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);
        builder.RegisterBuilding(1, new Bounds(
            new Vector3(25f, 0f, 15f), new Vector3(20f, 1f, 6f)));
        builder.Rebuild();

        var mesh = builder.ActiveNavMesh;
        Assert.IsNotNull(mesh);

        Vector2 start = new Vector2(5f, 15f);
        Vector2 goal = new Vector2(45f, 15f);
        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0.5f);
        Assert.IsNotNull(path, "Path around wall should exist");

        Rect wallInterior = new Rect(16f, 12.5f, 18f, 5f);
        AssertPathDoesNotCrossRect(path, wallInterior, "wall-shape");
    }

    /// <summary>
    /// Tests the scenario where grid-marked building cells extend beyond the
    /// registered building bounds. This happens when
    /// Building.FitFootprintCollider uses MaxFootprintScale = 0.85, making
    /// the collider 15% smaller than the visual model. The grid cells are
    /// marked from the full visual bounds, but the NavMesh obstacle uses the
    /// smaller collider bounds.
    ///
    /// NavMeshBuilder.IsPositionWalkable returns true for positions outside
    /// building bounds even if the grid says unwalkable (line 492), creating
    /// "leaky" walkable triangles at building edges.
    /// </summary>
    [Test]
    public void NavMeshBuilder_PathDoesNotCrossGridCells_WhenBoundsAreShrunk()
    {
        var grid = new FakeGrid(40, 40, 1f);

        // Full visual building: cells 12..27 (16x16 = 16 units)
        for (int x = 12; x < 28; x++)
            for (int y = 12; y < 28; y++)
                grid.SetUnwalkable(x, y);

        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);

        // Register with SHRUNK bounds (85% of visual = 13.6x13.6)
        // Visual center = (20, 20), shrunk to 13.6x13.6
        float fullSize = 16f;
        float shrunkSize = fullSize * 0.85f; // 13.6
        builder.RegisterBuilding(1, new Bounds(
            new Vector3(20f, 0f, 20f),
            new Vector3(shrunkSize, 1f, shrunkSize)));
        builder.Rebuild();

        var mesh = builder.ActiveNavMesh;
        Assert.IsNotNull(mesh);

        Vector2 start = new Vector2(5f, 20f);
        Vector2 goal = new Vector2(35f, 20f);
        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0.5f);
        Assert.IsNotNull(path, "Path around building should exist");

        // Check against the GRID footprint (what the player actually sees)
        // Grid cells 12..27 → world bounds roughly (11.5, 11.5) to (27.5, 27.5)
        // Inset by 1 unit to be generous at edges
        Rect gridFootprint = new Rect(13f, 13f, 14f, 14f);
        AssertPathDoesNotCrossRect(path, gridFootprint, "shrunk-bounds");
    }

    /// <summary>
    /// Tests that no walkable triangle in the NavMesh has its centroid inside
    /// the building bounds. Catches "leaky" triangles that A* can traverse.
    /// </summary>
    [Test]
    public void NavMeshBuilder_NoWalkableTriangleCentroid_InsideBuilding()
    {
        var grid = new FakeGrid(40, 40, 1f);
        for (int x = 15; x < 25; x++)
            for (int y = 15; y < 25; y++)
                grid.SetUnwalkable(x, y);

        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);
        builder.RegisterBuilding(1, new Bounds(
            new Vector3(20f, 0f, 20f), new Vector3(10f, 1f, 10f)));
        builder.Rebuild();

        var mesh = builder.ActiveNavMesh;
        Assert.IsNotNull(mesh);

        Rect buildingRect = new Rect(15.5f, 15.5f, 9f, 9f);

        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            if (!mesh.Triangles[t].IsWalkable) continue;

            ref var tri = ref mesh.Triangles[t];
            Vector2 centroid = (mesh.Vertices[tri.V0] + mesh.Vertices[tri.V1] + mesh.Vertices[tri.V2]) / 3f;

            Assert.IsFalse(buildingRect.Contains(centroid),
                $"Walkable triangle {t} has centroid ({centroid:F1}) inside building bounds");
        }
    }

    // ================================================================
    //  MULTIPLE BUILDINGS — paths between gaps
    // ================================================================

    [Test]
    public void NavMeshBuilder_PathBetweenTwoBuildings_DoesNotCrossEither()
    {
        var grid = new FakeGrid(40, 40, 1f);

        // Building A: cells 8..15, 15..24 (left building)
        for (int x = 8; x < 16; x++)
            for (int y = 15; y < 25; y++)
                grid.SetUnwalkable(x, y);

        // Building B: cells 24..31, 15..24 (right building, 8-unit gap)
        for (int x = 24; x < 32; x++)
            for (int y = 15; y < 25; y++)
                grid.SetUnwalkable(x, y);

        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);
        builder.RegisterBuilding(1, new Bounds(
            new Vector3(12f, 0f, 20f), new Vector3(8f, 1f, 10f)));
        builder.RegisterBuilding(2, new Bounds(
            new Vector3(28f, 0f, 20f), new Vector3(8f, 1f, 10f)));
        builder.Rebuild();

        var mesh = builder.ActiveNavMesh;
        Assert.IsNotNull(mesh);

        // Path through the gap between the two buildings
        Vector2 start = new Vector2(5f, 20f);
        Vector2 goal = new Vector2(35f, 20f);
        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0.5f);
        Assert.IsNotNull(path, "Path through gap between buildings should exist");

        Rect buildingA = new Rect(8.5f, 15.5f, 7f, 9f);
        Rect buildingB = new Rect(24.5f, 15.5f, 7f, 9f);
        AssertPathDoesNotCrossRect(path, buildingA, "two-buildings-A");
        AssertPathDoesNotCrossRect(path, buildingB, "two-buildings-B");
    }

    // ================================================================
    //  DIAGONAL PATH ACROSS BUILDING CORNER
    // ================================================================

    [Test]
    public void NavMeshBuilder_DiagonalPath_DoesNotCutThroughBuildingCorner()
    {
        var grid = new FakeGrid(40, 40, 1f);
        for (int x = 15; x < 25; x++)
            for (int y = 15; y < 25; y++)
                grid.SetUnwalkable(x, y);

        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);
        builder.RegisterBuilding(1, new Bounds(
            new Vector3(20f, 0f, 20f), new Vector3(10f, 1f, 10f)));
        builder.Rebuild();

        var mesh = builder.ActiveNavMesh;
        Assert.IsNotNull(mesh);

        // Diagonal path that would cut through the building corner
        Vector2 start = new Vector2(5f, 5f);
        Vector2 goal = new Vector2(35f, 35f);
        var path = NavMeshPathfinder.FindPath(mesh, start, goal, 0.5f);
        Assert.IsNotNull(path, "Diagonal path should exist");

        Rect buildingInterior = new Rect(15.5f, 15.5f, 9f, 9f);
        AssertPathDoesNotCrossRect(path, buildingInterior, "diagonal-corner");
    }

    // ================================================================
    //  STALE NAVMESH — path computed before building exists
    //  This is the actual bug: units replan on the old NavMesh before
    //  the async rebuild completes, producing paths through buildings.
    // ================================================================

    /// <summary>
    /// Reproduces the core bug: a path computed on a NavMesh that was built
    /// BEFORE a building was placed will go straight through the building.
    ///
    /// Verifies that NavMeshBuilder.PathCrossesAnyBuilding catches
    /// stale-mesh paths — this is the safety net used by
    /// PathfindingManager.RequestPath to reject bad paths.
    /// </summary>
    [Test]
    public void StaleMesh_PathCrossesBuilding_IsDetectedByValidator()
    {
        var grid = new FakeGrid(40, 40, 1f);
        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);
        var staleMesh = builder.ActiveNavMesh;
        Assert.IsNotNull(staleMesh);

        Vector2 start = new Vector2(5f, 20f);
        Vector2 goal = new Vector2(35f, 20f);
        var stalePath = NavMeshPathfinder.FindPath(staleMesh, start, goal, 0.5f);
        Assert.IsNotNull(stalePath, "Path on stale mesh should exist");

        // Building is placed AFTER the path was computed — simulates the stale window
        builder.RegisterBuilding(1, new Bounds(
            new Vector3(20f, 0f, 20f), new Vector3(10f, 1f, 10f)));

        // The validator must catch stale paths that cross registered buildings
        Assert.IsTrue(builder.PathCrossesAnyBuilding(stalePath),
            "PathCrossesAnyBuilding must detect stale path through building");
    }

    /// <summary>
    /// After a proper NavMesh rebuild with the building, the new path
    /// goes around and the validator confirms it is clean.
    /// </summary>
    [Test]
    public void FreshMesh_PathAroundBuilding_PassesValidator()
    {
        var grid = new FakeGrid(40, 40, 1f);
        for (int x = 15; x < 25; x++)
            for (int y = 15; y < 25; y++)
                grid.SetUnwalkable(x, y);

        var builder = new NavMeshBuilder();
        builder.BuildBase(grid);
        builder.RegisterBuilding(1, new Bounds(
            new Vector3(20f, 0f, 20f), new Vector3(10f, 1f, 10f)));
        builder.Rebuild();

        var mesh = builder.ActiveNavMesh;
        Assert.IsNotNull(mesh);

        Vector2 start = new Vector2(5f, 20f);
        Vector2 goal = new Vector2(35f, 20f);
        var freshPath = NavMeshPathfinder.FindPath(mesh, start, goal, 0.5f);
        Assert.IsNotNull(freshPath, "Path around building should exist");

        Assert.IsFalse(builder.PathCrossesAnyBuilding(freshPath),
            "Path on rebuilt mesh must not cross building");
    }

    // ================================================================
    //  SEGMENT-RECT INTERSECTION HELPER VALIDATION
    // ================================================================

    [Test]
    public void SegmentCrossesRect_BasicCases()
    {
        Rect r = new Rect(5, 5, 10, 10); // (5,5) to (15,15)

        Assert.IsTrue(SegmentCrossesRect(new Vector2(0, 10), new Vector2(20, 10), r),
            "Horizontal line through center should cross");
        Assert.IsTrue(SegmentCrossesRect(new Vector2(10, 0), new Vector2(10, 20), r),
            "Vertical line through center should cross");
        Assert.IsTrue(SegmentCrossesRect(new Vector2(0, 0), new Vector2(20, 20), r),
            "Diagonal through rect should cross");

        Assert.IsFalse(SegmentCrossesRect(new Vector2(0, 0), new Vector2(4, 0), r),
            "Segment entirely outside should not cross");
        Assert.IsFalse(SegmentCrossesRect(new Vector2(0, 0), new Vector2(0, 20), r),
            "Segment along left edge (outside) should not cross");

        Assert.IsTrue(SegmentCrossesRect(new Vector2(10, 10), new Vector2(20, 20), r),
            "Segment starting inside should cross");
    }
}
