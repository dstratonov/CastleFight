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

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Clamp attack range to valid bounds at author-time so runtime never needs to.
        // Melee: [0.3, 2], Ranged: [1, 8].
        if (!isRanged)
            attackRange = Mathf.Clamp(attackRange, 0.3f, 2f);
        else
            attackRange = Mathf.Clamp(attackRange, 1f, 8f);
    }
#endif
}
