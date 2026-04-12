using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>
/// Runtime debug overlay for Castle Fight.
///
/// Uses a pool of <see cref="LineRenderer"/> GameObjects instead of GL
/// immediate mode, so the overlay reliably shows up under URP and in every
/// Game View screenshot — GL.Begin/End inside endCameraRendering callbacks
/// silently no-ops under URP's render pass setup, which was making debug
/// screenshots useless.
///
/// Overlays visualize:
///  * Unit paths (line from unit to its WorldTarget)
///  * Unit velocities (speed-colored arrows)
///  * Attack ranges (effective radius + attack range circle per unit)
///  * RVO agent radii (actual collision-avoidance circles)
///  * Building physical footprints (real BoxCollider bounds)
///  * Build zones
///  * Attack slot claims (yellow diamond = in-flight claim, green = actively attacking)
///
/// All overlays are toggled at runtime via <see cref="PathfindingDebugToggle"/>.
/// </summary>
public class DebugOverlay : MonoBehaviour
{
    public static DebugOverlay Instance { get; private set; }

    public bool showNavMesh;              // F4 — deprecated (GL-based); keeps the F-key for API compat
    public bool showPaths = true;         // F3 — unit path lines to destination
    public bool showVelocities;           // F6 — movement velocity arrows
    public bool showAttackRange;          // F5 — attack reach = unitRadius + attackRange
    public bool showAggroRange;           // NEW — UnitData.aggroRadius (how far the unit scans for targets)
    public bool showUnitRadius;           // unit.EffectiveRadius — the single unified physical radius (== RVO radius)
    public bool showBuildingFootprints = true; // real BoxCollider bounds
    public bool showBuildZones;
    public bool showAttackSlots;          // claimed attack positions

    private const float Y_OFFSET = 0.25f;

    // LineRenderer pool — disabled LineRenderers are reused next frame.
    private Transform poolParent;
    private Material lineMat;
    private readonly List<LineRenderer> pool = new();
    private int poolCursor;

    private readonly List<Unit> unitBuffer = new(128);

