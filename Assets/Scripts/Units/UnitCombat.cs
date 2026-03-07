using UnityEngine;
using Mirror;

public class UnitCombat : NetworkBehaviour
{
    [SerializeField] private float scanInterval = 0.25f;

    private Unit unit;
    private GridMovement movement;
    private UnitStateMachine stateMachine;
    private Health targetHealth;
    private float scanTimer;
    private float attackTimer;
    private bool isTargetUnreachable;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        movement = GetComponent<GridMovement>();
        stateMachine = GetComponent<UnitStateMachine>();
    }

    private void OnEnable()
    {
        if (movement != null)
            movement.OnPathBlocked += HandlePathBlocked;
    }

    private void OnDisable()
    {
        if (movement != null)
            movement.OnPathBlocked -= HandlePathBlocked;
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
            else if (!movement.IsMoving)
            {
                MoveTowardTarget();
            }
        }
    }

    [Server]
    private void ScanForTarget()
    {
        if (targetHealth != null && !targetHealth.IsDead && !isTargetUnreachable)
        {
            float dist = Vector3.Distance(transform.position, targetHealth.transform.position);
            float scanRange = unit.Data != null ? unit.Data.attackRange * 2f : 10f;
            if (dist <= scanRange) return;
        }

        float range = unit.Data != null ? unit.Data.attackRange * 2f : 10f;

        var reachableEnemy = FindNearestReachableEnemy(range);
        if (reachableEnemy != null)
        {
            targetHealth = reachableEnemy.GetComponent<Health>();
            isTargetUnreachable = false;
            MoveTowardTarget();
            return;
        }

        var nearestEnemy = UnitManager.Instance?.FindNearestEnemy(transform.position, unit.TeamId, range);
        if (nearestEnemy != null)
        {
            targetHealth = nearestEnemy.GetComponent<Health>();
            isTargetUnreachable = true;
            MovePartialPathToward(nearestEnemy.transform.position);
            return;
        }

        targetHealth = null;
        isTargetUnreachable = false;
        if (movement != null && !movement.HasPath && !movement.IsMoving)
            movement.SetDestinationToEnemyCastle();
    }

    [Server]
    private void MoveTowardTarget()
    {
        if (targetHealth == null || movement == null || GridSystem.Instance == null) return;

        Vector2Int targetCell = GridSystem.Instance.WorldToCell(targetHealth.transform.position);
        movement.SetDestination(targetCell);
    }

    [Server]
    private void MovePartialPathToward(Vector3 targetWorldPos)
    {
        if (movement == null || GridSystem.Instance == null) return;

        Vector2Int start = movement.CurrentCell;
        Vector2Int goal = GridSystem.Instance.WorldToCell(targetWorldPos);

        var result = GridPathfinding.FindPath(start, goal, GridSystem.Instance, gameObject);
        if (result.HasPath)
            movement.SetPathDirect(result.Path);
    }

    private void HandlePathBlocked()
    {
        isTargetUnreachable = true;
        scanTimer = 0f;
    }

    private Unit FindNearestReachableEnemy(float maxRange)
    {
        if (UnitManager.Instance == null || GridSystem.Instance == null) return null;

        int enemyTeam = TeamManager.Instance != null
            ? TeamManager.Instance.GetEnemyTeamId(unit.TeamId)
            : (unit.TeamId == 0 ? 1 : 0);

        var enemies = UnitManager.Instance.GetTeamUnits(enemyTeam);
        Unit nearest = null;
        float nearestDist = maxRange * maxRange;

        foreach (var enemy in enemies)
        {
            if (enemy == null || enemy.IsDead) continue;

            float distSq = (enemy.transform.position - transform.position).sqrMagnitude;
            if (distSq >= nearestDist) continue;

            Vector2Int enemyCell = GridSystem.Instance.WorldToCell(enemy.transform.position);
            var adjacentCells = GridSystem.Instance.GetAdjacentCells(enemyCell);
            bool hasReachableNeighbor = false;

            foreach (var adj in adjacentCells)
            {
                if (GridSystem.Instance.IsWalkable(adj) || GridSystem.Instance.IsWalkableOrOccupiedBy(adj, gameObject))
                {
                    hasReachableNeighbor = true;
                    break;
                }
            }

            if (!hasReachableNeighbor) continue;

            nearestDist = distSq;
            nearest = enemy;
        }

        return nearest;
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
            isTargetUnreachable = false;
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
