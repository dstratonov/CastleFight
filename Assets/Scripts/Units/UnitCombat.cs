using UnityEngine;
using Mirror;

/// <summary>
/// Server-side combat component. Delegates target selection to TargetingState
/// and TargetingService. Handles chase, attack, and target lifecycle.
///
/// Combat positioning model — fully delegated to A* Pro's built-in
/// <c>RVODestinationCrowdedBehavior</c>:
///
///   When this unit aggros a target it sets its destination directly to
///   the target's world position. A* Pro's <c>rvoDensityBehavior</c>
///   (enabled by default on every AIBase) detects when the area around
///   that destination is more than ~60% packed with other agents and
///   automatically halts this agent at whatever position it reached.
///   Stopped agents have their RVO priority reduced to 0.1 and
///   flowFollowingStrength raised to 1.0, so newcomers approaching the
///   same target can push past them naturally. This is the same crowd
///   model used by StarCraft 2 and is the recommended A* Pro pattern
///   for this scenario.
///
///   There are no slots, no claims, no patience timer, no blacklist, no
///   manual stop commands during combat. UnitCombat's only job is:
///     1. Scan for targets.
///     2. When a target exists, point movement at target.Position.
///     3. When in attack range, play the attack animation and apply damage.
///   Crowd packing emerges entirely from the RVO simulation.
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
    // Kept purely for debug visualisation — DebugOverlay reads this to
    // show a "aim point" marker. In the density-based model there are
    // no real slot claims.
    private Vector3? attackPosition;

    // Tracks whether this unit reached attack range of its current target
    // at least once. Kept as a debug/introspection flag — the capacity
    // check in Scan is what actually prevents over-commitment, so we no
    // longer need this to gate overflow. Reset on target change.
    private bool reachedAttackRangeThisTarget;

    // Assigned attack angle around the current hard-lock target. Computed
    // in Scan() at commit time based on how much of the target's kissing
    // ring is already occupied — each committer is placed at the next
    // unoccupied arc slice, so locked attackers are guaranteed distinct
    // angles and their bodies cannot overlap. Measured in radians;
    // 0 = +X axis, π/2 = +Z axis. Only meaningful when
    // <see cref="hasAssignedAngle"/> is true (never true for soft-lock
    // castle targets — those use perimeter-closest-point instead).
    private float assignedAttackAngle;
    private bool hasAssignedAngle;

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
                attackPosition = null;
                reachedAttackRangeThisTarget = false;
                hasAssignedAngle = false;
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
            IsAttacking = false;
            SetAttackLock(false);
            return;
        }

        var target = targeting.Current;
        float myRadius = unit.EffectiveRadius;
        float attackRange = unit.Data.attackRange;

        // In-range check — attack when ALL THREE are true:
        //   1) the attacker's body can reach the target's body
        //      (distance(attackerPos, targetBody) <= attackerRadius + attackRange)
        //   2) we have arrived at our assigned ring slot (if any)
        //      so hard-locked attackers settle at distinct angles and
        //      don't pile up at whatever point first tripped (1).
        //   3) for soft-lock targets (no assigned angle) we skip (2).
        //
        // Gate (2) is essential: without it, attackers approaching a
        // unit target from the west would all reach "in range" while
        // still on the west side of the target and hard-lock there in
        // a clump, even though each one has a distinct angle pre-
        // computed in Scan(). With the gate they keep walking along
        // their curved path until they're at the assigned spot, and
        // only then lock in.
        bool inRange = AttackRangeHelper.IsTargetInRange(
            transform.position, myRadius, attackRange, target);

        bool atAssignedSlot = true;
        if (hasAssignedAngle && attackPosition.HasValue)
        {
            float slotDistSq = (transform.position - attackPosition.Value).sqrMagnitude;
            // 30% of body radius — tight enough that locked attackers are
            // at their assigned angle (so the arc-padding margin isn't
            // eaten by lock-position drift), but loose enough that RVO
            // convergence always reaches the gate before the unit would
            // otherwise orbit forever.
            float slotTolerance = myRadius * 0.3f;
            atAssignedSlot = slotDistSq <= slotTolerance * slotTolerance;
        }

        if (inRange && atAssignedSlot)
        {
            bool justArrived = !IsAttacking;
            IsAttacking = true;
            reachedAttackRangeThisTarget = true;
            // Lock the RVO agent in place so the incoming crowd can't push
            // us out of attack range mid-swing. Once locked, we're a static
            // obstacle to other agents — they accumulate arc behind us and
            // either fit into a remaining slot or are rejected by the
            // capacity check in Scan() and walk to the castle instead.
            SetAttackLock(true);

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

        // Walk toward the target. Call SetDestinationWorld every frame if:
        //   * attackPosition is stale (null, target moved, or movement was
        //     hard-stopped by ArriveAtDestination and needs wake-up).
        //
        // A hard-stop (ArriveAtDestination clears WorldTarget and sets
        // isStopped=true) on a stationary target would never satisfy the
        // "moved > threshold" condition by itself, so the IsHardStopped
        // clause is required to wake the unit back up for continued chase.
        // SetDestinationWorld has an internal dedup that makes same-dest
        // calls cheap for the normal case.
        Vector3 targetPos = target.Position;
        bool needsRefresh =
            !attackPosition.HasValue
            || Vector3.Distance(targetPos, lastKnownTargetPos) > TargetMoveThreshold
            || movement.IsHardStopped;

        if (needsRefresh)
        {
            lastKnownTargetPos = targetPos;
            Vector3 dest;
            if (hasAssignedAngle)
            {
                // Hard-lock target: walk to the pre-assigned arc slot on
                // the kissing ring so we settle at a distinct angle.
                dest = AttackRangeHelper.GetRingPosition(target, myRadius, assignedAttackAngle);
            }
            else
            {
                // Soft-lock (castle) or no angle assigned: fall back to
                // the perimeter-closest-point logic for extended targets
                // or the center for point-like targets.
                dest = AttackRangeHelper.FindAttackPosition(
                    transform.position, myRadius, attackRange, target, unit.GetInstanceID(), unit);
            }
            attackPosition = dest;
            movement.SetDestinationWorld(dest);
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

        // CAPACITY CHECK — each hard-lock target's kissing ring has exactly
        // 2π radians of usable arc. Each committed attacker occupies an
        // arc slice proportional to its own body radius (big troll = big
        // slice, small footman = small slice). If adding this unit would
        // push the total past 2π, the ring is full and we don't commit.
        //
        // Soft-lock targets (Default priority = castle) have no cap —
        // the castle IS the strategic destination and we always want
        // multiple attackers there simultaneously.
        float existingArc = 0f;
        if (found.Priority != TargetPriority.Default)
        {
            existingArc = AttackRangeHelper.GetArcOccupiedOn(found, unit.TeamId);
            float required = AttackRangeHelper.GetArcRequiredFor(found, unit.EffectiveRadius);
            const float FullArc = 2f * Mathf.PI;
            if (existingArc + required > FullArc + 0.001f)
            {
                if (GameDebug.Combat)
                    Debug.Log($"[Combat] {gameObject.name} skip {found.gameObject.name} " +
                        $"(arc {existingArc:F2}+{required:F2} > {FullArc:F2})");
                return;
            }
        }

        bool accepted = targeting.TrySetTarget(found);
        if (accepted)
        {
            attackPosition = null;
            lastKnownTargetPos = found.Position;
            reachedAttackRangeThisTarget = false;
            SetAttackLock(false);

            // Pre-assign an attack angle based on the current occupied arc.
            // This gives each committer a distinct spot on the target's
            // kissing ring — the first committer gets angle 0, the second
            // gets <first's arc>, the third gets <first + second>, and so
            // on. Locked attackers never occupy the same angle and can't
            // physically overlap. If the target is soft-lock (castle),
            // skip angle assignment and let combat use FindAttackPosition's
            // perimeter-closest-point path.
            if (found.Priority != TargetPriority.Default)
            {
                assignedAttackAngle = existingArc + AttackRangeHelper.GetArcRequiredFor(found, unit.EffectiveRadius) * 0.5f;
                hasAssignedAngle = true;
            }
            else
            {
                hasAssignedAngle = false;
            }

            if (GameDebug.Combat)
                Debug.Log($"[Combat] {gameObject.name} aggro -> {found.gameObject.name} " +
                    $"priority={found.Priority} angle={(hasAssignedAngle ? assignedAttackAngle.ToString("F2") : "n/a")}");
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
