using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class NavMeshDataTests
{
    private NavMeshData CreateSimpleMesh()
    {
        // Two adjacent triangles forming a quad:
        //   v2---v3
        //   | \ |
        //   v0---v1
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));  // v0
        mesh.AddVertex(new Vector2(10f, 0f)); // v1
        mesh.AddVertex(new Vector2(0f, 10f)); // v2
        mesh.AddVertex(new Vector2(10f, 10f));// v3

        mesh.AddTriangle(0, 1, 2, walkable: true);  // t0: (v0,v1,v2)
        mesh.AddTriangle(1, 3, 2, walkable: true);  // t1: (v1,v3,v2)

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();
        return mesh;
    }

    [Test]
    public void AddVertex_ReturnsIncrementingIds()
    {
        var mesh = new NavMeshData();
        Assert.AreEqual(0, mesh.AddVertex(Vector2.zero));
        Assert.AreEqual(1, mesh.AddVertex(Vector2.one));
        Assert.AreEqual(2, mesh.VertexCount);
    }

    [Test]
    public void AddTriangle_SetsWalkability()
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));
        mesh.AddVertex(new Vector2(5f, 0f));
        mesh.AddVertex(new Vector2(0f, 5f));

        int t0 = mesh.AddTriangle(0, 1, 2, walkable: true);
        Assert.IsTrue(mesh.Triangles[t0].IsWalkable);

        mesh.AddVertex(new Vector2(5f, 5f));
        int t1 = mesh.AddTriangle(1, 3, 2, walkable: false);
        Assert.IsFalse(mesh.Triangles[t1].IsWalkable);
    }

    [Test]
    public void BuildAdjacency_FindsSharedEdges()
    {
        var mesh = CreateSimpleMesh();

        bool t0HasNeighbor = mesh.Triangles[0].N0 >= 0 || mesh.Triangles[0].N1 >= 0 || mesh.Triangles[0].N2 >= 0;
        bool t1HasNeighbor = mesh.Triangles[1].N0 >= 0 || mesh.Triangles[1].N1 >= 0 || mesh.Triangles[1].N2 >= 0;

        Assert.IsTrue(t0HasNeighbor, "Triangle 0 should have at least one neighbor");
        Assert.IsTrue(t1HasNeighbor, "Triangle 1 should have at least one neighbor");
    }

    [Test]
    public void BuildAdjacency_NeighborsAreSymmetric()
    {
        var mesh = CreateSimpleMesh();

        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            for (int e = 0; e < 3; e++)
            {
                int neighbor = mesh.Triangles[i].GetNeighbor(e);
                if (neighbor < 0) continue;

                bool foundReverse = false;
                for (int ne = 0; ne < 3; ne++)
                {
                    if (mesh.Triangles[neighbor].GetNeighbor(ne) == i)
                    { foundReverse = true; break; }
                }
                Assert.IsTrue(foundReverse, $"Tri {i} has neighbor {neighbor}, but reverse not found");
            }
        }
    }

    [Test]
    public void ComputeAllWidths_ProducesPositiveWidths()
    {
        var mesh = CreateSimpleMesh();

        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            for (int e = 0; e < 3; e++)
            {
                float w = mesh.Triangles[i].GetWidth(e);
                Assert.GreaterOrEqual(w, 0f, $"Width of tri {i} edge {e} should be >= 0");
            }
        }
    }

    [Test]
    public void GetCentroid_ReturnsPointInsideTriangle()
    {
        var mesh = CreateSimpleMesh();
        Vector2 centroid = mesh.GetCentroid(0);

        Assert.Greater(centroid.x, -0.1f);
        Assert.Greater(centroid.y, -0.1f);
        Assert.Less(centroid.x, 10.1f);
        Assert.Less(centroid.y, 10.1f);
    }

    [Test]
    public void Cross2D_CorrectSignAndMagnitude()
    {
        Assert.AreEqual(1f, NavMeshData.Cross2D(new Vector2(1, 0), new Vector2(0, 1)), 0.001f, "CCW = +1");
        Assert.AreEqual(-1f, NavMeshData.Cross2D(new Vector2(0, 1), new Vector2(1, 0)), 0.001f, "CW = -1");
        Assert.AreEqual(0f, NavMeshData.Cross2D(new Vector2(2, 0), new Vector2(5, 0)), 0.001f, "Parallel = 0");
    }

    [Test]
    public void PointInTriangle_InsideReturnsTrue()
    {
        Vector2 a = new Vector2(0f, 0f);
        Vector2 b = new Vector2(10f, 0f);
        Vector2 c = new Vector2(0f, 10f);

        Assert.IsTrue(NavMeshData.PointInTriangle(new Vector2(2f, 2f), a, b, c));
        Assert.IsTrue(NavMeshData.PointInTriangle(new Vector2(1f, 1f), a, b, c));
    }

    [Test]
    public void PointInTriangle_OutsideReturnsFalse()
    {
        Vector2 a = new Vector2(0f, 0f);
        Vector2 b = new Vector2(10f, 0f);
        Vector2 c = new Vector2(0f, 10f);

        Assert.IsFalse(NavMeshData.PointInTriangle(new Vector2(8f, 8f), a, b, c));
        Assert.IsFalse(NavMeshData.PointInTriangle(new Vector2(-1f, 5f), a, b, c));
    }

    [Test]
    public void FindTriangleAtPosition_FindsCorrectTriangle()
    {
        var mesh = CreateSimpleMesh();
        mesh.BuildSpatialGrid(5f);

        int tri = mesh.FindTriangleAtPosition(new Vector2(2f, 2f));
        Assert.GreaterOrEqual(tri, 0, "Should find a triangle containing the point");
        Assert.IsTrue(mesh.Triangles[tri].IsWalkable);
    }

    [Test]
    public void FindTriangleAtPosition_OutsideMesh_ReturnsNearestWalkable()
    {
        var mesh = CreateSimpleMesh();
        mesh.BuildSpatialGrid(5f);

        int tri = mesh.FindTriangleAtPosition(new Vector2(-50f, -50f));
        Assert.GreaterOrEqual(tri, 0, "Brute-force fallback should return nearest walkable triangle");
        Assert.IsTrue(mesh.Triangles[tri].IsWalkable);
    }

    // ================================================================
    //  SharedVertexOfEdges
    // ================================================================

    [Test]
    public void SharedVertexOfEdges_AdjacentEdges_ReturnsSharedVertex()
    {
        // Edge 0 = (V0,V1), Edge 1 = (V1,V2) → shared vertex index = 1
        Assert.AreEqual(1, NavMeshData.SharedVertexOfEdges(0, 1));
    }

    [Test]
    public void SharedVertexOfEdges_Edge1And2_ReturnsVertex2()
    {
        // Edge 1 = (V1,V2), Edge 2 = (V2,V0) → shared vertex index = 2
        Assert.AreEqual(2, NavMeshData.SharedVertexOfEdges(1, 2));
    }

    [Test]
    public void SharedVertexOfEdges_Edge0And2_ReturnsVertex0()
    {
        // Edge 0 = (V0,V1), Edge 2 = (V2,V0) → shared vertex index = 0
        Assert.AreEqual(0, NavMeshData.SharedVertexOfEdges(0, 2));
    }

    // ================================================================
    //  GetEdgeLength
    // ================================================================

    [Test]
    public void GetEdgeLength_ReturnsCorrectLength()
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));
        mesh.AddVertex(new Vector2(3f, 0f));
        mesh.AddVertex(new Vector2(0f, 4f));
        mesh.AddTriangle(0, 1, 2, walkable: true);

        float len0 = mesh.GetEdgeLength(0, 0); // edge 0 = (v0,v1) = 3
        float len1 = mesh.GetEdgeLength(0, 1); // edge 1 = (v1,v2) = 5
        float len2 = mesh.GetEdgeLength(0, 2); // edge 2 = (v2,v0) = 4

        Assert.AreEqual(3f, len0, 0.01f);
        Assert.AreEqual(5f, len1, 0.01f);
        Assert.AreEqual(4f, len2, 0.01f);
    }

    // ================================================================
    //  GetTriangleArea
    // ================================================================

    [Test]
    public void GetTriangleArea_RightTriangle_CorrectArea()
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));
        mesh.AddVertex(new Vector2(6f, 0f));
        mesh.AddVertex(new Vector2(0f, 4f));
        mesh.AddTriangle(0, 1, 2, walkable: true);

        float area = mesh.GetTriangleArea(0);
        Assert.AreEqual(12f, area, 0.01f, "3-4-5 triangle has area 6*4/2=12");
    }

    // ================================================================
    //  NavTriangle methods
    // ================================================================

    [Test]
    public void GetEdgeToNeighbor_ReturnsCorrectEdge()
    {
        var mesh = CreateSimpleMesh();
        ref var t0 = ref mesh.Triangles[0];

        // Find which edge of t0 connects to t1
        int edge = t0.GetEdgeToNeighbor(1);
        Assert.GreaterOrEqual(edge, 0, "t0 should have t1 as a neighbor");
        Assert.LessOrEqual(edge, 2);
    }

    [Test]
    public void GetEdgeToNeighbor_NonNeighbor_ReturnsNegative()
    {
        var mesh = CreateSimpleMesh();
        ref var t0 = ref mesh.Triangles[0];

        int edge = t0.GetEdgeToNeighbor(999);
        Assert.AreEqual(-1, edge, "Non-neighbor should return -1");
    }

    [Test]
    public void GetEdgeVertices_ReturnsCorrectPairs()
    {
        var t = new NavTriangle { V0 = 10, V1 = 20, V2 = 30 };

        var (a0, b0) = t.GetEdgeVertices(0);
        Assert.AreEqual(10, a0); Assert.AreEqual(20, b0);

        var (a1, b1) = t.GetEdgeVertices(1);
        Assert.AreEqual(20, a1); Assert.AreEqual(30, b1);

        var (a2, b2) = t.GetEdgeVertices(2);
        Assert.AreEqual(30, a2); Assert.AreEqual(10, b2);
    }

    // ================================================================
    //  ComputeAllWidths accuracy
    // ================================================================

    [Test]
    public void ComputeAllWidths_EquilateralTriangle_AllWidthsEqual()
    {
        var mesh = new NavMeshData();
        float s = 10f;
        mesh.AddVertex(new Vector2(0f, 0f));
        mesh.AddVertex(new Vector2(s, 0f));
        mesh.AddVertex(new Vector2(s / 2f, s * Mathf.Sqrt(3f) / 2f));
        mesh.AddTriangle(0, 1, 2, walkable: true);
        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();

        float w0 = mesh.Triangles[0].W0;
        float w1 = mesh.Triangles[0].W1;
        float w2 = mesh.Triangles[0].W2;

        Assert.AreEqual(w0, w1, 0.1f, "Equilateral triangle: all widths should be equal");
        Assert.AreEqual(w1, w2, 0.1f);
        Assert.Greater(w0, 0f);
    }

    // ================================================================
    //  ValidateMesh
    // ================================================================

    [Test]
    public void ValidateMesh_HealthyMesh_ReturnsTrue()
    {
        var mesh = CreateSimpleMesh();
        Assert.IsTrue(mesh.ValidateMesh(), "Simple valid mesh should pass validation");
    }

    [Test]
    public void ValidateMesh_IsolatedTriangle_ReturnsFalse()
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));
        mesh.AddVertex(new Vector2(5f, 0f));
        mesh.AddVertex(new Vector2(2.5f, 5f));
        mesh.AddTriangle(0, 1, 2, walkable: true);
        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();

        Assert.IsFalse(mesh.ValidateMesh(),
            "Single isolated triangle should fail validation");
    }

    // ================================================================
    //  BuildAdjacency with unwalkable triangles
    // ================================================================

    [Test]
    public void BuildAdjacency_UnwalkableTriangle_NotAdjacent()
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0f, 0f));
        mesh.AddVertex(new Vector2(10f, 0f));
        mesh.AddVertex(new Vector2(0f, 10f));
        mesh.AddVertex(new Vector2(10f, 10f));

        mesh.AddTriangle(0, 1, 2, walkable: true);
        mesh.AddTriangle(1, 3, 2, walkable: false);

        mesh.BuildAdjacency();

        // t0 should have no neighbors (t1 is unwalkable and skipped)
        ref var t0 = ref mesh.Triangles[0];
        Assert.AreEqual(-1, t0.N0);
        Assert.AreEqual(-1, t0.N1);
        Assert.AreEqual(-1, t0.N2);
    }

    // ================================================================
    //  PointInTriangle on exact edge
    // ================================================================

    [Test]
    public void PointInTriangle_OnEdge_ReturnsTrue()
    {
        Vector2 a = new Vector2(0f, 0f);
        Vector2 b = new Vector2(10f, 0f);
        Vector2 c = new Vector2(0f, 10f);

        // Midpoint of edge a-b
        Assert.IsTrue(NavMeshData.PointInTriangle(new Vector2(5f, 0f), a, b, c));
        // Midpoint of edge a-c
        Assert.IsTrue(NavMeshData.PointInTriangle(new Vector2(0f, 5f), a, b, c));
        // On vertex
        Assert.IsTrue(NavMeshData.PointInTriangle(a, a, b, c));
    }

    // ================================================================
    //  ADDITIONAL EDGE CASES
    // ================================================================

    [Test]
    public void GetCentroid_IsInsideTriangle()
    {
        var mesh = CreateSimpleMesh();
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            Vector2 centroid = mesh.GetCentroid(i);
            bool inside = mesh.PointInTriangle(centroid, i);
            Assert.IsTrue(inside,
                $"Centroid of triangle {i} at {centroid} should be inside that triangle");
        }
    }

    [Test]
    public void SqrDistanceToSegment_CollinearPoint_ReturnsZero()
    {
        Vector2 a = new Vector2(0, 0);
        Vector2 b = new Vector2(10, 0);
        Vector2 p = new Vector2(5, 0);

        float dist = NavMeshData.SqrDistanceToSegment(p, a, b);
        Assert.AreEqual(0f, dist, 0.001f);
    }

    [Test]
    public void SqrDistanceToTriangle_DistantPoint_CorrectDistance()
    {
        Vector2 a = new Vector2(0, 0);
        Vector2 b = new Vector2(10, 0);
        Vector2 c = new Vector2(5, 10);
        Vector2 far = new Vector2(50, 50);

        float sqrDist = NavMeshData.SqrDistanceToTriangle(far, a, b, c);
        Assert.Greater(sqrDist, 0f);
        float dist = Mathf.Sqrt(sqrDist);
        Assert.Greater(dist, 30f, "Point (50,50) should be far from the triangle");
    }

    [Test]
    public void BuildAdjacency_ThreeConnectedTriangles_AllLinked()
    {
        var mesh = new NavMeshData();
        mesh.AddVertex(new Vector2(0, 0));   // v0
        mesh.AddVertex(new Vector2(5, 0));   // v1
        mesh.AddVertex(new Vector2(10, 0));  // v2
        mesh.AddVertex(new Vector2(2.5f, 5)); // v3
        mesh.AddVertex(new Vector2(7.5f, 5)); // v4

        mesh.AddTriangle(0, 1, 3, walkable: true); // t0
        mesh.AddTriangle(1, 2, 4, walkable: true); // t1
        mesh.AddTriangle(1, 4, 3, walkable: true); // t2 connects t0 and t1

        mesh.BuildAdjacency();

        // t2 should be adjacent to both t0 and t1
        bool t2_to_t0 = false, t2_to_t1 = false;
        for (int e = 0; e < 3; e++)
        {
            int n = mesh.Triangles[2].GetNeighbor(e);
            if (n == 0) t2_to_t0 = true;
            if (n == 1) t2_to_t1 = true;
        }
        Assert.IsTrue(t2_to_t0, "t2 should neighbor t0");
        Assert.IsTrue(t2_to_t1, "t2 should neighbor t1");
    }
}
