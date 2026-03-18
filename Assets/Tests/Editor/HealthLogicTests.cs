using NUnit.Framework;

[TestFixture]
public class HealthLogicTests
{
    // ================================================================
    //  GetHealthPercent
    // ================================================================

    [Test]
    public void GetHealthPercent_FullHealth_Returns1()
    {
        Assert.AreEqual(1f, HealthLogic.GetHealthPercent(100f, 100f), 0.001f);
    }

    [Test]
    public void GetHealthPercent_HalfHealth_Returns05()
    {
        Assert.AreEqual(0.5f, HealthLogic.GetHealthPercent(50f, 100f), 0.001f);
    }

    [Test]
    public void GetHealthPercent_ZeroHealth_Returns0()
    {
        Assert.AreEqual(0f, HealthLogic.GetHealthPercent(0f, 100f), 0.001f);
    }

    [Test]
    public void GetHealthPercent_ZeroMaxHealth_Returns0()
    {
        Assert.AreEqual(0f, HealthLogic.GetHealthPercent(50f, 0f), 0.001f);
    }

    [Test]
    public void GetHealthPercent_NegativeMaxHealth_Returns0()
    {
        Assert.AreEqual(0f, HealthLogic.GetHealthPercent(50f, -10f), 0.001f);
    }

    // ================================================================
    //  IsDead
    // ================================================================

    [Test]
    public void IsDead_ZeroHealthPositiveMax_ReturnsTrue()
    {
        Assert.IsTrue(HealthLogic.IsDead(0f, 100f));
    }

    [Test]
    public void IsDead_PositiveHealth_ReturnsFalse()
    {
        Assert.IsFalse(HealthLogic.IsDead(1f, 100f));
    }

    [Test]
    public void IsDead_ZeroMaxHealth_ReturnsFalse()
    {
        // Uninitialized unit (maxHealth=0) should not be considered dead
        Assert.IsFalse(HealthLogic.IsDead(0f, 0f));
    }

    [Test]
    public void IsDead_NegativeHealth_ReturnsTrue()
    {
        Assert.IsTrue(HealthLogic.IsDead(-5f, 100f));
    }

    // ================================================================
    //  ApplyDamage
    // ================================================================

    [Test]
    public void ApplyDamage_ReducesHealth()
    {
        float result = HealthLogic.ApplyDamage(100f, 100f, 30f, false);
        Assert.AreEqual(70f, result, 0.001f);
    }

    [Test]
    public void ApplyDamage_ClampsToZero()
    {
        float result = HealthLogic.ApplyDamage(10f, 100f, 50f, false);
        Assert.AreEqual(0f, result, 0.001f);
    }

    [Test]
    public void ApplyDamage_AlreadyDead_NoChange()
    {
        float result = HealthLogic.ApplyDamage(0f, 100f, 30f, false);
        Assert.AreEqual(0f, result, 0.001f);
    }

    [Test]
    public void ApplyDamage_ZeroAmount_NoChange()
    {
        float result = HealthLogic.ApplyDamage(100f, 100f, 0f, false);
        Assert.AreEqual(100f, result, 0.001f);
    }

    [Test]
    public void ApplyDamage_NegativeAmount_NoChange()
    {
        float result = HealthLogic.ApplyDamage(100f, 100f, -10f, false);
        Assert.AreEqual(100f, result, 0.001f);
    }

    [Test]
    public void ApplyDamage_Invincible_NoChange()
    {
        float result = HealthLogic.ApplyDamage(100f, 100f, 50f, true);
        Assert.AreEqual(100f, result, 0.001f);
    }

    [Test]
    public void ApplyDamage_ExactKill()
    {
        float result = HealthLogic.ApplyDamage(50f, 100f, 50f, false);
        Assert.AreEqual(0f, result, 0.001f);
    }

    // ================================================================
    //  ApplyHeal
    // ================================================================

    [Test]
    public void ApplyHeal_IncreasesHealth()
    {
        float result = HealthLogic.ApplyHeal(50f, 100f, 30f);
        Assert.AreEqual(80f, result, 0.001f);
    }

    [Test]
    public void ApplyHeal_ClampsToMax()
    {
        float result = HealthLogic.ApplyHeal(90f, 100f, 50f);
        Assert.AreEqual(100f, result, 0.001f);
    }

    [Test]
    public void ApplyHeal_Dead_NoChange()
    {
        float result = HealthLogic.ApplyHeal(0f, 100f, 50f);
        Assert.AreEqual(0f, result, 0.001f);
    }

    [Test]
    public void ApplyHeal_ZeroAmount_NoChange()
    {
        float result = HealthLogic.ApplyHeal(50f, 100f, 0f);
        Assert.AreEqual(50f, result, 0.001f);
    }

    [Test]
    public void ApplyHeal_NegativeAmount_NoChange()
    {
        float result = HealthLogic.ApplyHeal(50f, 100f, -20f);
        Assert.AreEqual(50f, result, 0.001f);
    }

    // ================================================================
    //  ClampToMax
    // ================================================================

    [Test]
    public void ClampToMax_HealthAboveNewMax_Clamped()
    {
        float result = HealthLogic.ClampToMax(100f, 50f);
        Assert.AreEqual(50f, result, 0.001f);
    }

    [Test]
    public void ClampToMax_HealthBelowNewMax_Unchanged()
    {
        float result = HealthLogic.ClampToMax(30f, 50f);
        Assert.AreEqual(30f, result, 0.001f);
    }

    [Test]
    public void ClampToMax_HealthEqualsNewMax_Unchanged()
    {
        float result = HealthLogic.ClampToMax(50f, 50f);
        Assert.AreEqual(50f, result, 0.001f);
    }

    // ================================================================
    //  ShouldTriggerDeath
    // ================================================================

    [Test]
    public void ShouldTriggerDeath_TransitionToZero_ReturnsTrue()
    {
        Assert.IsTrue(HealthLogic.ShouldTriggerDeath(50f, 0f, 100f));
    }

    [Test]
    public void ShouldTriggerDeath_TransitionToNegative_ReturnsTrue()
    {
        Assert.IsTrue(HealthLogic.ShouldTriggerDeath(10f, -5f, 100f));
    }

    [Test]
    public void ShouldTriggerDeath_AlreadyDead_ReturnsFalse()
    {
        // old=0 → new=0: not a fresh death
        Assert.IsFalse(HealthLogic.ShouldTriggerDeath(0f, 0f, 100f));
    }

    [Test]
    public void ShouldTriggerDeath_StillAlive_ReturnsFalse()
    {
        Assert.IsFalse(HealthLogic.ShouldTriggerDeath(100f, 50f, 100f));
    }

    [Test]
    public void ShouldTriggerDeath_ZeroMaxHealth_ReturnsFalse()
    {
        // Uninitialized entity should never trigger death
        Assert.IsFalse(HealthLogic.ShouldTriggerDeath(0f, 0f, 0f));
    }

    [Test]
    public void ShouldTriggerDeath_OldHealthNegative_ReturnsFalse()
    {
        // Already dead before transition
        Assert.IsFalse(HealthLogic.ShouldTriggerDeath(-1f, 0f, 100f));
    }
}
