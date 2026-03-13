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
    private bool hasApproachCache;
    private float approachRecalcCooldown;

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
        if (unit == null || unit.Data == null) return 1f;

        float dataRange = unit.Data.attackRange;

        if (!unit.Data.isRanged)
            return Mathf.Clamp(dataRange, 0.3f, 2f);

        return Mathf.Clamp(dataRange, 1f, 8f);
    }

    private void Awake()
    {
        unit = GetComponent<Unit>();
        movement = GetComponent<UnitMovement>();
        stateMachine = GetComponent<UnitStateMachine>();
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
    }

    private void Update()
    {
        if (!isServer || unit == null || unit.IsDead) return;
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
            float myRadius = unit != null ? unit.EffectiveRadius : 0.5f;
            float effectiveRange = range + myRadius;

            bool targetIsStructure = IsStructure(targetHealth);

            float disengageRange = wasInRange
                ? effectiveRange + effectiveRange * 0.15f
                : effectiveRange;
            bool inRange = dist <= disengageRange;

            bool pathDone = movement != null && !movement.IsMoving && !movement.HasPath;

            // When the unit has finished moving (arrived as close as it can), grant a
            // generous tolerance so it doesn't endlessly re-approach. The tolerance
            // grows with each stuck retry to eventually always succeed.
            if (!inRange && pathDone)
            {
                float arrivalTolerance = myRadius + stuckRetryCount * 0.5f;
                if (dist <= disengageRange + arrivalTolerance)
                {
                    inRange = true;
                    if (GameDebug.Combat && ShouldLog())
                        Debug.Log($"{UnitTag()} IN RANGE(tolerance) dist={dist:F2} effRange={disengageRange:F2} tolerance={arrivalTolerance:F2}");
                }
            }

            if (inRange)
            {
                if (!wasInRange && GameDebug.Combat)
                    Debug.Log($"{UnitTag()} IN RANGE of {targetHealth.name} dist={dist:F2} effRange={effectiveRange:F2} disengage={disengageRange:F2}");
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
                        Debug.Log($"{UnitTag()} LEFT RANGE of {targetHealth.name} dist={dist:F2} effRange={effectiveRange:F2} disengage={disengageRange:F2}");

                    if (stateMachine != null && stateMachine.CurrentState == UnitState.Fighting)
                        stateMachine.SetState(UnitState.Idle);
                }
                wasInRange = false;

                float progressThreshold = targetIsStructure ? 2f : 0.5f;
                if (dist < lastApproachDist - progressThreshold)
                {
                    lastApproachDist = dist;
                    approachStallTimer = 0f;
                }
                else
                {
                    approachStallTimer += Time.deltaTime;
                }

                float retryTime = targetIsStructure ? 2f : 3f;
                if (approachStallTimer > retryTime)
                {
                    stuckRetryCount++;
                    if (stuckRetryCount >= 4)
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
            wasInRange = false;
            approachStallTimer = 0f;
            lastApproachDist = float.MaxValue;
            RegisterToTarget(null);
            if (stateMachine != null && stateMachine.CurrentState == UnitState.Fighting)
                stateMachine.SetState(UnitState.Idle);
            movement?.Resume();
            movement?.SetDestinationToEnemyCastle();
        }

        if (targetHealth == null && movement != null
            && !movement.IsMoving && !movement.HasPath
            && !movement.IsDestinationUnreachable && !movement.IsWaitingForPath
            && stateMachine != null && stateMachine.CurrentState == UnitState.Idle)
        {
            idleRecoveryTimer -= Time.deltaTime;
            if (idleRecoveryTimer <= 0f)
            {
                idleRecoveryTimer = 2f;
                if (GameDebug.Combat)
                    Debug.Log($"{UnitTag()} IDLE RECOVERY: no target, no path — resuming castle march");
                movement.Resume();
                movement.SetDestinationToEnemyCastle();
            }
        }
        else
        {
            idleRecoveryTimer = 0f;
        }
    }

    [Server]
    private void ScanForTarget()
    {
        debugLogCounter++;
        bool log = ShouldLog() && GameDebug.Combat;

        float myRadius = unit != null ? unit.EffectiveRadius : 0.5f;
        float attackRangeVal = GetAttackRange();
        float aggroRange = attackRangeVal + 4f + myRadius;
        aggroRange = Mathf.Clamp(aggroRange, 5f, 12f);
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
        return TeamManager.Instance != null
            ? TeamManager.Instance.GetEnemyTeamId(unit.TeamId)
            : (unit.TeamId == 0 ? 1 : 0);
    }

    private void BlacklistTarget(Health target)
    {
        blacklistedTarget = target;
        blacklistExpiry = Time.time + 8f;
        if (GameDebug.Combat)
            Debug.Log($"{UnitTag()} BLACKLISTED {(target != null ? target.name : "null")} for 8s (stuckRetry={stuckRetryCount})");
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

        Castle[] castles = FindObjectsByType<Castle>(FindObjectsSortMode.None);
        foreach (var c in castles)
        {
            if (c.TeamId == enemyTeam)
            {
                cachedCastles[enemyTeam] = c.Health;
                return c.Health;
            }
        }
        return null;
    }

    [Server]
    private void MoveTowardTarget()
    {
        if (targetHealth == null || movement == null) return;

        // Moving to approach means the unit is NOT fighting — clear Fighting
        // state so the walk animation plays instead of attack/idle pose.
        if (stateMachine != null && stateMachine.CurrentState == UnitState.Fighting)
            stateMachine.SetState(UnitState.Idle);

        bool log = ShouldLog() && GameDebug.Combat;

        if (hasApproachCache && cachedApproachTarget == targetHealth)
        {
            if (movement.IsMoving || movement.HasPath)
            {
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
                    return;
                }

                if (approachRecalcCooldown > 0f)
                    return;
            }
        }

        if (approachRecalcCooldown > 0f && hasApproachCache)
            return;

        var grid = GridSystem.Instance;
        if (grid == null) return;

        Vector2Int attackCell = AttackPositionFinder.FindAttackPosition(unit, targetHealth, grid, grid.ClearanceMap);
        Vector3 destination = grid.CellToWorld(attackCell);

        cachedApproachTarget = targetHealth;
        cachedApproachPos = destination;
        hasApproachCache = true;
        approachRecalcCooldown = 2f;

        if (log)
        {
            Debug.Log($"{UnitTag()} MOVE(new) -> {targetHealth.name}" +
                $" myPos={transform.position:F1}" +
                $" dest={destination:F1}" +
                $" dist={Vector3.Distance(transform.position, destination):F1}");
        }

        movement.ForceSetDestinationWorld(destination);
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

    [Server]
    private void TryAttack()
    {
        attackTimer -= Time.deltaTime;
        if (attackTimer > 0f) return;

        float attackSpeed = unit.Data != null ? unit.Data.attackSpeed : 1f;
        attackTimer = 1f / attackSpeed;

        if (targetHealth == null || targetHealth.IsDead)
        {
            RegisterToTarget(null);
            stateMachine?.SetState(UnitState.Idle);
            movement?.Resume();
            return;
        }

        float dist = DistanceToTarget(targetHealth);
        float range = GetAttackRange();
        float myRadius = unit != null ? unit.EffectiveRadius : 0.5f;
        float maxAttackDist = range + myRadius;
        if (unit.Data != null && unit.Data.isRanged)
            maxAttackDist *= 1.15f;

        // Grant the same arrival tolerance used in the range check,
        // so a unit accepted via tolerance can actually execute attacks.
        maxAttackDist += myRadius;

        if (dist > maxAttackDist)
        {
            if (GameDebug.Combat)
                Debug.Log($"{UnitTag()} ATTACK BLOCKED: dist={dist:F2} > maxAttackDist={maxAttackDist:F2}, re-approaching");
            wasInRange = false;
            stateMachine?.SetState(UnitState.Idle);
            MoveTowardTarget();
            return;
        }

        float baseDamage = unit.Data != null ? unit.Data.attackDamage : 5f;
        AttackType atkType = unit.Data != null ? unit.Data.attackType : AttackType.Normal;
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
