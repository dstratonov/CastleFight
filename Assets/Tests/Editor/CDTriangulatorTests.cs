using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

[TestFixture]
public class CDTriangulatorTests
{
    [Test]
    public void AddVertex_DeduplicatesIdenticalPositions()
    {
        var cdt = new CDTriangulator();
        int a = cdt.AddVertex(new Vector2(1f, 2f));
        int b = cdt.AddVertex(new Vector2(1f, 2f));
        Assert.AreEqual(a, b, "Duplicate vertices should return the same index");
    }

    [Test]
    public void AddVertex_DistinguishesDifferentPositions()
    {
        var cdt = new CDTriangulator();
        int a = cdt.AddVertex(new Vector2(0f, 0f));
        int b = cdt.AddVertex(new Vector2(1f, 0f));
        Assert.AreNotEqual(a, b);
    }

    [Test]
    public void Triangulate_ProducesTriangles()
    {
        var cdt = new CDTriangulator();
        cdt.AddVertex(new Vector2(0f, 0f));
        cdt.AddVertex(new Vector2(10f, 0f));
        cdt.AddVertex(new Vector2(5f, 10f));
        cdt.AddVertex(new Vector2(5f, 3f));

        cdt.Triangulate();

        var mesh = cdt.BuildNavMesh(pos => true);
        Assert.Greater(mesh.TriangleCount, 0, "Should produce at least one triangle");
    }

    [Test]
    public void Triangulate_AllTrianglesWalkable_WhenAllPositionsWalkable()
    {
        var cdt = new CDTriangulator();
        cdt.AddVertex(new Vector2(0f, 0f));
        cdt.AddVertex(new Vector2(10f, 0f));
        cdt.AddVertex(new Vector2(10f, 10f));
        cdt.AddVertex(new Vector2(0f, 10f));
        cdt.AddVertex(new Vector2(5f, 5f));

        cdt.Triangulate();

        var mesh = cdt.BuildNavMesh(pos => true);
        for (int i = 0; i < mesh.TriangleCount; i++)
            Assert.IsTrue(mesh.Triangles[i].IsWalkable, $"Triangle {i} should be walkable");
    }

    [Test]
    public void InsertConstraint_CreatesConstraintEdge()
    {
        var cdt = new CDTriangulator();
        cdt.AddVertex(new Vector2(0f, 0f));
        cdt.AddVertex(new Vector2(10f, 0f));
        cdt.AddVertex(new Vector2(10f, 10f));
        cdt.AddVertex(new Vector2(0f, 10f));
        cdt.AddVertex(new Vector2(5f, 5f));

        cdt.Triangulate();
        cdt.InsertConstraint(0, 2);

        var mesh = cdt.BuildNavMesh(pos => true);
        Assert.Greater(mesh.TriangleCount, 0, "Should still have triangles after constraint");
    }

    [Test]
    public void InsertConstraint_RectangleObstacle_ProducesUnwalkableTriangles()
    {
        var cdt = new CDTriangulator();

        // Outer boundary
        cdt.AddVertex(new Vector2(-10f, -10f));
        cdt.AddVertex(new Vector2(10f, -10f));
        cdt.AddVertex(new Vector2(10f, 10f));
        cdt.AddVertex(new Vector2(-10f, 10f));

        // Inner obstacle rectangle: (2,2)-(4,4)
        int o0 = cdt.AddVertex(new Vector2(2f, 2f));
        int o1 = cdt.AddVertex(new Vector2(4f, 2f));
        int o2 = cdt.AddVertex(new Vector2(4f, 4f));
        int o3 = cdt.AddVertex(new Vector2(2f, 4f));

        cdt.Triangulate();
        cdt.InsertConstraint(o0, o1);
        cdt.InsertConstraint(o1, o2);
        cdt.InsertConstraint(o2, o3);
        cdt.InsertConstraint(o3, o0);

        var mesh = cdt.BuildNavMesh(pos =>
        {
            return !(pos.x > 2f && pos.x < 4f && pos.y > 2f && pos.y < 4f);
        });

        int walkable = 0, unwalkable = 0;
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            if (mesh.Triangles[i].IsWalkable) walkable++;
            else unwalkable++;
        }

        Assert.Greater(walkable, 0, "Should have walkable triangles");
        Assert.Greater(unwalkable, 0, "Should have unwalkable triangles inside obstacle");
    }

    [Test]
    public void InsertConstraint_SameVertex_NoError()
    {
        var cdt = new CDTriangulator();
        cdt.AddVertex(new Vector2(0f, 0f));
        cdt.AddVertex(new Vector2(5f, 0f));
        cdt.AddVertex(new Vector2(5f, 5f));

        cdt.Triangulate();
        cdt.InsertConstraint(0, 0);

        var mesh = cdt.BuildNavMesh(pos => true);
        Assert.Greater(mesh.TriangleCount, 0);
    }

    [Test]
    public void InsertConstraint_ExistingEdge_Succeeds()
    {
        var cdt = new CDTriangulator();
        cdt.AddVertex(new Vector2(0f, 0f));
        cdt.AddVertex(new Vector2(5f, 0f));
        cdt.AddVertex(new Vector2(2.5f, 5f));

        cdt.Triangulate();
        cdt.InsertConstraint(0, 1);

        var mesh = cdt.BuildNavMesh(pos => true);
        Assert.Greater(mesh.TriangleCount, 0);
    }

    [Test]
    public void Triangulate_ManyVertices_NoError()
    {
        var cdt = new CDTriangulator();
        for (int x = 0; x < 10; x++)
            for (int y = 0; y < 10; y++)
                cdt.AddVertex(new Vector2(x, y));

        cdt.Triangulate();

        var mesh = cdt.BuildNavMesh(pos => true);
        Assert.Greater(mesh.TriangleCount, 0, "Grid of 100 vertices should produce triangles");
        Assert.Greater(mesh.VertexCount, 0);
    }
}
