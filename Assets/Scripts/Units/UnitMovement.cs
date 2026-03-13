using UnityEngine;
using Mirror;
using System.Collections.Generic;

/// <summary>
/// Unit movement using SC2-style two-layer pathfinding.
/// Layer 1 (NavMesh + A* + Funnel) produces waypoints.
/// Layer 2 (Boids) handles unit-to-unit avoidance each frame.
/// Wall collision is handled by ValidatePosition (physics layer, not Boids).
/// </summary>
public class UnitMovement : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float waypointThreshold = 0.5f;
    [SerializeField] private float rotationSpeed = 10f;

    private Unit unit;
    private UnitStateMachine stateMachine;
    private GridSystem grid;
    private Animator cachedAnimator;

    // Current path (Layer 1 output: vertex-expanded waypoints)
    private List<Vector3> waypoints;
    private int waypointIndex;
    private Vector3? worldTarget;
    private bool isStopped;

    // Replan scheduling (staggered across units, not every frame)
    private int replanFrameSlot;
    private const int ReplanStaggerFrames = 15;
    private bool needsReplan;

    // Stuck detection
    private Vector3 lastProgressPos;
    private float stallTime;
    private int stuckFrameCount;
    private const int StuckThresholdFrames = 90; // ~1.5s at 60fps

    // Near-destination arrival timer: tracks time spent close to destination without arriving.
    // Independent of stallTime — fires even if the unit is sliding/oscillating.
    private float nearDestTimer;
    private const float NearDestArriveTime = 1.5f;

    // Velocity smoothing
    private Vector3 smoothedVelocity;

    // Network position sync
    private Vector3 serverPosition;
    private bool hasServerPos;

    // Previous position (used by Boids for velocity inference)
    private Vector3 previousPosition;
    public Vector3 PreviousPosition => previousPosition;

    // Push/yield for anti-ghosting
    private float yieldTimer;
    private Vector3 yieldDirection;
    private const float YieldDuration = 0.4f;

    // Debug log throttle
    private float debugLogTimer;

    // Public state
    public bool IsMoving => !isStopped && HasPath && !isDensityStopped;
    public bool HasPath => waypoints != null && waypointIndex < waypoints.Count;
    public bool IsDestinationUnreachable { get; private set; }
    public Vector3? WorldTarget => worldTarget;
    public IReadOnlyList<Vector3> Waypoints => waypoints;
    public int WaypointIndex => waypointIndex;
    public bool IsWaitingForPath => false;

    public event System.Action OnReachedDestination;

    private bool isDensityStopped;
    private float densityCheckTimer;

    private static readonly Dictionary<int, Castle> cachedEnemyCastles = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetMovementStatics()
    {
        cachedEnemyCastles.Clear();
    }

    private void Awake()
    {
        unit = GetComponent<Unit>();
        stateMachine = GetComponent<UnitStateMachine>();
        replanFrameSlot = Mathf.Abs(GetInstanceID()) % ReplanStaggerFrames;
    }

    public override void OnStartServer()
    {
        grid = GridSystem.Instance;
        if (grid == null)
            Debug.LogError($"[UnitMovement] GridSystem.Instance is NULL on {gameObject.name}!");

        cachedAnimator = GetComponentInChildren<Animator>();
        if (cachedAnimator != null)
            cachedAnimator.applyRootMotion = false;

        previousPosition = transform.position;
        lastProgressPos = transform.position;
    }

    private void Update()
    {
        if (!isServer)
        {
            if (hasServerPos)
            {
                float speed = (unit != null && unit.Data != null) ? unit.Data.moveSpeed : moveSpeed;
                transform.position = Vector3.MoveTowards(
                    transform.position, serverPosition, speed * 2f * Time.deltaTime);
            }
            return;
        }

        if (unit == null || grid == null || unit.IsDead)
            return;

        previousPosition = transform.position;

        if (isStopped)
        {
            UpdateYield();
            return;
        }

        UpdateYield();

        if (!HasPath && !worldTarget.HasValue)
            return;

        // Direct destination-distance check: if the unit is close to worldTarget,
        // skip waypoint logic and arrive immediately. This prevents getting stuck when
        // the last waypoint is near an obstacle edge that the unit can't quite reach.
        if (worldTarget.HasValue)
        {
            float distToDest = Vector3.Distance(transform.position, worldTarget.Value);
            float arrivalDist = unit != null
                ? Mathf.Max(waypointThreshold, unit.EffectiveRadius * 1.3f)
                : waypointThreshold;
            if (distToDest < arrivalDist)
            {
                ArriveAtDestination();
                return;
            }
        }

        // Waypoint management: advance to next waypoint if close enough
        if (HasPath)
        {
            float threshold = unit != null
                ? Mathf.Max(waypointThreshold, unit.EffectiveRadius * 1.3f)
                : waypointThreshold;

            while (waypointIndex < waypoints.Count)
            {
                float distToWp = Vector3.Distance(transform.position, waypoints[waypointIndex]);
                if (distToWp < threshold)
                {
                    waypointIndex++;
                    if (waypointIndex >= waypoints.Count)
                    {
                        ArriveAtDestination();
                        return;
                    }
                }
                else break;
            }
        }

        // Density stop near destination
        if (worldTarget.HasValue && HasPath && waypointIndex >= waypoints.Count - 2)
        {
            densityCheckTimer -= Time.deltaTime;
            if (densityCheckTimer <= 0f)
            {
                densityCheckTimer = 0.3f;
                var pfm = PathfindingManager.Instance;
                isDensityStopped = pfm != null && pfm.ShouldDensityStop(unit, worldTarget.Value);
            }

            if (isDensityStopped)
            {
                smoothedVelocity = Vector3.zero;
                TrackProgress();
                return;
            }
        }

        // Movement + Boids (every frame)
        if (HasPath)
            MoveAlongPath();
        else if (worldTarget.HasValue)
            MoveDirectToward(worldTarget.Value);

        // Staggered replan check
        if ((Time.frameCount % ReplanStaggerFrames) == replanFrameSlot)
            CheckReplan();

        // Periodic network sync
        if ((Time.frameCount + (GetInstanceID() & 0xFF)) % 10 == 0)
            RpcSyncPositionAndRotation(transform.position, transform.rotation);
    }

    // ================================================================
    //  CORE MOVEMENT (Layer 1 direction + Layer 2 Boids)
    // ================================================================

    private void MoveAlongPath()
    {
        float speed = (unit != null && unit.Data != null) ? unit.Data.moveSpeed : moveSpeed;
        Vector3 nextWp = waypoints[waypointIndex];
        Vector3 dir = nextWp - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f)
        {
            waypointIndex++;
            return;
        }

        Vector3 desiredVelocity = dir.normalized * speed;

        var pfm = PathfindingManager.Instance;
        bool isMarching = stateMachine == null || stateMachine.CurrentState == UnitState.Moving;
        Vector3 finalVelocity = pfm != null
            ? pfm.ComputeSteering(unit, desiredVelocity, speed, isMarching)
            : desiredVelocity;

        // Deep log: detect when Boids significantly redirects the unit
        if (GameDebug.Movement && desiredVelocity.sqrMagnitude > 0.01f && finalVelocity.sqrMagnitude > 0.01f)
        {
            float dirDot = Vector3.Dot(finalVelocity.normalized, desiredVelocity.normalized);
            if (dirDot < 0f)
            {
                debugLogTimer = 0f; // force next log
                Debug.LogWarning($"[Move:{gameObject.name}] BOIDS REVERSED direction! " +
                    $"desired={desiredVelocity:F2} final={finalVelocity:F2} dot={dirDot:F2}");
            }
        }

        smoothedVelocity = SmoothDamp(smoothedVelocity, finalVelocity, 10f);

        Vector3 oldPos = transform.position;
        Vector3 newPos = oldPos + smoothedVelocity * Time.deltaTime;
        newPos.y = grid.GridOrigin.y;

        newPos = ValidatePosition(oldPos, newPos);
        transform.position = newPos;

        if (GameDebug.Movement)
        {
            debugLogTimer -= Time.deltaTime;
            if (debugLogTimer <= 0f)
            {
                debugLogTimer = 2f;
                float distToWp = waypointIndex < waypoints.Count ? Vector3.Distance(transform.position, waypoints[waypointIndex]) : -1;
                float distToTarget = worldTarget.HasValue ? Vector3.Distance(transform.position, worldTarget.Value) : -1;
                Debug.Log($"[Move:{gameObject.name}] pos={transform.position:F1} wpIdx={waypointIndex}/{waypoints.Count} " +
                    $"distToWp={distToWp:F1} distToTarget={distToTarget:F1} " +
                    $"speed={smoothedVelocity.magnitude:F2}/{speed:F1} stall={stallTime:F1} " +
                    $"marching={isMarching} densStop={isDensityStopped}");
            }
        }

        ApplyRotation(newPos - oldPos);
        TrackProgress();
    }

    private void MoveDirectToward(Vector3 target)
    {
        float speed = (unit != null && unit.Data != null) ? unit.Data.moveSpeed : moveSpeed;
        Vector3 dir = target - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f)
        {
            ArriveAtDestination();
            return;
        }

        float dist = dir.magnitude;
        float threshold = unit != null ? Mathf.Max(waypointThreshold, unit.EffectiveRadius * 1.3f) : waypointThreshold;
        if (dist < threshold)
        {
            ArriveAtDestination();
            return;
        }

        Vector3 desiredVelocity = (dir / dist) * speed;
        var pfm = PathfindingManager.Instance;
        Vector3 finalVelocity = pfm != null
            ? pfm.ComputeSteering(unit, desiredVelocity, speed, true)
            : desiredVelocity;

        smoothedVelocity = SmoothDamp(smoothedVelocity, finalVelocity, 10f);

        Vector3 oldPos = transform.position;
        Vector3 newPos = oldPos + smoothedVelocity * Time.deltaTime;
        newPos.y = grid.GridOrigin.y;
        newPos = ValidatePosition(oldPos, newPos);
        transform.position = newPos;

        ApplyRotation(newPos - oldPos);
        TrackProgress();
    }

    // ================================================================
    //  PATH REQUESTS
    // ================================================================

    [Server]
    public void SetDestinationWorld(Vector3 target)
    {
        if (grid != null)
        {
            Vector2Int cell = grid.WorldToCell(target);
            if (!grid.IsInBounds(cell) || !grid.IsWalkable(cell))
            {
                var pfm = PathfindingManager.Instance;
                Vector3 corrected = pfm != null
                    ? pfm.FindNearestWalkable(target)
                    : grid.FindNearestWalkablePosition(target, transform.position);
                if (GameDebug.Movement)
                    Debug.Log($"[Move:{gameObject.name}] dest not walkable, corrected {target:F1} -> {corrected:F1}");
                target = corrected;
            }
        }

        if (worldTarget.HasValue && HasPath)
        {
            float threshold = grid != null ? grid.CellSize : 1.5f;
            if (Vector3.Distance(target, worldTarget.Value) < threshold)
                return;
        }

        if (GameDebug.Movement)
            Debug.Log($"[Move:{gameObject.name}] SetDest pos={transform.position:F1} -> target={target:F1}");

        worldTarget = target;
        isStopped = false;
        stallTime = 0f;
        stuckFrameCount = 0;
        nearDestTimer = 0f;
        isDensityStopped = false;
        IsDestinationUnreachable = false;
        lastProgressPos = transform.position;
        smoothedVelocity = Vector3.zero;
        needsReplan = false;

        ComputePath();
    }

    [Server]
    public void ForceSetDestinationWorld(Vector3 target)
    {
        // Preserve nearDestTimer if the new target is close to the old one —
        // combat recalcs every 2s and shouldn't reset the "almost arrived" timer.
        float preservedNearDestTimer = 0f;
        if (worldTarget.HasValue)
        {
            float shift = Vector3.Distance(target, worldTarget.Value);
            if (shift < (grid != null ? grid.CellSize * 2f : 3f))
                preservedNearDestTimer = nearDestTimer;
        }

        if (GameDebug.Movement)
            Debug.Log($"[Move:{gameObject.name}] FORCE dest={target:F1}");
        worldTarget = null;
        waypoints = null;
        waypointIndex = 0;
        smoothedVelocity = Vector3.zero;
        SetDestinationWorld(target);

        nearDestTimer = preservedNearDestTimer;
    }

    [Server]
    public void SetDestinationToEnemyCastle()
    {
        if (unit == null || grid == null) return;

        int enemyTeam = TeamManager.Instance != null
            ? TeamManager.Instance.GetEnemyTeamId(unit.TeamId)
            : (unit.TeamId == 0 ? 1 : 0);

        if (!cachedEnemyCastles.TryGetValue(enemyTeam, out var castle) || castle == null)
        {
            Castle[] castles = FindObjectsByType<Castle>(FindObjectsSortMode.None);
            foreach (var c in castles)
            {
                if (c.TeamId == enemyTeam)
                {
                    castle = c;
                    cachedEnemyCastles[enemyTeam] = c;
                    break;
                }
            }
        }

        if (castle != null)
        {
            Vector3 castlePos = castle.transform.position;
            // Spread units around the castle so they don't all converge on the same point.
            // Use a deterministic hash-based offset per unit so each unit approaches from a different angle.
            uint hash = (uint)Mathf.Abs(GetInstanceID()) * 2654435761u;
            float angle = ((hash & 0xFFFF) / (float)0xFFFF) * Mathf.PI * 2f;
            float spreadRadius = 3f;
            Vector3 offset = new Vector3(Mathf.Cos(angle) * spreadRadius, 0f, Mathf.Sin(angle) * spreadRadius);
            Vector3 spreadTarget = castlePos + offset;

            Vector3 target = grid.FindNearestWalkablePosition(spreadTarget, transform.position);

            // If the spread position ended up the same as the castle center, try without offset
            Vector2Int targetCell = grid.WorldToCell(target);
            if (!grid.IsWalkable(targetCell))
                target = grid.FindNearestWalkablePosition(castlePos, transform.position);

            if (GameDebug.Movement)
                Debug.Log($"[Move:{gameObject.name}] SetDestToEnemyCastle team={enemyTeam} target={target:F1} angle={angle * Mathf.Rad2Deg:F0}°");
            ForceSetDestinationWorld(target);
        }
    }

    /// <summary>
    /// Compute a new path from current position to worldTarget using NavMesh A* + Funnel.
    /// </summary>
    private void ComputePath()
    {
        if (!worldTarget.HasValue || grid == null) return;

        var pfm = PathfindingManager.Instance;
        if (pfm == null || !pfm.IsInitialized)
        {
            waypoints = new List<Vector3> { worldTarget.Value };
            waypointIndex = 0;
            return;
        }

        float unitRadius = unit != null ? unit.EffectiveRadius : 0.5f;
        var path = pfm.RequestPath(transform.position, worldTarget.Value, unitRadius);

        if (path != null && path.Count > 0)
        {
            waypoints = path;
            waypointIndex = 0;
            IsDestinationUnreachable = false;

            if (GameDebug.Movement)
            {
                float pathLen = 0f;
                for (int i = 1; i < path.Count; i++)
                    pathLen += Vector3.Distance(path[i - 1], path[i]);
                float directDist = Vector3.Distance(transform.position, worldTarget.Value);
                float ratio = directDist > 0.5f ? pathLen / directDist : 1f;
                Debug.Log($"[Move:{gameObject.name}] Path computed: {path.Count} waypoints ratio={ratio:F1}");
            }
        }
        else
        {
            if (GameDebug.Movement)
                Debug.Log($"[Move:{gameObject.name}] Path FAILED, destination unreachable");
            IsDestinationUnreachable = true;
            waypoints = null;
            waypointIndex = 0;
        }
    }

    /// <summary>
    /// Called by PathfindingManager when the NavMesh changes (building placed/destroyed).
    /// Unit replans on its next stagger slot.
    /// </summary>
    public void FlagForReplan()
    {
        needsReplan = true;
    }

    private void CheckReplan()
    {
        if (!worldTarget.HasValue) return;

        if (needsReplan)
        {
            needsReplan = false;
            ComputePath();
            return;
        }

        // Stuck detection: if unit hasn't made progress in StuckThresholdFrames
        if (stuckFrameCount > StuckThresholdFrames && HasPath)
        {
            stuckFrameCount = 0;
            var pfm = PathfindingManager.Instance;
            if (pfm != null && pfm.TryConsumePathRequest())
            {
                if (GameDebug.Movement)
                    Debug.Log($"[Move:{gameObject.name}] Stuck replan (stallTime={stallTime:F1}s)");
                ComputePath();
            }
        }
    }

    // ================================================================
    //  CONTROL METHODS
    // ================================================================

    [Server]
    public void Stop()
    {
        if (GameDebug.Movement)
            Debug.Log($"[Move:{gameObject.name}] STOP at {transform.position:F1}");
        waypoints = null;
        waypointIndex = 0;
        worldTarget = null;
        isStopped = true;
        smoothedVelocity = Vector3.zero;
        isDensityStopped = false;

        if (grid != null)
        {
            Vector2Int cell = grid.WorldToCell(transform.position);
            if (!grid.IsInBounds(cell) || !grid.IsWalkable(cell))
            {
                Vector3 safe = grid.FindNearestWalkablePosition(transform.position, transform.position);
                transform.position = safe;
            }
        }
    }

    [Server]
    public void Resume()
    {
        if (GameDebug.Movement)
            Debug.Log($"[Move:{gameObject.name}] RESUME target={worldTarget?.ToString("F1") ?? "none"}");
        isStopped = false;
        stallTime = 0f;
        stuckFrameCount = 0;
        nearDestTimer = 0f;
        isDensityStopped = false;
        if (worldTarget.HasValue)
            ComputePath();
    }

    private void ArriveAtDestination()
    {
        if (GameDebug.Movement)
            Debug.Log($"[Move:{gameObject.name}] Arrived at {transform.position:F1}");
        waypoints = null;
        waypointIndex = 0;
        worldTarget = null;
        stallTime = 0f;
        stuckFrameCount = 0;
        nearDestTimer = 0f;
        smoothedVelocity = Vector3.zero;
        isDensityStopped = false;
        OnReachedDestination?.Invoke();
    }

    // ================================================================
    //  PUSH / YIELD (Anti-ghosting Layer 2 supplement)
    // ================================================================

    [Server]
    public void RequestYield(Vector3 requesterPosition, Vector3 requesterDirection)
    {
        if (yieldTimer > 0f) return;
        bool isIdle = stateMachine == null || stateMachine.CurrentState == UnitState.Idle;
        if (!isIdle || IsMoving) return;

        Vector3 toMe = transform.position - requesterPosition;
        toMe.y = 0;
        if (toMe.sqrMagnitude < 0.01f) return;

        Vector3 perp = Vector3.Cross(Vector3.up, requesterDirection).normalized;
        float dot = Vector3.Dot(toMe.normalized, perp);
        yieldDirection = dot >= 0 ? perp : -perp;
        yieldTimer = YieldDuration;
    }

    private void UpdateYield()
    {
        if (yieldTimer <= 0f) return;
        yieldTimer -= Time.deltaTime;

        float speed = (unit != null && unit.Data != null) ? unit.Data.moveSpeed : moveSpeed;
        Vector3 move = yieldDirection * speed * 0.6f * Time.deltaTime;
        Vector3 newPos = transform.position + move;
        newPos.y = grid != null ? grid.GridOrigin.y : newPos.y;
        if (grid != null)
            newPos = ValidatePosition(transform.position, newPos);
        transform.position = newPos;
    }

    // ================================================================
    //  STUCK DETECTION
    // ================================================================

    private void TrackProgress()
    {
        float moved = Vector3.Distance(transform.position, lastProgressPos);
        float effectiveRadius = unit != null ? unit.EffectiveRadius : 0.5f;

        float distToGoal = float.MaxValue;
        if (worldTarget.HasValue)
            distToGoal = Vector3.Distance(transform.position, worldTarget.Value);

        bool isStalled = moved < 0.1f * Time.deltaTime * 60f;

        if (isStalled)
        {
            stallTime += Time.deltaTime;
            stuckFrameCount++;

            if (stallTime > 1f)
                TryRequestYieldFromBlockers();

            if (stallTime > 3f && worldTarget.HasValue)
            {
                if (GameDebug.Movement)
                    Debug.Log($"[Move:{gameObject.name}] Stalled {stallTime:F1}s — destination unreachable, idling");
                IsDestinationUnreachable = true;
                waypoints = null;
                waypointIndex = 0;
                worldTarget = null;
                stallTime = 0f;
                stuckFrameCount = 0;
                smoothedVelocity = Vector3.zero;
                return;
            }
        }
        else
        {
            stallTime = Mathf.Max(0f, stallTime - Time.deltaTime * 0.5f);
            stuckFrameCount = 0;
        }

        lastProgressPos = transform.position;

        if (worldTarget.HasValue)
        {
            float nearThreshold = Mathf.Max(1.5f, effectiveRadius * 1.5f);
            if (distToGoal < nearThreshold)
            {
                nearDestTimer += Time.deltaTime;
                if (nearDestTimer > NearDestArriveTime)
                {
                    if (GameDebug.Movement)
                        Debug.Log($"[Move:{gameObject.name}] Near-dest timer fired: dist={distToGoal:F1} radius={effectiveRadius:F1}");
                    ArriveAtDestination();
                    return;
                }
            }
            else
            {
                nearDestTimer = 0f;
            }
        }
        else
        {
            nearDestTimer = 0f;
        }
    }

    private void TryRequestYieldFromBlockers()
    {
        if (UnitManager.Instance == null || !worldTarget.HasValue) return;
        float myRadius = unit != null ? unit.EffectiveRadius : 0.5f;

        Vector3 moveDir = worldTarget.Value - transform.position;
        moveDir.y = 0;
        if (moveDir.sqrMagnitude < 0.01f) return;
        moveDir.Normalize();

        var nearby = UnitManager.Instance.GetUnitsInRadius(transform.position, myRadius * 4f);
        foreach (var other in nearby)
        {
            if (other == null || other == unit || other.IsDead) continue;
            if (other.TeamId != unit.TeamId) continue;

            var otherSm = other.StateMachine;
            if (otherSm != null && otherSm.CurrentState != UnitState.Idle) continue;

            Vector3 toOther = other.transform.position - transform.position;
            toOther.y = 0;
            if (Vector3.Dot(toOther.normalized, moveDir) < 0.3f) continue;

            float dist = toOther.magnitude;
            float combinedRadius = myRadius + other.EffectiveRadius;
            if (dist > combinedRadius * 2f) continue;

            other.Movement?.RequestYield(transform.position, moveDir);
        }
    }

    // ================================================================
    //  WALL COLLISION (terrain boundary — NOT Boids)
    // ================================================================

    private Vector3 ValidatePosition(Vector3 oldPos, Vector3 newPos)
    {
        Vector2Int newCell = grid.WorldToCell(newPos);
        if (grid.IsInBounds(newCell) && grid.IsWalkable(newCell))
            return newPos;

        Vector3 slideX = new Vector3(newPos.x, newPos.y, oldPos.z);
        Vector2Int cellX = grid.WorldToCell(slideX);
        bool xOk = grid.IsInBounds(cellX) && grid.IsWalkable(cellX);

        Vector3 slideZ = new Vector3(oldPos.x, newPos.y, newPos.z);
        Vector2Int cellZ = grid.WorldToCell(slideZ);
        bool zOk = grid.IsInBounds(cellZ) && grid.IsWalkable(cellZ);

        if (xOk && zOk)
            return Mathf.Abs(smoothedVelocity.x) >= Mathf.Abs(smoothedVelocity.z) ? slideX : slideZ;
        if (xOk) return slideX;
        if (zOk) return slideZ;

        return oldPos;
    }

    // ================================================================
    //  UTILITIES
    // ================================================================

    private void ApplyRotation(Vector3 moveDelta)
    {
        Vector3 dir = smoothedVelocity;
        dir.y = 0;
        if (dir.sqrMagnitude < 0.01f)
        {
            dir = moveDelta;
            dir.y = 0;
        }
        if (dir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
    }

    private static Vector3 SmoothDamp(Vector3 current, Vector3 target, float rate)
    {
        float t = 1f - Mathf.Exp(-rate * Time.deltaTime);
        return Vector3.Lerp(current, target, t);
    }

    [ClientRpc]
    private void RpcSyncPositionAndRotation(Vector3 pos, Quaternion rot)
    {
        if (isServer) return;
        serverPosition = pos;
        hasServerPos = true;
        transform.rotation = Quaternion.Slerp(transform.rotation, rot, 15f * Time.deltaTime);
    }
}
