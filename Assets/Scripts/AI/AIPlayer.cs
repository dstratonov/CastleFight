using UnityEngine;
using System.Collections.Generic;

public class AIPlayer : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField] private float buildInterval = 5f;
    [SerializeField] private float initialDelay = 8f;

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

    private const int AIPlayerId = -100;
    private readonly List<string> ownedBuildingIds = new();

    public int Gold => gold;
    public int TeamId => teamId;
    public string RaceId => raceId;

    public void Initialize(int team, string race)
    {
        teamId = team;

        var config = Resources.Load<GameConfig>("GameConfig");
        gold = config != null ? config.startingGold : 500;
        income = config != null ? config.passiveIncomeAmount : 25;
        incomeTickInterval = config != null ? config.incomeTickInterval : 5f;

        var raceDb = RaceDatabase.Instance;
        if (raceDb == null)
        {
            Debug.LogError("[AI] RaceDatabase not found");
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
            Debug.LogError($"[AI] Race '{raceId}' not found in database");
            return;
        }

        DiscoverBuildZone();

        buildTimer = initialDelay;
        incomeTimer = incomeTickInterval;
        initialized = true;

        Debug.Log($"[AI] Initialized on team {teamId}, race={raceData.raceName}, gold={gold}, income={income}/tick" +
            (hasBuildZone ? $", buildZone center={buildZoneBounds.center} size={buildZoneBounds.size}" : ", NO build zone found"));
    }

    private void DiscoverBuildZone()
    {
        var zones = FindObjectsByType<BuildZone>(FindObjectsSortMode.None);
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
        Debug.LogWarning($"[AI] No BuildZone found for team {teamId}");
    }

    private void Update()
    {
        if (!initialized) return;

        incomeTimer -= Time.deltaTime;
        if (incomeTimer <= 0f)
        {
            gold += income;
            incomeTimer = incomeTickInterval;
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

        Vector3? pos = FindValidPosition();
        if (!pos.HasValue)
        {
            Debug.LogWarning("[AI] Could not find valid build position");
            return;
        }

        gold -= chosen.cost;
        var obj = BuildingManager.Instance.PlaceBuilding(
            chosen, pos.Value, Quaternion.identity, teamId, AIPlayerId);

        if (obj != null)
        {
            ownedBuildingIds.Add(chosen.buildingId);
            Debug.Log($"[AI] Built {chosen.buildingName} at {pos.Value:F1} " +
                $"(cost={chosen.cost}, gold remaining={gold}, total buildings={currentCount + 1})");
        }
        else
        {
            gold += chosen.cost;
            Debug.LogWarning($"[AI] PlaceBuilding returned null for {chosen.buildingName}");
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
            if (score > bestScore)
            {
                bestScore = score;
                best = building;
            }
        }

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

    private Vector3? FindValidPosition()
    {
        if (!hasBuildZone) return null;

        var grid = GridSystem.Instance;
        if (grid == null) return null;

        for (int attempt = 0; attempt < 20; attempt++)
        {
            float x = Random.Range(buildZoneBounds.min.x, buildZoneBounds.max.x);
            float z = Random.Range(buildZoneBounds.min.z, buildZoneBounds.max.z);
            Vector3 candidate = new(x, buildZoneBounds.center.y, z);

            candidate = grid.SnapToGrid(candidate);

            if (!grid.CanPlaceBuilding(candidate, teamId))
                continue;

            Vector2Int cell = grid.WorldToCell(candidate);
            int checkRadius = 2;
            bool areaClear = true;
            for (int dx = -checkRadius; dx <= checkRadius && areaClear; dx++)
            {
                for (int dz = -checkRadius; dz <= checkRadius && areaClear; dz++)
                {
                    Vector2Int c = new(cell.x + dx, cell.y + dz);
                    if (!grid.IsInBounds(c) || !grid.IsWalkable(c))
                        areaClear = false;
                }
            }

            if (areaClear)
                return candidate;
        }

        for (int attempt = 0; attempt < 10; attempt++)
        {
            float x = Random.Range(buildZoneBounds.min.x, buildZoneBounds.max.x);
            float z = Random.Range(buildZoneBounds.min.z, buildZoneBounds.max.z);
            Vector3 candidate = new(x, buildZoneBounds.center.y, z);
            candidate = grid.SnapToGrid(candidate);

            if (grid.CanPlaceBuilding(candidate, teamId))
                return candidate;
        }

        return null;
    }
}
