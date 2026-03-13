using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

public class DebugOverlay : MonoBehaviour
{
    public static DebugOverlay Instance { get; private set; }

    public bool showGrid = true;
    public bool showPaths = true;
    public bool showRanges;
    public bool showBuildZones;
    public bool showSeparation;
    public bool showNavMesh;
    public bool showNavMeshWidths;
    public bool showVelocities;
    public bool showBoidsForces;

    private Material lineMaterial;
    private Camera cam;

    private const float Y_OFFSET = 0.25f;

    // Reusable unit list to avoid per-frame allocation
    private readonly List<Unit> unitBuffer = new(128);

    // Cached component lookups
    private HeroController[] cachedHeroes;
    private float heroCacheTime;
    private readonly Dictionary<int, (HeroAutoAttack atk, HeroBuilder bld)> heroComponentCache = new();

    private BuildZone[] cachedBuildZones;
    private float buildZoneCacheTime;
    private readonly Dictionary<int, BoxCollider> buildZoneColliderCache = new();

    // Stuck duration snapshot from PathfindingDiagnostic
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

        // Depth-tested pass: paths and grid respect building geometry
        lineMaterial.SetPass(0);
        if (showGrid) DrawGrid();
        if (showPaths) DrawUnitPaths();

        // Always-on-top pass: debug overlays visible through everything
        overlayMaterial.SetPass(0);
        if (showRanges) DrawRanges();
        if (showBuildZones) DrawBuildZones();
        if (showSeparation) DrawSeparation();
        if (showNavMesh) DrawNavMesh();
        if (showVelocities) DrawVelocities();
        if (showBoidsForces) DrawBoidsForces();

