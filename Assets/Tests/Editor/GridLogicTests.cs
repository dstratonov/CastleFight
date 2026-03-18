using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

[TestFixture]
public class GridLogicTests
{
    private GridLogic grid;

    private const int GridW = 10;
    private const int GridH = 10;
    private const float Cell = 2f;
    private static readonly Vector3 Origin = new(-10f, 0f, -10f);

    [SetUp]
    public void SetUp()
    {
        grid = new GridLogic(GridW, GridH, Cell, Origin);
    }

    // ================================================================
    //  WorldToCell / CellToWorld roundtrip
    // ================================================================

    [Test]
    public void WorldToCell_Origin_ReturnsZeroZero()
    {
        Vector2Int cell = grid.WorldToCell(Origin);
        Assert.AreEqual(new Vector2Int(0, 0), cell);
    }

    [Test]
    public void CellToWorld_ZeroZero_ReturnsOrigin()
    {
        Vector3 world = grid.CellToWorld(new Vector2Int(0, 0));
        Assert.AreEqual(Origin.x, world.x, 0.001f);
        Assert.AreEqual(Origin.y, world.y, 0.001f);
        Assert.AreEqual(Origin.z, world.z, 0.001f);
    }

    [Test]
    public void WorldToCell_CellToWorld_Roundtrip()
    {
        Vector2Int cell = new(5, 7);
        Vector3 world = grid.CellToWorld(cell);
        Vector2Int back = grid.WorldToCell(world);
        Assert.AreEqual(cell, back);
    }

    [Test]
    public void WorldToCell_OffsetPosition()
    {
        Vector3 pos = new(-10f + 3 * 2f, 0f, -10f + 4 * 2f);
        Vector2Int cell = grid.WorldToCell(pos);
        Assert.AreEqual(new Vector2Int(3, 4), cell);
    }

    // ================================================================
    //  SnapToGrid
    // ================================================================

    [Test]
    public void SnapToGrid_ExactCellCenter_ReturnsSame()
    {
        Vector3 pos = grid.CellToWorld(new Vector2Int(3, 3));
        Vector3 snapped = grid.SnapToGrid(pos);
        Assert.AreEqual(pos.x, snapped.x, 0.001f);
        Assert.AreEqual(pos.z, snapped.z, 0.001f);
    }

    [Test]
    public void SnapToGrid_OffCenter_SnapsToNearest()
    {
        Vector3 pos = new(-10f + 3 * 2f + 0.3f, 5f, -10f + 4 * 2f - 0.2f);
        Vector3 snapped = grid.SnapToGrid(pos);
        Vector3 expected = grid.CellToWorld(new Vector2Int(3, 4));
        Assert.AreEqual(expected.x, snapped.x, 0.001f);
        Assert.AreEqual(expected.z, snapped.z, 0.001f);
    }

    // ================================================================
    //  IsInBounds
    // ================================================================

    [Test]
    public void IsInBounds_InsideGrid_True()
    {
        Assert.IsTrue(grid.IsInBounds(new Vector2Int(5, 5)));
    }

    [Test]
    public void IsInBounds_EdgeCells_True()
    {
        Assert.IsTrue(grid.IsInBounds(new Vector2Int(0, 0)));
        Assert.IsTrue(grid.IsInBounds(new Vector2Int(9, 9)));
        Assert.IsTrue(grid.IsInBounds(new Vector2Int(0, 9)));
        Assert.IsTrue(grid.IsInBounds(new Vector2Int(9, 0)));
    }

    [Test]
    public void IsInBounds_OutOfBounds_False()
    {
        Assert.IsFalse(grid.IsInBounds(new Vector2Int(-1, 0)));
        Assert.IsFalse(grid.IsInBounds(new Vector2Int(0, -1)));
        Assert.IsFalse(grid.IsInBounds(new Vector2Int(10, 0)));
        Assert.IsFalse(grid.IsInBounds(new Vector2Int(0, 10)));
    }

    // ================================================================
    //  Cell state — consolidated
    // ================================================================

