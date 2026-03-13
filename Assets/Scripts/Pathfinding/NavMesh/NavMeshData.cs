using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Triangle face in the NavMesh. Each triangle represents a walkable region.
/// A* treats each triangle as a single graph node.
/// </summary>
public struct NavTriangle
{
    public int Id;
    public int V0, V1, V2;
    public int N0, N1, N2; // neighbor triangle IDs per edge; -1 = boundary
    public float W0, W1, W2; // portal widths per edge (max unit diameter that fits)
    public float CostMultiplier; // raised by cost stamps; default 1.0
    public bool IsWalkable;

    public int GetVertex(int i) => i switch { 0 => V0, 1 => V1, _ => V2 };
    public int GetNeighbor(int i) => i switch { 0 => N0, 1 => N1, _ => N2 };
    public float GetWidth(int i) => i switch { 0 => W0, 1 => W1, _ => W2 };

    public void SetNeighbor(int i, int val)
    {
        switch (i) { case 0: N0 = val; break; case 1: N1 = val; break; default: N2 = val; break; }
    }

    public void SetWidth(int i, float val)
    {
        switch (i) { case 0: W0 = val; break; case 1: W1 = val; break; default: W2 = val; break; }
    }

    /// <summary>
    /// Returns the edge index (0,1,2) that is shared with the given neighbor, or -1.
    /// Edge i connects vertex[i] and vertex[(i+1)%3], opposite to neighbor[i].
    /// Convention: edge 0 = (V0,V1), neighbor 0 = N0 (across edge 0).
    /// </summary>
    public int GetEdgeToNeighbor(int neighborId)
    {
        if (N0 == neighborId) return 0;
        if (N1 == neighborId) return 1;
        if (N2 == neighborId) return 2;
        return -1;
    }

    /// <summary>
    /// Returns the two vertex indices for edge i.
    /// Edge 0 = (V0,V1), Edge 1 = (V1,V2), Edge 2 = (V2,V0).
    /// </summary>
    public (int a, int b) GetEdgeVertices(int edgeIndex)
    {
        return edgeIndex switch
        {
            0 => (V0, V1),
            1 => (V1, V2),
            _ => (V2, V0),
        };
    }
}

/// <summary>
/// The full NavMesh: a set of triangles sharing a vertex pool.
/// Base mesh is built once at map load and never modified.
/// Active mesh is rebuilt from base whenever buildings change.
/// </summary>
public class NavMeshData
{
    public const int MaxVertices = 32000;
    public const int MaxTriangles = 64000;

    public Vector2[] Vertices;
    public int VertexCount;
    public NavTriangle[] Triangles;
    public int TriangleCount;

    // Spatial grid for fast triangle-at-position lookup
    private readonly Dictionary<long, List<int>> spatialGrid = new();
    private float spatialCellSize;
    private float inverseSpatialCellSize;

    public NavMeshData()
    {
        Vertices = new Vector2[MaxVertices];
        Triangles = new NavTriangle[MaxTriangles];
        VertexCount = 0;
        TriangleCount = 0;
    }

    public int AddVertex(Vector2 pos)
    {
        if (VertexCount >= MaxVertices)
        {
            Debug.LogError("[NavMeshData] Vertex limit reached!");
            return -1;
        }
        int id = VertexCount++;
        Vertices[id] = pos;
        return id;
    }

    public int AddTriangle(int v0, int v1, int v2, bool walkable = true)
    {
        if (TriangleCount >= MaxTriangles)
        {
            Debug.LogError("[NavMeshData] Triangle limit reached!");
            return -1;
        }
        int id = TriangleCount++;
        Triangles[id] = new NavTriangle
        {
            Id = id,
            V0 = v0, V1 = v1, V2 = v2,
            N0 = -1, N1 = -1, N2 = -1,
            W0 = 0f, W1 = 0f, W2 = 0f,
            CostMultiplier = 1f,
            IsWalkable = walkable
        };
        return id;
    }

    public Vector2 GetCentroid(int triId)
    {
        ref var t = ref Triangles[triId];
        return (Vertices[t.V0] + Vertices[t.V1] + Vertices[t.V2]) / 3f;
    }

