using UnityEngine;
using Mirror;
using Pathfinding;

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
        if (!isServer || !NetworkServer.active || !initialized || unitData == null) return;
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
        float unitRadius = unitData.unitRadius > 0 ? unitData.unitRadius : 0.5f;
        float spawnSpread = SpawnPositionFinder.ComputeSpawnSpread(unitRadius);
        bool foundValid = false;

        // Find a valid spawn position using A* Pro NavMesh queries
        for (int attempt = 0; attempt < 20; attempt++)
        {
            float spread = spawnSpread + attempt * 0.5f;
            Vector3 offset = new Vector3(Random.Range(-spread, spread), 0, Random.Range(-spread, spread));
            Vector3 candidate = basePos + offset;

            if (!TrySnapToSpawnSurface(candidate, unitRadius, out candidate))
                continue;

            if (!IsSpawnPositionClear(candidate, unitRadius))
                continue;

            pos = candidate;
            foundValid = true;
            break;
        }

        if (!foundValid)
        {
            // Fallback: find nearest walkable point on NavMesh
            if (TrySnapToSpawnSurface(basePos, unitRadius, out Vector3 snapped))
                pos = snapped;

            if (GameDebug.Spawning)
                Debug.LogWarning($"[Spawn] All attempts failed for {unitData.unitName}, using nearest NavMesh point {pos:F1}");
        }

        // Flat ground at Y=0
        pos.y = 0f;

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

    private bool TrySnapToSpawnSurface(Vector3 candidate, float unitRadius, out Vector3 snapped)
    {
        snapped = candidate;
        if (AstarPath.active == null)
            return false;

        var nearest = AstarPath.active.GetNearest(candidate, UnitPathingProfile.BuildWalkableConstraint(AstarPath.active, unitRadius));
        if (nearest.node == null || !nearest.node.Walkable)
            return false;

        snapped = nearest.position;
        return true;
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
