using UnityEngine;
using Mirror;

public class Unit : NetworkBehaviour, ISelectable
{
    [SyncVar] private int teamId;
    [SyncVar(hook = nameof(OnUnitDataIdChanged))]
    private string unitDataId;

    private UnitData data;
    private Health health;
    private UnitMovement movement;
    private UnitStateMachine stateMachine;
    private UnitCombat combat;

    private float cachedRadius = -1f;

    public UnitData Data => data;
    public int TeamId => teamId;
    public bool IsDead => health != null && health.IsDead;
    public UnitMovement Movement => movement;
    public UnitStateMachine StateMachine => stateMachine;
    public UnitCombat Combat => combat;
    public string DisplayName => data != null ? data.displayName : "Unit";
    Health ISelectable.Health => health;

    private const float MaxAutoRadius = 2f;
    private const float MaxEffectiveRadius = 3f;

    public float EffectiveRadius
    {
        get
        {
            float r;
            if (data != null && data.unitRadius > 0f)
                r = data.unitRadius;
            else if (cachedRadius > 0f)
                r = cachedRadius;
            else
            {
                cachedRadius = ComputeRadiusFromBounds();
                r = cachedRadius;
            }
            return Mathf.Min(r, MaxEffectiveRadius);
        }
    }

    private float ComputeRadiusFromBounds()
    {
        float raw = BoundsHelper.GetRadius(gameObject);
        if (raw > MaxAutoRadius)
        {
            Debug.LogWarning($"[Unit] {gameObject.name} auto-radius {raw:F2} clamped to {MaxAutoRadius}. Set unitRadius in UnitData to override.");
            raw = MaxAutoRadius;
        }
        return Mathf.Max(raw, 0.25f);
    }

    private void Awake()
    {
        health = GetComponent<Health>();
        movement = GetComponent<UnitMovement>();
        stateMachine = GetComponent<UnitStateMachine>();
        combat = GetComponent<UnitCombat>();
    }

    [Server]
    public void Initialize(UnitData unitData, int team)
    {
        data = unitData;
        teamId = team;
        unitDataId = unitData.unitName;

        if (health != null)
            health.Initialize(unitData.maxHealth, team);

        if (GameDebug.UnitLifecycle)
            Debug.Log($"[Unit] INIT {gameObject.name} data={unitData.unitName} team={team} hp={unitData.maxHealth} " +
                $"atk={unitData.attackDamage} spd={unitData.moveSpeed} range={unitData.attackRange} " +
                $"radius={unitData.unitRadius:F2} isRanged={unitData.isRanged}");
    }

    public override void OnStartClient()
    {
        if (data == null && !string.IsNullOrEmpty(unitDataId))
        {
            ResolveUnitData(unitDataId);
            if (GameDebug.UnitLifecycle && data == null)
                Debug.LogWarning($"[Unit] OnStartClient: failed to resolve data for '{unitDataId}' on {gameObject.name}");
        }

        if (GetComponent<WorldHealthBar>() == null)
            gameObject.AddComponent<WorldHealthBar>();

        if (GameDebug.UnitLifecycle)
            Debug.Log($"[Unit] OnStartClient {gameObject.name} data={data?.unitName ?? "NULL"} team={teamId}");
    }

    private void OnUnitDataIdChanged(string oldId, string newId)
    {
        if (data == null && !string.IsNullOrEmpty(newId))
            ResolveUnitData(newId);
    }

    private void ResolveUnitData(string id)
    {
        var raceDb = RaceDatabase.Instance;
        if (raceDb == null || raceDb.AllRaces == null) return;

        foreach (var race in raceDb.AllRaces)
        {
            if (race == null) continue;
            var found = race.GetUnit(id);
            if (found != null)
            {
                data = found;
                return;
            }
        }
    }

    private void OnEnable()
    {
        if (health != null)
            health.OnDeath += HandleDeath;
    }

    private void OnDisable()
    {
        if (health != null)
            health.OnDeath -= HandleDeath;
    }

    private void HandleDeath(GameObject killer)
    {
        int bounty = data != null ? data.goldBounty : 0;
        if (GameDebug.UnitLifecycle)
            Debug.Log($"[Unit] DEATH {gameObject.name} team={teamId} killer={killer?.name ?? "null"} bounty={bounty} isServer={isServer}");
        EventBus.Raise(new UnitKilledEvent(gameObject, killer, bounty));

        if (isServer)
        {
            float delay = 2f;
            if (stateMachine != null && stateMachine.Animator != null)
                delay = stateMachine.Animator.GetDeathDuration(2f) + 0.2f;
            if (GameDebug.UnitLifecycle)
                Debug.Log($"[Unit] {gameObject.name} will be destroyed in {delay:F1}s");
            Invoke(nameof(ServerDestroy), delay);
        }
    }

    [Server]
    private void ServerDestroy()
    {
        NetworkServer.Destroy(gameObject);
    }
}
