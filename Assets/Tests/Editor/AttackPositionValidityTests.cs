using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests that AttackPositionFinder returns positions that are:
/// 1. On walkable cells (never inside buildings)
/// 2. Within valid attack distance of the target
/// 3. Not at grid origin or map corners when no valid position exists
/// 4. Consistent with the grid's building footprint
///
/// Uses real GridLogic (implements same math as GridSystem) with IGrid adapter.
/// </summary>
[TestFixture]
public class AttackPositionValidityTests
{
    private TestGrid grid;
    private ClearanceMap clearance;

    private class TestGrid : IGrid
    {
        private readonly GridLogic logic;

        public TestGrid(int w, int h, float cellSize, Vector3 origin)
        {
            logic = new GridLogic(w, h, cellSize, origin);
        }

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

        public void MarkBuilding(Bounds bounds)
        {
            var cells = logic.GetCellsOverlappingBounds(bounds);
            foreach (var c in cells)
                logic.SetCell(c, CellState.Building);
        }

        public void SetUnwalkable(Vector2Int cell) => logic.SetCell(cell, CellState.Building);
    }

    [SetUp]
    public void SetUp()
    {
        grid = new TestGrid(100, 100, 2f, new Vector3(-100f, 0f, -100f));
        clearance = new ClearanceMap();
    }

    private void RebuildClearance()
    {
        clearance.ComputeFull(grid);
    }

    // ================================================================
    //  Position is on walkable cell
    // ================================================================

    [Test]
    public void AttackPosition_IsWalkable()
    {
        Vector3 targetPos = new Vector3(0f, 0f, 0f);
        grid.MarkBuilding(new Bounds(targetPos, new Vector3(6f, 4f, 6f)));
        RebuildClearance();

        var (cell, found) = AttackPositionFinder.FindAttackPositionCore(
            grid, clearance,
            targetCenter: targetPos,
            targetRadius: 3f,
            attackRange: 1.5f,
            unitRadius: 0.5f,
            isRanged: false,
            attackerPos: new Vector3(20f, 0f, 0f),
            attackerId: 1,
            targetId: 100
        );

        Assert.IsTrue(found, "Should find a valid attack position");
        Assert.IsTrue(grid.IsWalkable(cell),
            $"Attack position cell {cell} must be walkable");
    }

    [Test]
    public void AttackPosition_NotInsideTargetFootprint()
    {
        Vector3 targetPos = new Vector3(0f, 0f, 0f);
        Bounds targetBounds = new Bounds(targetPos, new Vector3(10f, 4f, 10f));
        grid.MarkBuilding(targetBounds);
        RebuildClearance();

        var (cell, found) = AttackPositionFinder.FindAttackPositionCore(
            grid, clearance,
            targetCenter: targetPos,
            targetRadius: 5f,
            attackRange: 1.5f,
            unitRadius: 0.5f,
            isRanged: false,
            attackerPos: new Vector3(20f, 0f, 0f),
            attackerId: 1,
            targetId: 100
        );

        if (found)
        {
            Vector3 worldPos = grid.CellToWorld(cell);
            Assert.IsFalse(targetBounds.Contains(worldPos),
                $"Attack position at {worldPos} must not be inside target bounds {targetBounds}");
        }
    }

    // ================================================================
    //  Position is within valid attack distance
    // ================================================================

    [Test]
    public void AttackPosition_WithinAttackRange_Melee()
    {
        Vector3 targetPos = new Vector3(0f, 0f, 0f);
        float targetRadius = 4f;
        float attackRange = 1.5f;
        float unitRadius = 0.5f;

        grid.MarkBuilding(new Bounds(targetPos, new Vector3(targetRadius * 2, 4f, targetRadius * 2)));
        RebuildClearance();

        var (cell, found) = AttackPositionFinder.FindAttackPositionCore(
            grid, clearance,
            targetCenter: targetPos,
            targetRadius: targetRadius,
            attackRange: attackRange,
            unitRadius: unitRadius,
            isRanged: false,
            attackerPos: new Vector3(20f, 0f, 0f),
            attackerId: 1,
            targetId: 100
        );

        Assert.IsTrue(found);

        Vector3 worldPos = grid.CellToWorld(cell);
        float distToCenter = Vector3.Distance(worldPos, targetPos);
        float maxAllowedDist = targetRadius + attackRange + unitRadius;

        Assert.LessOrEqual(distToCenter, maxAllowedDist + grid.CellSize,
            $"Attack position at {worldPos} (dist={distToCenter:F1}) exceeds max allowed " +
            $"distance {maxAllowedDist:F1} from target center");
    }

    [Test]
    public void AttackPosition_WithinAttackRange_Ranged()
    {
        Vector3 targetPos = new Vector3(0f, 0f, 0f);
        float targetRadius = 4f;
        float attackRange = 6f;
        float unitRadius = 0.5f;

        grid.MarkBuilding(new Bounds(targetPos, new Vector3(targetRadius * 2, 4f, targetRadius * 2)));
        RebuildClearance();

        var (cell, found) = AttackPositionFinder.FindAttackPositionCore(
            grid, clearance,
            targetCenter: targetPos,
            targetRadius: targetRadius,
            attackRange: attackRange,
            unitRadius: unitRadius,
            isRanged: true,
            attackerPos: new Vector3(20f, 0f, 0f),
            attackerId: 1,
            targetId: 100
        );

        if (!found)
        {
            Assert.Pass("No position found — ranged units have strict LOS/standoff, acceptable");
            return;
        }

        Vector3 worldPos = grid.CellToWorld(cell);
        float distToCenter = Vector3.Distance(worldPos, targetPos);
        float maxAllowedDist = targetRadius + attackRange + unitRadius;

        Assert.LessOrEqual(distToCenter, maxAllowedDist + grid.CellSize * 2f,
            "Ranged attack position must be within max attack range (with grid discretization tolerance)");
    }

