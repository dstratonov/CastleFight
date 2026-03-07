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
            teamBuildings[teamId].Add(building);
        }

        if (grid != null)
        {
            Vector2Int cell = grid.WorldToCell(position);
            grid.PlaceBuilding(cell, obj);
        }

        NetworkServer.Spawn(obj);
        EventBus.Raise(new BuildingPlacedEvent(obj, playerId, teamId));
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
            Vector2Int cell = grid.WorldToCell(evt.Building.transform.position);
            grid.RemoveBuilding(cell);
        }
    }
}
