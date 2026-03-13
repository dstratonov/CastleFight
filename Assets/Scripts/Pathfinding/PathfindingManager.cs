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
    private CostStampManager costStampManager;
    private GridSystem grid;

    private bool isInitialized;
    private bool initRequested;
    private float initDelay;
    private int pathRequestsThisFrame;
    private const int MaxPathRequestsPerFrame = 20;
    private float densityCostTimer;
    private bool pendingRebuild;

    public NavMeshBuilder NavMeshBuilder => navMeshBuilder;
    public NavMeshData ActiveNavMesh => navMeshBuilder?.ActiveNavMesh;
    public BoidsManager Boids => boidsManager;
    public CostStampManager CostStampManager => costStampManager;
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

        costStampManager = new CostStampManager();

        // Build NavMesh from grid (Layer 1)
        navMeshBuilder = new NavMeshBuilder();
        navMeshBuilder.BuildBase(grid);

        // Initialize Boids (Layer 2)
        var spatialHash = UnitManager.Instance?.SpatialHash;
        if (spatialHash == null)
        {
            spatialHash = new SpatialHashGrid(4f);
            Debug.LogWarning("[PathfindingManager] UnitManager not found, creating standalone SpatialHashGrid");
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
        costStampManager.Tick(Time.time);
        AttackPositionFinder.CleanupStaleSlots();

        if (pendingRebuild)
        {
            pendingRebuild = false;
            ExecuteDeferredRebuild();
        }

        // Update soft cost inflation every 0.5s (anti-ghosting)
        densityCostTimer -= Time.deltaTime;
        if (densityCostTimer <= 0f)
        {
            densityCostTimer = 0.5f;
            UpdateDensityCosts();
        }
    }

    // ================================================================
    //  PATH REQUESTS (Layer 1)
    // ================================================================

    /// <summary>
    /// Request a path from startWorld to goalWorld for a unit with the given radius.
    /// Returns waypoints in world coordinates, or null if no path.
    /// Capped at MaxPathRequestsPerFrame per frame.
    /// </summary>
    public List<Vector3> RequestPath(Vector3 startWorld, Vector3 goalWorld, float unitRadius)
    {
        if (!isInitialized || navMeshBuilder == null)
        {
            if (GameDebug.Pathfinding)
                Debug.LogWarning("[PathfindingManager] RequestPath called before initialization");
            return null;
        }

        if (pathRequestsThisFrame >= MaxPathRequestsPerFrame)
        {
            NavMeshPathfinder.StatThrottled++;
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
                Debug.LogWarning($"[PathfindingManager] Path FAILED: ({startWorld.x:F1},{startWorld.z:F1})->({goalWorld.x:F1},{goalWorld.z:F1}) r={unitRadius:F2}");
            return null;
        }

        var waypoints3D = new List<Vector3>(waypoints2D.Count);
        foreach (var wp in waypoints2D)
            waypoints3D.Add(navMeshBuilder.NavMeshToWorld(wp));

        return waypoints3D;
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

    /// <summary>
    /// Check if a 2D line segment (NavMesh coords: x=worldX, y=worldZ) is clear of buildings.
    /// </summary>
    public bool IsSegmentClear(Vector2 from, Vector2 to)
    {
        if (grid == null) return true;
        return IsSegmentClearOfBuildings(from, to);
    }

    private bool IsSegmentClearOfBuildings(Vector2 from, Vector2 to)
    {
        float dist = Vector2.Distance(from, to);
        float stepSize = grid.CellSize * 0.4f;
        int steps = Mathf.CeilToInt(dist / stepSize);
        if (steps < 2) steps = 2;

        for (int s = 0; s <= steps; s++)
        {
            float t = s / (float)steps;
            Vector2 p = Vector2.Lerp(from, to, t);
            Vector2Int cell = grid.WorldToCell(new Vector3(p.x, 0, p.y));
            if (!grid.IsInBounds(cell) || !grid.IsWalkable(cell))
                return false;
        }
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
        if (!isInitialized || boidsManager == null)
            return desiredVelocity;
        return boidsManager.ComputeSteering(agent, desiredVelocity, maxSpeed, isMarching);
    }

    /// <summary>
    /// Check if a unit should stop due to density at destination.
    /// </summary>
    public bool ShouldDensityStop(IPathfindingAgent agent, Vector3 destination)
    {
        if (!isInitialized || boidsManager == null)
            return false;
        return boidsManager.ShouldDensityStop(agent, destination);
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
    /// </summary>
    private void HandleBuildingChange(Building building, bool placed)
    {
        if (grid == null || navMeshBuilder == null) return;

        if (GameDebug.Pathfinding)
            Debug.Log($"[PathfindingManager] Building {(placed ? "placed" : "destroyed")}: {building.name} — deferring NavMesh rebuild to next frame");

        pendingRebuild = true;
    }

    private void ExecuteDeferredRebuild()
    {
        if (grid == null || navMeshBuilder == null) return;

        navMeshBuilder.Rebuild();

        if (GameDebug.Pathfinding)
        {
            Debug.Log("[PathfindingManager] Deferred NavMesh rebuild completed");
            navMeshBuilder.ActiveNavMesh?.ValidateMesh();
        }

        FlagUnitsForReplan();
    }

    /// <summary>
    /// Flag all units with active paths for replanning on their next tick.
    /// </summary>
    private void FlagUnitsForReplan()
    {
        if (UnitManager.Instance == null) return;
        foreach (var unit in UnitManager.Instance.AllUnits)
        {
            if (unit == null || unit.IsDead) continue;
            var movement = unit.Movement;
            if (movement != null && movement.HasPath)
                movement.FlagForReplan();
        }
    }

    private void UpdateDensityCosts()
    {
        if (navMeshBuilder?.ActiveNavMesh == null || UnitManager.Instance == null) return;
        NavMeshPathfinder.ApplyUnitDensityCosts(
            navMeshBuilder.ActiveNavMesh,
            UnitManager.Instance.AllUnits);
    }

    // ================================================================
    //  UTILITY
    // ================================================================

    /// <summary>
    /// Find the nearest walkable position on the NavMesh.
    /// </summary>
    public Vector3 FindNearestWalkable(Vector3 worldPos)
    {
        if (navMeshBuilder == null) return worldPos;
        return navMeshBuilder.FindNearestWalkablePosition(worldPos);
    }
}