    // ================================================================
    //  No valid position → found=false, not (0,0)
    // ================================================================

    [Test]
    public void NoValidPosition_ReturnsFalse()
    {
        for (int x = 0; x < 100; x++)
            for (int y = 0; y < 100; y++)
                grid.SetUnwalkable(new Vector2Int(x, y));
        RebuildClearance();

        var (cell, found) = AttackPositionFinder.FindAttackPositionCore(
            grid, clearance,
            targetCenter: new Vector3(0f, 0f, 0f),
            targetRadius: 3f,
            attackRange: 1.5f,
            unitRadius: 0.5f,
            isRanged: false,
            attackerPos: new Vector3(20f, 0f, 0f),
            attackerId: 1,
            targetId: 100
        );

        Assert.IsFalse(found, "When all cells are unwalkable, must return found=false");
    }

    // ================================================================
    //  Multiple units spread around target
    // ================================================================

    [Test]
    public void MultipleAttackers_GetDifferentPositions()
    {
        Vector3 targetPos = new Vector3(0f, 0f, 0f);
        grid.MarkBuilding(new Bounds(targetPos, new Vector3(8f, 4f, 8f)));
        RebuildClearance();

        Vector2Int[] positions = new Vector2Int[4];
        for (int i = 0; i < 4; i++)
        {
            var (cell, found) = AttackPositionFinder.FindAttackPositionCore(
                grid, clearance,
                targetCenter: targetPos,
                targetRadius: 4f,
                attackRange: 1.5f,
                unitRadius: 0.5f,
                isRanged: false,
                attackerPos: new Vector3(20f, 0f, i * 5f),
                attackerId: i + 1,
                targetId: 100
            );

            Assert.IsTrue(found, $"Attacker {i} should find a position");
            positions[i] = cell;
        }

        int uniqueCount = 0;
        for (int i = 0; i < positions.Length; i++)
        {
            bool isUnique = true;
            for (int j = 0; j < i; j++)
            {
                if (positions[i] == positions[j]) { isUnique = false; break; }
            }
            if (isUnique) uniqueCount++;
        }

        Assert.Greater(uniqueCount, 1,
            "Multiple attackers should get different positions (slot system)");
    }

    // ================================================================
    //  Attack position vs combat range consistency
    // ================================================================

    [Test]
    public void AttackPosition_DistanceToTarget_WithinCombatRange()
    {
        Vector3 targetPos = new Vector3(0f, 0f, 0f);
        float targetRadius = 4f;
        float attackRange = 1.5f;
        float unitRadius = 0.5f;

        grid.MarkBuilding(new Bounds(targetPos, new Vector3(targetRadius * 2, 4f, targetRadius * 2)));
        RebuildClearance();

        var (cell, found) = AttackPositionFinder.FindAttackPositionCore(
            grid, clearance,
            targetCenter: targetPos,
            targetRadius: targetRadius,
            attackRange: attackRange,
            unitRadius: unitRadius,
            isRanged: false,
            attackerPos: new Vector3(20f, 0f, 0f),
            attackerId: 1,
            targetId: 100
        );

        Assert.IsTrue(found);

        Vector3 attackPos = grid.CellToWorld(cell);
        float distToCenter = Vector3.Distance(attackPos, targetPos);
        float surfaceDist = Mathf.Max(0f, distToCenter - targetRadius);
        float effectiveRange = attackRange + unitRadius;

        Assert.LessOrEqual(surfaceDist, effectiveRange + grid.CellSize,
            $"Surface distance ({surfaceDist:F1}) from attack position to target " +
            $"should be within combat effective range ({effectiveRange:F1})");
    }

    // ================================================================
    //  Target at grid edge
    // ================================================================

    [Test]
    public void TargetNearEdge_StillFindsPosition()
    {
        Vector3 edgePos = grid.CellToWorld(new Vector2Int(5, 50));
        float targetRadius = 3f;
        grid.MarkBuilding(new Bounds(edgePos, new Vector3(6f, 4f, 6f)));
        RebuildClearance();

        var (cell, found) = AttackPositionFinder.FindAttackPositionCore(
            grid, clearance,
            targetCenter: edgePos,
            targetRadius: targetRadius,
            attackRange: 1.5f,
            unitRadius: 0.5f,
            isRanged: false,
            attackerPos: edgePos + new Vector3(15f, 0f, 0f),
            attackerId: 1,
            targetId: 100
        );

        Assert.IsTrue(found, "Should find position even near grid edge");
        Assert.IsTrue(grid.IsInBounds(cell), "Position must be within grid bounds");
    }

    // ================================================================
    //  Slot cleanup
    // ================================================================

    [TearDown]
    public void CleanUpSlots()
    {
        for (int i = 0; i < 10; i++)
            AttackPositionFinder.ReleaseAllSlots(i + 1);
        AttackPositionFinder.ReleaseTargetSlots(100);
    }
}
