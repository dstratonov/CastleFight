using NUnit.Framework;
using UnityEngine;
using Mirror;

/// <summary>
/// Tests BoundsHelper with REAL GameObjects, Colliders, and Renderers.
/// This class is the root cause of every footprint/occlusion bug we've had.
/// </summary>
[TestFixture]
public class BoundsHelperTests
{
    private GameObject root;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("TestRoot");
    }

    [TearDown]
    public void TearDown()
    {
        if (root != null)
            Object.DestroyImmediate(root);
    }

    private GameObject CreateChild(string name, Vector3 localPos, Vector3 localScale)
    {
        var child = new GameObject(name);
        child.transform.SetParent(root.transform);
        child.transform.localPosition = localPos;
        child.transform.localScale = localScale;
        return child;
    }

    private void AddRenderer(GameObject go, Vector3? boundsSize = null)
    {
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = CreateUnitCube();
        go.AddComponent<MeshRenderer>();
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

    // ================================================================
    //  TryGetColliderBounds — root collider preferred
    // ================================================================

    [Test]
    public void ColliderBounds_RootBoxCollider_ReturnsCorrectSize()
    {
        var box = root.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(6f, 4f, 8f);

        bool found = BoundsHelper.TryGetColliderBounds(root, out var bounds);

        Assert.IsTrue(found);
        Assert.AreEqual(6f, bounds.size.x, 0.01f);
        Assert.AreEqual(4f, bounds.size.y, 0.01f);
        Assert.AreEqual(8f, bounds.size.z, 0.01f);
    }

    [Test]
    public void ColliderBounds_TriggerCollider_IsIgnored()
    {
        var trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(20f, 20f, 20f);

        bool found = BoundsHelper.TryGetColliderBounds(root, out _);

        Assert.IsFalse(found, "Trigger colliders must be ignored");
    }

    [Test]
    public void ColliderBounds_RootPreferredOverChild()
    {
        var rootBox = root.AddComponent<BoxCollider>();
        rootBox.size = new Vector3(6f, 4f, 6f);

        var child = CreateChild("ChildMesh", Vector3.zero, Vector3.one);
        var childBox = child.AddComponent<BoxCollider>();
        childBox.size = new Vector3(20f, 20f, 20f);

        bool found = BoundsHelper.TryGetColliderBounds(root, out var bounds);

        Assert.IsTrue(found);
        Assert.AreEqual(6f, bounds.size.x, 0.01f, "Root collider must be preferred over child");
    }

    [Test]
    public void ColliderBounds_FallsBackToChildWhenNoRoot()
    {
        var child = CreateChild("ChildMesh", Vector3.zero, Vector3.one);
        var childBox = child.AddComponent<BoxCollider>();
        childBox.size = new Vector3(10f, 5f, 10f);

        bool found = BoundsHelper.TryGetColliderBounds(root, out var bounds);

        Assert.IsTrue(found);
        Assert.AreEqual(10f, bounds.size.x, 0.01f);
    }

    [Test]
    public void ColliderBounds_NoColliders_ReturnsFalse()
    {
        bool found = BoundsHelper.TryGetColliderBounds(root, out _);
        Assert.IsFalse(found);
    }

    // ================================================================
    //  TryGetCombinedBounds — renderer bounds
    // ================================================================

    [Test]
    public void RendererBounds_SingleChild_MatchesScale()
    {
        var child = CreateChild("Visual", Vector3.zero, new Vector3(8f, 10f, 8f));
        AddRenderer(child);

        bool found = BoundsHelper.TryGetCombinedBounds(root, out var bounds);

        Assert.IsTrue(found);
        Assert.AreEqual(8f, bounds.size.x, 0.1f);
        Assert.AreEqual(10f, bounds.size.y, 0.1f);
        Assert.AreEqual(8f, bounds.size.z, 0.1f);
    }

    [Test]
    public void RendererBounds_MultipleChildren_Encapsulates()
    {
        var wall = CreateChild("Wall", new Vector3(0, 0, 0), new Vector3(8f, 4f, 8f));
        AddRenderer(wall);
        var roof = CreateChild("Roof", new Vector3(0, 6, 0), new Vector3(12f, 4f, 12f));
        AddRenderer(roof);

        bool found = BoundsHelper.TryGetCombinedBounds(root, out var bounds);

        Assert.IsTrue(found);
        Assert.AreEqual(12f, bounds.size.x, 0.1f, "Should encapsulate the wider roof");
        Assert.AreEqual(12f, bounds.size.z, 0.1f, "Should encapsulate the wider roof");
    }

    [Test]
    public void RendererBounds_NoRenderers_ReturnsFalse()
    {
        bool found = BoundsHelper.TryGetCombinedBounds(root, out _);
        Assert.IsFalse(found);
    }

    // ================================================================
    //  GetPhysicalBounds — collider preferred, renderer fallback
    // ================================================================

    [Test]
    public void PhysicalBounds_PrefersCollider()
    {
        var box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(6f, 4f, 6f);

        var child = CreateChild("Visual", Vector3.zero, new Vector3(12f, 10f, 12f));
        AddRenderer(child);

        var result = BoundsHelper.GetPhysicalBounds(root);

        Assert.AreEqual(6f, result.size.x, 0.1f, "Physical bounds must use collider, not renderer");
    }

    [Test]
    public void PhysicalBounds_FallsBackToRenderer()
    {
        var child = CreateChild("Visual", Vector3.zero, new Vector3(8f, 4f, 8f));
        AddRenderer(child);

        var result = BoundsHelper.GetPhysicalBounds(root);

        Assert.AreEqual(8f, result.size.x, 0.1f, "Without collider, should use renderer");
    }

    [Test]
    public void PhysicalBounds_FallsBackToDefaultWhenEmpty()
    {
        var result = BoundsHelper.GetPhysicalBounds(root);

        Assert.AreEqual(2f, result.size.x, 0.01f, "Empty object should get 2x2x2 default");
    }

    // ================================================================
    //  GetCenter — must use physical bounds
    // ================================================================

    [Test]
    public void GetCenter_UsesColliderNotRenderer()
    {
        root.transform.position = new Vector3(10f, 0f, 20f);
        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(2f, 3f, 1f);
        box.size = new Vector3(6f, 6f, 6f);

        var child = CreateChild("BigRoof", new Vector3(0, 10, 0), new Vector3(20f, 5f, 20f));
        AddRenderer(child);

        Vector3 center = BoundsHelper.GetCenter(root);

        Assert.AreEqual(12f, center.x, 0.1f, "Center X should come from collider");
        Assert.AreEqual(0f, center.y, 0.01f, "Center Y should be transform Y");
        Assert.AreEqual(21f, center.z, 0.1f, "Center Z should come from collider");
    }

    // ================================================================
    //  GetRadius — XZ extent of physical bounds
    // ================================================================

    [Test]
    public void GetRadius_MatchesColliderExtent()
    {
        var box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(10f, 4f, 6f);

        float radius = BoundsHelper.GetRadius(root);

        Assert.AreEqual(5f, radius, 0.01f, "Radius should be max XZ extent (10/2=5)");
    }

    [Test]
    public void GetRadius_IgnoresRendererWhenColliderExists()
    {
        var box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(6f, 4f, 6f);

        var child = CreateChild("BigRoof", Vector3.zero, new Vector3(20f, 10f, 20f));
        AddRenderer(child);

        float radius = BoundsHelper.GetRadius(root);

        Assert.AreEqual(3f, radius, 0.1f, "Radius must come from collider (3), not renderer (10)");
    }

    // ================================================================
    //  ClosestPoint — must match spatial systems
    // ================================================================

    [Test]
    public void ClosestPoint_OnColliderSurface_NotRenderer()
    {
        root.transform.position = Vector3.zero;
        var box = root.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(6f, 4f, 6f);

        var child = CreateChild("BigRoof", Vector3.zero, new Vector3(20f, 10f, 20f));
        AddRenderer(child);

        Vector3 queryFrom = new Vector3(10f, 0f, 0f);
        Vector3 closest = BoundsHelper.ClosestPoint(root, queryFrom);

        Assert.AreEqual(3f, closest.x, 0.01f, "Closest point must be on collider surface (x=3), not renderer");
    }

    [Test]
    public void ClosestPoint_InsideBounds_ReturnsQueryPoint()
    {
        root.transform.position = Vector3.zero;
        var box = root.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(10f, 10f, 10f);

        Vector3 queryFrom = new Vector3(1f, 0f, 1f);
        Vector3 closest = BoundsHelper.ClosestPoint(root, queryFrom);

        Assert.AreEqual(1f, closest.x, 0.01f, "Point inside bounds returns the point itself");
        Assert.AreEqual(1f, closest.z, 0.01f);
    }

    [Test]
    public void ClosestPoint_Distance_ConsistentWithIsInRange()
    {
        root.transform.position = Vector3.zero;
        var box = root.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(10f, 4f, 10f);

        float attackRange = 2f;
        float unitRadius = 0.5f;
        float effectiveRange = attackRange + unitRadius;

        Vector3 unitPos = new Vector3(8f, 0f, 0f);
        Vector3 closest = BoundsHelper.ClosestPoint(root, unitPos);
        float dist = Vector3.Distance(unitPos, closest);

        Assert.AreEqual(3f, dist, 0.01f, "Distance to collider surface should be 8-5=3");
        Assert.IsTrue(dist > effectiveRange, "Unit at dist=3 should be OUT of range 2.5");

        Vector3 closeUnit = new Vector3(7f, 0f, 0f);
        Vector3 closest2 = BoundsHelper.ClosestPoint(root, closeUnit);
        float dist2 = Vector3.Distance(closeUnit, closest2);

        Assert.AreEqual(2f, dist2, 0.01f, "Distance should be 7-5=2");
        Assert.IsTrue(dist2 <= effectiveRange, "Unit at dist=2 should be IN range 2.5");
    }

    // ================================================================
    //  Collider vs Renderer mismatch — the exact bug pattern
    // ================================================================

    [Test]
    public void BuildingWithBigRoof_ColliderSmallerThanRenderer()
    {
        root.transform.position = Vector3.zero;

        var walls = CreateChild("Walls", new Vector3(0, 2, 0), new Vector3(8f, 4f, 8f));
        AddRenderer(walls);

        var roof = CreateChild("Roof", new Vector3(0, 6, 0), new Vector3(16f, 4f, 16f));
        AddRenderer(roof);

        var box = root.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(8f, 8f, 8f);

        BoundsHelper.TryGetCombinedBounds(root, out var rendererBounds);
        BoundsHelper.TryGetColliderBounds(root, out var colliderBounds);

        Assert.Greater(rendererBounds.size.x, colliderBounds.size.x,
            "Renderer bounds (includes roof) must be larger than collider bounds (walls only)");

        float radius = BoundsHelper.GetRadius(root);
        Assert.AreEqual(4f, radius, 0.1f,
            "GetRadius must use collider (4), not renderer which includes roof");

        Vector3 unitFarAway = new Vector3(20f, 0f, 0f);
        Vector3 closestPhysical = BoundsHelper.ClosestPoint(root, unitFarAway);
        float dist = Vector3.Distance(unitFarAway, closestPhysical);

        Assert.AreEqual(16f, dist, 0.1f,
            "Distance should be to collider surface (20-4=16), not renderer surface (20-8=12)");
    }

    [Test]
    public void UnitInsideRendererBounds_OutsideColliderBounds_CorrectDistance()
    {
        root.transform.position = Vector3.zero;

        var walls = CreateChild("Walls", Vector3.zero, new Vector3(6f, 4f, 6f));
        AddRenderer(walls);

        var dome = CreateChild("Dome", new Vector3(0, 4, 0), new Vector3(20f, 8f, 20f));
        AddRenderer(dome);

        var box = root.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(6f, 8f, 6f);

        Vector3 unitPos = new Vector3(8f, 0f, 0f);
        BoundsHelper.TryGetCombinedBounds(root, out var rendBounds);
        bool insideRenderer = rendBounds.Contains(unitPos);

        Vector3 closest = BoundsHelper.ClosestPoint(root, unitPos);
        float dist = Vector3.Distance(unitPos, closest);

        if (insideRenderer)
        {
            Assert.Greater(dist, 0f,
                "Unit inside renderer but outside collider must NOT report distance=0. " +
                "This was the 'total mess' bug — units everywhere inside the renderer AABB were considered in-range.");
        }
    }
}

