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

        return baseDamage * typeMultiplier * bonusMultiplier;
    }

    public static float CalculateDamageWithArmor(float baseDamage, AttackType attackType, ArmorType armorType, int armorValue, float bonusMultiplier = 1f)
    {
        float typedDamage = CalculateDamage(baseDamage, attackType, armorType, bonusMultiplier);
        float reduction = 1f - (armorValue * 0.06f / (1f + 0.06f * armorValue));
        return typedDamage * reduction;
    }
}
