using UnityEngine;
using Mirror;

/// <summary>
/// Server-side combat component. Delegates target selection to TargetingState
/// and TargetingService. Handles chase, attack, and target lifecycle.
///
/// Attack range is grid-based: the unit's footprint is expanded by attackRangeCells
/// to form an attack rectangle. A target is in range when its footprint cells
/// intersect this rectangle.
/// </summary>
public class UnitCombat : NetworkBehaviour
{
    private Unit unit;
    private UnitMovement movement;
    private UnitStateMachine stateMachine;

    private readonly TargetingState targeting = new();
    private float attackCooldown;
    private float scanTimer;
    private const float ScanInterval = 0.25f;
    private const float LeashMultiplier = 1.5f;

    // Cached attack position — recomputed when target changes cell
    private Vector3? attackPosition;
    private Vector2Int lastTargetCell;

    /// <summary>
    /// True when engaged with a hard-locked target (unit or building).
    /// Soft-locked castle does not count — state machine shows Moving while marching.
    /// </summary>
    public bool HasTarget => targeting.HasTarget && targeting.Lock == TargetLock.Hard;

    public IAttackable CurrentTarget => targeting.Current;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        movement = GetComponent<UnitMovement>();
        stateMachine = GetComponent<UnitStateMachine>();
    }

    private void Update()
    {
        if (!isServer || unit == null || unit.IsDead || unit.Data == null) return;

        attackCooldown -= Time.deltaTime;

        // Validate current target
        if (targeting.HasTarget)
        {
            float leashRange = unit.Data.aggroRadius * LeashMultiplier;
            if (!targeting.Validate(transform.position, leashRange))
            {
                attackPosition = null;
                targeting.Clear();
            }
        }

        // Periodic scan
        scanTimer -= Time.deltaTime;
        if (scanTimer <= 0f)
        {
            scanTimer = ScanInterval;
            if (targeting.ShouldScan)
                Scan();
        }

        if (!targeting.HasTarget) return;

        var target = targeting.Current;

        // Still walking — check if target moved to a new cell, recompute if so
        if (movement.IsMoving || movement.HasPath)
        {
            Vector2Int targetCell = target.CurrentCell;
            if (!attackPosition.HasValue)
            {
                MoveToAttackPosition(target);
            }
            else if (targetCell != lastTargetCell)
            {
                attackPosition = null;
                MoveToAttackPosition(target);
            }
            return;
        }

        // Arrived — check if target footprint intersects our attack rectangle
        var grid = GridSystem.Instance;
        if (grid == null) return;

        bool inRange = AttackRangeHelper.IsTargetInRange(
            grid, transform.position, unit.FootprintSize,
            unit.Data.attackRangeCells, target.gameObject);

        if (inRange)
        {
            FaceTarget(target.gameObject.transform.position);

            if (stateMachine.CurrentState != UnitState.Fighting)
                stateMachine.SetState(UnitState.Fighting);

            if (attackCooldown <= 0f)
            {
                Attack(target);
                attackCooldown = 1f / unit.Data.attackSpeed;
            }
        }
        else
        {
            // Arrived but not in range — recompute
            if (stateMachine.CurrentState == UnitState.Fighting)
                stateMachine.SetState(UnitState.Moving);

            attackPosition = null;
            MoveToAttackPosition(target);
        }
    }

    // ================================================================
    //  SCAN
    // ================================================================

    private void Scan()
    {
        var found = TargetingService.FindTarget(
            transform.position, unit.TeamId,
            unit.Data.aggroRadius, unit.Data.attackRangeCells, unit.FootprintSize
        );

        if (found == null) return;

        // Don't re-acquire the same target
        if (targeting.HasTarget && found.gameObject == targeting.Current.gameObject) return;

        bool accepted = targeting.TrySetTarget(found);
        if (accepted)
        {
            attackPosition = null;
            lastTargetCell = found.CurrentCell;

            if (GameDebug.Combat)
                Debug.Log($"[Combat] {gameObject.name} aggro -> {found.gameObject.name} " +
                    $"priority={found.Priority} lock={targeting.Lock}");
        }
    }

    // ================================================================
    //  ATTACK POSITION
    // ================================================================

    private void MoveToAttackPosition(IAttackable target)
    {
        var grid = GridSystem.Instance;
        if (grid == null)
        {
            movement.SetDestinationWorld(target.gameObject.transform.position);
            return;
        }

        Vector2Int targetCell = target.CurrentCell;
        if (!attackPosition.HasValue || targetCell != lastTargetCell)
        {
            lastTargetCell = targetCell;

            var cell = AttackRangeHelper.FindAttackCell(
                grid, transform.position, unit.FootprintSize,
                unit.Data.attackRangeCells, target.gameObject);

            attackPosition = cell.HasValue ? grid.CellToWorld(cell.Value) : target.gameObject.transform.position;
        }

        if (attackPosition.HasValue)
            movement.SetDestinationWorld(attackPosition.Value);
    }

    // ================================================================
    //  ATTACK
    // ================================================================

    private void Attack(IAttackable target)
    {
        if (target.Health == null || target.Health.IsDead) return;

        float damage = DamageSystem.CalculateDamage(
            unit.Data.attackDamage,
            unit.Data.attackType,
            target.ArmorType
        );

        target.Health.TakeDamage(damage, gameObject);
        stateMachine.TriggerAttackAnimation(1f / unit.Data.attackSpeed);

        if (GameDebug.Combat)
            Debug.Log($"[Combat] {gameObject.name} hit {target.gameObject.name} for {damage:F1} dmg");
    }

    private void FaceTarget(Vector3 targetPos)
    {
        Vector3 dir = targetPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 10f * Time.deltaTime);
    }

}
