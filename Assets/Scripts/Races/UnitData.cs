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
    [Tooltip("Physical radius of this unit for separation/spacing. Larger for big creatures like Cyclops.")]
    public float unitRadius = 0.5f;

    [Header("Combat")]
    public float attackDamage = 10f;
    public float attackSpeed = 1f;
    [Tooltip("Attack range in grid cells. Expands the unit's footprint rectangle by this many cells in each direction. 1 = melee (adjacent cell).")]
    public int attackRangeCells = 1;
    [Tooltip("Radius in world units for detecting and aggroing on enemies.")]
    public float aggroRadius = 8f;
    public AttackType attackType = AttackType.Normal;
    public ArmorType armorType = ArmorType.Unarmored;
    public bool isRanged;
    public GameObject projectilePrefab;
    public float projectileSpeed = 10f;

    [Header("Economy")]
    public int goldBounty = 5;

    [Header("Abilities")]
    public AbilityData[] abilities;

#if UNITY_EDITOR
    private void OnValidate()
    {
        attackRangeCells = Mathf.Clamp(attackRangeCells, 1, isRanged ? 6 : 2);
        float rangeCellsWorld = attackRangeCells * 2f; // approx cellSize
        aggroRadius = Mathf.Max(aggroRadius, rangeCellsWorld);
    }
#endif
}
