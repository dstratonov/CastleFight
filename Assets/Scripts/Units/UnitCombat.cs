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

    private Health cachedApproachTarget;
    private Vector3 cachedApproachPos;
    private bool hasApproachCache;

    public Transform AttackTarget => targetHealth != null && !targetHealth.IsDead ? targetHealth.transform : null;

    private bool ShouldLog() => debugLogCounter % DebugLogInterval == 0;
    private string UnitTag() => $"[Combat:{gameObject.name} t{unit?.TeamId}]";

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
            float myRadius = unit != null ? unit.EffectiveRadius : 0.5f;
            float effectiveRange = range + myRadius;

            bool isStructure = IsStructure(targetHealth);
            if (isStructure)
                effectiveRange += GetTargetRadius(targetHealth) * 0.3f;

            bool inRange = dist <= effectiveRange;

            bool pathDone = movement != null && !movement.IsMoving && !movement.HasPath;
            if (!inRange && pathDone)
            {
                stuckTimer += Time.deltaTime;
                if (stuckTimer > 1f)
                {
                    inRange = dist <= effectiveRange * 1.5f;
                    if (!inRange)
                    {
                        if (GameDebug.Combat)
                            Debug.Log($"{UnitTag()} can't reach {targetHealth.name} dist={dist:F2} effRange={effectiveRange:F2}, re-pathing");
                        stuckTimer = 0f;
                        InvalidateApproachCache();
                        MoveTowardTarget();
                    }
                }
            }
            else if (inRange || (movement != null && movement.IsMoving))
            {
                stuckTimer = 0f;
            }

            if (inRange)
            {
                if (!wasInRange && GameDebug.Combat)
                    Debug.Log($"{UnitTag()} IN RANGE of {targetHealth.name} dist={dist:F2} effRange={effectiveRange:F2}");
                wasInRange = true;
                movement?.Stop();
                stateMachine?.SetState(UnitState.Fighting);
                TryAttack();
            }
            else
            {
                if (wasInRange && GameDebug.Combat)
                    Debug.Log($"{UnitTag()} LEFT RANGE of {targetHealth.name} dist={dist:F2} effRange={effectiveRange:F2}");
                wasInRange = false;
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
        float aggroRange = (unit.Data != null ? unit.Data.attackRange * 2f : 10f) + myRadius;
        aggroRange = Mathf.Max(aggroRange, 8f);
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
            float attackRange = unit.Data != null ? unit.Data.attackRange : 2f;
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
            float distToCached = Vector3.Distance(transform.position, cachedApproachPos);
            if (distToCached > 1.5f)
            {
                if (log)
                    Debug.Log($"{UnitTag()} MOVE(cached) -> {targetHealth.name} dest={cachedApproachPos:F1} distToDest={distToCached:F1}");
                movement.SetDestinationWorld(cachedApproachPos);
                return;
            }
        }

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

        if (log)
        {
            Debug.Log($"{UnitTag()} MOVE(new) -> {targetHealth.name}" +
                $" myPos={transform.position:F1}" +
                $" dest={destination:F1}" +
                $" dist={Vector3.Distance(transform.position, destination):F1}" +
                $" struct={isStructure}");
        }

        movement.SetDestinationWorld(destination);
    }

    private Vector3 FindStructureApproachSlot(GridSystem grid)
    {
        Vector3 targetCenter = GetTargetCenter(targetHealth);
        float targetRadius = GetTargetRadius(targetHealth);
        float attackRange = unit.Data != null ? unit.Data.attackRange : 2f;
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
        float attackRange = unit.Data != null ? unit.Data.attackRange : 2f;
        float unitRadius = unit != null ? unit.EffectiveRadius : 0.5f;

        uint hash = (uint)Mathf.Abs(gameObject.GetInstanceID()) * 2654435761u;
        float unitAngle = ((hash & 0xFFFF) / (float)0xFFFF) * Mathf.PI * 2f;

        int engageCount = Mathf.Max(1, GetEngageCount(targetHealth));
        int slot = Mathf.Abs(gameObject.GetInstanceID()) % engageCount;
        float slotAngle = unitAngle + slot * (Mathf.PI * 2f / engageCount);

        float approachDist = targetRadius + attackRange * 0.5f + unitRadius;
        Vector3 destination = targetCenter + new Vector3(Mathf.Cos(slotAngle), 0f, Mathf.Sin(slotAngle)) * approachDist;
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
        targetHealth.TakeDamage(damage, gameObject);

        if (GameDebug.Combat)
            Debug.Log($"{UnitTag()} ATTACK {targetHealth.name} base={baseDamage:F0} {atkType}vs{defArmor} final={damage:F1} targetHP={targetHealth.CurrentHealth:F0}/{targetHealth.MaxHealth:F0}");

        Vector3 lookTarget = ClosestPointOnTarget(targetHealth);
        Vector3 lookDir = (lookTarget - transform.position);
        lookDir.y = 0;
        if (lookDir.sqrMagnitude > 0.001f)
            transform.forward = lookDir.normalized;

        var unitAnim = GetComponent<UnitAnimator>();
        if (unitAnim != null)
            unitAnim.PlayAttack();
    }
}