[TestFixture]
public class TargetingStateTests
{
    private GameObject targetObject;
    private Health targetHealth;
    private TargetingState targetingState;

    [SetUp]
    public void SetUp()
    {
        targetObject = new GameObject("PivotTarget");
        targetObject.AddComponent<NetworkIdentity>();
        targetHealth = targetObject.AddComponent<Health>();
        targetingState = new TargetingState();
    }

    [TearDown]
    public void TearDown()
    {
        if (targetObject != null)
            Object.DestroyImmediate(targetObject);
    }

    [Test]
    public void Validate_HardTarget_UsesPhysicalBoundsInsteadOfPivot()
    {
        var target = new DummyAttackable(
            targetObject,
            targetHealth,
            new Vector3(10f, 0f, 0f),
            new Bounds(new Vector3(10f, 0f, 0f), new Vector3(4f, 4f, 4f)),
            TargetPriority.Building);

        targetingState.ForceSetTarget(target);

        bool valid = targetingState.Validate(new Vector3(7f, 0f, 0f), 2f);

        Assert.IsTrue(valid, "Leash should measure to the building body, not the pivot.");
        Assert.IsTrue(targetingState.HasTarget, "Target should remain locked when still near the target footprint.");
    }

    [Test]
    public void Validate_HardTarget_ClearsWhenOutsidePhysicalBoundsLeash()
    {
        var target = new DummyAttackable(
            targetObject,
            targetHealth,
            new Vector3(10f, 0f, 0f),
            new Bounds(new Vector3(10f, 0f, 0f), new Vector3(4f, 4f, 4f)),
            TargetPriority.Building);

        targetingState.ForceSetTarget(target);

        bool valid = targetingState.Validate(new Vector3(4f, 0f, 0f), 2f);

        Assert.IsFalse(valid);
        Assert.IsFalse(targetingState.HasTarget);
    }

