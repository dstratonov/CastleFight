using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Orchestrates the two-layer SC2-style pathfinding system.
/// Layer 1: NavMesh (CDT) + A* + Funnel + Vertex Expansion — terrain/buildings only.
/// Layer 2: Boids steering — unit-to-unit avoidance only.
/// These two layers are completely separate. They share no state.
/// </summary>
public class PathfindingManager : MonoBehaviour
{
    public static PathfindingManager Instance { get; private set; }

    private NavMeshBuilder navMeshBuilder;
    private BoidsManager boidsManager;
    private GridSystem grid;

    private bool isInitialized;
    private bool initRequested;
    private float initDelay;
    private int pathRequestsThisFrame;
    private const int MaxPathRequestsPerFrame = 20;
    private bool pendingRebuild;
    private Bounds? pendingChangeBounds;

    // SC2-style group path cache: units heading to similar destinations share one A* result.
    // Keyed by (goalCell, radiusBucket). Cleared every frame.
    private readonly Dictionary<(Vector2Int goal, int radiusBucket), List<Vector3>> groupPathCache = new();
    private const float GroupPathMaxStartDist = 8f; // max distance between starts to share a path
    public int StatGroupPathHits;

    // #20: Spread replans over multiple frames
    private readonly HashSet<UnitMovement> pendingReplanSet = new();
    private readonly List<UnitMovement> pendingReplans = new();
    private const int MaxReplansPerFrame = 10;

    public NavMeshBuilder NavMeshBuilder => navMeshBuilder;
    public NavMeshData ActiveNavMesh => navMeshBuilder?.ActiveNavMesh;
    public BoidsManager Boids => boidsManager;
    public bool IsInitialized => isInitialized;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
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

    private void OnEnable()
    {
        EventBus.Subscribe<BuildingPlacedEvent>(OnBuildingPlaced);
        EventBus.Subscribe<BuildingDestroyedEvent>(OnBuildingDestroyed);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<BuildingPlacedEvent>(OnBuildingPlaced);
        EventBus.Unsubscribe<BuildingDestroyedEvent>(OnBuildingDestroyed);
    }

    public void RequestInitialize()
    {
        if (GameDebug.Pathfinding)
            Debug.Log("[PathfindingManager] Initialize requested, will build in 0.5s");
        initRequested = true;
        initDelay = 0.5f;
    }

    public void Initialize()
    {
        grid = GridSystem.Instance;
        if (grid == null)
        {
            Debug.LogError("[PathfindingManager] GridSystem not found!");
            return;
        }

        // Build NavMesh from grid (Layer 1)
        navMeshBuilder = new NavMeshBuilder();
        RegisterExistingObstacles();
        navMeshBuilder.BuildBase(grid);

        // Initialize Boids (Layer 2) — UnitManager is a required dependency
        var spatialHash = UnitManager.Instance?.SpatialHash;
        if (spatialHash == null)
        {
            Debug.LogError("[PathfindingManager] UnitManager.SpatialHash not available — delaying initialization");
            isInitialized = false;
            initRequested = true;
            initDelay = 0.5f;
            return;
        }
        boidsManager = new BoidsManager(spatialHash);

        isInitialized = true;

        var mesh = navMeshBuilder.ActiveNavMesh;
        int walkableTris = 0;
        for (int i = 0; i < mesh.TriangleCount; i++)
            if (mesh.Triangles[i].IsWalkable) walkableTris++;

        Debug.Log($"[PathfindingManager] Initialized: {mesh.TriangleCount} triangles ({walkableTris} walkable), {mesh.VertexCount} vertices");

        mesh.ValidateMesh();

        if (gameObject.GetComponent<PathfindingDiagnostic>() == null)
            gameObject.AddComponent<PathfindingDiagnostic>();
        if (gameObject.GetComponent<PathfindingDebugToggle>() == null)
            gameObject.AddComponent<PathfindingDebugToggle>();
        if (gameObject.GetComponent<PathfindingStressTest>() == null)
            gameObject.AddComponent<PathfindingStressTest>();
    }

