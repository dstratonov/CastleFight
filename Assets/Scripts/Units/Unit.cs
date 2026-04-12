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
    Vector3 IAttackable.Position => transform.position;
    // TargetRadius is the round-body sphere radius used by distance checks.
    // Non-zero on units signals to AttackRangeHelper.DistanceToTarget that
    // it should use sphere math (centerDist - radius) instead of the cube
    // ClosestPoint fallback — cube math over-reports reach on diagonal
    // approaches and causes attackers to "in-range-lock" far from the
    // correct kissing position, clumping them on one side of the target.
    float IAttackable.TargetRadius => EffectiveRadius;
    Bounds IAttackable.WorldBounds
    {
        get
        {
            // Square body bounds sized to EffectiveRadius (the visual half-extent).
            // Used by FindAttackPosition for extended-target perimeter math;
            // for range checks DistanceToTarget now uses the sphere form
            // via TargetRadius so the cube corners don't over-report reach.
            float r = EffectiveRadius;
            return new Bounds(transform.position, new Vector3(r * 2f, r * 2f, r * 2f));
        }
    }
    TargetPriority IAttackable.Priority => TargetPriority.Unit;

    /// <summary>
    /// Full body half-extent in world units, used for target bounds and as the
    /// attacker's own "reach origin" radius. NOT clamped to any max — large
    /// creatures (dragons, cyclopes, hydras) keep their true visual size.
    /// RVO / local-avoidance uses a separately clamped radius (see UnitMovement).
    /// </summary>
    public float EffectiveRadius
    {
        get
        {
            if (data != null && data.unitRadius > 0f)
                return data.unitRadius;
            if (cachedRadius > 0f)
                return cachedRadius;
            cachedRadius = ComputeRadiusFromBounds();
            return cachedRadius;
        }
    }

    private float ComputeRadiusFromBounds()
    {
        // Horizontal half-extent of the visible mesh. No upper clamp — data-driven
        // values in UnitData are the primary source; this fallback only runs when
        // unitRadius is missing or zero.
        if (!BoundsHelper.TryGetCombinedBounds(gameObject, out var b))
            return 0.5f;
        float half = Mathf.Max(b.extents.x, b.extents.z);
        return Mathf.Max(half, 0.25f);
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

        if (movement != null)
            movement.ConfigureFromData(unitData);

        if (GameDebug.UnitLifecycle)
            Debug.Log($"[Unit] INIT {gameObject.name} data={unitData.unitName} team={team} hp={unitData.maxHealth} " +
                $"atk={unitData.attackDamage} spd={unitData.moveSpeed} range={unitData.attackRange:F1} " +
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
