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

        Vector3 basePos = spawnPoint != null ? spawnPoint.position : transform.position + transform.forward * 2f;
        Quaternion rot = spawnPoint != null ? spawnPoint.rotation : transform.rotation;

        Vector3 pos = basePos;
        var grid = GridSystem.Instance;
        float spawnSpread = unitData.unitRadius > 0.5f ? unitData.unitRadius * 2f : 1.5f;
        bool foundWalkable = false;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Vector3 offset = new Vector3(Random.Range(-spawnSpread, spawnSpread), 0, Random.Range(-spawnSpread, spawnSpread));
            Vector3 candidate = basePos + offset;
            if (grid == null || grid.IsWalkable(grid.WorldToCell(candidate)))
            {
                pos = candidate;
                foundWalkable = true;
                break;
            }
        }

        if (!foundWalkable && grid != null)
            pos = grid.FindNearestWalkablePosition(basePos, basePos);

        var unitObj = UnitManager.Instance?.SpawnUnit(unitData, pos, rot, teamId);
        if (unitObj != null)
        {
            var movement = unitObj.GetComponent<UnitMovement>();
            movement?.SetDestinationToEnemyCastle();
        }
    }
}
