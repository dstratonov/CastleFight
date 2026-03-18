using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Tests that paths computed through a grid don't cross building cells.
/// This catches the runtime bug: "pathCrossesWall=True" where NavMesh
/// computes a path through occupied grid cells.
/// </summary>
[TestFixture]
public class PathValidityTests
{
    private GridLogic grid;

    [SetUp]
    public void SetUp()
    {
        grid = new GridLogic(100, 100, 2f, new Vector3(-100f, 0f, -100f));
    }

    // ================================================================
    //  Path waypoints must be on walkable cells
    // ================================================================

    [Test]
    public void FindNearestWalkable_ReturnsWalkableCell()
    {
        Bounds building = new Bounds(Vector3.zero, new Vector3(10f, 4f, 10f));
        var cells = grid.GetCellsOverlappingBounds(building);
        foreach (var c in cells)
            grid.SetCell(c, CellState.Building);

        Vector3 insideBuilding = Vector3.zero;
        Vector3 reference = new Vector3(20f, 0f, 0f);

        Vector3 result = grid.FindNearestWalkablePosition(insideBuilding, reference);
        Vector2Int resultCell = grid.WorldToCell(result);

        Assert.IsTrue(grid.IsWalkable(resultCell),
            $"FindNearestWalkable returned cell {resultCell} which is NOT walkable");
    }

    [Test]
    public void FindNearestWalkable_NeverReturnsInsideBuilding()
    {
        Bounds building = new Bounds(new Vector3(10f, 0f, 10f), new Vector3(8f, 4f, 8f));
        var cells = grid.GetCellsOverlappingBounds(building);
        foreach (var c in cells)
            grid.SetCell(c, CellState.Building);

        for (int trial = 0; trial < 10; trial++)
        {
            Vector3 query = new Vector3(
                10f + (trial - 5) * 1f,
                0f,
                10f + (trial - 5) * 1f
            );
            Vector3 reference = new Vector3(30f, 0f, 0f);
            Vector3 result = grid.FindNearestWalkablePosition(query, reference);
            Vector2Int resultCell = grid.WorldToCell(result);

            Assert.IsTrue(grid.IsWalkable(resultCell),
                $"Trial {trial}: FindNearestWalkable returned unwalkable cell {resultCell}");
        }
    }

    // ================================================================
    //  GridCells from bounds correctness
    // ================================================================

    [Test]
    public void GetCellsOverlappingBounds_MatchesPhysicalFootprint()
    {
        Bounds footprint = new Bounds(Vector3.zero, new Vector3(10f, 4f, 10f));
        var cells = grid.GetCellsOverlappingBounds(footprint);

        Assert.Greater(cells.Count, 0);

        foreach (var cell in cells)
        {
            Vector3 worldPos = grid.CellToWorld(cell);
            Assert.IsTrue(footprint.Contains(new Vector3(worldPos.x, footprint.center.y, worldPos.z)),
                $"Cell {cell} at world {worldPos} should be inside bounds {footprint}");
        }
    }

    [Test]
    public void GetCellsOverlappingBounds_NoCellsOutsideFootprint()
    {
        Bounds footprint = new Bounds(Vector3.zero, new Vector3(6f, 4f, 6f));
        var cells = grid.GetCellsOverlappingBounds(footprint);

        foreach (var cell in cells)
        {
            Vector3 worldPos = grid.CellToWorld(cell);
            bool inside = worldPos.x >= footprint.min.x && worldPos.x <= footprint.max.x
                       && worldPos.z >= footprint.min.z && worldPos.z <= footprint.max.z;
            Assert.IsTrue(inside,
                $"Cell {cell} at world {worldPos} is outside footprint bounds {footprint}");
        }
    }

    // ================================================================
    //  Building placement blocks grid
    // ================================================================

    [Test]
    public void PlacedBuilding_CellsUnwalkable()
    {
        Bounds footprint = new Bounds(new Vector3(20f, 0f, 20f), new Vector3(8f, 4f, 8f));
        var cells = grid.GetCellsOverlappingBounds(footprint);

        foreach (var c in cells)
            grid.SetCell(c, CellState.Building);

        foreach (var c in cells)
        {
            Assert.IsFalse(grid.IsWalkable(c),
                $"Cell {c} inside building footprint must be unwalkable");
        }
    }

    [Test]
    public void PlacedBuilding_AdjacentCellsStillWalkable()
    {
        Vector3 buildingPos = new Vector3(20f, 0f, 20f);
        Bounds footprint = new Bounds(buildingPos, new Vector3(6f, 4f, 6f));
        var cells = grid.GetCellsOverlappingBounds(footprint);

        foreach (var c in cells)
            grid.SetCell(c, CellState.Building);

        Vector2Int buildingCell = grid.WorldToCell(buildingPos);
        Vector2Int testCell = new Vector2Int(buildingCell.x + 5, buildingCell.y);

        if (grid.IsInBounds(testCell))
        {
            Assert.IsTrue(grid.IsWalkable(testCell),
                "Cell well outside building should remain walkable");
        }
    }

    // ================================================================
    //  LineOfSight through buildings
    // ================================================================

    [Test]
    public void LineOfSight_BlockedByBuilding()
    {
        Vector3 buildingPos = new Vector3(0f, 0f, 0f);
        Bounds footprint = new Bounds(buildingPos, new Vector3(8f, 4f, 8f));
        var cells = grid.GetCellsOverlappingBounds(footprint);
        foreach (var c in cells)
            grid.SetCell(c, CellState.Building);

        Vector2Int from = grid.WorldToCell(new Vector3(-20f, 0f, 0f));
        Vector2Int to = grid.WorldToCell(new Vector3(20f, 0f, 0f));

        Assert.IsFalse(grid.HasLineOfSight(from, to),
            "LOS through a building should be blocked");
    }