    [Test]
    public void CellState_DefaultEmpty_SetBuilding_OutOfBoundsIsBuilding()
    {
        Assert.AreEqual(CellState.Empty, grid.GetCellState(new Vector2Int(3, 3)));
        Assert.IsTrue(grid.IsWalkable(new Vector2Int(5, 5)));

        grid.SetCell(new Vector2Int(3, 3), CellState.Building);
        Assert.AreEqual(CellState.Building, grid.GetCellState(new Vector2Int(3, 3)));
        Assert.IsFalse(grid.IsWalkable(new Vector2Int(3, 3)));

        grid.SetCell(new Vector2Int(3, 3), CellState.Empty);
        Assert.AreEqual(CellState.Empty, grid.GetCellState(new Vector2Int(3, 3)));

        Assert.AreEqual(CellState.Building, grid.GetCellState(new Vector2Int(-1, -1)));
        Assert.IsFalse(grid.IsWalkable(new Vector2Int(-1, 0)));
        Assert.DoesNotThrow(() => grid.SetCell(new Vector2Int(-1, -1), CellState.Building));
    }

    // ================================================================
    //  GetWalkableNeighbors
    // ================================================================

    [Test]
    public void GetWalkableNeighbors_Center_Returns8()
    {
        var neighbors = grid.GetWalkableNeighbors(new Vector2Int(5, 5));
        Assert.AreEqual(8, neighbors.Count);
    }

    [Test]
    public void GetWalkableNeighbors_Corner_Returns3()
    {
        var neighbors = grid.GetWalkableNeighbors(new Vector2Int(0, 0));
        Assert.AreEqual(3, neighbors.Count);
        Assert.Contains(new Vector2Int(1, 0), neighbors);
        Assert.Contains(new Vector2Int(0, 1), neighbors);
        Assert.Contains(new Vector2Int(1, 1), neighbors);
    }

    [Test]
    public void GetWalkableNeighbors_NearObstacle_ExcludesBlocked()
    {
        grid.SetCell(new Vector2Int(5, 6), CellState.Building);
        grid.SetCell(new Vector2Int(6, 5), CellState.Building);
        var neighbors = grid.GetWalkableNeighbors(new Vector2Int(5, 5));
        Assert.IsFalse(neighbors.Contains(new Vector2Int(5, 6)));
        Assert.IsFalse(neighbors.Contains(new Vector2Int(6, 5)));
        Assert.AreEqual(6, neighbors.Count);
    }

    [Test]
    public void GetAdjacentCells_IncludesUnwalkable_UnlikeWalkableNeighbors()
    {
        grid.SetCell(new Vector2Int(5, 6), CellState.Building);
        var adj = grid.GetAdjacentCells(new Vector2Int(5, 5));
        Assert.AreEqual(8, adj.Count);
        Assert.Contains(new Vector2Int(5, 6), adj);

        var walkable = grid.GetWalkableNeighbors(new Vector2Int(5, 5));
        Assert.IsFalse(walkable.Contains(new Vector2Int(5, 6)),
            "GetWalkableNeighbors must exclude buildings; GetAdjacentCells must include them");
    }

    // ================================================================
    //  HasWalkableNeighbor
    // ================================================================

    [Test]
    public void HasWalkableNeighbor_OpenGrid_True()
    {
        Assert.IsTrue(grid.HasWalkableNeighbor(new Vector2Int(5, 5)));
    }

    [Test]
    public void HasWalkableNeighbor_AllBlocked_False()
    {
        Vector2Int center = new(5, 5);
        Vector2Int[] dirs =
        {
            new(0, 1), new(0, -1), new(1, 0), new(-1, 0),
            new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)
        };
        foreach (var d in dirs)
            grid.SetCell(center + d, CellState.Building);

