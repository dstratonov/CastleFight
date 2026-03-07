using UnityEngine;

[CreateAssetMenu(menuName = "CastleFight/Building Data")]
public class BuildingData : ScriptableObject
{
    public string buildingId;
    public string buildingName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("Prefabs")]
    public GameObject prefab;
    public GameObject ghostPrefab;

    [Header("Stats")]
    public int cost = 50;
    public int maxHealth = 500;
    public ArmorType armorType = ArmorType.Fortified;

    [Header("Spawning")]
    public UnitData spawnedUnit;
    public float spawnInterval = 15f;

    [Header("Income")]
    public int incomeBonus;

    [Header("Tech")]
    public int tier;
    public string[] prerequisites;

    [Header("Abilities")]
    public AbilityData[] buildingAbilities;
}
