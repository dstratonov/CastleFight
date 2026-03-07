using UnityEngine;
using Mirror;

public class Spawner : NetworkBehaviour
{
    [SerializeField] private Transform spawnPoint;

    private UnitData unitData;
    private float spawnInterval;
    private int teamId;
    private float spawnTimer;
    private bool initialized;

    [Server]
    public void Initialize(UnitData data, float interval, int team)
    {
        unitData = data;
        spawnInterval = interval;
        teamId = team;
        spawnTimer = spawnInterval;
        initialized = true;
    }

    private void Update()
    {
        if (!isServer || !initialized || unitData == null) return;

        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f)
        {
            SpawnUnit();
            spawnTimer = spawnInterval;
        }
    }

    [Server]
    private void SpawnUnit()
    {
        if (unitData.prefab == null) return;

        Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position + transform.forward * 2f;
        Quaternion rot = spawnPoint != null ? spawnPoint.rotation : transform.rotation;

        var grid = GridSystem.Instance;
        if (grid != null)
            pos = grid.SnapToGrid(pos);

        var unitObj = UnitManager.Instance?.SpawnUnit(unitData, pos, rot, teamId);
        if (unitObj != null)
        {
            var movement = unitObj.GetComponent<GridMovement>();
            movement?.SetDestinationToEnemyCastle();
        }
    }
}
