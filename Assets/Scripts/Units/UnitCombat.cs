using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class UnitCombat : NetworkBehaviour
{
    [SerializeField] private float scanInterval = 0.25f;

    private static readonly Dictionary<Health, int> targetEngageCounts = new();
    private static readonly Dictionary<Health, Dictionary<Vector2Int, UnitCombat>> slotReservations = new();
    private static float engageCleanupTimer;
    private Vector2Int reservedSlotCell = new(-9999, -9999);
    private Health reservedSlotTarget;

    private Unit unit;
    private UnitMovement movement;
    private UnitStateMachine stateMachine;
    private Health targetHealth;
    private float scanTimer;
    private float attackTimer;
    private int debugLogCounter;
    private const int DebugLogInterval = 16;
    private bool wasInRange;
    private float approachStallTimer;
    private int stuckRetryCount;
    private float lastApproachDist = float.MaxValue;
    private float idleRecoveryTimer;

    private Health cachedApproachTarget;
    private Vector3 cachedApproachPos;
    private Vector3 cachedTargetPos; // target position when cache was created
    private bool hasApproachCache;
    private float approachRecalcCooldown;
    private const float TargetMovedThresholdSq = 2f * 2f; // invalidate cache when target moves >2 units

    private Health blacklistedTarget;
    private float blacklistExpiry;

    private static readonly Dictionary<int, Health> cachedCastles = new();

    private static readonly List<(Health health, float distSq)> scanCandidateBuffer = new(32);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        targetEngageCounts.Clear();
        slotReservations.Clear();
        cachedCastles.Clear();
        structureCache.Clear();
        engageCleanupTimer = 0f;
        scanCandidateBuffer.Clear();
    }

    public Transform AttackTarget => targetHealth != null && !targetHealth.IsDead ? targetHealth.transform : null;

    private bool ShouldLog() => debugLogCounter % DebugLogInterval == 0;
    private string UnitTag() => $"[Combat:{gameObject.name} t{unit?.TeamId}]";

    private float GetAttackRange()
    {
        Debug.Assert(unit != null, $"[UnitCombat] {gameObject.name} GetAttackRange: unit is null", this);
        if (unit.Data == null)
        {
            Debug.LogError($"[UnitCombat] {gameObject.name} GetAttackRange: unit.Data is null — unit not initialized", this);
            return 1f;
        }
        return CombatTargeting.GetAttackRange(unit.Data.attackRange, unit.Data.isRanged);
    }

    private void Awake()
    {
        unit = GetComponent<Unit>();
        movement = GetComponent<UnitMovement>();
        stateMachine = GetComponent<UnitStateMachine>();

        Debug.Assert(unit != null, $"[UnitCombat] {gameObject.name} missing Unit component", this);
        Debug.Assert(movement != null, $"[UnitCombat] {gameObject.name} missing UnitMovement component", this);
        Debug.Assert(stateMachine != null, $"[UnitCombat] {gameObject.name} missing UnitStateMachine component", this);
    }

    private void OnDestroy()
    {
        ReleaseSlotReservation();
        UnregisterFromTarget();
        if (unit != null)
            AttackPositionFinder.ReleaseAllSlots(unit.GetInstanceID());
    }

    private void RegisterToTarget(Health newTarget)
    {
        if (newTarget == targetHealth) return;
        if (GameDebug.Combat)
        {
            string oldName = targetHealth != null ? targetHealth.name : "none";
            string newName = newTarget != null ? newTarget.name : "none";
            Debug.Log($"{UnitTag()} TARGET CHANGE: {oldName} -> {newName}");
        }
        UnregisterFromTarget();
        targetHealth = newTarget;
        wasInRange = false;
        approachStallTimer = 0f;
        stuckRetryCount = 0;
        lastApproachDist = float.MaxValue;
        InvalidateApproachCache();
        if (targetHealth != null)
        {
            targetEngageCounts.TryGetValue(targetHealth, out int count);
            targetEngageCounts[targetHealth] = count + 1;
            if (GameDebug.Combat)
                Debug.Log($"{UnitTag()} engagers on {targetHealth.name}: {count + 1}");
        }
    }

    private void InvalidateApproachCache()
    {
        hasApproachCache = false;
        cachedApproachTarget = null;
        approachRecalcCooldown = 0f;
        ReleaseSlotReservation();
        if (unit != null)
            AttackPositionFinder.ReleaseAllSlots(unit.GetInstanceID());
    }

    private void ReleaseSlotReservation()
    {
        if (reservedSlotTarget != null && slotReservations.TryGetValue(reservedSlotTarget, out var slots))
        {
            if (slots.TryGetValue(reservedSlotCell, out var owner) && owner == this)
                slots.Remove(reservedSlotCell);
            if (slots.Count == 0)
                slotReservations.Remove(reservedSlotTarget);
        }
        reservedSlotTarget = null;
        reservedSlotCell = new(-9999, -9999);
    }

    private void ReserveSlot(Health target, Vector2Int cell)
    {
        ReleaseSlotReservation();
        if (!slotReservations.ContainsKey(target))
            slotReservations[target] = new Dictionary<Vector2Int, UnitCombat>();
        slotReservations[target][cell] = this;
        reservedSlotTarget = target;
        reservedSlotCell = cell;
    }

    private static bool IsSlotTaken(Health target, Vector2Int cell)
    {
        if (!slotReservations.TryGetValue(target, out var slots)) return false;
        if (!slots.TryGetValue(cell, out var owner)) return false;
        return owner != null;
    }

    private void UnregisterFromTarget()
    {
        if (targetHealth != null && targetEngageCounts.ContainsKey(targetHealth))
        {
            targetEngageCounts[targetHealth]--;
            if (targetEngageCounts[targetHealth] <= 0)
                targetEngageCounts.Remove(targetHealth);
        }
    }

    public static int GetEngageCount(Health target)
    {
        if (target == null) return 0;
        targetEngageCounts.TryGetValue(target, out int count);
        return count;
    }

    private static int lastCleanupFrame = -1;

    private static void CleanupStaleEngageCounts()
    {
        if (Time.frameCount == lastCleanupFrame) return;
        lastCleanupFrame = Time.frameCount;

        engageCleanupTimer -= Time.deltaTime;
        if (engageCleanupTimer > 0f) return;
        engageCleanupTimer = 5f;

        var staleEngages = new List<Health>();
        foreach (var kvp in targetEngageCounts)
        {
            if (kvp.Key == null || kvp.Key.IsDead)
                staleEngages.Add(kvp.Key);
        }
        foreach (var key in staleEngages)
            targetEngageCounts.Remove(key);

        var staleSlots = new List<Health>();
        foreach (var kvp in slotReservations)
        {
            if (kvp.Key == null || kvp.Key.IsDead)
            {
                staleSlots.Add(kvp.Key);
                continue;
            }
            var deadOwners = new List<Vector2Int>();
            foreach (var slot in kvp.Value)
            {
                if (slot.Value == null)
                    deadOwners.Add(slot.Key);
            }
            foreach (var cell in deadOwners)
                kvp.Value.Remove(cell);
            if (kvp.Value.Count == 0)
                staleSlots.Add(kvp.Key);
        }
        foreach (var key in staleSlots)
            slotReservations.Remove(key);

        if (structureCache.Count > 100)
            structureCache.Clear();
    }

    private void Update()
    {
        if (!isServer || !NetworkServer.active || unit == null || unit.IsDead) return;
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver) return;

        CleanupStaleEngageCounts();
        approachRecalcCooldown -= Time.deltaTime;

        scanTimer -= Time.deltaTime;
        if (scanTimer <= 0f)
        {
            bool isIdle = stateMachine != null && stateMachine.CurrentState == UnitState.Idle;
            scanTimer = isIdle && targetHealth == null ? scanInterval * 0.5f : scanInterval;
            ScanForTarget();
        }

        if (targetHealth != null && !targetHealth.IsDead)
        {
            if (movement != null && movement.IsDestinationUnreachable)
            {
                stuckRetryCount++;

                InvalidateApproachCache();
                approachRecalcCooldown = 0f;
                MoveTowardTarget();
                return;
            }

            float dist = DistanceToTarget(targetHealth);
            float range = GetAttackRange();
            float myRadius = unit.EffectiveRadius;
            float effectiveRange = range + myRadius;

            bool targetIsStructure = IsStructure(targetHealth);

            bool inRange = CombatTargeting.IsInRange(dist, effectiveRange, wasInRange);

            bool pathDone = movement != null && !movement.IsMoving && !movement.HasPath;

            // Don't enter combat while actively walking to a designated attack position.
            // Without this, units stop mid-path wherever they first enter range, causing
            // them to clump instead of spreading around the target.
            bool isApproaching = !wasInRange
                && hasApproachCache && cachedApproachTarget == targetHealth
                && movement != null && (movement.IsMoving || movement.HasPath);
            if (inRange && isApproaching)
                inRange = false;

            // When the unit has finished moving (arrived as close as it can), grant a
            // generous tolerance so it doesn't endlessly re-approach. The tolerance
            // grows with each stuck retry to eventually always succeed.
            if (!inRange && pathDone)
            {
                if (CombatTargeting.IsInRangeWithTolerance(dist, effectiveRange, wasInRange, pathDone, myRadius, stuckRetryCount))
                {
                    inRange = true;
                    if (GameDebug.Combat && ShouldLog())
                        Debug.Log($"{UnitTag()} IN RANGE(tolerance) dist={dist:F2} effRange={effectiveRange:F2}");
                }
            }

            if (inRange)
            {
                if (!wasInRange && GameDebug.Combat)
                    Debug.Log($"{UnitTag()} IN RANGE of {targetHealth.name} dist={dist:F2} effRange={effectiveRange:F2}");
                wasInRange = true;
                stuckRetryCount = 0;
                approachStallTimer = 0f;
                movement?.Stop();
                stateMachine?.SetState(UnitState.Fighting);
                FaceTarget();
                TryAttack();
            }
            else
            {
                if (wasInRange)
                {
                    if (GameDebug.Combat)
                        Debug.Log($"{UnitTag()} LEFT RANGE of {targetHealth.name} dist={dist:F2} effRange={effectiveRange:F2}");

                    if (stateMachine != null && stateMachine.CurrentState == UnitState.Fighting)
                        stateMachine.SetState(UnitState.Idle);
                }
                wasInRange = false;

                bool progressing = CombatTargeting.IsApproachProgressing(dist, lastApproachDist, targetIsStructure);
                if (progressing)
                {
                    lastApproachDist = dist;
                    approachStallTimer = 0f;
                }
                else
                {
                    bool isPhysicallyMoving = movement != null && movement.IsMoving;
                    if (CombatTargeting.ShouldIncrementApproachStall(progressing, isPhysicallyMoving))
                        approachStallTimer += Time.deltaTime;
                }

                var stallAction = CombatTargeting.EvaluateApproachStall(approachStallTimer, stuckRetryCount, targetIsStructure);
                if (stallAction != ApproachAction.Continue)
                {
                    stuckRetryCount++;
                    if (stallAction == ApproachAction.BlacklistAndRetreat)
                    {
                        BlacklistTarget(targetHealth);
                        RegisterToTarget(null);
                        stateMachine?.SetState(UnitState.Idle);
                        movement?.SetDestinationToEnemyCastle();
                        return;
                    }

                    if (GameDebug.Combat)
                        Debug.Log($"{UnitTag()} APPROACH STALL retry={stuckRetryCount} t={approachStallTimer:F1}s dist={dist:F2}");

                    InvalidateApproachCache();
                    approachRecalcCooldown = 0f;
                    approachStallTimer = 0f;
                    lastApproachDist = float.MaxValue;
                    MoveTowardTarget();
                    return;
                }

                if (movement != null && !movement.IsMoving)
                    MoveTowardTarget();
            }
        }
        else
        {
            // Only run recovery when the target actually just died (not every frame).
            // After RegisterToTarget(null), targetHealth becomes null, so subsequent
            // frames skip this block entirely.
            bool targetJustDied = targetHealth != null;

            wasInRange = false;
            approachStallTimer = 0f;
            lastApproachDist = float.MaxValue;

            if (targetJustDied)
            {
                RegisterToTarget(null);
                if (stateMachine != null && stateMachine.CurrentState == UnitState.Fighting)
                    stateMachine.SetState(UnitState.Idle);

                if (GameDebug.Combat)
                    Debug.Log($"{UnitTag()} TARGET DEAD — resuming march" +
                        $" (hasStrategic={movement?.HasStrategicDestination ?? false})");

                movement?.Resume();

                if (movement != null && !movement.HasPath && !movement.IsMoving)
                    movement.SetDestinationToEnemyCastle();
                else if (movement != null && movement.HasPath)
                    EnsureMovingState();
            }
        }

        if (targetHealth == null && movement != null
            && !movement.IsMoving && !movement.HasPath
            && !movement.IsWaitingForPath
            && stateMachine != null && stateMachine.CurrentState == UnitState.Idle)
        {
            idleRecoveryTimer -= Time.deltaTime;
            if (idleRecoveryTimer <= 0f)
            {
                idleRecoveryTimer = 2f;
                if (GameDebug.Combat)
                    Debug.Log($"{UnitTag()} IDLE RECOVERY: no target, no path — resuming castle march" +
                        $" (hasStrategic={movement.HasStrategicDestination})");
                movement.Resume();
                if (!movement.HasPath && !movement.IsMoving)
                    movement.SetDestinationToEnemyCastle();
                else
                    EnsureMovingState();
            }
        }
        else
        {
            idleRecoveryTimer = 0f;
        }
    }

    private void ScanForTarget()
    {
        debugLogCounter++;
        bool log = ShouldLog() && GameDebug.Combat;

        float myRadius = unit.EffectiveRadius;
        float attackRangeVal = GetAttackRange();
        float aggroRange = CombatTargeting.GetAggroRange(attackRangeVal, myRadius);
        int enemyTeam = GetEnemyTeam();

        bool currentTargetIsStructure = targetHealth != null && !targetHealth.IsDead && IsStructure(targetHealth);

        if (currentTargetIsStructure)
        {
            Health nearbyUnit = FindBestEnemyUnit(aggroRange);
            if (nearbyUnit != null)
            {
                if (log)
                    Debug.Log($"{UnitTag()} PRIORITY SWITCH: dropping building {targetHealth.name} for unit {nearbyUnit.name}");
                RegisterToTarget(nearbyUnit);
                MoveTowardTarget();
                return;
            }

            float dist = DistanceToTarget(targetHealth);
            float scanRange = aggroRange;
            if (dist <= scanRange)
                return;
        }

        if (targetHealth != null && !targetHealth.IsDead && !currentTargetIsStructure)
        {
            float dist = DistanceToTarget(targetHealth);
            float scanRange = aggroRange;
            if (dist <= scanRange)
            {
                if (log)
                    Debug.Log($"{UnitTag()} SCAN: keeping target {targetHealth.name} dist={dist:F2} scanRange={scanRange:F2}");
                return;
            }
            if (log)
                Debug.Log($"{UnitTag()} SCAN: target {targetHealth.name} too far dist={dist:F2}, rescanning");
        }

        Health bestUnit = FindBestEnemyUnit(aggroRange);
        if (bestUnit != null)
        {
            if (log)
                Debug.Log($"{UnitTag()} SCAN: found enemy UNIT {bestUnit.name} dist={DistanceToTarget(bestUnit):F2}");
            RegisterToTarget(bestUnit);
            MoveTowardTarget();
            return;
        }

        Health nearestBuilding = FindNearestEnemyBuilding(enemyTeam, aggroRange);
        if (nearestBuilding != null)
        {
            if (log)
                Debug.Log($"{UnitTag()} SCAN: found enemy BUILDING {nearestBuilding.name} dist={DistanceToTarget(nearestBuilding):F2}");
            RegisterToTarget(nearestBuilding);
            MoveTowardTarget();
            return;
        }

        Health castleHealth = FindEnemyCastle(enemyTeam);
        if (castleHealth != null && !castleHealth.IsDead)
        {
            RegisterToTarget(castleHealth);
            float castleDist = DistanceToTarget(castleHealth);
            float attackRange = GetAttackRange();
            if (log)
                Debug.Log($"{UnitTag()} SCAN: targeting CASTLE {castleHealth.name} dist={castleDist:F2}");
            if (castleDist > attackRange)
                MoveTowardTarget();
            return;
        }

        RegisterToTarget(null);
        if (movement != null && (!movement.HasPath || !movement.IsMoving))
        {
            if (log)
                Debug.Log($"{UnitTag()} SCAN: NO TARGET found, starting castle march");
            movement.SetDestinationToEnemyCastle();
        }
    }

    private const int MaxEngagersPerUnit = 4;

    private Health FindBestEnemyUnit(float maxRange)
    {
        Debug.Assert(UnitManager.Instance != null, $"[UnitCombat] {gameObject.name} FindBestEnemyUnit: UnitManager.Instance is null", this);
        if (UnitManager.Instance == null) return null;

        int enemyTeam = GetEnemyTeam();
        var nearby = UnitManager.Instance.GetUnitsInRadius(transform.position, maxRange);

        scanCandidateBuffer.Clear();
        foreach (var enemy in nearby)
        {
            if (enemy == null || enemy.IsDead) continue;
            if (enemy.TeamId != enemyTeam) continue;
            float distSq = (enemy.transform.position - transform.position).sqrMagnitude;

            var h = enemy.GetComponent<Health>();
            if (h == null || h.IsDead) continue;
            if (IsBlacklisted(h)) continue;

            scanCandidateBuffer.Add((h, distSq));
        }

        scanCandidateBuffer.Sort((a, b) => a.distSq.CompareTo(b.distSq));

        foreach (var (health, _) in scanCandidateBuffer)
        {
            if (GetEngageCount(health) < MaxEngagersPerUnit)
                return health;
        }

        if (scanCandidateBuffer.Count > 0)
            return scanCandidateBuffer[0].health;

        return null;
    }

    private float DistanceToTarget(Health target)
    {
        Vector3 closest = ClosestPointOnTarget(target);
        return Vector3.Distance(transform.position, closest);
    }

    private Vector3 ClosestPointOnTarget(Health target)
    {
        return BoundsHelper.ClosestPoint(target.gameObject, transform.position);
    }

    private int GetEnemyTeam()
    {
        Debug.Assert(TeamManager.Instance != null, $"[UnitCombat] {gameObject.name} GetEnemyTeam: TeamManager.Instance is null", this);
        return TeamManager.Instance.GetEnemyTeamId(unit.TeamId);
    }

    private void BlacklistTarget(Health target)
    {
        blacklistedTarget = target;
        float dist = target != null ? DistanceToTarget(target) : float.MaxValue;
        float range = GetAttackRange();
        float myRadius = unit != null ? unit.EffectiveRadius : 0.5f;
        float effectiveRange = range + myRadius;
        float duration = CombatTargeting.GetBlacklistDuration(dist, effectiveRange);
        blacklistExpiry = Time.time + duration;
        if (GameDebug.Combat)
            Debug.Log($"{UnitTag()} BLACKLISTED {(target != null ? target.name : "null")} for {duration:F1}s dist={dist:F1} (stuckRetry={stuckRetryCount})");
    }

    private bool IsBlacklisted(Health target)
    {
        if (blacklistedTarget == null || Time.time > blacklistExpiry)
        {
            blacklistedTarget = null;
            return false;
        }
        return target == blacklistedTarget;
    }

    private static readonly Dictionary<int, bool> structureCache = new();

    private bool IsStructure(Health h)
    {
        if (h == null) return false;
        int id = h.GetInstanceID();
        if (structureCache.TryGetValue(id, out bool cached))
            return cached;
        bool result = h.GetComponent<Castle>() != null || h.GetComponent<Building>() != null;
        structureCache[id] = result;
        return result;
    }

    private Health FindNearestEnemyBuilding(int enemyTeam, float maxRange)
    {
        Debug.Assert(BuildingManager.Instance != null, $"[UnitCombat] {gameObject.name} FindNearestEnemyBuilding: BuildingManager.Instance is null", this);
        if (BuildingManager.Instance == null) return null;

        var buildings = BuildingManager.Instance.GetTeamBuildings(enemyTeam);
        Health nearest = null;
        float nearestDist = maxRange;

        foreach (var b in buildings)
        {
            if (b == null) continue;
            var h = b.GetComponent<Health>();
            if (h == null || h.IsDead) continue;
            if (IsBlacklisted(h)) continue;

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
        if (cachedCastles.TryGetValue(enemyTeam, out var cached) && cached != null && !cached.IsDead)
            return cached;

        var castle = GameRegistry.GetEnemyCastle(unit.TeamId);
        if (castle != null)
        {
            cachedCastles[enemyTeam] = castle.Health;
            return castle.Health;
        }
        return null;
    }

    private void MoveTowardTarget()
    {
        if (targetHealth == null) return;
        Debug.Assert(movement != null, $"[UnitCombat] {gameObject.name} MoveTowardTarget: movement is null", this);

        bool log = ShouldLog() && GameDebug.Combat;

        // Invalidate cache when mobile target has moved significantly
        if (hasApproachCache && cachedApproachTarget == targetHealth
            && (targetHealth.transform.position - cachedTargetPos).sqrMagnitude > TargetMovedThresholdSq)
        {
            InvalidateApproachCache();
        }

        if (hasApproachCache && cachedApproachTarget == targetHealth)
        {
            if (movement.IsMoving || movement.HasPath)
            {
                EnsureMovingState();
                if (approachRecalcCooldown > 0f)
                    return;
            }
            else
            {
                float distToCached = Vector3.Distance(transform.position, cachedApproachPos);
                if (distToCached > 1.5f)
                {
                    if (log)
                        Debug.Log($"{UnitTag()} MOVE(cached) -> {targetHealth.name} dest={cachedApproachPos:F1} distToDest={distToCached:F1}");
                    movement.SetDestinationWorld(cachedApproachPos);
                    EnsureMovingState();
                    return;
                }

                if (approachRecalcCooldown > 0f)
                    return;
            }
        }

        if (approachRecalcCooldown > 0f && hasApproachCache)
            return;

        var grid = GridSystem.Instance;
        if (grid == null)
        {
            Debug.LogError($"[UnitCombat] {gameObject.name} MoveTowardTarget: GridSystem.Instance is null", this);
            return;
        }

        var (attackCell, posFound) = AttackPositionFinder.FindAttackPosition(unit, targetHealth, grid, grid.ClearanceMap);
        if (!posFound)
        {
            Debug.LogWarning($"[UnitCombat] {gameObject.name} MoveTowardTarget: no attack position found for target {targetHealth.name}", this);
            return;
        }

        Vector3 destination = grid.CellToWorld(attackCell);

        cachedApproachTarget = targetHealth;
        cachedApproachPos = destination;
        cachedTargetPos = targetHealth.transform.position;
        hasApproachCache = true;
        approachRecalcCooldown = 0.5f;

        if (log)
        {
            Debug.Log($"{UnitTag()} MOVE(new) -> {targetHealth.name}" +
                $" myPos={transform.position:F1}" +
                $" dest={destination:F1}" +
                $" dist={Vector3.Distance(transform.position, destination):F1}");
        }

        movement.ForceSetDestinationWorld(destination);
        EnsureMovingState();
    }

    /// <summary>
    /// Transitions out of Fighting/Idle to Moving when the unit has an active path,
    /// so the walk animation plays immediately instead of waiting for the next UpdateState frame.
    /// </summary>
    private void EnsureMovingState()
    {
        if (stateMachine == null) return;
        var state = stateMachine.CurrentState;
        if (state == UnitState.Fighting || state == UnitState.Idle)
            stateMachine.SetState(UnitState.Moving);
    }

    /// <summary>
    /// Smoothly rotates the unit to face its current attack target every frame.
    /// Called continuously while in range, not just on attack frames.
    /// </summary>
    private void FaceTarget()
    {
        if (targetHealth == null || targetHealth.IsDead) return;

        Vector3 hitPos = ClosestPointOnTarget(targetHealth);
        Vector3 lookDir = hitPos - transform.position;
        lookDir.y = 0f;

        if (lookDir.sqrMagnitude < 0.001f) return;

        Quaternion targetRot = Quaternion.LookRotation(lookDir.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 15f * Time.deltaTime);
    }

    private void TryAttack()
    {
        attackTimer -= Time.deltaTime;
        if (attackTimer > 0f) return;

        Debug.Assert(unit.Data != null, $"[UnitCombat] {gameObject.name} TryAttack: unit.Data is null", this);
        float attackSpeed = unit.Data.attackSpeed;
        attackTimer = 1f / attackSpeed;

        if (targetHealth == null || targetHealth.IsDead)
        {
            if (GameDebug.Combat)
                Debug.Log($"{UnitTag()} KILL DETECTED in TryAttack — resuming march");
            RegisterToTarget(null);
            stateMachine?.SetState(UnitState.Idle);

            movement?.Resume();
            if (movement != null && !movement.HasPath && !movement.IsMoving)
                movement.SetDestinationToEnemyCastle();
            else if (movement != null && movement.HasPath)
                EnsureMovingState();
            return;
        }

        float dist = DistanceToTarget(targetHealth);
        float range = GetAttackRange();
        float myRadius = unit.EffectiveRadius;
        bool isRanged = unit.Data.isRanged;
        float maxAttackDist = CombatTargeting.GetMaxAttackDistance(range, myRadius, isRanged);

        if (dist > maxAttackDist)
        {
            if (GameDebug.Combat)
                Debug.Log($"{UnitTag()} ATTACK BLOCKED: dist={dist:F2} > maxAttackDist={maxAttackDist:F2}, re-approaching");
            wasInRange = false;
            stateMachine?.SetState(UnitState.Idle);
            MoveTowardTarget();
            return;
        }

        float baseDamage = unit.Data.attackDamage;
        AttackType atkType = unit.Data.attackType;
        ArmorType defArmor = ArmorType.Unarmored;

        var targetUnit = targetHealth.GetComponent<Unit>();
        if (targetUnit?.Data != null)
            defArmor = targetUnit.Data.armorType;

        float damage = DamageSystem.CalculateDamage(baseDamage, atkType, defArmor);

        // Snap to face target on the attack frame for visual accuracy
        Vector3 hitPos = ClosestPointOnTarget(targetHealth);
        Vector3 lookDir = hitPos - transform.position;
        lookDir.y = 0;
        if (lookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(lookDir.normalized);

        stateMachine?.TriggerAttackAnimation(1f / attackSpeed);

        if (unit.Data != null && unit.Data.isRanged)
        {
            Vector3 spawnPos = GetProjectileSpawnPoint();
            float projSpeed = unit.Data.projectileSpeed;

            Projectile.Spawn(spawnPos, targetHealth.transform, projSpeed,
                damage, gameObject, true, atkType);

            RpcSpawnProjectile(targetHealth.netIdentity, spawnPos, projSpeed, (int)atkType);

            if (GameDebug.Combat)
                Debug.Log($"{UnitTag()} RANGED {targetHealth.name} base={baseDamage:F0} {atkType}vs{defArmor} final={damage:F1} speed={projSpeed}");
        }
        else
        {
            targetHealth.TakeDamage(damage, gameObject);

            if (GameDebug.Combat)
                Debug.Log($"{UnitTag()} MELEE {targetHealth.name} base={baseDamage:F0} {atkType}vs{defArmor} final={damage:F1} targetHP={targetHealth.CurrentHealth:F0}/{targetHealth.MaxHealth:F0}");
        }
    }

    private Vector3 GetProjectileSpawnPoint()
    {
        if (BoundsHelper.TryGetCombinedBounds(gameObject, out var bounds))
            return new Vector3(transform.position.x, bounds.center.y, transform.position.z);
        return transform.position + Vector3.up;
    }

    [ClientRpc]
    private void RpcSpawnProjectile(NetworkIdentity targetId, Vector3 start, float speed, int attackType)
    {
        if (isServer) return;
        if (targetId == null) return;
        Projectile.Spawn(start, targetId.transform, speed, 0f, null, false, (AttackType)attackType);
    }
}
