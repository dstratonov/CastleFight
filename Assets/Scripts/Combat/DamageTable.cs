using UnityEngine;
using System;

[CreateAssetMenu(menuName = "CastleFight/Damage Table")]
public class DamageTable : ScriptableObject
{
    [Serializable]
    public struct DamageMultiplierRow
    {
        public AttackType attackType;
        public float vsUnarmored;
        public float vsLight;
        public float vsMedium;
        public float vsHeavy;
        public float vsFortified;
        public float vsHero;

        public float GetMultiplier(ArmorType armor)
        {
            return armor switch
            {
                ArmorType.Unarmored => vsUnarmored,
                ArmorType.Light => vsLight,
                ArmorType.Medium => vsMedium,
                ArmorType.Heavy => vsHeavy,
                ArmorType.Fortified => vsFortified,
                ArmorType.Hero => vsHero,
                _ => 1f
            };
        }
    }

    [SerializeField] private DamageMultiplierRow[] rows = new DamageMultiplierRow[]
    {
        new() { attackType = AttackType.Normal,  vsUnarmored = 1.0f, vsLight = 1.0f, vsMedium = 1.5f, vsHeavy = 1.0f, vsFortified = 0.7f, vsHero = 1.0f },
        new() { attackType = AttackType.Pierce,  vsUnarmored = 1.5f, vsLight = 2.0f, vsMedium = 0.75f, vsHeavy = 1.0f, vsFortified = 0.35f, vsHero = 0.5f },
        new() { attackType = AttackType.Magic,   vsUnarmored = 1.5f, vsLight = 1.25f, vsMedium = 0.75f, vsHeavy = 2.0f, vsFortified = 0.35f, vsHero = 0.5f },
        new() { attackType = AttackType.Siege,   vsUnarmored = 1.0f, vsLight = 0.5f, vsMedium = 0.5f, vsHeavy = 0.5f, vsFortified = 1.5f, vsHero = 0.5f },
        new() { attackType = AttackType.Hero,    vsUnarmored = 1.0f, vsLight = 1.0f, vsMedium = 1.0f, vsHeavy = 1.0f, vsFortified = 0.5f, vsHero = 1.0f },
        new() { attackType = AttackType.Chaos,   vsUnarmored = 1.0f, vsLight = 1.0f, vsMedium = 1.0f, vsHeavy = 1.0f, vsFortified = 1.0f, vsHero = 1.0f },
    };

    public float GetMultiplier(AttackType attack, ArmorType armor)
    {
        foreach (var row in rows)
        {
            if (row.attackType == attack)
                return row.GetMultiplier(armor);
        }
        return 1f;
    }
}
