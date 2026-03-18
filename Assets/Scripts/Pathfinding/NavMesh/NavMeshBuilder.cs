using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Builds and rebuilds the NavMesh from the GridSystem.
/// Follows the SC2 pattern: base NavMesh at map load, rebuilt from base on obstacle changes.
/// Extracts obstacle outlines from the grid, then runs CDT.
///
/// Rebuilds run on a background thread via RebuildAsync() to avoid freezing the game.
/// A GridSnapshot captures walkability state so the background thread never touches
/// the live grid. Path requests continue using the old NavMesh until the new one is ready.
/// </summary>
public class NavMeshBuilder
{
    private NavMeshData activeNavMesh;
    private IGrid grid;
    private float gridY;

    /// <summary>
    /// Actual building bounds used for precise CDT constraint edges.
    /// Keyed by building instance ID. Updated by PathfindingManager when
    /// buildings are placed/destroyed.
    /// </summary>
    private readonly Dictionary<int, Rect> buildingBounds = new();

    /// <summary>
    /// Buildings placed since the last NavMesh rebuild STARTED.
    /// Only these need path-crossing validation because the in-flight build
    /// already incorporates all buildings registered before it started.
    /// When RebuildAsync() starts, all currently pending buildings are moved
    /// to inFlightBuildingIds (incorporated by the build). Any buildings
    /// placed after that remain in pendingBuildingIds for the next cycle.
    /// </summary>
    private readonly HashSet<int> pendingBuildingIds = new();

    /// <summary>
    /// Buildings incorporated by the currently in-flight async rebuild.
    /// Cleared when the rebuild result is applied (they're now in the active mesh).
    /// </summary>
    private readonly HashSet<int> inFlightBuildingIds = new();

    // Thread-local build context — set before BuildFromGrid(), used by all internal
    // build methods. [ThreadStatic] ensures main thread and background thread builds
    // never interfere with each other.
    [ThreadStatic] private static IGrid t_grid;
    [ThreadStatic] private static Dictionary<int, Rect> t_bounds;
    [ThreadStatic] private static float t_gridY;

    private Task<NavMeshData> asyncRebuildTask;
    private CancellationTokenSource asyncCts;

    public NavMeshData ActiveNavMesh => activeNavMesh;
    public bool IsRebuilding => asyncRebuildTask != null && !asyncRebuildTask.IsCompleted;

    /// <summary>
    /// Register a building's actual world footprint for precise CDT constraints.
    /// Called when a building is placed.
    /// </summary>
    public void RegisterBuilding(int instanceId, Bounds worldBounds)
    {
        float x0 = worldBounds.min.x;
        float z0 = worldBounds.min.z;
        float x1 = worldBounds.max.x;
        float z1 = worldBounds.max.z;
        buildingBounds[instanceId] = new Rect(x0, z0, x1 - x0, z1 - z0);
        pendingBuildingIds.Add(instanceId);
    }

    /// <summary>
    /// Unregister a building when destroyed.
    /// </summary>
    public void UnregisterBuilding(int instanceId)
    {
        buildingBounds.Remove(instanceId);
        pendingBuildingIds.Remove(instanceId);
        inFlightBuildingIds.Remove(instanceId);
    }

    /// <summary>
    /// Build the NavMesh from the initial grid state (terrain only, no buildings).
    /// Called once at map load. Runs synchronously since no gameplay is active yet.
    /// </summary>
    public void BuildBase(IGrid gridSystem)
    {
        grid = gridSystem;
        gridY = grid.GridOrigin.y;

        t_grid = grid;
        t_bounds = buildingBounds;
        t_gridY = gridY;

        activeNavMesh = BuildFromGrid();
        pendingBuildingIds.Clear();
        inFlightBuildingIds.Clear();

        if (GameDebug.Pathfinding)
            Debug.Log($"[NavMeshBuilder] Base built: {activeNavMesh.TriangleCount} triangles, {activeNavMesh.VertexCount} vertices");
    }

    /// <summary>
    /// Full synchronous rebuild. Only used as a fallback; prefer RebuildAsync().
    /// </summary>
    public void Rebuild()
    {
        if (grid == null) return;

        t_grid = grid;
        t_bounds = buildingBounds;
        t_gridY = gridY;

        activeNavMesh = BuildFromGrid();
        pendingBuildingIds.Clear();
        inFlightBuildingIds.Clear();

        if (GameDebug.Pathfinding)
            Debug.Log($"[NavMeshBuilder] Rebuilt: {activeNavMesh.TriangleCount} tris, {activeNavMesh.VertexCount} verts");
    }

