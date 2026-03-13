using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Builds and rebuilds the NavMesh from the GridSystem.
/// Follows the SC2 pattern: base NavMesh at map load, rebuilt from base on obstacle changes.
/// Extracts obstacle outlines from the grid, then runs CDT.
/// </summary>
public class NavMeshBuilder
{
    private NavMeshData baseNavMesh;
    private NavMeshData activeNavMesh;
    private IGrid grid;
    private float gridY;

    public NavMeshData ActiveNavMesh => activeNavMesh;
    public NavMeshData BaseNavMesh => baseNavMesh;

    /// <summary>
    /// Build the base NavMesh from the initial grid state (terrain only, no buildings).
    /// Called once at map load.
    /// </summary>
    public void BuildBase(IGrid gridSystem)
    {
        grid = gridSystem;
        gridY = grid.GridOrigin.y;

        baseNavMesh = BuildFromGrid();
        activeNavMesh = baseNavMesh.DeepCopy();

        if (GameDebug.Pathfinding)
            Debug.Log($"[NavMeshBuilder] Base built: {baseNavMesh.TriangleCount} triangles, {baseNavMesh.VertexCount} vertices");
    }

    /// <summary>
    /// Full rebuild from current grid state. The grid is the single source of truth
    /// for walkability — buildings already update grid cells before this fires.
    /// Called when a building is placed or destroyed.
    /// </summary>
    public void Rebuild()
    {
        if (grid == null) return;

        activeNavMesh = BuildFromGrid();

        if (GameDebug.Pathfinding)
            Debug.Log($"[NavMeshBuilder] Rebuilt: {activeNavMesh.TriangleCount} tris, {activeNavMesh.VertexCount} verts");
    }

    // ================================================================
    //  CORE BUILD
    // ================================================================

    private NavMeshData BuildFromGrid()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var cdt = new CDTriangulator();
        float cs = grid.CellSize;
        Vector3 origin = grid.GridOrigin;
        int w = grid.Width, h = grid.Height;

        float mapMinX = origin.x - cs * 0.5f;
        float mapMinZ = origin.z - cs * 0.5f;
        float mapMaxX = origin.x + (w - 0.5f) * cs;
        float mapMaxZ = origin.z + (h - 0.5f) * cs;

        cdt.AddVertex(new Vector2(mapMinX, mapMinZ));
        cdt.AddVertex(new Vector2(mapMaxX, mapMinZ));
        cdt.AddVertex(new Vector2(mapMaxX, mapMaxZ));
        cdt.AddVertex(new Vector2(mapMinX, mapMaxZ));

        var obstacleRects = ExtractObstacleRects();

        if (GameDebug.Pathfinding)
            Debug.Log($"[NavMeshBuilder] Grid {w}x{h}, cellSize={cs:F2}, bounds=({mapMinX:F1},{mapMinZ:F1})-({mapMaxX:F1},{mapMaxZ:F1}), obstacles={obstacleRects.Count}");

        // Collect raw constraint line segments (world-space endpoints).
        // Adjacent rects can share boundary edges that overlap, which causes
        // CDT constraint insertion to fail. We merge collinear overlapping
        // segments before inserting them.
        var rawSegments = new List<(Vector2 a, Vector2 b)>();
        foreach (var rect in obstacleRects)
        {
            float x0 = rect.xMin, z0 = rect.yMin;
            float x1 = rect.xMax, z1 = rect.yMax;

            cdt.AddVertex(new Vector2(x0, z0));
            cdt.AddVertex(new Vector2(x1, z0));
            cdt.AddVertex(new Vector2(x1, z1));
            cdt.AddVertex(new Vector2(x0, z1));

            rawSegments.Add((new Vector2(x0, z0), new Vector2(x1, z0))); // bottom
            rawSegments.Add((new Vector2(x1, z0), new Vector2(x1, z1))); // right
            rawSegments.Add((new Vector2(x1, z1), new Vector2(x0, z1))); // top
            rawSegments.Add((new Vector2(x0, z1), new Vector2(x0, z0))); // left
        }

        var mergedSegments = MergeCollinearSegments(rawSegments);

