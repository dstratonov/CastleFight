using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class AbilitySystem : NetworkBehaviour
{
    private readonly List<AbilityInstance> abilities = new();
    private Unit unit;

    private void Awake()
    {
        unit = GetComponent<Unit>();
    }

    [Server]
    public void InitializeAbilities(AbilityData[] abilityDatas)
    {
        abilities.Clear();
        if (abilityDatas == null) return;

        foreach (var data in abilityDatas)
        {
            if (data == null) continue;
            abilities.Add(new AbilityInstance(data));
        }
    }

    private void Update()
    {
        if (!isServer) return;

        foreach (var ability in abilities)
        {
            ability.UpdateCooldown(Time.deltaTime);

            if (ability.Data.abilityType == AbilityType.Aura)
                ProcessAura(ability);
        }
    }

    [Server]
    public bool TryActivateAbility(int index, Vector3 targetPosition, GameObject targetObject = null)
    {
        if (index < 0 || index >= abilities.Count) return false;

        var ability = abilities[index];
        if (!ability.IsReady) return false;

        ExecuteAbility(ability, targetPosition, targetObject);
        ability.StartCooldown();
        return true;
    }

    [Server]
    private void ExecuteAbility(AbilityInstance ability, Vector3 position, GameObject target)
    {
        var data = ability.Data;

        switch (data.targetType)
        {
            case AbilityTargetType.SingleEnemy:
                if (target != null)
                    ApplyEffect(data, target);
                break;

            case AbilityTargetType.AreaEnemy:
                ApplyAreaEffect(data, position, true);
                break;

            case AbilityTargetType.AreaAlly:
                ApplyAreaEffect(data, position, false);
                break;

            case AbilityTargetType.AreaAll:
                ApplyAreaEffect(data, position, true);
                ApplyAreaEffect(data, position, false);
                break;

            case AbilityTargetType.Self:
                ApplyEffect(data, gameObject);
                break;

            case AbilityTargetType.SingleAlly:
                if (target != null)
                    ApplyEffect(data, target);
                break;
        }

        if (data.effectPrefab != null)
            RpcSpawnEffect(data.effectPrefab.name, position);
    }

    [Server]
    private void ApplyEffect(AbilityData data, GameObject target)
    {
        var health = target.GetComponent<Health>();
        if (health == null) return;

        if (data.value > 0)
        {
            var buffSystem = target.GetComponent<BuffSystem>();
            if (buffSystem != null)
                buffSystem.ApplyBuff(new Buff(data.abilityId, data.duration, data.value, data.abilityType == AbilityType.Aura));
        }
    }

    [Server]
    private void ApplyAreaEffect(AbilityData data, Vector3 center, bool targetEnemies)
    {
        int myTeam = unit != null ? unit.TeamId : 0;
        var colliders = Physics.OverlapSphere(center, data.radius);

        foreach (var col in colliders)
        {
            var targetHealth = col.GetComponent<Health>();
            if (targetHealth == null || targetHealth.IsDead) continue;

            bool isEnemy = targetHealth.TeamId != myTeam;
            if (targetEnemies == isEnemy)
                ApplyEffect(data, col.gameObject);
        }
    }

    [Server]
    private void ProcessAura(AbilityInstance ability)
    {
        if (!ability.IsReady) return;

        var data = ability.Data;
        int myTeam = unit != null ? unit.TeamId : 0;

        bool targetEnemies = data.targetType == AbilityTargetType.AreaEnemy;
        ApplyAreaEffect(data, transform.position, targetEnemies);
        ability.StartCooldown();
    }

    [ClientRpc]
    private void RpcSpawnEffect(string prefabName, Vector3 position)
    {
        // Visual effect spawning on clients
    }

    public AbilityInstance GetAbility(int index)
    {
        if (index < 0 || index >= abilities.Count) return null;
        return abilities[index];
    }

    public int AbilityCount => abilities.Count;
}

public class AbilityInstance
{
    public AbilityData Data { get; }
    public float CooldownRemaining { get; private set; }
    public bool IsReady => CooldownRemaining <= 0f;

    public AbilityInstance(AbilityData data)
    {
        Data = data;
        CooldownRemaining = 0f;
    }

    public void UpdateCooldown(float deltaTime)
    {
        if (CooldownRemaining > 0f)
            CooldownRemaining -= deltaTime;
    }

    public void StartCooldown()
    {
        CooldownRemaining = Data.cooldown;
    }
}