    private sealed class DummyAttackable : IAttackable
    {
        public DummyAttackable(GameObject gameObject, Health health, Vector3 position, Bounds worldBounds, TargetPriority priority)
        {
            this.gameObject = gameObject;
            Health = health;
            Position = position;
            WorldBounds = worldBounds;
            Priority = priority;
        }

        public GameObject gameObject { get; }
        public Health Health { get; }
        public int TeamId => 1;
        public ArmorType ArmorType => ArmorType.Fortified;
        public Vector3 Position { get; }
        public float TargetRadius => 0f;
        public Bounds WorldBounds { get; }
        public TargetPriority Priority { get; }
    }
}

[TestFixture]
public class AttackRangeHelperFindAttackPositionTests
{
    [Test]
    public void FindAttackPosition_RectStructure_UsesClosestPerimeterAndPathingStandOff()
    {
        var targetObject = new GameObject("StructureTarget");
        try
        {
            var target = new DummyStructureAttackable(
                targetObject,
                new Bounds(Vector3.zero, new Vector3(10f, 4f, 10f)));

            Vector3 destination = AttackRangeHelper.FindAttackPosition(
                new Vector3(20f, 0f, 0f),
                1.015f,
                0.5f,
                target,
                attackerUnitId: 7);

            Assert.AreEqual(6.17f, destination.x, 0.1f);
            Assert.AreEqual(0f, destination.z, 0.05f);
        }
        finally
        {
            Object.DestroyImmediate(targetObject);
        }
    }

