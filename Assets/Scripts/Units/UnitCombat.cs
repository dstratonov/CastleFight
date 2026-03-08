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

            bool reachedDestination = movement != null && !movement.IsMoving && !movement.HasPath;
            bool isStructure = targetHealth.GetComponent<Castle>() != null ||
                               targetHealth.GetComponent<Building>() != null;
            bool stillPathing = movement != null && (movement.IsMoving || movement.HasPath);

            bool inRange;
            if (isStructure && stillPathing)
                inRange = false;
            else if (reachedDestination)
                inRange = dist <= range * 2.5f;
            else
                inRange = dist <= range;

            if (inRange)
            {
                if (!wasInRange)
                    Debug.Log($"{UnitTag()} IN RANGE of {targetHealth.name} dist={dist:F2} range={range:F2} reached={reachedDestination}");
                wasInRange = true;
                movement?.Stop();
                stateMachine?.SetState(UnitState.Fighting);
                TryAttack();
            }
            else
            {
                if (wasInRange)
                    Debug.Log($"{UnitTag()} LEFT RANGE of {targetHealth.name} dist={dist:F2} range={range:F2}");
                wasInRange = false;
                if (movement != null && !movement.IsMoving)
                {
                    if (ShouldLog())
                        Debug.Log($"{UnitTag()} OUT OF RANGE of {targetHealth.name} dist={dist:F2} range={range:F2}, not moving -> re-path");
                    MoveTowardTarget();
                }
            }
        }
        else
        {
            wasInRange = false;
            if (stateMachine != null && stateMachine.CurrentState == UnitState.Fighting)
            {
                Debug.Log($"{UnitTag()} Target lost/dead -> Idle");
                stateMachine.SetState(UnitState.Idle);
            }
        }
    }

    [Server]
    private void ScanForTarget()
    {
        debugLogCounter++;
        bool log = ShouldLog();

        if (targetHealth != null && !targetHealth.IsDead)
        {
            float dist = DistanceToTarget(targetHealth);
            float scanRange = unit.Data != null ? unit.Data.attackRange * 2f : 10f;
            if (dist <= scanRange)
            {
                if (log)
                    Debug.Log($"{UnitTag()} SCAN: keeping target {targetHealth.name} dist={dist:F2} scanRange={scanRange:F2} engagers={GetEngageCount(targetHealth)}");
                return;
            }
            if (log)
                Debug.Log($"{UnitTag()} SCAN: target {targetHealth.name} too far dist={dist:F2} > scanRange={scanRange:F2}, rescanning");
        }

        float range = unit.Data != null ? unit.Data.attackRange * 2f : 10f;
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

        if (log)
            Debug.Log($"{UnitTag()} SCAN: NO TARGET found, hasPath={movement?.HasPath} isMoving={movement?.IsMoving} -> fallback to castle march");
        RegisterToTarget(null);
        if (movement != null && !movement.HasPath && !movement.IsMoving)
            movement.SetDestinationToEnemyCastle();
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
        var renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds combined = default;
            bool first = true;
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                if (first) { combined = r.bounds; first = false; }
                else combined.Encapsulate(r.bounds);
            }
            if (!first)
                return combined.ClosestPoint(transform.position);
        }

        var col = target.GetComponent<Collider>();
        if (col != null)
            return col.ClosestPoint(transform.position);

        return target.transform.position;
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
        bool log = ShouldLog();

        Vector3 targetCenter = GetTargetCenter(targetHealth);
        float targetRadius = GetTargetRadius(targetHealth);
        float attackRange = unit.Data != null ? unit.Data.attackRange : 2f;

        int engageCount = GetEngageCount(targetHealth);
        float unitRadius = unit != null ? unit.EffectiveRadius : 0.5f;
        Vector3 destination;

        if (engageCount <= 1 || targetRadius < 1f)
        {
            destination = ClosestPointOnTarget(targetHealth);
            destination.y = transform.position.y;
        }
        else
        {
            float angle = GetInstanceAngle();
            float approachDist = targetRadius + attackRange * 0.5f + unitRadius;
            destination = targetCenter + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * approachDist;
            destination.y = transform.position.y;
        }

        var grid = GridSystem.Instance;
        Vector3 preSnap = destination;
        if (grid != null)
            destination = grid.FindNearestWalkablePosition(destination, transform.position);

        if (log)
        {
            float snapDelta = Vector3.Distance(preSnap, destination);
            Debug.Log($"{UnitTag()} MOVE -> {targetHealth.name}" +
                $"\n  myPos={transform.position}" +
                $"\n  targetCenter={targetCenter} radius={targetRadius:F1}" +
                $"\n  engagers={engageCount} angle={GetInstanceAngle() * Mathf.Rad2Deg:F0}deg" +
                $"\n  preSnap={preSnap}" +
                $"\n  afterWalkSnap={destination} (moved {snapDelta:F2})" +
                $"\n  finalDist={Vector3.Distance(transform.position, destination):F2}");
        }

        movement.SetDestinationWorld(destination);
    }

    private Vector3 GetTargetCenter(Health target)
    {
        var renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds combined = default;
            bool first = true;
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                if (first) { combined = r.bounds; first = false; }
                else combined.Encapsulate(r.bounds);
            }
            if (!first)
            {
                Vector3 c = combined.center;
                c.y = target.transform.position.y;
                return c;
            }
        }
        return target.transform.position;
    }

    private float GetTargetRadius(Health target)
    {
        var renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds combined = default;
            bool first = true;
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                if (first) { combined = r.bounds; first = false; }
                else combined.Encapsulate(r.bounds);
            }
            if (!first)
                return Mathf.Max(combined.extents.x, combined.extents.z);
        }
        return 0.5f;
    }

    private float GetInstanceAngle()
    {
        int id = gameObject.GetInstanceID();
        float hash = ((id * 2654435761u) & 0xFFFFFF) / (float)0xFFFFFF;
        return hash * Mathf.PI * 2f;
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
