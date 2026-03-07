using UnityEngine;

public class AuraAbility : Ability
{
    [SerializeField] private float tickInterval = 1f;
    [SerializeField] private bool affectsEnemies;

    private float tickTimer;
    private bool isActive;
    private GameObject casterRef;

    public override void Activate(GameObject caster, Vector3 targetPosition, GameObject targetObject = null)
    {
        casterRef = caster;
        isActive = true;
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
        var colliders = Physics.OverlapSphere(casterRef.transform.position, data.radius);
        var casterHealth = casterRef.GetComponent<Health>();
        int myTeam = casterHealth != null ? casterHealth.TeamId : -1;

        foreach (var col in colliders)
        {
            var health = col.GetComponent<Health>();
            if (health == null || health.IsDead) continue;

            bool isEnemy = health.TeamId != myTeam;
            if (isEnemy != affectsEnemies) continue;

            var buffSystem = col.GetComponent<BuffSystem>();
            if (buffSystem != null)
                buffSystem.ApplyBuff(new Buff(data.abilityId, tickInterval * 1.5f, data.value, true));
        }
    }
}