    private void Update()
    {
        if (!isInitialized)
        {
            if (initRequested)
            {
                initDelay -= Time.deltaTime;
                if (initDelay <= 0f)
                {
                    initRequested = false;
                    Initialize();
                }
            }
            return;
        }

        pathRequestsThisFrame = 0;
        groupPathCache.Clear();
        AttackPositionFinder.CleanupStaleSlots();

        if (navMeshBuilder != null && navMeshBuilder.TryApplyAsyncResult())
        {
            OnAsyncRebuildComplete();
        }

        if (pendingRebuild && !navMeshBuilder.IsRebuilding)
        {
            pendingRebuild = false;
            ExecuteDeferredRebuild();
        }

        ProcessPendingReplans();
    }

    // ================================================================
    //  PATH REQUESTS (Layer 1)
    // ================================================================

    /// <summary>
    /// True if the last RequestPath call was throttled (frame budget exceeded).
    /// Check this to distinguish a throttled null from a genuine pathfinding failure.
    /// </summary>
    public bool LastRequestWasThrottled { get; private set; }

    /// <summary>
    /// Request a path from startWorld to goalWorld for a unit with the given radius.
    /// Returns waypoints in world coordinates, or null if no path.
    /// Capped at MaxPathRequestsPerFrame per frame.
    /// </summary>
    public List<Vector3> RequestPath(Vector3 startWorld, Vector3 goalWorld, float unitRadius)
    {
        LastRequestWasThrottled = false;

        if (!isInitialized || navMeshBuilder == null)
        {
            if (GameDebug.Pathfinding)
                Debug.LogWarning("[PathfindingManager] RequestPath called before initialization");
            return null;
        }

        // SC2-style group path sharing: check cache before running A*.
        // Units heading to the same destination cell with similar radius reuse
        // the leader's path, trimmed to their start position.
        var cacheKey = GetGroupPathKey(goalWorld, unitRadius);
        if (groupPathCache.TryGetValue(cacheKey, out var cachedPath) && cachedPath.Count > 1)
        {
            var shared = TrimSharedPath(cachedPath, startWorld);
            if (shared != null)
            {
                StatGroupPathHits++;
                return shared;
            }
        }

        if (pathRequestsThisFrame >= MaxPathRequestsPerFrame)
        {
            NavMeshPathfinder.StatThrottled++;
            LastRequestWasThrottled = true;
            if (GameDebug.Pathfinding)
                Debug.Log($"[PathfindingManager] Path request throttled ({pathRequestsThisFrame}/{MaxPathRequestsPerFrame})");
            return null;
        }
        pathRequestsThisFrame++;

        Vector2 start2D = navMeshBuilder.WorldToNavMesh(startWorld);
        Vector2 goal2D = navMeshBuilder.WorldToNavMesh(goalWorld);

        var waypoints2D = NavMeshPathfinder.FindPath(navMeshBuilder.ActiveNavMesh, start2D, goal2D, unitRadius);
        if (waypoints2D == null)
        {
            if (GameDebug.Pathfinding)
                Debug.Log($"[PathfindingManager] Path not found: ({startWorld.x:F1},{startWorld.z:F1})->({goalWorld.x:F1},{goalWorld.z:F1}) r={unitRadius:F2}");
            return null;
        }

        if (navMeshBuilder.PathCrossesAnyBuilding(waypoints2D))
        {
            NavMeshPathfinder.StatPathsFailed++;
            NavMeshPathfinder.StatFailedBuildingCross++;
            Debug.LogWarning($"[PathfindingManager] Path rejected — crosses pending building: " +
                $"({startWorld.x:F1},{startWorld.z:F1})->({goalWorld.x:F1},{goalWorld.z:F1})");
            return null;
        }

        var waypoints3D = new List<Vector3>(waypoints2D.Count);
        foreach (var wp in waypoints2D)
            waypoints3D.Add(navMeshBuilder.NavMeshToWorld(wp));

        // Cache for group sharing
        groupPathCache[cacheKey] = waypoints3D;

        return waypoints3D;
    }

