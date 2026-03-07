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
        {
            Vector2Int spawnCell = grid.WorldToCell(pos);
            Vector2Int? freeCell = FindFreeAdjacentCell(grid, spawnCell);
            if (freeCell.HasValue)
                pos = grid.CellToWorld(freeCell.Value);
            else
                pos = grid.SnapToGrid(pos);
        }

        var unitObj = UnitManager.Instance?.SpawnUnit(unitData, pos, rot, teamId);
        if (unitObj != null)
        {
            Debug.Log($"[Spawner] Spawned {unitData.unitName} at {pos} for team {teamId}");
            var movement = unitObj.GetComponent<GridMovement>();
            movement?.SetDestinationToEnemyCastle();
        }
        else
        {
            Debug.LogWarning($"[Spawner] Failed to spawn {unitData.unitName}! UnitManager={UnitManager.Instance != null}");
        }
    }

    private Vector2Int? FindFreeAdjacentCell(GridSystem grid, Vector2Int center)
    {
        if (grid.IsWalkable(center)) return center;
        var neighbors = grid.GetAdjacentCells(center);
        foreach (var n in neighbors)
        {
            if (grid.IsWalkable(n))
                return n;
        }
        return null;
    }
}
