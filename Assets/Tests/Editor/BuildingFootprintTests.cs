using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests Building.FitFootprintCollider and grid cell occupancy with REAL GameObjects.
/// Every occlusion/footprint bug originated from untested collider sizing.
/// </summary>
[TestFixture]
public class BuildingFootprintTests
{
    private GameObject building;

    [SetUp]
    public void SetUp()
    {
        building = new GameObject("TestBuilding");
    }

    [TearDown]
    public void TearDown()
    {
        if (building != null)
            Object.DestroyImmediate(building);
    }

    private GameObject AddChildMesh(string name, Vector3 localPos, Vector3 localScale)
    {
        var child = new GameObject(name);
        child.transform.SetParent(building.transform);
        child.transform.localPosition = localPos;
        child.transform.localScale = localScale;
        var mf = child.AddComponent<MeshFilter>();
        mf.sharedMesh = CreateUnitCube();
        child.AddComponent<MeshRenderer>();
        return child;
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
    //  Explicit footprint size
    // ================================================================

    [Test]
    public void ExplicitFootprint_CreatesCorrectCollider()
    {
        AddChildMesh("Walls", Vector3.zero, new Vector3(10f, 5f, 10f));

        Building.FitFootprintCollider(building, new Vector2(6f, 8f));

        var box = building.GetComponent<BoxCollider>();
        Assert.IsNotNull(box, "Should create a BoxCollider");
        Assert.AreEqual(6f, box.bounds.size.x, 0.1f);
        Assert.AreEqual(8f, box.bounds.size.z, 0.1f);
    }

    [Test]
    public void ExplicitFootprint_OverridesRendererSize()
    {
        AddChildMesh("BigModel", Vector3.zero, new Vector3(20f, 10f, 20f));

        Building.FitFootprintCollider(building, new Vector2(5f, 5f));

        var box = building.GetComponent<BoxCollider>();
        Assert.AreEqual(5f, box.bounds.size.x, 0.1f,
            "Explicit size must override renderer size");
    }

    [Test]
    public void ExplicitFootprint_ZeroSize_TriggersAutoDetect()
    {
        AddChildMesh("Walls", Vector3.zero, new Vector3(10f, 5f, 10f));

        Building.FitFootprintCollider(building, Vector2.zero);

        var box = building.GetComponent<BoxCollider>();
        Assert.IsNotNull(box);
        Assert.Greater(box.bounds.size.x, 0f, "Auto-detect should produce non-zero collider");
    }

    // ================================================================
    //  Auto-detect footprint
    // ================================================================

    [Test]
    public void AutoDetect_SimpleBox_ReasonableSize()
    {
        AddChildMesh("Walls", Vector3.zero, new Vector3(8f, 6f, 8f));

        Building.FitFootprintCollider(building, Vector2.zero);

        var box = building.GetComponent<BoxCollider>();
        Assert.Greater(box.bounds.size.x, 5f, "Should not shrink too aggressively");
        Assert.LessOrEqual(box.bounds.size.x, 8.1f, "Should not exceed renderer");
    }

    [Test]
    public void AutoDetect_TallRoof_FootprintSmallerThanFullBounds()
    {
        AddChildMesh("Walls", new Vector3(0, 2, 0), new Vector3(8f, 4f, 8f));
        AddChildMesh("Roof", new Vector3(0, 8, 0), new Vector3(14f, 4f, 14f));

        BoundsHelper.TryGetCombinedBounds(building, out var fullBounds);
        float fullX = fullBounds.size.x;

        Building.FitFootprintCollider(building, Vector2.zero);

        var box = building.GetComponent<BoxCollider>();
        Assert.Less(box.bounds.size.x, fullX,
            "Footprint should be smaller than full renderer bounds (roof extends beyond walls)");
    }

    [Test]
    public void AutoDetect_MaxFootprintScale_Caps()
    {
        AddChildMesh("Huge", Vector3.zero, new Vector3(20f, 20f, 20f));

        Building.FitFootprintCollider(building, Vector2.zero);

        BoundsHelper.TryGetCombinedBounds(building, out var fullBounds);
        var box = building.GetComponent<BoxCollider>();

        float ratio = box.bounds.size.x / fullBounds.size.x;
        Assert.LessOrEqual(ratio, 0.86f, "Must cap at MaxFootprintScale (0.85)");
        Assert.GreaterOrEqual(ratio, 0.84f, "Should be close to 0.85");
    }

    // ================================================================
    //  Castle uses same path as Building
    // ================================================================

    [Test]
    public void CastleAndBuilding_SameModel_SameFootprint()
    {
        var castleObj = new GameObject("TestCastle");
        try
        {
            var wallsB = AddChildMesh("Walls", Vector3.zero, new Vector3(10f, 6f, 10f));
            Building.FitFootprintCollider(building, Vector2.zero);
            var buildingBox = building.GetComponent<BoxCollider>();

            var wallsC = new GameObject("Walls");
            wallsC.transform.SetParent(castleObj.transform);
            wallsC.transform.localPosition = Vector3.zero;
            wallsC.transform.localScale = new Vector3(10f, 6f, 10f);
            var mf = wallsC.AddComponent<MeshFilter>();
            mf.sharedMesh = CreateUnitCube();
            wallsC.AddComponent<MeshRenderer>();

            Building.FitFootprintCollider(castleObj, Vector2.zero);
            var castleBox = castleObj.GetComponent<BoxCollider>();

            Assert.AreEqual(buildingBox.bounds.size.x, castleBox.bounds.size.x, 0.01f,
                "Castle and Building with same model must get same footprint");
            Assert.AreEqual(buildingBox.bounds.size.z, castleBox.bounds.size.z, 0.01f);
        }
        finally
        {
            Object.DestroyImmediate(castleObj);
        }
    }

    // ================================================================
    //  Grid occupancy from footprint
    // ================================================================

    [Test]
    public void GridCells_MatchColliderNotRenderer()
    {
        building.transform.position = Vector3.zero;

        AddChildMesh("Walls", Vector3.zero, new Vector3(8f, 4f, 8f));
        AddChildMesh("Dome", new Vector3(0, 6, 0), new Vector3(20f, 8f, 20f));

        Building.FitFootprintCollider(building, new Vector2(8f, 8f));

        Bounds physicalBounds = BoundsHelper.GetPhysicalBounds(building);
        BoundsHelper.TryGetCombinedBounds(building, out var rendererBounds);

        var grid = new GridLogic(100, 100, 2f, new Vector3(-100f, 0f, -100f));
        var physicalCells = grid.GetCellsOverlappingBounds(physicalBounds);
        var rendererCells = grid.GetCellsOverlappingBounds(rendererBounds);

        Assert.Less(physicalCells.Count, rendererCells.Count,
            "Physical footprint cells must be fewer than renderer-based cells");
        Assert.Greater(physicalCells.Count, 0, "Must occupy at least some cells");
    }

    [Test]
    public void GridCells_CountMatchesExpected()
    {
        building.transform.position = Vector3.zero;
        AddChildMesh("Box", Vector3.zero, new Vector3(6f, 4f, 6f));

        Building.FitFootprintCollider(building, new Vector2(6f, 6f));

        Bounds bounds = BoundsHelper.GetPhysicalBounds(building);
        var grid = new GridLogic(100, 100, 2f, new Vector3(-100f, 0f, -100f));
        var cells = grid.GetCellsOverlappingBounds(bounds);

        Assert.AreEqual(9, cells.Count, "6x6 footprint on 2-unit grid should be 3x3=9 cells");
    }

    // ================================================================
    //  Footprint consistency with distance checks
    // ================================================================

    [Test]
    public void FootprintDistance_ConsistentBetweenGridAndCombat()
    {
        building.transform.position = Vector3.zero;
        AddChildMesh("Walls", Vector3.zero, new Vector3(10f, 5f, 10f));
        Building.FitFootprintCollider(building, new Vector2(10f, 10f));

        Bounds physBounds = BoundsHelper.GetPhysicalBounds(building);
        Vector3 center = BoundsHelper.GetCenter(building);
        float radius = BoundsHelper.GetRadius(building);

        Assert.AreEqual(physBounds.center.x, center.x, 0.1f,
            "GetCenter must agree with GetPhysicalBounds center");
        Assert.AreEqual(physBounds.extents.x, radius, 0.1f,
            "GetRadius must agree with physical bounds extents");

        Vector3 unitPos = new Vector3(8f, 0f, 0f);
        Vector3 closest = BoundsHelper.ClosestPoint(building, unitPos);
        float combatDist = Vector3.Distance(unitPos, closest);

        float expectedDist = unitPos.x - physBounds.max.x;
        Assert.AreEqual(Mathf.Max(0f, expectedDist), combatDist, 0.1f,
            "Combat distance must match physical bounds surface distance");
    }

    // ================================================================
    //  Edge case: no renderers
    // ================================================================

    [Test]
    public void FitFootprint_NoRenderers_DoesNotCrash()
    {
        Building.FitFootprintCollider(building, new Vector2(5f, 5f));

        var box = building.GetComponent<BoxCollider>();
        Assert.IsNull(box, "No renderers → TryGetCombinedBounds fails → no collider added");
    }

    [Test]
    public void FitFootprint_NoRenderers_AutoDetect_DoesNotCrash()
    {
        Building.FitFootprintCollider(building, Vector2.zero);
        var box = building.GetComponent<BoxCollider>();
        Assert.IsNull(box);
    }
}
