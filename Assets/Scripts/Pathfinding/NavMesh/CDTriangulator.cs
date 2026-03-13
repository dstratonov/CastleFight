using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Constrained Delaunay Triangulation using the Bowyer-Watson algorithm.
/// Produces a triangle mesh where obstacle boundaries are preserved as edges.
/// All operations use 2D coordinates (X = world X, Y = world Z).
/// </summary>
public class CDTriangulator
{
    private readonly List<Vector2> vertices = new();
    private readonly List<int[]> triangles = new(); // each = { v0, v1, v2 }
    private readonly HashSet<long> constraintEdges = new();
    private readonly List<bool> triAlive = new();
    private readonly Dictionary<long, int> vertexDedup = new();

    private int superV0, superV1, superV2;
    private int constraintsInserted;
    private int constraintsFailed;
    private int degenerateTrianglesSkipped;

    public int VertexCount => vertices.Count;

    public int AddVertex(Vector2 v)
    {
        long key = HashVertex(v);
        if (vertexDedup.TryGetValue(key, out int existing))
            return existing;

        int id = vertices.Count;
        vertices.Add(v);
        vertexDedup[key] = id;
        return id;
    }

    private static long HashVertex(Vector2 v)
    {
        int hx = Mathf.RoundToInt(v.x * 1000f);
        int hy = Mathf.RoundToInt(v.y * 1000f);
        return ((long)hx << 32) | (uint)hy;
    }

    /// <summary>
    /// Run Bowyer-Watson incremental Delaunay triangulation on all added vertices.
    /// </summary>
    public void Triangulate()
    {
        if (vertices.Count < 3)
        {
            Debug.LogWarning("[CDT] Triangulate called with < 3 vertices");
            return;
        }

        if (GameDebug.Pathfinding)
            Debug.Log($"[CDT] Triangulate starting: {vertices.Count} input vertices");

        Vector2 min = vertices[0], max = vertices[0];
        for (int i = 1; i < vertices.Count; i++)
        {
            min = Vector2.Min(min, vertices[i]);
            max = Vector2.Max(max, vertices[i]);
        }

        float dx = max.x - min.x;
        float dy = max.y - min.y;
        float dmax = Mathf.Max(dx, dy);
        float midX = (min.x + max.x) * 0.5f;
        float midY = (min.y + max.y) * 0.5f;

        superV0 = AddVertex(new Vector2(midX - 20f * dmax, midY - dmax));
        superV1 = AddVertex(new Vector2(midX, midY + 20f * dmax));
        superV2 = AddVertex(new Vector2(midX + 20f * dmax, midY - dmax));

        triangles.Clear();
        triAlive.Clear();
        triangles.Add(new[] { superV0, superV1, superV2 });
        triAlive.Add(true);

        var badTriangles = new List<int>();
        var polygon = new List<int[]>(); // boundary edges
        var edgeCount = new Dictionary<long, int>();

        int numPoints = vertices.Count - 3; // exclude super-triangle vertices

        for (int pi = 0; pi < numPoints; pi++)
        {
            Vector2 p = vertices[pi];
            badTriangles.Clear();

            for (int ti = 0; ti < triangles.Count; ti++)
            {
                if (!triAlive[ti]) continue;
                if (InCircumcircle(p, triangles[ti]))
                    badTriangles.Add(ti);
            }

            edgeCount.Clear();
            foreach (int ti in badTriangles)
            {
                int[] tri = triangles[ti];
                CountEdge(edgeCount, tri[0], tri[1]);
                CountEdge(edgeCount, tri[1], tri[2]);
                CountEdge(edgeCount, tri[2], tri[0]);
            }

            polygon.Clear();
            foreach (int ti in badTriangles)
            {
                int[] tri = triangles[ti];
                TryAddBoundaryEdge(polygon, edgeCount, tri[0], tri[1]);
                TryAddBoundaryEdge(polygon, edgeCount, tri[1], tri[2]);
                TryAddBoundaryEdge(polygon, edgeCount, tri[2], tri[0]);
            }

            foreach (int ti in badTriangles)
                triAlive[ti] = false;

            foreach (int[] edge in polygon)
            {
                triangles.Add(new[] { edge[0], edge[1], pi });
                triAlive.Add(true);
            }
        }

        int alive = 0;
        for (int i = 0; i < triAlive.Count; i++)
            if (triAlive[i]) alive++;

        if (GameDebug.Pathfinding)
            Debug.Log($"[CDT] Triangulate done: {alive} alive triangles out of {triangles.Count} total, {vertices.Count} vertices (incl. super-tri)");
    }