        // Split long constraint edges into shorter segments. The CDT edge-flip
        // algorithm can fail to converge for edges spanning many triangles.
        float maxEdgeLen = cs * 3f;
        var constraintPairs = new List<(int, int)>(mergedSegments.Count * 2);
        foreach (var (a, b) in mergedSegments)
        {
            float len = Vector2.Distance(a, b);
            if (len <= maxEdgeLen)
            {
                int va = cdt.AddVertex(a);
                int vb = cdt.AddVertex(b);
                if (va != vb) constraintPairs.Add((va, vb));
            }
            else
            {
                int splits = Mathf.CeilToInt(len / maxEdgeLen);
                int prev = cdt.AddVertex(a);
                for (int s = 1; s <= splits; s++)
                {
                    float t = s / (float)splits;
                    Vector2 p = Vector2.Lerp(a, b, t);
                    int cur = cdt.AddVertex(p);
                    if (prev != cur) constraintPairs.Add((prev, cur));
                    prev = cur;
                }
            }
        }

        AddSamplingPoints(cdt, mapMinX, mapMinZ, mapMaxX, mapMaxZ, obstacleRects);

        if (GameDebug.Pathfinding)
            Debug.Log($"[NavMeshBuilder] CDT input: {cdt.VertexCount} vertices, {constraintPairs.Count} constraint edges (merged from {rawSegments.Count} raw)");

        cdt.Triangulate();

        foreach (var (a, b) in constraintPairs)
            cdt.InsertConstraint(a, b);

        Vector2 gridOrigin2D = new Vector2(origin.x, origin.z);
        var mesh = cdt.BuildNavMesh(pos => IsPositionWalkable(pos), cs, gridOrigin2D);

        // Debug-only post-build validation: verify walkable triangles vs grid
        if (GameDebug.Pathfinding)
        {
            int leakyCount = 0;
            for (int ti = 0; ti < mesh.TriangleCount; ti++)
            {
                if (!mesh.Triangles[ti].IsWalkable) continue;
                var tri = mesh.Triangles[ti];
                Vector2 va = mesh.Vertices[tri.V0];
                Vector2 vb = mesh.Vertices[tri.V1];
                Vector2 vc = mesh.Vertices[tri.V2];

                float bMinX = Mathf.Min(va.x, Mathf.Min(vb.x, vc.x));
                float bMaxX = Mathf.Max(va.x, Mathf.Max(vb.x, vc.x));
                float bMinY = Mathf.Min(va.y, Mathf.Min(vb.y, vc.y));
                float bMaxY = Mathf.Max(va.y, Mathf.Max(vb.y, vc.y));

                int cMinX = Mathf.FloorToInt((bMinX - origin.x) / cs) - 1;
                int cMaxX = Mathf.CeilToInt((bMaxX - origin.x) / cs) + 1;
                int cMinY = Mathf.FloorToInt((bMinY - origin.z) / cs) - 1;
                int cMaxY = Mathf.CeilToInt((bMaxY - origin.z) / cs) + 1;

                for (int cx = cMinX; cx <= cMaxX && leakyCount < 20; cx++)
                {
                    for (int cy = cMinY; cy <= cMaxY && leakyCount < 20; cy++)
                    {
                        float wx = origin.x + cx * cs;
                        float wy = origin.z + cy * cs;
                        Vector2 cellCenter = new Vector2(wx, wy);
                        if (!NavMeshData.PointInTriangle(cellCenter, va, vb, vc)) continue;
                        if (!IsPositionWalkable(cellCenter))
                        {
                            Vector2Int gridCell = grid.WorldToCell(new Vector3(wx, 0, wy));
                            Debug.LogWarning($"[NavMeshBuilder] LEAKY TRI #{ti}: walkable triangle contains unwalkable cell ({cx},{cy}) gridCell=({gridCell.x},{gridCell.y}) worldPos=({wx:F1},{wy:F1})");
                            leakyCount++;
                            goto nextTri;
                        }
                    }
                }
                nextTri:;
            }
            if (leakyCount > 0)
                Debug.LogWarning($"[NavMeshBuilder] Post-build validation: {leakyCount} leaky walkable triangles");
            else
                Debug.Log($"[NavMeshBuilder] Post-build validation PASSED: no leaky triangles");
        }

        mesh.BuildSpatialGrid(grid.CellSize * 4f);

