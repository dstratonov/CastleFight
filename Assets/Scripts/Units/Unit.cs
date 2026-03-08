using UnityEngine;
using Mirror;

public class Unit : NetworkBehaviour
{
    [SyncVar] private int teamId;
    [SyncVar(hook = nameof(OnUnitDataIdChanged))]
    private string unitDataId;

    private UnitData data;
    private Health health;

    private float cachedRadius = -1f;

    public UnitData Data => data;
    public int TeamId => teamId;
    public bool IsDead => health != null && health.IsDead;

    public float EffectiveRadius
    {
        get
        {
            if (data != null && data.unitRadius > 0.5f)
                return data.unitRadius;
            if (cachedRadius > 0f)
                return cachedRadius;
            cachedRadius = ComputeRadiusFromBounds();
            return cachedRadius;
        }
    }

    private float ComputeRadiusFromBounds()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0.5f;
        Bounds combined = default;
        bool first = true;
        foreach (var r in renderers)
        {
            if (r is ParticleSystemRenderer) continue;
            if (first) { combined = r.bounds; first = false; }
            else combined.Encapsulate(r.bounds);
        }
        if (first) return 0.5f;
        return Mathf.Max(combined.extents.x, combined.extents.z);
    }

    private void Awake()
    {
        health = GetComponent<Health>();
    }

    [Server]
    public void Initialize(UnitData unitData, int team)
    {
        data = unitData;
        teamId = team;
        unitDataId = unitData.unitName;

        if (health != null)
            health.Initialize(unitData.maxHealth, team);
    }

    public override void OnStartClient()
    {
        if (data == null && !string.IsNullOrEmpty(unitDataId))
            ResolveUnitData(unitDataId);
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
        EventBus.Raise(new UnitKilledEvent(gameObject, killer, bounty));

        if (isServer)
            Invoke(nameof(ServerDestroy), 2f);
    }

    [Server]
    private void ServerDestroy()
    {
        NetworkServer.Destroy(gameObject);
    }
}
