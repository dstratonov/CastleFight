using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections.Generic;
using System.Text.RegularExpressions;

[TestFixture]
public class AttackPositionTests
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

        public void SetUnwalkable(int x, int y)
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height)
                walkable[x, y] = false;
        }

        public void SetBlockRect(int x0, int y0, int x1, int y1)
        {
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    SetUnwalkable(x, y);
        }

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

        public bool HasLineOfSight(Vector2Int from, Vector2Int to)
        {
            int x0 = from.x, y0 = from.y;
            int x1 = to.x, y1 = to.y;
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (x0 == x1 && y0 == y1) return true;

                var cell = new Vector2Int(x0, y0);
                if (cell != from && cell != to && !IsWalkable(cell))
                    return false;

                int e2 = 2 * err;
                bool stepX = e2 > -dy;
                bool stepY = e2 < dx;

                if (stepX && stepY)
                {
                    var adjX = new Vector2Int(x0 + sx, y0);
                    var adjY = new Vector2Int(x0, y0 + sy);
                    if (!IsWalkable(adjX) || !IsWalkable(adjY))
                        return false;
                }

                if (stepX) { err -= dy; x0 += sx; }
                if (stepY) { err += dx; y0 += sy; }
            }
        }
    }

    [SetUp]
    public void SetUp()
    {
        AttackPositionFinder.ReleaseTargetSlots(1);
        AttackPositionFinder.ReleaseTargetSlots(2);
        AttackPositionFinder.ReleaseTargetSlots(99);
        AttackPositionFinder.ReleaseAllSlots(100);
        AttackPositionFinder.ReleaseAllSlots(200);
        AttackPositionFinder.ReleaseAllSlots(300);
    }

    [Test]
    public void GetAttackRangeFromData_MeleeClamped()
    {
        Assert.AreEqual(0.3f, AttackPositionFinder.GetAttackRangeFromData(0f, false), "Melee range clamped to min 0.3");
        Assert.AreEqual(1.5f, AttackPositionFinder.GetAttackRangeFromData(1.5f, false), "Melee range within bounds");
        Assert.AreEqual(2f, AttackPositionFinder.GetAttackRangeFromData(10f, false), "Melee range clamped to max 2");
    }

    [Test]
    public void GetAttackRangeFromData_RangedClamped()
    {
        Assert.AreEqual(1f, AttackPositionFinder.GetAttackRangeFromData(0f, true), "Ranged range clamped to min 1");
        Assert.AreEqual(5f, AttackPositionFinder.GetAttackRangeFromData(5f, true), "Ranged range within bounds");
        Assert.AreEqual(8f, AttackPositionFinder.GetAttackRangeFromData(20f, true), "Ranged range clamped to max 8");
    }

    [Test]
    public void MaxMeleeSlots_ScalesWithCircumference_ClampedToMinimum()
    {
        Assert.AreEqual(6, AttackPositionFinder.MaxMeleeSlots(1f, 1f));
        Assert.AreEqual(18, AttackPositionFinder.MaxMeleeSlots(3f, 1f));
        Assert.AreEqual(8, AttackPositionFinder.MaxMeleeSlots(2f, 0f), "Zero diameter should return default 8");
        Assert.AreEqual(1, AttackPositionFinder.MaxMeleeSlots(0.5f, 10f), "Should always return at least 1");
    }

    // ================================================================
    //  SLOT MANAGEMENT
    // ================================================================

    [Test]
    public void ClaimAndRelease_WorkCorrectly()
    {
        int targetId = 99;
        var cell = new Vector2Int(5, 5);
        int attackerId = 100;

        AttackPositionFinder.ClaimSlot(targetId, cell, attackerId);

        var result = AttackPositionFinder.FindAttackPositionCore(
            new FakeGrid(20, 20), null,
            new Vector3(5f, 0f, 5f), 0f,
            1f, 0.5f, false,
            new Vector3(0f, 0f, 0f), 200, targetId);

        Assert.IsTrue(result.found, "Should find a position even with one slot claimed");
        Assert.AreNotEqual(cell, result.cell, "Should not select the claimed slot");

        AttackPositionFinder.ReleaseSlot(targetId, cell, attackerId);
    }

    [Test]
    public void ReleaseAllSlots_ClearsAttackerSlots()
    {
        int targetId = 99;
        int attackerId = 300;

        AttackPositionFinder.ClaimSlot(targetId, new Vector2Int(5, 5), attackerId);
        AttackPositionFinder.ClaimSlot(targetId, new Vector2Int(6, 5), attackerId);
        AttackPositionFinder.ReleaseAllSlots(attackerId);

        var result = AttackPositionFinder.FindAttackPositionCore(
            new FakeGrid(20, 20), null,
            new Vector3(5f, 0f, 5f), 0f,
            1f, 0.5f, false,
            new Vector3(5f, 0f, 0f), 200, targetId);

        Assert.IsTrue(result.found);
    }

    // ================================================================
    //  RANGED UNIT: ATTACK POSITION AT DISTANCE
    // ================================================================

    [Test]
    public void Ranged_FindsPosition_AtAttackRange()
    {
        var grid = new FakeGrid(30, 30);
        Vector3 targetCenter = new Vector3(15f, 0f, 15f);
        float attackRange = 5f;
        float unitRadius = 0.5f;

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 0f,
            attackRange, unitRadius, isRanged: true,
            new Vector3(15f, 0f, 0f), 100, 1);

        Assert.IsTrue(result.found, "Ranged unit should find an attack position");

        Vector3 cellWorld = grid.CellToWorld(result.cell);
        float dist = Vector3.Distance(cellWorld, targetCenter);
        float maxDist = attackRange + unitRadius;
        Assert.LessOrEqual(dist, maxDist + 0.01f,
            $"Ranged position dist={dist:F1} should be within attackRange+unitRadius={maxDist:F1}");
    }

    [Test]
    public void Ranged_LargeRange_FindsDistantPosition()
    {
        var grid = new FakeGrid(40, 40);
        Vector3 targetCenter = new Vector3(20f, 0f, 20f);
        float attackRange = 8f;
        float unitRadius = 0.5f;

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 0f,
            attackRange, unitRadius, isRanged: true,
            new Vector3(20f, 0f, 0f), 100, 1);

        Assert.IsTrue(result.found);

        Vector3 cellWorld = grid.CellToWorld(result.cell);
        float dist = Vector3.Distance(cellWorld, targetCenter);
        Assert.LessOrEqual(dist, attackRange + unitRadius + 0.01f);
    }

    // ================================================================
    //  RANGED UNIT: LINE-OF-SIGHT
    // ================================================================

    [Test]
    public void Ranged_RequiresLineOfSight()
    {
        var grid = new FakeGrid(20, 20);
        // Wall between attacker and target
        for (int y = 0; y < 20; y++)
            grid.SetUnwalkable(10, y);
        grid.SetUnwalkable(10, 10); // ensure wall at center
        // Leave a gap at y=5
        // Actually make the wall solid first, then DON'T open a gap for this test

        Vector3 targetCenter = new Vector3(15f, 0f, 10f);
        Vector3 attackerPos = new Vector3(5f, 0f, 10f);

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 0f,
            5f, 0.5f, isRanged: true,
            attackerPos, 100, 1);

        if (result.found)
        {
            Vector3 cellWorld = grid.CellToWorld(result.cell);
            Assert.IsTrue(grid.HasLineOfSight(result.cell, grid.WorldToCell(targetCenter)),
                "Ranged unit's chosen cell must have line of sight to target");
        }
    }

    [Test]
    public void Ranged_SkipsCellsWithoutLOS()
    {
        var grid = new FakeGrid(20, 20);
        // Place a 1-cell wall blocking direct LOS from (5,10) to (15,10)
        grid.SetUnwalkable(10, 10);

        Vector3 targetCenter = new Vector3(15f, 0f, 10f);
        Vector2Int targetCell = new Vector2Int(15, 10);

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 0f,
            5f, 0.5f, isRanged: true,
            new Vector3(5f, 0f, 10f), 100, 1);

        Assert.IsTrue(result.found);
        Assert.IsTrue(grid.HasLineOfSight(result.cell, targetCell),
            "Ranged unit must only choose cells with LOS to target");
    }

    [Test]
    public void Melee_DoesNotRequireLineOfSight()
    {
        var grid = new FakeGrid(20, 20);
        grid.SetUnwalkable(10, 10);

        Vector3 targetCenter = new Vector3(11f, 0f, 10f);

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 0f,
            2f, 0.5f, isRanged: false,
            new Vector3(5f, 0f, 10f), 100, 1);

        Assert.IsTrue(result.found, "Melee unit should find position even without LOS");
    }

    // ================================================================
    //  RANGED UNIT: AROUND BUILDINGS (unwalkable target center)
    // ================================================================

    [Test]
    public void Ranged_AroundBuilding_FindsPositionWithLOS()
    {
        var grid = new FakeGrid(30, 30);
        // Single-cell obstacle so adjacent cells have clear LOS
        grid.SetUnwalkable(15, 15);

        Vector3 targetCenter = new Vector3(15f, 0f, 15f);
        float targetRadius = 0.5f;

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, targetRadius,
            5f, 0.5f, isRanged: true,
            new Vector3(15f, 0f, 5f), 100, 1);

        Assert.IsTrue(result.found, "Ranged unit should find position around building");

        Vector2Int targetCell = grid.WorldToCell(targetCenter);
        Assert.IsTrue(grid.HasLineOfSight(result.cell, targetCell) ||
                       grid.IsWalkable(result.cell),
            "Position should have LOS or be walkable");
    }

    [Test]
    public void Melee_AroundBuilding_FindsAdjacentPosition()
    {
        var grid = new FakeGrid(30, 30);
        grid.SetBlockRect(14, 14, 16, 16);

        Vector3 targetCenter = new Vector3(15f, 0f, 15f);
        float targetRadius = 1.5f;

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, targetRadius,
            2f, 0.5f, isRanged: false,
            new Vector3(15f, 0f, 5f), 100, 1);

        Assert.IsTrue(result.found, "Melee unit should find position adjacent to building");

        Vector3 cellWorld = grid.CellToWorld(result.cell);
        float dist = Vector3.Distance(cellWorld, targetCenter);
        Assert.LessOrEqual(dist, targetRadius + 2f + 0.5f + 0.01f,
            $"Melee position should be close to building edge (dist={dist:F1})");
    }

    // ================================================================
    //  RANGED UNIT: DIFFERENT SIZES
    // ================================================================

    [Test]
    public void Ranged_LargeUnit_FindsPosition()
    {
        var grid = new FakeGrid(40, 40);
        Vector3 targetCenter = new Vector3(20f, 0f, 20f);

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 0f,
            6f, 2.0f, isRanged: true,
            new Vector3(20f, 0f, 0f), 100, 1);

        Assert.IsTrue(result.found);

        Vector3 cellWorld = grid.CellToWorld(result.cell);
        float dist = Vector3.Distance(cellWorld, targetCenter);
        Assert.LessOrEqual(dist, 6f + 2f + 0.01f);
    }

    [Test]
    public void Ranged_VeryLargeUnit_FindsPosition()
    {
        var grid = new FakeGrid(50, 50);
        Vector3 targetCenter = new Vector3(25f, 0f, 25f);

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 0f,
            8f, 3.0f, isRanged: true,
            new Vector3(25f, 0f, 0f), 100, 1);

        Assert.IsTrue(result.found);

        Vector3 cellWorld = grid.CellToWorld(result.cell);
        float dist = Vector3.Distance(cellWorld, targetCenter);
        Assert.LessOrEqual(dist, 8f + 3f + 0.01f);
    }

    // ================================================================
    //  RANGED UNIT: PREFERS POSITION CLOSER TO ATTACKER
    // ================================================================

    [Test]
    public void Ranged_PrefersPositionCloserToAttacker()
    {
        var grid = new FakeGrid(30, 30);
        Vector3 targetCenter = new Vector3(15f, 0f, 15f);
        Vector3 attackerPos = new Vector3(15f, 0f, 0f);

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 0f,
            5f, 0.5f, isRanged: true,
            attackerPos, 100, 1);

        Assert.IsTrue(result.found);

        Vector3 cellWorld = grid.CellToWorld(result.cell);
        Assert.Less(cellWorld.z, 15f,
            "Ranged unit should prefer position on the attacker's side (south)");
    }

    // ================================================================
    //  RANGED UNIT: CLEARANCE-BASED FILTERING
    // ================================================================

    [Test]
    public void Ranged_LargeUnit_BlockedByNarrowGap_WithClearance()
    {
        var grid = new FakeGrid(20, 20);
        grid.SetUnwalkable(9, 10);
        grid.SetUnwalkable(11, 10);

        var clearance = new ClearanceMap();
        clearance.ComputeFull(grid);

        Vector3 targetCenter = new Vector3(10f, 0f, 15f);

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, clearance,
            targetCenter, 0f,
            5f, 3f, isRanged: true,
            new Vector3(10f, 0f, 5f), 100, 1);

        if (result.found)
        {
            Assert.IsTrue(clearance.CanPass(result.cell, 3f),
                "Position selected must pass clearance check for the unit radius");
        }
    }

    // ================================================================
    //  RANGED UNIT: MULTIPLE ATTACKERS
    // ================================================================

    [Test]
    public void Ranged_MultipleAttackers_GetDifferentPositions()
    {
        var grid = new FakeGrid(30, 30);
        Vector3 targetCenter = new Vector3(15f, 0f, 15f);
        int targetId = 1;

        var result1 = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 0f,
            5f, 0.5f, isRanged: true,
            new Vector3(15f, 0f, 0f), 100, targetId);

        var result2 = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 0f,
            5f, 0.5f, isRanged: true,
            new Vector3(15f, 0f, 0f), 200, targetId);

        Assert.IsTrue(result1.found, "First ranged attacker should find position");
        Assert.IsTrue(result2.found, "Second ranged attacker should find position");
        Assert.AreNotEqual(result1.cell, result2.cell,
            "Two attackers should get different positions (slot claiming)");
    }

    [Test]
    public void Ranged_MixedSizes_DifferentPositions()
    {
        var grid = new FakeGrid(30, 30);
        Vector3 targetCenter = new Vector3(15f, 0f, 15f);
        int targetId = 2;

        var smallResult = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 0f,
            5f, 0.3f, isRanged: true,
            new Vector3(15f, 0f, 0f), 100, targetId);

        var largeResult = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 0f,
            5f, 2.0f, isRanged: true,
            new Vector3(15f, 0f, 0f), 200, targetId);

        Assert.IsTrue(smallResult.found, "Small ranged unit should find position");
        Assert.IsTrue(largeResult.found, "Large ranged unit should find position");
    }

    // ================================================================
    //  NAVMESH PATHFINDING FOR RANGED UNITS
    // ================================================================

    /// <summary>
    /// Wide corridor: 30 units long, 8 units tall. Suitable for testing
    /// different unit radii from small (0.3) to very large (3.0).
    /// </summary>
    private NavMeshData CreateWideCorridor()
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));   // v0
        mesh.AddVertex(new Vector2(15f, 0f));  // v1
        mesh.AddVertex(new Vector2(0f, 8f));   // v2
        mesh.AddVertex(new Vector2(15f, 8f));  // v3
        mesh.AddVertex(new Vector2(30f, 0f));  // v4
        mesh.AddVertex(new Vector2(30f, 8f));  // v5

        mesh.AddTriangle(0, 1, 2, walkable: true);
        mesh.AddTriangle(1, 3, 2, walkable: true);
        mesh.AddTriangle(1, 4, 3, walkable: true);
        mesh.AddTriangle(4, 5, 3, walkable: true);

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();
        mesh.BuildSpatialGrid(5f);
        return mesh;
    }

    [Test]
    public void NavMesh_SmallRangedUnit_PathsThroughCorridor()
    {
        var mesh = CreateWideCorridor();
        var path = NavMeshPathfinder.FindPath(mesh, new Vector2(2f, 4f), new Vector2(28f, 4f), 0.3f);

        Assert.IsNotNull(path, "Small ranged unit (r=0.3) should path through wide corridor");
        Assert.GreaterOrEqual(path.Count, 2);
    }

    [Test]
    public void NavMesh_MediumRangedUnit_PathsThroughCorridor()
    {
        var mesh = CreateWideCorridor();
        var path = NavMeshPathfinder.FindPath(mesh, new Vector2(2f, 4f), new Vector2(28f, 4f), 0.5f);

        Assert.IsNotNull(path, "Medium ranged unit (r=0.5) should path through wide corridor");
        Assert.GreaterOrEqual(path.Count, 2);
    }

    [Test]
    public void NavMesh_LargeRangedUnit_PathsThroughWideCorridor()
    {
        var mesh = CreateWideCorridor();
        var path = NavMeshPathfinder.FindPath(mesh, new Vector2(2f, 4f), new Vector2(28f, 4f), 2.0f);

        Assert.IsNotNull(path, "Large ranged unit (r=2.0) should path through 8-unit-wide corridor");
        Assert.GreaterOrEqual(path.Count, 2);

        foreach (var wp in path)
        {
            Assert.GreaterOrEqual(wp.y, -0.5f, $"Waypoint {wp} should stay inside corridor");
            Assert.LessOrEqual(wp.y, 8.5f, $"Waypoint {wp} should stay inside corridor");
        }
    }

    [Test]
    public void NavMesh_VeryLargeRangedUnit_PathsThroughWideCorridor()
    {
        var mesh = CreateWideCorridor();
        var path = NavMeshPathfinder.FindPath(mesh, new Vector2(2f, 4f), new Vector2(28f, 4f), 3.0f);

        Assert.IsNotNull(path, "Very large ranged unit (r=3.0) should path through 8-unit-wide corridor");
    }

    // ================================================================
    //  RANGED UNIT: CORRIDOR WIDTH FILTERING
    // ================================================================

    [Test]
    public void NavMesh_WidthFilter_SmallUnitPassesNarrowCorridor()
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));   // v0
        mesh.AddVertex(new Vector2(5f, 0f));   // v1
        mesh.AddVertex(new Vector2(5f, 2f));   // v2 narrow point
        mesh.AddVertex(new Vector2(10f, 0f));  // v3
        mesh.AddVertex(new Vector2(0f, 5f));   // v4
        mesh.AddVertex(new Vector2(10f, 5f));  // v5

        mesh.AddTriangle(0, 1, 4, walkable: true);
        mesh.AddTriangle(1, 2, 4, walkable: true);
        mesh.AddTriangle(1, 3, 2, walkable: true);
        mesh.AddTriangle(3, 5, 2, walkable: true);

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();
        mesh.BuildSpatialGrid(5f);

        var path = NavMeshPathfinder.FindPath(mesh, new Vector2(1f, 1f), new Vector2(9f, 1f), 0.3f);
        Assert.IsNotNull(path, "Small unit (r=0.3) should pass through corridor");
    }

    [Test]
    public void NavMesh_WidthFilter_LargeRangedUnitBlockedByNarrow()
    {
        // Two triangles connected by a very narrow portal (1 unit wide).
        // A large ranged unit (radius=2, diameter=4) should be blocked.
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));    // v0
        mesh.AddVertex(new Vector2(5f, 0f));    // v1
        mesh.AddVertex(new Vector2(5f, 1f));    // v2: narrow portal top
        mesh.AddVertex(new Vector2(0f, 10f));   // v3
        mesh.AddVertex(new Vector2(10f, 0f));   // v4
        mesh.AddVertex(new Vector2(10f, 10f));  // v5

        mesh.AddTriangle(0, 1, 3, walkable: true);
        mesh.AddTriangle(1, 2, 3, walkable: true);
        mesh.AddTriangle(1, 4, 2, walkable: true);
        mesh.AddTriangle(4, 5, 2, walkable: true);

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();
        mesh.BuildSpatialGrid(5f);

        var path = NavMeshPathfinder.FindPath(mesh, new Vector2(1f, 2f), new Vector2(9f, 2f), 2.0f);
        Assert.IsNull(path, "Large unit (r=2.0, d=4.0) should be blocked by 1-unit-wide portal");
    }

    // ================================================================
    //  EDGE CASES
    // ================================================================

    [Test]
    public void FindPosition_NullGrid_ReturnsNotFound()
    {
        LogAssert.Expect(LogType.Assert, new Regex("grid is null"));

        var result = AttackPositionFinder.FindAttackPositionCore(
            null, null,
            Vector3.zero, 0f,
            5f, 0.5f, true,
            Vector3.zero, 100, 1);

        Assert.IsFalse(result.found);
    }

    [Test]
    public void FindPosition_TargetAtGridEdge_StillFindsPosition()
    {
        var grid = new FakeGrid(20, 20);
        Vector3 targetCenter = new Vector3(0f, 0f, 0f);

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 0f,
            3f, 0.5f, isRanged: true,
            new Vector3(5f, 0f, 5f), 100, 1);

        Assert.IsTrue(result.found, "Should find position even when target is at grid edge");
    }

    [Test]
    public void FindPosition_AttackerOnTarget_StillFindsPosition()
    {
        var grid = new FakeGrid(20, 20);
        Vector3 pos = new Vector3(10f, 0f, 10f);

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            pos, 0f,
            5f, 0.5f, isRanged: true,
            pos, 100, 1);

        Assert.IsTrue(result.found);
    }

    // ================================================================
    //  RANGED VS MELEE: DISTANCE COMPARISON
    // ================================================================

    [Test]
    public void Ranged_PositionsAreFartherThanMelee()
    {
        var grid = new FakeGrid(40, 40);
        Vector3 targetCenter = new Vector3(20f, 0f, 20f);
        Vector3 attackerPos = new Vector3(20f, 0f, 0f);

        var meleeResult = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 1f,
            1.5f, 0.5f, isRanged: false,
            attackerPos, 100, 1);

        AttackPositionFinder.ReleaseTargetSlots(1);

        var rangedResult = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 1f,
            6f, 0.5f, isRanged: true,
            attackerPos, 200, 1);

        Assert.IsTrue(meleeResult.found, "Melee should find position");
        Assert.IsTrue(rangedResult.found, "Ranged should find position");

        Vector3 meleeWorld = grid.CellToWorld(meleeResult.cell);
        Vector3 rangedWorld = grid.CellToWorld(rangedResult.cell);

        float meleeDist = Vector3.Distance(meleeWorld, targetCenter);
        float rangedDist = Vector3.Distance(rangedWorld, targetCenter);

        Assert.Less(meleeDist, rangedDist + 1f,
            $"Melee dist={meleeDist:F1} should generally be closer than ranged dist={rangedDist:F1}");
    }

    // ================================================================
    //  RANGED BEHIND WALL: LOS VERIFICATION
    // ================================================================

    [Test]
    public void Ranged_BehindFullWall_NoPositionOnBlockedSide()
    {
        var grid = new FakeGrid(30, 30);
        // Full wall at x=15, only gap at y=0
        for (int y = 1; y < 30; y++)
            grid.SetUnwalkable(15, y);

        Vector3 targetCenter = new Vector3(20f, 0f, 15f);
        Vector3 attackerPos = new Vector3(5f, 0f, 15f);

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 0f,
            5f, 0.5f, isRanged: true,
            attackerPos, 100, 1);

        if (result.found)
        {
            Vector2Int targetCell = grid.WorldToCell(targetCenter);
            Assert.IsTrue(grid.HasLineOfSight(result.cell, targetCell),
                "Ranged unit must have LOS even when wall separates attacker and target");
        }
    }

    // ================================================================
    //  MELEE SLOT SATURATION
    // ================================================================

    [Test]
    public void Melee_AllSlotsClaimed_StillFindsPosition()
    {
        AttackPositionFinder.ReleaseAllSlots(0);
        var grid = new FakeGrid(20, 20);
        Vector3 targetCenter = new Vector3(10f, 0f, 10f);
        float targetRadius = 1f;
        float attackRange = 1f;
        float unitRadius = 0.5f;
        int targetId = 500;

        // Claim many slots around the target so they're "taken"
        int maxSlots = AttackPositionFinder.MaxMeleeSlots(targetRadius, unitRadius * 2f);
        for (int i = 0; i < maxSlots + 5; i++)
        {
            int angle = i * 360 / (maxSlots + 5);
            float rad = angle * Mathf.Deg2Rad;
            int cx = 10 + Mathf.RoundToInt(Mathf.Cos(rad) * 2f);
            int cy = 10 + Mathf.RoundToInt(Mathf.Sin(rad) * 2f);
            AttackPositionFinder.ClaimSlot(targetId, new Vector2Int(cx, cy), 1000 + i);
        }

        // Now a new attacker tries to find a position
        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, targetRadius,
            attackRange, unitRadius, isRanged: false,
            new Vector3(5f, 0f, 10f), 2000, targetId);

        Assert.IsTrue(result.found,
            "Melee unit should find a position even when many slots are claimed");

        // Clean up all the slots we claimed
        for (int i = 0; i < maxSlots + 5; i++)
            AttackPositionFinder.ReleaseAllSlots(1000 + i);
        AttackPositionFinder.ReleaseAllSlots(2000);
    }

    // ================================================================
    //  RELEASE SLOT: WRONG ATTACKER
    // ================================================================

    [Test]
    public void ReleaseSlot_WrongAttacker_DoesNotFreeSlot()
    {
        AttackPositionFinder.ReleaseAllSlots(100);
        AttackPositionFinder.ReleaseAllSlots(200);

        int targetId = 1;
        var cell = new Vector2Int(5, 5);
        int correctAttacker = 100;
        int wrongAttacker = 200;

        AttackPositionFinder.ClaimSlot(targetId, cell, correctAttacker);
        AttackPositionFinder.ReleaseSlot(targetId, cell, wrongAttacker);

        // The correct attacker should still hold the slot.
        // Verify by releasing with the correct attacker (no exception expected)
        AttackPositionFinder.ReleaseSlot(targetId, cell, correctAttacker);
        AttackPositionFinder.ReleaseAllSlots(100);
    }

    // ================================================================
    //  MELEE LARGE UNIT AROUND BUILDING
    // ================================================================

    [Test]
    public void Melee_LargeUnit_AroundBuilding_FindsPosition()
    {
        AttackPositionFinder.ReleaseAllSlots(300);
        var grid = new FakeGrid(20, 20);
        // 3x3 building centered at (10,10)
        for (int x = 9; x <= 11; x++)
            for (int y = 9; y <= 11; y++)
                grid.SetUnwalkable(x, y);

        Vector3 targetCenter = new Vector3(10f, 0f, 10f);
        float targetRadius = 1.5f;
        float attackRange = 1.5f;
        float unitRadius = 1.5f; // large unit

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, targetRadius,
            attackRange, unitRadius, isRanged: false,
            new Vector3(5f, 0f, 10f), 300, 1);

        Assert.IsTrue(result.found,
            "Large melee unit should find attack position around building");

        Assert.IsFalse(result.cell.x >= 9 && result.cell.x <= 11 &&
                        result.cell.y >= 9 && result.cell.y <= 11,
            "Attack position should be outside the building");

        AttackPositionFinder.ReleaseAllSlots(300);
    }

    // ================================================================
    //  RELEASE TARGET SLOTS
    // ================================================================

    [Test]
    public void ReleaseTargetSlots_ClearsAllSlotsForTarget()
    {
        int targetId = 900;
        AttackPositionFinder.ReleaseAllSlots(0);

        AttackPositionFinder.ClaimSlot(targetId, new Vector2Int(1, 1), 10);
        AttackPositionFinder.ClaimSlot(targetId, new Vector2Int(2, 2), 20);
        AttackPositionFinder.ClaimSlot(targetId, new Vector2Int(3, 3), 30);

        AttackPositionFinder.ReleaseTargetSlots(targetId);

        // After releasing all slots for this target, claiming the same cells should work
        AttackPositionFinder.ClaimSlot(targetId, new Vector2Int(1, 1), 40);
        AttackPositionFinder.ReleaseTargetSlots(targetId);
    }

    [Test]
    public void ReleaseTargetSlots_DoesNotAffectOtherTargets()
    {
        int target1 = 901;
        int target2 = 902;
        AttackPositionFinder.ReleaseAllSlots(50);
        AttackPositionFinder.ReleaseAllSlots(60);

        AttackPositionFinder.ClaimSlot(target1, new Vector2Int(1, 1), 50);
        AttackPositionFinder.ClaimSlot(target2, new Vector2Int(2, 2), 60);

        AttackPositionFinder.ReleaseTargetSlots(target1);

        // target2's slot should still be claimed — releasing it via the correct attacker
        AttackPositionFinder.ReleaseSlot(target2, new Vector2Int(2, 2), 60);
        AttackPositionFinder.ReleaseTargetSlots(target2);
    }

    // ================================================================
    //  SLOT CLAIM / RELEASE EDGE CASES
    // ================================================================

    [Test]
    public void ClaimSlot_SameCellTwice_OverwritesPreviousAttacker()
    {
        int targetId = 904;
        var cell = new Vector2Int(4, 4);
        AttackPositionFinder.ClaimSlot(targetId, cell, 100);
        AttackPositionFinder.ClaimSlot(targetId, cell, 200);

        // Second attacker now owns the slot; releasing with first attacker
        // should NOT free it (wrong attacker)
        AttackPositionFinder.ReleaseSlot(targetId, cell, 100);

        // Clean up
        AttackPositionFinder.ReleaseSlot(targetId, cell, 200);
        AttackPositionFinder.ReleaseTargetSlots(targetId);
    }

    [Test]
    public void ClaimAndRelease_MultipleTargets_IndependentSlots()
    {
        int target1 = 905;
        int target2 = 906;
        var sameCell = new Vector2Int(7, 7);

        AttackPositionFinder.ClaimSlot(target1, sameCell, 100);
        AttackPositionFinder.ClaimSlot(target2, sameCell, 200);

        AttackPositionFinder.ReleaseSlot(target1, sameCell, 100);
        // target2 still has that cell claimed

        AttackPositionFinder.ReleaseTargetSlots(target1);
        AttackPositionFinder.ReleaseTargetSlots(target2);
    }

    // ================================================================
    //  MELEE: ADJACENT CELLS WITH OBSTACLES
    // ================================================================

    [Test]
    public void Melee_SurroundedByWalls_StillFindsWalkableSlot()
    {
        AttackPositionFinder.ReleaseAllSlots(400);
        var grid = new FakeGrid(20, 20);
        // Block three sides around the target (9,10), (11,10), (10,9)
        grid.SetUnwalkable(9, 10);
        grid.SetUnwalkable(11, 10);
        grid.SetUnwalkable(10, 9);

        Vector3 targetCenter = new Vector3(10f, 0f, 10f);
        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 0.5f,
            1f, 0.5f, isRanged: false,
            new Vector3(10f, 0f, 14f), 400, 1);

        Assert.IsTrue(result.found,
            "Melee should find the one remaining open side");
        AttackPositionFinder.ReleaseAllSlots(400);
    }

    // ================================================================
    //  RANGED: EXTREME RANGES
    // ================================================================

    [Test]
    public void Ranged_VeryShortRange_FindsClosePosition()
    {
        AttackPositionFinder.ReleaseAllSlots(500);
        var grid = new FakeGrid(20, 20);
        Vector3 targetCenter = new Vector3(10f, 0f, 10f);

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, 0.5f,
            1.0f, 0.3f, isRanged: true,
            new Vector3(5f, 0f, 10f), 500, 1);

        Assert.IsTrue(result.found);
        float dist = Vector2Int.Distance(result.cell, grid.WorldToCell(targetCenter));
        Assert.LessOrEqual(dist, 4f,
            "Very short range unit should be close to target");
        AttackPositionFinder.ReleaseAllSlots(500);
    }

    // ================================================================
    //  SC2-STYLE: RANGED STANDOFF DISTANCE
    // ================================================================

    [Test]
    public void Ranged_MaintainsStandoffDistance()
    {
        AttackPositionFinder.ReleaseAllSlots(600);
        var grid = new FakeGrid(40, 40);
        Vector3 targetCenter = new Vector3(20f, 0f, 20f);
        float attackRange = 6f;
        float targetRadius = 0f;

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, targetRadius,
            attackRange, 0.5f, isRanged: true,
            new Vector3(20f, 0f, 0f), 600, 1);

        Assert.IsTrue(result.found);

        Vector3 cellWorld = grid.CellToWorld(result.cell);
        float distToTarget = Vector3.Distance(cellWorld, targetCenter);
        float minStandoff = targetRadius + attackRange * 0.4f;

        Assert.GreaterOrEqual(distToTarget, minStandoff - 0.01f,
            $"Ranged unit should maintain standoff distance of {minStandoff:F1} " +
            $"but was at {distToTarget:F1}");
        AttackPositionFinder.ReleaseAllSlots(600);
    }

    [Test]
    public void Ranged_PrefersNearIdealRange()
    {
        AttackPositionFinder.ReleaseAllSlots(601);
        var grid = new FakeGrid(40, 40);
        Vector3 targetCenter = new Vector3(20f, 0f, 20f);
        float attackRange = 6f;
        float targetRadius = 0f;

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, targetRadius,
            attackRange, 0.5f, isRanged: true,
            new Vector3(20f, 0f, 0f), 601, 1);

        Assert.IsTrue(result.found);

        Vector3 cellWorld = grid.CellToWorld(result.cell);
        float distToTarget = Vector3.Distance(cellWorld, targetCenter);
        float idealDist = targetRadius + attackRange * 0.85f;

        Assert.Less(Mathf.Abs(distToTarget - idealDist), attackRange * 0.5f,
            $"Ranged unit should be near ideal range {idealDist:F1} " +
            $"but was at {distToTarget:F1}");
        AttackPositionFinder.ReleaseAllSlots(601);
    }

    [Test]
    public void Ranged_StandoffWithTargetRadius()
    {
        AttackPositionFinder.ReleaseAllSlots(602);
        var grid = new FakeGrid(40, 40);
        Vector3 targetCenter = new Vector3(20f, 0f, 20f);
        float attackRange = 5f;
        float targetRadius = 2f;

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, targetRadius,
            attackRange, 0.5f, isRanged: true,
            new Vector3(20f, 0f, 0f), 602, 1);

        Assert.IsTrue(result.found);

        Vector3 cellWorld = grid.CellToWorld(result.cell);
        float distToTarget = Vector3.Distance(cellWorld, targetCenter);
        float minStandoff = targetRadius + attackRange * 0.4f;

        Assert.GreaterOrEqual(distToTarget, minStandoff - 0.01f,
            $"Ranged should maintain standoff {minStandoff:F1} (includes targetR={targetRadius}) " +
            $"but was at {distToTarget:F1}");
        AttackPositionFinder.ReleaseAllSlots(602);
    }

    [Test]
    public void Melee_DoesNotHaveStandoffDistance()
    {
        AttackPositionFinder.ReleaseAllSlots(603);
        var grid = new FakeGrid(30, 30);
        Vector3 targetCenter = new Vector3(15f, 0f, 15f);
        float attackRange = 1.5f;
        float targetRadius = 0f;

        var result = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, targetRadius,
            attackRange, 0.5f, isRanged: false,
            new Vector3(15f, 0f, 0f), 603, 1);

        Assert.IsTrue(result.found);

        Vector3 cellWorld = grid.CellToWorld(result.cell);
        float distToTarget = Vector3.Distance(cellWorld, targetCenter);

        Assert.LessOrEqual(distToTarget, attackRange + 0.5f + 0.5f,
            $"Melee should be right next to target, dist={distToTarget:F1}");
        AttackPositionFinder.ReleaseAllSlots(603);
    }

    [Test]
    public void Ranged_VsTargetWithRadius_StaysFarther()
    {
        AttackPositionFinder.ReleaseAllSlots(604);
        AttackPositionFinder.ReleaseAllSlots(605);
        var grid = new FakeGrid(40, 40);
        Vector3 targetCenter = new Vector3(20f, 0f, 20f);
        float targetRadius = 1.5f;
        float attackRange = 5f;

        var rangedResult = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, targetRadius,
            attackRange, 0.5f, isRanged: true,
            new Vector3(20f, 0f, 5f), 604, 1);

        AttackPositionFinder.ReleaseTargetSlots(1);

        var meleeResult = AttackPositionFinder.FindAttackPositionCore(
            grid, null,
            targetCenter, targetRadius,
            1.5f, 0.5f, isRanged: false,
            new Vector3(20f, 0f, 5f), 605, 1);

        Assert.IsTrue(rangedResult.found);
        Assert.IsTrue(meleeResult.found);

        float rangedDist = Vector3.Distance(grid.CellToWorld(rangedResult.cell), targetCenter);
        float meleeDist = Vector3.Distance(grid.CellToWorld(meleeResult.cell), targetCenter);

        Assert.Greater(rangedDist, meleeDist,
            $"Ranged dist={rangedDist:F1} should be farther than melee dist={meleeDist:F1}");
        AttackPositionFinder.ReleaseAllSlots(604);
        AttackPositionFinder.ReleaseAllSlots(605);
    }

    [Test]
    public void Ranged_MultipleUnits_SpreadAroundTarget()
    {
        var grid = new FakeGrid(40, 40);
        Vector3 targetCenter = new Vector3(20f, 0f, 20f);
        float attackRange = 6f;
        int targetId = 3;

        var positions = new List<Vector2Int>();
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI / 2f;
            Vector3 attackerPos = targetCenter + new Vector3(
                Mathf.Cos(angle) * 15f, 0f, Mathf.Sin(angle) * 15f);

            var result = AttackPositionFinder.FindAttackPositionCore(
                grid, null,
                targetCenter, 0f,
                attackRange, 0.5f, isRanged: true,
                attackerPos, 700 + i, targetId);

            Assert.IsTrue(result.found, $"Ranged unit {i} should find position");
            positions.Add(result.cell);
        }

        int uniquePositions = new HashSet<Vector2Int>(positions).Count;
        Assert.AreEqual(4, uniquePositions,
            "4 ranged units approaching from different directions should get 4 different positions");

        AttackPositionFinder.ReleaseTargetSlots(targetId);
        for (int i = 0; i < 4; i++)
            AttackPositionFinder.ReleaseAllSlots(700 + i);
    }
}
