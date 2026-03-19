using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Grid-based A* pathfinding manager.
/// Buildings/terrain are obstacles on the grid. Units are NOT obstacles.
/// No steering, no boids — just pure A* on grid cells.
/// </summary>
public class PathfindingManager : MonoBehaviour
{
    public static PathfindingManager Instance { get; private set; }

    private GridSystem grid;

    private bool isInitialized;
    private bool initRequested;
    private float initDelay;
    private int pathRequestsThisFrame;
    private const int MaxPathRequestsPerFrame = 30;

    // Group path cache: units heading to same destination share one A* result.
    private readonly Dictionary<Vector2Int, List<Vector3>> groupPathCache = new();
    private const float GroupPathMaxStartDist = 8f;
    public int StatGroupPathHits;

    // Spread replans over multiple frames
    private readonly HashSet<UnitMovement> pendingReplanSet = new();
    private readonly List<UnitMovement> pendingReplans = new();
    private const int MaxReplansPerFrame = 15;

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

        // Wait for UnitManager (needed by combat scanning, not pathfinding itself)
        if (UnitManager.Instance == null)
        {
            isInitialized = false;
            initRequested = true;
            initDelay = 0.5f;
            return;
        }

        isInitialized = true;

        int walkableCells = 0;
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
                if (grid.IsWalkable(new Vector2Int(x, y))) walkableCells++;

        Debug.Log($"[PathfindingManager] Initialized: Grid {grid.Width}x{grid.Height}, " +
            $"cellSize={grid.CellSize}, walkable={walkableCells}/{grid.Width * grid.Height} cells");

        if (gameObject.GetComponent<PathfindingDiagnostic>() == null)
            gameObject.AddComponent<PathfindingDiagnostic>();
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
        ProcessPendingReplans();
    }

    // ================================================================
    //  PATH REQUESTS (Grid A*)
    // ================================================================

    public bool LastRequestWasThrottled { get; private set; }

    public List<Vector3> RequestPath(Vector3 startWorld, Vector3 goalWorld, float unitRadius)
    {
        LastRequestWasThrottled = false;

        if (!isInitialized || grid == null)
            return null;

        Vector2Int goalCell = grid.WorldToCell(goalWorld);
        if (groupPathCache.TryGetValue(goalCell, out var cachedPath) && cachedPath.Count > 1)
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
            GridAStar.StatThrottled++;
            LastRequestWasThrottled = true;
            return null;
        }
        pathRequestsThisFrame++;

        var path = GridAStar.FindPath(grid, startWorld, goalWorld);
        if (path != null && path.Count > 1)
            groupPathCache[goalCell] = path;

        return path;
    }

    public bool TryConsumePathRequest()
    {
        if (pathRequestsThisFrame >= MaxPathRequestsPerFrame)
            return false;
        pathRequestsThisFrame++;
        return true;
    }

    private static List<Vector3> TrimSharedPath(List<Vector3> leaderPath, Vector3 followerStart)
    {
        float bestDistSq = float.MaxValue;
        int bestIdx = -1;
        for (int i = 0; i < leaderPath.Count; i++)
        {
            float dSq = (leaderPath[i] - followerStart).sqrMagnitude;
            if (dSq < bestDistSq) { bestDistSq = dSq; bestIdx = i; }
        }
        if (bestDistSq > GroupPathMaxStartDist * GroupPathMaxStartDist)
            return null;

        var result = new List<Vector3>(leaderPath.Count - bestIdx + 1);
        result.Add(followerStart);
        for (int i = bestIdx; i < leaderPath.Count; i++)
            result.Add(leaderPath[i]);
        return result;
    }

    // ================================================================
    //  BUILDING CHANGE HANDLERS
    // ================================================================

    private void OnBuildingPlaced(BuildingPlacedEvent evt)
    {
        if (!isInitialized) return;
        var building = evt.Building?.GetComponent<Building>();
        if (building == null) return;

        Bounds buildingBounds = BoundsHelper.GetPhysicalBounds(building.gameObject);
        InvalidatePathsInRegion(buildingBounds);
    }

    private void OnBuildingDestroyed(BuildingDestroyedEvent evt)
    {
        if (!isInitialized) return;
        var building = evt.Building?.GetComponent<Building>();
        if (building == null) return;

        Bounds buildingBounds = BoundsHelper.GetPhysicalBounds(building.gameObject);
        FlagUnitsForReplan(buildingBounds);
    }

    private void InvalidatePathsInRegion(Bounds changedRegion)
    {
        if (UnitManager.Instance == null) return;
        foreach (var unit in UnitManager.Instance.AllUnits)
        {
            if (unit == null || unit.IsDead) continue;
            var movement = unit.Movement;
            if (movement == null || !movement.HasPath) continue;
            if (movement.PathBounds.Intersects(changedRegion))
            {
                movement.InvalidatePath();
                movement.FlagForReplan();
            }
        }
    }

    private void FlagUnitsForReplan(Bounds changedRegion)
    {
        if (UnitManager.Instance == null) return;
        foreach (var unit in UnitManager.Instance.AllUnits)
        {
            if (unit == null || unit.IsDead) continue;
            var movement = unit.Movement;
            if (movement == null || !movement.HasPath) continue;
            if (movement.PathBounds.Intersects(changedRegion))
            {
                if (pendingReplanSet.Add(movement))
                    pendingReplans.Add(movement);
            }
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

    public Vector3 FindNearestWalkable(Vector3 worldPos)
    {
        if (grid == null) return worldPos;
        Vector2Int cell = grid.WorldToCell(worldPos);
        if (grid.IsInBounds(cell) && grid.IsWalkable(cell))
            return worldPos;
        Vector2Int nearest = GridAStar.FindNearestWalkableCell(grid, cell, 15);
        return grid.CellToWorld(nearest);
    }
}
