using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Integration tests that verify combat range checks produce correct results
/// when used with REAL bounds from GameObjects with colliders and renderers.
/// These tests catch the bugs where units attack "out of range" or fail to
/// attack when visually close enough.
/// </summary>
[TestFixture]
public class CombatRangeIntegrationTests
{
    private GameObject target;
    private Mesh unitCube;

    [SetUp]
    public void SetUp()
    {
        target = new GameObject("Target");
        unitCube = CreateUnitCube();
    }

    [TearDown]
    public void TearDown()
    {
        if (target != null) Object.DestroyImmediate(target);
        if (unitCube != null) Object.DestroyImmediate(unitCube);
    }

    private Mesh CreateUnitCube()
    {
        var mesh = new Mesh();
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f),
            new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f)
        };
        mesh.triangles = new[]
        {
            0,2,1, 0,3,2, 4,5,6, 4,6,7,
            0,1,5, 0,5,4, 2,3,7, 2,7,6,
            0,4,7, 0,7,3, 1,2,6, 1,6,5
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private void AddRendererChild(string name, Vector3 localPos, Vector3 localScale)
    {
        var child = new GameObject(name);
        child.transform.SetParent(target.transform);
        child.transform.localPosition = localPos;
        child.transform.localScale = localScale;
        var mf = child.AddComponent<MeshFilter>();
        mf.sharedMesh = unitCube;
        child.AddComponent<MeshRenderer>();
    }

    private void SetupBuildingTarget(float colliderHalfX, float colliderHalfZ,
        float rendererHalfX = -1f, float rendererHalfZ = -1f)
    {
        target.transform.position = Vector3.zero;

        if (rendererHalfX < 0f) rendererHalfX = colliderHalfX;
        if (rendererHalfZ < 0f) rendererHalfZ = colliderHalfZ;

        AddRendererChild("Visual", Vector3.zero,
            new Vector3(rendererHalfX * 2f, 4f, rendererHalfZ * 2f));

        var box = target.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(colliderHalfX * 2f, 4f, colliderHalfZ * 2f);
    }

    // ================================================================
    //  Melee unit vs building — range consistency
    // ================================================================

    [Test]
    public void MeleeUnit_JustOutsideRange_NotInRange()
    {
        SetupBuildingTarget(colliderHalfX: 5f, colliderHalfZ: 5f);

        float attackRange = CombatTargeting.GetAttackRange(1.5f, false);
        float unitRadius = 0.5f;
        float effectiveRange = attackRange + unitRadius;

        Vector3 unitPos = new Vector3(5f + effectiveRange + 0.5f, 0f, 0f);
        Vector3 closest = BoundsHelper.ClosestPoint(target, unitPos);
        float dist = Vector3.Distance(unitPos, closest);

        bool inRange = CombatTargeting.IsInRange(dist, effectiveRange, false);
        Assert.IsFalse(inRange, $"Unit at dist={dist:F2} should be OUT of effectiveRange={effectiveRange:F2}");
    }

    [Test]
    public void MeleeUnit_JustInsideRange_InRange()
    {
        SetupBuildingTarget(colliderHalfX: 5f, colliderHalfZ: 5f);

        float attackRange = CombatTargeting.GetAttackRange(1.5f, false);
        float unitRadius = 0.5f;
        float effectiveRange = attackRange + unitRadius;

        Vector3 unitPos = new Vector3(5f + effectiveRange - 0.1f, 0f, 0f);
        Vector3 closest = BoundsHelper.ClosestPoint(target, unitPos);
        float dist = Vector3.Distance(unitPos, closest);

        bool inRange = CombatTargeting.IsInRange(dist, effectiveRange, false);
        Assert.IsTrue(inRange, $"Unit at dist={dist:F2} should be IN effectiveRange={effectiveRange:F2}");
    }

    // ================================================================
    //  IsInRange entry MUST allow damage (GetMaxAttackDistance)
    // ================================================================

    [Test]
    public void InRangeEntry_AlwaysWithinMaxAttackDistance_Melee()
    {
        SetupBuildingTarget(colliderHalfX: 5f, colliderHalfZ: 5f);

        float attackRange = CombatTargeting.GetAttackRange(1.5f, false);
        float unitRadius = 0.5f;
        float effectiveRange = attackRange + unitRadius;
        float maxAttackDist = CombatTargeting.GetMaxAttackDistance(attackRange, unitRadius, false);

        for (float offset = 0f; offset <= effectiveRange + 2f; offset += 0.1f)
        {
            Vector3 unitPos = new Vector3(5f + offset, 0f, 0f);
            Vector3 closest = BoundsHelper.ClosestPoint(target, unitPos);
            float dist = Vector3.Distance(unitPos, closest);

            bool canEnterCombat = CombatTargeting.IsInRange(dist, effectiveRange, false);
            if (canEnterCombat)
            {
                Assert.LessOrEqual(dist, maxAttackDist,
                    $"If IsInRange lets unit in at dist={dist:F2}, " +
                    $"GetMaxAttackDistance ({maxAttackDist:F2}) must not block damage");
            }
        }
    }

    [Test]
    public void InRangeEntry_AlwaysWithinMaxAttackDistance_Ranged()
    {
        SetupBuildingTarget(colliderHalfX: 5f, colliderHalfZ: 5f);

        float attackRange = CombatTargeting.GetAttackRange(5f, true);
        float unitRadius = 0.5f;
        float effectiveRange = attackRange + unitRadius;
        float maxAttackDist = CombatTargeting.GetMaxAttackDistance(attackRange, unitRadius, true);

        for (float offset = 0f; offset <= effectiveRange + 2f; offset += 0.1f)
        {
            Vector3 unitPos = new Vector3(5f + offset, 0f, 0f);
            Vector3 closest = BoundsHelper.ClosestPoint(target, unitPos);
            float dist = Vector3.Distance(unitPos, closest);

            bool canEnterCombat = CombatTargeting.IsInRange(dist, effectiveRange, false);
            if (canEnterCombat)
            {
                Assert.LessOrEqual(dist, maxAttackDist,
                    $"Ranged: if IsInRange lets unit in at dist={dist:F2}, " +
                    $"GetMaxAttackDistance ({maxAttackDist:F2}) must not block damage");
            }
        }
    }

    // ================================================================
    //  Hysteresis disengage MUST be within MaxAttackDistance
    // ================================================================

    [Test]
    public void HysteresisRange_WithinMaxAttackDistance()
    {
        float[] attackRanges = { 0.3f, 1f, 1.5f, 2f };
        float[] unitRadii = { 0.3f, 0.5f, 1.0f, 2.5f };
        bool[] isRangedOptions = { false, true };

        foreach (float ar in attackRanges)
        foreach (float ur in unitRadii)
        foreach (bool ranged in isRangedOptions)
        {
            float attackRange = CombatTargeting.GetAttackRange(ar, ranged);
            float effectiveRange = attackRange + ur;
            float disengageDist = effectiveRange + effectiveRange * 0.15f;
            float maxAttackDist = CombatTargeting.GetMaxAttackDistance(attackRange, ur, ranged);

            Assert.LessOrEqual(disengageDist, maxAttackDist,
                $"Hysteresis disengage ({disengageDist:F2}) must be within " +
                $"MaxAttackDistance ({maxAttackDist:F2}) for ar={ar} ur={ur} ranged={ranged}");
        }
    }

    // ================================================================
    //  Tolerance entry MUST be within MaxAttackDistance
    // ================================================================

    [Test]
    public void ToleranceEntry_WithinMaxAttackDistance()
    {
        float[] attackRanges = { 0.3f, 1f, 1.5f, 2f };
        float[] unitRadii = { 0.3f, 0.5f, 1.0f, 2.5f };

        foreach (float ar in attackRanges)
        foreach (float ur in unitRadii)
        {
            float attackRange = CombatTargeting.GetAttackRange(ar, false);
            float effectiveRange = attackRange + ur;
            float maxAttackDist = CombatTargeting.GetMaxAttackDistance(attackRange, ur, false);

            for (int retries = 0; retries <= 5; retries++)
            {
                float maxTolerance = 0.15f * effectiveRange + 0.5f;
                float arrivalTolerance = Mathf.Min(0.5f + retries * 0.5f, maxTolerance);
                float maxEntryDist = effectiveRange + arrivalTolerance;

                Assert.LessOrEqual(maxEntryDist, maxAttackDist + 0.01f,
                    $"Tolerance entry ({maxEntryDist:F4}) must be within " +
                    $"MaxAttackDistance ({maxAttackDist:F4}) for ar={ar} ur={ur} retries={retries}");
            }
        }
    }

    // ================================================================
    //  Building with big dome — the "total mess" scenario
    // ================================================================

    [Test]
    public void BigDomeBuilding_UnitsOutsideCollider_NotInRange()
    {
        target.transform.position = Vector3.zero;

        AddRendererChild("Walls", Vector3.zero, new Vector3(8f, 4f, 8f));
        AddRendererChild("Dome", new Vector3(0, 6, 0), new Vector3(30f, 10f, 30f));

        var box = target.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(8f, 8f, 8f);

        float attackRange = CombatTargeting.GetAttackRange(1.5f, false);
        float unitRadius = 0.5f;
        float effectiveRange = attackRange + unitRadius;

        Vector3 unitPos = new Vector3(12f, 0f, 0f);
        Vector3 closest = BoundsHelper.ClosestPoint(target, unitPos);
        float dist = Vector3.Distance(unitPos, closest);

        BoundsHelper.TryGetCombinedBounds(target, out var rendBounds);
        bool insideRendererAABB = rendBounds.Contains(unitPos);

        Assert.IsFalse(CombatTargeting.IsInRange(dist, effectiveRange, false),
            $"Unit at {unitPos} (dist to collider = {dist:F1}) should NOT be in range. " +
            $"InsideRendererAABB={insideRendererAABB}. " +
            "This catches the 'total mess' bug where renderer bounds let everyone in.");
    }

    [Test]
    public void BigDomeBuilding_ClosestPoint_MeasuresToCollider()
    {
        target.transform.position = Vector3.zero;

        AddRendererChild("Walls", Vector3.zero, new Vector3(8f, 4f, 8f));
        AddRendererChild("Dome", new Vector3(0, 6, 0), new Vector3(30f, 10f, 30f));

        var box = target.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(8f, 8f, 8f);

        Vector3 unitPos = new Vector3(10f, 0f, 0f);
        Vector3 closest = BoundsHelper.ClosestPoint(target, unitPos);
        float dist = Vector3.Distance(unitPos, closest);

        Assert.AreEqual(6f, dist, 0.1f,
            "Distance should be to collider surface (10-4=6), not renderer");
    }

    // ================================================================
    //  Distance symmetry (approach from different sides)
    // ================================================================

    [Test]
    public void Distance_SameFromAllSides_SquareBuilding()
    {
        SetupBuildingTarget(colliderHalfX: 5f, colliderHalfZ: 5f);
        float queryDist = 8f;

        Vector3[] positions =
        {
            new Vector3(queryDist, 0, 0),
            new Vector3(-queryDist, 0, 0),
            new Vector3(0, 0, queryDist),
            new Vector3(0, 0, -queryDist),
        };

        float[] distances = new float[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 closest = BoundsHelper.ClosestPoint(target, positions[i]);
            distances[i] = Vector3.Distance(positions[i], closest);
        }

        for (int i = 1; i < distances.Length; i++)
        {
            Assert.AreEqual(distances[0], distances[i], 0.01f,
                $"Distance from side {i} should equal side 0 for a square building");
        }
    }
}
