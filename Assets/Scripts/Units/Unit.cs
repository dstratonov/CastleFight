using UnityEngine;
using Mirror;

public class Unit : NetworkBehaviour, ISelectable, IAttackable
{
    [SyncVar] private int teamId;
    [SyncVar(hook = nameof(OnUnitDataIdChanged))]
    private string unitDataId;

    private UnitData data;
    private Health health;
    private UnitMovement movement;
    private UnitCombat combat;
    private UnitStateMachine stateMachine;

    private float cachedRadius = -1f;

    public UnitData Data => data;
    public int TeamId => teamId;
    public bool IsDead
    {
        get
        {
            Debug.Assert(health != null, $"[Unit] {gameObject.name} IsDead: health is null", this);
            return health != null && health.IsDead;
        }
    }
    public UnitMovement Movement => movement;
    public UnitCombat Combat => combat;
    public UnitStateMachine StateMachine => stateMachine;
    public string DisplayName => data != null ? data.displayName : "Unit";
    Health ISelectable.Health => health;
    Health IAttackable.Health => health;
    ArmorType IAttackable.ArmorType => data != null ? data.armorType : ArmorType.Unarmored;
    float IAttackable.TargetRadius => EffectiveRadius;
    TargetPriority IAttackable.Priority => TargetPriority.Unit;

    private const float MaxAutoRadius = 2f;
    private const float MaxEffectiveRadius = 4f; // Large creatures (dragons, cyclops) need bigger footprints

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

    private int cachedFootprint = -1;

    /// <summary>
    /// Grid footprint size (NxN cells). Auto-calculated from the unit's actual
    /// renderer bounds, same approach as buildings. Cached after first computation.
    /// </summary>
    public int FootprintSize
    {
        get
        {
            if (cachedFootprint > 0) return cachedFootprint;
            cachedFootprint = ComputeFootprintFromBounds();
            return cachedFootprint;
        }
    }

    private int ComputeFootprintFromBounds()
    {
        float cellSize = GridSystem.Instance != null ? GridSystem.Instance.CellSize : 2f;

        // Try to get XZ size from renderer bounds
        if (BoundsHelper.TryGetCombinedBounds(gameObject, out var bounds))
        {
            float maxXZ = Mathf.Max(bounds.size.x, bounds.size.z);
            return Mathf.Clamp(Mathf.CeilToInt(maxXZ / cellSize), 1, 4);
        }

        // Fallback: derive from unitRadius
        float radius = data != null ? data.unitRadius : 0.5f;
        return Mathf.Clamp(Mathf.CeilToInt(radius * 2f / cellSize), 1, 4);
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
        combat = GetComponent<UnitCombat>();
        stateMachine = GetComponent<UnitStateMachine>();

        Debug.Assert(health != null, $"[Unit] {gameObject.name} missing Health component", this);
        Debug.Assert(movement != null, $"[Unit] {gameObject.name} missing UnitMovement component", this);
        Debug.Assert(stateMachine != null, $"[Unit] {gameObject.name} missing UnitStateMachine component", this);
    }

    [Server]
    public void Initialize(UnitData unitData, int team)
    {
        Debug.Assert(unitData != null, $"[Unit] {gameObject.name} Initialize called with null unitData", this);
        data = unitData;
        teamId = team;
        unitDataId = unitData.unitName;

        Debug.Assert(health != null, $"[Unit] {gameObject.name} Initialize: health is null", this);
        health.Initialize(unitData.maxHealth, team);

        if (GameDebug.UnitLifecycle)
            Debug.Log($"[Unit] INIT {gameObject.name} data={unitData.unitName} team={team} hp={unitData.maxHealth} " +
                $"atk={unitData.attackDamage} spd={unitData.moveSpeed} rangeCells={unitData.attackRangeCells} " +
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
        Debug.Assert(health != null, $"[Unit] {gameObject.name} OnEnable: health is null", this);
        health.OnDeath += HandleDeath;
    }

    private void OnDisable()
    {
        Debug.Assert(health != null, $"[Unit] {gameObject.name} OnDisable: health is null", this);
        health.OnDeath -= HandleDeath;

    }

    private void HandleDeath(GameObject killer)
    {
        // HandleDeath is driven by Health.OnDeath which only fires on server,
        // but guard defensively in case the call chain changes.
        if (!isServer) return;

        Debug.Assert(data != null, $"[Unit] {gameObject.name} HandleDeath: data is null", this);
        int bounty = data.goldBounty;
        if (GameDebug.UnitLifecycle)
            Debug.Log($"[Unit] DEATH {gameObject.name} team={teamId} killer={killer?.name ?? "null"} bounty={bounty}");
        EventBus.Raise(new UnitKilledEvent(gameObject, killer, bounty));

        float delay = 2f;
        if (stateMachine != null && stateMachine.Animator != null)
            delay = stateMachine.Animator.GetDeathDuration(2f) + 0.2f;
        if (GameDebug.UnitLifecycle)
            Debug.Log($"[Unit] {gameObject.name} will be destroyed in {delay:F1}s");
        Invoke(nameof(ServerDestroy), delay);
    }

    [Server]
    private void ServerDestroy()
    {
        NetworkServer.Destroy(gameObject);
    }
}
