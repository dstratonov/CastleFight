using UnityEngine;
using Mirror;
using System.Collections.Generic;

[RequireComponent(typeof(Health))]
public class Castle : NetworkBehaviour, ISelectable, IAttackable
{
    [SerializeField] private int initialTeamId = -1;

    [Tooltip("XZ size of the castle's physical ground footprint in world units. " +
             "Leave at zero to auto-detect from renderers.")]
    [SerializeField] private Vector2 footprintSize;

    [SyncVar] private int teamId = -1;

    private Health health;
    private List<Vector2Int> occupiedCells = new();

    public int TeamId => teamId;
    public Health Health => health;
    public string DisplayName => TeamId == 0 ? "Blue Castle" : "Red Castle";
    ArmorType IAttackable.ArmorType => ArmorType.Fortified;
    float IAttackable.TargetRadius => BoundsHelper.GetRadius(gameObject);
    Vector2Int IAttackable.CurrentCell => GridSystem.Instance != null ? GridSystem.Instance.WorldToCell(transform.position) : Vector2Int.zero;
    int IAttackable.FootprintSize => occupiedCells.Count > 0
        ? Mathf.CeilToInt(Mathf.Sqrt(occupiedCells.Count)) : 2;
    (Vector2Int min, Vector2Int max) IAttackable.FootprintBounds
    {
        get
        {
            if (occupiedCells.Count == 0)
                return FootprintHelper.GetRect(((IAttackable)this).CurrentCell, 2);
            Vector2Int min = occupiedCells[0], max = occupiedCells[0];
            for (int i = 1; i < occupiedCells.Count; i++)
            {
                min = Vector2Int.Min(min, occupiedCells[i]);
                max = Vector2Int.Max(max, occupiedCells[i]);
            }
            return (min, max);
        }
    }
    TargetPriority IAttackable.Priority => TargetPriority.Default;

    private void Awake()
    {
        health = GetComponent<Health>();
        Building.FitFootprintCollider(gameObject, footprintSize);
    }

    public override void OnStartServer()
    {
        if (teamId < 0)
        {
            int team = initialTeamId >= 0 ? initialTeamId : FallbackDetectTeam();
            var config = GameConfig.Instance;
            int hp = config != null ? config.castleMaxHealth : 5000;
            Initialize(team, hp);
        }

        RegisterGridCells();
    }

    private int FallbackDetectTeam()
    {
        Debug.LogWarning($"[Castle] {gameObject.name} has no initialTeamId set — defaulting to team 0. " +
            "Set the Initial Team Id field in the Inspector.");
        return 0;
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
        Debug.Log($"[Castle] {gameObject.name} registered {occupiedCells.Count} grid cells " +
            $"footprint=({bounds.size.x:F1}, {bounds.size.z:F1})");
    }

    public override void OnStartClient()
    {
        if (GetComponent<WorldHealthBar>() == null)
            gameObject.AddComponent<WorldHealthBar>();
    }

    private void OnEnable()
    {
        GameRegistry.RegisterCastle(this);
        if (health != null)
            health.OnDeath += HandleCastleDestroyed;
    }

    private void OnDisable()
    {
        GameRegistry.UnregisterCastle(this);
        if (health != null)
            health.OnDeath -= HandleCastleDestroyed;
    }

    private void HandleCastleDestroyed(GameObject killer)
    {
        // HandleCastleDestroyed is driven by Health.OnDeath which only fires on server,
        // but guard defensively in case the call chain changes.
        if (!isServer) return;

        Debug.Log($"[Castle] {gameObject.name} DESTROYED by {(killer != null ? killer.name : "unknown")}!");
        EventBus.Raise(new CastleDestroyedEvent(teamId));

        var grid = GridSystem.Instance;
        if (grid != null && occupiedCells.Count > 0)
            grid.ClearCells(occupiedCells);

        int winningTeam = TeamManager.Instance != null
            ? TeamManager.Instance.GetEnemyTeamId(teamId)
            : (teamId == 0 ? 1 : 0);
        Debug.Log($"[Castle] GAME OVER! Team {winningTeam} wins!");
        if (GameManager.Instance != null)
            GameManager.Instance.EndMatch(winningTeam);
        else
            Debug.LogError("[Castle] GameManager.Instance is NULL — cannot end match! Game will continue in broken state.");
    }
}
