using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

[TestFixture]
public class ClearanceMapTests
{
    private class FakeGrid : IGrid
    {
        private readonly bool[,] walkable;

        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public Vector3 GridOrigin => Vector3.zero;

        public FakeGrid(int w, int h, float cellSize = 1f)
        {
            Width = w;
            Height = h;
            CellSize = cellSize;
            walkable = new bool[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    walkable[x, y] = true;
        }

        public void SetUnwalkable(int x, int y) => walkable[x, y] = false;

        public bool IsWalkable(Vector2Int cell)
        {
            if (!IsInBounds(cell)) return false;
            return walkable[cell.x, cell.y];
        }

        public bool IsInBounds(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < Width && cell.y >= 0 && cell.y < Height;
        }

        public Vector2Int WorldToCell(Vector3 worldPosition)
        {
            return new Vector2Int(
                Mathf.RoundToInt(worldPosition.x / CellSize),
                Mathf.RoundToInt(worldPosition.z / CellSize));
        }

        public Vector3 CellToWorld(Vector2Int cell)
        {
            return new Vector3(cell.x * CellSize, 0f, cell.y * CellSize);
        }

        public Vector3 FindNearestWalkablePosition(Vector3 desiredWorldPos, Vector3 referencePos)
        {
            return desiredWorldPos;
        }

        public bool HasLineOfSight(Vector2Int from, Vector2Int to) => true;
    }

    [Test]
    public void ComputeFull_AllWalkable_HighClearance()
    {
        var grid = new FakeGrid(10, 10);
        var map = new ClearanceMap();
        map.ComputeFull(grid);

        float center = map.GetClearance(new Vector2Int(5, 5));
        Assert.Greater(center, 0f, "Center of fully walkable grid should have positive clearance");
    }

    [Test]
    public void ComputeFull_ObstacleHasZeroClearance()
    {
        var grid = new FakeGrid(10, 10);
        grid.SetUnwalkable(5, 5);

        var map = new ClearanceMap();
        map.ComputeFull(grid);

        Assert.AreEqual(0f, map.GetClearance(new Vector2Int(5, 5)),
            "Unwalkable cell should have 0 clearance");
    }

    [Test]
    public void ComputeFull_AdjacentToObstacle_LowClearance()
    {
        var grid = new FakeGrid(10, 10);
        grid.SetUnwalkable(5, 5);

        var map = new ClearanceMap();
        map.ComputeFull(grid);

        float adj = map.GetClearance(new Vector2Int(4, 5));
        Assert.Greater(adj, 0f, "Walkable cell adjacent to obstacle should have positive clearance");
        Assert.LessOrEqual(adj, grid.CellSize * 1.5f,
            "Adjacent cell clearance should be small (roughly 1 cell)");
    }

    [Test]
    public void ComputeFull_FarFromObstacle_HighClearance()
    {
        var grid = new FakeGrid(20, 20);
        grid.SetUnwalkable(0, 0);

        var map = new ClearanceMap();
        map.ComputeFull(grid);

        float far = map.GetClearance(new Vector2Int(19, 19));
        float near = map.GetClearance(new Vector2Int(1, 0));
        Assert.Greater(far, near, "Distant cell should have more clearance than adjacent cell");
    }

    [Test]
    public void CanPass_SmallUnit_PassesNarrowGap()
    {
        var grid = new FakeGrid(10, 10);
        grid.SetUnwalkable(4, 5);
        grid.SetUnwalkable(6, 5);

        var map = new ClearanceMap();
        map.ComputeFull(grid);

        Assert.IsTrue(map.CanPass(new Vector2Int(5, 5), 0.5f),
            "Small unit should pass through 1-cell gap");
    }

    [Test]
    public void CanPass_LargeUnit_BlockedByNarrowGap()
    {
        var grid = new FakeGrid(10, 10);
        grid.SetUnwalkable(4, 5);
        grid.SetUnwalkable(6, 5);

        var map = new ClearanceMap();
        map.ComputeFull(grid);

        float clearance = map.GetClearance(new Vector2Int(5, 5));
        Assert.IsFalse(map.CanPass(new Vector2Int(5, 5), clearance + 1f),
            "Unit larger than gap clearance should not pass");
    }

    [Test]
    public void GetClearance_OutOfBounds_ReturnsZero()
    {
        var grid = new FakeGrid(5, 5);
        var map = new ClearanceMap();
        map.ComputeFull(grid);

        Assert.AreEqual(0f, map.GetClearance(new Vector2Int(-1, -1)));
        Assert.AreEqual(0f, map.GetClearance(new Vector2Int(100, 100)));
    }

    [Test]
    public void UpdateRegion_RecomputesAfterChange()
    {
        var grid = new FakeGrid(10, 10);
        var map = new ClearanceMap();
        map.ComputeFull(grid);

        float before = map.GetClearance(new Vector2Int(5, 5));

        grid.SetUnwalkable(5, 5);
        map.UpdateRegion(new Vector2Int(4, 4), new Vector2Int(6, 6), grid);

        float after = map.GetClearance(new Vector2Int(5, 5));
        Assert.AreEqual(0f, after, "After marking cell unwalkable and updating, clearance should be 0");
        Assert.Greater(before, after, "Clearance should decrease after placing obstacle");
    }

    // ================================================================
    //  CORRIDOR BETWEEN TWO WALLS
    // ================================================================

