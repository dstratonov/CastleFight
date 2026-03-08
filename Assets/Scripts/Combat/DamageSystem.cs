using UnityEngine;

public static class DamageSystem
{
    private static DamageTable damageTable;

    public static void Initialize(DamageTable table)
    {
        damageTable = table;
    }

    public static float CalculateDamage(float baseDamage, AttackType attackType, ArmorType armorType, float bonusMultiplier = 1f)
    {
        float typeMultiplier = 1f;
        if (damageTable != null)
            typeMultiplier = damageTable.GetMultiplier(attackType, armorType);

        float result = baseDamage * typeMultiplier * bonusMultiplier;

        if (GameDebug.Combat && typeMultiplier != 1f)
            Debug.Log($"[Dmg] {attackType} vs {armorType} mult={typeMultiplier:F2} base={baseDamage:F0} -> {result:F1}");

        return result;
    }

}
