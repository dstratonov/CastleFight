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

    // ================================================================
    //  Vertical constraint tests (grid-aligned geometry)
    // ================================================================

    /// <summary>
    /// Helper: creates a grid of vertices and a rectangular obstacle with all 4
    /// constraint edges inserted. Returns the mesh with the obstacle marked unwalkable.
    /// </summary>
    private NavMeshData CreateMeshWithRectObstacle(
        float gridMinX, float gridMaxX, float gridMinY, float gridMaxY, float gridStep,
        float obstMinX, float obstMaxX, float obstMinY, float obstMaxY)
    {
        var cdt = new CDTriangulator();
        for (float x = gridMinX; x <= gridMaxX; x += gridStep)
            for (float y = gridMinY; y <= gridMaxY; y += gridStep)
                cdt.AddVertex(new Vector2(x, y));

        int bl = cdt.AddVertex(new Vector2(obstMinX, obstMinY));
        int br = cdt.AddVertex(new Vector2(obstMaxX, obstMinY));
        int tr = cdt.AddVertex(new Vector2(obstMaxX, obstMaxY));
        int tl = cdt.AddVertex(new Vector2(obstMinX, obstMaxY));

        cdt.Triangulate();
        cdt.InsertConstraint(bl, br); // bottom (horizontal)
        cdt.InsertConstraint(br, tr); // right (vertical)
        cdt.InsertConstraint(tr, tl); // top (horizontal)
        cdt.InsertConstraint(tl, bl); // left (vertical)

        return cdt.BuildNavMesh(pos =>
        {
            return !(pos.x > obstMinX && pos.x < obstMaxX &&
                     pos.y > obstMinY && pos.y < obstMaxY);
        });
    }

    [Test]
    public void VerticalConstraint_SingleObstacleOnGrid_AllEdgesInserted()
    {
        var mesh = CreateMeshWithRectObstacle(
            -20f, 20f, -20f, 20f, 2f,
            -3f, 3f, -3f, 3f);

        int walkable = 0, unwalkable = 0;
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            if (mesh.Triangles[i].IsWalkable) walkable++;
            else unwalkable++;
        }

        Assert.Greater(walkable, 0, "Should have walkable area outside obstacle");
        Assert.Greater(unwalkable, 0, "Should have unwalkable area inside obstacle");
    }

    [Test]
    public void VerticalConstraint_ObstacleAlignedWithGridEdge()
    {
        // Obstacle edges align exactly with grid vertices — the hardest case.
        // Building at x=-15, y=23 to x=-9, y=29 on a grid with step 2.
        // The left edge at x=-15 is a vertical constraint crossing grid lines.
        var mesh = CreateMeshWithRectObstacle(
            -20f, 0f, 18f, 34f, 1f,
            -15f, -9f, 23f, 29f);

        int unwalkable = 0;
        for (int i = 0; i < mesh.TriangleCount; i++)
            if (!mesh.Triangles[i].IsWalkable) unwalkable++;

        Assert.Greater(unwalkable, 0, "Obstacle should produce unwalkable triangles");
    }

    [Test]
    public void VerticalConstraint_TwoObstaclesSameXColumn()
    {
        // Two buildings with edges on the same vertical line (x=-15).
        // Tests constraint insertion when previous constraints have already
        // modified the triangulation along the same vertical.
        var cdt = new CDTriangulator();
        for (float x = -20f; x <= 0f; x += 1f)
            for (float y = -10f; y <= 40f; y += 1f)
                cdt.AddVertex(new Vector2(x, y));

        // Building A at x=-15...-9, y=0...6
        int a0 = cdt.AddVertex(new Vector2(-15f, 0f));
        int a1 = cdt.AddVertex(new Vector2(-9f, 0f));
        int a2 = cdt.AddVertex(new Vector2(-9f, 6f));
        int a3 = cdt.AddVertex(new Vector2(-15f, 6f));

        // Building B at x=-15...-9, y=20...26 (same X column, different Y)
        int b0 = cdt.AddVertex(new Vector2(-15f, 20f));
        int b1 = cdt.AddVertex(new Vector2(-9f, 20f));
        int b2 = cdt.AddVertex(new Vector2(-9f, 26f));
        int b3 = cdt.AddVertex(new Vector2(-15f, 26f));

        cdt.Triangulate();

        cdt.InsertConstraint(a0, a1);
        cdt.InsertConstraint(a1, a2);
        cdt.InsertConstraint(a2, a3);
        cdt.InsertConstraint(a3, a0);

        cdt.InsertConstraint(b0, b1);
        cdt.InsertConstraint(b1, b2);
        cdt.InsertConstraint(b2, b3);
        cdt.InsertConstraint(b3, b0);

        var mesh = cdt.BuildNavMesh(pos =>
        {
            bool inA = pos.x > -15f && pos.x < -9f && pos.y > 0f && pos.y < 6f;
            bool inB = pos.x > -15f && pos.x < -9f && pos.y > 20f && pos.y < 26f;
            return !(inA || inB);
        });

        int unwalkable = 0;
        for (int i = 0; i < mesh.TriangleCount; i++)
            if (!mesh.Triangles[i].IsWalkable) unwalkable++;

        Assert.Greater(unwalkable, 0, "Both obstacles should produce unwalkable triangles");
    }

    [Test]
    public void VerticalConstraint_MultipleObstacles()
    {
        // Two rectangular obstacles on a moderate grid with vertical constraints.
        var cdt = new CDTriangulator();
        for (float x = -15f; x <= 15f; x += 2f)
            for (float y = -15f; y <= 15f; y += 2f)
                cdt.AddVertex(new Vector2(x, y));

        int a0 = cdt.AddVertex(new Vector2(-8f, -5f));
        int a1 = cdt.AddVertex(new Vector2(-2f, -5f));
        int a2 = cdt.AddVertex(new Vector2(-2f, 1f));
        int a3 = cdt.AddVertex(new Vector2(-8f, 1f));

        int b0 = cdt.AddVertex(new Vector2(3f, 2f));
        int b1 = cdt.AddVertex(new Vector2(9f, 2f));
        int b2 = cdt.AddVertex(new Vector2(9f, 8f));
        int b3 = cdt.AddVertex(new Vector2(3f, 8f));

        cdt.Triangulate();

        cdt.InsertConstraint(a0, a1);
        cdt.InsertConstraint(a1, a2);
        cdt.InsertConstraint(a2, a3);
        cdt.InsertConstraint(a3, a0);

        cdt.InsertConstraint(b0, b1);
        cdt.InsertConstraint(b1, b2);
        cdt.InsertConstraint(b2, b3);
        cdt.InsertConstraint(b3, b0);

        var mesh = cdt.BuildNavMesh(pos =>
        {
            bool inA = pos.x > -8f && pos.x < -2f && pos.y > -5f && pos.y < 1f;
            bool inB = pos.x > 3f && pos.x < 9f && pos.y > 2f && pos.y < 8f;
            return !(inA || inB);
        });

        Assert.Greater(mesh.TriangleCount, 0, "Should produce a valid mesh");

        int unwalkable = 0;
        for (int i = 0; i < mesh.TriangleCount; i++)
            if (!mesh.Triangles[i].IsWalkable) unwalkable++;
        Assert.Greater(unwalkable, 0, "Should have unwalkable triangles inside obstacles");

        mesh.BuildSpatialGrid(2f);
        var path = NavMeshPathfinder.FindPath(mesh, new Vector2(-12f, -12f), new Vector2(12f, 12f), 0.5f);
        Assert.IsNotNull(path, "Should find path around obstacles");
    }

    [Test]
    public void VerticalConstraint_IncrementalBuildings()
    {
        // Simulates buildings being placed one at a time. Each iteration
        // creates a fresh CDT (matching how the game rebuilds NavMesh).
        var buildingCorners = new[]
        {
            (new Vector2(-8f, -8f), new Vector2(-2f, -2f)),
            (new Vector2(2f, 3f), new Vector2(8f, 9f)),
            (new Vector2(-6f, 5f), new Vector2(0f, 11f)),
        };

        for (int numBuildings = 1; numBuildings <= buildingCorners.Length; numBuildings++)
        {
            var cdt = new CDTriangulator();
            for (float x = -12f; x <= 12f; x += 2f)
                for (float y = -12f; y <= 14f; y += 2f)
                    cdt.AddVertex(new Vector2(x, y));

            var activeBuildings = new List<(Vector2 min, Vector2 max)>();
            var vertIds = new List<(int bl, int br, int tr, int tl)>();
            for (int b = 0; b < numBuildings; b++)
            {
                var (bmin, bmax) = buildingCorners[b];
                activeBuildings.Add((bmin, bmax));

                int bl = cdt.AddVertex(bmin);
                int br = cdt.AddVertex(new Vector2(bmax.x, bmin.y));
                int tr = cdt.AddVertex(bmax);
                int tl = cdt.AddVertex(new Vector2(bmin.x, bmax.y));
                vertIds.Add((bl, br, tr, tl));
            }

            cdt.Triangulate();

            foreach (var (bl, br, tr, tl) in vertIds)
            {
                cdt.InsertConstraint(bl, br);
                cdt.InsertConstraint(br, tr);
                cdt.InsertConstraint(tr, tl);
                cdt.InsertConstraint(tl, bl);
            }

            var mesh = cdt.BuildNavMesh(pos =>
            {
                foreach (var (bmin, bmax) in activeBuildings)
                {
                    if (pos.x > bmin.x && pos.x < bmax.x &&
                        pos.y > bmin.y && pos.y < bmax.y)
                        return false;
                }
                return true;
            });

            Assert.Greater(mesh.TriangleCount, 0,
                $"Mesh with {numBuildings} buildings should produce triangles");
        }
    }

    [Test]
    public void VerticalConstraint_PureVerticalEdge_Length6()
    {
        // Minimal repro: a single vertical constraint of length 6 on a grid.
        // This is the exact pattern from the warnings: vertical edges of length 6.
        var cdt = new CDTriangulator();
        for (float x = -5f; x <= 5f; x += 1f)
            for (float y = -5f; y <= 10f; y += 1f)
                cdt.AddVertex(new Vector2(x, y));

        int bottom = cdt.AddVertex(new Vector2(0f, 0f));
        int top = cdt.AddVertex(new Vector2(0f, 6f));

        cdt.Triangulate();
        cdt.InsertConstraint(bottom, top);

        var mesh = cdt.BuildNavMesh(pos => true);
        Assert.Greater(mesh.TriangleCount, 0);

        // Note: vertex IDs in the output mesh are remapped, so we check by position
        int vBottom = -1, vTop = -1;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (Vector2.Distance(mesh.Vertices[i], new Vector2(0f, 0f)) < 0.01f) vBottom = i;
            if (Vector2.Distance(mesh.Vertices[i], new Vector2(0f, 6f)) < 0.01f) vTop = i;
        }
        Assert.GreaterOrEqual(vBottom, 0, "Should find bottom vertex in output mesh");
        Assert.GreaterOrEqual(vTop, 0, "Should find top vertex in output mesh");
    }

    [Test]
    public void VerticalConstraint_CollinearVerticesOnConstraintLine()
    {
        // Grid vertices at x=0 between y=0 and y=6 force the constraint
        // to be split at intermediate vertices. Tests FindVerticesOnSegment.
        var cdt = new CDTriangulator();
        for (float x = -5f; x <= 5f; x += 1f)
            for (float y = -2f; y <= 8f; y += 1f)
                cdt.AddVertex(new Vector2(x, y));

        // Constraint from (0,0) to (0,6) — intermediate vertices at (0,1),(0,2),...,(0,5)
        int v0 = cdt.AddVertex(new Vector2(0f, 0f));
        int v6 = cdt.AddVertex(new Vector2(0f, 6f));

        cdt.Triangulate();
        cdt.InsertConstraint(v0, v6);

        var mesh = cdt.BuildNavMesh(pos => true);
        Assert.Greater(mesh.TriangleCount, 0,
            "Constraint with many collinear intermediate vertices should succeed");
    }

    [Test]
    public void VerticalConstraint_BuildingOnMapBoundary()
    {
        // Building placed at the edge of the grid — constraint near boundary.
        var cdt = new CDTriangulator();
        for (float x = -40f; x <= -30f; x += 1f)
            for (float y = -40f; y <= -25f; y += 1f)
                cdt.AddVertex(new Vector2(x, y));

        int bl = cdt.AddVertex(new Vector2(-35f, -35f));
        int br = cdt.AddVertex(new Vector2(-29f, -35f));
        int tr = cdt.AddVertex(new Vector2(-29f, -29f));
        int tl = cdt.AddVertex(new Vector2(-35f, -29f));

        cdt.Triangulate();
        cdt.InsertConstraint(bl, br);
        cdt.InsertConstraint(br, tr);
        cdt.InsertConstraint(tr, tl);
        cdt.InsertConstraint(tl, bl);

        var mesh = cdt.BuildNavMesh(pos =>
        {
            return !(pos.x > -35f && pos.x < -29f && pos.y > -35f && pos.y < -29f);
        });

        int unwalkable = 0;
        for (int i = 0; i < mesh.TriangleCount; i++)
            if (!mesh.Triangles[i].IsWalkable) unwalkable++;

        Assert.Greater(unwalkable, 0, "Building at boundary should still produce unwalkable triangles");
    }

    // ================================================================
    //  ADJACENT BUILDINGS (shared wall)
    // ================================================================

    [Test]
    public void InsertConstraint_AdjacentBuildings_SharedWall_NoException()
    {
        var cdt = new CDTriangulator();
        // Scatter background points
        for (int x = -5; x <= 15; x += 5)
            for (int y = -5; y <= 15; y += 5)
                cdt.AddVertex(new Vector2(x, y));

        cdt.Triangulate();

        // Building A: (0,0)-(4,4)
        int a_bl = cdt.AddVertex(new Vector2(0f, 0f));
        int a_br = cdt.AddVertex(new Vector2(4f, 0f));
        int a_tr = cdt.AddVertex(new Vector2(4f, 4f));
        int a_tl = cdt.AddVertex(new Vector2(0f, 4f));
        cdt.InsertConstraint(a_bl, a_br);
        cdt.InsertConstraint(a_br, a_tr);
        cdt.InsertConstraint(a_tr, a_tl);
        cdt.InsertConstraint(a_tl, a_bl);

        // Building B: (4,0)-(8,4) — shares the right wall of A (x=4)
        int b_br = cdt.AddVertex(new Vector2(8f, 0f));
        int b_tr = cdt.AddVertex(new Vector2(8f, 4f));
        // a_br and a_tr are shared vertices (x=4 line)
        cdt.InsertConstraint(a_br, b_br);
        cdt.InsertConstraint(b_br, b_tr);
        cdt.InsertConstraint(b_tr, a_tr);
        // The shared edge a_br->a_tr is already a constraint from building A

        var mesh = cdt.BuildNavMesh(pos =>
        {
            bool inA = pos.x > 0f && pos.x < 4f && pos.y > 0f && pos.y < 4f;
            bool inB = pos.x > 4f && pos.x < 8f && pos.y > 0f && pos.y < 4f;
            return !(inA || inB);
        });

        Assert.Greater(mesh.TriangleCount, 0, "Adjacent buildings should produce a valid mesh");

        int unwalkable = 0;
        for (int i = 0; i < mesh.TriangleCount; i++)
            if (!mesh.Triangles[i].IsWalkable) unwalkable++;
        Assert.Greater(unwalkable, 0, "Should have unwalkable triangles inside the buildings");
    }

    // ================================================================
    //  DIAGONAL CONSTRAINT
    // ================================================================

    [Test]
    public void InsertConstraint_DiagonalEdge_NoException()
    {
        var cdt = new CDTriangulator();
        for (int x = -5; x <= 15; x += 5)
            for (int y = -5; y <= 15; y += 5)
                cdt.AddVertex(new Vector2(x, y));

        cdt.Triangulate();

        int v0 = cdt.AddVertex(new Vector2(2f, 1f));
        int v1 = cdt.AddVertex(new Vector2(8f, 7f));
        cdt.InsertConstraint(v0, v1);

        var mesh = cdt.BuildNavMesh(_ => true);
        Assert.Greater(mesh.TriangleCount, 0, "Diagonal constraint should produce valid mesh");
    }

    [Test]
    public void InsertConstraint_DiagonalBuilding_ProducesUnwalkable()
    {
        var cdt = new CDTriangulator();
        for (int x = -5; x <= 15; x += 5)
            for (int y = -5; y <= 15; y += 5)
                cdt.AddVertex(new Vector2(x, y));

        cdt.Triangulate();

        // Rotated square (diamond shape)
        int v0 = cdt.AddVertex(new Vector2(5f, 2f));
        int v1 = cdt.AddVertex(new Vector2(8f, 5f));
        int v2 = cdt.AddVertex(new Vector2(5f, 8f));
        int v3 = cdt.AddVertex(new Vector2(2f, 5f));
        cdt.InsertConstraint(v0, v1);
        cdt.InsertConstraint(v1, v2);
        cdt.InsertConstraint(v2, v3);
        cdt.InsertConstraint(v3, v0);

        var mesh = cdt.BuildNavMesh(pos =>
        {
            // Simple diamond containment test
            float dx = Mathf.Abs(pos.x - 5f);
            float dy = Mathf.Abs(pos.y - 5f);
            return (dx + dy) > 3.5f;
        });

        int unwalkable = 0;
        for (int i = 0; i < mesh.TriangleCount; i++)
            if (!mesh.Triangles[i].IsWalkable) unwalkable++;
        Assert.Greater(unwalkable, 0, "Diamond building should produce unwalkable triangles");
    }

    // ================================================================
    //  DEGENERATE INPUT
    // ================================================================

    [Test]
    public void Triangulate_TooFewVertices_NoException()
    {
        var cdt0 = new CDTriangulator();
        Assert.DoesNotThrow(() => cdt0.Triangulate(), "0 vertices should not throw");

        var cdt2 = new CDTriangulator();
        cdt2.AddVertex(new Vector2(0f, 0f));
        cdt2.AddVertex(new Vector2(1f, 0f));
        Assert.DoesNotThrow(() => cdt2.Triangulate(), "2 vertices should not throw");
    }

    [Test]
    public void Triangulate_CollinearPoints_ProducesTriangles()
    {
        var cdt = new CDTriangulator();
        // All points on a line + one off-line point to form triangles
        cdt.AddVertex(new Vector2(0f, 0f));
        cdt.AddVertex(new Vector2(5f, 0f));
        cdt.AddVertex(new Vector2(10f, 0f));
        cdt.AddVertex(new Vector2(5f, 5f));

        Assert.DoesNotThrow(() => cdt.Triangulate());
    }

    // ================================================================
    //  CDT → NavMesh quality checks
    // ================================================================

    [Test]
    public void BuildNavMesh_ProducesNonDegenerateTriangles()
    {
        var cdt = new CDTriangulator();
        for (float x = 0f; x <= 20f; x += 5f)
            for (float y = 0f; y <= 20f; y += 5f)
                cdt.AddVertex(new Vector2(x, y));
        cdt.Triangulate();

        var mesh = cdt.BuildNavMesh(_ => true);

        Assert.Greater(mesh.TriangleCount, 0);
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            float area = mesh.GetTriangleArea(i);
            Assert.Greater(area, 0.0001f,
                $"Triangle {i} should not be degenerate (area={area})");
        }
    }

    [Test]
    public void BuildNavMesh_AdjacencyIsSymmetric()
    {
        var cdt = new CDTriangulator();
        for (float x = 0f; x <= 15f; x += 5f)
            for (float y = 0f; y <= 15f; y += 5f)
                cdt.AddVertex(new Vector2(x, y));
        cdt.Triangulate();
        var mesh = cdt.BuildNavMesh(_ => true);

        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            for (int e = 0; e < 3; e++)
            {
                int n = mesh.Triangles[i].GetNeighbor(e);
                if (n < 0) continue;
                int back = mesh.Triangles[n].GetEdgeToNeighbor(i);
                Assert.GreaterOrEqual(back, 0,
                    $"Adjacency symmetry broken: tri {i} -> {n}, but {n} doesn't link back");
            }
        }
    }

    [Test]
    public void BuildNavMesh_AllWalkable_HasNoUnwalkable()
    {
        var cdt = new CDTriangulator();
        for (float x = 0f; x <= 10f; x += 5f)
            for (float y = 0f; y <= 10f; y += 5f)
                cdt.AddVertex(new Vector2(x, y));
        cdt.Triangulate();
        var mesh = cdt.BuildNavMesh(_ => true);

        for (int i = 0; i < mesh.TriangleCount; i++)
            Assert.IsTrue(mesh.Triangles[i].IsWalkable,
                $"Triangle {i} should be walkable when all cells are walkable");
    }

    [Test]
    public void BuildNavMesh_WithConstraint_HasUnwalkableTriangles()
    {
        var cdt = new CDTriangulator();
        for (float x = 0f; x <= 20f; x += 5f)
            for (float y = 0f; y <= 20f; y += 5f)
                cdt.AddVertex(new Vector2(x, y));
        cdt.Triangulate();

        // Insert a 4x4 building constraint
        int bl = cdt.AddVertex(new Vector2(8f, 8f));
        int br = cdt.AddVertex(new Vector2(12f, 8f));
        int tr = cdt.AddVertex(new Vector2(12f, 12f));
        int tl = cdt.AddVertex(new Vector2(8f, 12f));
        cdt.InsertConstraint(bl, br);
        cdt.InsertConstraint(br, tr);
        cdt.InsertConstraint(tr, tl);
        cdt.InsertConstraint(tl, bl);

        var mesh = cdt.BuildNavMesh(pos =>
            !(pos.x > 8f && pos.x < 12f && pos.y > 8f && pos.y < 12f));

        int unwalkable = 0;
        for (int i = 0; i < mesh.TriangleCount; i++)
            if (!mesh.Triangles[i].IsWalkable) unwalkable++;

        Assert.Greater(unwalkable, 0,
            "Building constraint should produce unwalkable triangles");
    }

    [Test]
    public void InsertConstraint_ManyBuildings_NoException()
    {
        var cdt = new CDTriangulator();
        for (float x = 0f; x <= 40f; x += 5f)
            for (float y = 0f; y <= 40f; y += 5f)
                cdt.AddVertex(new Vector2(x, y));
        cdt.Triangulate();

        // Insert 4 buildings at different positions
        float[][] buildings = new float[][]
        {
            new float[] { 3f, 3f, 4f, 4f },
            new float[] { 15f, 3f, 4f, 4f },
            new float[] { 3f, 15f, 4f, 4f },
            new float[] { 25f, 25f, 4f, 4f },
        };

        foreach (var b in buildings)
        {
            int v0 = cdt.AddVertex(new Vector2(b[0], b[1]));
            int v1 = cdt.AddVertex(new Vector2(b[0] + b[2], b[1]));
            int v2 = cdt.AddVertex(new Vector2(b[0] + b[2], b[1] + b[3]));
            int v3 = cdt.AddVertex(new Vector2(b[0], b[1] + b[3]));
            Assert.DoesNotThrow(() =>
            {
                cdt.InsertConstraint(v0, v1);
                cdt.InsertConstraint(v1, v2);
                cdt.InsertConstraint(v2, v3);
                cdt.InsertConstraint(v3, v0);
            });
        }

        var mesh = cdt.BuildNavMesh(_ => true);
        Assert.Greater(mesh.TriangleCount, 0);
    }

    [Test]
    public void BuildNavMesh_WidthsAreComputed()
    {
        var cdt = new CDTriangulator();
        for (float x = 0f; x <= 10f; x += 5f)
            for (float y = 0f; y <= 10f; y += 5f)
                cdt.AddVertex(new Vector2(x, y));
        cdt.Triangulate();
        var mesh = cdt.BuildNavMesh(_ => true);

        bool anyPositiveWidth = false;
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            for (int w = 0; w < 3; w++)
            {
                if (mesh.Triangles[i].GetWidth(w) > 0f)
                    anyPositiveWidth = true;
            }
        }
        Assert.IsTrue(anyPositiveWidth, "Widths should be computed during BuildNavMesh");
    }

    // ================================================================
    //  HORIZONTAL CONSTRAINT COLLINEAR WALK
    // ================================================================

    [Test]
    public void InsertConstraint_HorizontalEdge_CollinearWithGrid_NoWarning()
    {
        // Reproduces the real-world failure: horizontal building edge at y=33
        // with grid vertices also at y=33, causing WalkConstraint to fail.
        var cdt = new CDTriangulator();

        // Add grid-like vertices including points on the constraint line (y=10)
        for (float x = 0f; x <= 30f; x += 3f)
            for (float y = 0f; y <= 20f; y += 5f)
                cdt.AddVertex(new Vector2(x, y));

        // Extra vertices exactly on y=10 (the constraint line)
        cdt.AddVertex(new Vector2(2f, 10f));
        cdt.AddVertex(new Vector2(7f, 10f));
        cdt.AddVertex(new Vector2(13f, 10f));

        cdt.Triangulate();

        // Building edge: horizontal from (5,10) to (15,10) — collinear with grid
        int va = cdt.AddVertex(new Vector2(5f, 10f));
        int vb = cdt.AddVertex(new Vector2(15f, 10f));

        Assert.DoesNotThrow(() => cdt.InsertConstraint(va, vb),
            "Horizontal constraint collinear with grid vertices should not throw");

        var mesh = cdt.BuildNavMesh(_ => true);
        Assert.Greater(mesh.TriangleCount, 0);
    }

    [Test]
    public void InsertConstraint_HorizontalBuildingEdges_AllFourSides_NoException()
    {
        var cdt = new CDTriangulator();

        // Dense grid with many vertices on building edge lines
        for (float x = -20f; x <= 0f; x += 2f)
            for (float y = 30f; y <= 50f; y += 2f)
                cdt.AddVertex(new Vector2(x, y));

        cdt.Triangulate();

        // Building rect matching the real-world failure coordinates
        int bl = cdt.AddVertex(new Vector2(-15f, 33f));
        int br = cdt.AddVertex(new Vector2(-9f, 33f));
        int tr = cdt.AddVertex(new Vector2(-9f, 39f));
        int tl = cdt.AddVertex(new Vector2(-15f, 39f));

        Assert.DoesNotThrow(() =>
        {
            cdt.InsertConstraint(bl, br); // bottom (horizontal)
            cdt.InsertConstraint(br, tr); // right (vertical)
            cdt.InsertConstraint(tr, tl); // top (horizontal)
            cdt.InsertConstraint(tl, bl); // left (vertical)
        }, "Building constraint edges should insert without failure");
    }

    [Test]
    public void InsertConstraint_MultipleHorizontalBuildings_SameY_NoException()
    {
        var cdt = new CDTriangulator();

        // Grid with vertices on y=41 — the exact failing coordinate
        for (float x = -40f; x <= -25f; x += 2f)
            for (float y = 35f; y <= 45f; y += 2f)
                cdt.AddVertex(new Vector2(x, y));

        cdt.Triangulate();

        // Two buildings with edges on y=41
        int a0 = cdt.AddVertex(new Vector2(-35f, 41f));
        int a1 = cdt.AddVertex(new Vector2(-31f, 41f));
        int a2 = cdt.AddVertex(new Vector2(-31f, 45f));
        int a3 = cdt.AddVertex(new Vector2(-35f, 45f));

        Assert.DoesNotThrow(() =>
        {
            cdt.InsertConstraint(a0, a1);
            cdt.InsertConstraint(a1, a2);
            cdt.InsertConstraint(a2, a3);
            cdt.InsertConstraint(a3, a0);
        });
    }
}
