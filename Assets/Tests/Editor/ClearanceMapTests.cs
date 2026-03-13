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
}
