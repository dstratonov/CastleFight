using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class UnitManager : NetworkBehaviour
{
    public static UnitManager Instance { get; private set; }

    private readonly Dictionary<int, List<Unit>> teamUnits = new()
    {
        { 0, new List<Unit>() },
        { 1, new List<Unit>() }
    };

    private readonly List<Unit> allUnits = new();

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
        EventBus.Subscribe<UnitKilledEvent>(OnUnitKilled);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<UnitKilledEvent>(OnUnitKilled);
    }

    [Server]
    public GameObject SpawnUnit(UnitData data, Vector3 position, Quaternion rotation, int teamId)
    {
        if (data == null || data.prefab == null)
        {
            Debug.LogWarning($"[UnitManager] SpawnUnit failed: data={data != null}, prefab={data?.prefab != null}");
            return null;
        }

        GameObject obj = Instantiate(data.prefab, position, rotation);
        var unit = obj.GetComponent<Unit>();
        if (unit != null)
        {
            unit.Initialize(data, teamId);
            RegisterUnit(unit);
        }

        NetworkServer.Spawn(obj);
        EventBus.Raise(new UnitSpawnedEvent(obj, teamId));
        if (GameDebug.Spawning)
        {
            int t0 = teamUnits.TryGetValue(0, out var l0) ? l0.Count : 0;
            int t1 = teamUnits.TryGetValue(1, out var l1) ? l1.Count : 0;
            Debug.Log($"[Spawn] UnitManager created {data.unitName} team={teamId} at {position:F1} " +
                $"hp={data.maxHealth} atk={data.attackDamage} spd={data.moveSpeed} rad={data.unitRadius:F2} " +
                $"| alive t0={t0} t1={t1}");
        }
        return obj;
    }

    public void RegisterUnit(Unit unit)
    {
        if (unit == null) return;
        int team = unit.TeamId;
        if (!teamUnits.ContainsKey(team))
            teamUnits[team] = new List<Unit>();
        teamUnits[team].Add(unit);
        allUnits.Add(unit);
    }

    public void UnregisterUnit(Unit unit)
    {
        if (unit == null) return;
        if (teamUnits.TryGetValue(unit.TeamId, out var list))
            list.Remove(unit);
        allUnits.Remove(unit);
    }

    public IReadOnlyList<Unit> GetTeamUnits(int teamId)
    {
        return teamUnits.TryGetValue(teamId, out var list) ? list : new List<Unit>();
    }

    public Unit FindNearestEnemy(Vector3 position, int myTeamId, float maxRange)
    {
        Unit nearest = null;
        float nearestDist = maxRange * maxRange;

        int enemyTeam = TeamManager.Instance != null
            ? TeamManager.Instance.GetEnemyTeamId(myTeamId)
            : (myTeamId == 0 ? 1 : 0);

        if (!teamUnits.TryGetValue(enemyTeam, out var enemies)) return null;

        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            var enemy = enemies[i];
            if (enemy == null || enemy.IsDead) continue;

            float distSq = (enemy.transform.position - position).sqrMagnitude;
            if (distSq < nearestDist)
            {
                nearestDist = distSq;
                nearest = enemy;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Returns all live units (any team) within radius of position. Used for separation steering.
    /// </summary>
    public List<Unit> GetUnitsInRadius(Vector3 position, float radius)
    {
        var result = new List<Unit>();
        float radiusSq = radius * radius;

        for (int i = allUnits.Count - 1; i >= 0; i--)
        {
            var u = allUnits[i];
            if (u == null || u.IsDead) continue;

            float distSq = (u.transform.position - position).sqrMagnitude;
            if (distSq <= radiusSq)
                result.Add(u);
        }

        return result;
    }

    private void OnUnitKilled(UnitKilledEvent evt)
    {
        var unit = evt.Unit?.GetComponent<Unit>();
        if (unit != null)
        {
            UnregisterUnit(unit);
            if (GameDebug.Spawning)
            {
                int t0 = teamUnits.TryGetValue(0, out var l0) ? l0.Count : 0;
                int t1 = teamUnits.TryGetValue(1, out var l1) ? l1.Count : 0;
                Debug.Log($"[Spawn] Unit killed: {unit.gameObject.name} team={unit.TeamId} | alive t0={t0} t1={t1}");
            }
        }
    }

    private void LateUpdate()
    {
        for (int i = allUnits.Count - 1; i >= 0; i--)
        {
            if (allUnits[i] == null)
                allUnits.RemoveAt(i);
        }

        foreach (var kvp in teamUnits)
        {
            kvp.Value.RemoveAll(u => u == null);
        }
    }
}
