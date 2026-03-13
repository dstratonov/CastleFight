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
    public void Cross2D_CorrectSign()
    {
        float cross = NavMeshData.Cross2D(new Vector2(1, 0), new Vector2(0, 1));
        Assert.Greater(cross, 0f, "CCW cross product should be positive");

        float crossCW = NavMeshData.Cross2D(new Vector2(0, 1), new Vector2(1, 0));
        Assert.Less(crossCW, 0f, "CW cross product should be negative");
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

    [Test]
    public void DeepCopy_ProducesIndependentCopy()
    {
        var mesh = CreateSimpleMesh();
        var copy = mesh.DeepCopy();

        Assert.AreEqual(mesh.VertexCount, copy.VertexCount);
        Assert.AreEqual(mesh.TriangleCount, copy.TriangleCount);

        copy.Triangles[0].CostMultiplier = 99f;
        Assert.AreNotEqual(mesh.Triangles[0].CostMultiplier, copy.Triangles[0].CostMultiplier,
            "Modifying copy should not affect original");
    }
}
