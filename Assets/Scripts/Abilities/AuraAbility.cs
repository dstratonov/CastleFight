using UnityEngine;

public class AuraAbility : Ability
{
    [SerializeField] private float tickInterval = 1f;
    [SerializeField] private bool affectsEnemies;

    private float tickTimer;
    private bool isActive;
    private GameObject casterRef;
    private Health cachedCasterHealth;

    public override void Activate(GameObject caster, Vector3 targetPosition, GameObject targetObject = null)
    {
        casterRef = caster;
        cachedCasterHealth = casterRef.GetComponent<Health>();
        tickTimer = tickInterval;
        isActive = true;
        if (GameDebug.Combat)
            Debug.Log($"[Aura] {data.abilityId} activated on {caster.name} r={data.radius:F1} value={data.value:F1}");
    }

    public override void Deactivate()
    {
        isActive = false;
    }

    private void Update()
    {
        if (!isActive || casterRef == null) return;

        tickTimer -= Time.deltaTime;
        if (tickTimer <= 0f)
        {
            tickTimer = tickInterval;
            ApplyAuraEffect();
        }
    }

    private void ApplyAuraEffect()
    {
        if (UnitManager.Instance == null) return;
        int myTeam = cachedCasterHealth != null ? cachedCasterHealth.TeamId : -1;

        var units = UnitManager.Instance.GetUnitsInRadius(casterRef.transform.position, data.radius);
        int affected = 0;
        foreach (var unit in units)
        {
            if (unit == null || unit.IsDead) continue;

            bool isEnemy = unit.TeamId != myTeam;
            if (isEnemy != affectsEnemies) continue;

            var buffSystem = unit.GetComponent<BuffSystem>();
            if (buffSystem != null)
            {
                buffSystem.ApplyBuff(new Buff(data.abilityId, tickInterval * 1.5f, data.value, true));
                affected++;
            }
        }

        if (GameDebug.Combat && affected > 0)
            Debug.Log($"[Aura] {data.abilityId} tick: {affected} units {(affectsEnemies ? "enemies" : "allies")} affected");
    }
}
