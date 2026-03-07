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

    private bool isDead;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float HealthPercent => maxHealth > 0 ? currentHealth / maxHealth : 0f;
    public bool IsDead => isDead;
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
        isDead = false;
    }

    [Server]
    public void TakeDamage(float amount, GameObject attacker)
    {
        if (isDead || amount <= 0) return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        OnDamaged?.Invoke(amount, attacker);

        if (currentHealth <= 0)
        {
            isDead = true;
            OnDeath?.Invoke(attacker);
        }
    }

    [Server]
    public void Heal(float amount)
    {
        if (isDead || amount <= 0) return;
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
    }
}
