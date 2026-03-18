using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class DamageSystemTests
{
    private DamageTable table;

    [SetUp]
    public void SetUp()
    {
        table = ScriptableObject.CreateInstance<DamageTable>();
    }

    [TearDown]
    public void TearDown()
    {
        DamageSystem.Initialize(null);
        if (table != null)
            Object.DestroyImmediate(table);
    }

    // ================================================================
    //  No table (null) — multiplier defaults to 1.0
    // ================================================================

    [Test]
    public void CalculateDamage_NoTable_ReturnsBaseDamage()
    {
        DamageSystem.Initialize(null);
        float result = DamageSystem.CalculateDamage(100f, AttackType.Normal, ArmorType.Heavy);
        Assert.AreEqual(100f, result, 0.001f);
    }

    [Test]
    public void CalculateDamage_NoTable_BonusMultiplierStillApplied()
    {
        DamageSystem.Initialize(null);
        float result = DamageSystem.CalculateDamage(50f, AttackType.Pierce, ArmorType.Light, 2f);
        Assert.AreEqual(100f, result, 0.001f);
    }

    // ================================================================
    //  With table — spot-check key attack/armor combos
    // ================================================================

    [Test]
    public void CalculateDamage_NormalVsMedium_150Percent()
    {
        DamageSystem.Initialize(table);
        float result = DamageSystem.CalculateDamage(100f, AttackType.Normal, ArmorType.Medium);
        Assert.AreEqual(150f, result, 0.001f);
    }

    [Test]
    public void CalculateDamage_NormalVsFortified_70Percent()
    {
        DamageSystem.Initialize(table);
        float result = DamageSystem.CalculateDamage(100f, AttackType.Normal, ArmorType.Fortified);
        Assert.AreEqual(70f, result, 0.001f);
    }

    [Test]
    public void CalculateDamage_PierceVsLight_200Percent()
    {
        DamageSystem.Initialize(table);
        float result = DamageSystem.CalculateDamage(100f, AttackType.Pierce, ArmorType.Light);
        Assert.AreEqual(200f, result, 0.001f);
    }

    [Test]
    public void CalculateDamage_PierceVsFortified_35Percent()
    {
        DamageSystem.Initialize(table);
        float result = DamageSystem.CalculateDamage(100f, AttackType.Pierce, ArmorType.Fortified);
        Assert.AreEqual(35f, result, 0.001f);
    }

    [Test]
    public void CalculateDamage_MagicVsHeavy_200Percent()
    {
        DamageSystem.Initialize(table);
        float result = DamageSystem.CalculateDamage(100f, AttackType.Magic, ArmorType.Heavy);
        Assert.AreEqual(200f, result, 0.001f);
    }

    [Test]
    public void CalculateDamage_MagicVsHero_50Percent()
    {
        DamageSystem.Initialize(table);
        float result = DamageSystem.CalculateDamage(100f, AttackType.Magic, ArmorType.Hero);
        Assert.AreEqual(50f, result, 0.001f);
    }

    [Test]
    public void CalculateDamage_SiegeVsFortified_150Percent()
    {
        DamageSystem.Initialize(table);
        float result = DamageSystem.CalculateDamage(100f, AttackType.Siege, ArmorType.Fortified);
        Assert.AreEqual(150f, result, 0.001f);
    }

    [Test]
    public void CalculateDamage_SiegeVsLight_50Percent()
    {
        DamageSystem.Initialize(table);
        float result = DamageSystem.CalculateDamage(100f, AttackType.Siege, ArmorType.Light);
        Assert.AreEqual(50f, result, 0.001f);
    }

    [Test]
    public void CalculateDamage_HeroVsFortified_50Percent()
    {
        DamageSystem.Initialize(table);
        float result = DamageSystem.CalculateDamage(100f, AttackType.Hero, ArmorType.Fortified);
        Assert.AreEqual(50f, result, 0.001f);
    }

    [Test]
    public void CalculateDamage_HeroVsUnarmored_100Percent()
    {
        DamageSystem.Initialize(table);
        float result = DamageSystem.CalculateDamage(100f, AttackType.Hero, ArmorType.Unarmored);
        Assert.AreEqual(100f, result, 0.001f);
    }

    [Test]
    public void CalculateDamage_ChaosVsAllArmor_Always100Percent()
    {
        DamageSystem.Initialize(table);
        foreach (ArmorType armor in System.Enum.GetValues(typeof(ArmorType)))
        {
            float result = DamageSystem.CalculateDamage(100f, AttackType.Chaos, armor);
            Assert.AreEqual(100f, result, 0.001f, $"Chaos vs {armor} should be 1.0");
        }
    }

    // ================================================================
    //  Bonus multiplier
    // ================================================================

    [Test]
    public void CalculateDamage_BonusMultiplier_AppliedOnTopOfType()
    {
        DamageSystem.Initialize(table);
        float result = DamageSystem.CalculateDamage(100f, AttackType.Pierce, ArmorType.Light, 1.5f);
        Assert.AreEqual(300f, result, 0.001f);
    }

    [Test]
    public void CalculateDamage_ZeroBonusMultiplier_ReturnsZero()
    {
        DamageSystem.Initialize(table);
        float result = DamageSystem.CalculateDamage(100f, AttackType.Normal, ArmorType.Light, 0f);
        Assert.AreEqual(0f, result, 0.001f);
    }

    // ================================================================
    //  Zero and negative base damage
    // ================================================================

    [Test]
    public void CalculateDamage_ZeroBaseDamage_ReturnsZero()
    {
        DamageSystem.Initialize(table);
        float result = DamageSystem.CalculateDamage(0f, AttackType.Pierce, ArmorType.Light);
        Assert.AreEqual(0f, result, 0.001f);
    }

    // ================================================================
    //  DamageTable.GetMultiplier — verify every row
    // ================================================================

    [Test]
    public void DamageTable_NormalRow()
    {
        Assert.AreEqual(1.0f,  table.GetMultiplier(AttackType.Normal, ArmorType.Unarmored), 0.001f);
        Assert.AreEqual(1.0f,  table.GetMultiplier(AttackType.Normal, ArmorType.Light),     0.001f);
        Assert.AreEqual(1.5f,  table.GetMultiplier(AttackType.Normal, ArmorType.Medium),    0.001f);
        Assert.AreEqual(1.0f,  table.GetMultiplier(AttackType.Normal, ArmorType.Heavy),     0.001f);
        Assert.AreEqual(0.7f,  table.GetMultiplier(AttackType.Normal, ArmorType.Fortified), 0.001f);
        Assert.AreEqual(1.0f,  table.GetMultiplier(AttackType.Normal, ArmorType.Hero),      0.001f);
    }

    [Test]
    public void DamageTable_PierceRow()
    {
        Assert.AreEqual(1.5f,  table.GetMultiplier(AttackType.Pierce, ArmorType.Unarmored), 0.001f);
        Assert.AreEqual(2.0f,  table.GetMultiplier(AttackType.Pierce, ArmorType.Light),     0.001f);
        Assert.AreEqual(0.75f, table.GetMultiplier(AttackType.Pierce, ArmorType.Medium),    0.001f);
        Assert.AreEqual(1.0f,  table.GetMultiplier(AttackType.Pierce, ArmorType.Heavy),     0.001f);
        Assert.AreEqual(0.35f, table.GetMultiplier(AttackType.Pierce, ArmorType.Fortified), 0.001f);
        Assert.AreEqual(0.5f,  table.GetMultiplier(AttackType.Pierce, ArmorType.Hero),      0.001f);
    }

    [Test]
    public void DamageTable_MagicRow()
    {
        Assert.AreEqual(1.5f,  table.GetMultiplier(AttackType.Magic, ArmorType.Unarmored), 0.001f);
        Assert.AreEqual(1.25f, table.GetMultiplier(AttackType.Magic, ArmorType.Light),     0.001f);
        Assert.AreEqual(0.75f, table.GetMultiplier(AttackType.Magic, ArmorType.Medium),    0.001f);
        Assert.AreEqual(2.0f,  table.GetMultiplier(AttackType.Magic, ArmorType.Heavy),     0.001f);
        Assert.AreEqual(0.35f, table.GetMultiplier(AttackType.Magic, ArmorType.Fortified), 0.001f);
        Assert.AreEqual(0.5f,  table.GetMultiplier(AttackType.Magic, ArmorType.Hero),      0.001f);
    }

    [Test]
    public void DamageTable_SiegeRow()
    {
        Assert.AreEqual(1.0f,  table.GetMultiplier(AttackType.Siege, ArmorType.Unarmored), 0.001f);
        Assert.AreEqual(0.5f,  table.GetMultiplier(AttackType.Siege, ArmorType.Light),     0.001f);
        Assert.AreEqual(0.5f,  table.GetMultiplier(AttackType.Siege, ArmorType.Medium),    0.001f);
        Assert.AreEqual(0.5f,  table.GetMultiplier(AttackType.Siege, ArmorType.Heavy),     0.001f);
        Assert.AreEqual(1.5f,  table.GetMultiplier(AttackType.Siege, ArmorType.Fortified), 0.001f);
        Assert.AreEqual(0.5f,  table.GetMultiplier(AttackType.Siege, ArmorType.Hero),      0.001f);
    }

    [Test]
    public void DamageTable_HeroRow()
    {
        Assert.AreEqual(1.0f,  table.GetMultiplier(AttackType.Hero, ArmorType.Unarmored), 0.001f);
        Assert.AreEqual(1.0f,  table.GetMultiplier(AttackType.Hero, ArmorType.Light),     0.001f);
        Assert.AreEqual(1.0f,  table.GetMultiplier(AttackType.Hero, ArmorType.Medium),    0.001f);
        Assert.AreEqual(1.0f,  table.GetMultiplier(AttackType.Hero, ArmorType.Heavy),     0.001f);
        Assert.AreEqual(0.5f,  table.GetMultiplier(AttackType.Hero, ArmorType.Fortified), 0.001f);
        Assert.AreEqual(1.0f,  table.GetMultiplier(AttackType.Hero, ArmorType.Hero),      0.001f);
    }

    [Test]
    public void DamageTable_ChaosRow()
    {
        foreach (ArmorType armor in System.Enum.GetValues(typeof(ArmorType)))
        {
            Assert.AreEqual(1.0f, table.GetMultiplier(AttackType.Chaos, armor), 0.001f,
                $"Chaos vs {armor}");
        }
    }
}
