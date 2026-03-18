using UnityEngine;
using System.Collections.Generic;

public class PathfindingDiagnostic : MonoBehaviour
{
    public static PathfindingDiagnostic Instance { get; private set; }

    private float reportTimer;
    private const float REPORT_INTERVAL = 5f;
    private float stuckCheckTimer;
    private const float STUCK_CHECK_INTERVAL = 2f;
    private float detailTimer;
    private const float DETAIL_INTERVAL = 8f;

    private readonly Dictionary<int, Vector3> lastPositions = new();
    private readonly Dictionary<int, float> stuckDurations = new();
    private readonly Dictionary<int, Vector3> lastVelocities = new();
    private readonly HashSet<int> livingIdBuffer = new();

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
    private static void ResetStatics()
    {
        Instance = null;
    }

    private void Update()
    {
        if (UnitManager.Instance == null) return;

        stuckCheckTimer -= Time.deltaTime;
        if (stuckCheckTimer <= 0f)
        {
            stuckCheckTimer = STUCK_CHECK_INTERVAL;
            CheckStuckUnits();
        }

        reportTimer -= Time.deltaTime;
        if (reportTimer <= 0f)
        {
            reportTimer = REPORT_INTERVAL;
            Report();
        }

        detailTimer -= Time.deltaTime;
        if (detailTimer <= 0f)
        {
            detailTimer = DETAIL_INTERVAL;
            DetailedUnitReport();
        }
    }

