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

}