    private (Vector2Int goal, int radiusBucket) GetGroupPathKey(Vector3 goalWorld, float unitRadius)
    {
        Vector2Int goalCell = grid.WorldToCell(goalWorld);
        int radiusBucket = Mathf.RoundToInt(unitRadius * 4f); // 0.25 granularity
        return (goalCell, radiusBucket);
    }

    /// <summary>
    /// Create a shared path from a cached leader path for a follower unit.
    /// Finds the nearest waypoint on the cached path and returns a trimmed copy
    /// starting from the follower's position to that waypoint onward.
    /// Returns null if the follower is too far from the cached path.
    /// </summary>
    private static List<Vector3> TrimSharedPath(List<Vector3> leaderPath, Vector3 followerStart)
    {
        // Find the nearest waypoint on the leader's path
        float bestDistSq = float.MaxValue;
        int bestIdx = -1;
        for (int i = 0; i < leaderPath.Count; i++)
        {
            float dSq = (leaderPath[i] - followerStart).sqrMagnitude;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestIdx = i;
            }
        }

        if (bestDistSq > GroupPathMaxStartDist * GroupPathMaxStartDist)
            return null; // too far from any waypoint — compute fresh path

        // Build trimmed path: follower start -> nearest waypoint onward
        int remaining = leaderPath.Count - bestIdx;
        var result = new List<Vector3>(remaining + 1);
        result.Add(followerStart);
        for (int i = bestIdx; i < leaderPath.Count; i++)
            result.Add(leaderPath[i]);

