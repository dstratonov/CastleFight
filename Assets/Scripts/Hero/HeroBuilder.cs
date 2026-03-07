using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class HeroBuilder : NetworkBehaviour
{
    [SerializeField] private float buildRange = 15f;

    private BuildingPlacer placer;
    private NetworkPlayer networkPlayer;
    private readonly List<string> builtBuildingIds = new();

    public float BuildRange => buildRange;
    public IReadOnlyList<string> BuiltBuildingIds => builtBuildingIds;

    private void Awake()
    {
        placer = GetComponent<BuildingPlacer>();
        networkPlayer = GetComponent<NetworkPlayer>();
    }

    public void StartBuilding(BuildingData data)
    {
        if (!isLocalPlayer) return;
        if (data == null) return;

        if (networkPlayer != null && networkPlayer.Gold < data.cost)
            return;

        var race = RaceDatabase.Instance?.GetRace(networkPlayer.SelectedRaceId);
        if (race != null && !race.IsBuildingUnlocked(data.buildingId, builtBuildingIds))
            return;

        placer?.StartPlacing(data);
    }

    public void CancelBuilding()
    {
        placer?.CancelPlacement();
    }

    public bool IsInBuildRange(Vector3 position)
    {
        return Vector3.Distance(transform.position, position) <= buildRange;
    }

    private void OnEnable()
    {
        EventBus.Subscribe<BuildingPlacedEvent>(OnBuildingPlaced);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<BuildingPlacedEvent>(OnBuildingPlaced);
    }

    private void OnBuildingPlaced(BuildingPlacedEvent evt)
    {
        if (networkPlayer == null) return;
        if (evt.OwnerPlayerId != networkPlayer.PlayerId) return;

        var building = evt.Building?.GetComponent<Building>();
        if (building?.Data != null && !builtBuildingIds.Contains(building.Data.buildingId))
            builtBuildingIds.Add(building.Data.buildingId);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, buildRange);
    }
}