        Assert.IsFalse(grid.HasWalkableNeighbor(center));
    }

    // ================================================================
    //  AreCellsEmpty
    // ================================================================

    [Test]
    public void AreCellsEmpty_AllEmpty_True()
    {
        var cells = new List<Vector2Int>
        {
            new(1, 1), new(2, 2), new(3, 3)
        };
        Assert.IsTrue(grid.AreCellsEmpty(cells));
    }

    [Test]
    public void AreCellsEmpty_OneBuilding_False()
    {
        grid.SetCell(new Vector2Int(2, 2), CellState.Building);
        var cells = new List<Vector2Int>
        {
            new(1, 1), new(2, 2), new(3, 3)
        };
        Assert.IsFalse(grid.AreCellsEmpty(cells));
    }

    [Test]
    public void AreCellsEmpty_OutOfBounds_False()
    {
        var cells = new List<Vector2Int>
        {
            new(1, 1), new(-1, 0)
        };
        Assert.IsFalse(grid.AreCellsEmpty(cells));
    }

    // ================================================================
    //  GetCellsOverlappingBounds
    // ================================================================

    [Test]
    public void GetCellsOverlappingBounds_ExactSingleCell_ReturnsOneCell()
    {
        // Cell (5,5) center is at origin + (5*2, 0, 5*2) = (0, 0, 0)
        // Bounds exactly around that cell center
        var bounds = new Bounds(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        var cells = grid.GetCellsOverlappingBounds(bounds);
        Assert.AreEqual(1, cells.Count);
        Assert.Contains(new Vector2Int(5, 5), cells);
    }

    [Test]
    public void GetCellsOverlappingBounds_SmallBuilding_DoesNotInflate()
    {
        // 3x3 building at cell (5,5) = world (0,0,0)
        // bounds: (-1.5, 0, -1.5) to (1.5, 0, 1.5)
        var bounds = new Bounds(new Vector3(0, 0, 0), new Vector3(3, 1, 3));
        var cells = grid.GetCellsOverlappingBounds(bounds);

        // With cellSize=2, cell centers: (5,5)=world(0,0), (4,4)=world(-2,-2), (6,6)=world(2,2)
        // Cell (4,4) center at (-2,-2) is OUTSIDE bounds min (-1.5,-1.5) → should NOT be included
        // Cell (6,6) center at (2,2) is OUTSIDE bounds max (1.5,1.5) → should NOT be included
        // Only cell (5,5) center at (0,0) is inside bounds
        Assert.AreEqual(1, cells.Count, "3x3 building with cellSize=2 should only cover 1 cell (center)");
        Assert.Contains(new Vector2Int(5, 5), cells);
    }

    [Test]
    public void GetCellsOverlappingBounds_LargeBuilding_CorrectCellCount()
    {
        // 5x5 building centered at cell (5,5)
        // bounds: (-2.5, 0, -2.5) to (2.5, 0, 2.5)
        var bounds = new Bounds(new Vector3(0, 0, 0), new Vector3(5, 1, 5));
        var cells = grid.GetCellsOverlappingBounds(bounds);

        // Cell centers within bounds:
        // (4,4)=(-2,-2) inside bounds min=-2.5 ✓
        // (5,5)=(0,0) ✓
        // (6,6)=(2,2) inside bounds max=2.5 ✓
        // (3,3)=(-4,-4) outside ✗
        Assert.AreEqual(9, cells.Count, "5x5 building should cover 3x3=9 cells");
    }

    [Test]
    public void GetCellsOverlappingBounds_AsymmetricBounds()
    {
        // 6x2 building centered at (0,0,0)
        // bounds: (-3,0,-1) to (3,0,1)
        var bounds = new Bounds(new Vector3(0, 0, 0), new Vector3(6, 1, 2));
        var cells = grid.GetCellsOverlappingBounds(bounds);

        // X: cells 3,4,5,6,7 have centers at -4,-2,0,2,4
        //    -4 < -3 ✗, -2 >= -3 ✓, 0 ✓, 2 ✓, 4 > 3 ✗ → 3 cells (4,5,6)
        // Z: cell 5 center=0 inside -1..1 ✓, cell 4 center=-2 < -1 ✗, cell 6 center=2 > 1 ✗ → 1 cell
        Assert.AreEqual(3, cells.Count, "6x2 building: 3 cells along X, 1 along Z");
    }

    [Test]
    public void GetCellsOverlappingBounds_BuildingAtEdge_ClampedToGrid()
    {
        // Building at far corner
        var pos = grid.CellToWorld(new Vector2Int(9, 9));
        var bounds = new Bounds(pos, new Vector3(6, 1, 6));
        var cells = grid.GetCellsOverlappingBounds(bounds);

        foreach (var cell in cells)
            Assert.IsTrue(grid.IsInBounds(cell), $"Cell {cell} should be in bounds");
    }

    [Test]
    public void GetCellsOverlappingBounds_VerySmallBuilding_AtLeastOneCell()
    {
        // Building smaller than a cell, centered exactly on cell center
        var pos = grid.CellToWorld(new Vector2Int(5, 5));
        var bounds = new Bounds(pos, new Vector3(0.5f, 1, 0.5f));
        var cells = grid.GetCellsOverlappingBounds(bounds);
        Assert.GreaterOrEqual(cells.Count, 1, "A building centered on a cell center should cover at least 1 cell");
    }

    [Test]
    public void GetCellsOverlappingBounds_OldBehaviorWouldInflate()
    {
        // Regression: with the old RoundToInt code, a 2x2 building centered
        // between cell centers would include cells on both sides.
        // Bounds from (-1,0,-1) to (1,0,1) - exactly 2x2 at world origin
        var bounds = new Bounds(Vector3.zero, new Vector3(2, 1, 2));
        var cells = grid.GetCellsOverlappingBounds(bounds);

        // Cell (5,5) center at (0,0) — inside bounds ✓
        // Cell (4,4) center at (-2,-2) — outside bounds ✗
        // Old code with RoundToInt could include extra cells
        Assert.AreEqual(1, cells.Count,
            "2x2 building centered on cell (5,5) should only cover 1 cell, not inflate to 4");
    }

    // ================================================================
    //  HasLineOfSight
    // ================================================================

    [Test]
    public void HasLineOfSight_ClearPath_True()
    {
        Assert.IsTrue(grid.HasLineOfSight(new Vector2Int(0, 0), new Vector2Int(9, 0)));
    }

    [Test]
    public void HasLineOfSight_BlockedByObstacle_False()
    {
        grid.SetCell(new Vector2Int(5, 0), CellState.Building);
        Assert.IsFalse(grid.HasLineOfSight(new Vector2Int(0, 0), new Vector2Int(9, 0)));
    }

    [Test]
    public void HasLineOfSight_DiagonalBlocked_False()
    {
        grid.SetCell(new Vector2Int(3, 3), CellState.Building);
        Assert.IsFalse(grid.HasLineOfSight(new Vector2Int(1, 1), new Vector2Int(5, 5)));
    }

    [Test]
    public void HasLineOfSight_SameCell_True()
    {
        Assert.IsTrue(grid.HasLineOfSight(new Vector2Int(4, 4), new Vector2Int(4, 4)));
    }

    [Test]
    public void HasLineOfSight_AdjacentCells_True()
    {
        Assert.IsTrue(grid.HasLineOfSight(new Vector2Int(3, 3), new Vector2Int(4, 3)));
    }

    // ================================================================
    //  FindNearestWalkablePosition
    // ================================================================

    [Test]
    public void FindNearestWalkable_AlreadyWalkable_ReturnsSamePosition()
    {
        Vector3 pos = grid.CellToWorld(new Vector2Int(5, 5));
        Vector3 result = grid.FindNearestWalkablePosition(pos, Origin);
        Assert.AreEqual(pos.x, result.x, 0.001f);
        Assert.AreEqual(pos.z, result.z, 0.001f);
    }

    [Test]
    public void FindNearestWalkable_BlockedCenter_FindsNeighbor()
    {
        grid.SetCell(new Vector2Int(5, 5), CellState.Building);
        Vector3 pos = grid.CellToWorld(new Vector2Int(5, 5));
        Vector3 refPos = grid.CellToWorld(new Vector2Int(4, 5));

        Vector3 result = grid.FindNearestWalkablePosition(pos, refPos);

        Vector2Int resultCell = grid.WorldToCell(result);
        Assert.IsTrue(grid.IsWalkable(resultCell), "Result should be a walkable cell");

        int dist = Mathf.Abs(resultCell.x - 5) + Mathf.Abs(resultCell.y - 5);
        Assert.LessOrEqual(dist, 2, "Should find a neighbor within radius 1");
    }

    [Test]
    public void FindNearestWalkable_SurroundedByBuildings_SpiralSearch()
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                grid.SetCell(new Vector2Int(5 + dx, 5 + dz), CellState.Building);

        Vector3 pos = grid.CellToWorld(new Vector2Int(5, 5));
        Vector3 refPos = grid.CellToWorld(new Vector2Int(3, 5));

        Vector3 result = grid.FindNearestWalkablePosition(pos, refPos);
        Vector2Int resultCell = grid.WorldToCell(result);
        Assert.IsTrue(grid.IsWalkable(resultCell), "Should find walkable cell at radius 2");
    }

    // ================================================================
    //  ValidatePosition
    // ================================================================

    [Test]
    public void ValidatePosition_WalkableDestination_ReturnsNewPos()
    {
        Vector3 oldPos = grid.CellToWorld(new Vector2Int(3, 3));
        Vector3 newPos = grid.CellToWorld(new Vector2Int(4, 3));
        Vector3 vel = new(1f, 0f, 0f);

        Vector3 result = grid.ValidatePosition(oldPos, newPos, vel);
        Assert.AreEqual(newPos.x, result.x, 0.001f);
        Assert.AreEqual(newPos.z, result.z, 0.001f);
    }

    [Test]
    public void ValidatePosition_BlockedDiagonal_SlidesAlongX()
    {
        grid.SetCell(new Vector2Int(4, 4), CellState.Building);
        Vector3 oldPos = grid.CellToWorld(new Vector2Int(3, 3));
        Vector3 newPos = grid.CellToWorld(new Vector2Int(4, 4));
        Vector3 vel = new(2f, 0f, 1f);

        Vector3 result = grid.ValidatePosition(oldPos, newPos, vel);

        Vector2Int resultCell = grid.WorldToCell(result);
        Assert.IsTrue(grid.IsWalkable(resultCell), "Slide result should be walkable");
        Assert.AreEqual(4, resultCell.x, "Should slide along X (dominant velocity)");
        Assert.AreEqual(3, resultCell.y, "Z stays at old value");
    }

    [Test]
    public void ValidatePosition_BlockedDiagonal_SlidesAlongZ()
    {
        grid.SetCell(new Vector2Int(4, 4), CellState.Building);
        Vector3 oldPos = grid.CellToWorld(new Vector2Int(3, 3));
        Vector3 newPos = grid.CellToWorld(new Vector2Int(4, 4));
        Vector3 vel = new(1f, 0f, 2f);

        Vector3 result = grid.ValidatePosition(oldPos, newPos, vel);

        Vector2Int resultCell = grid.WorldToCell(result);
        Assert.IsTrue(grid.IsWalkable(resultCell), "Slide result should be walkable");
        Assert.AreEqual(3, resultCell.x, "X stays at old value");
        Assert.AreEqual(4, resultCell.y, "Should slide along Z (dominant velocity)");
    }

    [Test]
    public void ValidatePosition_BothSlideAxesBlocked_FallbackToOldPos()
    {
        grid.SetCell(new Vector2Int(4, 3), CellState.Building);
        grid.SetCell(new Vector2Int(3, 4), CellState.Building);
        grid.SetCell(new Vector2Int(4, 4), CellState.Building);

        Vector3 oldPos = grid.CellToWorld(new Vector2Int(3, 3));
        Vector3 newPos = grid.CellToWorld(new Vector2Int(4, 4));
        Vector3 vel = new(1f, 0f, 1f);

        Vector3 result = grid.ValidatePosition(oldPos, newPos, vel);
        Assert.AreEqual(oldPos.x, result.x, 0.001f);
        Assert.AreEqual(oldPos.z, result.z, 0.001f);
    }
}