    [Test]
    public void FindAttackPosition_RangedStructure_UsesOuterFiringBand()
    {
        var targetObject = new GameObject("RangedStructureTarget");
        try
        {
            var target = new DummyStructureAttackable(
                targetObject,
                new Bounds(Vector3.zero, new Vector3(10f, 4f, 10f)));

            Vector3 destination = AttackRangeHelper.FindAttackPosition(
                new Vector3(25f, 0f, 0f),
                0.5f,
                6f,
                target,
                attackerUnitId: 13);

            Assert.AreEqual(10f, destination.x, 0.1f);
            Assert.AreEqual(0f, destination.z, 0.05f);
        }
        finally
        {
            Object.DestroyImmediate(targetObject);
        }
    }

    [Test]
    public void RoundedStructurePerimeter_CornerUsesArcInsteadOfSharpCorner()
    {
        var method = typeof(AttackRangeHelper).GetMethod(
            "GetRoundedRectPerimeterPoint",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method, "Rounded structure perimeter helper should exist.");

        Bounds bounds = new(Vector3.zero, new Vector3(10f, 4f, 10f));
        float buffer = 2f;
        float t = bounds.size.x + 0.25f * Mathf.PI * buffer;
        Vector3 point = (Vector3)method.Invoke(null, new object[] { bounds, t, buffer });

        Assert.Greater(point.x, bounds.max.x + 0.1f, "Rounded corner should bulge out past the east wall.");
        Assert.Less(point.z, bounds.min.z - 0.1f, "Rounded corner should bulge out past the south wall.");
        Assert.Less(point.x, bounds.max.x + buffer - 0.1f, "Corner point should sit on an arc, not snap to the extreme box corner.");
        Assert.Greater(point.z, bounds.min.z - buffer + 0.1f, "Corner point should sit on an arc, not snap to the extreme box corner.");
    }