        GL.PopMatrix();
    }

    // ================================================================
    // UNIT BUFFER (single allocation, reused every frame)
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
    // UNIT PATHS — color-coded by state
    // ================================================================

    private void DrawUnitPaths()
    {
        if (unitBuffer.Count == 0) return;

        RefreshStuckCache();

        GL.Begin(GL.LINES);
        float y = GridSystem.Instance != null ? GridSystem.Instance.GridOrigin.y + Y_OFFSET : Y_OFFSET;

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
                    pathColor = new Color(1f, 0.15f, 0.15f, 0.7f); // Red: unreachable
                else if (stuck)
                    pathColor = new Color(1f, 0.8f, 0f, 0.7f); // Yellow: stuck
                else
                    pathColor = unit.TeamId == 0
                        ? new Color(0.2f, 0.9f, 0.5f, 0.6f) // Green-cyan: team 0
                        : new Color(0.9f, 0.5f, 0.2f, 0.6f); // Orange: team 1

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

            var combat = unit.Combat;
            if (combat != null && combat.AttackTarget != null)
            {
                GL.Color(new Color(1f, 0.2f, 0.2f, 0.5f));
                Vector3 from = unit.transform.position;
                from.y = y;
                Vector3 to = combat.AttackTarget.position;
                to.y = y;
                GLLine(from, to);
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
    // BOIDS FORCE VISUALIZATION
    // ================================================================

    private void DrawBoidsForces()
    {
        if (unitBuffer.Count == 0) return;
        var pfm = PathfindingManager.Instance;
        if (pfm == null || !pfm.IsInitialized) return;

        float y = GridSystem.Instance != null ? GridSystem.Instance.GridOrigin.y + Y_OFFSET + 0.1f : Y_OFFSET;

        GL.Begin(GL.LINES);
        for (int i = 0; i < unitBuffer.Count; i++)
        {
            var unit = unitBuffer[i];
            if (unit == null) continue;

            Vector3 pos = unit.transform.position;
            pos.y = y;
            float r = unit.EffectiveRadius;

            // Inner circle: physical radius
            GL.Color(new Color(0.2f, 1f, 0.2f, 0.35f));
            DrawCircle(pos, r, 12);

            // Outer circle: separation radius (3x)
            GL.Color(new Color(1f, 0.4f, 0.2f, 0.15f));
            DrawCircle(pos, r * 3f, 12);

            // Nearby unit proximity lines
            if (UnitManager.Instance != null)
            {
                var nearby = UnitManager.Instance.GetUnitsInRadius(pos, r * 3f);
                for (int j = 0; j < nearby.Count; j++)
                {
                    var other = nearby[j];
                    if (other == null || other == unit || other.IsDead) continue;
                    float dist = Vector3.Distance(pos, other.transform.position);
                    float combined = r + other.EffectiveRadius;

                    if (dist < combined)
                        GL.Color(new Color(1f, 0f, 0f, 0.5f)); // Overlapping
                    else if (dist < combined * 1.5f)
                        GL.Color(new Color(1f, 0.6f, 0f, 0.3f)); // Separation zone
                    else
                        continue;

                    Vector3 otherPos = other.transform.position;
                    otherPos.y = y;
                    GLLine(pos, otherPos);
                }
            }
        }
        GL.End();
    }

    // ================================================================
    // NAVMESH VISUALIZATION — frustum-culled, portal midpoint widths
    // ================================================================

    private void DrawNavMesh()
    {
        var pfm = PathfindingManager.Instance;
        if (pfm == null || !pfm.IsInitialized) return;

        var mesh = pfm.ActiveNavMesh;
        if (mesh == null) return;

        float y = GridSystem.Instance != null ? GridSystem.Instance.GridOrigin.y + Y_OFFSET + 0.05f : Y_OFFSET;

        // Compute visible bounds for culling
        float viewMinX, viewMaxX, viewMinZ, viewMaxZ;
        if (cam != null)
        {
            float camH = cam.transform.position.y;
            float halfH = camH * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * cam.aspect;
            float pad = 15f;
            viewMinX = cam.transform.position.x - halfW - pad;
            viewMaxX = cam.transform.position.x + halfW + pad;
            viewMinZ = cam.transform.position.z - halfH - pad;
            viewMaxZ = cam.transform.position.z + halfH + pad;
        }
        else
        {
            viewMinX = viewMinZ = float.MinValue;
            viewMaxX = viewMaxZ = float.MaxValue;
        }

        GL.Begin(GL.LINES);
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            ref var tri = ref mesh.Triangles[i];

            Vector2 v0 = mesh.Vertices[tri.V0];
            Vector2 v1 = mesh.Vertices[tri.V1];
            Vector2 v2 = mesh.Vertices[tri.V2];

            // Frustum cull: skip if triangle AABB is entirely off-screen
            float triMinX = Mathf.Min(v0.x, Mathf.Min(v1.x, v2.x));
            float triMaxX = Mathf.Max(v0.x, Mathf.Max(v1.x, v2.x));
            float triMinZ = Mathf.Min(v0.y, Mathf.Min(v1.y, v2.y));
            float triMaxZ = Mathf.Max(v0.y, Mathf.Max(v1.y, v2.y));

            if (triMaxX < viewMinX || triMinX > viewMaxX ||
                triMaxZ < viewMinZ || triMinZ > viewMaxZ)
                continue;

            if (!tri.IsWalkable)
            {
                GL.Color(new Color(1f, 0.1f, 0.1f, 0.1f));
                GLLine(new Vector3(v0.x, y, v0.y), new Vector3(v1.x, y, v1.y));
                GLLine(new Vector3(v1.x, y, v1.y), new Vector3(v2.x, y, v2.y));
                GLLine(new Vector3(v2.x, y, v2.y), new Vector3(v0.x, y, v0.y));
                continue;
            }

            bool isolated = tri.N0 < 0 && tri.N1 < 0 && tri.N2 < 0;
            bool highCost = tri.CostMultiplier > 1.5f;

            Color edgeColor;
            if (isolated)
                edgeColor = new Color(1f, 0f, 1f, 0.6f);
            else if (highCost)
            {
                float costNorm = Mathf.Clamp01((tri.CostMultiplier - 1f) / 30f);
                edgeColor = new Color(1f, 0.5f - costNorm * 0.3f, 0f, 0.2f + costNorm * 0.5f);
            }
            else
                edgeColor = new Color(0.3f, 0.7f, 1f, 0.15f);

            GL.Color(edgeColor);
            GLLine(new Vector3(v0.x, y, v0.y), new Vector3(v1.x, y, v1.y));
            GLLine(new Vector3(v1.x, y, v1.y), new Vector3(v2.x, y, v2.y));
            GLLine(new Vector3(v2.x, y, v2.y), new Vector3(v0.x, y, v0.y));

            if (showNavMeshWidths)
            {
                for (int e = 0; e < 3; e++)
                {
                    int n = tri.GetNeighbor(e);
                    if (n < 0 || n <= i) continue;

                    var (va, vb) = tri.GetEdgeVertices(e);
                    Vector2 edgeMid = (mesh.Vertices[va] + mesh.Vertices[vb]) * 0.5f;
                    float portalLen = Vector2.Distance(mesh.Vertices[va], mesh.Vertices[vb]);

                    float w = tri.GetWidth(e);
                    float wNorm = Mathf.Clamp01(w / 3f);

                    // Draw portal edge highlighted by width
                    GL.Color(new Color(1f - wNorm, wNorm, 0f, 0.5f));
                    GLLine(
                        new Vector3(mesh.Vertices[va].x, y + 0.08f, mesh.Vertices[va].y),
                        new Vector3(mesh.Vertices[vb].x, y + 0.08f, mesh.Vertices[vb].y));

                    // Draw width indicator: short perpendicular line at midpoint
                    Vector2 edgeDir = (mesh.Vertices[vb] - mesh.Vertices[va]).normalized;
                    Vector2 perp = new Vector2(-edgeDir.y, edgeDir.x);
                    float indLen = Mathf.Min(w * 0.3f, 0.5f);
                    Vector3 midA = new Vector3(edgeMid.x + perp.x * indLen, y + 0.12f, edgeMid.y + perp.y * indLen);
                    Vector3 midB = new Vector3(edgeMid.x - perp.x * indLen, y + 0.12f, edgeMid.y - perp.y * indLen);
                    GLLine(midA, midB);
                }
            }
        }
        GL.End();
    }

    // ================================================================
    // RANGES — cached component lookups
    // ================================================================

    private void DrawRanges()
    {
        float y = GridSystem.Instance != null ? GridSystem.Instance.GridOrigin.y + Y_OFFSET : Y_OFFSET;

        if (cachedHeroes == null || Time.time - heroCacheTime > 2f)
        {
            cachedHeroes = Object.FindObjectsByType<HeroController>(FindObjectsSortMode.None);
            heroCacheTime = Time.time;
            heroComponentCache.Clear();
        }

        GL.Begin(GL.LINES);
        foreach (var hero in cachedHeroes)
        {
            if (hero == null) continue;

            int hid = hero.GetInstanceID();
            if (!heroComponentCache.TryGetValue(hid, out var cached))
            {
                cached = (hero.GetComponent<HeroAutoAttack>(), hero.GetComponent<HeroBuilder>());
                heroComponentCache[hid] = cached;
            }

            Vector3 heroPos = hero.transform.position;
            heroPos.y = y;

            if (cached.atk != null)
            {
                GL.Color(new Color(1f, 0.9f, 0.2f, 0.5f));
                DrawCircle(heroPos, cached.atk.AttackRange, 32);
            }
            if (cached.bld != null)
            {
                GL.Color(new Color(0.2f, 1f, 0.4f, 0.35f));
                DrawCircle(heroPos, cached.bld.BuildRange, 48);
            }
        }

        for (int i = 0; i < unitBuffer.Count; i++)
        {
            var unit = unitBuffer[i];
            if (unit == null || unit.Data == null) continue;
            Vector3 pos = unit.transform.position;
            pos.y = y;

            GL.Color(new Color(1f, 0.8f, 0.2f, 0.25f));
            DrawCircle(pos, unit.Data.attackRange, 16);
        }
        GL.End();
    }

    // ================================================================
    // BUILD ZONES — cached collider lookups
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
    // SEPARATION
    // ================================================================

    private void DrawSeparation()
    {
        if (unitBuffer.Count == 0) return;

        float y = GridSystem.Instance != null ? GridSystem.Instance.GridOrigin.y + Y_OFFSET : Y_OFFSET;

        GL.Begin(GL.LINES);
        GL.Color(new Color(0.8f, 0.4f, 1f, 0.2f));
        for (int i = 0; i < unitBuffer.Count; i++)
        {
            var unit = unitBuffer[i];
            if (unit == null) continue;
            Vector3 pos = unit.transform.position;
            pos.y = y;
            DrawCircle(pos, unit.EffectiveRadius * 3f, 12);
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

    private static void DrawCircle(Vector3 center, float radius, int segments)
    {
        float step = 2f * Mathf.PI / segments;
        Vector3 prev = center + new Vector3(radius, 0, 0);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * step;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            GLLine(prev, next);
            prev = next;
        }
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

    // ================================================================
    // UTILITY
    // ================================================================

    private void GetVisibleCellRange(GridSystem grid, out int minX, out int maxX, out int minZ, out int maxZ)
    {
        if (cam == null)
        {
            minX = 0; maxX = grid.Width - 1;
            minZ = 0; maxZ = grid.Height - 1;
            return;
        }

        float camHeight = cam.transform.position.y;
        float halfFrustumH = camHeight * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfFrustumW = halfFrustumH * cam.aspect;

        Vector3 camPos = cam.transform.position;
        float padding = 10f;

        Vector3 worldMin = new(camPos.x - halfFrustumW - padding, 0, camPos.z - halfFrustumH - padding);
        Vector3 worldMax = new(camPos.x + halfFrustumW + padding, 0, camPos.z + halfFrustumH + padding);

        Vector2Int cellMin = grid.WorldToCell(worldMin);
        Vector2Int cellMax = grid.WorldToCell(worldMax);

        minX = Mathf.Max(0, cellMin.x);
        maxX = Mathf.Min(grid.Width - 1, cellMax.x);
        minZ = Mathf.Max(0, cellMin.y);
        maxZ = Mathf.Min(grid.Height - 1, cellMax.y);
    }
}
