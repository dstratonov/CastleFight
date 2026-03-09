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
            var config = Resources.Load<GameConfig>("GameConfig");
            int hp = config != null ? config.castleMaxHealth : 5000;
            Initialize(team, hp);
        }

        RegisterGridCells();
    }

    [Server]
    public void Initialize(int team, int maxHp)
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

    public override void OnStartClient()
    {
        if (GetComponent<WorldHealthBar>() == null)
            gameObject.AddComponent<WorldHealthBar>();
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
        Debug.Log($"[Castle] {gameObject.name} DESTROYED by {(killer != null ? killer.name : "unknown")}!");
        EventBus.Raise(new CastleDestroyedEvent(teamId));

        if (isServer)
        {
            var grid = GridSystem.Instance;
            if (grid != null && occupiedCells.Count > 0)
                grid.ClearCells(occupiedCells);

            int winningTeam = TeamManager.Instance != null
                ? TeamManager.Instance.GetEnemyTeamId(teamId)
                : (teamId == 0 ? 1 : 0);
            Debug.Log($"[Castle] GAME OVER! Team {winningTeam} wins!");
            GameManager.Instance?.EndMatch(winningTeam);
        }
    }
}