        return result;
    }

    /// <summary>
    /// Returns true if a path request slot is available this frame.
    /// </summary>
    public bool TryConsumePathRequest()
    {
        if (pathRequestsThisFrame >= MaxPathRequestsPerFrame)
            return false;
        pathRequestsThisFrame++;
        return true;
    }

    // ================================================================
    //  BOIDS STEERING (Layer 2)
    // ================================================================

    /// <summary>
    /// Compute Boids steering for a unit. Layer 2 — unit-to-unit avoidance only.
    /// </summary>
    public Vector3 ComputeSteering(IPathfindingAgent agent, Vector3 desiredVelocity, float maxSpeed, bool isMarching = true)
    {
        Debug.Assert(isInitialized, "[PathfindingManager] ComputeSteering called before initialization");
        Debug.Assert(boidsManager != null, "[PathfindingManager] ComputeSteering: boidsManager is null");
        return boidsManager.ComputeSteering(agent, desiredVelocity, maxSpeed, isMarching);
    }

    /// <summary>
    /// Check if a unit should stop due to density at destination.
    /// </summary>
    public bool ShouldDensityStop(IPathfindingAgent agent, Vector3 destination)
    {
        Debug.Assert(isInitialized, "[PathfindingManager] ShouldDensityStop called before initialization");
        Debug.Assert(boidsManager != null, "[PathfindingManager] ShouldDensityStop: boidsManager is null");
        return boidsManager.ShouldDensityStop(agent, destination);
    }

    /// <summary>
    /// Compute separation-only push for a stopped/idle unit to resolve overlap.
    /// </summary>
    public Vector3 ComputeSeparationPush(IPathfindingAgent agent, float deltaTime)
    {
        Debug.Assert(isInitialized, "[PathfindingManager] ComputeSeparationPush called before initialization");
        Debug.Assert(boidsManager != null, "[PathfindingManager] ComputeSeparationPush: boidsManager is null");
        return boidsManager.ComputeSeparationPush(agent, deltaTime);
    }

    // ================================================================
    //  OBSTACLE REGISTRATION
    // ================================================================

    /// <summary>
    /// Register bounds for all pre-placed obstacles (castles, pre-built buildings)
    /// so the NavMeshBuilder uses precise bounds instead of inflated grid cells.
    /// </summary>
    private void RegisterExistingObstacles()
    {
        foreach (var castle in GameRegistry.Castles)
        {
            if (castle == null) continue;
            Bounds b = BoundsHelper.GetPhysicalBounds(castle.gameObject);
            navMeshBuilder.RegisterBuilding(castle.gameObject.GetInstanceID(), b);
        }

        var buildingMgr = BuildingManager.Instance;
        if (buildingMgr != null)
        {
            for (int team = 0; team <= 1; team++)
            {
                foreach (var building in buildingMgr.GetTeamBuildings(team))
                {
                    if (building == null) continue;
                    Bounds b = BoundsHelper.GetPhysicalBounds(building.gameObject);
                    navMeshBuilder.RegisterBuilding(building.gameObject.GetInstanceID(), b);
                }
            }
        }
    }

    // ================================================================
    //  MAP CHANGE HANDLERS
    // ================================================================

    private void OnBuildingPlaced(BuildingPlacedEvent evt)
    {
        if (!isInitialized) return;
        var building = evt.Building?.GetComponent<Building>();
        if (building == null) return;

        HandleBuildingChange(building, true);
    }

    private void OnBuildingDestroyed(BuildingDestroyedEvent evt)
    {
        if (!isInitialized) return;
        var building = evt.Building?.GetComponent<Building>();
        if (building == null) return;

        HandleBuildingChange(building, false);
    }

    /// <summary>
    /// Defers NavMesh rebuild to next Update. This ensures all event handlers
    /// (including BuildingManager clearing grid cells) have completed before
    /// we rebuild the NavMesh from grid state.
    ///
    /// When a building is PLACED: invalidates (clears) paths crossing the
    /// building so units stop immediately. Units are NOT flagged for replan
    /// yet — that happens in OnAsyncRebuildComplete after the new NavMesh is
    /// ready. This prevents units from replanning on a stale mesh and getting
    /// paths that go through the building.
    ///
    /// When a building is DESTROYED: flags units for replan immediately.
    /// The stale mesh is conservative (still has the building), so replanned
    /// paths safely go around the now-empty space. OnAsyncRebuildComplete
    /// will re-flag to produce optimal paths on the fresh mesh.
    /// </summary>
    private void HandleBuildingChange(Building building, bool placed)
    {
        if (grid == null || navMeshBuilder == null) return;

        Bounds buildingBounds = BoundsHelper.GetPhysicalBounds(building.gameObject);
        int instanceId = building.gameObject.GetInstanceID();

        if (placed)
            navMeshBuilder.RegisterBuilding(instanceId, buildingBounds);
        else
            navMeshBuilder.UnregisterBuilding(instanceId);

        if (pendingChangeBounds.HasValue)
            pendingChangeBounds = PathInvalidation.UnionBounds(pendingChangeBounds.Value, buildingBounds);
        else
            pendingChangeBounds = buildingBounds;

        if (placed)
            InvalidatePathsInRegion(buildingBounds);
        else
            FlagUnitsForReplan(buildingBounds);

        if (GameDebug.Pathfinding)
            Debug.Log($"[PathfindingManager] Building {(placed ? "placed" : "destroyed")}: {building.name} " +
                $"bounds={buildingBounds.center:F1} size={buildingBounds.size:F1} — deferring NavMesh rebuild to next frame");

        pendingRebuild = true;
    }

    private void ExecuteDeferredRebuild()
    {
        if (grid == null || navMeshBuilder == null) return;

        navMeshBuilder.RebuildAsync();

        if (GameDebug.Pathfinding)
            Debug.Log("[PathfindingManager] Async NavMesh rebuild dispatched to background thread");
    }

    /// <summary>
    /// Called when the background NavMesh rebuild completes and the new mesh is swapped in.
    /// Validates the mesh (debug only) and flags affected units for path replanning.
    /// </summary>
    private void OnAsyncRebuildComplete()
    {
        groupPathCache.Clear(); // stale paths may cross new buildings

        if (GameDebug.Pathfinding)
        {
            var mesh = navMeshBuilder.ActiveNavMesh;
            int walkableTris = 0;
            if (mesh != null)
            {
                for (int i = 0; i < mesh.TriangleCount; i++)
                    if (mesh.Triangles[i].IsWalkable) walkableTris++;
            }
            Debug.Log($"[PathfindingManager] Async NavMesh rebuild applied: " +
                $"{mesh?.TriangleCount ?? 0} tris ({walkableTris} walkable), {mesh?.VertexCount ?? 0} verts");
            mesh?.ValidateMesh();
        }

        if (pendingChangeBounds.HasValue)
        {
            FlagUnitsForReplan(pendingChangeBounds.Value);
            pendingChangeBounds = null;
        }
        else
        {
            FlagAllUnitsForReplan();
        }
    }

    /// <summary>
    /// Immediately clear paths of units whose path crosses the changed region.
    /// Units stop but keep their destination — they'll resume when
    /// OnAsyncRebuildComplete flags them for replan on the fresh mesh.
    /// </summary>
    private void InvalidatePathsInRegion(Bounds changedRegion)
    {
        if (UnitManager.Instance == null) return;

        int invalidated = 0;
        foreach (var unit in UnitManager.Instance.AllUnits)
        {
            if (unit == null || unit.IsDead) continue;
            var movement = unit.Movement;
            if (movement == null || !movement.HasPath) continue;

            if (movement.PathBounds.Intersects(changedRegion))
            {
                movement.InvalidatePath();
                invalidated++;
            }
        }

        if (GameDebug.Pathfinding)
            Debug.Log($"[PathfindingManager] Paths invalidated: {invalidated} units stopped " +
                $"(region center={changedRegion.center:F1} size={changedRegion.size:F1})");
    }

    /// <summary>
    /// Flag only units whose path AABB intersects the changed region.
    /// </summary>
    private void FlagUnitsForReplan(Bounds changedRegion)
    {
        if (UnitManager.Instance == null) return;

        int flagged = 0, skipped = 0;
        foreach (var unit in UnitManager.Instance.AllUnits)
        {
            if (unit == null || unit.IsDead) continue;
            var movement = unit.Movement;
            if (movement == null || !movement.HasPath) continue;

            if (movement.PathBounds.Intersects(changedRegion))
            {
                movement.FlagForReplan();
                flagged++;
            }
            else
            {
                skipped++;
            }
        }

        if (GameDebug.Pathfinding)
            Debug.Log($"[PathfindingManager] Selective replan: {flagged} flagged, {skipped} skipped " +
                $"(region center={changedRegion.center:F1} size={changedRegion.size:F1})");
    }

    /// <summary>
    /// #20: Spread all replans over multiple frames via the pendingReplans queue
    /// instead of flagging all units at once.
    /// </summary>
    private void FlagAllUnitsForReplan()
    {
        if (UnitManager.Instance == null) return;
        foreach (var unit in UnitManager.Instance.AllUnits)
        {
            if (unit == null || unit.IsDead) continue;
            var movement = unit.Movement;
            if (movement != null && movement.HasPath && pendingReplanSet.Add(movement))
                pendingReplans.Add(movement);
        }
    }

    private void ProcessPendingReplans()
    {
        int count = Mathf.Min(pendingReplans.Count, MaxReplansPerFrame);
        for (int i = 0; i < count; i++)
        {
            var movement = pendingReplans[i];
            if (movement != null && movement.gameObject != null && movement.HasPath)
                movement.FlagForReplan();
            pendingReplanSet.Remove(movement);
        }
        if (count > 0)
            pendingReplans.RemoveRange(0, count);
    }

    // ================================================================
    //  UTILITY
    // ================================================================

    /// <summary>
    /// Find the nearest walkable position on the NavMesh.
    /// </summary>
    public Vector3 FindNearestWalkable(Vector3 worldPos)
    {
        Debug.Assert(navMeshBuilder != null, "[PathfindingManager] FindNearestWalkable: navMeshBuilder is null");
        return navMeshBuilder.FindNearestWalkablePosition(worldPos);
    }
}
