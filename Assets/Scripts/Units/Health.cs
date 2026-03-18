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

    [SyncVar]
    private GameObject lastAttacker;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float HealthPercent => HealthLogic.GetHealthPercent(currentHealth, maxHealth);
    public bool IsDead => HealthLogic.IsDead(currentHealth, maxHealth);
    public int TeamId => teamId;

    /// <summary>
    /// When true, TakeDamage is ignored. Used by stress-test tools
    /// to keep castles alive for extended testing.
    /// </summary>
    public bool Invincible { get; set; }

    public event Action<float, float> OnHealthUpdated;
    public event Action<GameObject> OnDeath;
    public event Action<float, GameObject> OnDamaged;

    [Server]
    public void Initialize(int hp, int team)
    {
        maxHealth = hp;
        currentHealth = hp;
        teamId = team;
        lastAttacker = null;
    }

    [Server]
    public void TakeDamage(float amount, GameObject attacker)
    {
        if (IsDead || amount <= 0 || Invincible) return;

        lastAttacker = attacker;
        currentHealth = Mathf.Max(0, currentHealth - amount);

        if (GameDebug.Health)
            Debug.Log($"[Health] {gameObject.name} took {amount:F1} dmg from {(attacker != null ? attacker.name : "null")} -> {currentHealth:F0}/{maxHealth:F0}");

        OnDamaged?.Invoke(amount, attacker);

        if (currentHealth <= 0 && GameDebug.Health)
            Debug.Log($"[Health] {gameObject.name} DIED, killer={attacker?.name}");
    }

    [Server]
    public void Heal(float amount)
    {
        if (IsDead || amount <= 0) return;
        float oldHealth = currentHealth;
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
        if (GameDebug.Health)
            Debug.Log($"[Health] {gameObject.name} healed {amount:F1} ({oldHealth:F0} -> {currentHealth:F0}/{maxHealth:F0})");
    }

    [Server]
    public void SetMaxHealth(float newMax)
    {
        float oldMax = maxHealth;
        maxHealth = newMax;
        if (currentHealth > maxHealth)
            currentHealth = maxHealth;
        if (GameDebug.Health)
            Debug.Log($"[Health] {gameObject.name} max HP changed {oldMax:F0} -> {maxHealth:F0} (current={currentHealth:F0})");
    }

    private void OnHealthChanged(float oldHealth, float newHealth)
    {
        OnHealthUpdated?.Invoke(newHealth, maxHealth);

        if (oldHealth > 0 && newHealth <= 0 && maxHealth > 0)
        {
            if (GameDebug.Health)
                Debug.Log($"[Health] {gameObject.name} OnHealthChanged DEATH trigger (old={oldHealth:F0} new={newHealth:F0} max={maxHealth:F0}) isServer={isServer}");

            // OnDeath drives game-logic events (UnitKilledEvent, BuildingDestroyedEvent,
            // grid updates, bounty gold, etc.) — must only fire on server to prevent
            // duplicate side effects on all clients.
            if (isServer)
                OnDeath?.Invoke(lastAttacker);
        }
    }
}
