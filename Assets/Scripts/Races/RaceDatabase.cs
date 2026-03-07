using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "CastleFight/Race Database")]
public class RaceDatabase : ScriptableObject
{
    private static RaceDatabase instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() { instance = null; }

    public static RaceDatabase Instance
    {
        get
        {
            if (instance == null)
                instance = Resources.Load<RaceDatabase>("RaceDatabase");
            return instance;
        }
    }

    [SerializeField] private RaceData[] races;

    private Dictionary<string, RaceData> raceLookup;

    public RaceData[] AllRaces => races;

    private void OnEnable()
    {
        BuildLookup();
    }

    private void BuildLookup()
    {
        raceLookup = new Dictionary<string, RaceData>();
        if (races == null) return;
        foreach (var race in races)
        {
            if (race != null)
                raceLookup[race.raceId] = race;
        }
    }

    public RaceData GetRace(string raceId)
    {
        if (raceLookup == null) BuildLookup();
        return raceLookup.TryGetValue(raceId, out var race) ? race : null;
    }

    public BuildingData GetBuildingData(string raceId, string buildingId)
    {
        var race = GetRace(raceId);
        return race?.GetBuilding(buildingId);
    }

    public UnitData GetUnitData(string raceId, string unitId)
    {
        var race = GetRace(raceId);
        return race?.GetUnit(unitId);
    }
}