    [Test]
    public void FindAttackPosition_RoundTarget_StaysOnApproachSide()
    {
        var targetObject = new GameObject("RoundTarget");
        try
        {
            var target = new DummyRoundAttackable(
                targetObject,
                Vector3.zero,
                1f);

            Vector3 destination = AttackRangeHelper.FindAttackPosition(
                new Vector3(8f, 0f, 0f),
                0.3f,
                0.5f,
                target,
                attackerUnitId: 5);

            Assert.Greater(destination.x, 1.3f);
            Assert.AreEqual(0f, destination.z, 0.05f);
        }
        finally
        {
            Object.DestroyImmediate(targetObject);
        }
    }

    private sealed class DummyStructureAttackable : IAttackable
    {
        public DummyStructureAttackable(GameObject gameObject, Bounds worldBounds)
        {
            this.gameObject = gameObject;
            WorldBounds = worldBounds;
        }

        public GameObject gameObject { get; }
        public Health Health => null;
        public int TeamId => 1;
        public ArmorType ArmorType => ArmorType.Fortified;
        public Vector3 Position => WorldBounds.center;
        public float TargetRadius => 0f;
        public Bounds WorldBounds { get; }
        public TargetPriority Priority => TargetPriority.Building;
    }

    private sealed class DummyRoundAttackable : IAttackable
    {
        public DummyRoundAttackable(GameObject gameObject, Vector3 position, float radius)
        {
            this.gameObject = gameObject;
            Position = position;
            TargetRadius = radius;
        }

        public GameObject gameObject { get; }
        public Health Health => null;
        public int TeamId => 1;
        public ArmorType ArmorType => ArmorType.Medium;
        public Vector3 Position { get; }
        public float TargetRadius { get; }
        public Bounds WorldBounds => new(Position, Vector3.one * TargetRadius * 2f);
        public TargetPriority Priority => TargetPriority.Unit;
    }
}