    private Building[] cachedBuildings;
    private float buildingsCacheTime;
    private BuildZone[] cachedBuildZones;
    private float buildZoneCacheTime;
    private readonly Dictionary<int, BoxCollider> buildZoneColliderCache = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    private void Start()
    {
        var go = new GameObject("DebugOverlayPool");
        go.transform.SetParent(transform, false);
        go.hideFlags = HideFlags.DontSave;
        poolParent = go.transform;

        // Sprites/Default handles alpha blending and works in both URP and
        // built-in pipelines without any setup. It's the most portable
        // material for debug lines.
        var shader = Shader.Find("Sprites/Default");
        lineMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    private void LateUpdate()
    {
        poolCursor = 0;
        RefreshUnitBuffer();

        // Order matters for visual stacking: bottom-up.
        if (showBuildZones) DrawBuildZones();
        if (showBuildingFootprints) DrawBuildingFootprints();
        if (showPaths) DrawUnitPaths();
        if (showAggroRange) DrawAggroRange();
        if (showAttackRange) DrawAttackRange();
        if (showUnitRadius) DrawUnitRadius();
        if (showVelocities) DrawVelocities();
        if (showAttackSlots) DrawAttackSlots();

        // Disable any LineRenderers we didn't use this frame.
        for (int i = poolCursor; i < pool.Count; i++)
            if (pool[i] != null) pool[i].enabled = false;
    }

    private void RefreshUnitBuffer()
    {
        unitBuffer.Clear();
        if (UnitManager.Instance == null) return;
        for (int team = 0; team <= 1; team++)
        {
            var teamUnits = UnitManager.Instance.GetTeamUnits(team);
            if (teamUnits == null) continue;
            for (int i = 0; i < teamUnits.Count; i++)
                unitBuffer.Add(teamUnits[i]);
        }
    }

    // ================================================================
    //  POOL / PRIMITIVES
    // ================================================================

    private LineRenderer GetLine(float width)
    {
        while (poolCursor < pool.Count && pool[poolCursor] == null)
            pool.RemoveAt(poolCursor);

        if (poolCursor >= pool.Count)
        {
            var go = new GameObject($"DbgLine_{pool.Count}");
            go.transform.SetParent(poolParent, false);
            go.hideFlags = HideFlags.DontSave;
            var lr = go.AddComponent<LineRenderer>();
            lr.material = lineMat;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 0;
            lr.numCornerVertices = 0;
            lr.alignment = LineAlignment.View; // always face camera for readable thin lines
            lr.generateLightingData = false;
            pool.Add(lr);
        }
        var line = pool[poolCursor++];
        line.enabled = true;
        line.startWidth = line.endWidth = width;
        line.loop = false;
        return line;
    }

    private void DrawLine(Vector3 a, Vector3 b, Color color, float width = 0.08f)
    {
        var lr = GetLine(width);
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        lr.startColor = lr.endColor = color;
    }

    private void DrawCircle(Vector3 center, float radius, Color color, int segments = 24, float width = 0.06f)
    {
        var lr = GetLine(width);
        lr.positionCount = segments + 1;
        lr.startColor = lr.endColor = color;
        float step = Mathf.PI * 2f / segments;
        for (int i = 0; i <= segments; i++)
        {
            float a = step * i;
            lr.SetPosition(i, new Vector3(
                center.x + Mathf.Cos(a) * radius,
                center.y,
                center.z + Mathf.Sin(a) * radius));
        }
    }

    private void DrawBoundsRect(Bounds b, float y, Color color, float width = 0.08f)
    {
        var lr = GetLine(width);
        lr.positionCount = 5;
        lr.startColor = lr.endColor = color;
        lr.SetPosition(0, new Vector3(b.min.x, y, b.min.z));
        lr.SetPosition(1, new Vector3(b.max.x, y, b.min.z));
        lr.SetPosition(2, new Vector3(b.max.x, y, b.max.z));
        lr.SetPosition(3, new Vector3(b.min.x, y, b.max.z));
        lr.SetPosition(4, new Vector3(b.min.x, y, b.min.z));
    }

    private void DrawDiamond(Vector3 center, float size, Color color, float width = 0.05f)
    {
        var lr = GetLine(width);
        lr.positionCount = 5;
        lr.startColor = lr.endColor = color;
        lr.SetPosition(0, center + new Vector3(0, 0, size));
        lr.SetPosition(1, center + new Vector3(size, 0, 0));
        lr.SetPosition(2, center + new Vector3(0, 0, -size));
        lr.SetPosition(3, center + new Vector3(-size, 0, 0));
        lr.SetPosition(4, center + new Vector3(0, 0, size));
    }

    // ================================================================
    //  DRAW METHODS
    // ================================================================

    private void DrawUnitPaths()
    {
        float y = Y_OFFSET;
        for (int u = 0; u < unitBuffer.Count; u++)
        {
            var unit = unitBuffer[u];
            if (unit == null) continue;
            var mov = unit.Movement;
            if (mov == null || !mov.WorldTarget.HasValue) continue;

            Vector3 dest = mov.WorldTarget.Value;
            dest.y = y;
            Vector3 pos = unit.transform.position;
            pos.y = y;

            Color lineCol = unit.TeamId == 0
                ? new Color(0.2f, 0.9f, 0.5f, 0.6f)
                : new Color(0.9f, 0.5f, 0.2f, 0.6f);
            DrawLine(pos, dest, lineCol);

            Color diamondCol = mov.IsDestinationUnreachable
                ? new Color(1f, 0.2f, 0.2f, 1f)
                : new Color(1f, 1f, 0.3f, 1f);
            DrawDiamond(dest, 0.35f, diamondCol);
        }
    }

    private void DrawVelocities()
    {
        float y = Y_OFFSET + 0.05f;
        for (int u = 0; u < unitBuffer.Count; u++)
        {
            var unit = unitBuffer[u];
            if (unit == null) continue;
            var mov = unit.Movement;
            if (mov == null || !mov.IsMoving) continue;

            Vector3 pos = unit.transform.position;
            pos.y = y;
            Vector3 vel = pos - mov.PreviousPosition;
            vel.y = 0f;
            if (vel.sqrMagnitude < 0.0001f) continue;

            float speed = vel.magnitude / Mathf.Max(Time.deltaTime, 0.001f);
            float speedNorm = Mathf.Clamp01(speed / 5f);
            Color c = new Color(1f - speedNorm, speedNorm, 0.3f, 1f);

            Vector3 dir = vel.normalized;
            Vector3 tip = pos + dir * 1.5f;
            DrawLine(pos, tip, c, 0.09f);

            Vector3 right = Vector3.Cross(Vector3.up, dir) * 0.25f;
            Vector3 arrowBase = tip - dir * 0.4f;
            DrawLine(tip, arrowBase + right, c, 0.09f);
            DrawLine(tip, arrowBase - right, c, 0.09f);
        }
    }

    private void DrawAttackRange()
    {
        float y = Y_OFFSET + 0.04f;
        for (int u = 0; u < unitBuffer.Count; u++)
        {
            var unit = unitBuffer[u];
            if (unit == null || unit.IsDead || unit.Data == null) continue;
            float totalRange = unit.EffectiveRadius + unit.Data.attackRange;
            if (totalRange <= 0f) continue;

            Color c = unit.TeamId == 0
                ? new Color(0.3f, 0.8f, 1f, 0.55f)
                : new Color(1f, 0.8f, 0.2f, 0.55f);
            Vector3 center = unit.transform.position;
            center.y = y;
            DrawCircle(center, totalRange, c, 28, 0.04f);
        }
    }

    /// <summary>
    /// Draw each unit's unified physical radius — Unit.EffectiveRadius,
    /// which equals its RVO/collision radius and its visual body half-extent.
    /// This is THE ONE physical radius that governs both crowd avoidance
    /// and slot spacing. White circle, team-tinted stroke.
    /// </summary>
    private void DrawUnitRadius()
    {
        float y = Y_OFFSET + 0.07f;
        for (int u = 0; u < unitBuffer.Count; u++)
        {
            var unit = unitBuffer[u];
            if (unit == null || unit.IsDead) continue;
            float r = unit.EffectiveRadius;
            if (r <= 0f) continue;
            Color c = unit.TeamId == 0
                ? new Color(0.4f, 0.9f, 1f, 0.95f)
                : new Color(1f, 0.6f, 0.4f, 0.95f);
            if (unit.Movement != null && !unit.Movement.IsMoving)
                c = new Color(1f, 1f, 1f, 1f); // bright white when locked
            Vector3 center = unit.transform.position;
            center.y = y;
            DrawCircle(center, r, c, 24, 0.07f);
        }
    }

    /// <summary>
    /// Draw each unit's aggro scan radius (UnitData.aggroRadius) — the
    /// distance it searches for enemies. Drawn as a translucent orange
    /// ring so it's distinct from the attack range and unit radius.
    /// </summary>
    private void DrawAggroRange()
    {
        float y = Y_OFFSET + 0.045f;
        for (int u = 0; u < unitBuffer.Count; u++)
        {
            var unit = unitBuffer[u];
            if (unit == null || unit.IsDead || unit.Data == null) continue;
            float r = unit.Data.aggroRadius;
            if (r <= 0f) continue;
            Color c = unit.TeamId == 0
                ? new Color(0.3f, 0.9f, 0.6f, 0.35f)
                : new Color(1f, 0.6f, 0.2f, 0.35f);
            Vector3 center = unit.transform.position;
            center.y = y;
            DrawCircle(center, r, c, 32, 0.04f);
        }
    }

    private void DrawBuildingFootprints()
    {
        if (cachedBuildings == null || Time.time - buildingsCacheTime > 2f)
        {
            cachedBuildings = Object.FindObjectsByType<Building>(FindObjectsSortMode.None);
            buildingsCacheTime = Time.time;
        }

        float y = Y_OFFSET + 0.03f;

        if (cachedBuildings != null)
        {
            foreach (var b in cachedBuildings)
            {
                if (b == null) continue;
                var col = b.GetComponent<BoxCollider>();
                if (col == null) continue;
                Color c = b.TeamId == 0
                    ? new Color(0.2f, 0.6f, 1f, 0.9f)
                    : new Color(1f, 0.35f, 0.2f, 0.9f);
                DrawBoundsRect(col.bounds, y, c);
            }
        }

        // Castles — thicker lines with a diagonal cross
        var castles = Object.FindObjectsByType<Castle>(FindObjectsSortMode.None);
        foreach (var castle in castles)
        {
            if (castle == null) continue;
            var col = castle.GetComponent<BoxCollider>();
            if (col == null) continue;
            Color color = castle.TeamId == 0
                ? new Color(0.1f, 0.5f, 1f, 1f)
                : new Color(1f, 0.2f, 0.1f, 1f);
            DrawBoundsRect(col.bounds, y, color, 0.15f);
            DrawLine(
                new Vector3(col.bounds.min.x, y, col.bounds.min.z),
                new Vector3(col.bounds.max.x, y, col.bounds.max.z),
                color, 0.1f);
            DrawLine(
                new Vector3(col.bounds.max.x, y, col.bounds.min.z),
                new Vector3(col.bounds.min.x, y, col.bounds.max.z),
                color, 0.1f);
        }
    }

    private void DrawBuildZones()
    {
        if (cachedBuildZones == null || Time.time - buildZoneCacheTime > 5f)
        {
            cachedBuildZones = Object.FindObjectsByType<BuildZone>(FindObjectsSortMode.None);
            buildZoneCacheTime = Time.time;
            buildZoneColliderCache.Clear();
        }
        if (cachedBuildZones == null) return;

        float y = Y_OFFSET;
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

            Color c = zone.TeamId == 0
                ? new Color(0.2f, 0.5f, 1f, 0.6f)
                : new Color(1f, 0.3f, 0.3f, 0.6f);
            DrawBoundsRect(col.bounds, y, c);
        }
    }

    /// <summary>
    /// Show the attack position each unit has claimed (via UnitCombat.AttackPosition).
    /// Yellow diamond = claimed, still moving to it. Green diamond = actively
    /// attacking from that slot. Thin line from unit to its claimed slot so
    /// you can see which attacker is going where.
    /// </summary>
    private void DrawAttackSlots()
    {
        float y = Y_OFFSET + 0.08f;
        for (int u = 0; u < unitBuffer.Count; u++)
        {
            var unit = unitBuffer[u];
            if (unit == null || unit.IsDead) continue;
            var cmb = unit.Combat;
            if (cmb == null || !cmb.AttackPosition.HasValue) continue;

            Vector3 slot = cmb.AttackPosition.Value;
            slot.y = y;

            Color c = cmb.IsAttacking
                ? new Color(0.3f, 1f, 0.3f, 1f)    // green — locked in and swinging
                : new Color(1f, 0.9f, 0.3f, 1f);   // yellow — claim in flight

            DrawDiamond(slot, 0.35f, c, 0.07f);

            Vector3 pos = unit.transform.position;
            pos.y = y;
            Color lineCol = new Color(c.r, c.g, c.b, 0.5f);
            DrawLine(pos, slot, lineCol, 0.04f);
        }
    }
}