    public float GetEdgeLength(int triId, int edgeIndex)
    {
        var (a, b) = Triangles[triId].GetEdgeVertices(edgeIndex);
        return Vector2.Distance(Vertices[a], Vertices[b]);
    }

    /// <summary>
    /// Deep copies this mesh (both vertex and triangle arrays).
    /// Used for creating active_navmesh from base_navmesh.
    /// </summary>
    public NavMeshData DeepCopy()
    {
        var copy = new NavMeshData();
        System.Array.Copy(Vertices, copy.Vertices, VertexCount);
        System.Array.Copy(Triangles, copy.Triangles, TriangleCount);
        copy.VertexCount = VertexCount;
        copy.TriangleCount = TriangleCount;

        if (spatialCellSize > 0f)
            copy.BuildSpatialGrid(spatialCellSize);

        return copy;
    }

    // ================================================================
    //  SPATIAL GRID for triangle-at-position lookup
    // ================================================================

    public void BuildSpatialGrid(float cellSize)
    {
        spatialCellSize = cellSize;
        inverseSpatialCellSize = 1f / cellSize;
        spatialGrid.Clear();

        for (int i = 0; i < TriangleCount; i++)
        {
            if (!Triangles[i].IsWalkable) continue;
            InsertTriangleIntoGrid(i);
        }
    }

    private void InsertTriangleIntoGrid(int triId)
    {
        ref var t = ref Triangles[triId];
        Vector2 v0 = Vertices[t.V0], v1 = Vertices[t.V1], v2 = Vertices[t.V2];

        float minX = Mathf.Min(v0.x, Mathf.Min(v1.x, v2.x));
        float maxX = Mathf.Max(v0.x, Mathf.Max(v1.x, v2.x));
        float minY = Mathf.Min(v0.y, Mathf.Min(v1.y, v2.y));
        float maxY = Mathf.Max(v0.y, Mathf.Max(v1.y, v2.y));

        int cx0 = Mathf.FloorToInt(minX * inverseSpatialCellSize);
        int cx1 = Mathf.FloorToInt(maxX * inverseSpatialCellSize);
        int cz0 = Mathf.FloorToInt(minY * inverseSpatialCellSize);
        int cz1 = Mathf.FloorToInt(maxY * inverseSpatialCellSize);

        for (int cx = cx0; cx <= cx1; cx++)
        {
            for (int cz = cz0; cz <= cz1; cz++)
            {
                long key = ((long)cx << 32) | (uint)cz;
                if (!spatialGrid.TryGetValue(key, out var list))
                {
                    list = new List<int>(4);
                    spatialGrid[key] = list;
                }
                list.Add(triId);
            }
        }
    }

    /// <summary>
    /// Find which walkable triangle contains the given position (in XZ plane).
    /// Returns triangle ID, or -1 if not found.
    /// </summary>
    public int FindTriangleAtPosition(Vector2 pos)
    {
        if (spatialCellSize <= 0f)
        {
            if (GameDebug.Pathfinding)
                Debug.LogWarning($"[NavMesh] FindTriangleAtPosition called before BuildSpatialGrid! pos={pos}");
            return FindTriangleBrute(pos);
        }

        int cx = Mathf.FloorToInt(pos.x * inverseSpatialCellSize);
        int cz = Mathf.FloorToInt(pos.y * inverseSpatialCellSize);
        long key = ((long)cx << 32) | (uint)cz;

        if (!spatialGrid.TryGetValue(key, out var candidates))
        {
            int brute = FindTriangleBrute(pos);
            if (brute >= 0 && GameDebug.Pathfinding)
            {
                float dist = (GetCentroid(brute) - pos).magnitude;
                if (dist > spatialCellSize * 2f)
                    Debug.LogWarning($"[NavMesh] Position ({pos.x:F1},{pos.y:F1}) far from NavMesh, nearest tri {brute} at dist={dist:F1}");
            }
            return brute;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            int triId = candidates[i];
            if (Triangles[triId].IsWalkable && PointInTriangle(pos, triId))
                return triId;
        }

        return FindTriangleBrute(pos);
    }

