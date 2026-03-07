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

    private Material lineMaterial;
    private Camera cam;

    private const float Y_OFFSET = 0.15f;

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

    private void EnsureMaterial()
    {
        if (lineMaterial != null) return;
        var shader = Shader.Find("Hidden/Internal-Colored");
        lineMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        lineMaterial.SetInt("_Cull", 0);
        lineMaterial.SetInt("_ZWrite", 0);
        lineMaterial.SetInt("_ZTest", 0);
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
        lineMaterial.SetPass(0);

        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity);

        if (showGrid) DrawGrid();
        if (showPaths) DrawUnitPaths();
        if (showRanges) DrawRanges();
        if (showBuildZones) DrawBuildZones();
        if (showSeparation) DrawSeparation();

        GL.PopMatrix();
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
        if (UnitManager.Instance == null) return;

        var units = GetAllUnits();
        if (units == null) return;

        GL.Begin(GL.LINES);
        float y = GridSystem.Instance != null ? GridSystem.Instance.GridOrigin.y + Y_OFFSET : Y_OFFSET;

        foreach (var unit in units)
        {
            if (unit == null) continue;
            var movement = unit.GetComponent<UnitMovement>();
            if (movement == null || movement.Waypoints == null) continue;

            var wps = movement.Waypoints;
            int idx = movement.WaypointIndex;
            if (wps.Count == 0) continue;

            // Path line (cyan)
            GL.Color(new Color(0f, 0.9f, 1f, 0.7f));
            Vector3 prev = unit.transform.position;
            prev.y = y;
            for (int i = idx; i < wps.Count; i++)
            {
                Vector3 wp = wps[i];
                wp.y = y;
                GLLine(prev, wp);
                prev = wp;
            }

            // Waypoint dots
            GL.Color(new Color(0f, 0.9f, 1f, 0.5f));
            for (int i = idx; i < wps.Count; i++)
            {
                Vector3 wp = wps[i];
                wp.y = y;
                DrawSmallCross(wp, 0.3f);
            }

            // Destination diamond
            if (movement.WorldTarget.HasValue)
            {
                GL.Color(new Color(1f, 1f, 0f, 0.8f));
                Vector3 dest = movement.WorldTarget.Value;
                dest.y = y;
                DrawDiamond(dest, 0.6f);
            }

            // Attack target line (red)
            var combat = unit.GetComponent<UnitCombat>();
            if (combat != null && combat.AttackTarget != null)
            {
                GL.Color(new Color(1f, 0.2f, 0.2f, 0.6f));
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
    // RANGES
    // ================================================================
    private void DrawRanges()
    {
        float y = GridSystem.Instance != null ? GridSystem.Instance.GridOrigin.y + Y_OFFSET : Y_OFFSET;

        var heroes = Object.FindObjectsByType<HeroController>(FindObjectsSortMode.None);
        GL.Begin(GL.LINES);
        foreach (var hero in heroes)
        {
            if (hero == null) continue;
            Vector3 heroPos = hero.transform.position;
            heroPos.y = y;

            var autoAttack = hero.GetComponent<HeroAutoAttack>();
            if (autoAttack != null)
            {
                GL.Color(new Color(1f, 0.9f, 0.2f, 0.5f));
                DrawCircle(heroPos, autoAttack.AttackRange, 32);
            }

            var builder = hero.GetComponent<HeroBuilder>();
            if (builder != null)
            {
                GL.Color(new Color(0.2f, 1f, 0.4f, 0.35f));
                DrawCircle(heroPos, builder.BuildRange, 48);
            }
        }
        GL.End();

        // Unit attack ranges
        var units = GetAllUnits();
        if (units == null) return;

        GL.Begin(GL.LINES);
        foreach (var unit in units)
        {
            if (unit == null || unit.Data == null) continue;
            Vector3 pos = unit.transform.position;
            pos.y = y;

            GL.Color(new Color(1f, 0.8f, 0.2f, 0.25f));
            DrawCircle(pos, unit.Data.attackRange, 16);

            GL.Color(new Color(1f, 0.8f, 0.2f, 0.1f));
            DrawCircle(pos, unit.Data.attackRange * 2f, 16);
        }
        GL.End();
    }

    // ================================================================
    // BUILD ZONES
    // ================================================================
    private void DrawBuildZones()
    {
        var zones = Object.FindObjectsByType<BuildZone>(FindObjectsSortMode.None);
        if (zones == null || zones.Length == 0) return;

        float y = GridSystem.Instance != null ? GridSystem.Instance.GridOrigin.y + Y_OFFSET : Y_OFFSET;

        GL.Begin(GL.LINES);
        foreach (var zone in zones)
        {
            if (zone == null) continue;
            var col = zone.GetComponent<BoxCollider>();
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

            // Diagonal cross for identification
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
        var units = GetAllUnits();
        if (units == null) return;

        float y = GridSystem.Instance != null ? GridSystem.Instance.GridOrigin.y + Y_OFFSET : Y_OFFSET;

        GL.Begin(GL.LINES);
        GL.Color(new Color(0.8f, 0.4f, 1f, 0.2f));
        foreach (var unit in units)
        {
            if (unit == null) continue;
            var movement = unit.GetComponent<UnitMovement>();
            if (movement == null) continue;

            Vector3 pos = unit.transform.position;
            pos.y = y;
            DrawCircle(pos, movement.SeparationRadius, 12);
        }
        GL.End();
    }

    // ================================================================
    // GL HELPERS (call between GL.Begin / GL.End)
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

    private static List<Unit> GetAllUnits()
    {
        if (UnitManager.Instance == null) return null;
        var list = new List<Unit>();
        for (int team = 0; team <= 1; team++)
        {
            var teamUnits = UnitManager.Instance.GetTeamUnits(team);
            if (teamUnits != null)
                list.AddRange(teamUnits);
        }
        return list;
    }
}