    [Test]
    public void LineOfSight_ClearAroundBuilding()
    {
        Vector3 buildingPos = new Vector3(0f, 0f, 0f);
        Bounds footprint = new Bounds(buildingPos, new Vector3(8f, 4f, 8f));
        var cells = grid.GetCellsOverlappingBounds(footprint);
        foreach (var c in cells)
            grid.SetCell(c, CellState.Building);

        Vector2Int from = grid.WorldToCell(new Vector3(-20f, 0f, 20f));
        Vector2Int to = grid.WorldToCell(new Vector3(20f, 0f, 20f));

        Assert.IsTrue(grid.HasLineOfSight(from, to),
            "LOS around building (well above it in Z) should be clear");
    }

    // ================================================================
    //  Path invalidation with building bounds
    // ================================================================

    [Test]
    public void PathInvalidation_PathThroughNewBuilding_Detected()
    {
        var waypoints = new List<Vector3>
        {
            new Vector3(-20f, 0f, 0f),
            new Vector3(0f, 0f, 0f),
            new Vector3(20f, 0f, 0f),
        };

        Bounds newBuilding = new Bounds(Vector3.zero, new Vector3(6f, 4f, 6f));

        Assert.IsTrue(PathInvalidation.PathIntersectsRegion(waypoints, newBuilding),
            "Path going through newly placed building should be invalidated");
    }

    [Test]
    public void PathInvalidation_PathAroundBuilding_NotInvalidated()
    {
        var waypoints = new List<Vector3>
        {
            new Vector3(-20f, 0f, 20f),
            new Vector3(0f, 0f, 20f),
            new Vector3(20f, 0f, 20f),
        };

        Bounds newBuilding = new Bounds(Vector3.zero, new Vector3(6f, 4f, 6f));

        Assert.IsFalse(PathInvalidation.PathIntersectsRegion(waypoints, newBuilding),
            "Path going well around building should not be invalidated");
    }

    // ================================================================
    //  ValidatePosition — wall sliding
    // ================================================================

    [Test]
    public void ValidatePosition_IntoBuilding_SlidesAlongWall()
    {
        Vector3 buildingPos = new Vector3(10f, 0f, 0f);
        Bounds footprint = new Bounds(buildingPos, new Vector3(6f, 4f, 6f));
        var cells = grid.GetCellsOverlappingBounds(footprint);
        foreach (var c in cells)
            grid.SetCell(c, CellState.Building);

        Vector3 oldPos = new Vector3(6f, 0f, 0f);
        Vector3 newPos = new Vector3(10f, 0f, 0f);

        Vector3 result = grid.ValidatePosition(oldPos, newPos, new Vector3(1f, 0f, 0f));
        Vector2Int resultCell = grid.WorldToCell(result);

        Assert.IsTrue(grid.IsWalkable(resultCell),
            $"ValidatePosition must return a walkable cell, got {resultCell}");
    }

    [Test]
    public void ValidatePosition_FreeSpace_NoChange()
    {
        Vector3 oldPos = new Vector3(0f, 0f, 0f);
        Vector3 newPos = new Vector3(2f, 0f, 0f);

        Vector3 result = grid.ValidatePosition(oldPos, newPos, Vector3.right);

        Assert.AreEqual(newPos.x, result.x, 0.01f);
        Assert.AreEqual(newPos.z, result.z, 0.01f);
    }

    // ================================================================
    //  Corridor width checks
    // ================================================================

    [Test]
    public void NarrowCorridor_ClearanceMapDetectsIt()
    {
        for (int z = 0; z < 100; z++)
        {
            grid.SetCell(new Vector2Int(48, z), CellState.Building);
            grid.SetCell(new Vector2Int(52, z), CellState.Building);
        }

        var clearance = new ClearanceMap();
        clearance.ComputeFull(new GridAdapter(grid));

        float corridorClearance = clearance.GetClearance(new Vector2Int(50, 50));

        Assert.Greater(corridorClearance, 0f, "Corridor center should have positive clearance");
        Assert.Less(corridorClearance, 6f, "Narrow corridor (4 cells wide) should have limited clearance");

        Assert.IsTrue(clearance.CanPass(new Vector2Int(50, 50), 1f),
            "Small unit (r=1) should pass through 4-cell corridor");
    }

    private class GridAdapter : IGrid
    {
        private readonly GridLogic logic;

        public GridAdapter(GridLogic g) => logic = g;

        public int Width => logic.Width;
        public int Height => logic.Height;
        public float CellSize => logic.CellSize;
        public Vector3 GridOrigin => logic.Origin;

        public bool IsWalkable(Vector2Int cell) => logic.IsWalkable(cell);
        public bool IsInBounds(Vector2Int cell) => logic.IsInBounds(cell);
        public Vector2Int WorldToCell(Vector3 pos) => logic.WorldToCell(pos);
        public Vector3 CellToWorld(Vector2Int cell) => logic.CellToWorld(cell);
        public Vector3 FindNearestWalkablePosition(Vector3 desired, Vector3 reference)
            => logic.FindNearestWalkablePosition(desired, reference);
        public bool HasLineOfSight(Vector2Int from, Vector2Int to)
            => logic.HasLineOfSight(from, to);
    }
}