    private int FindTriangleBrute(Vector2 pos)
    {
        int bestTri = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < TriangleCount; i++)
        {
            if (!Triangles[i].IsWalkable) continue;
            if (PointInTriangle(pos, i)) return i;

            float d = (GetCentroid(i) - pos).sqrMagnitude;
            if (d < bestDist) { bestDist = d; bestTri = i; }
        }
        return bestTri;
    }

    public bool PointInTriangle(Vector2 p, int triId)
    {
        ref var t = ref Triangles[triId];
        Vector2 a = Vertices[t.V0], b = Vertices[t.V1], c = Vertices[t.V2];
        return PointInTriangle(p, a, b, c);
    }

    public static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross2D(p - a, b - a);
        float d2 = Cross2D(p - b, c - b);
        float d3 = Cross2D(p - c, a - c);
        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
        return !(hasNeg && hasPos);
    }

    public static float Cross2D(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    /// <summary>
    /// Compute passage widths per vertex using the Demyen 2006 formula.
    /// W[v] = min(len_edge_a, len_edge_b, altitude_from_v) where edges a,b share vertex v.
    /// This is the maximum unit diameter that can traverse the triangle between
    /// the two edges adjacent to vertex v.
    /// </summary>
    public void ComputeAllWidths()
    {
        for (int i = 0; i < TriangleCount; i++)
        {
            if (!Triangles[i].IsWalkable) continue;

            Vector2 vA = Vertices[Triangles[i].V0];
            Vector2 vB = Vertices[Triangles[i].V1];
            Vector2 vC = Vertices[Triangles[i].V2];

            float lenE0 = Vector2.Distance(vA, vB); // edge 0: V0-V1
            float lenE1 = Vector2.Distance(vB, vC); // edge 1: V1-V2
            float lenE2 = Vector2.Distance(vC, vA); // edge 2: V2-V0

            float area2 = Mathf.Abs(Cross2D(vB - vA, vC - vA)); // 2 * triangle area

            // W0: passage at V0, between edge 0 (V0-V1) and edge 2 (V2-V0)
            // Altitude from V0 to opposite edge 1 (V1-V2)
            float altV0 = lenE1 > 0.0001f ? area2 / lenE1 : 0f;
            Triangles[i].SetWidth(0, Mathf.Min(lenE0, Mathf.Min(lenE2, altV0)));

            // W1: passage at V1, between edge 0 (V0-V1) and edge 1 (V1-V2)
            // Altitude from V1 to opposite edge 2 (V2-V0)
            float altV1 = lenE2 > 0.0001f ? area2 / lenE2 : 0f;
            Triangles[i].SetWidth(1, Mathf.Min(lenE0, Mathf.Min(lenE1, altV1)));

            // W2: passage at V2, between edge 1 (V1-V2) and edge 2 (V2-V0)
            // Altitude from V2 to opposite edge 0 (V0-V1)
            float altV2 = lenE0 > 0.0001f ? area2 / lenE0 : 0f;
            Triangles[i].SetWidth(2, Mathf.Min(lenE1, Mathf.Min(lenE2, altV2)));
        }
    }

    /// <summary>
    /// Returns the vertex index shared by two edges of a triangle.
    /// Edge i connects V_i and V_(i+1)%3.
    /// </summary>
    public static int SharedVertexOfEdges(int edgeA, int edgeB)
    {
        int a0 = edgeA, a1 = (edgeA + 1) % 3;
        int b0 = edgeB, b1 = (edgeB + 1) % 3;
        if (a0 == b0 || a0 == b1) return a0;
        return a1;
    }

    /// <summary>
    /// Build adjacency: for each triangle, find its neighbors across each edge.
    /// Two triangles are neighbors if they share exactly 2 vertices.
    /// </summary>
    public void BuildAdjacency()
    {
        var edgeMap = new Dictionary<long, (int triId, int edgeIdx)>();

        for (int i = 0; i < TriangleCount; i++)
        {
            if (!Triangles[i].IsWalkable) continue;
            Triangles[i].SetNeighbor(0, -1);
            Triangles[i].SetNeighbor(1, -1);
            Triangles[i].SetNeighbor(2, -1);

            for (int e = 0; e < 3; e++)
            {
                var (a, b) = Triangles[i].GetEdgeVertices(e);
                long edgeKey = EdgeKey(a, b);
                if (edgeMap.TryGetValue(edgeKey, out var other))
                {
                    Triangles[i].SetNeighbor(e, other.triId);
                    Triangles[other.triId].SetNeighbor(other.edgeIdx, i);
                }
                else
                {
                    edgeMap[edgeKey] = (i, e);
                }
            }
        }
    }

    private static long EdgeKey(int a, int b)
    {
        int lo = Mathf.Min(a, b);
        int hi = Mathf.Max(a, b);
        return ((long)lo << 32) | (uint)hi;
    }

    /// <summary>
    /// Validate the mesh integrity. Logs warnings for any structural problems.
    /// Returns true if the mesh is healthy.
    /// </summary>
    public bool ValidateMesh()
    {
        int errors = 0;
        int walkable = 0, unwalkable = 0, isolated = 0, zeroWidth = 0;
        int brokenAdjacency = 0;
        float minWidth = float.MaxValue, maxWidth = 0f;
        float minArea = float.MaxValue;

        for (int i = 0; i < TriangleCount; i++)
        {
            ref var t = ref Triangles[i];
            if (!t.IsWalkable) { unwalkable++; continue; }
            walkable++;

            // Check vertex indices
            if (t.V0 < 0 || t.V0 >= VertexCount || t.V1 < 0 || t.V1 >= VertexCount || t.V2 < 0 || t.V2 >= VertexCount)
            {
                Debug.LogError($"[NavMesh] Triangle {i} has out-of-range vertex indices: V=({t.V0},{t.V1},{t.V2}) vCount={VertexCount}");
                errors++;
                continue;
            }

            // Check area
            Vector2 vA = Vertices[t.V0], vB = Vertices[t.V1], vC = Vertices[t.V2];
            float area = Mathf.Abs(Cross2D(vB - vA, vC - vA)) * 0.5f;
            if (area < minArea) minArea = area;
            if (area < 0.001f)
            {
                Debug.LogWarning($"[NavMesh] Degenerate triangle {i}: area={area:F6}");
                errors++;
            }

            // Check neighbors (each neighbor should reference back)
            bool hasNeighbor = false;
            for (int e = 0; e < 3; e++)
            {
                int n = t.GetNeighbor(e);
                if (n >= 0)
                {
                    hasNeighbor = true;
                    if (n >= TriangleCount)
                    {
                        Debug.LogError($"[NavMesh] Triangle {i} edge {e}: neighbor {n} out of range");
                        errors++;
                    }
                    else if (Triangles[n].GetEdgeToNeighbor(i) < 0)
                    {
                        brokenAdjacency++;
                    }
                }

                float w = t.GetWidth(e);
                if (w < minWidth) minWidth = w;
                if (w > maxWidth) maxWidth = w;
                if (w <= 0f) zeroWidth++;
            }
            if (!hasNeighbor) isolated++;
        }

        bool healthy = errors == 0 && isolated == 0;

        if (GameDebug.Pathfinding)
        {
            Debug.Log($"[NavMesh] Validate: {VertexCount} verts, {TriangleCount} tris " +
                $"(walkable={walkable}, unwalkable={unwalkable})\n" +
                $"  errors={errors}, isolated={isolated}, brokenAdj={brokenAdjacency}, zeroWidth={zeroWidth}\n" +
                $"  width range: [{minWidth:F2}, {maxWidth:F2}], minArea={minArea:F4}\n" +
                $"  health: {(healthy ? "OK" : "PROBLEMS FOUND")}");
        }

        if (!healthy)
        {
            if (isolated > 0) Debug.LogWarning($"[NavMesh] {isolated} walkable triangles have NO neighbors — units cannot path through them");
            if (errors > 0) Debug.LogError($"[NavMesh] {errors} structural errors found in mesh");
        }

        return healthy;
    }
}
