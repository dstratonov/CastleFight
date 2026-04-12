using UnityEngine;
using Mirror;
using System.Collections.Generic;

/// <summary>
/// Server-side building registry + spawner. Placement validation is
/// physics-based (no grid cells): a new building may be placed if its
/// footprint bounds don't overlap any existing building or castle.
/// The A* Pro NavMesh is automatically re-cut by NavmeshCut on spawn.
/// </summary>
public class BuildingManager : NetworkBehaviour
{
    public static BuildingManager Instance { get; private set; }

    // Placement overlap uses Physics.OverlapBox on ALL layers and filters the
    // results by component type (Building / Castle / Unit). Prefabs in this
    // project are on the Default layer, so filtering by component is more
    // robust than filtering by LayerMask.
    private const int AllLayers = ~0;
    // Small inset to avoid grazing-overlap false positives (e.g. a ghost
    // whose bounds exactly touch a neighbour's edge).
    private const float OverlapInset = 0.1f;

    private readonly Dictionary<int, List<Building>> teamBuildings = new()
    {
        { 0, new List<Building>() },
        { 1, new List<Building>() }
    };

    // Reusable buffer for Physics.OverlapBoxNonAlloc
    private static readonly Collider[] overlapBuffer = new Collider[16];

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
        EventBus.Subscribe<BuildingDestroyedEvent>(OnBuildingDestroyed);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<BuildingDestroyedEvent>(OnBuildingDestroyed);
    }

    [Server]
    public GameObject PlaceBuilding(BuildingData data, Vector3 position, Quaternion rotation, int teamId, int playerId)
    {
        if (data == null)
        {
            Debug.LogError("[BuildingManager] PlaceBuilding: data is null");
            return null;
        }
        if (data.prefab == null)
        {
            Debug.LogError($"[BuildingManager] PlaceBuilding: {data.buildingName} has null prefab");
            return null;
        }

        // Keep Y on the flat ground plane
        position.y = 0f;

        // Estimate-based fast check — rejects obvious overlaps before we
        // even instantiate the prefab.
        if (IsBuildingSpaceBlocked(data, position, rotation))
        {
            if (GameDebug.Building)
                Debug.LogWarning($"[Build] Rejected {data.buildingName} at {position:F1}: estimate overlap");
            return null;
        }

        GameObject obj = Instantiate(data.prefab, position, rotation);
        var building = obj.GetComponent<Building>();
        if (building != null)
        {
            // Initialize runs FitFootprintCollider, which sets the box collider
            // to its actual footprint WITH the local offset the prefab needs.
            // Only after Initialize can we do a precise overlap check.
            building.Initialize(data, teamId, playerId);

            // Precise check: use the real collider bounds, ignoring the
            // newly-spawned building itself. This catches cases where the
            // estimate footprint underestimated, or where the prefab's
            // BoxCollider is offset from the pivot.
            if (IsColliderBlocked(obj))
            {
                if (GameDebug.Building)
                    Debug.LogWarning($"[Build] Rejected {data.buildingName} at {position:F1}: collider overlap with existing building/unit");
                Destroy(obj);
                return null;
            }

            if (!teamBuildings.ContainsKey(teamId))
                teamBuildings[teamId] = new List<Building>();
            teamBuildings[teamId].Add(building);
        }

        NetworkServer.Spawn(obj);
        EventBus.Raise(new BuildingPlacedEvent(obj, playerId, teamId));
        if (GameDebug.Building)
            Debug.Log($"[Build] Placed {data.buildingName} at {position:F1} team={teamId} player={playerId}");
        return obj;
    }

    /// <summary>
    /// Precise overlap check: uses <paramref name="placedBuilding"/>'s actual
    /// BoxCollider bounds. Returns true if the collider overlaps any other
    /// Building, Castle, or Unit in the scene (excluding the object itself).
    /// </summary>
    private static bool IsColliderBlocked(GameObject placedBuilding)
    {
        var col = placedBuilding.GetComponent<BoxCollider>();
        if (col == null) return false;

        Vector3 halfExtents = col.bounds.extents;
        halfExtents.x = Mathf.Max(0.05f, halfExtents.x - OverlapInset);
        halfExtents.y = Mathf.Max(0.05f, halfExtents.y - OverlapInset);
        halfExtents.z = Mathf.Max(0.05f, halfExtents.z - OverlapInset);

        int hits = Physics.OverlapBoxNonAlloc(
            col.bounds.center, halfExtents, overlapBuffer,
            placedBuilding.transform.rotation, AllLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits; i++)
        {
            var c = overlapBuffer[i];
            if (c == null) continue;
            // Self: the box collider of the just-instantiated building
            if (c.gameObject == placedBuilding) continue;
            if (c.transform.IsChildOf(placedBuilding.transform)) continue;

            if (c.GetComponentInParent<Building>() != null) return true;
            if (c.GetComponentInParent<Castle>() != null) return true;
            if (c.GetComponentInParent<Unit>() != null) return true;
        }
        return false;
    }

    /// <summary>
    /// Physics-based placement check. Samples the prefab's expected footprint
    /// at the target position/rotation and returns true if it would overlap
    /// any existing Building, Castle, or Unit.
    ///
    /// Uses <see cref="BuildingData.footprintSize"/> as an approximation —
    /// prefabs with BoxCollider offsets from the pivot may pass this check
    /// and still fail the precise <c>IsColliderBlocked</c> check after spawn.
    ///
    /// Does NOT check build zones — that's BuildingPlacer's job.
    /// </summary>
    public static bool IsBuildingSpaceBlocked(BuildingData data, Vector3 position, Quaternion rotation)
    {
        Vector3 size = EstimateFootprint(data);
        Vector3 halfExtents = new Vector3(
            Mathf.Max(0.05f, size.x * 0.5f - OverlapInset),
            Mathf.Max(0.05f, size.y * 0.5f - OverlapInset),
            Mathf.Max(0.05f, size.z * 0.5f - OverlapInset));
        Vector3 center = position + new Vector3(0, halfExtents.y, 0);

        int hits = Physics.OverlapBoxNonAlloc(
            center, halfExtents, overlapBuffer, rotation,
            AllLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits; i++)
        {
            var col = overlapBuffer[i];
            if (col == null) continue;
            if (col.GetComponentInParent<Building>() != null) return true;
            if (col.GetComponentInParent<Castle>() != null) return true;
            if (col.GetComponentInParent<Unit>() != null) return true;
        }
        return false;
    }

    /// <summary>
    /// Overlap check using an explicit pre-built bounds box — for the ghost
    /// preview in BuildingPlacer where the ghost's real BoxCollider.bounds
    /// is already available. Excludes <paramref name="ignoreRoot"/> so the
    /// ghost doesn't reject itself.
    /// </summary>
    public static bool IsBoundsBlocked(Bounds worldBounds, Quaternion rotation, GameObject ignoreRoot)
    {
        Vector3 halfExtents = worldBounds.extents;
        halfExtents.x = Mathf.Max(0.05f, halfExtents.x - OverlapInset);
        halfExtents.y = Mathf.Max(0.05f, halfExtents.y - OverlapInset);
        halfExtents.z = Mathf.Max(0.05f, halfExtents.z - OverlapInset);

        int hits = Physics.OverlapBoxNonAlloc(
            worldBounds.center, halfExtents, overlapBuffer, rotation,
            AllLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits; i++)
        {
            var col = overlapBuffer[i];
            if (col == null) continue;
            if (ignoreRoot != null && col.transform.IsChildOf(ignoreRoot.transform)) continue;
            if (col.GetComponentInParent<Building>() != null) return true;
            if (col.GetComponentInParent<Castle>() != null) return true;
            if (col.GetComponentInParent<Unit>() != null) return true;
        }
        return false;
    }

    public static Vector3 EstimateFootprint(BuildingData data)
    {
        // Prefer explicit data footprint; fall back to 2x2 default
        if (data != null && data.footprintSize.x > 0 && data.footprintSize.y > 0)
            return new Vector3(data.footprintSize.x, 3f, data.footprintSize.y);
        return new Vector3(2f, 3f, 2f);
    }

    private static readonly List<Building> EmptyBuildingList = new();

    public IReadOnlyList<Building> GetTeamBuildings(int teamId)
    {
        return teamBuildings.TryGetValue(teamId, out var buildings) ? buildings : EmptyBuildingList;
    }

    /// <summary>
    /// Find the nearest alive enemy building within range using closest-point distance.
    /// Returns null if none found.
    /// </summary>
    public Building FindNearestEnemyBuilding(Vector3 position, int myTeamId, float maxRange)
    {
        int enemyTeam = TeamManager.Instance != null
            ? TeamManager.Instance.GetEnemyTeamId(myTeamId)
            : (myTeamId == 0 ? 1 : 0);

        if (!teamBuildings.TryGetValue(enemyTeam, out var buildings))
            return null;

        float bestDistSq = maxRange * maxRange;
        Building best = null;

        for (int i = 0; i < buildings.Count; i++)
        {
            var b = buildings[i];
            if (b == null) continue;
            IAttackable attackable = b;
            if (attackable.Health == null || attackable.Health.IsDead) continue;

            float distSq = BoundsHelper.ClosestPointDistanceSq(position, b.gameObject);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = b;
            }
        }

        return best;
    }

    public int GetBuildingCount(int teamId, string buildingId)
    {
        if (!teamBuildings.TryGetValue(teamId, out var buildings)) return 0;
        int count = 0;
        foreach (var b in buildings)
        {
            if (b != null && b.Data != null && b.Data.buildingId == buildingId)
                count++;
        }
        return count;
    }

    private void OnBuildingDestroyed(BuildingDestroyedEvent evt)
    {
        var building = evt.Building?.GetComponent<Building>();
        if (building == null) return;

        if (teamBuildings.TryGetValue(evt.TeamId, out var list))
            list.Remove(building);

        if (GameDebug.Building)
            Debug.Log($"[Build] Removed {evt.Building?.name} team={evt.TeamId}");
    }

    // Kept for Castle.cs and older code that called into this helper.
    public static Bounds ComputeBuildingBounds(GameObject buildingObj)
    {
        return BoundsHelper.GetPhysicalBounds(buildingObj);
    }
}
