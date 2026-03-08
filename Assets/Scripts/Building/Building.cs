using UnityEngine;
using Mirror;
using System.Collections.Generic;

[RequireComponent(typeof(Health))]
public class Building : NetworkBehaviour
{
    [SyncVar] private int teamId;
    [SyncVar] private int ownerId;

    private BuildingData data;
    private Health health;
    private Spawner spawner;
    private List<Vector2Int> occupiedCells = new();

    public BuildingData Data => data;
    public int TeamId => teamId;
    public int OwnerId => ownerId;
    public IReadOnlyList<Vector2Int> OccupiedCells => occupiedCells;

    public void SetOccupiedCells(List<Vector2Int> cells)
    {
        occupiedCells = cells;
    }

    private void Awake()
    {
        health = GetComponent<Health>();
        spawner = GetComponent<Spawner>();
        StripModelColliders();
    }

    private void StripModelColliders()
    {
        var model = transform.Find("Model");
        if (model == null) return;

        foreach (var col in model.GetComponentsInChildren<BoxCollider>(true))
            Destroy(col);

        FitRootColliderToModel();
    }

    private void FitRootColliderToModel()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds worldBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            worldBounds.Encapsulate(renderers[i].bounds);

        var box = GetComponent<BoxCollider>();
        if (box == null) box = gameObject.AddComponent<BoxCollider>();

        box.center = transform.InverseTransformPoint(worldBounds.center);
        box.size = new Vector3(
            worldBounds.size.x / transform.lossyScale.x,
            worldBounds.size.y / transform.lossyScale.y,
            worldBounds.size.z / transform.lossyScale.z
        );
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
