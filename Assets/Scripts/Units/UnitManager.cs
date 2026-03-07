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
        if (data == null || data.prefab == null) return null;

        GameObject obj = Instantiate(data.prefab, position, rotation);
        var unit = obj.GetComponent<Unit>();
        if (unit != null)
        {
            unit.Initialize(data, teamId);
            RegisterUnit(unit);
        }

        NetworkServer.Spawn(obj);
        EventBus.Raise(new UnitSpawnedEvent(obj, teamId));
        return obj;
    }

    public void RegisterUnit(Unit unit)
    {
        if (unit == null) return;
        int team = unit.TeamId;
        if (!teamUnits.ContainsKey(team))
            teamUnits[team] = new List<Unit>();
        teamUnits[team].Add(unit);
    }

    public void UnregisterUnit(Unit unit)
    {
        if (unit == null) return;
        if (teamUnits.TryGetValue(unit.TeamId, out var list))
            list.Remove(unit);
    }

    public List<Unit> GetTeamUnits(int teamId)
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

    public Unit FindNearestReachableEnemy(Vector3 position, int myTeamId, float maxRange, GameObject requestingUnit)
    {
        var grid = GridSystem.Instance;
        if (grid == null) return FindNearestEnemy(position, myTeamId, maxRange);

        Unit nearest = null;
        float nearestDist = maxRange * maxRange;

        int enemyTeam = TeamManager.Instance != null
            ? TeamManager.Instance.GetEnemyTeamId(myTeamId)
            : (myTeamId == 0 ? 1 : 0);

        if (!teamUnits.TryGetValue(enemyTeam, out var enemies)) return null;
        Vector2Int startCell = grid.WorldToCell(position);

        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            var enemy = enemies[i];
            if (enemy == null || enemy.IsDead) continue;

            float distSq = (enemy.transform.position - position).sqrMagnitude;
            if (distSq >= nearestDist) continue;

            Vector2Int enemyCell = grid.WorldToCell(enemy.transform.position);
            if (GridPathfinding.IsReachable(startCell, enemyCell, grid, requestingUnit))
            {
                nearestDist = distSq;
                nearest = enemy;
            }
        }

        return nearest;
    }

    private void OnUnitKilled(UnitKilledEvent evt)
    {
        var unit = evt.Unit?.GetComponent<Unit>();
        if (unit != null)
            UnregisterUnit(unit);
    }

    private void LateUpdate()
    {
        foreach (var kvp in teamUnits)
        {
            kvp.Value.RemoveAll(u => u == null);
        }
    }
}
