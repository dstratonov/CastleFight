using UnityEngine;
using UnityEngine.Serialization;

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
    [Tooltip("Attack range in WORLD UNITS, measured as extra reach beyond the attacker's radius. 0.5 = melee, 2 = short reach, 5+ = ranged. A target is in range when the distance between attackerPos and the closest point on the target's bounds is <= attackerRadius + attackRange.")]
    public float attackRange = 0.5f;
    [Tooltip("Radius in world units for detecting and aggroing on enemies.")]
    public float aggroRadius = 8f;
    public AttackType attackType = AttackType.Normal;
    public ArmorType armorType = ArmorType.Unarmored;
    public bool isRanged;
    public GameObject projectilePrefab;
    public float projectileSpeed = 10f;

    // ---------- Legacy (pre-rework) ----------
    // Old grid-cell-based range. Kept for asset migration — OnValidate copies
    // its value into attackRange the first time this asset is loaded after
    // the rework. Hidden from the inspector.
    [HideInInspector]
    [SerializeField]
    [FormerlySerializedAs("attackRangeCells")]
    private int legacyAttackRangeCells = -1;

    [Header("Economy")]
    public int goldBounty = 5;

    [Header("Abilities")]
    public AbilityData[] abilities;

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Migrate old cell-based range into the new world-space value.
        // Mapping: 1 cell = 2 world units (grid cellSize is 2f).
        if (legacyAttackRangeCells > 0 && attackRange <= 0.01f)
        {
            attackRange = legacyAttackRangeCells * 2f;
            legacyAttackRangeCells = 0; // consumed
            UnityEditor.EditorUtility.SetDirty(this);
        }

        if (attackRange < 0f) attackRange = 0f;
        // Aggro must at least cover attack range so ranged units can see targets
        float minAggro = attackRange + 1f;
        if (aggroRadius < minAggro) aggroRadius = minAggro;
    }
#endif

    // Temporary compatibility shim for editor tools that still reference
    // the old field name. Reads/writes go through legacyAttackRangeCells
    // which is migrated to attackRange on next OnValidate.
#if UNITY_EDITOR
    public int attackRangeCells
    {
        get => legacyAttackRangeCells > 0 ? legacyAttackRangeCells : Mathf.RoundToInt(attackRange / 2f);
        set
        {
            legacyAttackRangeCells = value;
            attackRange = value * 2f;
        }
    }
#endif
}
