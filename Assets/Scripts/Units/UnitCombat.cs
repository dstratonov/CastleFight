using UnityEngine;
using Mirror;

/// <summary>
/// Server-side combat component. Delegates target selection to TargetingState
/// and TargetingService. Handles chase, attack, and target lifecycle.
///
/// Units always have a target. TargetingService scans in priority order
/// and falls back to the enemy castle as default. The unit marches toward
/// whatever target it has and attacks when in range.
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

    // Cached attack position
    private Vector3? attackPosition;
    private Vector3 lastTargetPos;
    private const float TargetMoveThreshold = 1.5f;

    // Unit obstacle: cells blocked while fighting in place
    private System.Collections.Generic.List<Vector2Int> blockedCells;
    private bool isBlocking;

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
                UnmarkAsObstacle();
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

        // Still walking — don't check range, wait for arrival
        if (movement.IsMoving || movement.HasPath)
        {
            if (!attackPosition.HasValue)
                MoveToAttackPosition(target);
            return;
        }

        // Arrived at destination — validate attack range
        Vector3 closestPoint = BoundsHelper.ClosestPoint(target.gameObject, transform.position);
        float dist = Vector3.Distance(transform.position, closestPoint);
        float atkRange = unit.Data.attackRange + unit.EffectiveRadius;

        if (dist <= atkRange)
        {
            // In range — fight
            if (!isBlocking)
                MarkAsObstacle();

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
            // Arrived but out of range — recompute attack position
            UnmarkAsObstacle();
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
            unit.Data.aggroRadius, unit.Data.attackRange, unit.EffectiveRadius
        );

        if (found == null) return;

        // Don't re-acquire the same target
        if (targeting.HasTarget && found.gameObject == targeting.Current.gameObject) return;

        bool accepted = targeting.TrySetTarget(found);
        if (accepted)
        {
            UnmarkAsObstacle();
            attackPosition = null;
            lastTargetPos = found.gameObject.transform.position;

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
        Vector3 targetPos = target.gameObject.transform.position;

        if (grid == null)
        {
            movement.SetDestinationWorld(targetPos);
            return;
        }

        float targetMoved = Vector3.Distance(targetPos, lastTargetPos);
        if (!attackPosition.HasValue || targetMoved > TargetMoveThreshold)
        {
            lastTargetPos = targetPos;
            attackPosition = FindAttackCell(grid, target);
        }

        if (attackPosition.HasValue)
            movement.SetDestinationWorld(attackPosition.Value);
    }

    private Vector3? FindAttackCell(GridSystem grid, IAttackable target)
    {
        float atkRange = unit.Data.attackRange + unit.EffectiveRadius;
        GameObject targetObj = target.gameObject;
        Vector3 targetPos = targetObj.transform.position;
        Vector2Int targetCell = grid.WorldToCell(targetPos);

        int footprint = unit.FootprintSize;
        int halfLow = (footprint - 1) / 2;
        int halfHigh = footprint / 2;

        int searchRadius = Mathf.CeilToInt((atkRange + target.TargetRadius) / grid.CellSize) + 2;

        float bestDistSq = float.MaxValue;
        Vector2Int bestCell = targetCell;
        bool found = false;

        for (int r = 0; r <= searchRadius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;

                    Vector2Int cell = new Vector2Int(targetCell.x + dx, targetCell.y + dy);
                    if (!GridAStar.IsFootprintWalkable(grid, cell, halfLow, halfHigh)) continue;

                    // Distance from this cell to the closest edge of the target
                    Vector3 cellWorld = grid.CellToWorld(cell);
                    Vector3 closest = BoundsHelper.ClosestPoint(targetObj, cellWorld);
                    float distToEdgeSq = (cellWorld - closest).sqrMagnitude;
                    if (distToEdgeSq > atkRange * atkRange) continue;

                    float distToUnitSq = (cellWorld - transform.position).sqrMagnitude;
                    if (distToUnitSq < bestDistSq)
                    {
                        bestDistSq = distToUnitSq;
                        bestCell = cell;
                        found = true;
                    }
                }
            }

            if (found) break;
        }

        if (found)
            return grid.CellToWorld(bestCell);

        return targetPos;
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

    // ================================================================
    //  UNIT OBSTACLE
    // ================================================================

    private void MarkAsObstacle()
    {
        var grid = GridSystem.Instance;
        if (grid == null || isBlocking) return;

        var presence = UnitGridPresence.Instance;
        if (presence == null) return;

        var cells = presence.GetUnitCells(unit.GetInstanceID());
        if (cells == null || cells.Count == 0) return;

        blockedCells = new System.Collections.Generic.List<Vector2Int>(cells);
        grid.MarkUnitObstacle(blockedCells);
        isBlocking = true;

        // Invalidate paths passing through blocked cells
        InvalidateNearbyPaths();

        if (GameDebug.Combat)
            Debug.Log($"[Combat] {gameObject.name} blocked {blockedCells.Count} cells");
    }

    private void UnmarkAsObstacle()
    {
        if (!isBlocking) return;

        var grid = GridSystem.Instance;
        if (grid != null && blockedCells != null)
            grid.UnmarkUnitObstacle(blockedCells);

        // Invalidate so nearby units replan through now-open cells
        InvalidateNearbyPaths();

        blockedCells = null;
        isBlocking = false;
    }

    private void InvalidateNearbyPaths()
    {
        var pfm = PathfindingManager.Instance;
        if (pfm == null || !pfm.IsInitialized) return;

        Bounds bounds = BoundsHelper.GetPhysicalBounds(gameObject);
        pfm.InvalidatePathsInRegion(bounds);
    }

    private void OnDestroy()
    {
        UnmarkAsObstacle();
    }
}