        sw.Stop();
        if (GameDebug.Pathfinding)
            Debug.Log($"[NavMeshBuilder] Build completed in {sw.ElapsedMilliseconds}ms, tris={mesh.TriangleCount}");

        return mesh;
    }

    /// <summary>
    /// Extract axis-aligned obstacle rectangles from the grid.
    /// Uses a greedy rect-merge to reduce vertex count.
    /// </summary>
    private List<Rect> ExtractObstacleRects()
    {
        var rects = new List<Rect>();
        int w = grid.Width, h = grid.Height;
        var visited = new bool[w, h];
        float cs = grid.CellSize;
        Vector3 origin = grid.GridOrigin;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (visited[x, y]) continue;
                if (grid.IsWalkable(new Vector2Int(x, y))) continue;

                // Greedy horizontal extend
                int x1 = x;
                while (x1 + 1 < w && !visited[x1 + 1, y] &&
                       !grid.IsWalkable(new Vector2Int(x1 + 1, y)))
                    x1++;

                // Greedy vertical extend
                int y1 = y;
                bool canExtend = true;
                while (canExtend && y1 + 1 < h)
                {
                    for (int xi = x; xi <= x1; xi++)
                    {
                        if (visited[xi, y1 + 1] || grid.IsWalkable(new Vector2Int(xi, y1 + 1)))
                        {
                            canExtend = false;
                            break;
                        }
                    }
                    if (canExtend) y1++;
                }

                // Mark visited
                for (int yi = y; yi <= y1; yi++)
                    for (int xi = x; xi <= x1; xi++)
                        visited[xi, yi] = true;

                float worldX0 = origin.x + (x - 0.5f) * cs;
                float worldZ0 = origin.z + (y - 0.5f) * cs;
                float worldX1 = origin.x + (x1 + 0.5f) * cs;
                float worldZ1 = origin.z + (y1 + 0.5f) * cs;

                rects.Add(new Rect(worldX0, worldZ0, worldX1 - worldX0, worldZ1 - worldZ0));
            }
        }

        return rects;
    }

    /// <summary>
    /// Add sampling points in open walkable areas to create better-quality triangles.
    /// Without these, large open areas become huge degenerate triangles.
    /// </summary>
    private void AddSamplingPoints(CDTriangulator cdt, float minX, float minZ, float maxX, float maxZ, List<Rect> obstacles)
    {
        float spacing = grid.CellSize * 6f;
        for (float x = minX + spacing; x < maxX; x += spacing)
        {
            for (float z = minZ + spacing; z < maxZ; z += spacing)
            {
                Vector2 p = new Vector2(x, z);
                if (IsPositionWalkable(p) && !IsInsideAnyRect(p, obstacles))
                    cdt.AddVertex(p);
            }
        }
    }

    private bool IsInsideAnyRect(Vector2 p, List<Rect> rects)
    {
        foreach (var r in rects)
        {
            if (p.x >= r.xMin && p.x <= r.xMax && p.y >= r.yMin && p.y <= r.yMax)
                return true;
        }
        return false;
    }

    private bool IsPositionWalkable(Vector2 pos)
    {
        Vector3 worldPos = new Vector3(pos.x, gridY, pos.y);
        Vector2Int cell = grid.WorldToCell(worldPos);
        if (!grid.IsInBounds(cell)) return false;
        return grid.IsWalkable(cell);
    }

    /// <summary>
    /// Merge collinear overlapping axis-aligned segments into non-overlapping
    /// sub-segments. Groups segments by their axis line (horizontal or vertical),
    /// sorts by the varying coordinate, and produces gap-free constraint edges.
    /// </summary>
    private static List<(Vector2 a, Vector2 b)> MergeCollinearSegments(
        List<(Vector2 a, Vector2 b)> segments)
    {
        const float eps = 0.01f;

        // Key: rounded fixed coordinate * 10000 + axis flag (0=horizontal, 1=vertical)
        var groups = new Dictionary<long, List<(float min, float max)>>();

        foreach (var (a, b) in segments)
        {
            bool isHorizontal = Mathf.Abs(a.y - b.y) < eps;
            bool isVertical = Mathf.Abs(a.x - b.x) < eps;

            if (isHorizontal)
            {
                float fixedY = (a.y + b.y) * 0.5f;
                long key = (long)Mathf.RoundToInt(fixedY * 1000f) * 2;
                float lo = Mathf.Min(a.x, b.x);
                float hi = Mathf.Max(a.x, b.x);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<(float, float)>();
                    groups[key] = list;
                }
                list.Add((lo, hi));
            }
            else if (isVertical)
            {
                float fixedX = (a.x + b.x) * 0.5f;
                long key = (long)Mathf.RoundToInt(fixedX * 1000f) * 2 + 1;
                float lo = Mathf.Min(a.y, b.y);
                float hi = Mathf.Max(a.y, b.y);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<(float, float)>();
                    groups[key] = list;
                }
                list.Add((lo, hi));
            }
            else
            {
                // Diagonal edge — shouldn't happen with axis-aligned rects but keep as-is
            }
        }

        var result = new List<(Vector2, Vector2)>();
        foreach (var kvp in groups)
        {
            long key = kvp.Key;
            var spans = kvp.Value;
            bool isVertical = (key & 1) == 1;
            float fixedCoord;
            if (isVertical)
                fixedCoord = (key / 2) / 1000f;
            else
                fixedCoord = (key / 2) / 1000f;

            spans.Sort((a, b) => a.min.CompareTo(b.min));

            // Merge overlapping/touching spans
            var merged = new List<(float min, float max)>();
            float curMin = spans[0].min, curMax = spans[0].max;
            for (int i = 1; i < spans.Count; i++)
            {
                if (spans[i].min <= curMax + eps)
                {
                    curMax = Mathf.Max(curMax, spans[i].max);
                }
                else
                {
                    merged.Add((curMin, curMax));
                    curMin = spans[i].min;
                    curMax = spans[i].max;
                }
            }
            merged.Add((curMin, curMax));

            // Emit sub-segments. Collect all original breakpoints within each merged
            // span and create constraint edges between consecutive breakpoints.
            foreach (var (mMin, mMax) in merged)
            {
                var breakpoints = new SortedSet<float> { mMin, mMax };
                foreach (var (lo, hi) in spans)
                {
                    if (lo >= mMin - eps && lo <= mMax + eps) breakpoints.Add(lo);
                    if (hi >= mMin - eps && hi <= mMax + eps) breakpoints.Add(hi);
                }

                float prev = float.NegativeInfinity;
                foreach (float bp in breakpoints)
                {
                    if (prev > float.NegativeInfinity && Mathf.Abs(bp - prev) > eps)
                    {
                        if (isVertical)
                            result.Add((new Vector2(fixedCoord, prev), new Vector2(fixedCoord, bp)));
                        else
                            result.Add((new Vector2(prev, fixedCoord), new Vector2(bp, fixedCoord)));
                    }
                    prev = bp;
                }
            }
        }

        return result;
    }

    // ================================================================
    //  WORLD <-> NAVMESH COORDINATE CONVERSIONS
    // ================================================================

    public Vector2 WorldToNavMesh(Vector3 worldPos)
    {
        return new Vector2(worldPos.x, worldPos.z);
    }

    public Vector3 NavMeshToWorld(Vector2 navPos)
    {
        return new Vector3(navPos.x, gridY, navPos.y);
    }

    /// <summary>
    /// Find the nearest position on the walkable NavMesh to the given world position.
    /// </summary>
    public Vector3 FindNearestWalkablePosition(Vector3 worldPos)
    {
        if (activeNavMesh == null) return worldPos;

        Vector2 nmPos = WorldToNavMesh(worldPos);
        int tri = activeNavMesh.FindTriangleAtPosition(nmPos);
        if (tri >= 0 && activeNavMesh.Triangles[tri].IsWalkable)
            return worldPos;

        float bestDist = float.MaxValue;
        Vector2 bestPos = nmPos;
        for (int i = 0; i < activeNavMesh.TriangleCount; i++)
        {
            if (!activeNavMesh.Triangles[i].IsWalkable) continue;
            Vector2 centroid = activeNavMesh.GetCentroid(i);
            float d = (centroid - nmPos).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                bestPos = centroid;
            }
        }
        return NavMeshToWorld(bestPos);
    }
}
