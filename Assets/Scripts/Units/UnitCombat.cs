using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class UnitCombat : NetworkBehaviour
{
    [SerializeField] private float scanInterval = 0.25f;

    private static readonly Dictionary<Health, int> targetEngageCounts = new();

    private Unit unit;
    private UnitMovement movement;
    private UnitStateMachine stateMachine;
    private Health targetHealth;
    private float scanTimer;
    private float attackTimer;
    private int debugLogCounter;
    private const int DebugLogInterval = 16;
    private bool wasInRange;

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
        UnregisterFromTarget();
    }

    private void RegisterToTarget(Health newTarget)
    {
        if (newTarget == targetHealth) return;
        UnregisterFromTarget();
        targetHealth = newTarget;
        if (targetHealth != null)
        {
            targetEngageCounts.TryGetValue(targetHealth, out int count);
            targetEngageCounts[targetHealth] = count + 1;
        }
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

    private void Update()
    {
        if (!isServer || unit == null || unit.IsDead) return;
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver) return;

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

            bool reachedDestination = movement != null && !movement.IsMoving && !movement.HasPath;
            bool isStructure = targetHealth.GetComponent<Castle>() != null ||
                               targetHealth.GetComponent<Building>() != null;
            bool stillPathing = movement != null && (movement.IsMoving || movement.HasPath);

            bool inRange;
            if (isStructure && stillPathing)
                inRange = false;
            else if (reachedDestination)
                inRange = dist <= effectiveRange * 2.5f;
            else
                inRange = dist <= effectiveRange;

            if (inRange)
            {
                if (!wasInRange && GameDebug.Combat)
                    Debug.Log($"{UnitTag()} IN RANGE of {targetHealth.name} dist={dist:F2} effRange={effectiveRange:F2} reached={reachedDestination}");
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
                {
                    if (ShouldLog() && GameDebug.Combat)
                        Debug.Log($"{UnitTag()} OUT OF RANGE of {targetHealth.name} dist={dist:F2} effRange={effectiveRange:F2}, not moving -> re-path");
                    MoveTowardTarget();
                }
            }
        }
        else
        {
            wasInRange = false;
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

        if (targetHealth != null && !targetHealth.IsDead)
        {
            float dist = DistanceToTarget(targetHealth);
            float myRadius = unit != null ? unit.EffectiveRadius : 0.5f;
            float scanRange = (unit.Data != null ? unit.Data.attackRange * 2f : 10f) + myRadius;
            if (dist <= scanRange)
            {
                if (log)
                    Debug.Log($"{UnitTag()} SCAN: keeping target {targetHealth.name} dist={dist:F2} scanRange={scanRange:F2} engagers={GetEngageCount(targetHealth)}");
                return;
            }
            if (log)
                Debug.Log($"{UnitTag()} SCAN: target {targetHealth.name} too far dist={dist:F2} > scanRange={scanRange:F2}, rescanning");
        }

        float unitRad = unit != null ? unit.EffectiveRadius : 0.5f;
        float range = (unit.Data != null ? unit.Data.attackRange * 2f : 10f) + unitRad;
        int enemyTeam = GetEnemyTeam();

        Health bestTarget = FindBestEnemyUnit(range);
        if (bestTarget != null)
        {
            if (log)
                Debug.Log($"{UnitTag()} SCAN: found enemy UNIT {bestTarget.name} dist={DistanceToTarget(bestTarget):F2} engagers={GetEngageCount(bestTarget)} range={range:F2}");
            RegisterToTarget(bestTarget);
            MoveTowardTarget();
            return;
        }

        Health nearestBuildingHealth = FindNearestEnemyBuilding(enemyTeam, range);
        if (nearestBuildingHealth != null)
        {
            if (log)
                Debug.Log($"{UnitTag()} SCAN: found enemy BUILDING {nearestBuildingHealth.name} dist={DistanceToTarget(nearestBuildingHealth):F2} engagers={GetEngageCount(nearestBuildingHealth)}");
            RegisterToTarget(nearestBuildingHealth);
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
                Debug.Log($"{UnitTag()} SCAN: targeting CASTLE {castleHealth.name} dist={castleDist:F2} atkRange={attackRange:F2} engagers={GetEngageCount(castleHealth)}" +
                    (castleDist > attackRange ? " -> moving" : " -> in range"));
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

        int enemyTeam = TeamManager.Instance != null
            ? TeamManager.Instance.GetEnemyTeamId(unit.TeamId)
            : (unit.TeamId == 0 ? 1 : 0);

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

        Vector3 targetCenter = GetTargetCenter(targetHealth);
        float targetRadius = GetTargetRadius(targetHealth);
        float attackRange = unit.Data != null ? unit.Data.attackRange : 2f;
        float unitRadius = unit != null ? unit.EffectiveRadius : 0.5f;

        var grid = GridSystem.Instance;
        bool isStructure = targetHealth.GetComponent<Castle>() != null ||
                           targetHealth.GetComponent<Building>() != null;

        Vector3 destination;

        if (isStructure && grid != null)
        {
            destination = FindWalkableApproachPoint(grid, targetCenter, targetRadius, attackRange, unitRadius);
        }
        else
        {
            int engageCount = GetEngageCount(targetHealth);

            Vector3 toMe = transform.position - targetCenter;
            toMe.y = 0;
            if (toMe.sqrMagnitude < 0.01f)
                toMe = Vector3.right;
            float baseAngle = Mathf.Atan2(toMe.z, toMe.x);

            float spreadOffset = 0f;
            if (engageCount > 1)
            {
                int id = gameObject.GetInstanceID();
                float hash = ((id * 2654435761u) & 0xFFFFFF) / (float)0xFFFFFF;
                spreadOffset = (hash - 0.5f) * Mathf.PI * 0.5f;
            }
            float angle = baseAngle + spreadOffset;

            float approachDist = targetRadius + attackRange * 0.5f + unitRadius;
            destination = targetCenter + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * approachDist;
            destination.y = transform.position.y;

            if (grid != null)
                destination = grid.FindNearestWalkablePosition(destination, transform.position);
        }

        if (log)
        {
            Debug.Log($"{UnitTag()} MOVE -> {targetHealth.name}" +
                $" myPos={transform.position:F1}" +
                $" dest={destination:F1}" +
                $" dist={Vector3.Distance(transform.position, destination):F1}" +
                $" struct={isStructure}");
        }

        movement.SetDestinationWorld(destination);
    }

    private Vector3 FindWalkableApproachPoint(GridSystem grid, Vector3 targetCenter, float targetRadius, float attackRange, float unitRadius)
    {
        Vector2Int centerCell = grid.WorldToCell(targetCenter);
        int searchRadius = Mathf.CeilToInt((targetRadius + attackRange) / grid.CellSize) + 2;

        Vector3 myPos = transform.position;
        Vector3 bestPos = myPos;
        float bestScore = float.MaxValue;
        bool found = false;

        for (int dx = -searchRadius; dx <= searchRadius; dx++)
        {
            for (int dz = -searchRadius; dz <= searchRadius; dz++)
            {
                Vector2Int cell = new(centerCell.x + dx, centerCell.y + dz);
                if (!grid.IsInBounds(cell) || !grid.IsWalkable(cell)) continue;

                Vector3 cellWorld = grid.CellToWorld(cell);
                float distToTarget = Vector3.Distance(cellWorld, targetCenter);

                float idealDist = targetRadius + attackRange * 0.3f;
                if (distToTarget > targetRadius + attackRange * 2f) continue;
                if (distToTarget < targetRadius * 0.5f) continue;

                float distToMe = Vector3.Distance(cellWorld, myPos);
                float distFromIdeal = Mathf.Abs(distToTarget - idealDist);
                float score = distToMe * 0.6f + distFromIdeal * 0.4f;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestPos = cellWorld;
                    found = true;
                }
            }
        }

        if (!found)
            bestPos = grid.FindNearestWalkablePosition(targetCenter, myPos);

        return bestPos;
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