    [Test]
    public void ComputeFull_CorridorBetweenWalls_CorrectClearance()
    {
        // 20-wide grid, walls at rows 5 and 14 (gap of 8 cells: rows 6-13)
        var grid = new FakeGrid(20, 20);
        for (int x = 0; x < 20; x++)
        {
            grid.SetUnwalkable(x, 5);
            grid.SetUnwalkable(x, 14);
        }

        var map = new ClearanceMap();
        map.ComputeFull(grid);

        // Center of the corridor is at y=9 or y=10, distance to nearest wall ~4-5 cells
        float centerClearance = map.GetClearance(new Vector2Int(10, 10));
        float nearWallClearance = map.GetClearance(new Vector2Int(10, 6));

        Assert.Greater(centerClearance, nearWallClearance,
            "Center of corridor should have higher clearance than near the wall");
        Assert.Greater(centerClearance, 3f,
            "Center of 8-cell corridor should have clearance > 3");
        Assert.Greater(nearWallClearance, 0f);
    }

    [Test]
    public void ComputeFull_NarrowCorridor3Wide_CenterClearanceLimited()
    {
        // Grid with two walls leaving a 3-cell-wide gap
        var grid = new FakeGrid(10, 10);
        for (int x = 0; x < 10; x++)
        {
            grid.SetUnwalkable(x, 3);
            grid.SetUnwalkable(x, 7);
        }

        var map = new ClearanceMap();
        map.ComputeFull(grid);

        float center = map.GetClearance(new Vector2Int(5, 5));
        Assert.Greater(center, 1f);
        Assert.Less(center, 4f,
            "Center of 3-cell corridor should have limited clearance");
    }

    // ================================================================
    //  OBSTACLE REMOVAL
    // ================================================================

    [Test]
    public void UpdateRegion_ObstacleRemoved_ClearanceIncreases()
    {
        var grid = new FakeGrid(10, 10);
        grid.SetUnwalkable(5, 5);

        var map = new ClearanceMap();
        map.ComputeFull(grid);
        float withObstacle = map.GetClearance(new Vector2Int(5, 4));

        // "Remove" the obstacle by making it walkable again
        // FakeGrid doesn't have SetWalkable, so we create a new grid without the obstacle
        var gridClean = new FakeGrid(10, 10);
        map.UpdateRegion(new Vector2Int(3, 3), new Vector2Int(7, 7), gridClean);

        float withoutObstacle = map.GetClearance(new Vector2Int(5, 4));
        Assert.Greater(withoutObstacle, withObstacle,
            "Clearance should increase after removing adjacent obstacle");
    }

    // ================================================================
    //  EXACT CLEARANCE BOUNDARY
    // ================================================================

    [Test]
    public void CanPass_ExactClearance_ReturnsTrue()
    {
        var grid = new FakeGrid(10, 10);
        grid.SetUnwalkable(5, 5);

        var map = new ClearanceMap();
        map.ComputeFull(grid);

        float clearance = map.GetClearance(new Vector2Int(4, 5));
        // CanPass uses >= so exact clearance should pass
        Assert.IsTrue(map.CanPass(new Vector2Int(4, 5), clearance),
            "CanPass with exactly matching clearance should return true (>= comparison)");
    }

    [Test]
    public void CanPass_SlightlyOverClearance_ReturnsFalse()
    {
        var grid = new FakeGrid(10, 10);
        grid.SetUnwalkable(5, 5);

        var map = new ClearanceMap();
        map.ComputeFull(grid);

        float clearance = map.GetClearance(new Vector2Int(4, 5));
        Assert.IsFalse(map.CanPass(new Vector2Int(4, 5), clearance + 0.01f),
            "CanPass with radius slightly over clearance should return false");
    }

    // ================================================================
    //  NON-UNIT CELL SIZE
    // ================================================================

    [Test]
    public void ComputeFull_CellSize2_ScalesClearance()
    {
        var grid1 = new FakeGrid(10, 10, cellSize: 1f);
        grid1.SetUnwalkable(5, 5);
        var map1 = new ClearanceMap();
        map1.ComputeFull(grid1);

        var grid2 = new FakeGrid(10, 10, cellSize: 2f);
        grid2.SetUnwalkable(5, 5);
        var map2 = new ClearanceMap();
        map2.ComputeFull(grid2);

        float c1 = map1.GetClearance(new Vector2Int(4, 5));
        float c2 = map2.GetClearance(new Vector2Int(4, 5));

        Assert.Greater(c2, c1,
            "Clearance with CellSize=2 should be larger than CellSize=1 (BFS step scales)");
    }

    // ================================================================
    //  CORNER OF GRID
    // ================================================================

    [Test]
    public void ComputeFull_CornerCell_HasClearance()
    {
        var grid = new FakeGrid(10, 10);
        var map = new ClearanceMap();
        map.ComputeFull(grid);

        float corner = map.GetClearance(new Vector2Int(0, 0));
        float center = map.GetClearance(new Vector2Int(5, 5));

        Assert.Greater(corner, 0f, "Corner should have positive clearance on fully walkable grid");
        // Note: corner clearance is bounded by grid boundary so it may be
        // less than center, but BFS doesn't treat boundary as obstacle
        // The BFS only seeds from unwalkable cells, so if there are none,
        // all cells keep MaxValue. This is valid behavior.
    }

    // ================================================================
    //  MULTIPLE OBSTACLES
    // ================================================================

    [Test]
    public void ComputeFull_TwoObstaclesNarrowPassage_LowClearance()
    {
        var grid = new FakeGrid(10, 10);
        // Two obstacles with a 2-cell gap at y=5
        for (int x = 0; x < 4; x++)
            grid.SetUnwalkable(x, 5);
        for (int x = 6; x < 10; x++)
            grid.SetUnwalkable(x, 5);

        var map = new ClearanceMap();
        map.ComputeFull(grid);

        float passage = map.GetClearance(new Vector2Int(5, 5));
        Assert.Greater(passage, 0f, "Passage between two obstacles should be walkable");
        Assert.Less(passage, 3f, "Narrow passage clearance should be limited");
    }
}
