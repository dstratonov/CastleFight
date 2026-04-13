using UnityEngine;
using Mirror;

/// <summary>
/// Server-side combat component. Delegates target selection to TargetingState
/// and TargetingService. Handles chase, attack, and target lifecycle.
///
/// Combat uses geometry-aware engagement slots instead of "everyone rushes
/// the same point". Attackers are distributed around unit rings and
/// structure perimeters based on body radius and attack range. Melee
/// attackers hard-lock once they reach a slot; ranged attackers keep a
/// softer hold so outer firing bands stay fluid.
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
    // Brief pause between arriving at attack range and the first swing so
    // RichAI fully stops and SnapFaceTarget's rotation is visible before
    // the attack animation plays.
    private const float FirstAttackWindup = 0.12f;

    // Re-issue destination when the target moves more than this many
    // world units from where it was when we last set it.
    private const float TargetMoveThreshold = 1.0f;
    private Vector3 lastKnownTargetPos;
    private const float AttackPositionRefreshInterval = 0.25f;
    private float attackPositionRefreshTimer;
    private float slotProgressTimer;
    private float bestAttackSlotDistance = float.PositiveInfinity;
    private const float SlotProgressGrace = 0.6f;
    private const float SlotProgressDriftThreshold = 0.35f;
    private int slotSearchDirection;

    // Stuck detection: when velocity ≈ 0 while walking (not attacking),
    // the unit is blocked by a large locked agent that RVO can't
    // navigate around. After StuckDuration seconds, offset the
    // destination perpendicular to the approach direction — the
    // Warcraft II approach: "if a unit cannot slide past, repath an
    // alternate route."
    private float stuckTimer;
    private bool isDetouring;
    private const float StuckDuration = 0.4f;
    private const float DetourDistance = 4f;
    private const float MaxDetourDuration = 1.0f;
    private const float DetourArriveDistance = 0.75f;
    private float detourTimer;
    private Vector3 detourDestination;
    // Kept for debug visualisation and slot-arrival gating.
    private Vector3? attackPosition;

    // Tracks whether we've called movement.LockForAttack for the current
    // attack swing. Single source of truth so we lock-on-enter and
    // unlock-on-exit exactly once per transition, never doubling up and
    // never forgetting to release the RVO lock.
    private bool attackLockActive;

    private void SetAttackLock(bool locked)
    {
        if (locked == attackLockActive) return;
        attackLockActive = locked;
        if (movement == null) return;
        if (locked) movement.LockForAttack();
        else movement.UnlockAfterAttack();
    }

    private void ResetDetourState(bool clearAttackPosition = false)
    {
        stuckTimer = 0f;
        detourTimer = 0f;
        isDetouring = false;
        detourDestination = Vector3.zero;
        attackPositionRefreshTimer = 0f;
        slotProgressTimer = 0f;
        bestAttackSlotDistance = float.PositiveInfinity;

        if (clearAttackPosition)
            attackPosition = null;
    }

    private int EnsureSlotSearchDirection(IAttackable target)
    {
        if (slotSearchDirection != 0)
            return slotSearchDirection;

        int targetId = target != null && target.gameObject != null ? target.gameObject.GetInstanceID() : 0;
        slotSearchDirection = ((unit.GetInstanceID() ^ targetId) & 1) == 0 ? 1 : -1;
        return slotSearchDirection;
    }

    private void FlipSlotSearchDirection(IAttackable target)
    {
        int current = EnsureSlotSearchDirection(target);
        slotSearchDirection = -current;
    }

    private void BeginDetour(Vector3 detour)
    {
        if (movement == null)
            return;

        detourDestination = detour;
        detourTimer = 0f;
        stuckTimer = 0f;
        isDetouring = true;
        attackPosition = null;
        movement.ForceSetDestinationWorld(detour);
    }

    /// <summary>
    /// True when engaged with a hard-locked target (unit or building).
    /// Soft-locked castle does not count — used by scan logic, not state transitions.
    /// For "is the unit currently fighting" use <see cref="IsAttacking"/>.
    /// </summary>
    public bool HasTarget => targeting.HasTarget && targeting.Lock == TargetLock.Hard;

    /// <summary>
    /// True when the unit is actively within attack range of its current target
    /// (hard or soft). Drives the Fighting state. Set per-frame by Update.
    /// </summary>
    public bool IsAttacking { get; private set; }

    public IAttackable CurrentTarget => targeting.Current;

    /// <summary>
    /// Debug-only: the raw destination the unit is walking toward while
    /// engaging its current target (always target.Position in the
    /// density-driven model). Used by DebugOverlay for visualisation.
    /// </summary>
    public Vector3? AttackPosition => attackPosition;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        movement = GetComponent<UnitMovement>();
        stateMachine = GetComponent<UnitStateMachine>();
    }

    private void OnDisable()
    {
        // Release any attack lock so the unit doesn't sit frozen as an
        // RVO obstacle after combat is disabled (pooling, teardown,
        // scenario cleanup). Safe to call even if not locked.
        ResetDetourState(clearAttackPosition: true);
        if (attackLockActive)
            SetAttackLock(false);
    }

    private void Update()
    {
        if (!isServer || unit == null || unit.IsDead || unit.Data == null) return;

        // Freeze combat after the match ends so the winning side's units
        // don't finish off the other castle in the 1-2s between GameOver
        // and play-mode cleanup.
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver)
        {
            ResetDetourState(clearAttackPosition: true);
            slotSearchDirection = 0;
            IsAttacking = false;
            SetAttackLock(false);
            return;
        }

        attackCooldown -= Time.deltaTime;

        // Validate current target
        if (targeting.HasTarget)
        {
            float leashRange = unit.Data.aggroRadius * LeashMultiplier;
            if (!targeting.Validate(transform.position, leashRange))
            {
                ResetDetourState(clearAttackPosition: true);
                slotSearchDirection = 0;
                SetAttackLock(false);
                targeting.Clear();
            }
        }

        // Periodic scan. The capacity check inside Scan() handles
        // "target full" — we never commit to an over-capacity target,
        // so no scan suppression / anchor / cooldown is needed.
        scanTimer -= Time.deltaTime;
        if (scanTimer <= 0f)
        {
            scanTimer = ScanInterval;
            if (targeting.ShouldScan)
                Scan();
        }

        if (!targeting.HasTarget)
        {
            ResetDetourState(clearAttackPosition: true);
            slotSearchDirection = 0;
            IsAttacking = false;
            SetAttackLock(false);
            return;
        }

        var target = targeting.Current;
        float myRadius = unit.EffectiveRadius;
        float attackRange = unit.Data.attackRange;
        attackPositionRefreshTimer -= Time.deltaTime;

        // In-range check. The assigned attack slot is still used to guide
        // movement, but it is no longer a hard veto once the unit is
        // already standing at a valid frontline spot. This avoids the
        // classic RTS failure mode where a unit is visibly in range yet
        // keeps orbiting because its mathematically assigned point is on
        // the far side of the target or temporarily blocked.
        bool inRange = AttackRangeHelper.IsTargetInRange(
            transform.position, myRadius, attackRange, target);

        bool atAssignedSlot = false;
        if (attackPosition.HasValue)
        {
            float slotDistSq = (transform.position - attackPosition.Value).sqrMagnitude;
            float slotTolerance = AttackRangeHelper.GetAttackSlotTolerance(unit);
            if (target.TargetRadius <= 0.01f)
            {
                float structureTolerance = Mathf.Clamp(unit.EffectiveRadius * 1.1f, 0.45f, 1.5f);
                slotTolerance = Mathf.Max(slotTolerance, structureTolerance);
            }
            atAssignedSlot = slotDistSq <= slotTolerance * slotTolerance;
        }

        if (!atAssignedSlot
            && inRange
            && AttackRangeHelper.TryGetCurrentFrontlinePosition(target, unit, out Vector3 frontlinePosition))
        {
            attackPosition = frontlinePosition;
            atAssignedSlot = true;
        }

        if (!atAssignedSlot
            && inRange
            && target.TargetRadius <= 0.01f
            && movement != null
            && movement.WorldTarget.HasValue)
        {
            float localStopDistance = Vector3.Distance(transform.position, movement.WorldTarget.Value);
            if (localStopDistance <= 0.4f)
                atAssignedSlot = true;
        }

        if (inRange && atAssignedSlot)
        {
            ResetDetourState();
            bool justArrived = !IsAttacking;
            IsAttacking = true;
            bool hardLock = AttackRangeHelper.ShouldHardLock(unit);
            // Lock the RVO agent in place so the incoming crowd can't push
            // us out of attack range mid-swing. Once locked, we're a static
            // obstacle to other agents — they accumulate arc behind us and
            // either fit into a remaining slot or are rejected by the
            // capacity check in Scan() and walk to the castle instead.
            SetAttackLock(hardLock);

            if (justArrived)
            {
                SnapFaceTarget(target.Position);
                attackCooldown = Mathf.Max(attackCooldown, FirstAttackWindup);
            }
            else
            {
                FaceTarget(target.Position);
            }

            if (attackCooldown <= 0f)
            {
                Attack(target);
                attackCooldown = 1f / unit.Data.attackSpeed;
            }
            return;
        }

        // Not in range — chasing the target. Release the attack lock so
        // the unit can move (no-op if it wasn't locked).
        IsAttacking = false;
        SetAttackLock(false);

        // Stuck detection: if velocity ≈ 0 while walking, the unit is
        // blocked by a large locked agent. Offset destination
        // perpendicular to approach direction to route around.
        var richAI = movement != null ? unit.GetComponent<Pathfinding.RichAI>() : null;
        float vel = richAI != null ? richAI.velocity.magnitude : 999f;

        if (attackPosition.HasValue && !isDetouring)
        {
            float slotDistance = Vector3.Distance(transform.position, attackPosition.Value);
            if (slotDistance + 0.1f < bestAttackSlotDistance)
            {
                bestAttackSlotDistance = slotDistance;
                slotProgressTimer = 0f;
            }
            else
            {
                bool driftingAway = slotDistance > bestAttackSlotDistance + SlotProgressDriftThreshold;
                bool stalledOnSlot = vel < 0.2f;
                if (driftingAway || stalledOnSlot)
                    slotProgressTimer += Time.deltaTime;
                else
                    slotProgressTimer = Mathf.Max(0f, slotProgressTimer - Time.deltaTime * 0.5f);
            }

            if (slotProgressTimer >= SlotProgressGrace)
            {
                slotSearchDirection = 0;
                attackPosition = null;
                attackPositionRefreshTimer = 0f;
                slotProgressTimer = 0f;
                bestAttackSlotDistance = float.PositiveInfinity;
            }
        }
        else
        {
            slotProgressTimer = 0f;
            bestAttackSlotDistance = float.PositiveInfinity;
        }

        if (isDetouring)
        {
            detourTimer += Time.deltaTime;

            bool reachedDetour = Vector3.Distance(transform.position, detourDestination) <= DetourArriveDistance;
            bool targetShifted = Vector3.Distance(target.Position, lastKnownTargetPos) > TargetMoveThreshold;
            bool detourExpired = detourTimer >= MaxDetourDuration;

            if (reachedDetour || targetShifted || detourExpired || movement.IsHardStopped)
            {
                ResetDetourState(clearAttackPosition: true);
            }
        }

        if (!isDetouring)
        {
            if (vel < 0.15f && !movement.IsHardStopped)
            {
                stuckTimer += Time.deltaTime;
                if (stuckTimer > StuckDuration)
                {
                    // Pick a perpendicular detour point
                    Vector3 toTarget = target.Position - transform.position;
                    toTarget.y = 0f;
                    if (toTarget.sqrMagnitude > 0.01f)
                    {
                        toTarget.Normalize();
                        Vector3 perp = new Vector3(-toTarget.z, 0f, toTarget.x);
                        float side = (unit.GetInstanceID() % 2 == 0) ? 1f : -1f;
                        Vector3 detour = transform.position + perp * side * DetourDistance
                                       + toTarget * 1f; // slight forward bias
                        BeginDetour(detour);
                    }

                    stuckTimer = 0f;
                }
            }
            else
            {
                stuckTimer = 0f;
            }
        }

        // Walk toward the target.
        Vector3 targetPos = target.Position;
        bool targetMoved = Vector3.Distance(targetPos, lastKnownTargetPos) > TargetMoveThreshold;
        bool needsRefresh =
            attackPositionRefreshTimer <= 0f
            || !movement.WorldTarget.HasValue
            || !attackPosition.HasValue
            || targetMoved
            || movement.IsHardStopped;

        if (needsRefresh && !isDetouring)
        {
            lastKnownTargetPos = targetPos;

            bool keepCurrentAttackPosition =
                attackPosition.HasValue
                && !targetMoved
                && !movement.IsHardStopped
                && AttackRangeHelper.ShouldKeepCurrentAttackPosition(target, unit, attackPosition.Value);

            if (keepCurrentAttackPosition)
            {
                attackPositionRefreshTimer = AttackPositionRefreshInterval;
                if (!movement.WorldTarget.HasValue
                    || Vector3.Distance(movement.WorldTarget.Value, attackPosition.Value) > 0.35f)
                {
                    movement.SetDestinationWorld(attackPosition.Value, treatAsCombatApproach: true);
                }
                return;
            }

            Vector3 dest = AttackRangeHelper.FindAttackPosition(
                transform.position,
                myRadius,
                attackRange,
                target,
                unit.GetInstanceID(),
                unit,
                allowQueueOutsideRange: target.Priority == TargetPriority.Default,
                searchDirection: EnsureSlotSearchDirection(target));

            bool destinationChanged = !attackPosition.HasValue
                || Vector3.Distance(dest, attackPosition.Value) > 0.35f
                || movement.IsHardStopped
                || !movement.WorldTarget.HasValue;

            attackPositionRefreshTimer = AttackPositionRefreshInterval;

            if (!attackPosition.HasValue || destinationChanged)
            {
                attackPosition = dest;
                bestAttackSlotDistance = Vector3.Distance(transform.position, dest);
                slotProgressTimer = 0f;
            }

            if (destinationChanged)
                movement.SetDestinationWorld(dest, treatAsCombatApproach: true);
        }
    }

    // ================================================================
    //  SCAN
    // ================================================================

    private void Scan()
    {
        var found = TargetingService.FindTarget(
            transform.position, unit.TeamId, unit.Data.aggroRadius
        );

        if (found == null) return;

        // Don't re-acquire the same target
        if (targeting.HasTarget && found.gameObject == targeting.Current.gameObject) return;

        // Hard targets only accept new commitments if we can assign a
        // valid engagement slot. Soft-lock defaults (castle) are allowed
        // to queue in outer bands outside attack range until space opens.
        if (found.Priority != TargetPriority.Default)
        {
            if (!AttackRangeHelper.CanCommitToTarget(found, unit))
            {
                if (GameDebug.Combat)
                    Debug.Log($"[Combat] {gameObject.name} skip {found.gameObject.name} (no engagement slot)");
                return;
            }
        }

        bool accepted = targeting.TrySetTarget(found);
        if (accepted)
        {
            ResetDetourState(clearAttackPosition: true);
            slotSearchDirection = 0;
            lastKnownTargetPos = found.Position;
            SetAttackLock(false);
            attackPositionRefreshTimer = 0f;

            if (GameDebug.Combat)
                Debug.Log($"[Combat] {gameObject.name} aggro -> {found.gameObject.name} " +
                    $"priority={found.Priority}");
        }
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

    // Slerp speed for combat facing (radians-ish per second) — high enough
    // that the rotation visually completes in ~150 ms.
    private const float FaceSlerpSpeed = 18f;

    private void FaceTarget(Vector3 targetPos)
    {
        Vector3 dir = targetPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                FaceSlerpSpeed * Time.deltaTime);
    }

    /// <summary>Hard-snap to face the target — no slerp. Used when first
    /// entering attack state so the attack animation doesn't play while the
    /// unit is still turning.</summary>
    private void SnapFaceTarget(Vector3 targetPos)
    {
        Vector3 dir = targetPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir);
    }
}
