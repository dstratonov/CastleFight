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
        float unitRadius = unitData.unitRadius > 0 ? unitData.unitRadius : 0.5f;
        float spawnSpread = SpawnPositionFinder.ComputeSpawnSpread(unitRadius);
        bool foundValid = false;

        for (int attempt = 0; attempt < 20; attempt++)
        {
            float spread = spawnSpread + attempt * 0.5f;
            Vector3 offset = new Vector3(Random.Range(-spread, spread), 0, Random.Range(-spread, spread));
            Vector3 candidate = basePos + offset;

            if (grid != null)
            {
                Vector2Int cell = grid.WorldToCell(candidate);
                if (!grid.IsWalkable(cell)) continue;

                int cellRadius = Mathf.CeilToInt(unitRadius / grid.CellSize) + 1;
                bool hasClearance = true;
                for (int dx = -cellRadius; dx <= cellRadius && hasClearance; dx++)
                {
                    for (int dz = -cellRadius; dz <= cellRadius && hasClearance; dz++)
                    {
                        Vector2Int adj = new(cell.x + dx, cell.y + dz);
                        if (!grid.IsInBounds(adj) || !grid.IsWalkable(adj))
                            hasClearance = false;
                    }
                }
                if (!hasClearance) continue;
            }

            if (!IsSpawnPositionClear(candidate, unitRadius))
                continue;

            pos = candidate;
            foundValid = true;
            break;
        }

        if (!foundValid && grid != null)
        {
            pos = grid.FindNearestWalkablePosition(basePos, basePos);
            if (!IsSpawnPositionClear(pos, unitRadius))
            {
                for (int fallbackAttempt = 0; fallbackAttempt < 8; fallbackAttempt++)
                {
                    Vector3 fallbackOffset = SpawnPositionFinder.ComputeFallbackOffset(fallbackAttempt, spawnSpread);
                    Vector3 candidate = basePos + fallbackOffset;
                    Vector2Int candidateCell = grid.WorldToCell(candidate);
                    if (grid.IsInBounds(candidateCell) && grid.IsWalkable(candidateCell)
                        && IsSpawnPositionClear(candidate, unitRadius))
                    {
                        pos = candidate;
                        foundValid = true;
                        break;
                    }
                }
                if (!foundValid)
                {
                    pos = grid.FindNearestWalkablePosition(basePos + Vector3.forward * spawnSpread, basePos);
                    if (GameDebug.Spawning)
                        Debug.LogWarning($"[Spawn] All fallback attempts failed for {unitData.unitName}, using nearest walkable {pos:F1}");
                }
            }
        }

        if (grid != null)
            pos.y = grid.GridOrigin.y;

        var unitObj = UnitManager.Instance?.SpawnUnit(unitData, pos, rot, teamId);
        if (unitObj != null)
        {
            var unit = unitObj.GetComponent<Unit>();
            unit?.Movement?.SetDestinationToEnemyCastle();

            if (GameDebug.Spawning)
                Debug.Log($"[Spawn] {unitData.unitName} at {pos:F1} team={teamId} valid={foundValid}" +
                    $" basePos={basePos:F1} offset={Vector3.Distance(pos, basePos):F1}");
        }
        else if (GameDebug.Spawning)
        {
            Debug.LogWarning($"[Spawn] FAILED to spawn {unitData.unitName} at {pos:F1}");
        }
    }

    private bool IsSpawnPositionClear(Vector3 position, float radius)
    {
        if (UnitManager.Instance == null) return true;

        float checkRadius = radius * 2.5f;
        var nearby = UnitManager.Instance.GetUnitsInRadius(position, checkRadius);
        foreach (var other in nearby)
        {
            if (other == null || other.IsDead) continue;
            float combinedRadius = radius + other.EffectiveRadius;
            float dist = Vector3.Distance(position, other.transform.position);
            if (dist < combinedRadius * 0.8f)
                return false;
        }
        return true;
    }
}
