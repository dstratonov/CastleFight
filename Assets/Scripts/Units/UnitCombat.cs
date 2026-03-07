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
            float dist = DistanceToTarget(targetHealth);
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
            float dist = DistanceToTarget(targetHealth);
            float scanRange = unit.Data != null ? unit.Data.attackRange * 2f : 10f;
            if (dist <= scanRange) return;
        }

        float range = unit.Data != null ? unit.Data.attackRange * 2f : 10f;
        int enemyTeam = GetEnemyTeam();

        // Priority 1: nearest enemy unit
        var nearestEnemy = UnitManager.Instance?.FindNearestEnemy(transform.position, unit.TeamId, range);
        if (nearestEnemy != null)
        {
            targetHealth = nearestEnemy.GetComponent<Health>();
            MoveTowardTarget();
            return;
        }

        // Priority 2: nearest enemy building
        Health nearestBuildingHealth = FindNearestEnemyBuilding(enemyTeam, range);
        if (nearestBuildingHealth != null)
        {
            targetHealth = nearestBuildingHealth;
            MoveTowardTarget();
            return;
        }

        // Priority 3: enemy castle (always target when no other enemies exist)
        Health castleHealth = FindEnemyCastle(enemyTeam);
        if (castleHealth != null && !castleHealth.IsDead)
        {
            targetHealth = castleHealth;
            float castleDist = DistanceToTarget(castleHealth);
            float attackRange = unit.Data != null ? unit.Data.attackRange : 2f;
            if (castleDist > attackRange)
                MoveTowardTarget();
            return;
        }

        targetHealth = null;
        if (movement != null && !movement.HasPath && !movement.IsMoving)
            movement.SetDestinationToEnemyCastle();
    }

    private float DistanceToTarget(Health target)
    {
        Vector3 closest = ClosestPointOnTarget(target);
        return Vector3.Distance(transform.position, closest);
    }

    private Vector3 ClosestPointOnTarget(Health target)
    {
        var renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds combined = default;
            bool first = true;
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                if (first) { combined = r.bounds; first = false; }
                else combined.Encapsulate(r.bounds);
            }
            if (!first)
                return combined.ClosestPoint(transform.position);
        }

        var col = target.GetComponent<Collider>();
        if (col != null)
            return col.ClosestPoint(transform.position);

        return target.transform.position;
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
        float nearestDist = maxRange;

        foreach (var b in buildings)
        {
            if (b == null) continue;
            var h = b.GetComponent<Health>();
            if (h == null || h.IsDead) continue;

            float dist = DistanceToTarget(h);
            if (dist < nearestDist)
            {
                nearestDist = dist;
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
        Vector3 closest = ClosestPointOnTarget(targetHealth);
        closest.y = transform.position.y;
        movement.SetDestinationWorld(closest);
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

        Vector3 lookTarget = ClosestPointOnTarget(targetHealth);
        Vector3 lookDir = (lookTarget - transform.position);
        lookDir.y = 0;
        if (lookDir.sqrMagnitude > 0.001f)
            transform.forward = lookDir.normalized;

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
