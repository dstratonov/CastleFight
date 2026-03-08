using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class BuildingManager : NetworkBehaviour
{
    public static BuildingManager Instance { get; private set; }

    private readonly Dictionary<int, List<Building>> teamBuildings = new()
    {
        { 0, new List<Building>() },
        { 1, new List<Building>() }
    };

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
        if (data == null || data.prefab == null) return null;

        var grid = GridSystem.Instance;
        if (grid != null)
            position = grid.SnapToGrid(position);

        GameObject obj = Instantiate(data.prefab, position, rotation);
        var building = obj.GetComponent<Building>();
        if (building != null)
        {
            building.Initialize(data, teamId, playerId);
            if (!teamBuildings.ContainsKey(teamId))
                teamBuildings[teamId] = new List<Building>();
            teamBuildings[teamId].Add(building);
        }

        if (grid != null && building != null)
        {
            Bounds bounds = ComputeBuildingBounds(obj);
            var occupiedCells = grid.GetCellsOverlappingBounds(bounds);
            grid.MarkCells(occupiedCells, CellState.Building);
            building.SetOccupiedCells(occupiedCells);
        }

        NetworkServer.Spawn(obj);
        EventBus.Raise(new BuildingPlacedEvent(obj, playerId, teamId));
        if (GameDebug.Building)
            Debug.Log($"[Build] Placed {data.buildingName} at {position:F1} team={teamId} player={playerId}");
        return obj;
    }

    public bool IsValidPlacement(Vector3 position, int teamId)
    {
        var grid = GridSystem.Instance;
        if (grid == null) return false;
        return grid.CanPlaceBuilding(position, teamId);
    }

    public List<Building> GetTeamBuildings(int teamId)
    {
        return teamBuildings.TryGetValue(teamId, out var buildings) ? buildings : new List<Building>();
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

        var grid = GridSystem.Instance;
        if (grid != null)
        {
            grid.ClearCells(new List<Vector2Int>(building.OccupiedCells));
        }

        if (GameDebug.Building)
            Debug.Log($"[Build] Removed {evt.Building?.name} team={evt.TeamId} cells freed={building.OccupiedCells.Count}");
    }

    public static Bounds ComputeBuildingBounds(GameObject buildingObj)
    {
        return BoundsHelper.GetCombinedBounds(buildingObj);
    }
}
