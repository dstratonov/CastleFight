using UnityEngine;
using Mirror;
using System.Collections.Generic;

[RequireComponent(typeof(Health))]
public class Castle : NetworkBehaviour
{
    [SyncVar] private int teamId = -1;

    private Health health;
    private List<Vector2Int> occupiedCells = new();

    public int TeamId => teamId;
    public Health Health => health;

    private void Awake()
    {
        health = GetComponent<Health>();
    }

    public override void OnStartServer()
    {
        if (teamId < 0)
        {
            int team = gameObject.name.Contains("Team0") ? 0 : 1;
            Initialize(team, 5000);
        }

        RegisterGridCells();
    }

    [Server]
    public void Initialize(int team, int maxHp = 5000)
    {
        teamId = team;
        health.Initialize(maxHp, team);
        Debug.Log($"[Castle] {gameObject.name} initialized as team {team} with {maxHp} HP");
    }

    [Server]
    private void RegisterGridCells()
    {
        var grid = GridSystem.Instance;
        if (grid == null) return;

        Bounds bounds = BuildingManager.ComputeBuildingBounds(gameObject);
        occupiedCells = grid.GetCellsOverlappingBounds(bounds);
        grid.MarkCells(occupiedCells, CellState.Building);
        Debug.Log($"[Castle] {gameObject.name} registered {occupiedCells.Count} grid cells");
    }

    private void OnEnable()
    {
        if (health != null)
            health.OnDeath += HandleCastleDestroyed;
    }

    private void OnDisable()
    {
        if (health != null)
            health.OnDeath -= HandleCastleDestroyed;
    }

    private void HandleCastleDestroyed(GameObject killer)
    {
        EventBus.Raise(new CastleDestroyedEvent(teamId));

        if (isServer)
        {
            var grid = GridSystem.Instance;
            if (grid != null && occupiedCells.Count > 0)
                grid.ClearCells(occupiedCells);

            int winningTeam = TeamManager.Instance != null
                ? TeamManager.Instance.GetEnemyTeamId(teamId)
                : (teamId == 0 ? 1 : 0);
            GameManager.Instance?.EndMatch(winningTeam);
        }
    }
}
