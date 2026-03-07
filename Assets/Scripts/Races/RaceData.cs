using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "CastleFight/Race Data")]
public class RaceData : ScriptableObject
{
    public string raceId;
    public string raceName;
    [TextArea] public string description;
    public Sprite icon;
    public Color themeColor = Color.white;

    [Header("Buildings")]
    public BuildingData[] buildings;

    [Header("Tech Tree")]
    public TechTreeNode[] techTree;

    private Dictionary<string, BuildingData> buildingLookup;
    private Dictionary<string, UnitData> unitLookup;

    private void OnEnable()
    {
        BuildLookups();
    }

    private void BuildLookups()
    {
        buildingLookup = new Dictionary<string, BuildingData>();
        unitLookup = new Dictionary<string, UnitData>();

        if (buildings == null) return;
        foreach (var b in buildings)
        {
            if (b != null)
            {
                buildingLookup[b.buildingId] = b;
                if (b.spawnedUnit != null)
                    unitLookup[b.spawnedUnit.unitName] = b.spawnedUnit;
            }
        }
    }

    public BuildingData GetBuilding(string buildingId)
    {
        if (buildingLookup == null) BuildLookups();
        return buildingLookup.TryGetValue(buildingId, out var data) ? data : null;
    }

    public UnitData GetUnit(string unitId)
    {
        if (unitLookup == null) BuildLookups();
        return unitLookup.TryGetValue(unitId, out var data) ? data : null;
    }

    public BuildingData[] GetAvailableBuildings(List<string> unlockedBuildings)
    {
        var result = new List<BuildingData>();
        foreach (var b in buildings)
        {
            if (b == null) continue;
            if (IsBuildingUnlocked(b.buildingId, unlockedBuildings))
                result.Add(b);
        }
        return result.ToArray();
    }

    public bool IsBuildingUnlocked(string buildingId, List<string> existingBuildingIds)
    {
        if (techTree == null) return true;

        foreach (var node in techTree)
        {
            if (node.buildingId == buildingId)
            {
                if (node.prerequisites == null || node.prerequisites.Length == 0)
                    return true;

                foreach (var prereq in node.prerequisites)
                {
                    if (!existingBuildingIds.Contains(prereq))
                        return false;
                }
                return true;
            }
        }
        return true;
    }
}