    /// <summary>
    /// Insert a constrained edge using cavity retriangulation (Shewchuk/Brown 2015).
    /// Splits the constraint at intermediate vertices, walks the triangulation to
    /// find crossed triangles, deletes them, and retriangulates the two cavities.
    /// </summary>
    public void InsertConstraint(int va, int vb)
    {
        if (va == vb) return;

        constraintEdges.Add(EdgeKeyOrdered(va, vb));

        if (EdgeExists(va, vb))
        {
            constraintsInserted++;
            return;
        }

        var intermediates = FindVerticesOnSegment(va, vb);
        if (intermediates.Count > 0)
        {
            int prev = va;
            foreach (int mid in intermediates)
            {
                InsertConstraintSegment(prev, mid);
                prev = mid;
            }
            InsertConstraintSegment(prev, vb);
            return;
        }

        InsertConstraintSegment(va, vb);
    }

    private void InsertConstraintSegment(int va, int vb)
    {
        if (va == vb) return;
        constraintEdges.Add(EdgeKeyOrdered(va, vb));

        if (EdgeExists(va, vb))
        {
            constraintsInserted++;
            return;
        }

        var crossedTris = new List<int>();
        var upper = new List<int>();
        var lower = new List<int>();

        if (!WalkConstraint(va, vb, crossedTris, upper, lower))
        {
            constraintsFailed++;
            if (GameDebug.Pathfinding)
                Debug.LogWarning($"[CDT] Constraint walk FAILED: v{va}({vertices[va]:F1}) -> v{vb}({vertices[vb]:F1})");
            return;
        }

        foreach (int ti in crossedTris)
            triAlive[ti] = false;

        upper.Reverse();
        TriangulateCavity(upper);
        TriangulateCavity(lower);

        if (EdgeExists(va, vb))
            constraintsInserted++;
        else
        {
            constraintsFailed++;
            if (GameDebug.Pathfinding)
                Debug.LogWarning($"[CDT] Constraint cavity FAILED: v{va}({vertices[va]:F1}) -> v{vb}({vertices[vb]:F1})");
        }
    }

    /// <summary>
    /// Find all CDT vertices that lie strictly on the open segment (va, vb).
    /// Returns them sorted by distance from va.
    /// </summary>
    private List<int> FindVerticesOnSegment(int va, int vb)
    {
        var result = new List<int>();
        Vector2 a = vertices[va];
        Vector2 b = vertices[vb];
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 1e-8f) return result;

        float invLenSq = 1f / lenSq;
        float crossEps = 0.01f * Mathf.Sqrt(lenSq);

        for (int i = 0; i < vertices.Count; i++)
        {
            if (i == va || i == vb) continue;
            if (i == superV0 || i == superV1 || i == superV2) continue;

            Vector2 ap = vertices[i] - a;
            float cross = ab.x * ap.y - ab.y * ap.x;
            if (Mathf.Abs(cross) > crossEps) continue;

            float dot = Vector2.Dot(ap, ab) * invLenSq;
            if (dot <= 0.001f || dot >= 0.999f) continue;

            result.Add(i);
        }

