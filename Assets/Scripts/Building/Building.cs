using UnityEngine;
using Mirror;
using System.Collections.Generic;

[RequireComponent(typeof(Health))]
public class Building : NetworkBehaviour, ISelectable, IAttackable
{
    [SyncVar] private int teamId;
    [SyncVar] private int ownerId;

    private BuildingData data;
    private Health health;
    private Spawner spawner;
    private List<Vector2Int> occupiedCells = new();

    public BuildingData Data => data;
    public int TeamId => teamId;
    public int OwnerId => ownerId;
    public IReadOnlyList<Vector2Int> OccupiedCells => occupiedCells;
    public string DisplayName => data != null ? data.buildingName : "Building";
    Health ISelectable.Health => health;
    Health IAttackable.Health => health;
    ArmorType IAttackable.ArmorType => data != null ? data.armorType : ArmorType.Fortified;
    float IAttackable.TargetRadius => BoundsHelper.GetRadius(gameObject);
    TargetPriority IAttackable.Priority => TargetPriority.Building;

    public void SetOccupiedCells(List<Vector2Int> cells)
    {
        occupiedCells = cells;
    }

    private void Awake()
    {
        health = GetComponent<Health>();
        spawner = GetComponent<Spawner>();
        StripModelColliders();
    }

    private void StripModelColliders()
    {
        var model = transform.Find("Model");
        if (model == null) return;

        foreach (var col in model.GetComponentsInChildren<BoxCollider>(true))
            Destroy(col);

        FitFootprintCollider(Vector2.zero);
    }

    [Server]
    public void Initialize(BuildingData buildingData, int team, int owner)
    {
        data = buildingData;
        teamId = team;
        ownerId = owner;

        health.Initialize(data.maxHealth, team);

        if (data.footprintSize.x > 0 && data.footprintSize.y > 0)
            FitFootprintCollider(data.footprintSize);

        if (spawner != null && data.spawnedUnit != null)
        {
            spawner.Initialize(data.spawnedUnit, data.spawnInterval, team);
        }

        if (GameDebug.Building)
        {
            var col = GetComponent<BoxCollider>();
            string colSize = col != null
                ? $"collider=({col.bounds.size.x:F1}, {col.bounds.size.z:F1})"
                : "collider=NONE";
            BoundsHelper.TryGetCombinedBounds(gameObject, out var rBounds);
            Debug.Log($"[Build] {gameObject.name} initialized: type={data.buildingName} team={team} " +
                $"hp={data.maxHealth} pos={transform.position:F1} " +
                $"renderer=({rBounds.size.x:F1}, {rBounds.size.z:F1}) {colSize}");
        }
    }

    /// <summary>
    /// Creates a BoxCollider sized to the building's ground footprint.
    /// When explicitSize is non-zero, uses that directly (data-driven).
    /// Otherwise auto-detects from ground-level renderers with a cap.
    /// </summary>
    public static void FitFootprintCollider(GameObject go, Vector2 explicitSize)
    {
        if (!BoundsHelper.TryGetCombinedBounds(go, out Bounds fullBounds)) return;

        var box = go.GetComponent<BoxCollider>();
        if (box == null) box = go.AddComponent<BoxCollider>();

        float fpX, fpZ;
        Vector3 center;

        if (explicitSize.x > 0 && explicitSize.y > 0)
        {
            fpX = explicitSize.x;
            fpZ = explicitSize.y;
            center = new Vector3(fullBounds.center.x, fullBounds.center.y, fullBounds.center.z);
        }
        else
        {
            ComputeAutoFootprint(go, fullBounds, out fpX, out fpZ, out center);
        }

        box.center = go.transform.InverseTransformPoint(center);
        box.size = new Vector3(
            fpX / go.transform.lossyScale.x,
            fullBounds.size.y / go.transform.lossyScale.y,
            fpZ / go.transform.lossyScale.z
        );
    }

    private void FitFootprintCollider(Vector2 explicitSize)
    {
        FitFootprintCollider(gameObject, explicitSize);
    }

    private const float MaxFootprintScale = 1.0f; // Match visual model — units shouldn't path through visible geometry

    private static void ComputeAutoFootprint(GameObject go, Bounds fullBounds,
        out float fpX, out float fpZ, out Vector3 center)
    {
        float midY = fullBounds.center.y;
        Bounds footprint = default;
        bool found = false;

        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (r.bounds.min.y > midY) continue;
            if (!found) { footprint = r.bounds; found = true; }
            else footprint.Encapsulate(r.bounds);
        }

        if (!found) footprint = fullBounds;

        float maxX = fullBounds.size.x * MaxFootprintScale;
        float maxZ = fullBounds.size.z * MaxFootprintScale;
        fpX = Mathf.Min(footprint.size.x, maxX);
        fpZ = Mathf.Min(footprint.size.z, maxZ);
        center = new Vector3(footprint.center.x, fullBounds.center.y, footprint.center.z);
    }

    public override void OnStartClient()
    {
        if (GetComponent<WorldHealthBar>() == null)
            gameObject.AddComponent<WorldHealthBar>();
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
        // HandleDeath is driven by Health.OnDeath which only fires on server,
        // but guard defensively in case the call chain changes.
        if (!isServer) return;

        if (GameDebug.Building)
            Debug.Log($"[Build] {gameObject.name} DESTROYED by {(killer != null ? killer.name : "null")} team={teamId}");
        EventBus.Raise(new BuildingDestroyedEvent(gameObject, teamId));

        Invoke(nameof(ServerDestroy), 2f);
    }

    [Server]
    private void ServerDestroy()
    {
        NetworkServer.Destroy(gameObject);
    }
}
