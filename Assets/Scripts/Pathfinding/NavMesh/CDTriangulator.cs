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
    private readonly Dictionary<int, HashSet<int>> vertToTris = new();

    private int superV0, superV1, superV2;
    private int constraintsInserted;
    private int constraintsFailed;
    private int degenerateTrianglesSkipped;

    public int VertexCount => vertices.Count;

    public int ConstraintsFailed => constraintsFailed;

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

    private int AddTriangleIndexed(int v0, int v1, int v2)
    {
        int idx = triangles.Count;
        triangles.Add(new[] { v0, v1, v2 });
        triAlive.Add(true);
        AddToAdjacency(idx, v0, v1, v2);
        return idx;
    }

    private void KillTriangle(int ti)
    {
        if (!triAlive[ti]) return;
        triAlive[ti] = false;
        int[] tri = triangles[ti];
        RemoveFromAdjacency(ti, tri[0], tri[1], tri[2]);
    }

    private void AddToAdjacency(int ti, int v0, int v1, int v2)
    {
        GetOrCreateSet(v0).Add(ti);
        GetOrCreateSet(v1).Add(ti);
        GetOrCreateSet(v2).Add(ti);
    }

    private void RemoveFromAdjacency(int ti, int v0, int v1, int v2)
    {
        if (vertToTris.TryGetValue(v0, out var s0)) s0.Remove(ti);
        if (vertToTris.TryGetValue(v1, out var s1)) s1.Remove(ti);
        if (vertToTris.TryGetValue(v2, out var s2)) s2.Remove(ti);
    }

    private HashSet<int> GetOrCreateSet(int v)
    {
        if (!vertToTris.TryGetValue(v, out var set))
        {
            set = new HashSet<int>(8);
            vertToTris[v] = set;
        }
        return set;
    }

    private static long HashVertex(Vector2 v)
    {
        int hx = Mathf.RoundToInt(v.x * GeometryConstants.VertexDeduplicationScale);
        int hy = Mathf.RoundToInt(v.y * GeometryConstants.VertexDeduplicationScale);
        return ((long)hx << 32) | (uint)hy;
    }

    /// <summary>
    /// Run Bowyer-Watson incremental Delaunay triangulation on all added vertices.
    /// Uses a compact alive-set so circumcircle scans never touch dead triangles.
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

        superV0 = AddVertex(new Vector2(midX - GeometryConstants.SuperTriangleMultiplier * dmax, midY - dmax));
        superV1 = AddVertex(new Vector2(midX, midY + GeometryConstants.SuperTriangleMultiplier * dmax));
        superV2 = AddVertex(new Vector2(midX + GeometryConstants.SuperTriangleMultiplier * dmax, midY - dmax));

        triangles.Clear();
        triAlive.Clear();
        triangles.Add(new[] { superV0, superV1, superV2 });
        triAlive.Add(true);

        var aliveSet = new HashSet<int> { 0 };
        var badTriangles = new List<int>();
        var polygon = new List<int[]>();
        var edgeCount = new Dictionary<long, int>();

        int numPoints = vertices.Count - 3;

        for (int pi = 0; pi < numPoints; pi++)
        {
            Vector2 p = vertices[pi];
            badTriangles.Clear();

            foreach (int ti in aliveSet)
            {
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
            {
                triAlive[ti] = false;
                aliveSet.Remove(ti);
            }

            foreach (int[] edge in polygon)
            {
                int newIdx = triangles.Count;
                triangles.Add(new[] { edge[0], edge[1], pi });
                triAlive.Add(true);
                aliveSet.Add(newIdx);
            }
        }

        // Build the vertex-to-triangle adjacency index for fast lookups
        // during constraint insertion.
        vertToTris.Clear();
        int alive = 0;
        for (int i = 0; i < triAlive.Count; i++)
        {
            if (!triAlive[i]) continue;
            alive++;
            int[] tri = triangles[i];
            AddToAdjacency(i, tri[0], tri[1], tri[2]);
        }

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
            if (crossedTris.Count == 0 && upper.Count <= 1 && lower.Count <= 1)
            {
                // Walk found no starting triangle. Check if vertices became
                // orphaned (all their triangles killed by prior insertions).
                // Re-insert orphaned vertices before retrying.
                bool vaOrphaned = !HasAnyAliveTriangle(va);
                bool vbOrphaned = !HasAnyAliveTriangle(vb);

                if (vaOrphaned) ReinsertOrphanedVertex(va);
                if (vbOrphaned) ReinsertOrphanedVertex(vb);

                if (EdgeExists(va, vb))
                {
                    constraintsInserted++;
                    return;
                }

                if (BruteForceInsertConstraint(va, vb))
                    return;

                return;
            }

            if (FlipInsertFallback(va, vb))
                return;

            if (BruteForceInsertConstraint(va, vb))
                return;

            constraintsFailed++;
            Debug.LogWarning($"[CDT] Constraint FAILED (walk partial, crossed={crossedTris.Count}): v{va}({vertices[va]:F1}) -> v{vb}({vertices[vb]:F1})");
            return;
        }

        foreach (int ti in crossedTris)
            KillTriangle(ti);

        upper.Reverse();
        TriangulateCavity(upper);
        TriangulateCavity(lower);

        if (EdgeExists(va, vb))
            constraintsInserted++;
        else
        {
            if (FlipInsertFallback(va, vb))
                return;

            if (BruteForceInsertConstraint(va, vb))
                return;

            constraintsFailed++;
            Debug.LogWarning($"[CDT] Constraint cavity FAILED: v{va}({vertices[va]:F1}) -> v{vb}({vertices[vb]:F1})");
        }
    }

    /// <summary>
    /// Fallback constraint insertion using edge flips (Sloan 1993).
    /// TRANSACTIONAL: all changes are rolled back on failure to prevent
    /// corrupting the triangulation for subsequent fallback methods.
    /// </summary>
    private bool FlipInsertFallback(int va, int vb)
    {
        Vector2 a = vertices[va];
        Vector2 b = vertices[vb];

        int snapshotTriCount = triangles.Count;
        int snapshotAliveCount = triAlive.Count;
        var killedOriginals = new List<int>();

        int maxIter = snapshotTriCount * 4;
        for (int iter = 0; iter < maxIter; iter++)
        {
            if (EdgeExists(va, vb))
            {
                constraintsInserted++;
                return true;
            }

            bool flippedAny = false;
            int triCount = triangles.Count;

            for (int ti = 0; ti < triCount && !flippedAny; ti++)
            {
                if (!triAlive[ti]) continue;
                int[] tri = triangles[ti];

                for (int e = 0; e < 3; e++)
                {
                    int v0 = tri[e];
                    int v1 = tri[(e + 1) % 3];

                    if (v0 == va || v0 == vb || v1 == va || v1 == vb) continue;
                    if (constraintEdges.Contains(EdgeKeyOrdered(v0, v1))) continue;
                    if (!EdgesIntersect(a, b, vertices[v0], vertices[v1])) continue;

                    int adjTri = FindAdjacentTriangle(ti, v0, v1);
                    if (adjTri < 0) continue;

                    int v2 = GetOppositeVertex(ti, v0, v1);
                    int v3 = GetOppositeVertex(adjTri, v0, v1);
                    if (v2 < 0 || v3 < 0) continue;

                    if (!IsConvexQuad(v0, v1, v2, v3)) continue;

                    if (ti < snapshotTriCount) killedOriginals.Add(ti);
                    if (adjTri < snapshotTriCount) killedOriginals.Add(adjTri);
                    KillTriangle(ti);
                    KillTriangle(adjTri);
                    AddTriangleIndexed(v2, v0, v3);
                    AddTriangleIndexed(v2, v3, v1);

                    flippedAny = true;
                    break;
                }
            }

            if (!flippedAny)
                flippedAny = TryFlipToward(va, vb, snapshotTriCount, killedOriginals);

            if (!flippedAny) break;
        }

        if (EdgeExists(va, vb))
        {
            constraintsInserted++;
            return true;
        }

        // ROLLBACK: restore killed originals and their adjacency, remove added triangles.
        for (int r = triangles.Count - 1; r >= snapshotTriCount; r--)
            KillTriangle(r);
        int removeFrom = snapshotTriCount;
        if (triangles.Count > removeFrom)
        {
            triangles.RemoveRange(removeFrom, triangles.Count - removeFrom);
            triAlive.RemoveRange(removeFrom, triAlive.Count - removeFrom);
        }
        foreach (int ti in killedOriginals)
        {
            triAlive[ti] = true;
            int[] tri = triangles[ti];
            AddToAdjacency(ti, tri[0], tri[1], tri[2]);
        }
        return false;
    }

    /// <summary>
    /// Combined flip-toward: first tries adjacent diagonal flip (va and vb
    /// share adjacent triangles), then progressive flip (extend va's reach
    /// toward vb by flipping opposite edges).
    /// </summary>
    private bool TryFlipToward(int va, int vb, int snapshotTriCount, List<int> killedOriginals)
    {
        Vector2 a = vertices[va];
        Vector2 b = vertices[vb];
        Vector2 ab = b - a;
        float abLenSq = ab.sqrMagnitude;

        if (!vertToTris.TryGetValue(va, out var vaTrisSet)) return false;
        var vaTris = new List<int>(vaTrisSet);
        foreach (int ti in vaTris)
        {
            if (!triAlive[ti]) continue;
            int[] tri = triangles[ti];

            int vaIdx = -1;
            for (int k = 0; k < 3; k++)
                if (tri[k] == va) { vaIdx = k; break; }
            if (vaIdx < 0) continue;

            int p = tri[(vaIdx + 1) % 3];
            int q = tri[(vaIdx + 2) % 3];

            if (constraintEdges.Contains(EdgeKeyOrdered(p, q))) continue;

            int adjTri = FindAdjacentTriangle(ti, p, q);
            if (adjTri < 0) continue;

            int[] adjVerts = triangles[adjTri];
            bool adjHasVb = adjVerts[0] == vb || adjVerts[1] == vb || adjVerts[2] == vb;

            if (adjHasVb)
            {
                if (!IsConvexQuad(p, q, va, vb)) continue;

                if (ti < snapshotTriCount) killedOriginals.Add(ti);
                if (adjTri < snapshotTriCount) killedOriginals.Add(adjTri);
                KillTriangle(ti);
                KillTriangle(adjTri);
                AddTriangleIndexed(va, p, vb);
                AddTriangleIndexed(va, vb, q);
                return true;
            }

            if (abLenSq < 1e-8f) continue;

            int opp = GetOppositeVertex(adjTri, p, q);
            if (opp < 0 || opp == va || opp == vb) continue;

            float cp = CrossConstraint(ab, vertices[p] - a);
            float cq = CrossConstraint(ab, vertices[q] - a);
            if (cp * cq > 0f) continue;

            float tOpp = Vector2.Dot(vertices[opp] - a, ab) / abLenSq;
            if (tOpp <= 0.01f || tOpp >= 0.99f) continue;

            if (!IsConvexQuad(p, q, va, opp)) continue;

            if (ti < snapshotTriCount) killedOriginals.Add(ti);
            if (adjTri < snapshotTriCount) killedOriginals.Add(adjTri);
            KillTriangle(ti);
            KillTriangle(adjTri);
            AddTriangleIndexed(va, p, opp);
            AddTriangleIndexed(va, opp, q);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Convexity test for the quadrilateral formed by two triangles sharing
    /// edge (v0, v1) with opposite vertices v2 and v3.
    /// Returns true only if v2, v3 are on opposite sides of edge (v0,v1)
    /// AND v0, v1 are on opposite sides of edge (v2,v3).
    /// </summary>
    private bool IsConvexQuad(int v0, int v1, int v2, int v3)
    {
        float c1 = NavMeshData.Cross2D(vertices[v1] - vertices[v0], vertices[v2] - vertices[v0]);
        float c2 = NavMeshData.Cross2D(vertices[v1] - vertices[v0], vertices[v3] - vertices[v0]);
        if (c1 * c2 >= 0f) return false;

        float c3 = NavMeshData.Cross2D(vertices[v3] - vertices[v2], vertices[v0] - vertices[v2]);
        float c4 = NavMeshData.Cross2D(vertices[v3] - vertices[v2], vertices[v1] - vertices[v2]);
        if (c3 * c4 >= 0f) return false;

        return true;
    }

    private bool HasAnyAliveTriangle(int v)
    {
        return vertToTris.TryGetValue(v, out var set) && set.Count > 0;
    }

    /// <summary>
    /// Re-inserts an orphaned vertex into the alive triangulation by finding
    /// the alive triangle that contains the vertex's position and splitting it.
    /// </summary>
    private void ReinsertOrphanedVertex(int v)
    {
        Vector2 p = vertices[v];

        // Find the alive triangle that geometrically contains this point.
        int containingTri = -1;
        for (int ti = 0; ti < triangles.Count; ti++)
        {
            if (!triAlive[ti]) continue;
            int[] tri = triangles[ti];
            if (NavMeshData.PointInTriangle(p, vertices[tri[0]], vertices[tri[1]], vertices[tri[2]]))
            {
                containingTri = ti;
                break;
            }
        }

        if (containingTri < 0)
        {
            // Point not inside any alive triangle — find the closest one.
            float bestDist = float.MaxValue;
            for (int ti = 0; ti < triangles.Count; ti++)
            {
                if (!triAlive[ti]) continue;
                int[] tri = triangles[ti];
                float d = NavMeshData.SqrDistanceToTriangle(p,
                    vertices[tri[0]], vertices[tri[1]], vertices[tri[2]]);
                if (d < bestDist)
                {
                    bestDist = d;
                    containingTri = ti;
                }
            }
        }

        if (containingTri < 0) return;

        // Split the triangle at vertex v, creating 3 new triangles.
        int[] ct = triangles[containingTri];
        int a = ct[0], b = ct[1], c = ct[2];

        // Check if v coincides with any triangle vertex.
        if (v == a || v == b || v == c) return;

        KillTriangle(containingTri);
        AddTriangleIndexed(v, a, b);
        AddTriangleIndexed(v, b, c);
        AddTriangleIndexed(v, c, a);
    }

    /// <summary>
    /// Last-resort constraint insertion: collects a corridor of triangles between
    /// va and vb (including triangles incident to va/vb that face toward the
    /// constraint), kills them all, and retriangulates the upper/lower cavities.
    /// </summary>
    private bool BruteForceInsertConstraint(int va, int vb)
    {
        if (EdgeExists(va, vb))
        {
            constraintsInserted++;
            return true;
        }

        Vector2 a = vertices[va];
        Vector2 b = vertices[vb];
        Vector2 ab = b - a;
        float abLenSq = ab.sqrMagnitude;
        if (abLenSq < 1e-8f) return false;

        float abLen = Mathf.Sqrt(abLenSq);
        float sosEps = GeometryConstants.SoSPerturbation(ab.x, ab.y);
        Vector2 perpBF = new Vector2(-ab.y, ab.x) * (sosEps / abLen);
        Vector2 apBF = a + perpBF;

        var corridor = new HashSet<int>();

        // Phase 1: collect triangles incident to va that face toward vb.
        CollectFacingTriangles(va, vb, ab, corridor);

        // Phase 2: collect triangles incident to vb that face toward va.
        CollectFacingTriangles(vb, va, -ab, corridor);

        // Phase 3: flood-fill from the initial corridor triangles to collect
        // any triangles between them that the constraint passes through.
        bool expanded = true;
        while (expanded)
        {
            expanded = false;
            var toAdd = new List<int>();
            foreach (int ti in corridor)
            {
                int[] tri = triangles[ti];
                for (int e = 0; e < 3; e++)
                {
                    int v0 = tri[e];
                    int v1 = tri[(e + 1) % 3];
                    int adj = FindAdjacentTriangle(ti, v0, v1);
                    if (adj < 0 || !triAlive[adj] || corridor.Contains(adj)) continue;

                    int[] adjTri = triangles[adj];
                    bool adjHasVa = adjTri[0] == va || adjTri[1] == va || adjTri[2] == va;
                    bool adjHasVb = adjTri[0] == vb || adjTri[1] == vb || adjTri[2] == vb;
                    if (adjHasVa || adjHasVb)
                    {
                        toAdd.Add(adj);
                        continue;
                    }

                    if (TriangleIntersectsSegment(adj, a, b))
                        toAdd.Add(adj);
                }
            }
            foreach (int ti in toAdd)
            {
                if (corridor.Add(ti))
                    expanded = true;
            }
        }

        if (corridor.Count == 0) return false;

        // Build upper/lower boundary from corridor edges.
        var edgeCount = new Dictionary<long, int>();
        foreach (int ti in corridor)
        {
            int[] tri = triangles[ti];
            CountEdge(edgeCount, tri[0], tri[1]);
            CountEdge(edgeCount, tri[1], tri[2]);
            CountEdge(edgeCount, tri[2], tri[0]);
        }

        var upperVerts = new HashSet<int>();
        var lowerVerts = new HashSet<int>();
        upperVerts.Add(va);
        lowerVerts.Add(va);

        foreach (var kvp in edgeCount)
        {
            if (kvp.Value != 1) continue;
            int ea = (int)(kvp.Key >> 32);
            int eb = (int)(kvp.Key & 0xFFFFFFFFL);

            ClassifyBoundaryVertex(ea, apBF, ab, upperVerts, lowerVerts);
            ClassifyBoundaryVertex(eb, apBF, ab, upperVerts, lowerVerts);
        }

        upperVerts.Add(vb);
        lowerVerts.Add(vb);

        var upper = new List<int>(upperVerts);
        var lower = new List<int>(lowerVerts);

        upper.Sort((x, y) =>
        {
            if (x == va) return -1;
            if (y == va) return 1;
            if (x == vb) return 1;
            if (y == vb) return -1;
            return Vector2.Dot(vertices[x] - a, ab).CompareTo(
                Vector2.Dot(vertices[y] - a, ab));
        });
        lower.Sort((x, y) =>
        {
            if (x == va) return -1;
            if (y == va) return 1;
            if (x == vb) return 1;
            if (y == vb) return -1;
            return Vector2.Dot(vertices[x] - a, ab).CompareTo(
                Vector2.Dot(vertices[y] - a, ab));
        });

        foreach (int ti in corridor)
            KillTriangle(ti);

        upper.Reverse();
        TriangulateCavity(upper);
        TriangulateCavity(lower);

        if (EdgeExists(va, vb))
        {
            constraintsInserted++;
            return true;
        }
        return false;
    }

    private void ClassifyBoundaryVertex(int v, Vector2 perturbedA, Vector2 ab,
        HashSet<int> upper, HashSet<int> lower)
    {
        float c = CrossConstraint(ab, vertices[v] - perturbedA);
        if (c > 0) upper.Add(v);
        else if (c < 0) lower.Add(v);
        else { upper.Add(v); lower.Add(v); }
    }

    /// <summary>
    /// Collects alive triangles incident to vertex 'from' whose opposite edge
    /// faces toward vertex 'toward' (the constraint direction exits through
    /// that edge).
    /// </summary>
    private void CollectFacingTriangles(int from, int toward, Vector2 dir,
        HashSet<int> corridor)
    {
        if (!vertToTris.TryGetValue(from, out var set)) return;
        Vector2 fromPos = vertices[from];

        foreach (int ti in set)
        {
            if (!triAlive[ti]) continue;
            int[] tri = triangles[ti];

            int fIdx = -1;
            for (int k = 0; k < 3; k++)
                if (tri[k] == from) { fIdx = k; break; }
            if (fIdx < 0) continue;

            int p = tri[(fIdx + 1) % 3];
            int q = tri[(fIdx + 2) % 3];

            float cp = CrossConstraint(dir, vertices[p] - fromPos);
            float cq = CrossConstraint(dir, vertices[q] - fromPos);

            if (cp * cq <= 0f)
                corridor.Add(ti);
        }
    }

    /// <summary>
    /// Tests whether the given alive triangle intersects segment (a, b).
    /// Includes proper crossings, touching cases, and interior containment.
    /// </summary>
    private bool TriangleIntersectsSegment(int ti, Vector2 a, Vector2 b)
    {
        int[] tri = triangles[ti];
        Vector2 ta = vertices[tri[0]];
        Vector2 tb = vertices[tri[1]];
        Vector2 tc = vertices[tri[2]];

        if (EdgesIntersect(a, b, ta, tb)) return true;
        if (EdgesIntersect(a, b, tb, tc)) return true;
        if (EdgesIntersect(a, b, tc, ta)) return true;

        Vector2 mid = (a + b) * 0.5f;
        if (NavMeshData.PointInTriangle(mid, ta, tb, tc)) return true;

        // Check several sample points along the segment.
        for (float t = 0.25f; t <= 0.75f; t += 0.25f)
        {
            Vector2 pt = a + (b - a) * t;
            if (NavMeshData.PointInTriangle(pt, ta, tb, tc)) return true;
        }

        return false;
    }

    /// <summary>
    /// Find the closest mesh-adjacent vertex of va that lies strictly on the
    /// open segment (va, vb). Used as a fallback when WalkConstraint fails
    /// because the constraint is collinear with existing triangulation edges
    /// (common for horizontal/vertical building edges on a grid).
    /// </summary>
    private int FindAdjacentVertexOnSegment(int va, int vb)
    {
        Vector2 a = vertices[va];
        Vector2 b = vertices[vb];
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 1e-8f) return -1;

        float crossEps = GeometryConstants.CollinearEps(Mathf.Sqrt(lenSq));
        float bestT = float.MaxValue;
        int bestVert = -1;

        if (vertToTris.TryGetValue(va, out var vaTriSet))
        {
            foreach (int ti in vaTriSet)
            {
                if (!triAlive[ti]) continue;
                int[] tri = triangles[ti];

                int vaIdx = -1;
                for (int k = 0; k < 3; k++)
                    if (tri[k] == va) { vaIdx = k; break; }
                if (vaIdx < 0) continue;

                for (int d = 1; d <= 2; d++)
                {
                    int v = tri[(vaIdx + d) % 3];
                    if (v == vb || IsSuperVertex(v)) continue;

                    Vector2 av = vertices[v] - a;
                    float cross = ab.x * av.y - ab.y * av.x;
                    if (Mathf.Abs(cross) > crossEps) continue;

                    float t = Vector2.Dot(av, ab) / lenSq;
                    if (t > 0.001f && t < 0.999f && t < bestT)
                    {
                        bestT = t;
                        bestVert = v;
                    }
                }
            }
        }

        return bestVert;
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
        float crossEps = GeometryConstants.CollinearEps(Mathf.Sqrt(lenSq));

        // AABB filter: only check vertices within the segment's bounding box + epsilon
        float bboxMinX = Mathf.Min(a.x, b.x) - crossEps;
        float bboxMaxX = Mathf.Max(a.x, b.x) + crossEps;
        float bboxMinY = Mathf.Min(a.y, b.y) - crossEps;
        float bboxMaxY = Mathf.Max(a.y, b.y) + crossEps;

        for (int i = 0; i < vertices.Count; i++)
        {
            if (i == va || i == vb) continue;
            if (i == superV0 || i == superV1 || i == superV2) continue;

            Vector2 vi = vertices[i];
            // Early AABB rejection — skips the vast majority of vertices
            if (vi.x < bboxMinX || vi.x > bboxMaxX || vi.y < bboxMinY || vi.y > bboxMaxY)
                continue;

            Vector2 ap = vi - a;
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
    /// Upper = vertices to the LEFT of directed segment va→vb (positive cross product).
    /// Lower = vertices to the RIGHT (negative cross product).
    ///
    /// Collinear intermediate vertices are pre-split by FindVerticesOnSegment
    /// in InsertConstraint, so this method only handles the simple case where
    /// the sub-segment has no interior collinear vertices.
    ///
    /// Super-triangle vertices are allowed during the walk (matching standard CDT
    /// literature — Shewchuk, artem-ogre/CDT). They participate in cavity
    /// retriangulation and are filtered out later in BuildNavMesh.
    ///
    /// Edge progression uses a perturbed segment (Simulation of Simplicity,
    /// Edelsbrunner & Mücke 1990) to break exact collinearity with grid-aligned
    /// edges, ensuring EdgesIntersect always returns a definitive result.
    /// </summary>
    private bool WalkConstraint(int va, int vb, List<int> crossedTris,
        List<int> upper, List<int> lower)
    {
        Vector2 a = vertices[va];
        Vector2 b = vertices[vb];
        Vector2 ab = b - a;
        float abLenSq = ab.sqrMagnitude;
        float abLen = Mathf.Sqrt(abLenSq);
        if (abLen < 1e-6f) return false;

        upper.Add(va);
        lower.Add(va);

        // Perpendicular perturbation for the Simulation of Simplicity approach.
        float sosEps = GeometryConstants.SoSPerturbation(ab.x, ab.y);
        Vector2 perp = new Vector2(-ab.y, ab.x) * (sosEps / abLen);
        Vector2 ap = a + perp;
        Vector2 bp = b + perp;

        // --- Find starting triangle: the triangle incident to va whose
        //     opposite edge (p,q) is crossed by the constraint. ---
        // Use the SoS-perturbed base point to break collinearity for
        // axis-aligned constraints on grid-aligned vertices.
        int startTri = -1;
        int upperVert = -1, lowerVert = -1;

        if (vertToTris.TryGetValue(va, out var vaTrisWalk))
        {
            foreach (int ti in vaTrisWalk)
            {
                if (!triAlive[ti]) continue;
                int[] tri = triangles[ti];

                int vaIdx = -1;
                for (int k = 0; k < 3; k++)
                    if (tri[k] == va) { vaIdx = k; break; }
                if (vaIdx < 0) continue;

                int p = tri[(vaIdx + 1) % 3];
                int q = tri[(vaIdx + 2) % 3];

                float cp = CrossConstraint(ab, vertices[p] - ap);
                float cq = CrossConstraint(ab, vertices[q] - ap);

                if (cp > 1e-6f && cq < -1e-6f)
                {
                    startTri = ti; upperVert = p; lowerVert = q; break;
                }
                if (cp < -1e-6f && cq > 1e-6f)
                {
                    startTri = ti; upperVert = q; lowerVert = p; break;
                }
                if (Mathf.Abs(cp) <= 1e-6f && Mathf.Abs(cq) > 1e-6f)
                {
                    float tP = Vector2.Dot(vertices[p] - a, ab) / Mathf.Max(abLenSq, 1e-10f);
                    if (tP > 0.001f)
                    {
                        if (cq > 0f) { startTri = ti; upperVert = q; lowerVert = p; }
                        else { startTri = ti; upperVert = p; lowerVert = q; }
                        break;
                    }
                }
                if (Mathf.Abs(cq) <= 1e-6f && Mathf.Abs(cp) > 1e-6f)
                {
                    float tQ = Vector2.Dot(vertices[q] - a, ab) / Mathf.Max(abLenSq, 1e-10f);
                    if (tQ > 0.001f)
                    {
                        if (cp > 0f) { startTri = ti; upperVert = p; lowerVert = q; }
                        else { startTri = ti; upperVert = q; lowerVert = p; }
                        break;
                    }
                }
            }
        }

        if (startTri < 0)
        {
            // Primary collinear path: constraint is collinear with existing mesh edges.
            // Walk along adjacent vertices that lie on the segment va→vb.
            int splitVert = FindAdjacentVertexOnSegment(va, vb);
            if (splitVert >= 0)
            {
                constraintEdges.Add(EdgeKeyOrdered(va, splitVert));
                constraintsInserted++;
                // Recurse for the remaining sub-segment
                crossedTris.Clear();
                upper.Clear();
                lower.Clear();
                InsertConstraintSegment(splitVert, vb);
                return false; // signal caller to skip cavity retriangulation
            }
            return false;
        }

        crossedTris.Add(startTri);
        upper.Add(upperVert);
        lower.Add(lowerVert);

        int maxIter = triangles.Count;
        var visited = new HashSet<int>(crossedTris);

        for (int iter = 0; iter < maxIter; iter++)
        {
            int adjTri = FindAdjacentTriangleExcluding(visited, upperVert, lowerVert);
            if (adjTri < 0) return false;

            int opp = GetOppositeVertex(adjTri, upperVert, lowerVert);
            if (opp < 0) return false;

            crossedTris.Add(adjTri);
            visited.Add(adjTri);

            if (opp == vb)
            {
                upper.Add(vb);
                lower.Add(vb);
                return true;
            }

            // Edge progression: determine which edge the perturbed constraint
            // segment crosses next (upper or lower). The perturbation ensures
            // EdgesIntersect returns a definitive result for collinear edges.
            bool crossesUpper = EdgesIntersect(ap, bp, vertices[upperVert], vertices[opp]);
            bool crossesLower = EdgesIntersect(ap, bp, vertices[lowerVert], vertices[opp]);

            if (crossesLower && !crossesUpper)
            {
                upper.Add(opp);
                upperVert = opp;
            }
            else
            {
                lower.Add(opp);
                lowerVert = opp;
            }
        }

        return false;
    }

    private bool IsSuperVertex(int v)
    {
        return v == superV0 || v == superV1 || v == superV2;
    }

    /// <summary>
    /// Signed cross product ab × dp. Positive = dp is to the LEFT of ab (upper).
    /// Negative = dp is to the RIGHT of ab (lower).
    /// </summary>
    private static float CrossConstraint(Vector2 ab, Vector2 dp)
    {
        return ab.x * dp.y - ab.y * dp.x;
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
            AddTriangleIndexed(polygon[0], polygon[1], polygon[2]);
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

        AddTriangleIndexed(polygon[0], polygon[bestIdx], polygon[n - 1]);

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
            if (area < GeometryConstants.DegenerateAreaThreshold)
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
        mesh.CullIsolatedTriangles();
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
        if (!vertToTris.TryGetValue(va, out var set)) return false;
        foreach (int ti in set)
        {
            if (!triAlive[ti]) continue;
            if (HasEdge(triangles[ti], va, vb))
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
        if (!vertToTris.TryGetValue(ea, out var set)) return -1;
        foreach (int ti in set)
        {
            if (!triAlive[ti] || ti == triIdx) continue;
            if (HasEdge(triangles[ti], ea, eb))
                return ti;
        }
        return -1;
    }

    private int FindAdjacentTriangleExcluding(HashSet<int> excluded, int ea, int eb)
    {
        if (!vertToTris.TryGetValue(ea, out var set)) return -1;
        foreach (int ti in set)
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
    /// Check if a triangle is fully walkable by rasterizing it onto the grid
    /// and checking every covered cell center.
    /// </summary>
    private static bool IsTriangleFullyWalkable(Vector2 a, Vector2 b, Vector2 c,
        System.Func<Vector2, bool> isWalkable, float cellSize, Vector2 gridOrigin)
    {
        float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
        float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
        float minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
        float maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));

        int cellMinX = Mathf.FloorToInt((minX - gridOrigin.x) / cellSize) - 1;
        int cellMaxX = Mathf.CeilToInt((maxX - gridOrigin.x) / cellSize) + 1;
        int cellMinY = Mathf.FloorToInt((minY - gridOrigin.y) / cellSize) - 1;
        int cellMaxY = Mathf.CeilToInt((maxY - gridOrigin.y) / cellSize) + 1;

        for (int cx = cellMinX; cx <= cellMaxX; cx++)
        {
            for (int cy = cellMinY; cy <= cellMaxY; cy++)
            {
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
