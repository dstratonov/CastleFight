using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

public class DebugOverlay : MonoBehaviour
{
    public static DebugOverlay Instance { get; private set; }

    public bool showGrid = true;
    public bool showPaths = true;
    public bool showBuildZones;
    public bool showNavMesh;
    public bool showVelocities;
    public bool showUnitCells = true;
    public bool showAttackRange;

    private Material lineMaterial;
    private Camera cam;

    private const float Y_OFFSET = 0.25f;

    private readonly List<Unit> unitBuffer = new(128);

    private HeroController[] cachedHeroes;
    private float heroCacheTime;
    private readonly Dictionary<int, (HeroAutoAttack atk, HeroBuilder bld)> heroComponentCache = new();

    private BuildZone[] cachedBuildZones;
    private float buildZoneCacheTime;
    private readonly Dictionary<int, BoxCollider> buildZoneColliderCache = new();

    private readonly HashSet<int> stuckUnitIds = new();
    private float stuckCacheTime;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    private void Start()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
    }

    private void OnEnable()
    {
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
    }

    private Material overlayMaterial;

    private void EnsureMaterial()
    {
        if (lineMaterial != null) return;
        var shader = Shader.Find("Hidden/Internal-Colored");

        lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        lineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        lineMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        lineMaterial.SetInt("_Cull", 0);
        lineMaterial.SetInt("_ZWrite", 0);
        lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);

        overlayMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        overlayMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        overlayMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        overlayMaterial.SetInt("_Cull", 0);
        overlayMaterial.SetInt("_ZWrite", 0);
        overlayMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
    {
        if (cam == null) cam = Camera.main;
        if (renderingCamera != cam) return;
        RenderOverlay();
    }

    private void OnPostRender()
    {
        RenderOverlay();
    }

    private void RenderOverlay()
    {
        EnsureMaterial();

        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity);

        RefreshUnitBuffer();

        lineMaterial.SetPass(0);
        if (showGrid) DrawGrid();
        if (showPaths) DrawUnitPaths();

        overlayMaterial.SetPass(0);
        if (showBuildZones) DrawBuildZones();
        if (showNavMesh) DrawNavMesh();
        if (showVelocities) DrawVelocities();
        if (showUnitCells) DrawUnitCells();
        if (showAttackRange) DrawAttackRange();

        GL.PopMatrix();
    }

    // ================================================================
    // UNIT BUFFER
    // ================================================================

    private void RefreshUnitBuffer()
    {
        unitBuffer.Clear();
        if (UnitManager.Instance == null) return;
        for (int team = 0; team <= 1; team++)
        {
            var teamUnits = UnitManager.Instance.GetTeamUnits(team);
            if (teamUnits != null)
            {
                for (int i = 0; i < teamUnits.Count; i++)
                    unitBuffer.Add(teamUnits[i]);
            }
        }
    }

    private void RefreshStuckCache()
    {
        if (Time.time - stuckCacheTime < 1f) return;
        stuckCacheTime = Time.time;
        stuckUnitIds.Clear();

        for (int i = 0; i < unitBuffer.Count; i++)
        {
            var unit = unitBuffer[i];
            if (unit == null) continue;
            var movement = unit.Movement;
            if (movement == null) continue;
            if (movement.IsDestinationUnreachable)
                stuckUnitIds.Add(unit.GetInstanceID());
        }
    }

    // ================================================================
    // GRID
    // ================================================================

    private void DrawGrid()
    {
        var grid = GridSystem.Instance;
        if (grid == null) return;

        float cs = grid.CellSize;
        float y = grid.GridOrigin.y + Y_OFFSET;

        GetVisibleCellRange(grid, out int minX, out int maxX, out int minZ, out int maxZ);

        GL.Begin(GL.LINES);
        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                var cell = new Vector2Int(x, z);
                Vector3 center = grid.CellToWorld(cell);
                center.y = y;

                CellState state = grid.GetCellState(cell);
                if (state == CellState.Building)
                {
                    GL.Color(new Color(1f, 0.2f, 0.1f, 0.5f));
                    DrawCellFill(center, cs * 0.45f);
                }
                else
                {
                    GL.Color(new Color(0.3f, 0.8f, 0.3f, 0.08f));
                }

                float half = cs * 0.5f;
                Vector3 a = center + new Vector3(-half, 0, -half);
                Vector3 b = center + new Vector3(half, 0, -half);
                Vector3 c = center + new Vector3(half, 0, half);
                Vector3 d = center + new Vector3(-half, 0, half);

                GLLine(a, b); GLLine(b, c); GLLine(c, d); GLLine(d, a);
            }
        }
        GL.End();
    }

    // ================================================================
    // UNIT PATHS
    // ================================================================

    private void DrawUnitPaths()
    {
        if (unitBuffer.Count == 0) return;

        RefreshStuckCache();

        GL.Begin(GL.LINES);
        var grid = GridSystem.Instance;
        float y = grid != null ? grid.GridOrigin.y + Y_OFFSET : Y_OFFSET;

        for (int u = 0; u < unitBuffer.Count; u++)
        {
            var unit = unitBuffer[u];
            if (unit == null) continue;
            var movement = unit.Movement;
            if (movement == null) continue;

            int id = unit.GetInstanceID();
            bool unreachable = movement.IsDestinationUnreachable;
            bool stuck = stuckUnitIds.Contains(id);

            var wps = movement.Waypoints;
            if (wps != null && wps.Count > 0)
            {
                int idx = movement.WaypointIndex;

                Color pathColor;
                if (unreachable)
                    pathColor = new Color(1f, 0.15f, 0.15f, 0.7f);
                else if (stuck)
                    pathColor = new Color(1f, 0.8f, 0f, 0.7f);
                else
                    pathColor = unit.TeamId == 0
                        ? new Color(0.2f, 0.9f, 0.5f, 0.6f)
                        : new Color(0.9f, 0.5f, 0.2f, 0.6f);

                GL.Color(pathColor);
                Vector3 prev = unit.transform.position;
                prev.y = y;
                for (int i = idx; i < wps.Count; i++)
                {
                    Vector3 wp = wps[i];
                    wp.y = y;
                    GLLine(prev, wp);
                    DrawSmallCross(wp, 0.2f);
                    prev = wp;
                }
            }

            if (movement.WorldTarget.HasValue)
            {
                GL.Color(unreachable
                    ? new Color(1f, 0.2f, 0.2f, 0.8f)
                    : new Color(1f, 1f, 0f, 0.8f));
                Vector3 dest = movement.WorldTarget.Value;
                dest.y = y;
                DrawDiamond(dest, 0.5f);
            }
        }
        GL.End();
    }

    // ================================================================
    // VELOCITY VECTORS
    // ================================================================

    private void DrawVelocities()
    {
        if (unitBuffer.Count == 0) return;

        float y = GridSystem.Instance != null ? GridSystem.Instance.GridOrigin.y + Y_OFFSET + 0.05f : Y_OFFSET;

        GL.Begin(GL.LINES);
        for (int i = 0; i < unitBuffer.Count; i++)
        {
            var unit = unitBuffer[i];
            if (unit == null) continue;
            var movement = unit.Movement;
            if (movement == null || !movement.IsMoving) continue;

            Vector3 pos = unit.transform.position;
            pos.y = y;

            Vector3 vel = pos - movement.PreviousPosition;
            vel.y = 0f;

            if (vel.sqrMagnitude < 0.0001f) continue;

            float speed = vel.magnitude / Mathf.Max(Time.deltaTime, 0.001f);
            float speedNorm = Mathf.Clamp01(speed / 5f);
            GL.Color(new Color(1f - speedNorm, speedNorm, 0.3f, 0.7f));

            Vector3 tip = pos + vel.normalized * 1.5f;
            GLLine(pos, tip);

            Vector3 right = Vector3.Cross(Vector3.up, vel.normalized) * 0.2f;
            GLLine(tip, tip - vel.normalized * 0.4f + right);
            GLLine(tip, tip - vel.normalized * 0.4f - right);
        }
        GL.End();
    }

    // ================================================================
    // UNIT CELL PRESENCE
    // ================================================================

    private void DrawUnitCells()
    {
        var presence = UnitGridPresence.Instance;
        var grid = GridSystem.Instance;
        if (presence == null || grid == null || UnitManager.Instance == null) return;

        float y = grid.GridOrigin.y + Y_OFFSET + 0.02f;
        float cs = grid.CellSize;
        float half = cs * 0.48f;

        GetVisibleGroundBounds(out float viewMinX, out float viewMaxX, out float viewMinZ, out float viewMaxZ);

        GL.Begin(GL.LINES);

        var allUnits = UnitManager.Instance.AllUnits;
        for (int u = 0; u < allUnits.Count; u++)
        {
            var unit = allUnits[u];
            if (unit == null || unit.IsDead) continue;

            var cells = presence.GetUnitCells(unit.GetInstanceID());
            if (cells == null) continue;

            Color cellColor = unit.TeamId == 0
                ? new Color(0.2f, 0.4f, 1f, 0.4f)
                : new Color(1f, 0.3f, 0.2f, 0.4f);
            GL.Color(cellColor);

            for (int c = 0; c < cells.Count; c++)
            {
                Vector3 center = grid.CellToWorld(cells[c]);
                if (center.x + half < viewMinX || center.x - half > viewMaxX ||
                    center.z + half < viewMinZ || center.z - half > viewMaxZ)
                    continue;

                DrawCellOutline(center, half, y);
            }
        }
        GL.End();
    }

    // ================================================================
    // ATTACK RANGE — grid-based expanded footprint
    // ================================================================

    private void DrawAttackRange()
    {
        var grid = GridSystem.Instance;
        if (grid == null || unitBuffer.Count == 0) return;

        float y = grid.GridOrigin.y + Y_OFFSET + 0.04f;
        float cs = grid.CellSize;
        float half = cs * 0.46f;

        GetVisibleGroundBounds(out float viewMinX, out float viewMaxX, out float viewMinZ, out float viewMaxZ);

        GL.Begin(GL.LINES);

        for (int u = 0; u < unitBuffer.Count; u++)
        {
            var unit = unitBuffer[u];
            if (unit == null || unit.IsDead || unit.Data == null) continue;

            Vector2Int unitCell = grid.WorldToCell(unit.transform.position);
            var (atkMin, atkMax) = AttackRangeHelper.GetAttackRect(
                unitCell, unit.FootprintSize, unit.Data.attackRangeCells);

            // Yellow for attack range
            Color rangeColor = unit.TeamId == 0
                ? new Color(0.3f, 0.8f, 1f, 0.25f)
                : new Color(1f, 0.8f, 0.2f, 0.25f);
            GL.Color(rangeColor);

            for (int x = atkMin.x; x <= atkMax.x; x++)
            {
                for (int z = atkMin.y; z <= atkMax.y; z++)
                {
                    // Skip cells that are part of the unit's own footprint
                    var (fpMin, fpMax) = FootprintHelper.GetRect(unitCell, unit.FootprintSize);
                    if (x >= fpMin.x && x <= fpMax.x && z >= fpMin.y && z <= fpMax.y)
                        continue;

                    Vector3 center = grid.CellToWorld(new Vector2Int(x, z));
                    if (center.x + half < viewMinX || center.x - half > viewMaxX ||
                        center.z + half < viewMinZ || center.z - half > viewMaxZ)
                        continue;

                    DrawCellOutline(center, half, y);
                }
            }
        }
        GL.End();
    }

    // ================================================================
    // GRID WALKABILITY
    // ================================================================

    private void DrawNavMesh()
    {
        var grid = GridSystem.Instance;
        if (grid == null) return;

        float y = grid.GridOrigin.y + Y_OFFSET + 0.05f;
        float cs = grid.CellSize;
        float half = cs * 0.5f;

        GetVisibleGroundBounds(out float viewMinX, out float viewMaxX, out float viewMinZ, out float viewMaxZ);

        GL.Begin(GL.LINES);
        GL.Color(new Color(1f, 0.2f, 0.2f, 0.3f));

        for (int gx = 0; gx < grid.Width; gx++)
        {
            for (int gy = 0; gy < grid.Height; gy++)
            {
                if (grid.IsWalkable(new Vector2Int(gx, gy))) continue;

                Vector3 center = grid.CellToWorld(new Vector2Int(gx, gy));
                if (center.x + half < viewMinX || center.x - half > viewMaxX ||
                    center.z + half < viewMinZ || center.z - half > viewMaxZ)
                    continue;

                GLLine(new Vector3(center.x - half, y, center.z - half),
                       new Vector3(center.x + half, y, center.z + half));
                GLLine(new Vector3(center.x + half, y, center.z - half),
                       new Vector3(center.x - half, y, center.z + half));
            }
        }
        GL.End();
    }

    // ================================================================
    // BUILD ZONES
    // ================================================================

    private void DrawBuildZones()
    {
        if (cachedBuildZones == null || Time.time - buildZoneCacheTime > 5f)
        {
            cachedBuildZones = Object.FindObjectsByType<BuildZone>(FindObjectsSortMode.None);
            buildZoneCacheTime = Time.time;
            buildZoneColliderCache.Clear();
        }
        if (cachedBuildZones == null || cachedBuildZones.Length == 0) return;

        float y = GridSystem.Instance != null ? GridSystem.Instance.GridOrigin.y + Y_OFFSET : Y_OFFSET;

        GL.Begin(GL.LINES);
        foreach (var zone in cachedBuildZones)
        {
            if (zone == null) continue;

            int zid = zone.GetInstanceID();
            if (!buildZoneColliderCache.TryGetValue(zid, out var col))
            {
                col = zone.GetComponent<BoxCollider>();
                buildZoneColliderCache[zid] = col;
            }
            if (col == null) continue;

            Bounds b = col.bounds;
            Color c = zone.TeamId == 0
                ? new Color(0.2f, 0.5f, 1f, 0.5f)
                : new Color(1f, 0.3f, 0.3f, 0.5f);
            GL.Color(c);

            Vector3 a1 = new(b.min.x, y, b.min.z);
            Vector3 b1 = new(b.max.x, y, b.min.z);
            Vector3 c1 = new(b.max.x, y, b.max.z);
            Vector3 d1 = new(b.min.x, y, b.max.z);

            GLLine(a1, b1); GLLine(b1, c1); GLLine(c1, d1); GLLine(d1, a1);

            GL.Color(new Color(c.r, c.g, c.b, 0.15f));
            GLLine(a1, c1); GLLine(b1, d1);
        }
        GL.End();
    }

    // ================================================================
    // GL HELPERS
    // ================================================================

    private static void GLLine(Vector3 a, Vector3 b)
    {
        GL.Vertex3(a.x, a.y, a.z);
        GL.Vertex3(b.x, b.y, b.z);
    }

    private static void DrawSmallCross(Vector3 center, float size)
    {
        GLLine(center + new Vector3(-size, 0, 0), center + new Vector3(size, 0, 0));
        GLLine(center + new Vector3(0, 0, -size), center + new Vector3(0, 0, size));
    }

    private static void DrawDiamond(Vector3 center, float size)
    {
        Vector3 top = center + new Vector3(0, 0, size);
        Vector3 right = center + new Vector3(size, 0, 0);
        Vector3 bottom = center + new Vector3(0, 0, -size);
        Vector3 left = center + new Vector3(-size, 0, 0);
        GLLine(top, right); GLLine(right, bottom);
        GLLine(bottom, left); GLLine(left, top);
    }

    private static void DrawCellFill(Vector3 center, float half)
    {
        Vector3 a = center + new Vector3(-half, 0, -half);
        Vector3 b = center + new Vector3(half, 0, -half);
        Vector3 c = center + new Vector3(half, 0, half);
        Vector3 d = center + new Vector3(-half, 0, half);
        GLLine(a, c); GLLine(b, d);
    }

    private static void DrawCellOutline(Vector3 center, float half, float y)
    {
        GLLine(new Vector3(center.x - half, y, center.z - half),
               new Vector3(center.x + half, y, center.z - half));
        GLLine(new Vector3(center.x + half, y, center.z - half),
               new Vector3(center.x + half, y, center.z + half));
        GLLine(new Vector3(center.x + half, y, center.z + half),
               new Vector3(center.x - half, y, center.z + half));
        GLLine(new Vector3(center.x - half, y, center.z + half),
               new Vector3(center.x - half, y, center.z - half));
    }

    // ================================================================
    // UTILITY
    // ================================================================

    private void GetVisibleGroundBounds(out float worldMinX, out float worldMaxX, out float worldMinZ, out float worldMaxZ)
    {
        worldMinX = worldMinZ = float.MaxValue;
        worldMaxX = worldMaxZ = float.MinValue;

        if (cam == null)
        {
            worldMinX = worldMinZ = float.MinValue;
            worldMaxX = worldMaxZ = float.MaxValue;
            return;
        }

        float groundY = GridSystem.Instance != null ? GridSystem.Instance.GridOrigin.y : 0f;
        float padding = 15f;

        Vector3[] viewportPoints = {
            new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 0),
            new(0.5f, 0, 0), new(0.5f, 1, 0), new(0, 0.5f, 0), new(1, 0.5f, 0)
        };

        int hits = 0;
        for (int i = 0; i < viewportPoints.Length; i++)
        {
            Ray ray = cam.ViewportPointToRay(viewportPoints[i]);
            if (Mathf.Abs(ray.direction.y) < 0.001f) continue;

            float t = (groundY - ray.origin.y) / ray.direction.y;
            if (t < 0) t = cam.farClipPlane;

            Vector3 hit = ray.origin + ray.direction * Mathf.Min(t, cam.farClipPlane);
            worldMinX = Mathf.Min(worldMinX, hit.x);
            worldMaxX = Mathf.Max(worldMaxX, hit.x);
            worldMinZ = Mathf.Min(worldMinZ, hit.z);
            worldMaxZ = Mathf.Max(worldMaxZ, hit.z);
            hits++;
        }

        if (hits == 0)
        {
            worldMinX = worldMinZ = float.MinValue;
            worldMaxX = worldMaxZ = float.MaxValue;
            return;
        }

        worldMinX -= padding;
        worldMaxX += padding;
        worldMinZ -= padding;
        worldMaxZ += padding;
    }

    private void GetVisibleCellRange(GridSystem grid, out int minX, out int maxX, out int minZ, out int maxZ)
    {
        GetVisibleGroundBounds(out float wMinX, out float wMaxX, out float wMinZ, out float wMaxZ);

        if (wMinX == float.MinValue)
        {
            minX = 0; maxX = grid.Width - 1;
            minZ = 0; maxZ = grid.Height - 1;
            return;
        }

        Vector2Int cellMin = grid.WorldToCell(new Vector3(wMinX, 0, wMinZ));
        Vector2Int cellMax = grid.WorldToCell(new Vector3(wMaxX, 0, wMaxZ));

        minX = Mathf.Max(0, cellMin.x);
        maxX = Mathf.Min(grid.Width - 1, cellMax.x);
        minZ = Mathf.Max(0, cellMin.y);
        maxZ = Mathf.Min(grid.Height - 1, cellMax.y);
    }
}
