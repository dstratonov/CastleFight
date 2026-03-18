using UnityEngine;
using System.Collections.Generic;

public class AIPlayer : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField] private float buildInterval = 10f;
    [SerializeField] private float initialDelay = 5f;

    [Header("Limits")]
    [SerializeField] private int maxBuildingsTotal = 15;
    [SerializeField] private int maxCopiesPerBuilding = 3;

    [Header("Strategy")]
    [SerializeField, Range(0f, 1f)] private float aggressiveness = 0.3f;

    private int teamId;
    private string raceId;
    private RaceData raceData;
    private int gold;
    private int income;
    private float incomeTickInterval;
    private float incomeTimer;
    private float buildTimer;
    private Bounds buildZoneBounds;
    private bool hasBuildZone;
    private bool initialized;
    private float summaryTimer;

    private static int nextAIPlayerId = -100;
    private int aiPlayerId;
    private readonly List<string> ownedBuildingIds = new();

    public int Gold => gold;
    public int TeamId => teamId;
    public string RaceId => raceId;

    private string Tag => $"[AI t{teamId}]";

    public void Initialize(int team, string race)
    {
        teamId = team;
        aiPlayerId = nextAIPlayerId--;


        var config = GameConfig.Instance;
        gold = config != null ? config.startingGold : 500;
        income = config != null ? config.passiveIncomeAmount : 25;
        incomeTickInterval = config != null ? config.incomeTickInterval : 5f;

        var raceDb = RaceDatabase.Instance;
        if (raceDb == null)
        {
            Debug.LogError($"{Tag} RaceDatabase not found");
            return;
        }

        if (string.IsNullOrEmpty(race))
        {
            var all = raceDb.AllRaces;
            if (all != null && all.Length > 0)
                race = all[Random.Range(0, all.Length)].raceId;
        }
        raceId = race;
        raceData = raceDb.GetRace(raceId);

        if (raceData == null)
        {
            Debug.LogError($"{Tag} Race '{raceId}' not found in database");
            return;
        }

        DiscoverBuildZone();

        buildTimer = initialDelay;
        incomeTimer = incomeTickInterval;
        initialized = true;

        Debug.Log($"{Tag} Initialized race={raceData.raceName}, gold={gold}, income={income}/tick" +
            (hasBuildZone ? $", buildZone center={buildZoneBounds.center} size={buildZoneBounds.size}" : ", NO build zone found"));
    }

    private void OnEnable()
    {
        EventBus.Subscribe<BuildingDestroyedEvent>(OnBuildingDestroyed);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<BuildingDestroyedEvent>(OnBuildingDestroyed);
    }

    private void OnBuildingDestroyed(BuildingDestroyedEvent evt)
    {
        if (evt.TeamId != teamId) return;

        var building = evt.Building?.GetComponent<Building>();
        if (building?.Data == null) return;

        string destroyedId = building.Data.buildingId;
        if (ownedBuildingIds.Remove(destroyedId))
        {
            if (building.Data.incomeBonus > 0)
                income = Mathf.Max(0, income - building.Data.incomeBonus);

            if (GameDebug.AI)
                Debug.Log($"{Tag} Building {destroyedId} destroyed, removed from owned list (remaining copies={CountOwnedCopies(destroyedId)}, income={income})");
        }
    }

    private void DiscoverBuildZone()
    {
        var zones = GameRegistry.BuildZones;
        foreach (var zone in zones)
        {
            if (zone.TeamId == teamId)
            {
                var col = zone.GetComponent<BoxCollider>();
                if (col != null)
                {
                    buildZoneBounds = col.bounds;
                    hasBuildZone = true;
                    return;
                }
            }
        }
        Debug.LogWarning($"{Tag} No BuildZone found for team {teamId}");
    }

    private void Update()
    {
        if (!initialized) return;
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver) return;

        incomeTimer -= Time.deltaTime;
        if (incomeTimer <= 0f)
        {
            gold += income;
            incomeTimer = incomeTickInterval;
            if (GameDebug.Economy)
                Debug.Log($"{Tag} Income +{income}, gold={gold}");
        }

        if (teamId == 0)
        {
            summaryTimer -= Time.deltaTime;
            if (summaryTimer <= 0f)
            {
                summaryTimer = 15f;
                LogGameSummary();
            }
        }

        buildTimer -= Time.deltaTime;
        if (buildTimer <= 0f)
        {
            buildTimer = buildInterval;
            TryBuild();
        }
    }

    private void TryBuild()
    {
        if (raceData == null || raceData.buildings == null) return;
        if (BuildingManager.Instance == null) return;

        int currentCount = BuildingManager.Instance.GetTeamBuildings(teamId).Count;
        if (currentCount >= maxBuildingsTotal) return;

        BuildingData chosen = ChooseBuilding();
        if (chosen == null) return;
        if (gold < chosen.cost) return;

        Vector3? pos = FindValidPosition(chosen);
        if (!pos.HasValue)
        {
            if (GameDebug.AI)
                Debug.LogWarning($"{Tag} Could not find valid build position for {chosen.buildingName}");
            return;
        }

        gold -= chosen.cost;
        var obj = BuildingManager.Instance.PlaceBuilding(
            chosen, pos.Value, Quaternion.identity, teamId, aiPlayerId);

        if (obj != null)
        {
            ownedBuildingIds.Add(chosen.buildingId);

            if (chosen.incomeBonus > 0)
            {
                income += chosen.incomeBonus;
                if (GameDebug.AI)
                    Debug.Log($"{Tag} Income bonus +{chosen.incomeBonus} from {chosen.buildingName}, total income={income}");
            }

            Debug.Log($"{Tag} Built {chosen.buildingName} at {pos.Value:F1} " +
                $"(cost={chosen.cost}, gold={gold}, buildings={currentCount + 1})");
        }
        else
        {
            gold += chosen.cost;
            Debug.LogWarning($"{Tag} PlaceBuilding returned null for {chosen.buildingName}");
        }
    }

    private BuildingData ChooseBuilding()
    {
        float gameTime = Time.timeSinceLevelLoad;

        BuildingData best = null;
        float bestScore = float.MinValue;

        foreach (var building in raceData.buildings)
        {
            if (building == null) continue;
            if (gold < building.cost) continue;

            int copies = CountOwnedCopies(building.buildingId);
            if (copies >= maxCopiesPerBuilding) continue;

            if (!IsTechUnlocked(building)) continue;

            float score = ScoreBuilding(building, copies, gameTime);
            if (GameDebug.AI)
                Debug.Log($"{Tag} score {building.buildingName}: {score:F1} (copies={copies}, cost={building.cost})");
            if (score > bestScore)
            {
                bestScore = score;
                best = building;
            }
        }

        if (GameDebug.AI && best != null)
            Debug.Log($"{Tag} Chose {best.buildingName} score={bestScore:F1}");
        return best;
    }

    private float ScoreBuilding(BuildingData building, int existingCopies, float gameTime)
    {
        float score = 100f;

        float tierPenalty = building.tier * 15f * (1f - aggressiveness);
        score -= tierPenalty;

        score -= existingCopies * 25f;

        float costEfficiency = 1f / Mathf.Max(building.cost, 1f) * 100f;
        score += costEfficiency;

        if (building.spawnedUnit != null)
        {
            float dps = building.spawnedUnit.attackDamage * building.spawnedUnit.attackSpeed;
            score += dps * 0.5f;
        }

        if (building.incomeBonus > 0)
            score += building.incomeBonus * 2f;

        bool earlyGame = gameTime < 60f;
        bool midGame = gameTime >= 60f && gameTime < 180f;

        if (earlyGame && building.tier <= 1)
            score += 30f;
        else if (midGame && building.tier <= 2)
            score += 15f;
        else if (!earlyGame && !midGame && building.tier >= 3)
            score += 20f * aggressiveness;

        if (gold > building.cost * 3)
            score += 10f;

        score += Random.Range(-10f, 10f);

        return score;
    }

    private bool IsTechUnlocked(BuildingData building)
    {
        if (raceData.techTree == null || raceData.techTree.Length == 0)
            return true;
        return raceData.IsBuildingUnlocked(building.buildingId, ownedBuildingIds);
    }

    private int CountOwnedCopies(string buildingId)
    {
        int count = 0;
        foreach (var id in ownedBuildingIds)
        {
            if (id == buildingId)
                count++;
        }
        return count;
    }

    private Vector3? FindValidPosition(BuildingData data)
    {
        if (!hasBuildZone) return null;

        var grid = GridSystem.Instance;
        if (grid == null) return null;

        int footprintRadius = data != null && data.prefab != null ? EstimateBuildingRadius(data) : 2;

        for (int attempt = 0; attempt < 30; attempt++)
        {
            float x = Random.Range(buildZoneBounds.min.x, buildZoneBounds.max.x);
            float z = Random.Range(buildZoneBounds.min.z, buildZoneBounds.max.z);
            Vector3 candidate = new(x, buildZoneBounds.center.y, z);

            candidate = grid.SnapToGrid(candidate);

            if (!grid.CanPlaceBuilding(candidate, teamId))
                continue;

            if (!IsAreaClear(grid, candidate, footprintRadius + 1))
                continue;

            if (!IsAreaFreeOfUnits(candidate, footprintRadius))
                continue;

            if (!IsAreaFreeOfBuildings(candidate, footprintRadius))
                continue;

            return candidate;
        }

        return null;
    }

    private int EstimateBuildingRadius(BuildingData data)
    {
        if (data.prefab == null) return 2;
        var renderers = data.prefab.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 2;
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers)
            b.Encapsulate(r.bounds);
        float maxExtent = Mathf.Max(b.extents.x, b.extents.z);
        var grid = GridSystem.Instance;
        float cellSize = grid != null ? grid.CellSize : 2f;
        return Mathf.CeilToInt(maxExtent / cellSize) + 1;
    }

    private bool IsAreaClear(GridSystem grid, Vector3 position, int checkRadius)
    {
        Vector2Int cell = grid.WorldToCell(position);
        for (int dx = -checkRadius; dx <= checkRadius; dx++)
        {
            for (int dz = -checkRadius; dz <= checkRadius; dz++)
            {
                Vector2Int c = new(cell.x + dx, cell.y + dz);
                if (!grid.IsInBounds(c) || !grid.IsWalkable(c))
                    return false;
            }
        }
        return true;
    }

    private bool IsAreaFreeOfUnits(Vector3 position, int radius)
    {
        if (UnitManager.Instance == null)
        {
            Debug.LogError("[AIPlayer] IsAreaFreeOfUnits: UnitManager.Instance is null — cannot check");
            return false;
        }
        var grid = GridSystem.Instance;
        float checkDist = grid != null ? radius * grid.CellSize + 1f : radius * 2f + 1f;
        var nearby = UnitManager.Instance.GetUnitsInRadius(position, checkDist);
        return nearby.Count == 0;
    }

    private bool IsAreaFreeOfBuildings(Vector3 position, int radius)
    {
        if (BuildingManager.Instance == null)
        {
            Debug.LogError("[AIPlayer] IsAreaFreeOfBuildings: BuildingManager.Instance is null — cannot check");
            return false;
        }
        var grid = GridSystem.Instance;
        float cellSize = grid != null ? grid.CellSize : 2f;
        float minDist = (radius + 1) * cellSize;

        var buildings = BuildingManager.Instance.GetTeamBuildings(teamId);
        foreach (var b in buildings)
        {
            if (b == null) continue;
            float dist = Vector3.Distance(position, b.transform.position);
            if (dist < minDist)
                return false;
        }
        return true;
    }

    private void LogGameSummary()
    {
        int u0 = 0, u1 = 0;
        if (UnitManager.Instance != null)
        {
            u0 = UnitManager.Instance.GetTeamUnits(0).Count;
            u1 = UnitManager.Instance.GetTeamUnits(1).Count;
        }

        int b0 = 0, b1 = 0;
        if (BuildingManager.Instance != null)
        {
            b0 = BuildingManager.Instance.GetTeamBuildings(0).Count;
            b1 = BuildingManager.Instance.GetTeamBuildings(1).Count;
        }

        float c0hp = -1, c0max = -1, c1hp = -1, c1max = -1;
        var castles = GameRegistry.Castles;
        foreach (var c in castles)
        {
            if (c == null || c.Health == null) continue;
            if (c.TeamId == 0) { c0hp = c.Health.CurrentHealth; c0max = c.Health.MaxHealth; }
            else if (c.TeamId == 1) { c1hp = c.Health.CurrentHealth; c1max = c.Health.MaxHealth; }
        }

        Debug.Log($"[Summary] t={Time.timeSinceLevelLoad:F0}s" +
            $" | T0: units={u0} bld={b0} castle={c0hp:F0}/{c0max:F0}" +
            $" | T1: units={u1} bld={b1} castle={c1hp:F0}/{c1max:F0}");
    }
}
