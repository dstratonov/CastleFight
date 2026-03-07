using UnityEngine;

[CreateAssetMenu(menuName = "CastleFight/Unit Data")]
public class UnitData : ScriptableObject
{
    public string unitName;
    public string displayName;
    [TextArea] public string description;
    public Sprite icon;
    public GameObject prefab;

    [Header("Health")]
    public int maxHealth = 100;

    [Header("Movement")]
    public float moveSpeed = 3.5f;

    [Header("Combat")]
    public float attackDamage = 10f;
    public float attackSpeed = 1f;
    public float attackRange = 2f;
    public AttackType attackType = AttackType.Normal;
    public ArmorType armorType = ArmorType.Unarmored;
    public bool isRanged;
    public GameObject projectilePrefab;
    public float projectileSpeed = 10f;

    [Header("Economy")]
    public int goldBounty = 5;

    [Header("Abilities")]
    public AbilityData[] abilities;
}
