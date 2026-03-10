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
    private float stuckTimer;
    private int stuckRetryCount;

    private Health cachedApproachTarget;
    private Vector3 cachedApproachPos;
    private bool hasApproachCache;
    private float approachRecalcCooldown;

    private Health blacklistedTarget;
    private float blacklistExpiry;

    private float approachTimer;
    private float lastApproachDist = float.MaxValue;

    public Transform AttackTarget => targetHealth != null && !targetHealth.IsDead ? targetHealth.transform : null;

    private bool ShouldLog() => debugLogCounter % DebugLogInterval == 0;
    private string UnitTag() => $"[Combat:{gameObject.name} t{unit?.TeamId}]";

    private float GetAttackRange()
    {
        if (unit == null || unit.Data == null) return 1f;

        float modelRadius = unit.EffectiveRadius;

        if (!unit.Data.isRanged)
        {
            return Mathf.Clamp(modelRadius + 0.3f, 0.8f, 3f);
        }

        float dataRange = unit.Data.attackRange;
        return Mathf.Clamp(dataRange, modelRadius + 1f, 5f);
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
    }

    private void RegisterToTarget(Health newTarget)
    {
        if (newTarget == targetHealth) return;
        UnregisterFromTarget();
        targetHealth = newTarget;
        wasInRange = false;
        stuckTimer = 0f;
        stuckRetryCount = 0;
        approachTimer = 0f;
        lastApproachDist = float.MaxValue;
        InvalidateApproachCache();
        if (targetHealth != null)
        {
            targetEngageCounts.TryGetValue(targetHealth, out int count);
            targetEngageCounts[targetHealth] = count + 1;
        }
    }

    private void InvalidateApproachCache()
    {
        hasApproachCache = false;
        cachedApproachTarget = null;
        approachRecalcCooldown = 0f;
        ReleaseSlotReservation();
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

    private static void CleanupStaleEngageCounts()
    {
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
            scanTimer = scanInterval;
            ScanForTarget();
        }

        if (targetHealth != null && !targetHealth.IsDead)
        {
            if (movement != null && movement.IsDestinationUnreachable)
            {
                stuckRetryCount++;
                if (GameDebug.Combat)
                    Debug.Log($"{UnitTag()} movement reports unreachable for {targetHealth.name}, retry={stuckRetryCount}");

                if (stuckRetryCount >= 3)
                {
                    if (GameDebug.Combat)
                        Debug.Log($"{UnitTag()} giving up on unreachable target {targetHealth.name}");
                    BlacklistTarget(targetHealth);
                    RegisterToTarget(null);
                    stateMachine?.SetState(UnitState.Idle);
                    movement?.SetDestinationToEnemyCastle();
                    return;
                }

                InvalidateApproachCache();
                approachRecalcCooldown = 0f;
                MoveTowardTarget();
                return;
            }

            float dist = DistanceToTarget(targetHealth);
            float range = GetAttackRange();
            float myRadius = unit != null ? unit.EffectiveRadius : 0.5f;
            float effectiveRange = range + myRadius;

            float disengageRange = wasInRange
                ? effectiveRange + Mathf.Max(1f, effectiveRange * 0.2f)
                : effectiveRange;
            bool inRange = dist <= disengageRange;

            bool pathDone = movement != null && !movement.IsMoving && !movement.HasPath;
            if (!inRange && pathDone)
            {
                stuckTimer += Time.deltaTime;
                float leeway = disengageRange + 0.5f;
                if (dist <= leeway)
                {
                    inRange = true;
                    stuckTimer = 0f;
                }
                else if (stuckTimer > 2.5f)
                {
                    stuckRetryCount++;
                    if (stuckRetryCount >= 3)
                    {
                        if (GameDebug.Combat)
                            Debug.Log($"{UnitTag()} stuck too many times on {targetHealth.name}, dropping target");
                        BlacklistTarget(targetHealth);
                        RegisterToTarget(null);
                        stateMachine?.SetState(UnitState.Idle);
                        movement?.SetDestinationToEnemyCastle();
                        return;
                    }

                    if (GameDebug.Combat)
                        Debug.Log($"{UnitTag()} can't reach {targetHealth.name} dist={dist:F2} effRange={effectiveRange:F2}, re-pathing (retry={stuckRetryCount})");
                    stuckTimer = 0f;
                    InvalidateApproachCache();
                    approachRecalcCooldown = 0f;
                    MoveTowardTarget();
                }
            }
            else if (inRange || (movement != null && movement.IsMoving))
            {
                stuckTimer = 0f;
            }

            if (inRange)
            {
                if (!wasInRange && GameDebug.Combat)
                    Debug.Log($"{UnitTag()} IN RANGE of {targetHealth.name} dist={dist:F2} effRange={effectiveRange:F2} disengage={disengageRange:F2}");
                wasInRange = true;
                stuckRetryCount = 0;
                approachTimer = 0f;
                movement?.Stop();
                stateMachine?.SetState(UnitState.Fighting);
                TryAttack();
            }
            else
            {
                if (wasInRange && GameDebug.Combat)
                    Debug.Log($"{UnitTag()} LEFT RANGE of {targetHealth.name} dist={dist:F2} effRange={effectiveRange:F2} disengage={disengageRange:F2}");
                wasInRange = false;

                approachTimer += Time.deltaTime;
                if (dist < lastApproachDist - 0.5f)
                {
                    lastApproachDist = dist;
                    approachTimer = 0f;
                }

                if (approachTimer > 5f && GameDebug.Combat && Time.frameCount % 60 == 0)
                    Debug.Log($"{UnitTag()} APPROACH STALL t={approachTimer:F1}s target={targetHealth.name} dist={dist:F2} lastBest={lastApproachDist:F2} moving={movement?.IsMoving} hasPath={movement?.HasPath}");

                if (approachTimer > 10f)
                {
                    if (GameDebug.Combat)
                        Debug.Log($"{UnitTag()} APPROACH TIMEOUT on {targetHealth.name} dist={dist:F2}, no progress for 10s");
                    BlacklistTarget(targetHealth);
                    RegisterToTarget(null);
                    stateMachine?.SetState(UnitState.Idle);
                    movement?.SetDestinationToEnemyCastle();
                    return;
                }

                if (movement != null && !movement.IsMoving)
                    MoveTowardTarget();
            }
        }
        else
        {
            wasInRange = false;
            stuckTimer = 0f;
            if (stateMachine != null && stateMachine.CurrentState == UnitState.Fighting)
            {
                if (GameDebug.Combat)
                    Debug.Log($"{UnitTag()} Target lost/dead -> Idle");
                stateMachine.SetState(UnitState.Idle);
            }
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
        if (movement != null && !movement.HasPath && !movement.IsMoving)
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
        var enemies = UnitManager.Instance.GetTeamUnits(enemyTeam);
        float maxRangeSq = maxRange * maxRange;

        var candidates = new List<(Health health, float distSq)>();
        foreach (var enemy in enemies)
        {
            if (enemy == null || enemy.IsDead) continue;
            float distSq = (enemy.transform.position - transform.position).sqrMagnitude;
            if (distSq > maxRangeSq) continue;

            var h = enemy.GetComponent<Health>();
            if (h == null || h.IsDead) continue;
            if (IsBlacklisted(h)) continue;

            candidates.Add((h, distSq));
        }

        candidates.Sort((a, b) => a.distSq.CompareTo(b.distSq));

        foreach (var (health, _) in candidates)
        {
            if (GetEngageCount(health) < MaxEngagersPerUnit)
                return health;
        }

        if (candidates.Count > 0)
            return candidates[0].health;

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

    private bool IsStructure(Health h)
    {
        return h.GetComponent<Castle>() != null || h.GetComponent<Building>() != null;
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
        bool isStructure = IsStructure(targetHealth);

        Vector3 destination;

        if (isStructure && grid != null)
        {
            destination = FindStructureApproachSlot(grid);
        }
        else
        {
            destination = FindUnitApproachPoint(grid);
        }

        cachedApproachTarget = targetHealth;
        cachedApproachPos = destination;
        hasApproachCache = true;
        approachRecalcCooldown = 2f;

        if (log)
        {
            Debug.Log($"{UnitTag()} MOVE(new) -> {targetHealth.name}" +
                $" myPos={transform.position:F1}" +
                $" dest={destination:F1}" +
                $" dist={Vector3.Distance(transform.position, destination):F1}" +
                $" struct={isStructure}");
        }

        movement.ForceSetDestinationWorld(destination);
    }

    private Vector3 FindStructureApproachSlot(GridSystem grid)
    {
        Vector3 targetCenter = GetTargetCenter(targetHealth);
        float targetRadius = GetTargetRadius(targetHealth);
        float attackRange = GetAttackRange();
        float unitRadius = unit != null ? unit.EffectiveRadius : 0.5f;

        Vector2Int centerCell = grid.WorldToCell(targetCenter);
        int searchRadius = Mathf.CeilToInt((targetRadius + attackRange + unitRadius) / grid.CellSize) + 2;

        Vector3 myPos = transform.position;
        var borderCells = new List<(Vector2Int cell, Vector3 pos, float distToMe)>();

        for (int dx = -searchRadius; dx <= searchRadius; dx++)
        {
            for (int dz = -searchRadius; dz <= searchRadius; dz++)
            {
                Vector2Int cell = new(centerCell.x + dx, centerCell.y + dz);
                if (!grid.IsInBounds(cell) || !grid.IsWalkable(cell)) continue;

                bool touchesStructure = false;
                for (int nx = -1; nx <= 1 && !touchesStructure; nx++)
                {
                    for (int nz = -1; nz <= 1 && !touchesStructure; nz++)
                    {
                        if (nx == 0 && nz == 0) continue;
                        Vector2Int neighbor = new(cell.x + nx, cell.y + nz);
                        if (grid.IsInBounds(neighbor) && !grid.IsWalkable(neighbor))
                            touchesStructure = true;
                    }
                }

                if (!touchesStructure) continue;

                Vector3 cellWorld = grid.CellToWorld(cell);
                float distToTarget = Vector3.Distance(cellWorld, targetCenter);
                if (distToTarget > targetRadius + attackRange * 3f) continue;

                float distToMe = Vector3.Distance(cellWorld, myPos);
                borderCells.Add((cell, cellWorld, distToMe));
            }
        }

        if (borderCells.Count == 0)
            return grid.FindNearestWalkablePosition(targetCenter, myPos);

        borderCells.Sort((a, b) => a.distToMe.CompareTo(b.distToMe));

        foreach (var (cell, pos, _) in borderCells)
        {
            if (!IsSlotTaken(targetHealth, cell))
            {
                ReserveSlot(targetHealth, cell);
                return pos;
            }
        }

        ReserveSlot(targetHealth, borderCells[0].cell);
        return borderCells[0].pos;
    }

    private Vector3 FindUnitApproachPoint(GridSystem grid)
    {
        Vector3 targetCenter = GetTargetCenter(targetHealth);
        float targetRadius = GetTargetRadius(targetHealth);
        float attackRange = GetAttackRange();
        float unitRadius = unit != null ? unit.EffectiveRadius : 0.5f;

        Vector3 dirToTarget = transform.position - targetCenter;
        dirToTarget.y = 0f;
        if (dirToTarget.sqrMagnitude < 0.01f)
        {
            uint hash = (uint)Mathf.Abs(gameObject.GetInstanceID()) * 2654435761u;
            float angle = ((hash & 0xFFFF) / (float)0xFFFF) * Mathf.PI * 2f;
            dirToTarget = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }
        dirToTarget.Normalize();

        float approachDist = targetRadius + attackRange * 0.5f + unitRadius;
        Vector3 destination = targetCenter + dirToTarget * approachDist;
        destination.y = transform.position.y;

        if (grid != null)
        {
            Vector2Int cell = grid.WorldToCell(destination);
            if (!grid.IsInBounds(cell) || !grid.IsWalkable(cell))
                destination = grid.FindNearestWalkablePosition(destination, transform.position);
        }

        return destination;
    }

    private Vector3 GetTargetCenter(Health target)
    {
        return BoundsHelper.GetCenter(target.gameObject);
    }

    private float GetTargetRadius(Health target)
    {
        return BoundsHelper.GetRadius(target.gameObject);
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

        float baseDamage = unit.Data != null ? unit.Data.attackDamage : 5f;
        AttackType atkType = unit.Data != null ? unit.Data.attackType : AttackType.Normal;
        ArmorType defArmor = ArmorType.Unarmored;

        var targetUnit = targetHealth.GetComponent<Unit>();
        if (targetUnit?.Data != null)
            defArmor = targetUnit.Data.armorType;

        float damage = DamageSystem.CalculateDamage(baseDamage, atkType, defArmor);

        Vector3 hitPos = ClosestPointOnTarget(targetHealth);
        Vector3 lookDir = hitPos - transform.position;
        lookDir.y = 0;
        if (lookDir.sqrMagnitude > 0.001f)
            transform.forward = lookDir.normalized;

        var unitAnim = GetComponent<UnitAnimator>();
        if (unitAnim != null)
            unitAnim.PlayAttack();

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