    private void CheckStuckUnits()
    {
        BuildLivingIdSet();

        for (int team = 0; team <= 1; team++)
        {
            var units = UnitManager.Instance.GetTeamUnits(team);
            if (units == null) continue;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null || unit.IsDead) continue;
                int id = unit.GetInstanceID();
                Vector3 pos = unit.transform.position;

                if (lastPositions.TryGetValue(id, out Vector3 lastPos))
                {
                    float moved = Vector3.Distance(pos, lastPos);
                    var movement = unit.Movement;
                    bool shouldBeMoving = movement != null && movement.IsMoving;

                    if (shouldBeMoving && moved < 0.1f)
                    {
                        stuckDurations.TryGetValue(id, out float dur);
                        float newDur = dur + STUCK_CHECK_INTERVAL;
                        stuckDurations[id] = newDur;

                        if (newDur >= 4f && (int)(dur / STUCK_CHECK_INTERVAL) % 3 == 0)
                        {
                            LogStuckUnit(unit, movement, pos, newDur);
                        }
                    }
                    else
                    {
                        stuckDurations.Remove(id);
                    }

                    Vector3 vel = (pos - lastPos) / STUCK_CHECK_INTERVAL;
                    lastVelocities[id] = vel;
                }

                lastPositions[id] = pos;
            }
        }

        CleanupDeadEntries();
    }

    private void LogStuckUnit(Unit unit, UnitMovement movement, Vector3 pos, float stuckDuration)
    {
        var sm = unit.StateMachine;
        string state = sm != null ? sm.CurrentState.ToString() : "?";
        string target = movement.WorldTarget.HasValue ? movement.WorldTarget.Value.ToString("F1") : "none";
        bool hasPath = movement.HasPath;
        int wpCount = movement.Waypoints?.Count ?? 0;
        int wpIdx = movement.WaypointIndex;

        var grid = GridSystem.Instance;
        string wpDetail = "";
        if (hasPath && movement.Waypoints != null && wpIdx < movement.Waypoints.Count)
        {
            Vector3 wp = movement.Waypoints[wpIdx];
            float distToWp = Vector3.Distance(pos, wp);
            bool wpWalkable = false;
            string cellStr = "?";
            if (grid != null)
            {
                Vector2Int cell = grid.WorldToCell(wp);
                wpWalkable = grid.IsInBounds(cell) && grid.IsWalkable(cell);
                cellStr = $"({cell.x},{cell.y})";
            }

            // Check if position-to-waypoint path crosses an unwalkable cell
            bool pathCrossesWall = false;
            if (grid != null)
            {
                Vector3 dir = wp - pos;
                float dist = dir.magnitude;
                if (dist > 0.1f)
                {
                    int steps = Mathf.CeilToInt(dist / (grid.CellSize * 0.5f));
                    for (int s = 1; s <= steps; s++)
                    {
                        Vector3 sample = pos + dir * (s / (float)steps);
                        Vector2Int sc = grid.WorldToCell(sample);
                        if (!grid.IsInBounds(sc) || !grid.IsWalkable(sc))
                        {
                            pathCrossesWall = true;
                            break;
                        }
                    }
                }
            }

            wpDetail = $" wp[{wpIdx}]={wp:F1} distToWp={distToWp:F1} wpCell={cellStr} wpWalkable={wpWalkable} pathCrossesWall={pathCrossesWall}";
        }

        int arrives = movement.ArriveCount;
        Debug.LogWarning($"[PathDiag] STUCK unit={unit.name} t{unit.TeamId} " +
            $"state={state} pos={pos:F1} target={target} " +
            $"hasPath={hasPath} wpts={wpCount}{wpDetail} " +
            $"radius={unit.EffectiveRadius:F2} arrives={arrives} stuckFor={stuckDuration:F0}s");
    }

    private void BuildLivingIdSet()
    {
        livingIdBuffer.Clear();
        for (int team = 0; team <= 1; team++)
        {
            var units = UnitManager.Instance.GetTeamUnits(team);
            if (units == null) continue;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u != null && !u.IsDead)
                    livingIdBuffer.Add(u.GetInstanceID());
            }
        }
    }

    private void CleanupDeadEntries()
    {
        var toRemove = new List<int>();
        foreach (var kvp in lastPositions)
        {
            if (!livingIdBuffer.Contains(kvp.Key))
                toRemove.Add(kvp.Key);
        }
        for (int i = 0; i < toRemove.Count; i++)
        {
            int id = toRemove[i];
            lastPositions.Remove(id);
            stuckDurations.Remove(id);
            lastVelocities.Remove(id);
        }
    }

    private void Report()
    {
        int totalUnits = 0;
        int movingUnits = 0;
        int stuckUnits = 0;
        int idleUnits = 0;
        int fightingUnits = 0;
        int unreachableUnits = 0;
        float avgSpeed = 0;
        int speedSamples = 0;

        for (int team = 0; team <= 1; team++)
        {
            var units = UnitManager.Instance.GetTeamUnits(team);
            if (units == null) continue;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null || unit.IsDead) continue;
                totalUnits++;

                var movement = unit.Movement;
                var sm = unit.StateMachine;

                if (sm != null && sm.CurrentState == UnitState.Fighting)
                    fightingUnits++;
                else if (movement != null && movement.IsMoving)
                    movingUnits++;
                else
                    idleUnits++;

                if (movement != null && movement.IsDestinationUnreachable)
                    unreachableUnits++;

                int id = unit.GetInstanceID();
                if (stuckDurations.TryGetValue(id, out float dur) && dur >= 4f)
                    stuckUnits++;

                if (lastVelocities.TryGetValue(id, out Vector3 vel))
                {
                    float spd = new Vector2(vel.x, vel.z).magnitude;
                    if (movement != null && movement.IsMoving)
                    {
                        avgSpeed += spd;
                        speedSamples++;
                    }
                }
            }
        }

        // Overlap check using spatial hash instead of O(n^2)
        int overlappingPairs = 0;
        int severeOverlaps = 0;
        if (UnitManager.Instance.SpatialHash != null)
        {
            for (int team = 0; team <= 1; team++)
            {
                var units = UnitManager.Instance.GetTeamUnits(team);
                if (units == null) continue;
                for (int i = 0; i < units.Count; i++)
                {
                    var unit = units[i];
                    if (unit == null || unit.IsDead) continue;

                    float r = unit.EffectiveRadius;
                    var nearby = UnitManager.Instance.GetUnitsInRadius(unit.transform.position, r * 2f);
                    for (int j = 0; j < nearby.Count; j++)
                    {
                        var other = nearby[j];
                        if (other == null || other == unit || other.IsDead) continue;
                        if (other.GetInstanceID() <= unit.GetInstanceID()) continue;

                        float d = Vector3.Distance(unit.transform.position, other.transform.position);
                        float combinedR = r + other.EffectiveRadius;
                        if (d < combinedR * 0.5f)
                        {
                            overlappingPairs++;
                            if (d < combinedR * 0.2f)
                                severeOverlaps++;
                        }
                    }
                }
            }
        }

        float avgSpeedVal = speedSamples > 0 ? avgSpeed / speedSamples : 0;

        int navTris = 0;
        int walkableTris = 0;
        var pfm = PathfindingManager.Instance;
        if (pfm != null && pfm.ActiveNavMesh != null)
        {
            navTris = pfm.ActiveNavMesh.TriangleCount;
            for (int i = 0; i < navTris; i++)
                if (pfm.ActiveNavMesh.Triangles[i].IsWalkable) walkableTris++;
        }

        Debug.Log($"[PathDiag] units={totalUnits} move={movingUnits} fight={fightingUnits} idle={idleUnits}" +
                  $" | stuck={stuckUnits} unreach={unreachableUnits}" +
                  $" | overlaps={overlappingPairs} severe={severeOverlaps}" +
                  $" | avgSpd={avgSpeedVal:F2}" +
                  $" | navmesh: tris={navTris} walkable={walkableTris}");

        // Deep A* / pathfinding stats
        int req = NavMeshPathfinder.StatPathsRequested;
        int ok = NavMeshPathfinder.StatPathsSucceeded;
        int fail = NavMeshPathfinder.StatPathsFailed;
        int failOutside = NavMeshPathfinder.StatFailedOutsideMesh;
        int failAStar = NavMeshPathfinder.StatFailedAStarNoPath;
        int failBldCross = NavMeshPathfinder.StatFailedBuildingCross;
        int throttled = NavMeshPathfinder.StatThrottled;
        int astarNodes = NavMeshPathfinder.StatTotalAStarNodes;
        int widthReject = NavMeshPathfinder.StatTotalWidthRejections;
        int chanLen = NavMeshPathfinder.StatTotalChannelLength;
        int funnelWpts = NavMeshPathfinder.StatTotalFunnelWaypoints;
        int maxChan = NavMeshPathfinder.StatMaxChannelLength;
        float worstRatio = NavMeshPathfinder.StatWorstPathRatio;

        float avgChan = ok > 0 ? (float)chanLen / ok : 0;
        float avgWpts = ok > 0 ? (float)funnelWpts / ok : 0;
        float avgNodes = ok > 0 ? (float)astarNodes / ok : 0;

        Debug.Log($"[PathDiag:DEEP] paths req={req} ok={ok} fail={fail}(outside={failOutside} astar={failAStar} bldCross={failBldCross}) throttled={throttled}" +
                  $" | A* avgNodes={avgNodes:F0} widthReject={widthReject}" +
                  $" | channel avg={avgChan:F1} max={maxChan}" +
                  $" | funnel avgWpts={avgWpts:F1} worstRatio={worstRatio:F2}");

        if (fail > 0 && req > 0)
        {
            float failPct = 100f * fail / req;
            if (failPct > 50f)
                Debug.LogError($"[PathDiag] CRITICAL: {failPct:F0}% path failure rate ({fail}/{req})! outside={failOutside} astar={failAStar} bldCross={failBldCross}");
            else if (failPct > 20f)
                Debug.LogWarning($"[PathDiag] HIGH path failure rate: {failPct:F0}% ({fail}/{req}) outside={failOutside} astar={failAStar} bldCross={failBldCross}");
        }

        NavMeshPathfinder.ResetStats();

        // Boids stats
        if (pfm != null && pfm.Boids != null)
        {
            var boids = pfm.Boids;
            int boidsCalls = boids.StatBoidsCallCount;
            int overridden = boids.StatOverriddenByBoids;
            int densStops = boids.StatDensityStopCount;
            float maxSteer = boids.StatMaxSteeringMagnitude;
            float overridePct = boidsCalls > 0 ? (100f * overridden / boidsCalls) : 0f;

            Debug.Log($"[PathDiag:BOIDS] calls={boidsCalls} overridden={overridden}({overridePct:F1}%)" +
                      $" densityStops={densStops} maxSteer={maxSteer:F2}");
            boids.ResetStats();
        }

        // AttackPosition slot stats
        ReportAttackPositionSlots();
    }

    private void ReportAttackPositionSlots()
    {
        var (targets, totalSlots, maxPerTarget) = AttackPositionFinder.GetSlotStats();
        if (totalSlots > 0)
            Debug.Log($"[PathDiag:SLOTS] targets={targets} totalSlots={totalSlots} maxPerTarget={maxPerTarget}");
    }

    private void DetailedUnitReport()
    {
        int worstStuckId = -1;
        float worstStuckTime = 0;

        foreach (var kvp in stuckDurations)
        {
            if (kvp.Value > worstStuckTime)
            {
                worstStuckTime = kvp.Value;
                worstStuckId = kvp.Key;
            }
        }

        if (worstStuckTime < 4f) return;

        for (int team = 0; team <= 1; team++)
        {
            var units = UnitManager.Instance.GetTeamUnits(team);
            if (units == null) continue;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null || unit.GetInstanceID() != worstStuckId) continue;

                var movement = unit.Movement;
                var sm = unit.StateMachine;
                var combat = unit.Combat;

                string state = sm != null ? sm.CurrentState.ToString() : "?";
                string target = movement?.WorldTarget.HasValue == true ? movement.WorldTarget.Value.ToString("F1") : "none";
                bool hasPath = movement?.HasPath ?? false;
                int wpCount = movement?.Waypoints?.Count ?? 0;
                int wpIdx = movement?.WaypointIndex ?? 0;
                string combatTarget = combat?.AttackTarget != null ? combat.AttackTarget.name : "none";
                bool isMoving = movement?.IsMoving ?? false;

                Vector3 pos = unit.transform.position;
                var grid = GridSystem.Instance;
                bool onWalkable = grid != null && grid.IsWalkable(grid.WorldToCell(pos));

                // Check if position is inside NavMesh
                bool inNavMesh = false;
                var pfm = PathfindingManager.Instance;
                if (pfm != null && pfm.ActiveNavMesh != null)
                {
                    Vector2 pos2D = new Vector2(pos.x, pos.z);
                    int tri = pfm.ActiveNavMesh.FindTriangleAtPosition(pos2D);
                    inNavMesh = tri >= 0 && pfm.ActiveNavMesh.Triangles[tri].IsWalkable;
                }

                int nearbyCount = 0;
                int overlapping = 0;
                if (UnitManager.Instance != null)
                {
                    var nearby = UnitManager.Instance.GetUnitsInRadius(pos, unit.EffectiveRadius * 3f);
                    nearbyCount = nearby.Count - 1;
                    for (int j = 0; j < nearby.Count; j++)
                    {
                        var other = nearby[j];
                        if (other == unit) continue;
                        float d = Vector3.Distance(pos, other.transform.position);
                        if (d < unit.EffectiveRadius + other.EffectiveRadius)
                            overlapping++;
                    }
                }

                string wpInfo = "";
                if (hasPath && movement.Waypoints != null && wpIdx < movement.Waypoints.Count)
                {
                    Vector3 wp = movement.Waypoints[wpIdx];
                    float distToWp = Vector3.Distance(pos, wp);
                    Vector2Int wpCell = grid != null ? grid.WorldToCell(wp) : Vector2Int.zero;
                    bool wpWalkable = grid != null && grid.IsInBounds(wpCell) && grid.IsWalkable(wpCell);
                    wpInfo = $" wp[{wpIdx}]={wp:F2} distToWp={distToWp:F2} wpCell=({wpCell.x},{wpCell.y}) wpWalkable={wpWalkable}";
                }

                Debug.LogWarning($"[PathDiag] DETAIL worst stuck unit={unit.name} t{unit.TeamId} " +
                    $"state={state} pos={pos:F2} walkable={onWalkable} inNavMesh={inNavMesh} " +
                    $"target={target} isMoving={isMoving} hasPath={hasPath} " +
                    $"wpts={wpCount} wpIdx={wpIdx}{wpInfo} " +
                    $"combatTarget={combatTarget} " +
                    $"radius={unit.EffectiveRadius:F2} nearby={nearbyCount} overlapping={overlapping} " +
                    $"stuckFor={worstStuckTime:F0}s");
                return;
            }
        }
    }
}
