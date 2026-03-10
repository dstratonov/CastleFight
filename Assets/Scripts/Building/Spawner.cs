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

    [SyncVar] private float syncedProgress;

    public float SpawnProgress => syncedProgress;
    public float SpawnInterval => spawnInterval;
    public bool IsInitialized => initialized;
    public UnitData UnitData => unitData;

    [Server]
    public void Initialize(UnitData data, float interval, int team)
    {
        unitData = data;
        spawnInterval = interval;
        teamId = team;
        spawnTimer = spawnInterval;
        initialized = true;
        syncedProgress = 0f;
        if (GameDebug.Spawning)
            Debug.Log($"[Spawn] Spawner on {gameObject.name} initialized: unit={data?.unitName} interval={interval}s team={team}");
    }

    private void Update()
    {
        if (!isServer || !initialized || unitData == null) return;
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver) return;

        spawnTimer -= Time.deltaTime;
        syncedProgress = 1f - Mathf.Clamp01(spawnTimer / spawnInterval);

        if (spawnTimer <= 0f)
        {
            SpawnUnit();
            spawnTimer = spawnInterval;
            syncedProgress = 0f;
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
        float spawnSpread = Mathf.Max(3f, unitData.unitRadius * 3f);
        bool foundWalkable = false;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            float spread = spawnSpread + attempt * 0.5f;
            Vector3 offset = new Vector3(Random.Range(-spread, spread), 0, Random.Range(-spread, spread));
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
            if (GameDebug.Spawning)
                Debug.Log($"[Spawn] {unitData.unitName} at {pos:F1} team={teamId} walkable={foundWalkable}");
        }
        else if (GameDebug.Spawning)
        {
            Debug.LogWarning($"[Spawn] FAILED to spawn {unitData.unitName} at {pos:F1}");
        }
    }
}
