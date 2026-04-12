using UnityEngine;
using Mirror;
using Pathfinding;

[RequireComponent(typeof(Health))]
public class Castle : NetworkBehaviour, ISelectable, IAttackable
{
    [SerializeField] private int initialTeamId = -1;

    [Tooltip("XZ size of the castle's physical ground footprint in world units. " +
             "Leave at zero to auto-detect from renderers.")]
    [SerializeField] private Vector2 footprintSize;

    [SyncVar] private int teamId = -1;

    private Health health;

    public int TeamId => teamId;
    public Health Health => health;
    public string DisplayName => TeamId == 0 ? "Blue Castle" : "Red Castle";
    ArmorType IAttackable.ArmorType => ArmorType.Fortified;
    Vector3 IAttackable.Position
    {
        get
        {
            var col = GetComponent<BoxCollider>();
            if (col != null)
            {
                var c = col.bounds.center;
                c.y = transform.position.y;
                return c;
            }
            return transform.position;
        }
    }
    float IAttackable.TargetRadius => 0f;  // bounds already encodes full size
    Bounds IAttackable.WorldBounds
    {
        get
        {
            var col = GetComponent<BoxCollider>();
            if (col != null) return col.bounds;
            if (BoundsHelper.TryGetCombinedBounds(gameObject, out var b)) return b;
            return new Bounds(transform.position, Vector3.one);
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

        SetupNavmeshCut();
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

    private void SetupNavmeshCut()
    {
        var cut = GetComponent<NavmeshCut>();
        if (cut == null) cut = gameObject.AddComponent<NavmeshCut>();

        var box = GetComponent<BoxCollider>();
        if (box != null)
        {
            cut.type = NavmeshCut.MeshType.Rectangle;
            // Mirror the box collider's LOCAL offset and size so the navmesh
            // hole aligns with the actual castle footprint, not the pivot.
            cut.rectangleSize = new Vector2(box.size.x, box.size.z);
            cut.height = box.size.y;
            cut.center = box.center;
        }
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

        // Only the FIRST castle death triggers EndMatch. If we're already past
        // GameOver, this is the losing side's second castle being mopped up
        // after the match ended — don't re-report a winner.
        if (GameManager.Instance == null)
        {
            Debug.LogError("[Castle] GameManager.Instance is NULL — cannot end match! Game will continue in broken state.");
            return;
        }
        if (GameManager.Instance.CurrentState == GameState.GameOver)
        {
            Debug.Log($"[Castle] {gameObject.name} died after game already ended — ignoring.");
            return;
        }

        int winningTeam = TeamManager.Instance != null
            ? TeamManager.Instance.GetEnemyTeamId(teamId)
            : (teamId == 0 ? 1 : 0);
        Debug.Log($"[Castle] GAME OVER! Team {winningTeam} wins!");
        GameManager.Instance.EndMatch(winningTeam);
    }
}
