using UnityEngine;
using Mirror;
using System;

public class Health : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnHealthChanged))]
    private float currentHealth;

    [SyncVar]
    private float maxHealth;

    [SyncVar]
    private int teamId;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float HealthPercent => maxHealth > 0 ? currentHealth / maxHealth : 0f;
    public bool IsDead => maxHealth > 0 && currentHealth <= 0;
    public int TeamId => teamId;

    public event Action<float, float> OnHealthUpdated;
    public event Action<GameObject> OnDeath;
    public event Action<float, GameObject> OnDamaged;

    [Server]
    public void Initialize(int hp, int team)
    {
        maxHealth = hp;
        currentHealth = hp;
        teamId = team;
    }

    [Server]
    public void TakeDamage(float amount, GameObject attacker)
    {
        if (IsDead || amount <= 0) return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        OnDamaged?.Invoke(amount, attacker);

        if (currentHealth <= 0)
        {
            OnDeath?.Invoke(attacker);
        }
    }

    [Server]
    public void Heal(float amount)
    {
        if (IsDead || amount <= 0) return;
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
    }

    [Server]
    public void SetMaxHealth(float newMax)
    {
        maxHealth = newMax;
        if (currentHealth > maxHealth)
            currentHealth = maxHealth;
    }

    private void OnHealthChanged(float oldHealth, float newHealth)
    {
        OnHealthUpdated?.Invoke(newHealth, maxHealth);

        if (oldHealth > 0 && newHealth <= 0 && maxHealth > 0)
        {
            OnDeath?.Invoke(null);
        }
    }
}
