using UnityEngine;
using Mirror;

[RequireComponent(typeof(Health))]
public class Building : NetworkBehaviour
{
    [SyncVar] private int teamId;
    [SyncVar] private int ownerId;

    private BuildingData data;
    private Health health;
    private Spawner spawner;

    public BuildingData Data => data;
    public int TeamId => teamId;
    public int OwnerId => ownerId;

    private void Awake()
    {
        health = GetComponent<Health>();
        spawner = GetComponent<Spawner>();
    }

    [Server]
    public void Initialize(BuildingData buildingData, int team, int owner)
    {
        data = buildingData;
        teamId = team;
        ownerId = owner;

        health.Initialize(data.maxHealth, team);

        if (spawner != null && data.spawnedUnit != null)
        {
            spawner.Initialize(data.spawnedUnit, data.spawnInterval, team);
        }
    }

    private void OnEnable()
    {
        if (health != null)
            health.OnDeath += HandleDeath;
    }

    private void OnDisable()
    {
        if (health != null)
            health.OnDeath -= HandleDeath;
    }

    private void HandleDeath(GameObject killer)
    {
        EventBus.Raise(new BuildingDestroyedEvent(gameObject, teamId));

        if (isServer)
        {
            Invoke(nameof(ServerDestroy), 2f);
        }
    }

    [Server]
    private void ServerDestroy()
    {
        NetworkServer.Destroy(gameObject);
    }
}
