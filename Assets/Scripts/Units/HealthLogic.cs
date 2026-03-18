using UnityEngine;

/// <summary>
/// Pure health logic extracted from Health for testability.
/// No MonoBehaviour, no Mirror, no network dependencies.
/// </summary>
public static class HealthLogic
{
    public static float GetHealthPercent(float currentHealth, float maxHealth)
    {
        return maxHealth > 0 ? currentHealth / maxHealth : 0f;
    }

    public static bool IsDead(float currentHealth, float maxHealth)
    {
        return maxHealth > 0 && currentHealth <= 0;
    }

    /// <summary>
    /// Apply damage and return new health. Returns unchanged health if dead, zero damage, or invincible.
    /// </summary>
    public static float ApplyDamage(float currentHealth, float maxHealth, float amount, bool invincible)
    {
        if (IsDead(currentHealth, maxHealth) || amount <= 0 || invincible)
            return currentHealth;
        return Mathf.Max(0, currentHealth - amount);
    }

    /// <summary>
    /// Apply heal and return new health. Returns unchanged health if dead or zero amount.
    /// </summary>
    public static float ApplyHeal(float currentHealth, float maxHealth, float amount)
    {
        if (IsDead(currentHealth, maxHealth) || amount <= 0)
            return currentHealth;
        return Mathf.Min(currentHealth + amount, maxHealth);
    }

    /// <summary>
    /// Clamp current health after max health changes.
    /// </summary>
    public static float ClampToMax(float currentHealth, float newMaxHealth)
    {
        return currentHealth > newMaxHealth ? newMaxHealth : currentHealth;
    }

    /// <summary>
    /// Determine if death event should fire based on health transition.
    /// </summary>
    public static bool ShouldTriggerDeath(float oldHealth, float newHealth, float maxHealth)
    {
        return oldHealth > 0 && newHealth <= 0 && maxHealth > 0;
    }
}
