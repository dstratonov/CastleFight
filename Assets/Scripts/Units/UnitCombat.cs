using UnityEngine;
using Mirror;

public class UnitCombat : NetworkBehaviour
{
    [SerializeField] private float scanInterval = 0.25f;

    private Unit unit;
    private UnitMovement movement;
    private UnitStateMachine stateMachine;
    private Health targetHealth;
    private float scanTimer;
    private float attackTimer;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        movement = GetComponent<UnitMovement>();
        stateMachine = GetComponent<UnitStateMachine>();
    }

    private void Update()
    {
        if (!isServer || unit == null || unit.IsDead) return;

        scanTimer -= Time.deltaTime;
        if (scanTimer <= 0f)
        {
            scanTimer = scanInterval;
            ScanForTarget();
        }

        if (targetHealth != null && !targetHealth.IsDead)
        {
            float dist = Vector3.Distance(transform.position, targetHealth.transform.position);
            float range = unit.Data != null ? unit.Data.attackRange : 2f;

            if (dist <= range)
            {
                movement?.Stop();
                stateMachine?.SetState(UnitState.Fighting);
                TryAttack();
            }
            else if (movement != null && !movement.IsMoving)
            {
                MoveTowardTarget();
            }
        }
    }

    [Server]
    private void ScanForTarget()
    {
        if (targetHealth != null && !targetHealth.IsDead)
        {
            float dist = Vector3.Distance(transform.position, targetHealth.transform.position);
            float scanRange = unit.Data != null ? unit.Data.attackRange * 2f : 10f;
            if (dist <= scanRange) return;
        }

        float range = unit.Data != null ? unit.Data.attackRange * 2f : 10f;

        var nearestEnemy = UnitManager.Instance?.FindNearestEnemy(transform.position, unit.TeamId, range);
        if (nearestEnemy != null)
        {
            targetHealth = nearestEnemy.GetComponent<Health>();
            MoveTowardTarget();
            return;
        }

        targetHealth = null;
        if (movement != null && !movement.HasPath && !movement.IsMoving)
            movement.SetDestinationToEnemyCastle();
    }

    [Server]
    private void MoveTowardTarget()
    {
        if (targetHealth == null || movement == null) return;
        movement.SetDestinationWorld(targetHealth.transform.position);
    }

    [Server]
    private void TryAttack()
    {
        attackTimer -= Time.deltaTime;
        if (attackTimer > 0f) return;

        float attackSpeed = unit.Data != null ? unit.Data.attackSpeed : 1f;
        attackTimer = 1f / attackSpeed;

        if (targetHealth == null || targetHealth.IsDead)
        {
            targetHealth = null;
            movement?.Resume();
            return;
        }

        float baseDamage = unit.Data != null ? unit.Data.attackDamage : 5f;
        AttackType atkType = unit.Data != null ? unit.Data.attackType : AttackType.Normal;
        ArmorType defArmor = ArmorType.Unarmored;

        var targetUnit = targetHealth.GetComponent<Unit>();
        if (targetUnit?.Data != null)
            defArmor = targetUnit.Data.armorType;

        float damage = DamageSystem.CalculateDamage(baseDamage, atkType, defArmor);
        targetHealth.TakeDamage(damage, gameObject);

        Vector3 lookDir = (targetHealth.transform.position - transform.position).normalized;
        if (lookDir != Vector3.zero)
        {
            lookDir.y = 0;
            transform.forward = lookDir;
        }

        RpcPlayAttackAnimation();
    }

    [ClientRpc]
    private void RpcPlayAttackAnimation()
    {
        var unitAnim = GetComponent<UnitAnimator>();
        if (unitAnim != null)
            unitAnim.PlayAttack();
    }
}
