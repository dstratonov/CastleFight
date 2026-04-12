using UnityEngine;
using Mirror;
using Pathfinding;

[RequireComponent(typeof(Health))]
public class Building : NetworkBehaviour, ISelectable, IAttackable
{
    [SyncVar] private int teamId;
    [SyncVar] private int ownerId;

    private BuildingData data;
    private Health health;
    private Spawner spawner;

    public BuildingData Data => data;
    public int TeamId => teamId;
    public int OwnerId => ownerId;
    public string DisplayName => data != null ? data.buildingName : "Building";
    Health ISelectable.Health => health;
    Health IAttackable.Health => health;
    ArmorType IAttackable.ArmorType => data != null ? data.armorType : ArmorType.Fortified;
    // Position returns the VISUAL/COLLIDER center in world space (not the
    // pivot). Building prefabs often have their BoxCollider offset from the
    // transform pivot — FitFootprintCollider sets box.center = renderer center
    // relative to pivot — so using transform.position would make units face
    // the wrong spot.
    Vector3 IAttackable.Position
    {
        get
        {
            var col = GetComponent<BoxCollider>();
            if (col != null)
            {
                var c = col.bounds.center;
                c.y = transform.position.y; // keep facing horizontal
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
    TargetPriority IAttackable.Priority => TargetPriority.Building;

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

        // Add NavmeshCut to carve the A* Pro NavMesh around this building
        SetupNavmeshCut();

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

    private void SetupNavmeshCut()
    {
        var cut = GetComponent<NavmeshCut>();
        if (cut == null) cut = gameObject.AddComponent<NavmeshCut>();

        // Size from collider or data footprint.
        // CRITICAL: BoxCollider.center is a local offset — it's common for
        // building prefabs to have the collider offset from the pivot because
        // FitFootprintCollider uses the renderer's visual center. We must
        // mirror that offset in the NavmeshCut so the carved hole lands on
        // the actual building footprint and not on the transform pivot.
        var box = GetComponent<BoxCollider>();
        if (box != null)
        {
            cut.type = NavmeshCut.MeshType.Rectangle;
            // box.size and box.center are in the collider's local space;
            // NavmeshCut.rectangleSize / .center use the same local space
            // (both components share this GameObject's transform).
            cut.rectangleSize = new Vector2(box.size.x, box.size.z);
            cut.height = box.size.y;
            cut.center = box.center;
        }
        else if (data != null && data.footprintSize.x > 0)
        {
            cut.type = NavmeshCut.MeshType.Rectangle;
            cut.rectangleSize = new Vector2(data.footprintSize.x, data.footprintSize.y);
            cut.height = 5f;
            cut.center = Vector3.zero;
        }
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