    /// <summary>
    /// Start an asynchronous NavMesh rebuild on a background thread.
    /// Snapshots the grid walkability state so the build is fully thread-safe.
    /// The old NavMesh remains active for path requests until the new one is ready.
    /// Call TryApplyAsyncResult() each frame to check for and apply the result.
    /// </summary>
    public void RebuildAsync()
    {
        if (grid == null) return;

        asyncCts?.Cancel();
        asyncCts = new CancellationTokenSource();
        var token = asyncCts.Token;

        var snapshotGrid = new GridSnapshot(grid);
        var snapshotBounds = new Dictionary<int, Rect>(buildingBounds);
        float snapshotY = gridY;

        // All currently pending buildings are being incorporated into this build.
        // Move them to inFlightBuildingIds so they stop blocking path requests.
        // Any buildings placed AFTER this point will be added to pendingBuildingIds
        // by RegisterBuilding() and will correctly block until the next rebuild.
        inFlightBuildingIds.UnionWith(pendingBuildingIds);
        pendingBuildingIds.Clear();

        asyncRebuildTask = Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();

            t_grid = snapshotGrid;
            t_bounds = snapshotBounds;
            t_gridY = snapshotY;

            return BuildFromGrid();
        }, token);

        if (GameDebug.Pathfinding)
            Debug.Log($"[NavMeshBuilder] Async rebuild started on background thread (incorporated {inFlightBuildingIds.Count} buildings)");
    }

    /// <summary>
    /// Check if the async rebuild has completed. If so, swap in the new NavMesh
    /// and return true. Returns false if still building or if the build failed.
    /// </summary>
    public bool TryApplyAsyncResult()
    {
        if (asyncRebuildTask == null || !asyncRebuildTask.IsCompleted)
            return false;

        if (asyncRebuildTask.IsCompletedSuccessfully)
        {
            activeNavMesh = asyncRebuildTask.Result;
            asyncRebuildTask = null;
            inFlightBuildingIds.Clear();

            if (GameDebug.Pathfinding)
                Debug.Log($"[NavMeshBuilder] Async rebuild applied: {activeNavMesh.TriangleCount} tris, {activeNavMesh.VertexCount} verts" +
                    $" (pending={pendingBuildingIds.Count} buildings still awaiting next rebuild)");

            return true;
        }

        // Faulted or canceled: move in-flight buildings back to pending for the next rebuild
        if (asyncRebuildTask.IsFaulted)
        {
            var ex = asyncRebuildTask.Exception?.InnerException ?? asyncRebuildTask.Exception;
            Debug.LogError($"[NavMeshBuilder] Async rebuild FAILED: {ex?.Message}\n{ex?.StackTrace}");
        }
        else if (asyncRebuildTask.IsCanceled)
        {
            if (GameDebug.Pathfinding)
                Debug.Log("[NavMeshBuilder] Async rebuild canceled (superseded by newer rebuild)");
        }

        pendingBuildingIds.UnionWith(inFlightBuildingIds);
        inFlightBuildingIds.Clear();
        asyncRebuildTask = null;
        return false;
    }

    // ================================================================
    //  CORE BUILD (runs on main thread or background thread)
    //  All methods below use t_grid / t_bounds / t_gridY context fields.
    // ================================================================

    private NavMeshData BuildFromGrid()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long tSetup, tTriangulate, tConstraints, tBuildMesh;

        var cdt = new CDTriangulator();
        float cs = t_grid.CellSize;
        Vector3 origin = t_grid.GridOrigin;
        int w = t_grid.Width, h = t_grid.Height;

        float mapMinX = origin.x - cs * 0.5f;
        float mapMinZ = origin.z - cs * 0.5f;
        float mapMaxX = origin.x + (w - 0.5f) * cs;
        float mapMaxZ = origin.z + (h - 0.5f) * cs;

        cdt.AddVertex(new Vector2(mapMinX, mapMinZ));
        cdt.AddVertex(new Vector2(mapMaxX, mapMinZ));
        cdt.AddVertex(new Vector2(mapMaxX, mapMaxZ));
        cdt.AddVertex(new Vector2(mapMinX, mapMaxZ));

        var obstacleRects = t_bounds.Count > 0
            ? ExtractObstacleRectsFromBuildings()
            : ExtractObstacleRects();

        // Build spatial grid early so AddSteinerPoints also gets O(1) walkability checks
        BuildWalkabilitySpatialGrid(cs);

        AddSteinerPoints(cdt, mapMinX, mapMinZ, mapMaxX, mapMaxZ, obstacleRects);

        if (GameDebug.Pathfinding)
            Debug.Log($"[NavMeshBuilder] Grid {w}x{h}, cellSize={cs:F2}, bounds=({mapMinX:F1},{mapMinZ:F1})-({mapMaxX:F1},{mapMaxZ:F1}), obstacles={obstacleRects.Count}");

        var rawSegments = new List<(Vector2 a, Vector2 b)>();
        foreach (var rect in obstacleRects)
        {
            float x0 = rect.xMin, z0 = rect.yMin;
            float x1 = rect.xMax, z1 = rect.yMax;

            cdt.AddVertex(new Vector2(x0, z0));
            cdt.AddVertex(new Vector2(x1, z0));
            cdt.AddVertex(new Vector2(x1, z1));
            cdt.AddVertex(new Vector2(x0, z1));

            rawSegments.Add((new Vector2(x0, z0), new Vector2(x1, z0)));
            rawSegments.Add((new Vector2(x1, z0), new Vector2(x1, z1)));
            rawSegments.Add((new Vector2(x1, z1), new Vector2(x0, z1)));
            rawSegments.Add((new Vector2(x0, z1), new Vector2(x0, z0)));
        }

        var mergedSegments = MergeCollinearSegments(rawSegments);

        float maxEdgeLen = cs * GeometryConstants.MaxConstraintEdgeCells;
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

        tSetup = sw.ElapsedMilliseconds;

        if (GameDebug.Pathfinding)
            Debug.Log($"[NavMeshBuilder] CDT input: {cdt.VertexCount} vertices, {constraintPairs.Count} constraint edges (merged from {rawSegments.Count} raw)");

        cdt.Triangulate();
        tTriangulate = sw.ElapsedMilliseconds;

        foreach (var (a, b) in constraintPairs)
            cdt.InsertConstraint(a, b);
        tConstraints = sw.ElapsedMilliseconds;

        if (cdt.ConstraintsFailed > 0)
            Debug.LogWarning($"[NavMeshBuilder] CDT had {cdt.ConstraintsFailed} constraint failures — mesh may have gaps");

        Vector2 gridOrigin2D = new Vector2(origin.x, origin.z);
        var mesh = cdt.BuildNavMesh(pos => IsPositionWalkable(pos), cs, gridOrigin2D);
        tBuildMesh = sw.ElapsedMilliseconds;

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
                            Vector2Int gridCell = t_grid.WorldToCell(new Vector3(wx, 0, wy));
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

        mesh.BuildSpatialGrid(t_grid.CellSize * 4f);

        sw.Stop();
        long buildMs = sw.ElapsedMilliseconds;
        if (buildMs > 100)
            Debug.LogWarning($"[NavMeshBuilder] Build took {buildMs}ms (>{(buildMs > 1000 ? "FREEZE RISK" : "slow")}), " +
                $"tris={mesh.TriangleCount}, verts={mesh.VertexCount} | " +
                $"setup={tSetup}ms triangulate={tTriangulate - tSetup}ms constraints={tConstraints - tTriangulate}ms buildMesh={tBuildMesh - tConstraints}ms spatial+validate={buildMs - tBuildMesh}ms");
        else if (GameDebug.Pathfinding)
            Debug.Log($"[NavMeshBuilder] Build completed in {buildMs}ms, tris={mesh.TriangleCount}");

        return mesh;
    }

    /// <summary>
    /// Use registered building bounds for precise CDT constraint edges.
    /// Also includes any grid-only obstacles (terrain) via cell scanning,
    /// but buildings use their actual bounds instead of inflated cell rects.
    /// </summary>
    private List<Rect> ExtractObstacleRectsFromBuildings()
    {
        var rects = new List<Rect>();

        foreach (var kvp in t_bounds)
            rects.Add(kvp.Value);

        int w = t_grid.Width, h = t_grid.Height;
        var visited = new bool[w, h];
        float cs = t_grid.CellSize;
        Vector3 origin = t_grid.GridOrigin;

        foreach (var rect in rects)
        {
            Vector3 rMin = new Vector3(rect.xMin, 0, rect.yMin);
            Vector3 rMax = new Vector3(rect.xMax, 0, rect.yMax);
            int cx0 = Mathf.Max(0, Mathf.FloorToInt((rMin.x - origin.x) / cs));
            int cx1 = Mathf.Min(w - 1, Mathf.CeilToInt((rMax.x - origin.x) / cs));
            int cz0 = Mathf.Max(0, Mathf.FloorToInt((rMin.z - origin.z) / cs));
            int cz1 = Mathf.Min(h - 1, Mathf.CeilToInt((rMax.z - origin.z) / cs));
            for (int gy = cz0; gy <= cz1; gy++)
                for (int gx = cx0; gx <= cx1; gx++)
                    visited[gx, gy] = true;
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (visited[x, y]) continue;
                if (t_grid.IsWalkable(new Vector2Int(x, y))) continue;

                int x1 = x;
                while (x1 + 1 < w && !visited[x1 + 1, y] &&
                       !t_grid.IsWalkable(new Vector2Int(x1 + 1, y)))
                    x1++;

                int y1 = y;
                bool canExtend = true;
                while (canExtend && y1 + 1 < h)
                {
                    for (int xi = x; xi <= x1; xi++)
                    {
                        if (visited[xi, y1 + 1] || t_grid.IsWalkable(new Vector2Int(xi, y1 + 1)))
                        {
                            canExtend = false;
                            break;
                        }
                    }
                    if (canExtend) y1++;
                }

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
    /// Extract axis-aligned obstacle rectangles from the grid.
    /// Uses a greedy rect-merge to reduce vertex count.
    /// Fallback when no building bounds are registered (initial base build).
    /// </summary>
    private List<Rect> ExtractObstacleRects()
    {
        var rects = new List<Rect>();
        int w = t_grid.Width, h = t_grid.Height;
        var visited = new bool[w, h];
        float cs = t_grid.CellSize;
        Vector3 origin = t_grid.GridOrigin;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (visited[x, y]) continue;
                if (t_grid.IsWalkable(new Vector2Int(x, y))) continue;

                int x1 = x;
                while (x1 + 1 < w && !visited[x1 + 1, y] &&
                       !t_grid.IsWalkable(new Vector2Int(x1 + 1, y)))
                    x1++;

                int y1 = y;
                bool canExtend = true;
                while (canExtend && y1 + 1 < h)
                {
                    for (int xi = x; xi <= x1; xi++)
                    {
                        if (visited[xi, y1 + 1] || t_grid.IsWalkable(new Vector2Int(xi, y1 + 1)))
                        {
                            canExtend = false;
                            break;
                        }
                    }
                    if (canExtend) y1++;
                }

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
    /// Add Steiner points in a staggered (hexagonal) pattern to produce
    /// well-shaped, near-equilateral triangles. Every other row is offset
    /// by half the spacing, which is the optimal layout for Delaunay quality.
    /// A fractional cell offset prevents collinearity with grid-aligned
    /// building edges (common CDT failure source).
    /// </summary>
    private void AddSteinerPoints(CDTriangulator cdt, float minX, float minZ,
        float maxX, float maxZ, List<Rect> obstacles)
    {
        float spacing = t_grid.CellSize * 4f;
        float offset = t_grid.CellSize * 0.37f;
        float halfSpacing = spacing * 0.5f;
        int row = 0;

        for (float z = minZ + spacing; z < maxZ; z += spacing)
        {
            float rowOffset = (row % 2 == 1) ? halfSpacing : 0f;
            for (float x = minX + spacing + rowOffset; x < maxX; x += spacing)
            {
                Vector2 p = new Vector2(x + offset, z + offset);
                if (IsPositionWalkable(p) && !IsInsideAnyRect(p, obstacles))
                    cdt.AddVertex(p);
            }
            row++;
        }
    }

    private static bool IsInsideAnyRect(Vector2 p, List<Rect> rects)
    {
        foreach (var r in rects)
        {
            if (p.x >= r.xMin && p.x <= r.xMax && p.y >= r.yMin && p.y <= r.yMax)
                return true;
        }
        return false;
    }

    // Spatial grid for O(1) point-in-building lookups.
    // Each cell maps to the list of building Rects that overlap it.
    // Built once per rebuild via BuildWalkabilitySpatialGrid(), used by IsPositionWalkable().
    [ThreadStatic] private static Dictionary<long, List<Rect>> t_buildingSpatial;
    [ThreadStatic] private static float t_spatialCellSize;

    /// <summary>
    /// Build a spatial hash grid from t_bounds so IsPositionWalkable can do O(1)
    /// lookups instead of O(B) linear scans over all building rects.
    /// </summary>
    private static void BuildWalkabilitySpatialGrid(float gridCellSize)
    {
        float spatialCell = gridCellSize * 4f;
        t_spatialCellSize = spatialCell;
        float invCell = 1f / spatialCell;

        var spatial = new Dictionary<long, List<Rect>>();
        t_buildingSpatial = spatial;

        if (t_bounds == null || t_bounds.Count == 0) return;

        foreach (var kvp in t_bounds)
        {
            Rect r = kvp.Value;
            int cx0 = Mathf.FloorToInt(r.xMin * invCell);
            int cx1 = Mathf.FloorToInt(r.xMax * invCell);
            int cy0 = Mathf.FloorToInt(r.yMin * invCell);
            int cy1 = Mathf.FloorToInt(r.yMax * invCell);

            for (int cx = cx0; cx <= cx1; cx++)
            {
                for (int cy = cy0; cy <= cy1; cy++)
                {
                    long key = ((long)cx << 32) | (uint)cy;
                    if (!spatial.TryGetValue(key, out var list))
                    {
                        list = new List<Rect>(4);
                        spatial[key] = list;
                    }
                    list.Add(r);
                }
            }
        }
    }

    private bool IsPositionWalkable(Vector2 pos)
    {
        Vector3 worldPos = new Vector3(pos.x, t_gridY, pos.y);
        Vector2Int cell = t_grid.WorldToCell(worldPos);
        if (!t_grid.IsInBounds(cell)) return false;

        if (t_bounds.Count > 0)
        {
            // O(1) spatial lookup instead of O(B) linear scan
            if (t_buildingSpatial != null && t_spatialCellSize > 0f)
            {
                float invCell = 1f / t_spatialCellSize;
                int cx = Mathf.FloorToInt(pos.x * invCell);
                int cy = Mathf.FloorToInt(pos.y * invCell);
                long key = ((long)cx << 32) | (uint)cy;

                if (t_buildingSpatial.TryGetValue(key, out var rects))
                {
                    for (int i = 0; i < rects.Count; i++)
                    {
                        if (rects[i].Contains(pos))
                            return false;
                    }
                }
                return true;
            }

            // Fallback: linear scan (before spatial grid is built)
            foreach (var kvp in t_bounds)
            {
                if (kvp.Value.Contains(pos))
                    return false;
            }
            return true;
        }

        return t_grid.IsWalkable(cell);
    }

    /// <summary>
    /// Merge collinear overlapping axis-aligned segments into non-overlapping
    /// sub-segments. Groups segments by their axis line (horizontal or vertical),
    /// sorts by the varying coordinate, and produces gap-free constraint edges.
    /// </summary>
    private static List<(Vector2 a, Vector2 b)> MergeCollinearSegments(
        List<(Vector2 a, Vector2 b)> segments)
    {
        float eps = GeometryConstants.PositionEpsilon;

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
                Debug.LogWarning($"[NavMeshBuilder] Dropped non-axis-aligned segment: ({a.x:F2},{a.y:F2})->({b.x:F2},{b.y:F2})");
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
    //  PATH VALIDATION (stale-mesh guard)
    // ================================================================

    /// <summary>
    /// Returns true if any segment of the 2D path crosses a PENDING building rect.
    /// Only checks buildings placed AFTER the current async rebuild started, because
    /// the in-flight build already incorporates all earlier buildings and paths through
    /// them were already invalidated by HandleBuildingChange.
    /// Uses Liang-Barsky line clipping for robust segment-rect intersection.
    /// </summary>
    public bool PathCrossesAnyBuilding(List<Vector2> path)
    {
        if (path == null || path.Count < 2 || pendingBuildingIds.Count == 0)
            return false;

        foreach (int pendingId in pendingBuildingIds)
        {
            if (!buildingBounds.TryGetValue(pendingId, out Rect r))
                continue;
            for (int i = 0; i < path.Count - 1; i++)
            {
                if (SegmentCrossesRect(path[i], path[i + 1], r))
                    return true;
            }
        }
        return false;
    }

    private static bool SegmentCrossesRect(Vector2 p1, Vector2 p2, Rect rect)
    {
        float dx = p2.x - p1.x;
        float dy = p2.y - p1.y;
        float tMin = 0f, tMax = 1f;

        float[] p = { -dx, dx, -dy, dy };
        float[] q = { p1.x - rect.xMin, rect.xMax - p1.x, p1.y - rect.yMin, rect.yMax - p1.y };

        for (int i = 0; i < 4; i++)
        {
            if (Mathf.Abs(p[i]) < 1e-10f)
            {
                if (q[i] < 0f) return false;
            }
            else
            {
                float t = q[i] / p[i];
                if (p[i] < 0f) tMin = Mathf.Max(tMin, t);
                else tMax = Mathf.Min(tMax, t);
            }
        }
        return tMin <= tMax;
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
    /// Projects onto the closest triangle edge/interior rather than just centroid.
    /// </summary>
    public Vector3 FindNearestWalkablePosition(Vector3 worldPos)
    {
        if (activeNavMesh == null)
        {
            Debug.LogError("[NavMeshBuilder] FindNearestWalkablePosition: activeNavMesh is null, returning unvalidated position");
            return worldPos;
        }

        Vector2 nmPos = WorldToNavMesh(worldPos);
        int tri = activeNavMesh.FindTriangleAtPosition(nmPos);
        if (tri >= 0 && activeNavMesh.Triangles[tri].IsWalkable)
            return worldPos;

        float bestDist = float.MaxValue;
        Vector2 bestPos = nmPos;
        for (int i = 0; i < activeNavMesh.TriangleCount; i++)
        {
            if (!activeNavMesh.Triangles[i].IsWalkable) continue;
            ref var t = ref activeNavMesh.Triangles[i];
            Vector2 va = activeNavMesh.Vertices[t.V0];
            Vector2 vb = activeNavMesh.Vertices[t.V1];
            Vector2 vc = activeNavMesh.Vertices[t.V2];

            if (NavMeshData.PointInTriangle(nmPos, va, vb, vc))
                return worldPos;

            float d = NavMeshData.SqrDistanceToTriangle(nmPos, va, vb, vc);
            if (d < bestDist)
            {
                bestDist = d;
                bestPos = NearestPointOnTriangle(nmPos, va, vb, vc);
            }
        }
        return NavMeshToWorld(bestPos);
    }

    private static Vector2 NearestPointOnTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 bestPt = a;
        float bestD = float.MaxValue;

        Vector2 pAB = NearestPointOnSegment(p, a, b);
        float dAB = (p - pAB).sqrMagnitude;
        if (dAB < bestD) { bestD = dAB; bestPt = pAB; }

        Vector2 pBC = NearestPointOnSegment(p, b, c);
        float dBC = (p - pBC).sqrMagnitude;
        if (dBC < bestD) { bestD = dBC; bestPt = pBC; }

        Vector2 pCA = NearestPointOnSegment(p, c, a);
        float dCA = (p - pCA).sqrMagnitude;
        if (dCA < bestD) { bestPt = pCA; }

        return bestPt;
    }

    private static Vector2 NearestPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 1e-10f) return a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
        return a + ab * t;
    }

    // ================================================================
    //  GRID SNAPSHOT (thread-safe read-only copy of grid walkability)
    // ================================================================

    /// <summary>
    /// Immutable snapshot of grid walkability for thread-safe NavMesh building.
    /// Captures the full cells array so the background thread never reads live state.
    /// </summary>
    private class GridSnapshot : IGrid
    {
        private readonly int width, height;
        private readonly float cellSize;
        private readonly Vector3 gridOrigin;
        private readonly bool[,] walkable;

        public int Width => width;
        public int Height => height;
        public float CellSize => cellSize;
        public Vector3 GridOrigin => gridOrigin;

        public GridSnapshot(IGrid source)
        {
            width = source.Width;
            height = source.Height;
            cellSize = source.CellSize;
            gridOrigin = source.GridOrigin;

            walkable = new bool[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    walkable[x, y] = source.IsWalkable(new Vector2Int(x, y));
        }

        public bool IsWalkable(Vector2Int cell)
        {
            if (!IsInBounds(cell)) return false;
            return walkable[cell.x, cell.y];
        }

        public bool IsInBounds(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
        }

        public Vector2Int WorldToCell(Vector3 worldPosition)
        {
            int x = Mathf.RoundToInt((worldPosition.x - gridOrigin.x) / cellSize);
            int z = Mathf.RoundToInt((worldPosition.z - gridOrigin.z) / cellSize);
            return new Vector2Int(x, z);
        }

        public Vector3 CellToWorld(Vector2Int cell)
        {
            return new Vector3(
                cell.x * cellSize + gridOrigin.x,
                gridOrigin.y,
                cell.y * cellSize + gridOrigin.z
            );
        }

        public Vector3 FindNearestWalkablePosition(Vector3 desiredWorldPos, Vector3 referencePos)
        {
            return desiredWorldPos;
        }

        public bool HasLineOfSight(Vector2Int from, Vector2Int to)
        {
            return false;
        }
    }
}