        result.Sort((x, y) =>
        {
            float dx = Vector2.Dot(vertices[x] - a, ab);
            float dy = Vector2.Dot(vertices[y] - a, ab);
            return dx.CompareTo(dy);
        });
        return result;
    }

    /// <summary>
    /// Walk from va toward vb through the triangulation, collecting all crossed
    /// triangles and building upper/lower cavity boundary vertex chains.
    /// Upper = vertices to the LEFT of directed segment va→vb.
    /// Lower = vertices to the RIGHT.
    /// </summary>
    private bool WalkConstraint(int va, int vb, List<int> crossedTris,
        List<int> upper, List<int> lower)
    {
        Vector2 a = vertices[va];
        Vector2 b = vertices[vb];
        Vector2 ab = b - a;

        upper.Add(va);
        lower.Add(va);

        int startTri = -1;
        int exitA = -1, exitB = -1;

        for (int ti = 0; ti < triangles.Count; ti++)
        {
            if (!triAlive[ti]) continue;
            int[] tri = triangles[ti];

            int vaIdx = -1;
            for (int k = 0; k < 3; k++)
                if (tri[k] == va) { vaIdx = k; break; }
            if (vaIdx < 0) continue;

            int p = tri[(vaIdx + 1) % 3];
            int q = tri[(vaIdx + 2) % 3];

            Vector2 dp = vertices[p] - a;
            Vector2 dq = vertices[q] - a;

            float cp = dp.x * ab.y - dp.y * ab.x;
            float cq = dq.x * ab.y - dq.y * ab.x;
            float cpq = dp.x * dq.y - dp.y * dq.x;

            bool exits;
            if (cpq > 0f)
                exits = cp >= -1e-6f && cq <= 1e-6f;
            else if (cpq < 0f)
                exits = cq >= -1e-6f && cp <= 1e-6f;
            else
                continue;

            if (exits)
            {
                startTri = ti;
                exitA = p;
                exitB = q;
                break;
            }
        }

        if (startTri < 0) return false;

        crossedTris.Add(startTri);

        float sideA = ab.x * (vertices[exitA].y - a.y) - ab.y * (vertices[exitA].x - a.x);
        float sideB = ab.x * (vertices[exitB].y - a.y) - ab.y * (vertices[exitB].x - a.x);

        if (sideA > 0f) { upper.Add(exitA); lower.Add(exitB); }
        else { upper.Add(exitB); lower.Add(exitA); }

        int edgeA = exitA, edgeB = exitB;
        int maxIter = triangles.Count;
        var visited = new HashSet<int>(crossedTris);

        for (int iter = 0; iter < maxIter; iter++)
        {
            int adjTri = FindAdjacentTriangleExcluding(visited, edgeA, edgeB);
            if (adjTri < 0) return false;

            int opp = GetOppositeVertex(adjTri, edgeA, edgeB);
            if (opp < 0) return false;

            crossedTris.Add(adjTri);
            visited.Add(adjTri);

            if (opp == vb)
            {
                upper.Add(vb);
                lower.Add(vb);
                return true;
            }

            float side = ab.x * (vertices[opp].y - a.y) - ab.y * (vertices[opp].x - a.x);
            if (side > 0f) upper.Add(opp);
            else lower.Add(opp);

            if (EdgesIntersect(a, b, vertices[edgeA], vertices[opp]))
                edgeB = opp;
            else
                edgeA = opp;
        }

        return false;
    }

    /// <summary>
    /// Retriangulate a polygonal cavity using recursive Delaunay triangulation
    /// (Chew's algorithm). polygon = [v0, v1, ..., vn-1] where (v0, vn-1) is
    /// the constraint base edge.
    /// </summary>
    private void TriangulateCavity(List<int> polygon)
    {
        int n = polygon.Count;
        if (n < 3) return;

        if (n == 3)
        {
            triangles.Add(new[] { polygon[0], polygon[1], polygon[2] });
            triAlive.Add(true);
            return;
        }

        int bestIdx = 1;
        for (int i = 2; i < n - 1; i++)
        {
            if (InCircumcircleRaw(vertices[polygon[bestIdx]],
                vertices[polygon[0]], vertices[polygon[i]], vertices[polygon[n - 1]]))
            {
                bestIdx = i;
            }
        }

        triangles.Add(new[] { polygon[0], polygon[bestIdx], polygon[n - 1] });
        triAlive.Add(true);

        if (bestIdx > 1)
        {
            var left = new List<int>(bestIdx + 1);
            for (int i = 0; i <= bestIdx; i++)
                left.Add(polygon[i]);
            TriangulateCavity(left);
        }

        if (bestIdx < n - 2)
        {
            var right = new List<int>(n - bestIdx);
            for (int i = bestIdx; i < n; i++)
                right.Add(polygon[i]);
            TriangulateCavity(right);
        }
    }

    private static bool InCircumcircleRaw(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float ax = a.x - p.x, ay = a.y - p.y;
        float bx = b.x - p.x, by = b.y - p.y;
        float cx = c.x - p.x, cy = c.y - p.y;

        float det = ax * (by * (cx * cx + cy * cy) - cy * (bx * bx + by * by))
                  - bx * (ay * (cx * cx + cy * cy) - cy * (ax * ax + ay * ay))
                  + cx * (ay * (bx * bx + by * by) - by * (ax * ax + ay * ay));

        float cross = NavMeshData.Cross2D(b - a, c - a);
        if (Mathf.Abs(cross) < 1e-10f) return false;
        return cross > 0 ? det > 0 : det < 0;
    }

    /// <summary>
    /// Output the final NavMeshData, excluding super-triangle vertices and
    /// non-walkable triangles (those inside obstacles).
    /// Each triangle is rasterized onto the grid for exhaustive walkability checking.
    /// </summary>
    public NavMeshData BuildNavMesh(System.Func<Vector2, bool> isWalkable,
        float gridCellSize = 1f, Vector2 gridOrigin = default)
    {
        var mesh = new NavMeshData();
        var vertexRemap = new Dictionary<int, int>();
        degenerateTrianglesSkipped = 0;

        for (int i = 0; i < vertices.Count; i++)
        {
            if (i == superV0 || i == superV1 || i == superV2) continue;
            vertexRemap[i] = mesh.AddVertex(vertices[i]);
        }

        int superSkipped = 0;
        int unwalkable = 0;

        for (int ti = 0; ti < triangles.Count; ti++)
        {
            if (!triAlive[ti]) continue;
            int[] tri = triangles[ti];

            if (tri[0] == superV0 || tri[0] == superV1 || tri[0] == superV2 ||
                tri[1] == superV0 || tri[1] == superV1 || tri[1] == superV2 ||
                tri[2] == superV0 || tri[2] == superV1 || tri[2] == superV2)
            {
                superSkipped++;
                continue;
            }

            if (!vertexRemap.ContainsKey(tri[0]) ||
                !vertexRemap.ContainsKey(tri[1]) ||
                !vertexRemap.ContainsKey(tri[2]))
                continue;

            int nv0 = vertexRemap[tri[0]];
            int nv1 = vertexRemap[tri[1]];
            int nv2 = vertexRemap[tri[2]];

            Vector2 a = mesh.Vertices[nv0];
            Vector2 b = mesh.Vertices[nv1];
            Vector2 c = mesh.Vertices[nv2];

            float cross = NavMeshData.Cross2D(b - a, c - a);

            if (cross < 0f)
            {
                (nv1, nv2) = (nv2, nv1);
                (b, c) = (c, b);
            }

            float area = Mathf.Abs(cross) * 0.5f;
            if (area < 0.001f)
            {
                degenerateTrianglesSkipped++;
                continue;
            }

            // Rasterize the triangle onto the grid and check every covered cell.
            // This guarantees no building cell is missed regardless of triangle size.
            bool walkable = IsTriangleFullyWalkable(a, b, c, isWalkable, gridCellSize, gridOrigin);
            if (!walkable) unwalkable++;
            mesh.AddTriangle(nv0, nv1, nv2, walkable);
        }

        mesh.BuildAdjacency();
        mesh.ComputeAllWidths();

        if (GameDebug.Pathfinding)
        {
            int walkableCount = mesh.TriangleCount - unwalkable;
            int isolatedWalkable = 0;
            for (int i = 0; i < mesh.TriangleCount; i++)
            {
                if (!mesh.Triangles[i].IsWalkable) continue;
                if (mesh.Triangles[i].N0 < 0 && mesh.Triangles[i].N1 < 0 && mesh.Triangles[i].N2 < 0)
                    isolatedWalkable++;
            }

            Debug.Log($"[CDT] BuildNavMesh summary:\n" +
                $"  vertices: {mesh.VertexCount} (deduped from {vertices.Count - 3} input)\n" +
                $"  triangles: {mesh.TriangleCount} (walkable={walkableCount}, unwalkable={unwalkable})\n" +
                $"  skipped: super={superSkipped}, degenerate={degenerateTrianglesSkipped}\n" +
                $"  constraints: inserted={constraintsInserted}, failed={constraintsFailed}\n" +
                $"  isolated walkable tris (no neighbors): {isolatedWalkable}");

            if (constraintsFailed > 0)
                Debug.LogWarning($"[CDT] {constraintsFailed} constraint edges failed to insert — obstacle boundaries may have gaps!");
            if (isolatedWalkable > 0)
                Debug.LogWarning($"[CDT] {isolatedWalkable} walkable triangles have NO neighbors — pathfinding will fail through them!");
        }

        return mesh;
    }

    // ================================================================
    //  GEOMETRY HELPERS
    // ================================================================

    private bool InCircumcircle(Vector2 p, int[] tri)
    {
        Vector2 a = vertices[tri[0]];
        Vector2 b = vertices[tri[1]];
        Vector2 c = vertices[tri[2]];

        float ax = a.x - p.x, ay = a.y - p.y;
        float bx = b.x - p.x, by = b.y - p.y;
        float cx = c.x - p.x, cy = c.y - p.y;

        float det = ax * (by * (cx * cx + cy * cy) - cy * (bx * bx + by * by))
                  - bx * (ay * (cx * cx + cy * cy) - cy * (ax * ax + ay * ay))
                  + cx * (ay * (bx * bx + by * by) - by * (ax * ax + ay * ay));

        // For CCW triangles, point inside circumcircle when det > 0
        float cross = NavMeshData.Cross2D(b - a, c - a);
        return cross > 0 ? det > 0 : det < 0;
    }

    private static void CountEdge(Dictionary<long, int> edgeCount, int a, int b)
    {
        long key = EdgeKeyOrdered(a, b);
        edgeCount.TryGetValue(key, out int count);
        edgeCount[key] = count + 1;
    }

    private static void TryAddBoundaryEdge(List<int[]> polygon, Dictionary<long, int> edgeCount, int a, int b)
    {
        long key = EdgeKeyOrdered(a, b);
        if (edgeCount[key] == 1)
            polygon.Add(new[] { a, b });
    }

    private static long EdgeKeyOrdered(int a, int b)
    {
        int lo = a < b ? a : b;
        int hi = a < b ? b : a;
        return ((long)lo << 32) | (uint)hi;
    }

    private bool EdgeExists(int va, int vb)
    {
        for (int ti = 0; ti < triangles.Count; ti++)
        {
            if (!triAlive[ti]) continue;
            int[] tri = triangles[ti];
            if (HasEdge(tri, va, vb))
                return true;
        }
        return false;
    }

    private static bool HasEdge(int[] tri, int a, int b)
    {
        for (int i = 0; i < 3; i++)
        {
            int j = (i + 1) % 3;
            if ((tri[i] == a && tri[j] == b) || (tri[i] == b && tri[j] == a))
                return true;
        }
        return false;
    }

    private bool IsConstraintEdge(int a, int b)
    {
        return constraintEdges.Contains(EdgeKeyOrdered(a, b));
    }

    private int FindAdjacentTriangle(int triIdx, int ea, int eb)
    {
        for (int ti = 0; ti < triangles.Count; ti++)
        {
            if (!triAlive[ti] || ti == triIdx) continue;
            if (HasEdge(triangles[ti], ea, eb))
                return ti;
        }
        return -1;
    }

    private int FindAdjacentTriangleExcluding(HashSet<int> excluded, int ea, int eb)
    {
        for (int ti = 0; ti < triangles.Count; ti++)
        {
            if (!triAlive[ti] || excluded.Contains(ti)) continue;
            if (HasEdge(triangles[ti], ea, eb))
                return ti;
        }
        return -1;
    }

    private int GetOppositeVertex(int triIdx, int ea, int eb)
    {
        int[] tri = triangles[triIdx];
        for (int i = 0; i < 3; i++)
        {
            if (tri[i] != ea && tri[i] != eb)
                return tri[i];
        }
        return -1;
    }

    /// <summary>
    /// Test if segments (p1,p2) and (p3,p4) properly intersect (cross each other).
    /// Shared endpoints do not count as intersection.
    /// </summary>
    private static bool EdgesIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        float d1 = NavMeshData.Cross2D(p4 - p3, p1 - p3);
        float d2 = NavMeshData.Cross2D(p4 - p3, p2 - p3);
        float d3 = NavMeshData.Cross2D(p2 - p1, p3 - p1);
        float d4 = NavMeshData.Cross2D(p2 - p1, p4 - p1);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
            ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;

        return false;
    }

    /// <summary>
    /// Rasterize a triangle onto the grid and check every overlapping grid cell
    /// center. Returns false if ANY cell center inside the triangle is unwalkable.
    /// Uses actual grid cell centers (aligned to gridOrigin) to guarantee no
    /// cell is missed.
    /// </summary>
    private static bool IsTriangleFullyWalkable(Vector2 a, Vector2 b, Vector2 c,
        System.Func<Vector2, bool> isWalkable, float cellSize, Vector2 gridOrigin)
    {
        if (!isWalkable((a + b + c) / 3f)) return false;

        float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
        float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
        float minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
        float maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));

        // Compute grid cell index range covering the triangle's bounding box
        int cellMinX = Mathf.FloorToInt((minX - gridOrigin.x) / cellSize) - 1;
        int cellMaxX = Mathf.CeilToInt((maxX - gridOrigin.x) / cellSize) + 1;
        int cellMinY = Mathf.FloorToInt((minY - gridOrigin.y) / cellSize) - 1;
        int cellMaxY = Mathf.CeilToInt((maxY - gridOrigin.y) / cellSize) + 1;

        for (int cx = cellMinX; cx <= cellMaxX; cx++)
        {
            for (int cy = cellMinY; cy <= cellMaxY; cy++)
            {
                // Compute actual grid cell center in world space
                float wx = gridOrigin.x + cx * cellSize;
                float wy = gridOrigin.y + cy * cellSize;
                Vector2 cellCenter = new Vector2(wx, wy);

                if (!NavMeshData.PointInTriangle(cellCenter, a, b, c)) continue;
                if (!isWalkable(cellCenter)) return false;
            }
        }
        return true;
    }
}
