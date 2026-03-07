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

    public Transform AttackTarget => targetHealth != null && !targetHealth.IsDead ? targetHealth.transform : null;

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
        else if (stateMachine != null && stateMachine.CurrentState == UnitState.Fighting)
        {
            stateMachine.SetState(UnitState.Idle);
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
        float attackRange = unit.Data != null ? unit.Data.attackRange : 2f;
        int enemyTeam = GetEnemyTeam();

        // Priority 1: nearest enemy unit
        var nearestEnemy = UnitManager.Instance?.FindNearestEnemy(transform.position, unit.TeamId, range);
        if (nearestEnemy != null)
        {
            targetHealth = nearestEnemy.GetComponent<Health>();
            MoveTowardTarget();
            return;
        }

        // Priority 2: nearest enemy building within engagement range
        Health nearestBuildingHealth = FindNearestEnemyBuilding(enemyTeam, range);
        if (nearestBuildingHealth != null)
        {
            targetHealth = nearestBuildingHealth;
            MoveTowardTarget();
            return;
        }

        // Priority 3: enemy castle when close enough
        Health castleHealth = FindEnemyCastle(enemyTeam);
        if (castleHealth != null && !castleHealth.IsDead)
        {
            float castleDist = Vector3.Distance(transform.position, castleHealth.transform.position);
            if (castleDist <= attackRange * 2f)
            {
                targetHealth = castleHealth;
                return;
            }
        }

        targetHealth = null;
        if (movement != null && !movement.HasPath && !movement.IsMoving)
            movement.SetDestinationToEnemyCastle();
    }

    private int GetEnemyTeam()
    {
        return TeamManager.Instance != null
            ? TeamManager.Instance.GetEnemyTeamId(unit.TeamId)
            : (unit.TeamId == 0 ? 1 : 0);
    }

    private Health FindNearestEnemyBuilding(int enemyTeam, float maxRange)
    {
        if (BuildingManager.Instance == null) return null;

        var buildings = BuildingManager.Instance.GetTeamBuildings(enemyTeam);
        Health nearest = null;
        float nearestDistSq = maxRange * maxRange;

        foreach (var b in buildings)
        {
            if (b == null) continue;
            var h = b.GetComponent<Health>();
            if (h == null || h.IsDead) continue;

            float distSq = (b.transform.position - transform.position).sqrMagnitude;
            if (distSq < nearestDistSq)
            {
                nearestDistSq = distSq;
                nearest = h;
            }
        }
        return nearest;
    }

    private Health FindEnemyCastle(int enemyTeam)
    {
        Castle[] castles = FindObjectsByType<Castle>(FindObjectsSortMode.None);
        foreach (var c in castles)
        {
            if (c.TeamId == enemyTeam)
                return c.Health;
        }
        return null;
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
            stateMachine?.SetState(UnitState.Idle);
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
